using System;

namespace GalaxySim.Core.Simulation;

public static class SolarSystemCatalog
{
    private enum SystemArchitecture
    {
        Compact,
        ResonantChain,
        SolarLike,
        HabitableRich,
        GiantRich,
        DebrisHeavy,
        HarshYoung,
        MigratedGiant,
        AncientStable,
        MetalRich,
    }

    public static PlanetInfo[] Generate(SelectedStarInfo star)
    {
        int count = Math.Clamp(star.PlanetCount, 0, 9);
        var planets = new PlanetInfo[count];
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
        float effectiveHzAu = EffectiveHabitableZoneAu(star, stellar);
        SystemArchitecture architecture = SelectArchitecture(star, stellar);
        float orbitAu = MathF.Max(0.06f, effectiveHzAu * InitialOrbitFactor(architecture, stellar));
        orbitAu = MathF.Max(orbitAu, MinimumStableOrbitAu(stellar, effectiveHzAu));
        float previousVisualRadius = 0.014f;
        float previousOrbitRender = 0.11f;
        float hzRender = HabitableRenderRadius(effectiveHzAu);
        float spacing = SpacingMultiplier(architecture) * BinarySpacingMultiplier(stellar);

        for (int i = 0; i < count; i++)
        {
            float h0 = Hash01(star.Index * 131 + i * 917 + 5);
            float h1 = Hash01(star.Index * 193 + i * 631 + 17);
            float h2 = Hash01(star.Index * 251 + i * 379 + 29);
            float h3 = Hash01(star.Index * 313 + i * 547 + 41);
            bool inHz = orbitAu >= effectiveHzAu * 0.78f && orbitAu <= effectiveHzAu * 1.32f;
            float orbitToHz = orbitAu / MathF.Max(effectiveHzAu, 0.05f);
            int typeCode = SelectTypeCode(star, architecture, i, h0, orbitToHz, inHz);
            if (typeCode == 1 && !inHz)
                typeCode = orbitToHz < 1.0f ? 10 : 4;

            float radiusEarth = RadiusEarth(typeCode, h1);
            float massEarth = MassEarth(typeCode, radiusEarth, h2);
            float visualRadius = VisualRadius(typeCode, radiusEarth);
            float minGap = (previousVisualRadius * 1.10f + visualRadius * 2.05f + 0.320f + i * 0.040f + h2 * 0.110f) * spacing;
            float solarLikeOrbit = hzRender * MathF.Pow(orbitAu / MathF.Max(effectiveHzAu, 0.05f), 0.60f);
            float orbitRender = i == 0
                ? MathF.Max(0.56f + h0 * 0.120f, solarLikeOrbit)
                : MathF.Max(solarLikeOrbit, previousOrbitRender + minGap);
            previousVisualRadius = visualRadius;
            previousOrbitRender = orbitRender;

            float ecc = 0.84f + h1 * 0.12f;
            float phase = h2 * MathF.Tau;
            float speed = 0.22f / MathF.Pow(i + 1.35f, 1.45f);
            string type = TypeName(typeCode, h1, h2, inHz, orbitToHz);
            float ringBoost = architecture == SystemArchitecture.GiantRich ? 0.18f : architecture == SystemArchitecture.MigratedGiant ? 0.14f : architecture == SystemArchitecture.DebrisHeavy ? 0.10f : architecture == SystemArchitecture.HarshYoung ? 0.06f : 0f;
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
                DescriptionFor(typeCode, inHz, hasRings, architecture, orbitToHz),
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

            orbitAu *= OrbitGrowth(architecture, h0) * BinaryOrbitGrowthMultiplier(stellar, i);
        }

        return planets;
    }

    public static AsteroidBeltInfo[] GenerateAsteroidBelts(SelectedStarInfo star, PlanetInfo[] planets)
    {
        if (planets.Length < 3)
            return [];

        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
        SystemArchitecture architecture = SelectArchitecture(star, stellar);
        float chance = Hash01(star.Index * 991 + 73);
        float threshold = architecture == SystemArchitecture.DebrisHeavy ? 0.78f : architecture == SystemArchitecture.GiantRich ? 0.52f : 0.42f;
        threshold = Math.Clamp(threshold + BinaryDebrisBonus(stellar), 0.12f, 0.90f);
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

        float outerChance = Math.Clamp((architecture == SystemArchitecture.DebrisHeavy ? 0.48f : 0.20f) + BinaryDebrisBonus(stellar) * 0.65f, 0.04f, 0.74f);
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

    public static MoonInfo[] GenerateMoons(SelectedStarInfo star, PlanetInfo[] planets)
    {
        var moonInfos = new MoonInfo[32];
        int moonCount = 0;
        for (int i = 0; i < planets.Length && i < 9; i++)
        {
            PlanetInfo p = planets[i];
            for (int m = 0; m < p.MoonCount && moonCount < moonInfos.Length; m++, moonCount++)
            {
                float h0 = Hash01(star.Index * 1009 + i * 197 + m * 53 + 11);
                float h1 = Hash01(star.Index * 1217 + i * 211 + m * 67 + 17);
                float h2 = Hash01(star.Index * 1471 + i * 229 + m * 71 + 23);
                float orbit = p.VisualRadius * (2.35f + m * 1.18f + h0 * 1.00f);
                float radius = Math.Clamp(p.VisualRadius * (0.13f + h1 * 0.13f), 0.006f, 0.020f);
                float phase = h2 * MathF.Tau;
                float speed = (0.62f + h0 * 0.56f) / MathF.Sqrt(m + 1.4f);
                float seed = h0 * 41f + h1 * 17f;
                float tilt = -0.45f + h2 * 0.90f;
                (string moonType, string moonDescription, bool isIcy) = MoonProfile(star, p, m, h0, h1, h2);
                moonInfos[moonCount] = new MoonInfo(
                    moonCount,
                    i,
                    CelestialNameGenerator.MoonName(star.Index, i, m, isIcy),
                    moonType,
                    moonDescription,
                    orbit,
                    radius,
                    phase,
                    speed,
                    seed,
                    tilt,
                    isIcy);
            }
        }

        return moonInfos[..moonCount];
    }

    public static float EffectiveHabitableZoneAu(SelectedStarInfo star)
        => EffectiveHabitableZoneAu(star, StarCatalog.CreateSystemProfile(star));

    public static string StarDescription(SelectedStarInfo star) => StarDescriptionForClass(star.SpectralClass);

    public static string ArchitectureName(SelectedStarInfo star) => SelectArchitecture(star) switch
    {
        SystemArchitecture.Compact => "Compact system",
        SystemArchitecture.ResonantChain => "Resonant chain system",
        SystemArchitecture.SolarLike => "Solar-like system",
        SystemArchitecture.HabitableRich => "Temperate-rich system",
        SystemArchitecture.GiantRich => "Giant-rich system",
        SystemArchitecture.DebrisHeavy => "Debris-heavy system",
        SystemArchitecture.HarshYoung => "Harsh young system",
        SystemArchitecture.MigratedGiant => "Migrated giant system",
        SystemArchitecture.AncientStable => "Ancient stable system",
        SystemArchitecture.MetalRich => "Metal-rich inner system",
        _ => "Planetary system",
    };

    public static string ArchitectureDescription(SelectedStarInfo star) => SelectArchitecture(star) switch
    {
        SystemArchitecture.Compact => "Planets are packed into comparatively tighter resonant-like spacing.",
        SystemArchitecture.ResonantChain => "A compact sequence of planets with near-regular spacing and synchronized-looking orbital periods.",
        SystemArchitecture.SolarLike => "Rocky inner worlds, a temperate middle zone, and wider outer giant/icy regions.",
        SystemArchitecture.HabitableRich => "The layout is biased toward multiple rocky or oceanic worlds near the temperate zone.",
        SystemArchitecture.GiantRich => "The system is biased toward large outer planets, rings, and richer moon families.",
        SystemArchitecture.DebrisHeavy => "The system keeps more leftover debris, making asteroid belts more likely and denser.",
        SystemArchitecture.HarshYoung => "Young bright stars and intense radiation bias the system toward hot inner worlds and volatile giants.",
        SystemArchitecture.MigratedGiant => "A giant planet appears unusually close to the star, suggesting inward migration and disturbed inner orbits.",
        SystemArchitecture.AncientStable => "Old long-lived stars favor calmer, wider orbital histories and weathered rocky worlds.",
        SystemArchitecture.MetalRich => "The inner system is biased toward dense rocky and carbon-rich worlds.",
        _ => "Procedurally generated planetary architecture.",
    };

    public static string InterestSummary(SelectedStarInfo star)
        => string.Join(", ", InterestTags(star));

    public static string InterestExplanationSummary(SelectedStarInfo star)
        => string.Join("\n", InterestExplanations(star));

    public static string SystemProfileDescription(SelectedStarInfo star)
    {
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
        string architecture = SelectArchitecture(star, stellar) switch
        {
            SystemArchitecture.Compact => "Планеты здесь собраны плотнее обычного, поэтому система удобна для быстрого обзора и поиска резонансных орбит.",
            SystemArchitecture.ResonantChain => "Планеты выстроены почти регулярной цепочкой: орбиты выглядят компактными и хорошо подходят для наблюдения резонансных групп.",
            SystemArchitecture.SolarLike => "Архитектура напоминает спокойную солнечную систему: внутренние каменистые миры, умеренная зона и более массивные внешние орбиты.",
            SystemArchitecture.HabitableRich => "Система смещена к нескольким каменистым или океаническим планетам около умеренной зоны.",
            SystemArchitecture.GiantRich => "Система смещена в сторону крупных внешних планет, колец и богатых семейств спутников.",
            SystemArchitecture.DebrisHeavy => "В системе сохранилось много остаточного материала, поэтому пояса обломков и пыльные структуры встречаются чаще.",
            SystemArchitecture.HarshYoung => "Молодая яркая звезда формирует жесткую среду: горячие внутренние планеты, сильное излучение и нестабильная ранняя история.",
            SystemArchitecture.MigratedGiant => "Один из газовых гигантов оказался близко к звезде: такая миграция могла вытеснить часть внутренних миров и возмутить соседние орбиты.",
            SystemArchitecture.AncientStable => "Система старая и спокойная: орбиты успели стабилизироваться, а каменистые планеты несут следы долгой эрозии и ударной истории.",
            SystemArchitecture.MetalRich => "Внутренние области богаты плотными каменистыми и углеродными планетами, поэтому система выглядит более тяжелой и компактной.",
            _ => "Планетная архитектура сгенерирована процедурно.",
        };
        string binaryInfluence = stellar.Layout switch
        {
            BinaryLayout.CloseBinary => " Тесная пара смещает устойчивые орбиты наружу и делает внутреннюю архитектуру более компактной вокруг общего центра массы.",
            BinaryLayout.CircumbinaryCandidate => " Планеты считаются circumbinary-кандидатами: расчет умеренной зоны идет от суммарной светимости пары.",
            BinaryLayout.DisturbedYoungBinary => " Молодой компаньон повышает вероятность пылевых поясов, наклоненных орбит и внешних возмущений.",
            BinaryLayout.WideCompanion => " Широкий компаньон почти не ломает внутреннюю систему, но заметнее влияет на внешние области и пояса обломков.",
            _ => string.Empty,
        };

        return $"{architecture}{binaryInfluence} Ключевые признаки: {InterestSummary(star)}.";
    }

    public static string StarEncyclopedia(SelectedStarInfo star)
    {
        PlanetInfo[] planets = Generate(star);
        AsteroidBeltInfo[] belts = GenerateAsteroidBelts(star, planets);
        string activity = star.SpectralClass switch
        {
            "M" => "Низкая светимость делает систему компактной, но планеты в умеренной зоне чаще находятся близко к звезде и могут испытывать вспышечную активность.",
            "K" => "Оранжевые звезды считаются особенно устойчивыми: они живут долго, светят мягче Солнца и дают планетам больше времени на спокойную эволюцию.",
            "G" => "Желтая звезда создает сбалансированную среду с привычной умеренной зоной и сравнительно стабильной историей излучения.",
            "F" => "Повышенная светимость расширяет умеренную зону, но усиливает ультрафиолетовую нагрузку и сокращает спокойный период жизни системы.",
            "A" or "B" or "O" => "Массивная яркая звезда быстро меняет окружающую среду: планеты получают сильное излучение, а стабильные биосферы маловероятны.",
            _ => "Звезда формирует процедурную планетную систему с параметрами, близкими к главной последовательности.",
        };

        string beltsText = belts.Length == 0
            ? "Выраженных поясов обломков не обнаружено."
            : $"Обнаружено поясов обломков: {belts.Length}; они указывают на сохранившийся материал после формирования планет.";

        return
            $"{activity}\n\n" +
            $"Система содержит {planets.Length} планет. Умеренная зона находится около {EffectiveHabitableZoneAu(star):F2} а.е.; светимость звезды {star.LuminositySolar:F2} L☉, возраст {star.AgeGyr:F2} млрд лет. {beltsText}";
    }

    public static string PlanetEncyclopedia(SelectedStarInfo star, PlanetInfo planet)
    {
        float hzAu = EffectiveHabitableZoneAu(star);
        string orbitZone = planet.OrbitAu < hzAu * 0.72f
            ? "внутренней горячей области"
            : planet.OrbitAu > hzAu * 1.45f
                ? "внешней холодной области"
                : "умеренной области";

        string climate = planet.TypeCode switch
        {
            0 => planet.IsInHabitableZone ? "Каменистая поверхность может поддерживать устойчивые температурные контрасты при наличии атмосферы." : "Каменистая поверхность, вероятно, испытывает сильную зависимость от локального нагрева и состава атмосферы.",
            1 => "Большая доля воды или плотных облаков делает климат инерционным; наблюдения поверхности могут быть затруднены.",
            2 => "Газовая оболочка доминирует над всей структурой планеты; интерес представляют кольца, штормовые зоны и спутники.",
            3 => "Летучие льды и холодная атмосфера делают планету хорошим маркером внешней эволюции системы.",
            4 => "Ледяная кора может скрывать внутренние слои, а при приливном нагреве возможны подповерхностные резервуары.",
            5 => "Близость к звезде поддерживает расплавленные породы и мощное тепловое излучение.",
            6 => planet.IsInHabitableZone ? "Высокая масса помогает удерживать атмосферу, но давление у поверхности может быть заметно выше земного." : "Суперземля сохраняет мощную гравитацию и потенциально активную внутреннюю динамику.",
            7 => "Мини-нептун является переходным объектом с плотной оболочкой и неясной границей между поверхностью и атмосферой.",
            8 => "Углеродная химия делает поверхность темнее и необычнее по составу, чем у силикатных миров.",
            9 => planet.IsInHabitableZone ? "Сухой климат может быть устойчивым, если небольшие резервуары воды пережили раннюю эволюцию." : "Сухая поверхность и слабая облачность усиливают температурные перепады.",
            10 => "Плотная парниковая атмосфера удерживает тепло и скрывает поверхность под облачным слоем.",
            _ => "Планета интересна как часть процедурной архитектуры системы.",
        };

        string rings = planet.HasRings
            ? " Кольца делают объект заметным динамическим узлом и могут быть связаны с разрушенными спутниками или остаточным материалом."
            : string.Empty;
        string subtype = PlanetSubtypeNote(planet);

        return
            $"Планета находится в {orbitZone}: расстояние {planet.OrbitAu:F2} а.е., период около {MathF.Pow(MathF.Max(planet.OrbitAu, 0.01f), 1.5f):F2} лет.\n\n" +
            $"{climate}{rings}\n\n" +
            $"{subtype}\n\n" +
            $"Масса {planet.MassEarth:F2} M⊕, радиус {planet.RadiusEarth:F2} R⊕, спутников: {planet.MoonCount}.";
    }

    public static string MoonEncyclopedia(SelectedStarInfo star, PlanetInfo parent, MoonInfo moon)
    {
        string composition = moon.IsIcy
            ? "Ледяной состав делает спутник хорошим кандидатом для холодной геологии, трещиноватой коры и следов приливного нагрева."
            : "Каменистый состав указывает на более темную кратерированную поверхность и слабую собственную активность.";

        string context = parent.TypeCode is 2 or 3 or 7
            ? "Спутник вращается вокруг массивной планеты, поэтому приливные эффекты и резонансы с соседними лунами могут быть важнее прямого влияния звезды."
            : "Спутник связан с относительно небольшой планетой, поэтому его эволюция сильнее зависит от истории ударов и устойчивости орбиты.";
        string subtype = MoonSubtypeNote(moon);

        return
            $"{composition}\n\n" +
            $"{context}\n\n" +
            $"{subtype}\n\n" +
            $"Родительская планета: {parent.Name}. Орбитальный радиус в модели {moon.OrbitRenderRadius:F3}, относительный радиус {moon.VisualRadius:F3}.";
    }

    public static string[] InterestTags(SelectedStarInfo star)
    {
        PlanetInfo[] planets = Generate(star);
        AsteroidBeltInfo[] belts = GenerateAsteroidBelts(star, planets);
        return InterestTags(star, planets, belts);
    }

    public static string[] InterestTags(SelectedStarInfo star, PlanetInfo[] planets, AsteroidBeltInfo[] belts)
    {
        var tags = new System.Collections.Generic.List<string>(6);
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
        float hzAu = EffectiveHabitableZoneAu(star, stellar);
        SystemArchitecture architecture = SelectArchitecture(star, stellar);

        bool hasHabitableCandidate = Array.Exists(planets, p => p.IsInHabitableZone && p.TypeCode is 0 or 1 or 6 or 9);
        bool hasRingedGiant = Array.Exists(planets, p => p.HasRings && p.TypeCode is 2 or 3);
        bool hasTemperateOcean = Array.Exists(planets, p => p.TypeCode == 1 && p.IsInHabitableZone);
        bool hasCarbonWorld = Array.Exists(planets, p => p.TypeCode == 8);
        bool hasHotInnerWorld = Array.Exists(planets, p => p.TypeCode is 5 or 10);
        bool hasHotGiant = Array.Exists(planets, p => p.TypeCode == 2 && p.OrbitAu < hzAu * 0.62f);
        int denseInnerCount = 0;
        int temperateCandidateCount = 0;
        int outerGiantCount = 0;
        int moonCount = 0;
        int icyOuterCount = 0;
        foreach (PlanetInfo planet in planets)
        {
            moonCount += planet.MoonCount;
            if (planet.IsInHabitableZone && planet.TypeCode is 0 or 1 or 6 or 9)
                temperateCandidateCount++;
            if (planet.OrbitAu < hzAu * 0.82f && planet.TypeCode is 0 or 6 or 8)
                denseInnerCount++;
            if (planet.OrbitAu > hzAu * 1.35f && planet.TypeCode is 2 or 3 or 7)
                outerGiantCount++;
            if (planet.TypeCode is 3 or 4 or 7)
                icyOuterCount++;
        }
        bool hasWideOuterSystem = planets.Length >= 6 && planets[^1].OrbitAu > hzAu * 5.2f;

        if (stellar.IsBinary)
            tags.Add(stellar.Layout == BinaryLayout.CircumbinaryCandidate ? "circumbinary-кандидат" : "двойная звездная система");

        if (hasHabitableCandidate && star.SpectralClass is "K" or "G" or "F")
            tags.Add("перспективная умеренная зона");
        else if (hasHabitableCandidate)
            tags.Add("потенциально умеренные миры");

        if (hasTemperateOcean)
            tags.Add("умеренные океанические миры");

        if (temperateCandidateCount >= 2)
            tags.Add("несколько умеренных кандидатов");

        if (hasRingedGiant)
            tags.Add("кольцевые гиганты");

        if (moonCount >= 8)
            tags.Add("богатые спутниковые семьи");

        if (moonCount >= 14 && outerGiantCount >= 2)
            tags.Add("крупная спутниковая экосистема");

        if (architecture is SystemArchitecture.Compact or SystemArchitecture.ResonantChain && planets.Length >= 5)
            tags.Add("плотная компактная система");

        if (architecture == SystemArchitecture.ResonantChain)
            tags.Add("резонансная цепочка");

        if (architecture == SystemArchitecture.HabitableRich)
            tags.Add("богатая умеренная зона");

        if (belts.Length > 0)
            tags.Add("пояса обломков");

        if (belts.Length >= 2)
            tags.Add("двойная поясная структура");

        if (hasCarbonWorld)
            tags.Add("углеродные планеты");

        if (hasHotInnerWorld)
            tags.Add("экстремальные внутренние миры");

        if (hasHotGiant)
            tags.Add("горячий гигант близко к звезде");

        if (architecture == SystemArchitecture.MigratedGiant)
            tags.Add("мигрировавший гигант");

        if (architecture == SystemArchitecture.MetalRich || denseInnerCount >= 3)
            tags.Add("металл-богатые внутренние миры");

        if (icyOuterCount >= 2)
            tags.Add("ледяная внешняя область");

        if (hasWideOuterSystem)
            tags.Add("широкая внешняя система");

        if (architecture == SystemArchitecture.HarshYoung || star.SpectralClass is "O" or "B" or "A" && star.AgeGyr < 1.8f)
            tags.Add("молодая яркая система");

        if (star.SpectralClass is "K" or "G" && star.AgeGyr is > 1.0f and < 8.5f)
            tags.Add("стабильная долгоживущая звезда");

        if (star.SpectralClass == "K" && star.AgeGyr >= 8.5f)
            tags.Add("древняя оранжевая система");

        if (architecture == SystemArchitecture.AncientStable)
            tags.Add("древняя стабильная архитектура");

        if (tags.Count == 0)
            tags.Add("обычная планетная архитектура");

        return tags.Count > 5 ? tags.GetRange(0, 5).ToArray() : tags.ToArray();
    }

    public static string[] InterestExplanations(SelectedStarInfo star)
    {
        PlanetInfo[] planets = Generate(star);
        AsteroidBeltInfo[] belts = GenerateAsteroidBelts(star, planets);
        var notes = new System.Collections.Generic.List<string>(6);
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
        float hzAu = EffectiveHabitableZoneAu(star, stellar);
        SystemArchitecture architecture = SelectArchitecture(star, stellar);

        bool hasHabitableCandidate = Array.Exists(planets, p => p.IsInHabitableZone && p.TypeCode is 0 or 1 or 6 or 9);
        bool hasRingedGiant = Array.Exists(planets, p => p.HasRings && p.TypeCode is 2 or 3);
        bool hasTemperateOcean = Array.Exists(planets, p => p.TypeCode == 1 && p.IsInHabitableZone);
        bool hasCarbonWorld = Array.Exists(planets, p => p.TypeCode == 8);
        bool hasHotInnerWorld = Array.Exists(planets, p => p.TypeCode is 5 or 10);
        bool hasHotGiant = Array.Exists(planets, p => p.TypeCode == 2 && p.OrbitAu < hzAu * 0.62f);
        int denseInnerCount = 0;
        int temperateCandidateCount = 0;
        int outerGiantCount = 0;
        int moonCount = 0;
        int icyOuterCount = 0;
        foreach (PlanetInfo planet in planets)
        {
            moonCount += planet.MoonCount;
            if (planet.IsInHabitableZone && planet.TypeCode is 0 or 1 or 6 or 9)
                temperateCandidateCount++;
            if (planet.OrbitAu < hzAu * 0.82f && planet.TypeCode is 0 or 6 or 8)
                denseInnerCount++;
            if (planet.OrbitAu > hzAu * 1.35f && planet.TypeCode is 2 or 3 or 7)
                outerGiantCount++;
            if (planet.TypeCode is 3 or 4 or 7)
                icyOuterCount++;
        }
        bool hasWideOuterSystem = planets.Length >= 6 && planets[^1].OrbitAu > hzAu * 5.2f;

        if (stellar.IsBinary)
            notes.Add($"- {stellar.Summary}: {stellar.Description}");

        if (hasHabitableCandidate && star.SpectralClass is "K" or "G" or "F")
            notes.Add("- Перспективная умеренная зона: есть каменистый, океанический или массивный земной кандидат около расчетной HZ.");
        else if (hasHabitableCandidate)
            notes.Add("- Потенциально умеренные миры: подходящие планеты есть, но класс звезды делает условия менее спокойными.");

        if (hasTemperateOcean)
            notes.Add("- Умеренные океанические миры: среди планет есть водный мир внутри расчетной зоны обитаемости.");

        if (temperateCandidateCount >= 2)
            notes.Add($"- Несколько умеренных кандидатов: подходящих планет около HZ найдено {temperateCandidateCount}.");

        if (hasRingedGiant)
            notes.Add("- Кольцевые гиганты: среди планет найден газовый или ледяной гигант с кольцами.");

        if (moonCount >= 8)
            notes.Add($"- Богатые спутниковые семьи: суммарно найдено спутников {moonCount}.");

        if (moonCount >= 14 && outerGiantCount >= 2)
            notes.Add($"- Крупная спутниковая экосистема: внешних гигантов {outerGiantCount}, суммарно спутников {moonCount}.");

        if ((architecture is SystemArchitecture.Compact or SystemArchitecture.ResonantChain) && planets.Length >= 5)
            notes.Add("- Плотная компактная система: орбиты сжаты, а число планет достаточно велико для тесной архитектуры.");

        if (architecture == SystemArchitecture.ResonantChain)
            notes.Add("- Резонансная цепочка: генератор выбрал почти регулярное орбитальное построение.");

        if (architecture == SystemArchitecture.HabitableRich)
            notes.Add("- Богатая умеренная зона: архитектура системы смещена к нескольким мирам около комфортной области.");

        if (belts.Length > 0)
            notes.Add($"- Пояса обломков: обнаружено поясов {belts.Length}, значит в системе осталось заметное количество малого материала.");

        if (belts.Length >= 2)
            notes.Add("- Двойная поясная структура: есть внутренний и внешний резервуар малого материала.");

        if (hasCarbonWorld)
            notes.Add("- Углеродные планеты: среди миров есть планеты с углеродной классификацией.");

        if (hasHotInnerWorld)
            notes.Add("- Экстремальные внутренние миры: найдены лавовые или раскаленные планеты на близких орбитах.");

        if (hasHotGiant)
            notes.Add("- Горячий гигант близко к звезде: массивная газовая планета находится внутри внутренней горячей области.");

        if (architecture == SystemArchitecture.MigratedGiant)
            notes.Add("- Мигрировавший гигант: архитектура намеренно ставит газовый гигант близко к звезде, создавая возмущенную внутреннюю систему.");

        if (architecture == SystemArchitecture.MetalRich || denseInnerCount >= 3)
            notes.Add($"- Металл-богатые внутренние миры: плотных каменистых или углеродных планет внутри HZ найдено {denseInnerCount}.");

        if (icyOuterCount >= 2)
            notes.Add($"- Ледяная внешняя область: внешних ледяных или холодных миров найдено {icyOuterCount}.");

        if (hasWideOuterSystem)
            notes.Add("- Широкая внешняя система: последняя планета находится далеко за умеренной зоной, формируя протяженную холодную область.");

        if (architecture == SystemArchitecture.HarshYoung || star.SpectralClass is "O" or "B" or "A" && star.AgeGyr < 1.8f)
            notes.Add("- Молодая яркая система: высокая светимость или молодой возраст усиливают жесткие условия.");

        if (star.SpectralClass is "K" or "G" && star.AgeGyr is > 1.0f and < 8.5f)
            notes.Add("- Стабильная долгоживущая звезда: класс и возраст дают сравнительно спокойное окно эволюции.");

        if (star.SpectralClass == "K" && star.AgeGyr >= 8.5f)
            notes.Add("- Древняя оранжевая система: возраст и класс K дают очень длинную, медленную историю эволюции.");

        if (architecture == SystemArchitecture.AncientStable)
            notes.Add("- Древняя стабильная архитектура: генератор выбрал старую спокойную компоновку с меньшей долей хаотичных молодых миров.");

        if (notes.Count == 0)
            notes.Add("- Обычная планетная архитектура: выраженных редких признаков в этой системе не найдено.");

        return notes.Count > 5 ? notes.GetRange(0, 5).ToArray() : notes.ToArray();
    }

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
        => Math.Clamp(0.46f + MathF.Pow(MathF.Max(habitableZoneAu, 0.03f), 0.86f) * 1.18f, 0.28f, 8.20f);

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
        if (architecture == SystemArchitecture.MigratedGiant)
        {
            if (index == 0 || orbitToHz < 0.55f && h > 0.62f)
                return 2;

            return orbitToHz < 0.88f
                ? h < 0.34f ? 5 : h < 0.58f ? 10 : h < 0.78f ? 8 : 6
                : h < 0.34f ? 3 : h < 0.56f ? 7 : h < 0.74f ? 4 : h < 0.90f ? 2 : 8;
        }

        if (architecture == SystemArchitecture.MetalRich && orbitToHz < 1.18f)
            return h < 0.30f ? 8 : h < 0.58f ? 0 : h < 0.78f ? 6 : h < 0.90f ? 5 : 10;

        if (architecture == SystemArchitecture.AncientStable)
        {
            if (inHz)
                return h < 0.32f ? 0 : h < 0.55f ? 9 : h < 0.74f ? 6 : h < 0.90f ? 1 : 4;

            return orbitToHz > 1.38f
                ? h < 0.35f ? 4 : h < 0.62f ? 3 : h < 0.78f ? 7 : h < 0.92f ? 2 : 8
                : h < 0.30f ? 0 : h < 0.54f ? 9 : h < 0.72f ? 6 : h < 0.88f ? 8 : 4;
        }

        if (architecture == SystemArchitecture.HarshYoung)
            return orbitToHz < 0.72f
                ? h < 0.42f ? 5 : h < 0.72f ? 10 : h < 0.90f ? 8 : 2
                : h < 0.36f ? 2 : h < 0.62f ? 3 : h < 0.78f ? 7 : h < 0.92f ? 8 : 4;
        if (architecture == SystemArchitecture.HabitableRich && orbitToHz is > 0.62f and < 1.48f)
            return h < 0.28f ? 0 : h < 0.52f ? 1 : h < 0.76f ? 6 : h < 0.90f ? 9 : 3;
        if (architecture == SystemArchitecture.ResonantChain && index < Math.Max(4, star.PlanetCount - 2))
            return h < 0.32f ? 0 : h < 0.58f ? 6 : h < 0.74f ? 1 : h < 0.88f ? 9 : 7;
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
        => SelectArchitecture(star, StarCatalog.CreateSystemProfile(star));

    private static SystemArchitecture SelectArchitecture(SelectedStarInfo star, StellarSystemProfile stellar)
    {
        float h = Hash01(star.Index * 3571 + 239);
        if (stellar.Layout == BinaryLayout.CircumbinaryCandidate)
            return h < 0.46f ? SystemArchitecture.ResonantChain : h < 0.76f ? SystemArchitecture.HabitableRich : SystemArchitecture.SolarLike;
        if (stellar.Layout == BinaryLayout.CloseBinary)
            return h < 0.48f ? SystemArchitecture.ResonantChain : h < 0.78f ? SystemArchitecture.Compact : SystemArchitecture.SolarLike;
        if (stellar.Layout == BinaryLayout.DisturbedYoungBinary)
            return h < 0.64f ? SystemArchitecture.DebrisHeavy : SystemArchitecture.HarshYoung;
        if (stellar.Layout == BinaryLayout.WideCompanion && h > 0.56f)
            return SystemArchitecture.DebrisHeavy;

        if (star.SpectralClass is "O" or "B")
            return h < 0.42f ? SystemArchitecture.HarshYoung : h < 0.68f ? SystemArchitecture.GiantRich : SystemArchitecture.DebrisHeavy;
        if (star.SpectralClass is "A" && h < 0.28f)
            return SystemArchitecture.HarshYoung;
        if (star.PlanetCount >= 4 && h > 0.94f)
            return SystemArchitecture.MigratedGiant;
        if (star.SpectralClass is "K" or "G" && star.AgeGyr > 8.4f && h > 0.68f)
            return SystemArchitecture.AncientStable;
        if (star.SpectralClass is "F" or "G" or "K" && h is > 0.88f and <= 0.94f)
            return SystemArchitecture.MetalRich;
        if (star.SpectralClass is "K" or "G" && h > 0.82f)
            return SystemArchitecture.HabitableRich;
        if (h < 0.16f)
            return SystemArchitecture.ResonantChain;
        if (h < 0.30f)
            return SystemArchitecture.Compact;
        if (h < 0.60f)
            return SystemArchitecture.SolarLike;
        if (h < 0.80f)
            return SystemArchitecture.GiantRich;
        return SystemArchitecture.DebrisHeavy;
    }

    private static float InitialOrbitFactor(SystemArchitecture architecture) => architecture switch
    {
        SystemArchitecture.Compact => 0.16f,
        SystemArchitecture.ResonantChain => 0.18f,
        SystemArchitecture.HabitableRich => 0.30f,
        SystemArchitecture.GiantRich => 0.28f,
        SystemArchitecture.DebrisHeavy => 0.20f,
        SystemArchitecture.HarshYoung => 0.12f,
        SystemArchitecture.MigratedGiant => 0.10f,
        SystemArchitecture.AncientStable => 0.24f,
        SystemArchitecture.MetalRich => 0.14f,
        _ => 0.22f,
    };

    private static float InitialOrbitFactor(SystemArchitecture architecture, StellarSystemProfile stellar)
    {
        float factor = InitialOrbitFactor(architecture);
        return stellar.Layout switch
        {
            BinaryLayout.CloseBinary => MathF.Max(factor, 0.36f),
            BinaryLayout.CircumbinaryCandidate => MathF.Max(factor, 0.46f),
            BinaryLayout.DisturbedYoungBinary => factor * 1.12f,
            BinaryLayout.WideCompanion => factor * 1.04f,
            _ => factor,
        };
    }

    private static float SpacingMultiplier(SystemArchitecture architecture) => architecture switch
    {
        SystemArchitecture.Compact => 0.76f,
        SystemArchitecture.ResonantChain => 0.68f,
        SystemArchitecture.HabitableRich => 0.92f,
        SystemArchitecture.GiantRich => 1.18f,
        SystemArchitecture.DebrisHeavy => 1.06f,
        SystemArchitecture.HarshYoung => 1.12f,
        SystemArchitecture.MigratedGiant => 1.22f,
        SystemArchitecture.AncientStable => 1.08f,
        SystemArchitecture.MetalRich => 0.82f,
        _ => 1.0f,
    };

    private static float OrbitGrowth(SystemArchitecture architecture, float h) => architecture switch
    {
        SystemArchitecture.Compact => 1.58f + h * 0.44f,
        SystemArchitecture.ResonantChain => 1.48f + h * 0.30f,
        SystemArchitecture.HabitableRich => 1.74f + h * 0.48f,
        SystemArchitecture.GiantRich => 2.35f + h * 0.92f,
        SystemArchitecture.DebrisHeavy => 2.02f + h * 0.76f,
        SystemArchitecture.HarshYoung => 2.18f + h * 0.88f,
        SystemArchitecture.MigratedGiant => 2.48f + h * 0.92f,
        SystemArchitecture.AncientStable => 1.92f + h * 0.62f,
        SystemArchitecture.MetalRich => 1.64f + h * 0.42f,
        _ => 2.18f + h * 0.88f,
    };

    private static float EffectiveHabitableZoneAu(SelectedStarInfo star, StellarSystemProfile stellar)
    {
        if (!stellar.IsBinary)
            return star.HabitableZoneAu;

        if (stellar.Layout is BinaryLayout.CloseBinary or BinaryLayout.CircumbinaryCandidate)
            return stellar.EffectiveHabitableZoneAu;

        float separation = MathF.Max(stellar.SeparationAu, star.HabitableZoneAu + 0.20f);
        float companionFlux = stellar.SecondaryLuminositySolar / MathF.Max(separation * separation, 0.04f);
        float targetFlux = Math.Clamp(1.0f - companionFlux, 0.32f, 1.0f);
        float primaryHz = MathF.Sqrt(MathF.Max(star.LuminositySolar, 0.015f) / targetFlux);
        float stabilityLimit = separation * 0.26f;
        return Math.Clamp(primaryHz, star.HabitableZoneAu * 0.92f, MathF.Max(star.HabitableZoneAu * 1.08f, stabilityLimit));
    }

    private static float MinimumStableOrbitAu(StellarSystemProfile stellar, float effectiveHzAu)
    {
        if (stellar.Layout == BinaryLayout.CloseBinary)
            return Math.Clamp(stellar.SeparationAu * 2.8f, 0.06f, effectiveHzAu * 0.70f);
        if (stellar.Layout == BinaryLayout.CircumbinaryCandidate)
            return Math.Clamp(stellar.SeparationAu * 3.6f, 0.08f, effectiveHzAu * 0.86f);
        return 0.06f;
    }

    private static float BinarySpacingMultiplier(StellarSystemProfile stellar) => stellar.Layout switch
    {
        BinaryLayout.CloseBinary => 1.10f,
        BinaryLayout.CircumbinaryCandidate => 1.18f,
        BinaryLayout.DisturbedYoungBinary => 1.20f,
        BinaryLayout.WideCompanion => 1.06f,
        _ => 1.0f,
    };

    private static float BinaryOrbitGrowthMultiplier(StellarSystemProfile stellar, int planetIndex) => stellar.Layout switch
    {
        BinaryLayout.CloseBinary => planetIndex < 2 ? 1.06f : 1.0f,
        BinaryLayout.CircumbinaryCandidate => planetIndex < 3 ? 1.10f : 1.02f,
        BinaryLayout.DisturbedYoungBinary => 1.08f,
        BinaryLayout.WideCompanion => planetIndex >= 4 ? 1.08f : 1.0f,
        _ => 1.0f,
    };

    private static float BinaryDebrisBonus(StellarSystemProfile stellar) => stellar.Layout switch
    {
        BinaryLayout.DisturbedYoungBinary => 0.22f,
        BinaryLayout.WideCompanion => 0.12f,
        BinaryLayout.CloseBinary => 0.06f,
        BinaryLayout.CircumbinaryCandidate => 0.08f,
        _ => 0f,
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

    private static string TypeName(int typeCode, float h0, float h1, bool inHz, float orbitToHz) => typeCode switch
    {
        0 => inHz
            ? h0 < 0.45f ? "Умеренная каменистая планета" : "Континентальный мир"
            : h0 < 0.34f ? "Железистая планета" : h0 < 0.68f ? "Базальтовая планета" : "Каменистая планета",
        1 => inHz
            ? h0 < 0.50f ? "Океанический мир" : "Теплый архипелаговый мир"
            : orbitToHz > 1.35f ? "Ледяной океанический мир" : "Облачный водный мир",
        2 => orbitToHz < 0.62f
            ? "Горячий газовый гигант"
            : h0 < 0.36f ? "Штормовой газовый гигант" : h0 < 0.72f ? "Полосатый газовый гигант" : "Холодный газовый гигант",
        3 => h0 < 0.45f ? "Метановый ледяной гигант" : "Аммиачный ледяной гигант",
        4 => h0 < 0.45f ? "Криогенная ледяная планета" : "Тундровая ледяная планета",
        5 => h0 < 0.50f ? "Лавовая планета" : "Приливно-расплавленный мир",
        6 => inHz
            ? "Умеренная суперземля"
            : h1 < 0.50f ? "Массивная суперземля" : "Скалистая суперземля",
        7 => h0 < 0.50f ? "Мини-нептун" : "Газовый карлик",
        8 => h0 < 0.48f ? "Углеродная планета" : "Алмазно-графитовый мир",
        9 => inHz ? "Умеренная пустынная планета" : "Сухой марсианский мир",
        10 => orbitToHz < 0.58f ? "Раскаленный парниковый мир" : "Плотный парниковый мир",
        _ => "Планета",
    };

    private static string DescriptionFor(int typeCode, bool inHz, bool hasRings, SystemArchitecture architecture, float orbitToHz)
    {
        string baseText = DescriptionFor(typeCode, inHz);
        string architectureText = architecture switch
        {
            SystemArchitecture.ResonantChain => " Орбита встроена в плотную регулярную цепочку, поэтому соседние планеты могут заметно влиять на долгосрочную устойчивость.",
            SystemArchitecture.HabitableRich when inHz => " Эта система богата мирами около умеренной зоны, так что климатическая история планеты особенно интересна для сравнения с соседями.",
            SystemArchitecture.GiantRich when typeCode is 2 or 3 or 7 => " Планета является частью массивной внешней архитектуры, где спутники и кольца важны почти так же, как сама планета.",
            SystemArchitecture.DebrisHeavy => " Окружение богато остаточным материалом, поэтому поверхность или кольца могли чаще получать ударные следы.",
            SystemArchitecture.HarshYoung when orbitToHz < 1.0f => " Молодая яркая среда усиливает нагрев, эрозию атмосферы и раннюю ударную историю.",
            SystemArchitecture.MigratedGiant when typeCode == 2 => " Близкая орбита газового гиганта указывает на миграцию, которая могла очистить или перестроить внутреннюю часть системы.",
            SystemArchitecture.MigratedGiant => " Орбита сформирована рядом с мигрировавшим гигантом, поэтому история планеты вероятно была динамически напряженной.",
            SystemArchitecture.AncientStable => " Долгая спокойная история делает планету хорошим объектом для следов древней эрозии, кратеров и медленной атмосферной эволюции.",
            SystemArchitecture.MetalRich when typeCode is 0 or 6 or 8 => " Металл-богатая среда повышает долю плотных пород, железа и углеродной химии.",
            _ => string.Empty,
        };
        return hasRings
            ? baseText + architectureText + " Вокруг планеты есть разреженная система колец из льда и пыли."
            : baseText + architectureText;
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

    private static string PlanetSubtypeNote(PlanetInfo planet)
    {
        string type = planet.Type;
        if (type.Contains("Континент", StringComparison.OrdinalIgnoreCase))
            return "Подтип указывает на сочетание суши и устойчивых атмосферных циклов; при достаточном давлении возможны выраженные климатические пояса.";
        if (type.Contains("архипелаг", StringComparison.OrdinalIgnoreCase))
            return "Архипелаговая поверхность предполагает мелкие океанические бассейны, много береговых зон и сильное влияние облачности.";
        if (type.Contains("Ледяной океан", StringComparison.OrdinalIgnoreCase))
            return "Водная оболочка вероятно скрыта под ледяной корой; наблюдаемая поверхность может быть яркой и трещиноватой.";
        if (type.Contains("Железист", StringComparison.OrdinalIgnoreCase))
            return "Железистый подтип означает высокую плотность и крупное ядро, поэтому магнитная и ударная история особенно важны.";
        if (type.Contains("Базальтов", StringComparison.OrdinalIgnoreCase))
            return "Базальтовая поверхность связана с древними лавовыми равнинами и слабой отражательной способностью.";
        if (type.Contains("Горячий газовый", StringComparison.OrdinalIgnoreCase))
            return "Горячий гигант должен испытывать сильное звездное излучение, атмосферное распухание и быстрые ветровые потоки.";
        if (type.Contains("Штормовой", StringComparison.OrdinalIgnoreCase))
            return "Штормовой гигант выделяется мощной атмосферной динамикой, долгоживущими вихрями и контрастными облачными поясами.";
        if (type.Contains("Газовый карлик", StringComparison.OrdinalIgnoreCase))
            return "Газовый карлик занимает промежуточное положение между суперземлей и мини-нептуном, поэтому его радиус чувствителен к потере атмосферы.";
        if (type.Contains("Алмазно", StringComparison.OrdinalIgnoreCase))
            return "Углеродная химия делает этот мир редким кандидатом на плотную карбидную кору и темные графитовые области.";
        if (type.Contains("Приливно", StringComparison.OrdinalIgnoreCase))
            return "Приливное плавление поддерживает молодую поверхность даже без массивной атмосферы.";

        return "Подтип уточняет физический сценарий планеты и помогает отличать похожие по базовому классу миры.";
    }

    private static string MoonSubtypeNote(MoonInfo moon)
    {
        if (moon.Type.Contains("Вулкан", StringComparison.OrdinalIgnoreCase))
            return "Вулканический подтип обычно связан с приливным нагревом и быстрым обновлением поверхности.";
        if (moon.Type.Contains("Океан", StringComparison.OrdinalIgnoreCase))
            return "Океанический ледяной подтип указывает на возможную жидкую прослойку под корой и активные трещинные области.";
        if (moon.Type.Contains("Захвач", StringComparison.OrdinalIgnoreCase))
            return "Захваченный спутник может иметь наклоненную или вытянутую орбиту и состав, отличный от регулярных лун планеты.";
        if (moon.Type.Contains("Обожж", StringComparison.OrdinalIgnoreCase))
            return "Обожженная поверхность вероятно темнее и суше из-за близости системы к звезде.";

        return "Подтип спутника задает его вероятную историю: регулярное формирование, захват, ледяная эволюция или приливная переработка.";
    }

    private static (string Type, string Description, bool IsIcy) MoonProfile(SelectedStarInfo star, PlanetInfo parent, int moonIndex, float h0, float h1, float h2)
    {
        bool aroundGiant = parent.TypeCode is 2 or 3 or 7;
        float hzAu = EffectiveHabitableZoneAu(star);
        bool innerHot = parent.OrbitAu < hzAu * 0.72f;
        bool outerCold = parent.OrbitAu > hzAu * 1.35f;

        if (aroundGiant && moonIndex == 0 && h0 < 0.30f && !outerCold)
        {
            return (
                "Вулканический спутник",
                "Плотный внутренний спутник с приливным нагревом, темными лавовыми равнинами и активной молодой поверхностью.",
                false);
        }

        if (aroundGiant && h1 < 0.34f)
        {
            return (
                "Океанический ледяной спутник",
                "Ледяная кора может скрывать подповерхностный океан; приливные напряжения поддерживают трещины и возможные криовулканические области.",
                true);
        }

        if (h2 > 0.82f)
        {
            return (
                "Захваченный малый спутник",
                "Небольшое неправильное тело с темной поверхностью; вероятно, оно было захвачено уже после формирования планеты.",
                false);
        }

        if (outerCold || h1 < 0.46f)
        {
            return (
                "Ледяной спутник",
                "Небольшое холодное тело с ледяной корой, кратерами и слабой отражающей поверхностью.",
                true);
        }

        if (innerHot)
        {
            return (
                "Обожженный каменистый спутник",
                "Каменистая поверхность темнее обычного из-за близости к звезде и многократной термической переработки.",
                false);
        }

        return (
            "Каменистый спутник",
            "Небольшой каменистый спутник с кратерированной поверхностью и слабой собственной геологией.",
            false);
    }

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
