#pragma once

#include <Arduino.h>
#include <Preferences.h>

enum RobotOperatingMode : uint8_t {
  ROBOT_MODE_LOCAL = 0,
  ROBOT_MODE_REMOTE = 1
};

enum RobotConnectionState : uint8_t {
  CONN_LOCAL_ACTIVE,
  CONN_MODE_SWITCHING,
  CONN_WIFI_CONNECTING,
  CONN_TIME_SYNC,
  CONN_MQTT_CONNECTING,
  CONN_REMOTE_ACTIVE,
  CONN_REMOTE_RECOVERY
};

class RobotConnectionModeStore {
 public:
  bool begin() {
    if (!preferences_.begin("arigator_conn", false)) return false;
    ready_ = true;
    mode_ = preferences_.getUChar("op_mode", ROBOT_MODE_LOCAL) == ROBOT_MODE_REMOTE
      ? ROBOT_MODE_REMOTE : ROBOT_MODE_LOCAL;
    return true;
  }

  bool save(RobotOperatingMode mode) {
    if (!ready_ && !begin()) return false;
    if (preferences_.putUChar("op_mode", (uint8_t)mode) != sizeof(uint8_t)) return false;
    mode_ = mode;
    return true;
  }

  RobotOperatingMode mode() const { return mode_; }

 private:
  Preferences preferences_;
  RobotOperatingMode mode_ = ROBOT_MODE_LOCAL;
  bool ready_ = false;
};
