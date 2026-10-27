using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Android.App;
using Android.Content;
using Android.Content.PM;
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
        Android.Content.Intent.CategoryDefault,
        Android.Content.Intent.CategoryBrowsable
    },
    DataScheme = "com.companyname.arigatorcontroller",
    DataHost = "oauth2redirect")]
public class WebAuthenticationCallbackActivity
    : WebAuthenticatorCallbackActivity
{
}
