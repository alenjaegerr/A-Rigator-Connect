#pragma once

#include <Arduino.h>

// Proof-of-concept shared MQTT account.
// Replace these placeholders with the test account created in EMQX Cloud.
// These values are intentionally compile-time constants; MQTT credentials
// are not provisioned through the mobile app or BLE.
static const char* MQTT_BROKER_HOST = "c1d96f86.ala.asia-southeast1.emqxsl.com";
static const uint16_t MQTT_BROKER_TLS_PORT = 8883;
static const char* MQTT_USERNAME = "REPLACE_WITH_SHARED_TEST_USERNAME";
static const char* MQTT_PASSWORD = "REPLACE_WITH_SHARED_TEST_PASSWORD";
