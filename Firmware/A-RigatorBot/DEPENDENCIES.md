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
