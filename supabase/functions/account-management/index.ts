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

    const projectUrl = Deno.env.get("SUPABASE_URL");
    const anonKey = Deno.env.get("SUPABASE_ANON_KEY");
    const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
    if (!projectUrl || !anonKey || !serviceKey) {
      console.error("account-management missing required Supabase environment variables");
      return json({ error: "server_not_configured" }, 500);
    }

    const userClient = createClient(projectUrl, anonKey, {
      global: { headers: { Authorization: authHeader } },
      auth: { persistSession: false, autoRefreshToken: false },
    });
    const token = authHeader.slice("Bearer ".length);
    const { data: { user }, error: authError } = await userClient.auth.getUser(token);
    if (authError || !user) return json({ error: "unauthorized" }, 401);

    const body = await req.json().catch(() => null);
    if (body?.action !== "delete_account") return json({ error: "unsupported_action" }, 400);

    const admin = createClient(projectUrl, serviceKey, {
      auth: { persistSession: false, autoRefreshToken: false },
    });

    // Delete account-owned data before removing the Auth identity.
    const { error: profilesError } = await admin
      .from("irrigation_profiles")
      .delete()
      .eq("owner_id", user.id);
    if (profilesError) {
      console.error("account deletion: irrigation profile cleanup failed", profilesError.message);
      return json({ error: "account_data_cleanup_failed" }, 500);
    }

    const { error: robotsError } = await admin
      .from("robots")
      .delete()
      .eq("owner_id", user.id);
    if (robotsError) {
      console.error("account deletion: robot cleanup failed", robotsError.message);
      return json({ error: "account_data_cleanup_failed" }, 500);
    }

    const { error: profileError } = await admin
      .from("profiles")
      .delete()
      .eq("id", user.id);
    if (profileError) {
      console.error("account deletion: profile cleanup failed", profileError.message);
      return json({ error: "account_data_cleanup_failed" }, 500);
    }

    const { error: deleteError } = await admin.auth.admin.deleteUser(user.id);
    if (deleteError) {
      console.error("account deletion: Auth deletion failed", deleteError.message);
      return json({ error: "account_deletion_failed" }, 500);
    }

    return json({ success: true });
  } catch (error) {
    console.error("account-management error", error instanceof Error ? error.message : "unknown");
    return json({ error: "account_deletion_failed" }, 500);
  }
});
