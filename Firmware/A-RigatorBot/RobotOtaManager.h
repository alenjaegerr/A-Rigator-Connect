#pragma once

#include <Arduino.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <Update.h>
#include <mbedtls/md.h>

// Secure OTA download/install engine.
//
// IMPORTANT:
// - This class is deliberately not called by setup()/loop() yet.
// - The caller must provide a release-signature verifier. With no verifier,
//   every update is rejected (fail closed).
// - The caller must also provide a safety interlock proving the robot is
//   stationary, pump-off, and otherwise safe to update.
// - Use a trusted root CA. Never use setInsecure().
// - Configure an OTA-capable partition table before enabling this module.
class RobotOtaManager {
public:
  using SignatureVerifier = bool (*)(
    const String& version,
    const String& firmwareUrl,
    const String& sha256Hex,
    const String& signatureBase64
  );

  using SafetyInterlock = bool (*)();

  enum class State : uint8_t {
    Idle,
    Downloading,
    Verifying,
    Installing,
    Completed,
    Failed
  };

  struct Release {
    String version;
    String firmwareUrl;
    String sha256Hex;
    String signatureBase64;
    size_t firmwareSize = 0;
  };

  RobotOtaManager() = default;

  void setSignatureVerifier(SignatureVerifier verifier) {
    _signatureVerifier = verifier;
  }

  void setSafetyInterlock(SafetyInterlock interlock) {
    _safetyInterlock = interlock;
  }

  State state() const { return _state; }
  uint8_t progressPercent() const { return _progressPercent; }
  const String& lastError() const { return _lastError; }

  // This operation is synchronous and blocking. Call it only from a
  // dedicated update task or controlled maintenance flow, never from a
  // latency-sensitive motor-control loop.
  bool install(
    const Release& release,
    const char* trustedRootCaPem,
    bool allowDowngrade = false
  ) {
    resetStatus();

    if (_signatureVerifier == nullptr) {
      return fail("Release signature verifier is not configured.");
    }
    if (_safetyInterlock == nullptr || !_safetyInterlock()) {
      return fail("Robot safety interlock did not approve OTA.");
    }
    if (!WiFi.isConnected()) {
      return fail("Wi-Fi station connection is required for OTA.");
    }
    if (trustedRootCaPem == nullptr || trustedRootCaPem[0] == '\0') {
      return fail("Trusted HTTPS root CA is missing.");
    }
    if (!isValidRelease(release)) {
      return fail("Release metadata is incomplete or invalid.");
    }
    if (!_signatureVerifier(
          release.version,
          release.firmwareUrl,
          release.sha256Hex,
          release.signatureBase64)) {
      return fail("Release signature verification failed.");
    }

    if (!allowDowngrade && isSameOrOlderVersion(release.version, ARIGATOR_FIRMWARE_VERSION)) {
      return fail("Release is not newer than the installed firmware.");
    }

    WiFiClientSecure client;
    client.setCACert(trustedRootCaPem);

    HTTPClient http;
    http.setTimeout(15000);
    http.setFollowRedirects(HTTPC_DISABLE_FOLLOW_REDIRECTS);

    if (!http.begin(client, release.firmwareUrl)) {
      return fail("Could not initialize HTTPS firmware request.");
    }

    _state = State::Downloading;
    const int responseCode = http.GET();
    if (responseCode != HTTP_CODE_OK) {
      http.end();
      return fail(String("Firmware server returned HTTP ") + responseCode + ".");
    }

    const int contentLength = http.getSize();
    if (contentLength <= 0 || (size_t)contentLength != release.firmwareSize) {
      http.end();
      return fail("Firmware content length does not match the signed release metadata.");
    }

    if (!Update.begin(release.firmwareSize, U_FLASH)) {
      http.end();
      return fail(String("Could not begin OTA partition write: ") + Update.errorString());
    }

    mbedtls_md_context_t digestContext;
    mbedtls_md_init(&digestContext);
    const mbedtls_md_info_t* digestInfo =
      mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);

    if (digestInfo == nullptr ||
        mbedtls_md_setup(&digestContext, digestInfo, 0) != 0 ||
        mbedtls_md_starts(&digestContext) != 0) {
      mbedtls_md_free(&digestContext);
      Update.abort();
      http.end();
      return fail("Could not initialize SHA-256 verification.");
    }

    WiFiClient* stream = http.getStreamPtr();
    uint8_t buffer[1024];
    size_t received = 0;
    const unsigned long lastProgressAtStart = millis();
    unsigned long lastDataAt = lastProgressAtStart;
    bool writeFailed = false;

    while (http.connected() && received < release.firmwareSize) {
      if (_safetyInterlock == nullptr || !_safetyInterlock()) {
        writeFailed = true;
        _lastError = "Robot safety interlock changed during OTA.";
        break;
      }

      const size_t availableBytes = stream->available();
      if (availableBytes == 0) {
        if (millis() - lastDataAt > 20000UL) {
          writeFailed = true;
          _lastError = "Firmware download stalled.";
          break;
        }
        delay(10);
        continue;
      }

      const size_t remaining = release.firmwareSize - received;
      const size_t toRead = min((size_t)sizeof(buffer), min(availableBytes, remaining));
      const int bytesRead = stream->readBytes(buffer, toRead);
      if (bytesRead <= 0) continue;

      lastDataAt = millis();
      if (mbedtls_md_update(&digestContext, buffer, (size_t)bytesRead) != 0) {
        writeFailed = true;
        _lastError = "SHA-256 update failed.";
        break;
      }

      const size_t written = Update.write(buffer, (size_t)bytesRead);
      if (written != (size_t)bytesRead) {
        writeFailed = true;
        _lastError = String("Flash write failed: ") + Update.errorString();
        break;
      }

      received += (size_t)bytesRead;
      _progressPercent = (uint8_t)((received * 100ULL) / release.firmwareSize);
    }

    http.end();

    if (writeFailed || received != release.firmwareSize) {
      mbedtls_md_free(&digestContext);
      Update.abort();
      if (_lastError.isEmpty()) _lastError = "Firmware download was incomplete.";
      _state = State::Failed;
      return false;
    }

    _state = State::Verifying;
    uint8_t digest[32];
    const int digestResult = mbedtls_md_finish(&digestContext, digest);
    mbedtls_md_free(&digestContext);

    if (digestResult != 0 || !constantTimeDigestMatch(digest, release.sha256Hex)) {
      Update.abort();
      return fail("Firmware SHA-256 digest mismatch.");
    }

    if (_safetyInterlock == nullptr || !_safetyInterlock()) {
      Update.abort();
      return fail("Robot is no longer in a safe state to finalize OTA.");
    }

    _state = State::Installing;
    if (!Update.end(true) || !Update.isFinished()) {
      return fail(String("OTA finalization failed: ") + Update.errorString());
    }

    _state = State::Completed;
    _progressPercent = 100;
    delay(250);
    ESP.restart();
    return true; // Normally not reached; restart is requested above.
  }

private:
  SignatureVerifier _signatureVerifier = nullptr;
  SafetyInterlock _safetyInterlock = nullptr;
  State _state = State::Idle;
  uint8_t _progressPercent = 0;
  String _lastError;

  void resetStatus() {
    _state = State::Idle;
    _progressPercent = 0;
    _lastError = "";
  }

  bool fail(const String& message) {
    _lastError = message;
    _state = State::Failed;
    return false;
  }

  static bool isValidRelease(const Release& release) {
    if (release.version.isEmpty() || release.firmwareUrl.isEmpty() ||
        release.sha256Hex.length() != 64 || release.signatureBase64.isEmpty() ||
        release.firmwareSize == 0) return false;

    if (!release.firmwareUrl.startsWith("https://")) return false;

    for (size_t i = 0; i < release.sha256Hex.length(); ++i) {
      if (!isHexadecimalDigit(release.sha256Hex[i])) return false;
    }
    return true;
  }

  static bool constantTimeDigestMatch(const uint8_t digest[32], const String& expectedHex) {
    uint8_t mismatch = 0;
    for (size_t i = 0; i < 32; ++i) {
      const char high = toLowerCase(expectedHex[i * 2]);
      const char low = toLowerCase(expectedHex[i * 2 + 1]);
      const uint8_t expected = (uint8_t)((hexValue(high) << 4) | hexValue(low));
      mismatch |= digest[i] ^ expected;
    }
    return mismatch == 0;
  }

  static uint8_t hexValue(char ch) {
    if (ch >= '0' && ch <= '9') return (uint8_t)(ch - '0');
    if (ch >= 'a' && ch <= 'f') return (uint8_t)(ch - 'a' + 10);
    return 0;
  }

  // Semantic comparison for numeric dotted versions (for example 0.2.0).
  // A release parser should validate version syntax before passing metadata.
  static bool isSameOrOlderVersion(const String& candidate, const char* installed) {
    int candidateParts[3] = {0, 0, 0};
    int installedParts[3] = {0, 0, 0};
    if (!parseVersion(candidate, candidateParts) ||
        !parseVersion(String(installed), installedParts)) return true;

    for (int i = 0; i < 3; ++i) {
      if (candidateParts[i] < installedParts[i]) return true;
      if (candidateParts[i] > installedParts[i]) return false;
    }
    return true;
  }

  static bool parseVersion(const String& value, int parts[3]) {
    int partIndex = 0;
    int start = 0;
    for (int i = 0; i <= (int)value.length(); ++i) {
      if (i == (int)value.length() || value[i] == '.') {
        if (partIndex >= 3 || i == start) return false;
        const String token = value.substring(start, i);
        for (size_t j = 0; j < token.length(); ++j) {
          if (!isDigit(token[j])) return false;
        }
        parts[partIndex++] = token.toInt();
        start = i + 1;
      }
    }
    return partIndex == 3;
  }
};
