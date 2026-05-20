namespace GalaxySim.Core.Simulation;

public sealed record FavoriteObjectEntry(
    GalaxyObjectId Id,
    string Name,
    string ObjectKind,
    string Type,
    string Summary);
