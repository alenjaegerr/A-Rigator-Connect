# Remote connection implementation

## Implemented

- `RobotMqttCommunicationService` implements `IRobotTransport` using MQTTnet.
- `RobotConnectionManager` can select it after local HTTP is unavailable.
- The app uses the topic namespace `arigator/v1/{robotId}/`.
- Commands are published with QoS 1 and a unique command ID.
- The app waits up to four seconds for the matching acknowledgement.
- Broker host, port and robot ID are stored in MAUI SecureStorage.
- The temporary STI EXPO MQTT username/password are hardcoded in the app and firmware source and intentionally match.
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

1. Configure the single demo MQTT account in EMQX with permissions limited to the A-Rigator topic namespace.
2. Supply the broker hostname, robot ID and trusted root CA certificate to the firmware configuration; these are still required independently of the hardcoded demo username/password.
3. Populate the app's broker host, port and robot ID in `IRemoteRobotConfigurationStore` during the existing authenticated robot-registration/setup flow.
4. Implement and publish a documented telemetry JSON envelope from the ESP32. The app currently deserializes telemetry into `RobotStatus`, but the firmware telemetry scheduler/payload is not yet implemented.
5. Compile the MAUI app for Android and firmware, then test with the EMQX deployment and physical robot.

## Safety notes

- A successful MQTT publish is not treated as command success; only a matching `executed` acknowledgement completes the command.
- Expired, rejected, duplicate, and timed-out commands surface as failures to the caller.
- Local HTTP remains preferred and the robot AP remains the local recovery path.
- TEMPORARY STI EXPO ONLY: the demo MQTT username/password are hardcoded in app and firmware source. Remove them and rotate the broker password immediately after the expo.
- OTA remains out of scope until the connection architecture is verified.


## Temporary STI EXPO MQTT credentials

> **TEMPORARY — STI EXPO DEMONSTRATION ONLY.** The demo MQTT username and password are hardcoded in both the ESP32 firmware configuration and the MAUI app configuration so the single prototype can connect without automated broker-user provisioning. Remove these hardcoded values after STI EXPO and replace them with a secure provisioning flow before any production/public deployment. The demo broker account must not be an administrator account and must be restricted to the A-Rigator topic namespace.
