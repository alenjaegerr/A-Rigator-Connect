using Microsoft.Extensions.Logging;
using A_Rigator_Controller.Services;
using A_Rigator_Controller.ViewModels;
using A_Rigator_Controller.Views;

namespace A_Rigator_Controller
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();

#if ANDROID
            Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("EntryOutlineColor", (handler, _) =>
            {
                var entry = handler.PlatformView;
                var density = entry.Resources?.DisplayMetrics?.Density ?? 1f;
                var background = new Android.Graphics.Drawables.GradientDrawable();
                background.SetColor(Android.Graphics.Color.Transparent);
                background.SetCornerRadius(8 * density);
                background.SetStroke(Math.Max(1, (int)Math.Round(density)), Android.Graphics.Color.Rgb(23, 107, 69));

                var paddingLeft = entry.PaddingLeft;
                var paddingTop = entry.PaddingTop;
                var paddingRight = entry.PaddingRight;
                var paddingBottom = entry.PaddingBottom;

                entry.Background = background;
                entry.SetPadding(paddingLeft, paddingTop, paddingRight, paddingBottom);
            });
#endif

            builder.Services.AddSingleton<IRobotCommunicationService, RobotHttpCommunicationService>();
            builder.Services.AddSingleton<IWifiStatusService, WifiStatusService>();
            builder.Services.AddSingleton<IUserProfileService, UserProfileService>();
            builder.Services.AddSingleton<ISupabaseConfigService, SupabaseConfigService>();
            builder.Services.AddSingleton<IAccountService, AccountService>();
            builder.Services.AddSingleton<ISupabaseSyncService, SupabaseSyncService>();
            builder.Services.AddTransient<DashboardViewModel>();
            builder.Services.AddTransient<PairingViewModel>();
            builder.Services.AddTransient<WelcomeViewModel>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<WelcomePage>();
            builder.Services.AddTransient<PairingPage>();
            builder.Services.AddTransient<AccountPage>();
            builder.Services.AddTransient<AccountViewModel>();
            builder.Services.AddSingleton<AppShell>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
