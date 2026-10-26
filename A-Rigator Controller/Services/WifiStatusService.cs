using Microsoft.Maui.ApplicationModel;
#if ANDROID
using Android.Content;
using Android.Net.Wifi;
using Android.Provider;
#endif

namespace A_Rigator_Controller.Services;

public sealed class WifiStatusService : IWifiStatusService
{
    public bool IsWifiEnabled
    {
        get
        {
#if ANDROID
            var wifiManager = (WifiManager?)Platform.AppContext.GetSystemService(Context.WifiService);
            return wifiManager?.IsWifiEnabled ?? false;
#else
            return false;
#endif
        }
    }

    public void OpenWifiSettings()
    {
#if ANDROID
        var intent = new Intent(Settings.ActionWifiSettings);
        intent.AddFlags(ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(intent);
#endif
    }
}
