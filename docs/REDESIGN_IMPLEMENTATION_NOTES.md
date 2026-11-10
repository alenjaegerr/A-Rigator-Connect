# A-Rigator Connect redesign implementation notes

## Scope delivered in this pass

- Replaced the flyout navigation with a persistent four-tab Shell: Home, Control, Telemetry, Settings.
- Added dedicated Control and Telemetry pages.
- Shared one DashboardViewModel instance across the tabs so commands and telemetry state stay synchronized.
- Kept the existing welcome, BLE pairing, Wi-Fi provisioning, remote Wi-Fi setup, account, and settings routes.
- Reworked Home into a robot overview and connection recovery surface.
- Added explicit app connection-path metadata: IoT/router path versus direct robot AP path.
- Added discovery attempts in this order: last selected path, known BLE-provisioned station address (when available), then the A-Rigator AP address.
- Added automatic reconnect attempts every five seconds while disconnected.
- Added AP recovery guidance that opens Android's Internet connectivity panel, sets the AP endpoint as preferred, and asks the user to reconnect to the A-Rigator SSID.
- Extended the app's status model to consume the firmware's existing wifiConnected, wifiIp, and hostname fields.
- Added ActivityIndicator feedback for connection, telemetry refresh, control operations, and account authentication.
- Stops manual motor/pump output requests when the app backgrounds, then resumes robot monitoring when the app returns.
- Restored the Supabase Google OAuth implementation with secure token storage and an Android callback activity.
- Added backend URL configuration as a separate requirement for remote-mode eligibility.
- Disabled cloud-sync success stubs and replaced the simulated remote Wi-Fi success screen with an honest remote-access-pending explanation.
- Added a visible note that sensor calibration and writable telemetry settings require firmware API support.

## Firmware data currently available

The current firmware `GET /api/status` response reports:
- success
- state
- mode
- pump
- controlsEnabled
- wifiConnected
- wifiIp
- hostname
- obstacleDistanceCm, obstacleEchoDetected, obstacleDetected
- lineGpio14 and lineGpio27 (HIGH = black, LOW = white)
- obstacleStopThresholdCm
- manualControlTimeoutMs

The app must not infer battery percentage, reservoir percentage, motor current, or historical charts from this response. Water level remains unavailable because the reservoir sonar pins are reserved but not installed/calibrated. Line inputs are labelled by GPIO rather than physical left/right until sensor orientation is verified.

## Cloud and account boundary

Google OAuth authenticates through the configured Supabase project and stores the returned tokens using MAUI SecureStorage. Remote-mode eligibility also requires a configured backend API URL. This does not itself implement a cloud relay, telemetry database, OTA service, or remote robot-control transport. Those services still require backend endpoints and security review.

Local BLE provisioning and local HTTP robot control must continue to work when no cloud account is configured.

## Manual regression checklist

### Build and navigation
- [ ] Build Android Debug in Visual Studio.
- [ ] Launch and verify the alligator loading screen transitions into the app.
- [ ] Verify Home, Control, Telemetry, Settings bottom tabs.
- [ ] Verify the flyout/sandwich menu is absent.
- [ ] Verify dark mode persists after app restart.
- [ ] Verify welcome, pairing, remote Wi-Fi setup, and account routes still open.

### IoT connection
- [ ] Connect robot to the same router/hotspot as the phone.
- [ ] Verify mDNS discovery at arigator.local.
- [ ] Verify BLE-provided station-IP fallback.
- [ ] Verify Home reports the IoT/router connection path.
- [ ] Turn off router Wi-Fi and verify the disconnect notice appears.
- [ ] Restore router Wi-Fi and verify automatic reconnect.

### AP recovery
- [ ] Verify robot setup AP remains available while STA is connected.
- [ ] Disconnect the phone from the robot AP and verify AP recovery guidance.
- [ ] Tap Connect through A-Rigator AP and verify Android Internet settings opens.
- [ ] Join the A-Rigator SSID, return to the app, and tap Search for robot.
- [ ] Verify direct AP connection is reported.
- [ ] Verify Search for robot can return to IoT discovery after AP recovery.

### Robot controls and safety
- [ ] Verify manual forward/reverse, pump on/off, stop, shield reset.
- [ ] Verify autonomous start/stop.
- [ ] Verify controls disable when disconnected or firmware disables controls.
- [ ] Interrupt connectivity during a manual command and verify UI locks controls.
- [ ] Confirm the ESP32 manual-control lease stops outputs after communication loss.
- [ ] Do not treat the app Stop button as a guaranteed emergency stop when the robot is unreachable.

### Telemetry and configuration
- [ ] Verify robot state, operating mode, pump state and network diagnostics update.
- [ ] Verify obstacle distance and echo/no-echo state update from the front sonar.
- [ ] Verify the read-only obstacle stop threshold and manual-control lease timeout match firmware constants.
- [ ] Verify line sensor GPIO14/GPIO27 values map HIGH to black and LOW to white.
- [ ] Verify water and battery remain visibly unavailable until firmware provides them.
- [ ] Confirm no demo values or fabricated historical graphs are displayed.
- [ ] Add firmware/API tests before enabling sensor calibration or writable thresholds.

### Account and cloud
- [ ] Configure Supabase URL and anon key.
- [ ] Add the Android callback URI to Supabase and Google OAuth provider settings.
- [ ] Verify Google sign-in browser redirect and callback.
- [ ] Verify token validation and SecureStorage persistence.
- [ ] Verify sign-out clears only the app account session and does not erase robot Wi-Fi credentials.
- [ ] Verify local robot control works when signed out.
- [ ] Do not enable remote control until a real authenticated backend relay is implemented and tested.

## Known limitations to debug next

- The app has not yet been built or run on a physical Android device after this code pass.
- The firmware status API extension has not yet been compiled or uploaded to the ESP32; rebuild and upload the unified-branch sketch before expecting obstacle/line telemetry fields.
- Android Wi-Fi panel behavior varies by OS/OEM; validate on the target Honor device.
- mDNS support can vary across Android networks; validate the BLE station-IP fallback.
- The current firmware does not expose reservoir water level, battery voltage/percentage, motor telemetry, historical telemetry, or writable sensor settings. Front obstacle distance and the two line-sensor GPIO states are now exposed.
- Remote/cloud robot control and OTA are not implemented by this pass.
- The reconnect loop and heartbeat timing must be exercised on-device; do not assume the configured five-second reconnect cadence is optimal.
- Google OAuth requires correct Supabase provider configuration, Google credentials, and the exact Android callback URL.
