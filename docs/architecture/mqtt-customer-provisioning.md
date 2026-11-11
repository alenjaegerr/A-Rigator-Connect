# Customer MQTT Provisioning

## Product requirement

A Play Store customer must never be asked to enter an EMQX username, EMQX password, broker hostname, port, or topic. Those are device infrastructure settings.

The customer signs in with the existing account flow, pairs a robot, and configures ordinary Wi-Fi. MQTT credentials are provisioned automatically.

## Current app change

- The manual MQTT credential card is visible only in DEBUG builds.
- Release builds show a customer-facing cloud-connection explanation instead.
- The existing BLE credential characteristic and `SendMqttCredentialsAsync` implementation remain available for developer testing.
- This is a UI boundary change, not a completed automatic provisioning implementation.

## Required production flow

1. The customer authenticates with the existing Supabase account service.
2. The customer discovers and pairs a robot over BLE.
3. The app obtains the robot's stable device identity and proves ownership/claim eligibility.
4. The app requests provisioning from an authenticated backend using the user's Supabase access token.
5. The backend verifies that the user may claim or manage this robot.
6. The backend creates or retrieves a unique EMQX device identity, configures ACLs limited to that robot's topic namespace, and returns the device credentials only to the authorized provisioning session.
7. The app forwards the credentials to the robot over the existing BLE MQTT configuration characteristic.
8. The ESP32 stores the credentials and, once MQTT transport is implemented, connects to EMQX over TLS.
9. The app reports a customer-friendly result such as "Cloud connection configured" or a clear retry/error state.

## Backend security requirements

- Never ship EMQX Management API credentials, a fleet-wide MQTT username/password, or broker administration secrets in the mobile app or firmware.
- Never authorize a robot using its display name or a guessable Robot ID alone.
- Require an authenticated account and an ownership/claim check. A printed QR code should contain a one-time claim secret or equivalent proof, not the broker password.
- Generate a unique random credential per robot. Apply broker ACLs so a robot can subscribe only to its own command topic and publish only to its own acknowledgement, telemetry, and availability topics.
- Keep the user's Supabase JWT and device credentials out of logs and analytics.
- Return device credentials only over HTTPS to the authorized pairing session; keep them in app memory only long enough to provision over BLE.
- Support credential revocation and rotation when a robot is unpaired, transferred, or reported compromised.
- BLE Just Works encryption is not strong owner authentication. Production claiming must include physical-presence verification and an appropriate authenticated provisioning design.
- Evaluate ESP32 Secure Boot and Flash Encryption. NVS storage alone is not tamper-resistant.

## API contract to implement

Suggested authenticated endpoint:

`POST /v1/robots/{robotId}/mqtt-provisioning`

Request:
- Supabase bearer access token
- Robot claim proof (one-time claim token or equivalent)

Response (HTTPS only, pairing session only):
- MQTT host and TLS port (or use app-bundled non-secret broker endpoint configuration)
- Unique device username and password
- Robot ID and credential version

The backend must be idempotent for a provisioning retry and must not create unlimited credentials on repeated requests. A later credential rotation endpoint should revoke the previous credential after the new one is confirmed on-device.

The exact endpoint and response schema must be aligned with the deployed backend before app implementation. The current `IBackendConfigService` stores only a base URL; no automatic MQTT provisioning endpoint is implemented yet.

## Remaining implementation work

1. Confirm the production backend host and deploy the authenticated provisioning endpoint.
2. Add an app provisioning client that uses the existing Supabase session and backend base URL.
3. Add a stable robot ID and one-time ownership claim mechanism to firmware/manufacturing records.
4. Replace the DEBUG-only manual credential form with the automatic request-and-BLE flow.
5. Implement and test ESP32 MQTT TLS connection, ACL behavior, reconnect, telemetry, acknowledgements, and local safety leases.
6. Run end-to-end tests for first claim, retry, already-claimed robot, revoked ownership, credential rotation, and offline recovery.
