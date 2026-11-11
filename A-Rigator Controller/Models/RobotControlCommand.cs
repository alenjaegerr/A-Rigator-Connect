namespace A_Rigator_Controller.Models;

public enum RobotControlCommand
{
    Forward,
    Reverse,
    SteerLeft,
    SteerRight,
    SteerCenter,
    NozzleSweep,
    ManualIrrigationStart,
    ManualIrrigationStop,
    Stop,
    PumpOn,
    PumpOff,
    ResetShield,
    Heartbeat,
    AutonomousStart,
    AutonomousStop,
    PathwayStart,
    PathwayStop,
    TerrainStart,
    TerrainBiasLeft,
    TerrainBiasRight,
    TerrainBiasCenter,
    TerrainStop,
    ManualTerrainStart,
    ManualTerrainStop
}
