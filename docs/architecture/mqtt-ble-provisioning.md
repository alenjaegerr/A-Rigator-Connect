# BLE MQTT credential provisioning (prototype)

This is the first provisioning step for the EMQX transport. It does not connect to MQTT or enable remote robot control yet.

## GATT characteristic

- Service UUID: `7a1c0001-8f2d-4b6a-9c31-4a5249474154`
- MQTT configuration characteristic UUID: `7a1c0007-8f2d-4b6a-9c31-4a5249474154`
- Properties: read and write
- Permissions: encrypted BLE read/write

The existing Wi-Fi and AP configuration characteristics remain unchanged.

## Commands

Write UTF-8 text to the MQTT configuration characteristic. Credentials use a chunked hexadecimal transfer so the flow works with the default BLE MTU.

| Command | Result |
|---|---|
| `GET` | `CONFIGURED` or `NOT_CONFIGURED` |
| `BEGIN:<hexLength>` | Starts a transfer; expected hex length must be even and 4–388 |
| `D000:<hexChunk>` | Appends the next chunk; offset is a zero-padded 3-digit decimal byte offset into the hex stream |
| `COMMIT` | Validates and saves credentials; returns `OK:SAVED` |
| `CLEAR` | `OK:CLEARED` after removing saved credentials |

The decoded payload is:

1. One byte username length (1–64)
2. One byte password length (1–128)
3. Username bytes (UTF-8)
4. Password bytes (UTF-8)

Chunks must arrive in order, with no gaps or overlaps. Each chunk carries at most 12 hex characters (17 bytes including the command prefix), fitting the default ATT write payload. A transfer can be abandoned by starting a new `BEGIN` or issuing `CLEAR`.

The ESP32 stores credentials in the separate NVS namespace `arigator-mqtt`; Wi-Fi and AP settings are not modified. Passwords are never printed to Serial.

## Prototype security notes

- The characteristic requests encrypted BLE access, matching the existing provisioning approach.
- The current BLE pairing configuration uses Just Works and does not provide MITM authentication. Encryption alone does not prove that the phone is an authorized owner. Treat this as a prototype flow and require physical-presence/ownership verification before production.
- NVS storage is not equivalent to encrypted-at-rest secret storage. Production devices should be evaluated with ESP32 Secure Boot, Flash Encryption, and an authenticated manufacturing provisioning process.
- Never commit broker credentials to source control or share them in chat.

## Current implementation boundary

The firmware currently accepts, stores, and clears MQTT credentials over BLE. It does not yet establish a TLS MQTT session, publish telemetry, or subscribe to command topics. The next firmware step is broker transport and connection diagnostics; remote movement and irrigation commands remain disabled until separate safety and authorization tests pass.
