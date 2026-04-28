namespace GalaxySim.Core.Simulation;

public sealed record MoonInfo(
    int Index,
    int ParentPlanetIndex,
    string Name,
    string Type,
    string Description,
    float OrbitRenderRadius,
    float VisualRadius,
    float Phase,
    float OrbitSpeed,
    float Seed,
    float Tilt,
    bool IsIcy);
