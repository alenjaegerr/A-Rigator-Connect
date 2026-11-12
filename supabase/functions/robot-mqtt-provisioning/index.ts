import { createClient } from "npm:@supabase/supabase-js@2";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", "cache-control": "no-store" },
  });

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

    if (action === "register") {
      const hardwareId = String(body.hardwareId ?? "").replace(/[^A-Fa-f0-9]/g, "").toUpperCase();
      const robotName = String(body.robotName ?? "A-Rigator").trim().slice(0, 48);
      if (!/^[A-F0-9]{12}$/.test(hardwareId)) {
        return json({ error: "invalid_robot_identifier" }, 400);
      }

      const { data, error } = await admin.rpc("_register_arigator_robot", {
        p_hardware_id: hardwareId,
        p_user_id: user.id,
        p_robot_name: robotName || "A-Rigator",
      });

      if (error) {
        if (error.message.includes("robot_already_registered")) {
          return json({ error: "robot_already_registered" }, 409);
        }
        if (error.message.includes("invalid_registration")) {
          return json({ error: "invalid_robot_identifier" }, 400);
        }
        console.error("robot registration failed", error.message);
        return json({ error: "registration_failed" }, 500);
      }

      if (!data?.length) return json({ error: "registration_failed" }, 500);
      return json({
        success: true,
        robotId: data[0].robot_id,
        robotName: data[0].robot_name,
        alreadyRegistered: data[0].already_registered,
      });
    }

    return json({ error: "unsupported_action" }, 400);
  } catch (error) {
    console.error("robot-mqtt-provisioning error", error instanceof Error ? error.message : "unknown");
    return json({ error: "provisioning_failed", detail: error instanceof Error ? error.message : "unknown" }, 500);
  }
});
