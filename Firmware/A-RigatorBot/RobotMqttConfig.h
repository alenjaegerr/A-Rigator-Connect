#pragma once

#include <Arduino.h>

// ============================================================================
// STI EXPO DEMONSTRATION MQTT CONFIGURATION
// Fixed single-robot configuration. Replace shared demo credentials with
// per-device provisioning before any public/production release.
// ============================================================================
static const char* ARIGATOR_DEMO_MQTT_USERNAME = "a-rigator-demo";
static const char* ARIGATOR_DEMO_MQTT_PASSWORD = "12345678";
static const char* ARIGATOR_MQTT_BROKER_HOST = "c1d96f86.ala.asia-southeast1.emqxsl.com";
static const uint16_t ARIGATOR_MQTT_BROKER_PORT = 8883;

// TLS trust is provided by the Arduino-ESP32 built-in Mozilla root CA bundle.
// Keep this endpoint configuration independent from any single CA certificate.

struct RobotMqttConfig {
  String brokerHost;
  uint16_t brokerPort = 8883;
  String username;
  String password;

  bool isValid() const {
    return brokerHost.length() > 0 &&
           brokerPort == 8883 &&
           username.length() > 0 &&
           password.length() > 0;
  }
};

inline RobotMqttConfig makeRobotMqttConfig() {
  RobotMqttConfig config;
  config.brokerHost = ARIGATOR_MQTT_BROKER_HOST;
  config.brokerPort = ARIGATOR_MQTT_BROKER_PORT;
  config.username = ARIGATOR_DEMO_MQTT_USERNAME;
  config.password = ARIGATOR_DEMO_MQTT_PASSWORD;
  return config;
}
