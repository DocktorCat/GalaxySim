using System;

namespace GalaxySim.Core.Simulation;

public static class SolarSystemCatalog
{
    private enum SystemArchitecture
    {
        Compact,
        SolarLike,
        GiantRich,
        DebrisHeavy,
    }

    public static PlanetInfo[] Generate(SelectedStarInfo star)
    {
        int count = Math.Clamp(star.PlanetCount, 0, 9);
        var planets = new PlanetInfo[count];
        SystemArchitecture architecture = SelectArchitecture(star);
        float orbitAu = MathF.Max(0.06f, star.HabitableZoneAu * InitialOrbitFactor(architecture));
        float previousVisualRadius = 0.014f;
        float previousOrbitRender = 0.11f;
        float hzRender = HabitableRenderRadius(star.HabitableZoneAu);
        float spacing = SpacingMultiplier(architecture);

        for (int i = 0; i < count; i++)
        {
            float h0 = Hash01(star.Index * 131 + i * 917 + 5);
            float h1 = Hash01(star.Index * 193 + i * 631 + 17);
            float h2 = Hash01(star.Index * 251 + i * 379 + 29);
            float h3 = Hash01(star.Index * 313 + i * 547 + 41);
            bool inHz = orbitAu >= star.HabitableZoneAu * 0.78f && orbitAu <= star.HabitableZoneAu * 1.32f;
            int typeCode = SelectTypeCode(star, architecture, i, h0, orbitAu / MathF.Max(star.HabitableZoneAu, 0.05f), inHz);

            float radiusEarth = RadiusEarth(typeCode, h1);
            float massEarth = MassEarth(typeCode, radiusEarth, h2);
            float visualRadius = VisualRadius(typeCode, radiusEarth);
            float minGap = (previousVisualRadius * 1.10f + visualRadius * 2.05f + 0.320f + i * 0.040f + h2 * 0.110f) * spacing;
            float solarLikeOrbit = hzRender * MathF.Pow(orbitAu / MathF.Max(star.HabitableZoneAu, 0.05f), 0.60f);
            float orbitRender = i == 0
                ? MathF.Max(0.56f + h0 * 0.120f, solarLikeOrbit)
                : MathF.Max(solarLikeOrbit, previousOrbitRender + minGap);
            previousVisualRadius = visualRadius;
            previousOrbitRender = orbitRender;

            float ecc = 0.84f + h1 * 0.12f;
            float phase = h2 * MathF.Tau;
            float speed = 0.22f / MathF.Pow(i + 1.35f, 1.45f);
            string type = TypeName(typeCode);
            float ringBoost = architecture == SystemArchitecture.GiantRich ? 0.18f : architecture == SystemArchitecture.DebrisHeavy ? 0.10f : 0f;
            bool hasRings = (typeCode == 2 && h3 < 0.32f + ringBoost) || (typeCode == 3 && h3 < 0.15f + ringBoost * 0.55f);
            float ringScale = hasRings ? 1.0f + Hash01(star.Index * 421 + i * 719 + 53) * 1.65f : 0f;
            float ringInner = hasRings ? visualRadius * (1.28f + h0 * 0.18f) : 0f;
            float ringOuter = hasRings ? visualRadius * (2.05f + h2 * 0.85f + ringScale) : 0f;
            float ringTilt = hasRings ? -0.55f + h1 * 1.10f : 0f;
            int moonCount = MoonCount(typeCode, h0, h1, h2);

            planets[i] = new PlanetInfo(
                i,
                CelestialNameGenerator.PlanetName(star.Index, i, typeCode),
                type,
                DescriptionFor(typeCode, inHz, hasRings),
                orbitAu,
                orbitRender,
                ecc,
                radiusEarth,
                massEarth,
                visualRadius,
                phase,
                speed,
                h0 * 37.0f + h1 * 11.0f,
                typeCode,
                inHz,
                hasRings,
                ringInner,
                ringOuter,
                ringTilt,
                moonCount);

            orbitAu *= OrbitGrowth(architecture, h0);
        }

        return planets;
    }

    public static AsteroidBeltInfo[] GenerateAsteroidBelts(SelectedStarInfo star, PlanetInfo[] planets)
    {
        if (planets.Length < 3)
            return [];

        SystemArchitecture architecture = SelectArchitecture(star);
        float chance = Hash01(star.Index * 991 + 73);
        float threshold = architecture == SystemArchitecture.DebrisHeavy ? 0.78f : architecture == SystemArchitecture.GiantRich ? 0.52f : 0.42f;
        if (chance > threshold)
            return [];

        var temp = new AsteroidBeltInfo[2];
        int count = 0;
        int inner = 1 + (int)(Hash01(star.Index * 461 + 19) * MathF.Max(1, planets.Length - 2));
        inner = Math.Clamp(inner, 1, planets.Length - 2);
        PlanetInfo a = planets[inner];
        PlanetInfo b = planets[inner + 1];
        float gap = MathF.Max(0.045f, b.OrbitRenderRadius - a.OrbitRenderRadius);
        temp[count++] = new AsteroidBeltInfo(
            (a.OrbitRenderRadius + b.OrbitRenderRadius) * 0.5f,
            Math.Clamp(gap * 0.42f, 0.035f, 0.085f),
                0.68f + Hash01(star.Index * 811 + 31) * 0.55f,
            Hash01(star.Index * 577 + 47) * 97f);

        float outerChance = architecture == SystemArchitecture.DebrisHeavy ? 0.48f : 0.20f;
        if (planets.Length >= 6 && Hash01(star.Index * 1229 + 101) < outerChance)
        {
            PlanetInfo last = planets[^1];
            temp[count++] = new AsteroidBeltInfo(
                last.OrbitRenderRadius + 0.12f + Hash01(star.Index * 1531 + 59) * 0.08f,
                0.080f + Hash01(star.Index * 1811 + 67) * 0.070f,
                0.35f + Hash01(star.Index * 2011 + 83) * 0.35f,
                Hash01(star.Index * 2131 + 97) * 127f);
        }

        var result = new AsteroidBeltInfo[count];
        for (int i = 0; i < count; i++)
            result[i] = temp[i];
        return result;
    }

    public static string StarDescription(SelectedStarInfo star) => StarDescriptionForClass(star.SpectralClass);

    public static string ArchitectureName(SelectedStarInfo star) => SelectArchitecture(star) switch
    {
        SystemArchitecture.Compact => "Compact system",
        SystemArchitecture.SolarLike => "Solar-like system",
        SystemArchitecture.GiantRich => "Giant-rich system",
        SystemArchitecture.DebrisHeavy => "Debris-heavy system",
        _ => "Planetary system",
    };

    public static string ArchitectureDescription(SelectedStarInfo star) => SelectArchitecture(star) switch
    {
        SystemArchitecture.Compact => "Planets are packed into comparatively tighter resonant-like spacing.",
        SystemArchitecture.SolarLike => "Rocky inner worlds, a temperate middle zone, and wider outer giant/icy regions.",
        SystemArchitecture.GiantRich => "The system is biased toward large outer planets, rings, and richer moon families.",
        SystemArchitecture.DebrisHeavy => "The system keeps more leftover debris, making asteroid belts more likely and denser.",
        _ => "Procedurally generated planetary architecture.",
    };

    public static string StarDescriptionForClass(string spectralClass) => spectralClass switch
    {
        "M" => "Красные карлики живут чрезвычайно долго и светят слабо; обитаемая зона близка к звезде, поэтому планеты чаще подвержены вспышкам и приливной блокировке.",
        "K" => "Оранжевые звезды стабильны и долговечны; это хорошие кандидаты для спокойных планетных систем с широкой историей эволюции.",
        "G" => "Желтые звезды похожи на Солнце: умеренная светимость, стабильная обитаемая зона и удобный баланс ультрафиолета.",
        "F" => "Желто-белые звезды ярче и горячее Солнца; их системы получают больше УФ-излучения, а срок стабильной жизни короче.",
        "A" => "Бело-голубые звезды яркие и молодые; планеты вокруг них сложнее удерживают спокойные условия из-за сильного излучения.",
        "B" => "Голубые гиганты живут недолго и излучают мощный ультрафиолет; устойчивые биосферы рядом с ними маловероятны.",
        "O" => "Горячие голубые гиганты крайне массивны и короткоживущи; их системы больше похожи на драматичные молодые лаборатории планетообразования.",
        _ => "Звезда главной последовательности с процедурно сгенерированной планетной системой.",
    };

    private static float HabitableRenderRadius(float habitableZoneAu)
        => Math.Clamp(habitableZoneAu * 0.55f, 0.52f, 2.10f);

    private static int MoonCount(int typeCode, float h0, float h1, float h2) => typeCode switch
    {
        2 => h0 < 0.18f ? 0 : 2 + (int)(h1 * 5.0f),
        3 => h0 < 0.30f ? 0 : 1 + (int)(h1 * 4.0f),
        1 => h0 < 0.68f ? 0 : 1 + (int)(h2 * 2.0f),
        0 => h0 < 0.74f ? 0 : 1,
        4 => h0 < 0.62f ? 0 : 1,
        6 => h0 < 0.66f ? 0 : 1,
        7 => h0 < 0.42f ? 0 : 1 + (int)(h1 * 3.0f),
        8 => h0 < 0.80f ? 0 : 1,
        9 => h0 < 0.78f ? 0 : 1,
        10 => 0,
        _ => h0 < 0.86f ? 0 : 1,
    };

    private static int SelectTypeCode(SelectedStarInfo star, SystemArchitecture architecture, int index, float h, float orbitToHz, bool inHz)
    {
        if (star.SpectralClass is "O" or "B")
            return h < 0.36f ? 2 : h < 0.62f ? 3 : h < 0.78f ? 7 : h < 0.90f ? 8 : 0;
        if (architecture == SystemArchitecture.GiantRich && orbitToHz > 0.78f)
            return h < 0.44f ? 2 : h < 0.66f ? 3 : h < 0.78f ? 7 : h < 0.90f ? 4 : 8;
        if (architecture == SystemArchitecture.DebrisHeavy && h > 0.82f)
            return orbitToHz < 0.70f ? 8 : 4;
        if (orbitToHz < 0.58f)
            return h < 0.36f ? 5 : h < 0.58f ? 10 : h < 0.78f ? 8 : h < 0.92f ? 0 : 2;
        if (inHz)
            return h < 0.30f ? 0 : h < 0.54f ? 1 : h < 0.68f ? 6 : h < 0.80f ? 9 : h < 0.92f ? 3 : 2;
        if (orbitToHz > 1.42f || index >= Math.Max(3, star.PlanetCount - 2))
            return h < 0.30f ? 3 : h < 0.54f ? 4 : h < 0.72f ? 7 : h < 0.88f ? 2 : 8;
        return h < 0.22f ? 0 : h < 0.38f ? 6 : h < 0.52f ? 9 : h < 0.66f ? 5 : h < 0.84f ? 7 : 3;
    }

    private static SystemArchitecture SelectArchitecture(SelectedStarInfo star)
    {
        float h = Hash01(star.Index * 3571 + 239);
        if (star.SpectralClass is "O" or "B")
            return h < 0.55f ? SystemArchitecture.GiantRich : SystemArchitecture.DebrisHeavy;
        if (h < 0.24f)
            return SystemArchitecture.Compact;
        if (h < 0.58f)
            return SystemArchitecture.SolarLike;
        if (h < 0.78f)
            return SystemArchitecture.GiantRich;
        return SystemArchitecture.DebrisHeavy;
    }

    private static float InitialOrbitFactor(SystemArchitecture architecture) => architecture switch
    {
        SystemArchitecture.Compact => 0.16f,
        SystemArchitecture.GiantRich => 0.28f,
        SystemArchitecture.DebrisHeavy => 0.20f,
        _ => 0.22f,
    };

    private static float SpacingMultiplier(SystemArchitecture architecture) => architecture switch
    {
        SystemArchitecture.Compact => 0.76f,
        SystemArchitecture.GiantRich => 1.18f,
        SystemArchitecture.DebrisHeavy => 1.06f,
        _ => 1.0f,
    };

    private static float OrbitGrowth(SystemArchitecture architecture, float h) => architecture switch
    {
        SystemArchitecture.Compact => 1.58f + h * 0.44f,
        SystemArchitecture.GiantRich => 2.35f + h * 0.92f,
        SystemArchitecture.DebrisHeavy => 2.02f + h * 0.76f,
        _ => 2.18f + h * 0.88f,
    };

    private static float RadiusEarth(int typeCode, float h) => typeCode switch
    {
        0 => 0.45f + h * 1.15f,
        1 => 0.85f + h * 1.35f,
        2 => 4.2f + h * 7.5f,
        3 => 2.4f + h * 2.6f,
        4 => 0.7f + h * 1.2f,
        5 => 0.55f + h * 1.0f,
        6 => 1.35f + h * 1.35f,
        7 => 1.9f + h * 1.9f,
        8 => 0.55f + h * 1.7f,
        9 => 0.65f + h * 1.45f,
        10 => 0.75f + h * 1.8f,
        _ => 1f,
    };

    private static float MassEarth(int typeCode, float radiusEarth, float h) => typeCode switch
    {
        2 => radiusEarth * radiusEarth * (5.0f + h * 8.0f),
        3 => radiusEarth * radiusEarth * (2.8f + h * 3.5f),
        6 => MathF.Pow(radiusEarth, 3.25f) * (1.15f + h * 0.85f),
        7 => radiusEarth * radiusEarth * (1.15f + h * 1.4f),
        10 => MathF.Pow(radiusEarth, 3.05f) * (1.45f + h * 1.1f),
        _ => MathF.Pow(radiusEarth, 3.15f) * (0.72f + h * 0.55f),
    };

    private static float VisualRadius(int typeCode, float radiusEarth) => typeCode switch
    {
        2 => 0.052f + MathF.Min(radiusEarth, 12f) * 0.0024f,
        3 => 0.039f + MathF.Min(radiusEarth, 5f) * 0.0023f,
        7 => 0.032f + MathF.Min(radiusEarth, 4.2f) * 0.0024f,
        6 => 0.021f + MathF.Min(radiusEarth, 3.0f) * 0.0028f,
        10 => 0.019f + MathF.Min(radiusEarth, 2.8f) * 0.0028f,
        1 => 0.018f + MathF.Min(radiusEarth, 2.4f) * 0.0027f,
        _ => 0.013f + MathF.Min(radiusEarth, 1.8f) * 0.0032f,
    };

    private static string TypeName(int typeCode) => typeCode switch
    {
        0 => "Каменистая планета",
        1 => "Океанический мир",
        2 => "Газовый гигант",
        3 => "Ледяной гигант",
        4 => "Ледяная планета",
        5 => "Лавовая планета",
        6 => "Суперземля",
        7 => "Мини-нептун",
        8 => "Углеродная планета",
        9 => "Пустынная планета",
        10 => "Парниковый мир",
        _ => "Планета",
    };

    private static string DescriptionFor(int typeCode, bool inHz, bool hasRings)
    {
        string baseText = DescriptionFor(typeCode, inHz);
        return hasRings
            ? baseText + " Вокруг планеты есть разреженная система колец из льда и пыли."
            : baseText;
    }

    private static string DescriptionFor(int typeCode, bool inHz) => typeCode switch
    {
        0 => inHz
            ? "Каменистый мир в зоне умеренного облучения; при наличии атмосферы может удерживать жидкую воду."
            : "Твердая планета с поверхностью из силикатов и металлов; условия сильно зависят от расстояния до звезды.",
        1 => "Планета с большой долей воды или плотной облачной оболочкой; климат может быть устойчивым, но часто скрыт облаками.",
        2 => "Массивный газовый гигант с полосами облаков и сильной гравитацией; потенциально имеет богатую систему спутников.",
        3 => "Холодный гигант с летучими льдами, метаном и аммиаком; обычно расположен во внешней части системы.",
        4 => "Малый холодный мир с ледяной корой; может сохранять подповерхностные океаны при приливном нагреве.",
        5 => "Близкая к звезде горячая планета с расплавленной поверхностью и интенсивным тепловым излучением.",
        6 => inHz
            ? "Массивная каменистая планета в умеренной зоне; высокая гравитация помогает удерживать плотную атмосферу."
            : "Крупная каменистая планета с сильной гравитацией; давление и тектоника заметно выше земных.",
        7 => "Переходный мир между каменистой планетой и ледяным гигантом; вероятна плотная водородно-гелиевая оболочка.",
        8 => "Темная углеродная планета с графитовой или карбидной корой; отражает мало света и выглядит контрастно.",
        9 => inHz
            ? "Сухой умеренный мир с редкими облаками и большими пустынными плато; вода ограничена локальными резервуарами."
            : "Сухая планета с разреженной атмосферой и сильными перепадами температуры.",
        10 => "Горячий парниковый мир с плотной токсичной атмосферой; поверхность скрыта желтоватой дымкой и облаками.",
        _ => "Процедурно сгенерированная планета.",
    };

    private static string Roman(int value) => value switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        7 => "VII",
        8 => "VIII",
        9 => "IX",
        _ => value.ToString(),
    };

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
