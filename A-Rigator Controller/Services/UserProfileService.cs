namespace A_Rigator_Controller.Services;

public sealed class UserProfileService : IUserProfileService
{
    private const string DisplayNameKey = "display_name";

    public string? GetDisplayName()
    {
        return Preferences.Default.Get<string?>(DisplayNameKey, null);
    }

    public void SaveDisplayName(string displayName)
    {
        Preferences.Default.Set(DisplayNameKey, displayName);
    }
}
