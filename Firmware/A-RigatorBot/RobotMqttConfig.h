#pragma once

#include <Arduino.h>
#include <Preferences.h>

// MQTT credentials are provisioned over the encrypted BLE configuration
// characteristic. This store is separate from Wi-Fi and AP settings.
struct RobotMqttCredentials {
  String username;
  String password;

  bool isValid() const {
    return username.length() > 0 && password.length() > 0;
  }
};

class RobotMqttCredentialStore {
 public:
  bool begin() {
    return preferences.begin("arigator-mqtt", false);
  }

  bool isConfigured() {
    return preferences.getBool("configured", false) &&
           preferences.getString("username", "").length() > 0 &&
           preferences.getString("password", "").length() > 0;
  }

  bool load(RobotMqttCredentials& credentials) {
    if (!preferences.getBool("configured", false)) {
      credentials.username = "";
      credentials.password = "";
      return false;
    }

    credentials.username = preferences.getString("username", "");
    credentials.password = preferences.getString("password", "");
    return credentials.isValid();
  }

  bool save(const String& username, const String& password) {
    if (username.length() == 0 || username.length() > 64 ||
        password.length() == 0 || password.length() > 128) {
      return false;
    }

    preferences.remove("configured");
    if (preferences.putString("username", username) == 0 ||
        preferences.putString("password", password) == 0 ||
        preferences.putBool("configured", true) == 0) {
      preferences.remove("configured");
      return false;
    }

    RobotMqttCredentials verification;
    return load(verification) &&
           verification.username == username &&
           verification.password == password;
  }

  bool clear() {
    const bool markerRemoved = !preferences.isKey("configured") || preferences.remove("configured");
    const bool usernameRemoved = !preferences.isKey("username") || preferences.remove("username");
    const bool passwordRemoved = !preferences.isKey("password") || preferences.remove("password");
    return markerRemoved && usernameRemoved && passwordRemoved;
  }

  void end() {
    preferences.end();
  }

 private:
  Preferences preferences;
};
