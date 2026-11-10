using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Content;
#endif

namespace A_Rigator_Controller.Services;

public sealed class WifiSettingsService : IWifiSettingsService
{
    public Task OpenNetworkSettingsAsync()
    {
#if ANDROID
        var intent = new Intent("android.settings.panel.action.INTERNET_CONNECTIVITY");
        intent.AddFlags(ActivityFlags.NewTask);
        Platform.CurrentActivity?.StartActivity(intent);
#else
        AppInfo.ShowSettingsUI();
#endif
        return Task.CompletedTask;
    }
}