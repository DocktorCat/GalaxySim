using ComputeSharp;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalaxySim.Core.Simulation;

public sealed class GalaxySearchIndex
{
    public static GalaxySearchIndex Empty { get; } = new(0, [], []);

    private sealed record SearchProfile(
        bool HasHabitablePlanet,
        bool HasRingedGiant,
        StellarSystemProfile StellarProfile,
        string[] InterestTags,
        string InterestSummary);

    private readonly Float4[] _positions;
    private readonly Float4[] _velocities;
    private readonly StarSearchEntry?[] _entryCache;
    private readonly SearchProfile?[] _profileCache;

    public GalaxySearchIndex(int galaxySeed, Float4[] positions, Float4[] velocities)
    {
        GalaxySeed = galaxySeed;
        _positions = positions;
        _velocities = velocities;
        Count = Math.Min(positions.Length, velocities.Length);
        _entryCache = new StarSearchEntry?[Count];
        _profileCache = new SearchProfile?[Count];
    }

    public int GalaxySeed { get; }

    public int Count { get; }

    public StarSearchEntry? GetEntry(int starIndex)
    {
        if (starIndex < 0 || starIndex >= Count)
            return null;

        return GetEntry(starIndex, CreateStar(starIndex));
    }

    public IReadOnlyList<StarSearchEntry> Search(SearchSettingsState settings, int maxResults = 80)
    {
        maxResults = Math.Clamp(maxResults, 1, 512);
        var results = new List<StarSearchEntry>(Math.Min(maxResults, 64));
        string? query = string.IsNullOrWhiteSpace(settings.Query)
            ? null
            : settings.Query.Trim();
        string? spectralClass = string.IsNullOrWhiteSpace(settings.SpectralClass)
            ? null
            : settings.SpectralClass.Trim();
        string? interestTrait = string.IsNullOrWhiteSpace(settings.InterestTrait)
            ? null
            : settings.InterestTrait.Trim();
        string? binaryFilter = string.IsNullOrWhiteSpace(settings.BinaryFilter)
            ? null
            : settings.BinaryFilter.Trim();

        for (int i = 0; i < Count; i++)
        {
            SelectedStarInfo star = CreateStar(i);

            if (spectralClass is not null &&
                !string.Equals(star.SpectralClass, spectralClass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (settings.MinimumPlanetCount > 0 && star.PlanetCount < settings.MinimumPlanetCount)
                continue;

            if (query is not null &&
                !star.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !star.Type.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !star.SystemKind.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SearchProfile? profile = settings.HabitableSystemsOnly || settings.RingedGiantsOnly || interestTrait is not null || binaryFilter is not null
                ? GetProfile(star)
                : null;

            if ((settings.HabitableSystemsOnly || settings.RingedGiantsOnly) && !MatchesPlanetFilters(profile!, settings))
                continue;

            if (interestTrait is not null && !MatchesInterestTrait(profile!, interestTrait))
                continue;

            if (binaryFilter is not null && !MatchesBinaryFilter(profile!, binaryFilter))
                continue;

            results.Add(GetEntry(i, star));
        }

        SortResults(results, settings);
        if (results.Count > maxResults)
            results.RemoveRange(maxResults, results.Count - maxResults);

        return results;
    }

    private static void SortResults(List<StarSearchEntry> results, SearchSettingsState settings)
    {
        switch (settings.SortMode)
        {
            case "relevance":
                results.Sort((a, b) =>
                {
                    int relevance = SearchRelevanceScore(b, settings).CompareTo(SearchRelevanceScore(a, settings));
                    return relevance != 0 ? relevance : a.Index.CompareTo(b.Index);
                });
                break;
            case "planets_desc":
                results.Sort((a, b) => b.PlanetCount.CompareTo(a.PlanetCount));
                break;
            case "hz_asc":
                results.Sort((a, b) => a.HabitableZoneAu.CompareTo(b.HabitableZoneAu));
                break;
            case "luminosity_desc":
                results.Sort((a, b) => b.LuminositySolar.CompareTo(a.LuminositySolar));
                break;
            case "mass_desc":
                results.Sort((a, b) => b.SolarMass.CompareTo(a.SolarMass));
                break;
            case "class":
                results.Sort((a, b) => string.Compare(a.SpectralClass, b.SpectralClass, StringComparison.Ordinal));
                break;
            default:
                results.Sort((a, b) => a.Index.CompareTo(b.Index));
                break;
        }
    }

    private static int SearchRelevanceScore(StarSearchEntry entry, SearchSettingsState settings)
    {
        int score = Math.Min(entry.PlanetCount, 12);

        if (!string.IsNullOrWhiteSpace(settings.Query))
        {
            string query = settings.Query.Trim();
            if (entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                score += 80;
            else if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += 55;

            if (entry.Type.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += 25;

            if (entry.SystemKind.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += 25;

            if (entry.StellarSummary.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += 25;
        }

        if (!string.IsNullOrWhiteSpace(settings.SpectralClass) &&
            string.Equals(entry.SpectralClass, settings.SpectralClass, StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (!string.IsNullOrWhiteSpace(settings.InterestTrait))
        {
            string trait = settings.InterestTrait.Trim();
            if (entry.TraitsSummary.Contains(trait, StringComparison.OrdinalIgnoreCase))
                score += 70;
        }

        if (settings.HabitableSystemsOnly)
        {
            if (entry.TraitsSummary.Contains("умеренн", StringComparison.OrdinalIgnoreCase))
                score += 45;

            score += Math.Max(0, 18 - (int)MathF.Round(MathF.Abs(entry.HabitableZoneAu - 1f) * 8f));
        }

        if (settings.RingedGiantsOnly &&
            entry.TraitsSummary.Contains("кольцевые гиганты", StringComparison.OrdinalIgnoreCase))
        {
            score += 45;
        }

        if (!string.IsNullOrWhiteSpace(settings.BinaryFilter) && settings.BinaryFilter != "any")
            score += 35;

        return score;
    }

    private static bool MatchesInterestTrait(SearchProfile profile, string interestTrait)
        => Array.Exists(
            profile.InterestTags,
            tag => tag.Contains(interestTrait, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesPlanetFilters(SearchProfile profile, SearchSettingsState settings)
    {
        if (settings.HabitableSystemsOnly && !profile.HasHabitablePlanet)
            return false;

        if (settings.RingedGiantsOnly && !profile.HasRingedGiant)
            return false;

        return true;
    }

    private static bool MatchesBinaryFilter(SearchProfile profile, string binaryFilter)
    {
        StellarSystemProfile stellar = profile.StellarProfile;
        return binaryFilter switch
        {
            "binary" => stellar.IsBinary,
            "close" => stellar.Layout == BinaryLayout.CloseBinary,
            "wide" => stellar.Layout == BinaryLayout.WideCompanion,
            "circumbinary" => stellar.Layout == BinaryLayout.CircumbinaryCandidate,
            "disturbed" => stellar.Layout == BinaryLayout.DisturbedYoungBinary,
            "single" => !stellar.IsBinary,
            _ => true,
        };
    }

    private StarSearchEntry GetEntry(int index, SelectedStarInfo star)
    {
        StarSearchEntry? cached = _entryCache[index];
        if (cached is not null)
            return cached;

        StarSearchEntry entry = CreateEntry(star);
        _entryCache[index] = entry;
        return entry;
    }

    private SearchProfile GetProfile(SelectedStarInfo star)
    {
        SearchProfile? cached = _profileCache[star.Index];
        if (cached is not null)
            return cached;

        PlanetInfo[] planets = SolarSystemCatalog.Generate(star);
        AsteroidBeltInfo[] belts = SolarSystemCatalog.GenerateAsteroidBelts(star, planets);
        string[] tags = SolarSystemCatalog.InterestTags(star, planets, belts);
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
        var profile = new SearchProfile(
            Array.Exists(planets, p => p.IsInHabitableZone),
            Array.Exists(planets, p => p.HasRings && p.TypeCode is 2 or 3),
            stellar,
            tags,
            string.Join(", ", tags));
        _profileCache[star.Index] = profile;
        return profile;
    }

    private SelectedStarInfo CreateStar(int index)
    {
        Float4 pos = _positions[index];
        Float4 vel = _velocities[index];
        float temp = Math.Clamp(vel.W, 0f, 1f);
        return StarCatalog.Create(
            index,
            new Vector3(pos.X, pos.Y, pos.Z),
            new Vector3(vel.X, vel.Y, vel.Z),
            temp,
            pos.W);
    }

    private StarSearchEntry CreateEntry(SelectedStarInfo star) => new(
        GalaxyObjectId.ForStar(GalaxySeed, star.Index),
        star.Index,
        star.Name,
        star.SpectralClass,
        star.Type,
        star.SystemKind,
        GetProfile(star).StellarProfile.Summary,
        star.PlanetCount,
        SolarSystemCatalog.EffectiveHabitableZoneAu(star),
        star.LuminositySolar,
        star.SolarMass,
        GetProfile(star).InterestSummary,
        star.Position);
}
