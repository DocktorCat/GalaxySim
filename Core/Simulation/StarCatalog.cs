using System;
using System.Numerics;

namespace GalaxySim.Core.Simulation;

public static class StarCatalog
{
    public static SelectedStarInfo Create(int index, Vector3 position, Vector3 velocity, float temperatureClass, float mass)
    {
        temperatureClass = Math.Clamp(temperatureClass, 0f, 1f);
        float seedA = Hash01(index * 17 + 11);
        float seedB = Hash01(index * 31 + 7);
        float seedC = Hash01(index * 47 + 23);

        string spectralClass = SpectralClassForTemperature(temperatureClass);
        string type = TypeForSpectralClass(spectralClass);
        string modelKey = ModelKeyForTemperature(temperatureClass);
        float solarMass = MathF.Max(0.08f, MassScaleForTemperature(temperatureClass) * (0.88f + seedA * 0.24f));
        float radiusSolar = RadiusForMass(solarMass, temperatureClass);
        float luminositySolar = LuminosityForMass(solarMass, temperatureClass);
        float ageGyr = MathF.Min(13.2f, (0.35f + seedB * 11.8f) / MathF.Max(0.35f, solarMass * 0.42f));
        int planetCount = PlanetCount(index, spectralClass, seedC);
        string systemKind = SystemKind(index, spectralClass);
        float habitableZoneAu = MathF.Sqrt(MathF.Max(luminositySolar, 0.015f));

        return new SelectedStarInfo(
            index,
            MakeStarName(index, spectralClass),
            type,
            modelKey,
            position,
            velocity,
            temperatureClass,
            mass,
            spectralClass,
            solarMass,
            radiusSolar,
            luminositySolar,
            ageGyr,
            systemKind,
            planetCount,
            habitableZoneAu,
            SolarSystemCatalog.StarDescriptionForClass(spectralClass));
    }

    public static string ModelKeyForTemperature(float temperatureClass)
    {
        if (temperatureClass < 0.18f) return "red_dwarf";
        if (temperatureClass < 0.38f) return "orange_star";
        if (temperatureClass < 0.58f) return "yellow_star";
        if (temperatureClass < 0.78f) return "blue_white_star";
        return "blue_giant";
    }

    private static string MakeStarName(int index, string spectralClass)
        => CelestialNameGenerator.StarName(index, spectralClass);

    private static string SpectralClassForTemperature(float t)
    {
        if (t < 0.16f) return "M";
        if (t < 0.34f) return "K";
        if (t < 0.52f) return "G";
        if (t < 0.66f) return "F";
        if (t < 0.82f) return "A";
        if (t < 0.94f) return "B";
        return "O";
    }

    private static string TypeForSpectralClass(string spectralClass) => spectralClass switch
    {
        "M" => "Красный карлик",
        "K" => "Оранжевая звезда",
        "G" => "Жёлтая звезда",
        "F" => "Жёлто-белая звезда",
        "A" => "Бело-голубая звезда",
        "B" => "Голубой гигант",
        "O" => "Горячий голубой гигант",
        _ => "Звезда главной последовательности",
    };

    private static float MassScaleForTemperature(float t)
    {
        if (t < 0.16f) return 0.18f + t * 1.8f;
        if (t < 0.34f) return 0.55f + (t - 0.16f) * 1.8f;
        if (t < 0.52f) return 0.82f + (t - 0.34f) * 1.4f;
        if (t < 0.66f) return 1.05f + (t - 0.52f) * 2.7f;
        if (t < 0.82f) return 1.45f + (t - 0.66f) * 8.0f;
        return 3.0f + (t - 0.82f) * 18.0f;
    }

    private static float RadiusForMass(float mass, float temperatureClass)
        => MathF.Pow(mass, temperatureClass > 0.80f ? 0.68f : 0.82f);

    private static float LuminosityForMass(float mass, float temperatureClass)
    {
        float exp = temperatureClass > 0.80f ? 3.15f : 3.75f;
        return MathF.Max(0.003f, MathF.Pow(mass, exp));
    }

    private static int PlanetCount(int index, string spectralClass, float seed)
    {
        int baseCount = spectralClass switch
        {
            "O" or "B" => 1,
            "A" => 2,
            "M" => 4,
            _ => 5,
        };
        int variation = (int)MathF.Floor(seed * 5.0f);
        return Math.Clamp(baseCount + variation - 1, 0, 9);
    }

    private static string SystemKind(int index, string spectralClass)
    {
        float roll = Hash01(index * 101 + 19);
        if (spectralClass is "O" or "B")
            return roll > 0.72f ? "Двойная массивная система" : "Одиночная молодая система";
        if (roll > 0.88f) return "Двойная система";
        if (roll > 0.78f) return "Компактная планетная система";
        return "Одиночная планетная система";
    }

    private static float Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x / (float)uint.MaxValue;
        }
    }
}
