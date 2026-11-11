using System.ComponentModel;
using System.Runtime.CompilerServices;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.ViewModels;

public sealed class PublicIrrigationProfileItem : INotifyPropertyChanged
{
    private bool _isDownloaded;

    public PublicIrrigationProfileItem(IrrigationProfile profile)
    {
        Profile = profile;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IrrigationProfile Profile { get; }

    public string Name => Profile.Name;

    public string DirectionMode => Profile.DirectionMode switch
    {
        "LeftSweep" => "Left sweep",
        "RightSweep" => "Right sweep",
        "BackSweep" => "Back sweep",
        "BothSides" => "Both sides",
        "FullSweep" => "Full sweep",
        _ => Profile.DirectionMode
    };

    public string Details => $"{Profile.DurationSeconds}s per sweep · {Profile.RepeatCount} pass(es)";

    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            if (_isDownloaded == value) return;
            _isDownloaded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DownloadButtonText));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public string DownloadButtonText => IsDownloaded ? "DOWNLOADED" : "DOWNLOAD";

    public bool CanDownload => !IsDownloaded;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
