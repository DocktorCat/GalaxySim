using System.Numerics;

namespace GalaxySim.Core.Simulation;

public sealed record SelectedStarInfo(
    int Index,
    string Name,
    string Type,
    string ModelKey,
    Vector3 Position,
    Vector3 Velocity,
    float TemperatureClass,
    float Mass,
    string SpectralClass,
    float SolarMass,
    float RadiusSolar,
    float LuminositySolar,
    float AgeGyr,
    string SystemKind,
    int PlanetCount,
    float HabitableZoneAu,
    string Description);
