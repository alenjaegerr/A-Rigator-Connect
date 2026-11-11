# Google sign-in setup

The welcome-page Google button uses the existing Supabase Auth integration in `AccountService`. Google credentials are never embedded in the app. The app starts Supabase OAuth in the system browser, receives the app callback, validates the Supabase access token against `/auth/v1/user`, and stores the session using MAUI SecureStorage.

## App callback URI

```
com.companyname.arigatorcontroller://oauth2redirect
```

Platform callback registration:
- Android: intent filter on `MainActivity` for scheme `com.companyname.arigatorcontroller` and host `oauth2redirect`.
- iOS: URL scheme in `Info.plist`, with `WebAuthenticator.Default.OpenUrl` forwarded by `AppDelegate`.

If the app's ApplicationId or callback URI changes, update the URI consistently in `AccountService`, Android, iOS, and Supabase's redirect allow list.

## Supabase dashboard configuration required

The code alone cannot enable a Google provider. In the Supabase project used by the app:

1. Open Authentication → Providers → Google and enable Google.
2. Add the Google OAuth Client ID and Client Secret from Google Cloud.
3. In Google Cloud Console, add this as an authorized redirect URI (the Supabase Auth callback, not the app callback):
   `https://pcgbyowcqpgtkfbdfwfb.supabase.co/auth/v1/callback`
4. In Supabase Authentication → URL Configuration, add this exact app callback to the Additional Redirect URLs allow list:
   `com.companyname.arigatorcontroller://oauth2redirect`
5. Save the provider settings and test on a physical Android device.

The Supabase publishable key already configured in the app is a public client key; it is not a Google OAuth client secret. Never put the Google client secret in the app or repository.

## Onboarding behavior

- Google sign-in success saves the Google profile display name locally and continues to the existing BLE robot-pairing step.
- Google sign-in cancellation or failure leaves the welcome page open and offers Continue Locally.
- Local onboarding remains available without signing in.
- Account tokens remain in MAUI SecureStorage.

## Verification checklist

- Google provider is enabled and credentials are configured in Supabase.
- The app callback URI is allow-listed by Supabase.
- Android returns from the browser to A-Rigator Connect.
- Supabase user lookup succeeds before the session is saved.
- The account page shows the authenticated email.
- Force-close and relaunch; the account session restores.
- Sign out clears the locally stored tokens.

This repository change does not configure Google Cloud or Supabase dashboard settings and has not been build-tested in this environment.
