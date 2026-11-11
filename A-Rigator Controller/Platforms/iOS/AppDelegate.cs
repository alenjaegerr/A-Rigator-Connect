using Foundation;
using UIKit;
using Microsoft.Maui.Authentication;

namespace A_Rigator_Controller
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
        {
            WebAuthenticator.Default.OpenUrl(url);
            return base.OpenUrl(app, url, options);
        }
    }
}
