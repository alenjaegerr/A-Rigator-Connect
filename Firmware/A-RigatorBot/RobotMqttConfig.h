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

// CA certificate supplied from this A-Rigator EMQX Serverless deployment.
static const char ARIGATOR_EMQX_ROOT_CA[] = R"EMQX_CA(
-----BEGIN CERTIFICATE-----
MIIDjjCCAnagAwIBAgIQAzrx5qcRqaC7KGSxHQn65TANBgkqhkiG9w0BAQsFADBh
MQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaWNlcnQgSW5jMRkwFwYDVQQLExB3
d3cuZGlnaWNlcnQuY29tMSAwHgYDVQQDExdEaWdpQ2VydCBHbG9iYWwgUm9vdCBH
MjAeFw0xMzA4MDExMjAwMDBaFw0zODAxMTUxMjAwMDBaMGExCzAJBgNVBAYTAlVT
MRUwEwYDVQQKEwxEaWdpY2VydCBJbmMxGTAXBgNVBAsTEHd3dy5kaWdpY2VydC5j
b20xIDAeBgNVBAMTF0RpZ2lDZXJ0IEdsb2JhbCBSb290IEcyMIIBIjANBgkqhkiG
9w0BAQEFAAOCAQ8AMIIBCgKCAQEAuzfNNNx7a8myaJCtSnX/RrohCgiN9RlUyfuI
2/Ou8jqJkTx65qsGGmvPrC3oXgkkRLpimn7Wo6h+4FR1IAWsULecYxpsMNzaHxmx
1x7e/dfgy5SDN67sH0NO3Xss0r0upS/kqbitOtSZpLYl6ZtrAGCSYP9PIUkY92eQ
q2EGnI/yuum06ZIya7XzV+hdG82MHauVBJVJ8zUtluNJbd134/tJS7SsVQepj5Wz
tCO7TG1F8PapspUwtP1MVYwnSlcUfIKdzXOS0xZKBgyMUNGPHgm+F6HmIcr9g+UQ
vIOlCsRnKPZzFBQ9RnbDhxSJITRNrw9FDKZJobq7nMWxM4MphQIDAQABo0IwQDAP
BgNVHRMBAf8EBTADAQH/MA4GA1UdDwEB/wQEAwIBhjAdBgNVHQ4EFgQUTiJUIBiV
5uNu5g/6+rkS7QYXjzkwDQYJKoZIhvcNAQELBQADggEBAGBnKJRvDkhj6zHd6mcY
1Yl9PMWLSn/pvtsrF9+wX3N3KjITOYFnQoQj8kVnNeyIv/iPsGEMNKSuIEyExtv4
NeF22d+mQrvHRAiGfzZ0JFrabA0UWTW98kndth/Jsw1HKj2ZL7tcu7XUIOGZX1NG
Fdtom/DzMNU+MeKNhJ7jitralj41E6Vf8PlwUHBHQRFXGU7Aj64GxJUTFy8bJZ91
8rGOmaFvE7FBcf6IKshPECBV1/MUReXgRPTqh5Uykw7+U0b6LJ3/iyK5S9kJRaTe
pLiaWN0bfVKfjllDiIGknibVb63dDcY3fe0Dkhvld1927jyNxF1WW6LZZm6zNTfl
MrY=
-----END CERTIFICATE-----
)EMQX_CA";

struct RobotMqttConfig {
  String brokerHost;
  uint16_t brokerPort = 8883;
  String username;
  String password;
  String rootCaPem;

  bool isValid() const {
    return brokerHost.length() > 0 &&
           brokerPort == 8883 &&
           username.length() > 0 &&
           password.length() > 0 &&
           rootCaPem.startsWith("-----BEGIN CERTIFICATE-----") &&
           rootCaPem.indexOf("-----END CERTIFICATE-----") >= 0;
  }
};

inline RobotMqttConfig makeRobotMqttConfig() {
  RobotMqttConfig config;
  config.brokerHost = ARIGATOR_MQTT_BROKER_HOST;
  config.brokerPort = ARIGATOR_MQTT_BROKER_PORT;
  config.username = ARIGATOR_DEMO_MQTT_USERNAME;
  config.password = ARIGATOR_DEMO_MQTT_PASSWORD;
  config.rootCaPem = ARIGATOR_EMQX_ROOT_CA;
  return config;
}
