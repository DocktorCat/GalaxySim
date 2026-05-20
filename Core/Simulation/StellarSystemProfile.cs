namespace GalaxySim.Core.Simulation;

public enum StellarMultiplicity
{
    Single,
    Binary,
}

public enum BinaryLayout
{
    None,
    WideCompanion,
    CloseBinary,
    CircumbinaryCandidate,
    DisturbedYoungBinary,
}

public sealed record StellarSystemProfile(
    int StarIndex,
    StellarMultiplicity Multiplicity,
    BinaryLayout Layout,
    string SecondarySpectralClass,
    string SecondaryModelKey,
    float SecondarySolarMass,
    float SecondaryRadiusSolar,
    float SecondaryLuminositySolar,
    float SeparationAu,
    float BinaryEccentricity,
    float CombinedLuminositySolar,
    float EffectiveHabitableZoneAu,
    string Summary,
    string Description)
{
    public bool IsBinary => Multiplicity == StellarMultiplicity.Binary;
}
