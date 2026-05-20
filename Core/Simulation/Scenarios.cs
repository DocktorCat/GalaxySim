using ComputeSharp;

namespace GalaxySim.Core.Simulation;

public enum Scenario
{
    Single,
}

public static class Scenarios
{
    public static GalaxyInitializer.GalaxyPlacement[] Build(Scenario s)
    {
        return
        [
            new GalaxyInitializer.GalaxyPlacement
            {
                Offset = Float3.Zero,
                BulkVelocity = Float3.Zero,
                Inclination = 0f,
                Azimuth = 0f,
                Spin = 1f,
                MassScale = 1f,
                Seed = 42,
            }
        ];
    }

    public static string Describe(Scenario s) => "Одна галактика";
}
