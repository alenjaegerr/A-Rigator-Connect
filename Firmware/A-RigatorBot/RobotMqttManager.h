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
  static const uint8_t COMMAND_QUEUE_CAPACITY = 4;
  static const unsigned int MAX_COMMAND_BYTES = 768;

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

  void loop(bool tlsClockReady) {
    if (!configured_ || WiFi.status() != WL_CONNECTED) return;

    // EMQX Serverless TLS certificate validation requires a valid system clock.
    // Do not attempt the TLS handshake until NTP has synchronized the ESP32.
    if (!tlsClockReady) {
      if (!waitingForClockLogged_) {
        Serial.println(">> [MQTT] Waiting for NTP time synchronization before TLS connection.");
        waitingForClockLogged_ = true;
      }
      return;
    }
    waitingForClockLogged_ = false;

    if (!mqtt_.connected()) {
      const unsigned long now = millis();
      if ((uint32_t)(now - lastConnectAttempt_) < RECONNECT_INTERVAL_MS) return;
      lastConnectAttempt_ = now;
      connect();
      return;
    }

    mqtt_.loop();
  }

  bool connected() {
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

  // Called from the main loop, never from PubSubClient's callback.
  bool takeCommand(String& payload) {
    if (queuedCommandCount_ == 0) return false;

    payload = commandQueue_[commandQueueHead_];
    commandQueue_[commandQueueHead_] = "";
    commandQueueHead_ = (commandQueueHead_ + 1) % COMMAND_QUEUE_CAPACITY;
    --queuedCommandCount_;
    return true;
  }

  uint8_t queuedCommandCount() const {
    return queuedCommandCount_;
  }

  uint32_t droppedCommandCount() const {
    return droppedCommandCount_;
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
  String commandQueue_[COMMAND_QUEUE_CAPACITY];
  uint8_t commandQueueHead_ = 0;
  uint8_t commandQueueTail_ = 0;
  uint8_t queuedCommandCount_ = 0;
  uint32_t droppedCommandCount_ = 0;
  bool configured_ = false;
  unsigned long lastConnectAttempt_ = 0;
  bool waitingForClockLogged_ = false;

  void buildTopics() {
    const String prefix = "arigator/v1/";
    commandTopic_ = prefix + "command";
    ackTopic_ = prefix + "ack";
    telemetryTopic_ = prefix + "telemetry";
    availabilityTopic_ = prefix + "availability";
  }

  void connect() {
    const String clientId = "arigator-esp32";
    Serial.printf(">> [MQTT] Connecting to %s:%u ...\\n",
                  config_.brokerHost.c_str(), config_.brokerPort);
    const bool ok = mqtt_.connect(
      clientId.c_str(),
      config_.username.c_str(),
      config_.password.c_str(),
      availabilityTopic_.c_str(),
      1,
      true,
      "{\"status\":\"offline\"}"
    );

    if (!ok) {
      Serial.printf(">> [MQTT] Connection failed. PubSubClient state: %d\\n", mqtt_.state());
      return;
    }

    const bool subscribed = mqtt_.subscribe(commandTopic_.c_str(), 1);
    Serial.printf(">> [MQTT] Connected. Command subscription: %s\\n",
                  subscribed ? "OK" : "FAILED");
    publishAvailability(true);
  }

  void onMessage(char* topic, uint8_t* payload, unsigned int length) {
    if (commandTopic_ != topic || length == 0 || length > MAX_COMMAND_BYTES) {
      ++droppedCommandCount_;
      return;
    }

    // Never overwrite an earlier command. Queue overflow is observable and
    // will be handled by the command protocol's publisher-side retry/expiry.
    if (queuedCommandCount_ >= COMMAND_QUEUE_CAPACITY) {
      ++droppedCommandCount_;
      return;
    }

    String message;
    message.reserve(length);
    for (unsigned int i = 0; i < length; ++i) {
      if (payload[i] < 0x20 && payload[i] != '\\n' &&
          payload[i] != '\\r' && payload[i] != '\\t') {
        ++droppedCommandCount_;
        return;
      }
      message += (char)payload[i];
    }

    commandQueue_[commandQueueTail_] = message;
    commandQueueTail_ = (commandQueueTail_ + 1) % COMMAND_QUEUE_CAPACITY;
    ++queuedCommandCount_;
  }
};
