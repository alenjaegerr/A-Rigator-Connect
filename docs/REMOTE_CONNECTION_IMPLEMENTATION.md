# Remote connection implementation

## Implemented

- `RobotMqttCommunicationService` implements `IRobotTransport` using MQTTnet.
- `RobotConnectionManager` can select it after local HTTP is unavailable.
- The app uses the topic namespace `arigator/v1/{robotId}/`.
- Commands are published with QoS 1 and a unique command ID.
- The app waits up to four seconds for the matching acknowledgement.
- Broker host, port, robot ID, username and password are stored in MAUI SecureStorage.
- TLS certificate validation remains enabled; there is no insecure TLS bypass.

## Remote command allowlist

Only the firmware commands currently implemented by the ESP32 dispatcher are enabled:

- `forward`
- `reverse`
- `stop`
- `pump_on`
- `pump_off`
- `heartbeat`

All other `RobotControlCommand` values are explicitly rejected by the remote transport until the corresponding firmware dispatcher and safety checks are implemented.

## Still required before a live remote test

1. Extend the authenticated Supabase Edge Function to provision a unique EMQX credential and return the broker host, port, robot ID, username and password to the signed-in app. The current function only supports robot registration.
2. After provisioning, save the returned connection details through `IRemoteRobotConfigurationStore.SaveAsync`.
3. Implement the matching ESP32 BLE/NVS provisioning write so the same per-robot credentials and trusted CA certificate reach the robot securely. Do not send broker credentials through an unencrypted HTTP endpoint.
4. Implement and publish a documented telemetry JSON envelope from the ESP32. The app currently deserializes telemetry into `RobotStatus`, but the firmware telemetry scheduler/payload is not yet implemented.
5. Add reconnect/backoff and an explicit UI indication for local versus remote transport.
6. Compile the MAUI app for Android and verify MQTTnet API compatibility, then test with a provisioned EMQX deployment and the physical robot.

## Safety notes

- A successful MQTT publish is not treated as command success; only a matching `executed` acknowledgement completes the command.
- Expired, rejected, duplicate, and timed-out commands surface as failures to the caller.
- Local HTTP remains preferred and the robot AP remains the local recovery path.
- Remote credentials are not hard-coded in source or committed to the repository.
- OTA remains out of scope until the connection architecture is verified.


## Temporary STI EXPO MQTT credentials

> **TEMPORARY — STI EXPO DEMONSTRATION ONLY.** The demo MQTT username and password are hardcoded in both the ESP32 firmware configuration and the MAUI app configuration so the single prototype can connect without automated broker-user provisioning. Remove these hardcoded values after STI EXPO and replace them with a secure provisioning flow before any production/public deployment. The demo broker account must not be an administrator account and must be restricted to the A-Rigator topic namespace.
