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
- Added a centered, translucent connection overlay with fade transitions for active connection attempts; routine status polling remains in the background without a persistent spinner.
- Updated BLE robot discovery to scan without an Android service-UUID filter and validate the advertised A-Rigator name instead, avoiding scan-response UUID filter false negatives on some Android BLE stacks.
- Slowed background status polling to every two seconds while retaining the 250 ms heartbeat cadence needed during active manual commands.
- Hid the bottom tab bar during welcome, pairing, and remote-access setup; added fade transitions for Shell navigation and pairing-step changes.
- Fixed startup loading animation timing so the alligator waits for its track to receive a measured layout width before animating.
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

## Next interaction and driving-mode scope

The following is the agreed product scope for the next implementation phase. These items are requirements, not claims that the current firmware/app already supports every control.

### Dedicated Manual Control page

- Give manual operation a clearly designated page rather than mixing it into the general robot overview.
- Provide forward, reverse, left, right, and an always-accessible STOP action.
- Provide pump on/off and sprinkler direction selection: left only, back only (current default), and right only.
- Support nozzle positioning and configurable irrigation duration when the firmware exposes the required commands.
- Include practical diagnostics for line sensors, front obstacle sonar, water-level sonar (once installed/calibrated), pump/servo, motor shield, and connection heartbeat.
- STOP must override other manual actions. On communication loss, lock movement controls and rely on the ESP32 safety timeout; never imply that a remote STOP command succeeded when the robot is unreachable.

### Driving modes

- **Pathway Mode:** fully automatic movement using line tracing, with sonar-based obstacle protection.
- **Terrain Mode:** line tracing disabled; sonar is used for obstacle protection while a person steers the robot through grass or uneven fields.
- Both modes retain sonar safety. Terrain Mode is not autonomous line-following.

### Irrigation profiles

- Preserve the fixed nozzle direction choices: left only, back only, and right only.
- Both-side irrigation alternates left and right rather than treating the nozzle as a single simultaneous direction.
- Left and right irrigation durations are independently configurable.
- Allow multiple saved irrigation profiles.

Before exposing any of these settings as operational controls, confirm that the corresponding firmware command/API and physical actuator behavior exist. Do not simulate a successful operation in the app when firmware support is missing.

## Manual regression checklist

### Build and navigation
- [ ] Build Android Debug in Visual Studio.
- [ ] Launch and verify the alligator loading screen transitions into the app.
- [ ] Verify Home, Control, Telemetry, Settings bottom tabs.
- [ ] Verify the flyout/sandwich menu is absent.
- [ ] Verify dark mode persists after app restart.
- [ ] Verify welcome, pairing, remote Wi-Fi setup, and account routes still open.
- [ ] Verify BLE discovery finds the ESP32 by its advertised A-Rigator name on the target Android phone.
- [ ] Verify bottom tabs are hidden on welcome, pairing, and remote-access setup pages.
- [ ] Verify Shell tab changes and pairing-step changes use a brief fade transition.
- [ ] Verify connection attempts show a centered dark translucent overlay that fades in/out above the page.
- [ ] Verify background status polling does not display a persistent spinner or cause layout movement.

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
- [ ] Background the app during manual drive/pump operation and verify it sends Stop when possible; otherwise the ESP32 lease timeout must stop outputs.
- [ ] Confirm autonomous operation behavior is intentional when the app backgrounds.
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
