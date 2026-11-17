#pragma once

#include <Arduino.h>
#include <esp_ota_ops.h>
#include <esp_system.h>

// First-boot validation for ESP-IDF rollback-enabled OTA partitions.
//
// Call begin() early during setup. Run the robot's non-motion startup checks,
// then call confirmHealthyBoot() only after all required checks pass.
// If the candidate image fails its checks, call rejectAndRollback().
// This does not replace a hardware watchdog or a full functional test.
class RobotOtaBootValidation {
public:
  enum class Result : uint8_t {
    NotPending,
    PendingValidation,
    Confirmed,
    RolledBack,
    Error
  };

  Result begin() {
    const esp_partition_t* running = esp_ota_get_running_partition();
    if (running == nullptr) return Result::Error;

    esp_ota_img_states_t imageState;
    const esp_err_t result = esp_ota_get_state_partition(running, &imageState);
    if (result == ESP_ERR_NOT_SUPPORTED || result == ESP_ERR_NOT_FOUND) {
      _pending = false;
      return Result::NotPending;
    }
    if (result != ESP_OK) return Result::Error;

    _pending = imageState == ESP_OTA_IMG_PENDING_VERIFY;
    return _pending ? Result::PendingValidation : Result::NotPending;
  }

  bool isPendingValidation() const { return _pending; }

  bool confirmHealthyBoot() {
    if (!_pending) return false;
    if (esp_ota_mark_app_valid_cancel_rollback() != ESP_OK) return false;
    _pending = false;
    return true;
  }

  void rejectAndRollback() {
    if (!_pending) return;
    esp_ota_mark_app_invalid_rollback_and_reboot();
    // The function normally reboots. Do not continue normal application work.
    while (true) delay(1000);
  }

private:
  bool _pending = false;
};
