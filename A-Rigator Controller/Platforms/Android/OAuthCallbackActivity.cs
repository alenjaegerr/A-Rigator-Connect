using Android.App;
using Android.Content;
using Microsoft.Maui.Authentication;

namespace A_Rigator_Controller;

[Activity(
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[]
    {
        Intent.CategoryDefault,
        Intent.CategoryBrowsable
    },
    DataScheme = "com.companyname.arigatorcontroller",
    DataHost = "oauth2redirect")]
public class OAuthCallbackActivity : WebAuthenticatorCallbackActivity
{
}
