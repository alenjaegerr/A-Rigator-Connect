using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace A_Rigator_Controller
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(new[] { Android.Content.Intent.ActionView }, Categories = new[] { Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable }, DataScheme = "com.companyname.arigatorcontroller", DataHost = "oauth2redirect")]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if (Window is { } window)
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(35))
                {
                    window.SetStatusBarColor(Android.Graphics.Color.White);
                }

                new WindowInsetsControllerCompat(window, window.DecorView).AppearanceLightStatusBars = true;
            }
        }
    }
}
