namespace GalaxySim.Core.Simulation;

public sealed record PlanetInfo(
    int Index,
    string Name,
    string Type,
    string Description,
    float OrbitAu,
    float OrbitRenderRadius,
    float Eccentricity,
    float RadiusEarth,
    float MassEarth,
    float VisualRadius,
    float Phase,
    float OrbitSpeed,
    float Seed,
    int TypeCode,
    bool IsInHabitableZone,
    bool HasRings,
    float RingInnerRadius,
    float RingOuterRadius,
    float RingTilt,
    int MoonCount);
