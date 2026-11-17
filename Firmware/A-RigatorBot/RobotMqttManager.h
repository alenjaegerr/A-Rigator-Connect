#pragma once

#include <Arduino.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <PubSubClient.h>
#include "RobotMqttConfig.h"

// Requires the Arduino PubSubClient library by Nick O'Leary.
// TLS certificate verification is mandatory; no insecure fallback is provided.
class RobotMqttManager {
 public:
  bool begin(const RobotMqttConfig& config) {
    if (!config.isValid()) return false;
    config_ = config;
    tlsClient_.setCACert(config_.rootCaPem.c_str());
    mqtt_.setClient(tlsClient_);
    mqtt_.setServer(config_.brokerHost.c_str(), config_.brokerPort);
    mqtt_.setBufferSize(1024);
    mqtt_.setKeepAlive(30);
    mqtt_.setSocketTimeout(5);
    mqtt_.setCallback([this](char* topic, uint8_t* payload, unsigned int length) {
      this->onMessage(topic, payload, length);
    });
    buildTopics();
    configured_ = true;
    return true;
  }

  void loop() {
    if (!configured_ || WiFi.status() != WL_CONNECTED) return;

    if (!mqtt_.connected()) {
      const unsigned long now = millis();
      if ((uint32_t)(now - lastConnectAttempt_) < RECONNECT_INTERVAL_MS) return;
      lastConnectAttempt_ = now;
      connect();
      return;
    }

    mqtt_.loop();
  }

  bool connected() const {
    return mqtt_.connected();
  }

  bool publishTelemetry(const String& json) {
    return mqtt_.connected() &&
           mqtt_.publish(telemetryTopic_.c_str(), json.c_str(), false);
  }

  bool publishAvailability(bool online) {
    if (!mqtt_.connected()) return false;
    const char* payload = online ? "{\"status\":\"online\"}" : "{\"status\":\"offline\"}";
    return mqtt_.publish(availabilityTopic_.c_str(), payload, true);
  }

  bool publishAck(const String& json) {
    return mqtt_.connected() &&
           mqtt_.publish(ackTopic_.c_str(), json.c_str(), false);
  }

  bool takeCommand(String& payload) {
    if (!commandPending_) return false;
    payload = pendingCommand_;
    pendingCommand_ = "";
    commandPending_ = false;
    return true;
  }

  const String& robotId() const {
    return config_.robotId;
  }

 private:
  static const unsigned long RECONNECT_INTERVAL_MS = 5000;

  RobotMqttConfig config_;
  WiFiClientSecure tlsClient_;
  PubSubClient mqtt_{tlsClient_};
  String commandTopic_;
  String ackTopic_;
  String telemetryTopic_;
  String availabilityTopic_;
  String pendingCommand_;
  bool configured_ = false;
  bool commandPending_ = false;
  unsigned long lastConnectAttempt_ = 0;

  void buildTopics() {
    const String prefix = String("arigator/v1/") + config_.robotId + "/";
    commandTopic_ = prefix + "command";
    ackTopic_ = prefix + "ack";
    telemetryTopic_ = prefix + "telemetry";
    availabilityTopic_ = prefix + "availability";
  }

  void connect() {
    String clientId = String("arigator-") + config_.robotId;
    const bool ok = mqtt_.connect(
      clientId.c_str(),
      config_.username.c_str(),
      config_.password.c_str(),
      availabilityTopic_.c_str(),
      1,
      true,
      "{\"status\":\"offline\"}"
    );

    if (!ok) return;

    mqtt_.subscribe(commandTopic_.c_str(), 1);
    publishAvailability(true);
  }

  void onMessage(char* topic, uint8_t* payload, unsigned int length) {
    if (commandTopic_ != topic || length == 0 || length > 768) return;

    // Keep callbacks short. The main loop consumes the copied payload and
    // performs command parsing/validation outside the MQTT callback context.
    String message;
    message.reserve(length);
    for (unsigned int i = 0; i < length; ++i) {
      if (payload[i] < 0x20 && payload[i] != '\\n' && payload[i] != '\\r' && payload[i] != '\\t') return;
      message += (char)payload[i];
    }

    pendingCommand_ = message;
    commandPending_ = true;
  }
};
