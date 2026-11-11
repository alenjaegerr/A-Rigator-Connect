namespace A_Rigator_Controller.Models;

public sealed class IrrigationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public string DirectionMode { get; set; } = "FullSweep";
    public int DurationSeconds { get; set; } = 3;
    public int LeftDurationSeconds { get; set; } = 3;
    public int BackDurationSeconds { get; set; } = 3;
    public int RightDurationSeconds { get; set; } = 3;
    public int RepeatCount { get; set; } = 1;

    public void ApplyDirectionMode()
    {
        LeftDurationSeconds = DirectionMode switch
        {
            "LeftSweep" or "BothSides" or "FullSweep" => DurationSeconds,
            _ => 0
        };
        BackDurationSeconds = DirectionMode switch
        {
            "BackSweep" or "FullSweep" => DurationSeconds,
            _ => 0
        };
        RightDurationSeconds = DirectionMode switch
        {
            "RightSweep" or "BothSides" or "FullSweep" => DurationSeconds,
            _ => 0
        };
    }

    public IrrigationProfile Clone() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = $"{Name} copy",
        DirectionMode = DirectionMode,
        DurationSeconds = DurationSeconds,
        LeftDurationSeconds = LeftDurationSeconds,
        BackDurationSeconds = BackDurationSeconds,
        RightDurationSeconds = RightDurationSeconds,
        RepeatCount = RepeatCount
    };
}
