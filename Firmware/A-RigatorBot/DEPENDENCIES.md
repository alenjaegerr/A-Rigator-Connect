# Firmware library dependencies

The A-Rigator firmware uses the ESP32 Arduino core and the following external libraries:

- **PubSubClient** by Nick O'Leary — MQTT client used by `RobotMqttManager.h`.
- **DFRobotDFPlayerMini** — DFPlayer audio module support.

The following libraries are supplied by the ESP32 Arduino core or its bundled components:

- WiFi / WiFiClientSecure
- WebServer
- Preferences
- BLEDevice / BLEServer / BLEUtils / BLESecurity
- ESPmDNS
- Wire

Install PubSubClient and DFRobotDFPlayerMini through the Arduino IDE Library Manager if they are not already installed.

## MQTT security requirements

- Each robot must receive its own MQTT username and password.
- Configure the broker hostname, TLS port, robot ID, credentials, and trusted root CA PEM in the device's `arigator-mqtt` NVS namespace.
- Do not commit credentials, private keys, or device-specific secrets.
- Do not use `setInsecure()`; the MQTT manager requires a trusted root CA certificate.
- Rotate any broker password that has previously been committed to source control.

## Current implementation boundary

The MQTT manager currently establishes the TLS MQTT session, subscribes to the robot command topic, publishes online/offline availability, and exposes methods for telemetry and command acknowledgements. Command JSON parsing and dispatch into the existing robot controllers are the next implementation step.


## OTA module status

`RobotOtaManager.h` contains the initial HTTPS download and inactive-partition installation engine. It uses ESP32 Arduino core facilities (`WiFiClientSecure`, `HTTPClient`, `Update`) and the ESP32 core's bundled mbedTLS SHA-256 implementation.

The OTA engine is intentionally not included in the main sketch's startup or loop yet. It fails closed unless both a release-signature verifier and a robot safety interlock are supplied. The Ed25519 verifier, signed-manifest parser, safe-state integration, OTA partition-table selection, boot validation/rollback confirmation, and app workflow remain to be implemented before enabling updates.

Do not call the installer from the motor-control loop: the download is synchronous and blocking. Integrate it through a dedicated maintenance/update task after the robot is safely stopped.


## Additional OTA files

- `RobotOtaManager.h` — HTTPS download, SHA-256 verification, and inactive-slot write.
- `RobotOtaSignatureVerifier.h` — ECDSA P-256 signature verification.
- `RobotOtaReleaseClient.h` — HTTPS manifest retrieval and schema validation.
- `RobotOtaBootValidation.h` — first-boot confirmation and rollback helper.
- `Firmware/releases/OTA_POLICY.md` — signed payload contract and safety requirements.
- `tools/ota_sign_release.py` — offline release-manifest signing tool (requires Python `cryptography`).

Install **ArduinoJson 7** through Arduino IDE Library Manager for manifest parsing. The ESP32 Arduino core supplies HTTPClient, WiFiClientSecure, Update, ESP OTA APIs, and mbedTLS.

The release signing tool expects a P-256 EC private PEM key outside the repository. Embed only its matching public SubjectPublicKeyInfo PEM in firmware. The example stable manifest stays disabled until a real release is signed and published.
