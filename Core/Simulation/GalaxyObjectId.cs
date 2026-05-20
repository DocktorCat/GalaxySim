namespace GalaxySim.Core.Simulation;

public readonly record struct GalaxyObjectId(
    int GalaxySeed,
    int StarIndex,
    int PlanetIndex = -1,
    int MoonIndex = -1)
{
    public bool IsStar => PlanetIndex < 0;
    public bool IsPlanet => PlanetIndex >= 0 && MoonIndex < 0;
    public bool IsMoon => PlanetIndex >= 0 && MoonIndex >= 0;

    public static GalaxyObjectId ForStar(int galaxySeed, int starIndex)
        => new(galaxySeed, starIndex);

    public static GalaxyObjectId ForPlanet(int galaxySeed, int starIndex, int planetIndex)
        => new(galaxySeed, starIndex, planetIndex);

    public static GalaxyObjectId ForMoon(int galaxySeed, int starIndex, int planetIndex, int moonIndex)
        => new(galaxySeed, starIndex, planetIndex, moonIndex);

    public override string ToString()
    {
        if (IsMoon)
            return $"{GalaxySeed}:{StarIndex}:{PlanetIndex}:{MoonIndex}";
        if (IsPlanet)
            return $"{GalaxySeed}:{StarIndex}:{PlanetIndex}";
        return $"{GalaxySeed}:{StarIndex}";
    }
}
