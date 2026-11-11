using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using A_Rigator_Controller.Models;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class IrrigationProfilesViewModel : INotifyPropertyChanged
{
    private readonly IrrigationProfileService _service;
    private readonly IrrigationProfileSyncCoordinator _sync;
    private IrrigationProfile? _selectedProfile;
    private string _name = string.Empty;
    private string _directionMode = "FullSweep";
    private int _durationSeconds = 3;
    private string _repeatCount = "1";
    private string _message = "Profiles are stored on this device.";

    public IrrigationProfilesViewModel(IrrigationProfileService service, IrrigationProfileSyncCoordinator sync)
    {
        _service = service;
        _sync = sync;
        _sync.StatusChanged += OnSyncStatusChanged;
        Profiles = service.Profiles;
        _selectedProfile = service.SelectedProfile;
        LoadEditor(_selectedProfile);
        NewProfileCommand = new Command(CreateNewProfile);
        SaveCommand = new Command(SaveProfile);
        DeleteCommand = new Command(DeleteProfile, () => Profiles.Count > 1 && SelectedProfile is not null);
        DuplicateCommand = new Command(DuplicateProfile, () => SelectedProfile is not null);
        SelectDirectionCommand = new Command<string>(SelectDirection);
        IncreaseDurationCommand = new Command(() => DurationSeconds = Math.Min(60, DurationSeconds + 1));
        DecreaseDurationCommand = new Command(() => DurationSeconds = Math.Max(1, DurationSeconds - 1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<IrrigationProfile> Profiles { get; }
    public ICommand NewProfileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand SelectDirectionCommand { get; }
    public ICommand IncreaseDurationCommand { get; }
    public ICommand DecreaseDurationCommand { get; }

    public IrrigationProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetField(ref _selectedProfile, value)) return;
            if (value is not null) { _service.SelectProfile(value.Id); LoadEditor(value); }
            RefreshCommands();
        }
    }

    public string Name { get => _name; set => SetField(ref _name, value); }
    public string DirectionMode
    {
        get => _directionMode;
        set { if (SetField(ref _directionMode, value)) NotifySummary(); }
    }
    public int DurationSeconds
    {
        get => _durationSeconds;
        set { if (SetField(ref _durationSeconds, Math.Clamp(value, 1, 60))) NotifySummary(); }
    }
    public string RepeatCount { get => _repeatCount; set { if (SetField(ref _repeatCount, value)) NotifySummary(); } }
    public string Message { get => _message; private set => SetField(ref _message, value); }

    public string LeftModeLabel => DirectionMode == "LeftSweep" ? "✓ LEFT SWEEP" : "LEFT SWEEP";
    public string RightModeLabel => DirectionMode == "RightSweep" ? "✓ RIGHT SWEEP" : "RIGHT SWEEP";
    public string BackModeLabel => DirectionMode == "BackSweep" ? "✓ BACK SWEEP" : "BACK SWEEP";
    public string BothModeLabel => DirectionMode == "BothSides" ? "✓ BOTH SIDES" : "BOTH SIDES";
    public string FullModeLabel => DirectionMode == "FullSweep" ? "✓ FULL SWEEP" : "FULL SWEEP";
    public bool IsLeftSelected => DirectionMode is "LeftSweep" or "BothSides" or "FullSweep";
    public bool IsBackSelected => DirectionMode is "BackSweep" or "FullSweep";
    public bool IsRightSelected => DirectionMode is "RightSweep" or "BothSides" or "FullSweep";
    public int SelectedDirectionCount => (IsLeftSelected ? 1 : 0) + (IsBackSelected ? 1 : 0) + (IsRightSelected ? 1 : 0);
    public int TotalSpraySeconds => DurationSeconds * SelectedDirectionCount * (int.TryParse(RepeatCount, out var n) ? Math.Clamp(n, 1, 20) : 1);
    public string SequenceSummary => DirectionMode switch
    {
        "LeftSweep" => "Left sweep",
        "RightSweep" => "Right sweep",
        "BackSweep" => "Back sweep",
        "BothSides" => "Left sweep → Right sweep",
        _ => "Left sweep → Back sweep → Right sweep"
    };
    public string TotalTimeText => $"Estimated spray time: {TotalSpraySeconds} seconds (excluding direction changes)";
    public string EnabledDirectionsText =>
        $"{(IsLeftSelected ? $"Left {DurationSeconds}s" : "Left off")}  •  " +
        $"{(IsBackSelected ? $"Back {DurationSeconds}s" : "Back off")}  •  " +
        $"{(IsRightSelected ? $"Right {DurationSeconds}s" : "Right off")}";

    private void SelectDirection(string? mode)
    {
        if (mode is not ("LeftSweep" or "RightSweep" or "BackSweep" or "BothSides" or "FullSweep")) return;
        DirectionMode = mode;
    }

    private void CreateNewProfile()
    {
        SelectedProfile = null;
        Name = "New profile";
        DirectionMode = "FullSweep";
        DurationSeconds = 3;
        RepeatCount = "1";
        Message = "Choose a sweep pattern and set its timer.";
    }

    private void SaveProfile()
    {
        if (!int.TryParse(RepeatCount, out var repeat))
        {
            Message = "Enter a whole number for the number of passes.";
            return;
        }
        var profile = new IrrigationProfile
        {
            Id = SelectedProfile?.Id ?? Guid.NewGuid().ToString("N"),
            Name = Name,
            DirectionMode = DirectionMode,
            DurationSeconds = DurationSeconds,
            RepeatCount = repeat
        };
        profile.ApplyDirectionMode();
        try
        {
            _service.SaveProfile(profile);
            _service.SelectProfile(profile.Id);
            SelectedProfile = profile;
            Message = $"'{profile.Name}' saved locally. Unselected directions are set to 0 seconds. Cloud sync runs automatically when signed in and online.";
            RefreshCommands();
        }
        catch (Exception ex) { Message = ex.Message; }
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1) return;
        _service.DeleteProfile(SelectedProfile.Id);
        SelectedProfile = _service.SelectedProfile;
        Message = "Profile deleted.";
        RefreshCommands();
    }

    private void DuplicateProfile()
    {
        if (SelectedProfile is null) return;
        var copy = SelectedProfile.Clone();
        _service.SaveProfile(copy);
        SelectedProfile = copy;
        Message = "Profile duplicated. Rename it and save your changes.";
        RefreshCommands();
    }

    private void LoadEditor(IrrigationProfile? profile)
    {
        if (profile is null) return;
        Name = profile.Name;
        DirectionMode = profile.DirectionMode;
        DurationSeconds = profile.DurationSeconds;
        RepeatCount = profile.RepeatCount.ToString();
        NotifySummary();
    }

    private void OnSyncStatusChanged(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() => Message = status);
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(LeftModeLabel));
        OnPropertyChanged(nameof(RightModeLabel));
        OnPropertyChanged(nameof(BackModeLabel));
        OnPropertyChanged(nameof(BothModeLabel));
        OnPropertyChanged(nameof(FullModeLabel));
        OnPropertyChanged(nameof(IsLeftSelected));
        OnPropertyChanged(nameof(IsBackSelected));
        OnPropertyChanged(nameof(IsRightSelected));
        OnPropertyChanged(nameof(SelectedDirectionCount));
        OnPropertyChanged(nameof(TotalSpraySeconds));
        OnPropertyChanged(nameof(SequenceSummary));
        OnPropertyChanged(nameof(TotalTimeText));
        OnPropertyChanged(nameof(EnabledDirectionsText));
    }

    private void RefreshCommands()
    {
        (DeleteCommand as Command)?.ChangeCanExecute();
        (DuplicateCommand as Command)?.ChangeCanExecute();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
