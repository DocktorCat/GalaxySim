using System.Numerics;

namespace GalaxySim.Core.Simulation;

public sealed record StarSearchEntry(
    GalaxyObjectId Id,
    int Index,
    string Name,
    string SpectralClass,
    string Type,
    string SystemKind,
    string StellarSummary,
    int PlanetCount,
    float HabitableZoneAu,
    float LuminositySolar,
    float SolarMass,
    string TraitsSummary,
    Vector3 Position);
