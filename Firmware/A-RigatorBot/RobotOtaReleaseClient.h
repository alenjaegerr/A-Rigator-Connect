#pragma once

#include <Arduino.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include "RobotOtaManager.h"

// Retrieves and parses the signed stable-release manifest.
// The signature is not trusted here; RobotOtaManager verifies it immediately
// before opening the firmware download.
class RobotOtaReleaseClient {
public:
  bool fetchStableRelease(
    const String& manifestUrl,
    const char* trustedRootCaPem,
    RobotOtaManager::Release& release,
    String& error
  ) const {
    error = "";

    if (!manifestUrl.startsWith("https://")) {
      error = "Manifest URL must use HTTPS.";
      return false;
    }
    if (trustedRootCaPem == nullptr || trustedRootCaPem[0] == '\0') {
      error = "Trusted HTTPS root CA is missing.";
      return false;
    }
    if (!WiFi.isConnected()) {
      error = "Wi-Fi station connection is required.";
      return false;
    }

    WiFiClientSecure client;
    client.setCACert(trustedRootCaPem);

    HTTPClient http;
    http.setTimeout(12000);
    http.setFollowRedirects(HTTPC_DISABLE_FOLLOW_REDIRECTS);
    if (!http.begin(client, manifestUrl)) {
      error = "Could not initialize HTTPS manifest request.";
      return false;
    }

    const int status = http.GET();
    if (status != HTTP_CODE_OK) {
      http.end();
      error = String("Manifest server returned HTTP ") + status + ".";
      return false;
    }

    const int contentLength = http.getSize();
    if (contentLength <= 0 || contentLength > 8192) {
      http.end();
      error = "Manifest size is invalid or exceeds 8 KB.";
      return false;
    }

    String body = http.getString();
    http.end();
    if (body.length() == 0 || body.length() > 8192) {
      error = "Manifest body is empty or too large.";
      return false;
    }

    JsonDocument document;
    const DeserializationError parseError = deserializeJson(document, body);
    if (parseError) {
      error = String("Manifest JSON parse failed: ") + parseError.c_str();
      return false;
    }

    if (document["schemaVersion"] != 1 ||
        document["product"] != "A-Rigator" ||
        document["channel"] != "stable" ||
        document["releaseAvailable"] != true) {
      error = "No stable A-Rigator release is currently available.";
      return false;
    }

    JsonObjectConst firmware = document["firmware"].as<JsonObjectConst>();
    const char* version = document["version"] | nullptr;
    const char* url = firmware["url"] | nullptr;
    const char* digest = firmware["sha256"] | nullptr;
    const char* signature = firmware["signature"] | nullptr;
    const char* algorithm = firmware["signatureAlgorithm"] | nullptr;
    const char* encoding = firmware["signatureEncoding"] | nullptr;
    const size_t size = firmware["size"] | (size_t)0;

    if (version == nullptr || url == nullptr || digest == nullptr ||
        signature == nullptr || algorithm == nullptr || encoding == nullptr ||
        size == 0) {
      error = "Manifest is missing required firmware metadata.";
      return false;
    }

    if (strcmp(algorithm, "ECDSA-P256-SHA256") != 0 ||
        strcmp(encoding, "base64-der") != 0) {
      error = "Manifest signature algorithm or encoding is unsupported.";
      return false;
    }

    RobotOtaManager::Release candidate;
    candidate.version = version;
    candidate.firmwareUrl = url;
    candidate.sha256Hex = digest;
    candidate.signatureBase64 = signature;
    candidate.firmwareSize = size;

    if (candidate.version.isEmpty() ||
        !candidate.firmwareUrl.startsWith("https://") ||
        candidate.sha256Hex.length() != 64 ||
        candidate.signatureBase64.isEmpty() ||
        candidate.firmwareSize == 0) {
      error = "Manifest firmware metadata is invalid.";
      return false;
    }

    release = candidate;
    return true;
  }
};
