#pragma once

#include <Arduino.h>
#include <Preferences.h>

// Stores only A-Rigator's Wi-Fi credentials in its own NVS namespace.
// Other robot settings (irrigation profiles, calibration, etc.) are untouched.
struct RobotWifiCredentials {
  String ssid;
  String password;

  bool isValid() const {
    return ssid.length() > 0;
  }
};

class RobotWifiCredentialStore {
 public:
  bool begin() {
    return preferences.begin("arigator-wifi", false);
  }

  bool load(RobotWifiCredentials& credentials) {
    if (!preferences.getBool("configured", false)) {
      credentials.ssid = "";
      credentials.password = "";
      return false;
    }

    credentials.ssid = preferences.getString("ssid", "");
    credentials.password = preferences.getString("password", "");
    return credentials.isValid();
  }

  bool save(const String& ssid, const String& password) {
    if (ssid.length() == 0 || password.length() > 63) {
      return false;
    }

    // Mark the profile unavailable until both values have been written.
    // A power loss midway through saving cannot make a partial profile active.
    preferences.remove("configured");
    if (preferences.putString("ssid", ssid) == 0 ||
        preferences.putString("password", password) == 0 ||
        preferences.putBool("configured", true) == 0) {
      preferences.remove("configured");
      return false;
    }

    RobotWifiCredentials verification;
    return load(verification) &&
           verification.ssid == ssid &&
           verification.password == password;
  }

  // Forget Wi-Fi only. Do not clear the NVS partition or other namespaces.
  bool clear() {
    // Invalidate first, then remove the values. A reset interrupted by power
    // loss will never cause stale credentials to be treated as configured.
    const bool markerRemoved = !preferences.isKey("configured") || preferences.remove("configured");
    const bool ssidRemoved = !preferences.isKey("ssid") || preferences.remove("ssid");
    const bool passwordRemoved = !preferences.isKey("password") || preferences.remove("password");
    return markerRemoved && ssidRemoved && passwordRemoved;
  }

  void end() {
    preferences.end();
  }

 private:
  Preferences preferences;
};


// Stores the local AP password independently from the router Wi-Fi profile.
// The initial value preserves compatibility with existing A-Rigator setups.
// Users can change it later through the encrypted BLE AP-settings characteristic.
class RobotApConfigStore {
 public:
  static constexpr const char* DEFAULT_PASSWORD = "12345678";

  bool begin() {
    return preferences.begin("arigator-ap", false);
  }

  String load() {
    String password = preferences.getString("password", DEFAULT_PASSWORD);
    if (password.length() < 8 || password.length() > 63) {
      password = DEFAULT_PASSWORD;
      preferences.putString("password", password);
    }
    return password;
  }

  bool save(const String& password) {
    if (password.length() < 8 || password.length() > 63) {
      return false;
    }

    if (preferences.putString("password", password) == 0) {
      return false;
    }

    return preferences.getString("password", "") == password;
  }

  void end() {
    preferences.end();
  }

 private:
  Preferences preferences;
};
