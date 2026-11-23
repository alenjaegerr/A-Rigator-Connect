#pragma once

#include <Arduino.h>
#include <Preferences.h>

// ============================================================================
// TEMPORARY STI EXPO DEMONSTRATION CREDENTIALS
// Remove this hardcoded block after STI EXPO and replace it with a secure
// device-credential provisioning flow before any public/production release.
// These credentials are intentionally shared with the demo app configuration.
// They are not broker administrator credentials; restrict their broker ACL
// to this A-Rigator robot's topic namespace only.
// ============================================================================
static const char* ARIGATOR_DEMO_MQTT_USERNAME = "a-rigator-demo";
static const char* ARIGATOR_DEMO_MQTT_PASSWORD = "12345678";

// Broker endpoint, robot ID and public TLS trust certificate are stored in
// this dedicated NVS namespace. MQTT username/password use the temporary
// STI EXPO demonstration constants above and must be removed after the expo.
struct RobotMqttConfig {
  String brokerHost;
  uint16_t brokerPort = 8883;
  String robotId;
  String username;
  String password;
  String rootCaPem;

  bool isValid() const {
    return brokerHost.length() > 0 &&
           brokerPort > 0 &&
           robotId.length() > 0 &&
           username.length() > 0 &&
           password.length() > 0 &&
           rootCaPem.startsWith("-----BEGIN CERTIFICATE-----") &&
           rootCaPem.indexOf("-----END CERTIFICATE-----") >= 0;
  }
};

class RobotMqttConfigStore {
 public:
  bool begin() {
    return preferences.begin("arigator-mqtt", false);
  }

  bool load(RobotMqttConfig& config) {
    if (!preferences.getBool("configured", false)) return false;

    config.brokerHost = preferences.getString("host", "");
    config.brokerPort = preferences.getUShort("port", 8883);
    config.robotId = preferences.getString("robotId", "");
    // TEMPORARY — STI EXPO DEMONSTRATION ONLY.
    // Remove these hardcoded credentials after STI EXPO and restore secure
    // per-device provisioning. Do not use this fallback in production.
    config.username = ARIGATOR_DEMO_MQTT_USERNAME;
    config.password = ARIGATOR_DEMO_MQTT_PASSWORD;
    config.rootCaPem = preferences.getString("rootCa", "");
    return config.isValid();
  }

  bool save(const RobotMqttConfig& config) {
    if (!config.isValid()) return false;

    // Invalidate first so a power loss during a multi-key write cannot expose
    // a partially written configuration as a usable broker profile.
    preferences.remove("configured");

    const bool written =
      preferences.putString("host", config.brokerHost) > 0 &&
      preferences.putUShort("port", config.brokerPort) > 0 &&
      preferences.putString("robotId", config.robotId) > 0 &&
      preferences.putString("rootCa", config.rootCaPem) > 0 &&
      preferences.putBool("configured", true) > 0;

    if (!written) {
      preferences.remove("configured");
      return false;
    }

    RobotMqttConfig verification;
    return load(verification) &&
           verification.brokerHost == config.brokerHost &&
           verification.brokerPort == config.brokerPort &&
           verification.robotId == config.robotId &&
           verification.rootCaPem == config.rootCaPem;
  }

  bool clear() {
    const bool markerRemoved = !preferences.isKey("configured") || preferences.remove("configured");
    const bool hostRemoved = !preferences.isKey("host") || preferences.remove("host");
    const bool portRemoved = !preferences.isKey("port") || preferences.remove("port");
    const bool idRemoved = !preferences.isKey("robotId") || preferences.remove("robotId");
    const bool caRemoved = !preferences.isKey("rootCa") || preferences.remove("rootCa");
    return markerRemoved && hostRemoved && portRemoved && idRemoved &&
           caRemoved;
  }

  void end() {
    preferences.end();
  }

 private:
  Preferences preferences;
};
