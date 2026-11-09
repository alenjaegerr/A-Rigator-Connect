#pragma once

#include <Arduino.h>
#include <Preferences.h>

// Stores only A-Rigator's Wi-Fi credentials in its own NVS namespace.
// Other robot settings (irrigation profiles, calibration, etc.) are untouched.
struct RobotWifiCredentials {
  String ssid;
  String password;

  bool isValid() const {
    return ssid.length() > 0 && password.length() > 0;
  }
};

class RobotWifiCredentialStore {
 public:
  bool begin() {
    return preferences.begin("arigator-wifi", false);
  }

  bool load(RobotWifiCredentials& credentials) {
    credentials.ssid = preferences.getString("ssid", "");
    credentials.password = preferences.getString("password", "");
    return credentials.isValid();
  }

  bool save(const String& ssid, const String& password) {
    if (ssid.isEmpty() || password.length() < 8) {
      return false;
    }

    if (preferences.putString("ssid", ssid) == 0 ||
        preferences.putString("password", password) == 0) {
      return false;
    }

    RobotWifiCredentials verification;
    return load(verification) &&
           verification.ssid == ssid &&
           verification.password == password;
  }

  // Forget Wi-Fi only. Do not clear the NVS partition or other namespaces.
  bool clear() {
    const bool ssidRemoved = !preferences.isKey("ssid") || preferences.remove("ssid");
    const bool passwordRemoved = !preferences.isKey("password") || preferences.remove("password");
    return ssidRemoved && passwordRemoved;
  }

  void end() {
    preferences.end();
  }

 private:
  Preferences preferences;
};
