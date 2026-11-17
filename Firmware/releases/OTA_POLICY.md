# A-Rigator firmware release and OTA policy

## Release manifest

The stable release manifest is published at `Firmware/releases/stable.json`. It is a discovery document, not a firmware image. The initial manifest deliberately sets `releaseAvailable` to `false` and leaves the version, binary URL, digest, and signature empty. Do not present a firmware update as available until a tested release has been published with all required metadata.

## Update transport

- The app checks the release manifest over HTTPS.
- The ESP32 downloads the firmware binary directly over HTTPS. The app does not relay the binary over Bluetooth.
- BLE is reserved for pairing, provisioning, and optionally requesting the robot to check for an update.
- MQTT may notify the robot that a release is available, but it must not carry the firmware binary.
- Local robot operation must remain safe if internet access or the cloud broker is unavailable.

## Authenticity and integrity

Before installation, the ESP32 must:
1. Validate the HTTPS server certificate.
2. Verify the binary SHA-256 digest against the manifest.
3. Verify an ECDSA P-256 / SHA-256 signature over the canonical release payload using a public key embedded in firmware.
4. Reject missing, malformed, expired (if expiry is added), or incompatible release metadata.

SHA-256 detects accidental corruption; it does not prove who published the firmware. The digital signature is required for authenticity. The firmware verifier expects a Base64-encoded ASN.1 DER ECDSA signature over SHA-256 of the canonical payload documented in `RobotOtaSignatureVerifier.h`. The signing private key must remain offline and must never be stored in the repository or on the robot.

## Installation and recovery

- Configure an ESP32 partition table with two OTA application slots and an OTA data partition. Confirm the actual flash size and partition layout before enabling OTA.
- Write the candidate image to the inactive OTA slot.
- Reboot into the candidate image and run startup/self-tests.
- Mark the candidate image valid only after checks pass; otherwise allow the ESP32 OTA bootloader rollback mechanism to restore the previous image.
- Preserve a USB/serial recovery path.
- Do not start an update while the robot is moving, irrigating, below the configured safe battery threshold, or in an unsafe state.
- A failed or interrupted download must leave the currently bootable image intact.

## Proof of successful update

The firmware must report its compiled version from the local status API and MQTT telemetry after reboot. The app should show success only when:
- the robot reconnects after the update,
- the reported running version equals the target release version, and
- the firmware has passed its startup validation.

A download completion or flash-write completion alone is not a successful update.

## Current implementation status

The repository now contains an initial HTTPS OTA installer (`RobotOtaManager.h`) and an ECDSA signature verifier (`RobotOtaSignatureVerifier.h`). The installer requires both a signature verifier and a robot safety interlock and is not yet wired into the main sketch. The release manifest remains unavailable until a real key pair is generated, the public key is embedded in firmware, the release-signing process is implemented, OTA partitions and rollback validation are configured, and a tested firmware artifact exists.


## Canonical signed payload

The verifier signs these exact UTF-8 lines, with no trailing newline:

```text
ARIGATOR-RELEASE-V1
product=A-Rigator
version=<version>
firmwareUrl=<https URL>
sha256=<64 lowercase hex characters>
firmwareSize=<decimal byte count>
```

The manifest's `firmware.size`, `firmware.url`, `firmware.sha256`, and `firmware.signature` values must be used to construct the signed payload. The signature is Base64-encoded ASN.1 DER ECDSA. Keep the firmware URL byte-for-byte identical between signing and verification.
