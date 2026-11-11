# QR ownership and MQTT provisioning

## Customer flow
1. Customer signs in to A-Rigator Connect.
2. Customer scans the QR label. It contains a robot code and a high-entropy, one-time claim code, never MQTT credentials.
3. App posts `{ "action": "claim", "robotCode": "...", "claimCode": "..." }` with the signed-in Supabase access token.
4. Edge Function hashes the claim code and calls the service-role-only `_claim_arigator_robot` RPC. Ownership is assigned atomically and the token is consumed.
5. App posts `{ "action": "provision", "robotId": "..." }`. Function verifies ownership, provisions a unique EMQX username/password and scoped ACL rules, then returns credentials over HTTPS.
6. App sends credentials to the robot over BLE. Do not log or persist the MQTT password in the app.

## Supabase Edge Function secrets
Configure in Dashboard → Edge Functions → Secrets:
- `EMQX_API_BASE_URL`: HTTPS EMQX management API base URL (not the MQTT listener hostname unless EMQX documents it as the management API).
- `EMQX_API_KEY`, `EMQX_API_SECRET`: EMQX API key pair with authentication and authorization management permissions.
- `MQTT_BROKER_HOST`: broker TLS hostname.
- `MQTT_BROKER_TLS_PORT`: normally `8883`.
- `MQTT_CREDENTIAL_ENCRYPTION_KEY`: 32 random bytes as 64 hex characters. Keep a secure backup; rotation requires re-encrypting stored credentials.
Supabase's built-in `SUPABASE_URL`, `SUPABASE_ANON_KEY`, and `SUPABASE_SERVICE_ROLE_KEY` are used server-side. Never expose service-role, EMQX API, or encryption keys to the app, firmware, or Git.

## API
Endpoint: `POST https://pcgbyowcqpgtkfbdfwfb.supabase.co/functions/v1/robot-mqtt-provisioning`
Headers: `Authorization: Bearer <Supabase user access token>`, `Content-Type: application/json`.

Claim:
```json
{ "action": "claim", "robotCode": "AR-...", "claimCode": "<one-time random secret>" }
```
Provision:
```json
{ "action": "provision", "robotId": "<UUID returned by claim>" }
```
Provision response includes host, port, TLS flag, username, password, and topic root. Treat it as a secret response.

## Manufacturing / QR registration
For each unit, generate a claim code with at least 128 bits of cryptographic randomness. Print the raw code only in the QR label and store only SHA-256(code) in `private.robot_claim_tokens`. Create the corresponding `public.robots` row with unique `robot_code`, null `owner_id`, and a name. Set a finite expiry. QR payload should be versioned and carry `robotCode` and `claimCode`.

## Current implementation boundaries
- Edge Function deployed with JWT verification enabled; claim RPC is service-role-only.
- Claim token and credential tables deny access to anon/authenticated; MQTT password is AES-GCM encrypted at rest.
- EMQX management API URL/keys and encryption key still need configuration in Supabase.
- EMQX deployment must have password-based built-in authentication and built-in database authorization enabled.
- Mobile QR scanner/claim screen and automatic BLE handoff are not yet connected to this endpoint.
- ESP32 MQTT transport is not implemented; local HTTP control remains the fallback.
