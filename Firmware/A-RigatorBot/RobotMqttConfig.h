#pragma once

#include <Arduino.h>
#include <Preferences.h>

// MQTT credentials and trust material are provisioned per robot and stored
// in this dedicated NVS namespace. Never commit broker passwords or private
// keys to source control. The CA certificate is public trust material, but is
// stored with the device configuration so it can be rotated independently.
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
    config.brokerHost = preferences.getString("host", "");
    config.brokerPort = preferences.getUShort("port", 8883);
    config.robotId = preferences.getString("robotId", "");
    config.username = preferences.getString("username", "");
    config.password = preferences.getString("password", "");
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
      preferences.putString("username", config.username) > 0 &&
      preferences.putString("password", config.password) > 0 &&
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
           verification.username == config.username &&
           verification.password == config.password &&
           verification.rootCaPem == config.rootCaPem;
  }

  bool clear() {
    const bool markerRemoved = !preferences.isKey("configured") || preferences.remove("configured");
    const bool hostRemoved = !preferences.isKey("host") || preferences.remove("host");
    const bool portRemoved = !preferences.isKey("port") || preferences.remove("port");
    const bool idRemoved = !preferences.isKey("robotId") || preferences.remove("robotId");
    const bool usernameRemoved = !preferences.isKey("username") || preferences.remove("username");
    const bool passwordRemoved = !preferences.isKey("password") || preferences.remove("password");
    const bool caRemoved = !preferences.isKey("rootCa") || preferences.remove("rootCa");
    return markerRemoved && hostRemoved && portRemoved && idRemoved &&
           usernameRemoved && passwordRemoved && caRemoved;
  }

  void end() {
    preferences.end();
  }

 private:
  Preferences preferences;
};
