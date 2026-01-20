namespace GalaxyBudsController.Models;

public class BudsStatus
{
    public int BatteryLeft { get; set; } = -1;
    public int BatteryRight { get; set; } = -1;
    public bool IsWearing { get; set; }
    public NoiseControlMode NoiseControl { get; set; } = NoiseControlMode.Unknown;
    public int AmbientSoundLevel { get; set; } = -1;
    public int NoiseReductionLevel { get; set; } = -1;
}

public enum NoiseControlMode
{
    Off = 0,
    ANC = 1,
    Ambient = 2,
    Adaptive = 3,
    Unknown = -1
}
