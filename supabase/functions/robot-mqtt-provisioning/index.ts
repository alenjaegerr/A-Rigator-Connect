import { createClient } from "npm:@supabase/supabase-js@2";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", "cache-control": "no-store" },
  });

const hex = (bytes: Uint8Array) =>
  Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");

const fromHex = (value: string) =>
  new Uint8Array(value.match(/.{1,2}/g)?.map((x) => parseInt(x, 16)) ?? []);

async function sha256(value: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return hex(new Uint8Array(digest));
}

function base64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

async function encryptionKey(): Promise<CryptoKey> {
  const raw = Deno.env.get("MQTT_CREDENTIAL_ENCRYPTION_KEY");
  if (!raw) throw new Error("MQTT_CREDENTIAL_ENCRYPTION_KEY is not configured");
  const bytes = fromHex(raw);
  if (bytes.length !== 32) throw new Error("MQTT_CREDENTIAL_ENCRYPTION_KEY must be 64 hex characters");
  return await crypto.subtle.importKey("raw", bytes, "AES-GCM", false, ["encrypt", "decrypt"]);
}

async function encryptSecret(secret: string) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const encrypted = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv },
    await encryptionKey(),
    new TextEncoder().encode(secret),
  );
  return { ciphertext: base64(new Uint8Array(encrypted)), nonce: base64(iv) };
}

async function decryptSecret(ciphertext: string, nonce: string) {
  const plain = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: Uint8Array.from(atob(nonce), (c) => c.charCodeAt(0)) },
    await encryptionKey(),
    Uint8Array.from(atob(ciphertext), (c) => c.charCodeAt(0)),
  );
  return new TextDecoder().decode(plain);
}

async function emqxRequest(path: string, method: string, body?: unknown) {
  const base = Deno.env.get("EMQX_API_BASE_URL")?.replace(/\/+$/, "");
  const key = Deno.env.get("EMQX_API_KEY");
  const secret = Deno.env.get("EMQX_API_SECRET");
  if (!base || !key || !secret) throw new Error("EMQX management API secrets are not configured");
  const response = await fetch(`${base}${path}`, {
    method,
    headers: {
      authorization: `Basic ${btoa(`${key}:${secret}`)}`,
      accept: "application/json",
      "content-type": "application/json",
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    const detail = await response.text();
    throw new Error(`EMQX API failed (${response.status}): ${detail.slice(0, 300)}`);
  }
  return response.status === 204 ? null : await response.json().catch(() => null);
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST") return json({ error: "method_not_allowed" }, 405);

  try {
    const authHeader = req.headers.get("authorization");
    if (!authHeader?.startsWith("Bearer ")) return json({ error: "unauthorized" }, 401);

    const projectUrl = Deno.env.get("SUPABASE_URL")!;
    const anonKey = Deno.env.get("SUPABASE_ANON_KEY")!;
    const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
    const userClient = createClient(projectUrl, anonKey, {
      global: { headers: { Authorization: authHeader } },
      auth: { persistSession: false, autoRefreshToken: false },
    });
    const token = authHeader.slice("Bearer ".length);
    const { data: { user }, error: authError } = await userClient.auth.getUser(token);
    if (authError || !user) return json({ error: "unauthorized" }, 401);

    const admin = createClient(projectUrl, serviceKey, {
      auth: { persistSession: false, autoRefreshToken: false },
    });
    const body = await req.json();
    const action = body?.action;

    if (action === "claim") {
      const robotCode = String(body.robotCode ?? "").trim();
      const claimCode = String(body.claimCode ?? "").trim();
      if (!/^[A-Za-z0-9_-]{4,64}$/.test(robotCode) || claimCode.length < 20 || claimCode.length > 256) {
        return json({ error: "invalid_claim" }, 400);
      }
      const tokenHash = await sha256(claimCode);
      const { data, error } = await admin.rpc("_claim_arigator_robot", {
        p_robot_code: robotCode,
        p_claim_token_hash: tokenHash,
        p_user_id: user.id,
      });
      if (error || !data?.length) return json({ error: "claim_invalid_or_already_used" }, 409);
      return json({ success: true, robotId: data[0].robot_id, robotName: data[0].robot_name });
    }

    if (action === "provision") {
      const robotId = String(body.robotId ?? "");
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(robotId)) {
        return json({ error: "invalid_robot_id" }, 400);
      }
      const { data: robot, error: robotError } = await admin
        .from("robots").select("id,owner_id,robot_code").eq("id", robotId).maybeSingle();
      if (robotError || !robot || robot.owner_id !== user.id) return json({ error: "robot_not_owned" }, 403);

      const { data: existing } = await admin
        .from("robot_mqtt_credentials")
        .select("mqtt_username,password_ciphertext,password_nonce")
        .eq("robot_id", robotId).maybeSingle();

      let username: string;
      let password: string;
      if (existing) {
        username = existing.mqtt_username;
        password = await decryptSecret(existing.password_ciphertext, existing.password_nonce);
      } else {
        username = `ar_${robotId.replaceAll("-", "")}`;
        password = base64(crypto.getRandomValues(new Uint8Array(36)));
        await emqxRequest("/api/v5/authentication/password_based:built_in_database/users", "POST", {
          user_id: username, password, is_superuser: false,
        });
        const topicRoot = `arigator/v1/${robotId}`;
        await emqxRequest(
          "/api/v5/authorization/sources/built_in_database/rules/users",
          "POST",
          [{
            username,
            rules: [
              { action: "publish", permission: "allow", topic: `${topicRoot}/command`, qos: [0, 1], retain: false },
              { action: "subscribe", permission: "allow", topic: `${topicRoot}/ack`, qos: [0, 1], retain: false },
              { action: "subscribe", permission: "allow", topic: `${topicRoot}/telemetry`, qos: [0, 1], retain: false },
              { action: "subscribe", permission: "allow", topic: `${topicRoot}/availability`, qos: [0, 1], retain: false },
              { action: "all", permission: "deny", topic: "arigator/v1/#" },
            ],
          }],
        );
        const encrypted = await encryptSecret(password);
        const { error: saveError } = await admin.from("robot_mqtt_credentials").upsert({
          robot_id: robotId,
          mqtt_username: username,
          password_ciphertext: encrypted.ciphertext,
          password_nonce: encrypted.nonce,
          key_version: 1,
          provisioned_at: new Date().toISOString(),
          rotated_at: new Date().toISOString(),
        });
        if (saveError) throw saveError;
        await admin.from("robots").update({ mqtt_provisioned_at: new Date().toISOString() }).eq("id", robotId);
      }

      return json({
        success: true,
        robotId,
        host: Deno.env.get("MQTT_BROKER_HOST"),
        port: Number(Deno.env.get("MQTT_BROKER_TLS_PORT") ?? "8883"),
        tls: true,
        username,
        password,
        topicRoot: `arigator/v1/${robotId}`,
      });
    }

    return json({ error: "unsupported_action" }, 400);
  } catch (error) {
    console.error("robot-mqtt-provisioning error", error instanceof Error ? error.message : "unknown");
    return json({ error: "provisioning_failed", detail: error instanceof Error ? error.message : "unknown" }, 500);
  }
});
