#!/usr/bin/env python3
"""Create a signed A-Rigator OTA manifest.

The private PEM key must be generated and stored outside the repository.
Requires: python -m pip install cryptography

Example:
  python tools/ota_sign_release.py \
    --firmware build/A-RigatorBot.bin \
    --version 0.2.0 \
    --firmware-url https://releases.example.com/arigator/0.2.0/A-RigatorBot.bin \
    --private-key /secure/offline/arigator-release-private.pem \
    --output stable.json \
    --notes "OTA support and stability improvements"

The firmware URL must point to an already-published immutable binary. Upload
the binary first, then run this tool to produce the signed manifest. Never
commit or upload the private key.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec


def canonical_payload(version: str, firmware_url: str, sha256_hex: str, size: int) -> bytes:
    return (
        "ARIGATOR-RELEASE-V1\n"
        "product=A-Rigator\n"
        f"version={version}\n"
        f"firmwareUrl={firmware_url}\n"
        f"sha256={sha256_hex.lower()}\n"
        f"firmwareSize={size}"
    ).encode("utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--firmware", required=True, type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--firmware-url", required=True)
    parser.add_argument("--private-key", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--hardware-revision", default="unspecified")
    parser.add_argument("--notes", action="append", default=[])
    args = parser.parse_args()

    if not args.firmware.is_file():
        parser.error("Firmware binary does not exist.")
    if not args.private_key.is_file():
        parser.error("Private signing key does not exist.")
    if not args.firmware_url.startswith("https://"):
        parser.error("--firmware-url must use HTTPS.")

    try:
        parts = args.version.split(".")
        if len(parts) != 3 or any(not part.isdigit() for part in parts):
            raise ValueError
    except ValueError:
        parser.error("--version must use numeric MAJOR.MINOR.PATCH format.")

    private_key = serialization.load_pem_private_key(
        args.private_key.read_bytes(), password=None
    )
    if not isinstance(private_key, ec.EllipticCurvePrivateKey):
        parser.error("Signing key must be an EC private key.")
    if not isinstance(private_key.curve, ec.SECP256R1):
        parser.error("Signing key must use the P-256 curve.")

    firmware = args.firmware.read_bytes()
    digest = hashlib.sha256(firmware).hexdigest()
    payload = canonical_payload(args.version, args.firmware_url, digest, len(firmware))
    signature = private_key.sign(payload, ec.ECDSA(hashes.SHA256()))

    manifest = {
        "schemaVersion": 1,
        "product": "A-Rigator",
        "channel": "stable",
        "releaseAvailable": True,
        "version": args.version,
        "minimumFirmwareVersion": None,
        "hardwareRevision": args.hardware_revision,
        "publishedAt": datetime.now(timezone.utc).isoformat(),
        "releaseNotes": args.notes,
        "firmware": {
            "url": args.firmware_url,
            "size": len(firmware),
            "sha256": digest,
            "signature": base64.b64encode(signature).decode("ascii"),
            "signatureAlgorithm": "ECDSA-P256-SHA256",
            "signatureEncoding": "base64-der",
        },
        "security": {
            "requireHttps": True,
            "requireSignature": True,
            "requireSha256": True,
        },
    }

    args.output.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Signed manifest written to: {args.output}")
    print(f"Firmware size: {len(firmware)} bytes")
    print(f"SHA-256: {digest}")
    print("Keep the private signing key offline and out of source control.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
