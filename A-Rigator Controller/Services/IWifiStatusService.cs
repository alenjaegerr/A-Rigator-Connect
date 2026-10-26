namespace A_Rigator_Controller.Services;

public interface IWifiStatusService
{
    bool IsWifiEnabled { get; }

    void OpenWifiSettings();
}
