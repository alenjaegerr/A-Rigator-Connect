# A-Rigator MQTT Multi-Device Architecture

## Device identity

Every manufactured robot receives a permanent, unique Robot ID during factory provisioning (for example, `AR-000001`). The ID is an identifier, not a password. Do not use a user-editable robot display name as the MQTT identity.

The ESP32 eFuse MAC can be used as a manufacturing uniqueness input or diagnostic identifier, but it is not an authentication secret. Production provisioning should assign a registry-backed Robot ID and generate unique credentials for each unit.

## Broker

- Host: `c1d96f86.ala.asia-southeast1.emqxsl.com`
- Native MQTT over TLS: `8883`
- TLS must validate the broker certificate chain using the supplied trusted CA. Never disable certificate validation.
- Do not commit broker passwords, device secrets, or app tokens.

## Topic namespace

Use one namespace per robot:

| Topic | Direction | Purpose |
|---|---|---|
| `arigator/v1/{robotId}/command` | App/backend → robot | Requested action |
| `arigator/v1/{robotId}/ack` | Robot → app/backend | Command result |
| `arigator/v1/{robotId}/telemetry` | Robot → app/backend | Periodic robot status and sensor readings |
| `arigator/v1/{robotId}/availability` | Robot → app/backend | Online/offline presence |

The robot subscribes only to its own `command` topic and publishes only to its own `ack`, `telemetry`, and `availability` topics. Configure broker ACLs to enforce this; topic naming alone is not access control.

## Authentication and ownership

- Each robot has its own broker identity and unique secret. Never share one firmware-wide username/password across a production fleet.
- A-Rigator Connect must not contain a permanent fleet-wide broker credential.
- The signed-in user's backend session should authorize access to a specific robot, then obtain short-lived, robot-scoped MQTT credentials (or use a trusted backend MQTT bridge).
- The backend maintains the user-to-robot ownership/permission mapping. A user must not gain access merely by guessing a Robot ID.
- Revocation must disable a robot credential and remove the relevant account-to-robot association.

## Factory provisioning

1. Assign and record the Robot ID in the device registry.
2. Generate a unique credential pair (or device certificate) for that unit.
3. Configure broker ACLs for that exact Robot ID.
4. Provision the ID and secret into the device securely; do not bake fleet credentials into the shared firmware image.
5. Verify TLS, authentication, topic permissions, and reconnect behavior before packaging.
6. Record credential rotation and revocation procedures.

For production hardware, evaluate ESP32 secure boot, flash encryption, encrypted NVS, and a protected manufacturing provisioning process. Plain NVS storage alone should not be treated as tamper-resistant secret storage.

## Command safety

MQTT is a transport, not the safety controller. Firmware must continue enforcing local motor/pump timeouts, autonomous-control leases, obstacle stops, and safe-stop behavior even if the broker or network becomes unavailable. Commands should include a command ID and be acknowledged; stale or duplicate commands should not be replayed as new actions.

## Current implementation boundary

The current firmware already provisions router Wi-Fi credentials over BLE and exposes a local HTTP API. MQTT should be added alongside that API first. Keep HTTP available for local setup/recovery until MQTT control and telemetry are validated end-to-end. Broker credentials and app-side scoped authorization are prerequisites for enabling remote control in production.
