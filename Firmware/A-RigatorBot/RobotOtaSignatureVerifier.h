#pragma once

#include <Arduino.h>
#include <string.h>
#include <mbedtls/pk.h>
#include <mbedtls/md.h>
#include <mbedtls/base64.h>

// ECDSA P-256 / SHA-256 verifier for A-Rigator OTA release metadata.
//
// Signed canonical payload (UTF-8, exact bytes, no trailing newline):
// ARIGATOR-RELEASE-V1\n
// product=A-Rigator\n
// version=<version>\n
// firmwareUrl=<https URL>\n
// sha256=<64 lowercase hex characters>\n
// firmwareSize=<decimal byte count>
//
// The signature is Base64-encoded ASN.1 DER ECDSA signature over SHA-256
// of the canonical payload. The public key is an X.509 SubjectPublicKeyInfo
// PEM. The private signing key must remain offline and must never be committed.
class RobotOtaSignatureVerifier {
public:
  explicit RobotOtaSignatureVerifier(const char* publicKeyPem)
    : _publicKeyPem(publicKeyPem) {}

  bool isConfigured() const {
    return _publicKeyPem != nullptr && _publicKeyPem[0] != '\0';
  }

  static bool callback(
    void* context,
    const String& version,
    const String& firmwareUrl,
    const String& sha256Hex,
    size_t firmwareSize,
    const String& signatureBase64
  ) {
    if (context == nullptr) return false;
    return static_cast<RobotOtaSignatureVerifier*>(context)->verify(
      version, firmwareUrl, sha256Hex, firmwareSize, signatureBase64);
  }

  bool verify(
    const String& version,
    const String& firmwareUrl,
    const String& sha256Hex,
    size_t firmwareSize,
    const String& signatureBase64
  ) const {
    if (!isConfigured() || version.isEmpty() ||
        !firmwareUrl.startsWith("https://") ||
        sha256Hex.length() != 64 || firmwareSize == 0 ||
        signatureBase64.isEmpty()) return false;

    String normalizedDigest = sha256Hex;
    normalizedDigest.toLowerCase();

    String payload = "ARIGATOR-RELEASE-V1\n";
    payload += "product=A-Rigator\n";
    payload += "version=" + version + "\n";
    payload += "firmwareUrl=" + firmwareUrl + "\n";
    payload += "sha256=" + normalizedDigest + "\n";
    payload += "firmwareSize=" + String((unsigned long)firmwareSize);

    uint8_t digest[32];
    const mbedtls_md_info_t* mdInfo = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
    if (mdInfo == nullptr ||
        mbedtls_md(mdInfo,
          reinterpret_cast<const unsigned char*>(payload.c_str()),
          payload.length(), digest) != 0) {
      return false;
    }

    size_t signatureLength = 0;
    const size_t maximumSignatureLength = (signatureBase64.length() * 3U) / 4U + 4U;
    if (maximumSignatureLength == 0 || maximumSignatureLength > 256) return false;

    uint8_t signature[256];
    const int decodeResult = mbedtls_base64_decode(
      signature, sizeof(signature), &signatureLength,
      reinterpret_cast<const unsigned char*>(signatureBase64.c_str()),
      signatureBase64.length());
    if (decodeResult != 0 || signatureLength == 0) return false;

    mbedtls_pk_context key;
    mbedtls_pk_init(&key);
    const int parseResult = mbedtls_pk_parse_public_key(
      &key,
      reinterpret_cast<const unsigned char*>(_publicKeyPem),
      strlen(_publicKeyPem) + 1);
    if (parseResult != 0) {
      mbedtls_pk_free(&key);
      return false;
    }

    bool valid = false;
    if (mbedtls_pk_can_do(&key, MBEDTLS_PK_ECKEY)) {
      valid = mbedtls_pk_verify(
        &key, MBEDTLS_MD_SHA256, digest, sizeof(digest),
        signature, signatureLength) == 0;
    }

    mbedtls_pk_free(&key);
    memset(signature, 0, sizeof(signature));
    return valid;
  }

private:
  const char* _publicKeyPem;
};
