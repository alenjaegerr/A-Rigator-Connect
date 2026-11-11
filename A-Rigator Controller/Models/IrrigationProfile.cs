namespace A_Rigator_Controller.Models;

public sealed class IrrigationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public int LeftDurationSeconds { get; set; } = 3;
    public int BackDurationSeconds { get; set; } = 3;
    public int RightDurationSeconds { get; set; } = 3;
    public int RepeatCount { get; set; } = 1;

    public IrrigationProfile Clone() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = $"{Name} copy",
        LeftDurationSeconds = LeftDurationSeconds,
        BackDurationSeconds = BackDurationSeconds,
        RightDurationSeconds = RightDurationSeconds,
        RepeatCount = RepeatCount
    };
}
