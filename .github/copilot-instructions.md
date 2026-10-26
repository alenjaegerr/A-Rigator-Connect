# Copilot Instructions

## Project Guidelines
- A-Rigator Bot's currently listed component prices are WeMos/TB6612FNG motor driver ₱205, two JSN ultrasonic sensors ₱135, two line-tracking modules ₱26, one DFRobot 6V 15RPM 10kg-cm motor ₱1,040, and ESP32 dev board ₱355. The planned drive is not differential: one centrally mounted DC motor drives a geared rear axle connected to two rear wheels; the steering mechanism/components remain undecided. Pause mechanical steering design while working on the A-Rigator Connect app.
- For A-Rigator Connect, implement Google account auth and Supabase-backed user/robot data sync as part of the app architecture. Keep pairing as a clearly labeled mockup while the UI and protocol are still being finalized, but allow the app to support a real Google-account path and cloud sync when the backend is ready.
- Prioritize implementing real robot controls soon for a same-day presentation; defer OTA firmware updates and nonessential settings. Any real motor/pump controls must not be exposed over the current open AP; establish WPA2 access and firmware-side manual-control disconnect failsafe, preserving autonomous continuation on phone disconnect.
- For the A-Rigator robot control demo, omit steering controls and steering-related app/API behavior for now; limit the phone demo to DC motor forward/stop/reverse and pump on/off. Unused steering firmware routines may remain dormant unless they interfere with compiling/testing.
- Remote connection mode and cloud sync are locked behind a Google account and Supabase-backed auth flow. Local mode stays available without Google sign-in, but remote-mode controls and sync features may remain disabled until the user is authenticated.

## A-Rigator Connect Setup Flow
- Updated setup flow has exactly three steps:
  1. Choose Google or Local, then declare a name in the app.
  2. Pair/connect via the robot's Wi-Fi without any one-time pairing code.
  3. Show confirmation and offer to proceed to the dashboard.
- Remove the one-time code UI and logic.
- Keep actual Google sign-in/robot connection mocked until the UI/protocol/backend are ready, but allow the app architecture to support a real Google auth and Supabase account path. Maintain local-first behavior while remote mode remains gated behind Google account authentication.
