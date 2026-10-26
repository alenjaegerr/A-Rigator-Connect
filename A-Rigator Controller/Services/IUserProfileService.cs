namespace A_Rigator_Controller.Services;

public interface IUserProfileService
{
    string? GetDisplayName();

    void SaveDisplayName(string displayName);
}
