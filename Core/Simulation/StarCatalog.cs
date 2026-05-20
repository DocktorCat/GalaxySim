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

    public static StellarSystemProfile CreateSystemProfile(SelectedStarInfo star)
    {
        float roll = Hash01(star.Index * 101 + 19);
        if (!IsBinaryRoll(star.SpectralClass, roll))
        {
            return new StellarSystemProfile(
                star.Index,
                StellarMultiplicity.Single,
                BinaryLayout.None,
                string.Empty,
                string.Empty,
                0f,
                0f,
                0f,
                0f,
                0f,
                star.LuminositySolar,
                star.HabitableZoneAu,
                "Одиночная звезда",
                "Планетная система развивается вокруг одного звездного центра.");
        }

        float massRatio = 0.22f + Hash01(star.Index * 127 + 43) * 0.72f;
        float secondaryMass = MathF.Max(0.08f, star.SolarMass * massRatio);
        float secondaryTemp = TemperatureClassForMass(secondaryMass);
        string secondarySpectral = SpectralClassForTemperature(secondaryTemp);
        string secondaryModel = ModelKeyForTemperature(secondaryTemp);
        float secondaryRadius = RadiusForMass(secondaryMass, secondaryTemp);
        float secondaryLuminosity = LuminosityForMass(secondaryMass, secondaryTemp);
        BinaryLayout layout = BinaryLayoutFor(star, roll);
        float separation = SeparationFor(layout, star.Index);
        float eccentricity = 0.03f + Hash01(star.Index * 173 + 61) * 0.46f;
        float combinedLuminosity = star.LuminositySolar + secondaryLuminosity;
        float effectiveHz = MathF.Sqrt(MathF.Max(combinedLuminosity, 0.015f));
        string summary = layout switch
        {
            BinaryLayout.CloseBinary => "Тесная двойная система",
            BinaryLayout.CircumbinaryCandidate => "Кандидат circumbinary-системы",
            BinaryLayout.DisturbedYoungBinary => "Молодая возмущенная двойная система",
            _ => "Широкая двойная система",
        };
        string description = layout switch
        {
            BinaryLayout.CloseBinary => "Звезды расположены близко друг к другу, поэтому устойчивые планеты вероятнее находятся на более широких общих орбитах.",
            BinaryLayout.CircumbinaryCandidate => "Профиль подходит для планет, обращающихся вокруг пары звезд как вокруг общего центра света.",
            BinaryLayout.DisturbedYoungBinary => "Молодая двойная система усиливает пылевые пояса, наклоненные орбиты и нестабильные ранние конфигурации.",
            _ => "Широкий компаньон слабо влияет на внутренние планеты, но может возмущать внешние области и пояса обломков.",
        };

        return new StellarSystemProfile(
            star.Index,
            StellarMultiplicity.Binary,
            layout,
            secondarySpectral,
            secondaryModel,
            secondaryMass,
            secondaryRadius,
            secondaryLuminosity,
            separation,
            eccentricity,
            combinedLuminosity,
            effectiveHz,
            summary,
            description);
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
        if (IsBinaryRoll(spectralClass, roll))
            return BinaryLayoutFor(spectralClass, index, roll) switch
            {
                BinaryLayout.CloseBinary => "Тесная двойная система",
                BinaryLayout.CircumbinaryCandidate => "Circumbinary-кандидат",
                BinaryLayout.DisturbedYoungBinary => "Молодая двойная система",
                _ => spectralClass is "O" or "B" ? "Двойная массивная система" : "Широкая двойная система",
            };

        if (spectralClass is "O" or "B")
            return "Одиночная молодая система";
        if (roll > 0.78f) return "Компактная планетная система";
        return "Одиночная планетная система";
    }

    private static bool IsBinaryRoll(string spectralClass, float roll)
        => spectralClass is "O" or "B"
            ? roll > 0.72f
            : roll > 0.88f;

    private static BinaryLayout BinaryLayoutFor(SelectedStarInfo star, float roll)
        => BinaryLayoutFor(star.SpectralClass, star.Index, roll);

    private static BinaryLayout BinaryLayoutFor(string spectralClass, int index, float roll)
    {
        float detail = Hash01(index * 149 + 37);
        if (spectralClass is "O" or "B" && detail > 0.56f)
            return BinaryLayout.DisturbedYoungBinary;
        if (roll > 0.965f)
            return BinaryLayout.CircumbinaryCandidate;
        if (detail < 0.34f)
            return BinaryLayout.CloseBinary;
        return BinaryLayout.WideCompanion;
    }

    private static float SeparationFor(BinaryLayout layout, int index)
    {
        float h = Hash01(index * 191 + 89);
        return layout switch
        {
            BinaryLayout.CloseBinary => 0.08f + h * 0.42f,
            BinaryLayout.CircumbinaryCandidate => 0.18f + h * 0.72f,
            BinaryLayout.DisturbedYoungBinary => 2.0f + h * 18.0f,
            BinaryLayout.WideCompanion => 8.0f + h * 120.0f,
            _ => 0f,
        };
    }

    private static float TemperatureClassForMass(float mass)
    {
        if (mass < 0.45f)
            return Math.Clamp((mass - 0.18f) / 1.8f, 0.02f, 0.16f);
        if (mass < 0.82f)
            return Math.Clamp(0.16f + (mass - 0.55f) / 1.8f, 0.16f, 0.34f);
        if (mass < 1.05f)
            return Math.Clamp(0.34f + (mass - 0.82f) / 1.4f, 0.34f, 0.52f);
        if (mass < 1.45f)
            return Math.Clamp(0.52f + (mass - 1.05f) / 2.7f, 0.52f, 0.66f);
        if (mass < 3.0f)
            return Math.Clamp(0.66f + (mass - 1.45f) / 8.0f, 0.66f, 0.82f);
        return Math.Clamp(0.82f + (mass - 3.0f) / 18.0f, 0.82f, 1.0f);
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
