using System;

namespace GalaxySim.Core.Simulation;

public static class CelestialNameGenerator
{
    private static readonly string[] GreekPrefixes =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta",
        "Iota", "Kappa", "Lambda", "Mu", "Nu", "Xi", "Omicron", "Pi",
        "Rho", "Sigma", "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega"
    ];

    private static readonly string[] StarClassicRoots =
    [
        "Centavra", "Asterion", "Elaris", "Vireon", "Orvella", "Caldris",
        "Novera", "Tessara", "Lythera", "Aurel", "Syrion", "Velorum",
        "Cygara", "Altairis", "Meridia", "Rhelion", "Vespera", "Caelora",
        "Arctora", "Bellatrixa", "Denebria", "Mizaris", "Rigelia", "Sadrion",
        "Nashira", "Sabik", "Talitha", "Vindemi", "Zuben", "Alhena"
    ];

    private static readonly string[] StarRegions =
    [
        "Aquilae", "Aurigae", "Caeli", "Carinae", "Cassiae", "Cygni",
        "Draconis", "Eridani", "Fornacis", "Lacertae", "Lyrae", "Orionis",
        "Pegasi", "Persei", "Phoenicis", "Pictoris", "Serpentis", "Tauri",
        "Velorum", "Virginis", "Volantis", "Vulpeculae"
    ];

    private static readonly string[] NameStarts =
    [
        "A", "Ae", "Al", "An", "Ar", "As", "Ba", "Bel", "Ca", "Cal", "Ce", "Cy",
        "Da", "Del", "E", "El", "En", "Er", "Fa", "Fen", "Ha", "Hel", "Ia", "Il",
        "Ka", "Kel", "La", "Lyr", "Ma", "Mer", "Na", "Nor", "O", "Or", "Pa", "Per",
        "Ra", "Rel", "Sa", "Ser", "Ta", "Tor", "Ul", "Ur", "Va", "Vel", "Xa", "Zor"
    ];

    private static readonly string[] NameMiddles =
    [
        "bar", "bel", "bor", "cal", "car", "cer", "dan", "del", "dor", "far",
        "fel", "gal", "har", "ion", "jor", "kal", "lan", "lar", "len", "lor",
        "mar", "mer", "mir", "mor", "nar", "nel", "nor", "phor", "quor", "ran",
        "rel", "rian", "ros", "sai", "sel", "ser", "sor", "tar", "ther", "tor",
        "val", "var", "ven", "vor", "xan", "yor", "zen", "zor"
    ];

    private static readonly string[] NameEndings =
    [
        "a", "ae", "an", "ara", "aris", "ea", "eon", "era", "eron", "ia",
        "ion", "is", "ium", "on", "ora", "os", "um", "ura", "ys", "yx",
        "alis", "arae", "eronis", "oria", "yra", "alon", "eth", "oriae", "aris", "or"
    ];

    private static readonly string[] PlanetClassicRoots =
    [
        "Aurelia", "Nereon", "Velora", "Ilyria", "Caldara", "Thalassa",
        "Rovaris", "Meridian", "Solenne", "Eosara", "Virelia", "Nixara",
        "Orthea", "Lunaris", "Elarion", "Cyrene", "Sorelia", "Asteron",
        "Aldora", "Boreas", "Cirelia", "Damaris", "Ephara", "Galatea",
        "Heloria", "Iskera", "Jovara", "Kaelus", "Lysoria", "Mireva",
        "Norion", "Ophira", "Pheron", "Quessara", "Ravelle", "Saphira"
    ];

    private static readonly string[] RockySuffixes = ["Prime", "Major", "Minor", "Secundus", "Tertia", "Vesta", "Astra", "Regis", "Tellus", "Cera"];
    private static readonly string[] OceanSuffixes = ["Mare", "Pelagia", "Thalor", "Aqua", "Nerissa", "Maris", "Lacus", "Nereid", "Oceana", "Azure"];
    private static readonly string[] GiantSuffixes = ["Magnus", "Jovia", "Kronos", "Aurelion", "Borealis", "Titan", "Regulus", "Hyperion", "Aegis", "Colossus"];
    private static readonly string[] IceSuffixes = ["Glacia", "Nivalis", "Cryon", "Umbra", "Hesper", "Frost", "Boreal", "Noctis", "Rime", "Nix"];
    private static readonly string[] LavaSuffixes = ["Ignis", "Vulcan", "Pyra", "Ember", "Caldera", "Ashen", "Cinder", "Scoria", "Ferro", "Sulfur"];
    private static readonly string[] SuperEarthSuffixes = ["Tellurion", "Gaia", "Atlas", "Massif", "Craton", "Highland", "Terranova", "Gravis", "Mantle", "Aegira"];
    private static readonly string[] MiniNeptuneSuffixes = ["Nimbara", "Zephyr", "Aeria", "Vaporis", "Nephele", "Mist", "Paleon", "Caelum", "Drift", "Auralis"];
    private static readonly string[] CarbonSuffixes = ["Onyx", "Graphis", "Noira", "Carbora", "Diamond", "Umbriel", "Obsidian", "Cinderis", "Jet", "Tenebra"];
    private static readonly string[] DesertSuffixes = ["Dune", "Sahara", "Aridia", "Sirocco", "Mirage", "Mesa", "Kharif", "Saffron", "Dust", "Solum"];
    private static readonly string[] GreenhouseSuffixes = ["Venara", "Sulphur", "Haze", "Caustis", "Acrid", "Fumar", "Vesper", "Miasma", "Brume", "Pyroclast"];

    private static readonly string[] PlanetEpithets =
    [
        "Reach", "Crown", "Vale", "Harbor", "Gate", "Basin", "March", "Deep",
        "Rise", "Shore", "Field", "Forge", "Haven", "Veil", "Cradle", "Frontier"
    ];

    private static readonly string[] MoonRoots =
    [
        "Lys", "Mira", "Kora", "Nai", "Thea", "Rhea", "Dione", "Elara",
        "Vesta", "Ione", "Nyx", "Cira", "Astra", "Luma", "Seren", "Ophel",
        "Kaly", "Meno", "Ravo", "Sino", "Teth", "Yara", "Zira", "Oryn"
    ];

    private static readonly string[] MoonSuffixes =
    [
        "ia", "on", "is", "a", "e", "ara", "ora", "ion", "ea", "aris",
        "eth", "orae", "yn", "os", "elle", "ionis"
    ];

    private static readonly string[] MoonEpithets =
    [
        "Boreal", "Umbra", "Pale", "Outer", "Inner", "Silent", "Ash", "Glass",
        "Rime", "Dust", "Dawn", "Night", "Low", "High"
    ];

    public static string StarName(int index, string spectralClass)
    {
        int seed = index * 4099 + SpectralSeed(spectralClass) * 131;
        string prefix = GreekPrefixes[Pick(seed, GreekPrefixes.Length)];
        string root = Hash01(seed + 41) > 0.32f
            ? ProceduralRoot(seed * 17 + 5, uppercase: true)
            : StarClassicRoots[Pick(seed * 17 + 5, StarClassicRoots.Length)];

        float pattern = Hash01(seed + 83);
        if (pattern < 0.56f)
            return $"{prefix} {root}";
        if (pattern < 0.82f)
            return $"{root} {StarRegions[Pick(seed * 29 + 13, StarRegions.Length)]}";
        return $"{prefix} {root} {StarRegions[Pick(seed * 31 + 19, StarRegions.Length)]}";
    }

    public static string PlanetName(int starIndex, int planetIndex, int typeCode)
    {
        int seed = starIndex * 7919 + planetIndex * 3571 + typeCode * 173;
        string root = Hash01(seed + 37) > 0.25f
            ? ProceduralRoot(seed, uppercase: true)
            : PlanetClassicRoots[Pick(seed, PlanetClassicRoots.Length)];
        string suffix = PlanetSuffix(typeCode, seed);
        if (Hash01(seed + 89) > 0.68f)
            return $"{root} {suffix} {PlanetEpithets[Pick(seed * 43 + 17, PlanetEpithets.Length)]}";
        return $"{root} {suffix}";
    }

    public static string MoonName(int starIndex, int parentPlanetIndex, int moonIndex, bool isIcy)
    {
        int seed = starIndex * 6151 + parentPlanetIndex * 1291 + moonIndex * 373 + (isIcy ? 97 : 31);
        string root = Hash01(seed + 23) > 0.38f
            ? CompactRoot(seed)
            : MoonRoots[Pick(seed, MoonRoots.Length)];
        string suffix = MoonSuffixes[Pick(seed * 19 + 11, MoonSuffixes.Length)];
        string modifier = Hash01(seed + 41) > 0.62f
            ? $" {MoonEpithets[Pick(seed * 31 + 7, MoonEpithets.Length)]}"
            : string.Empty;
        return $"{root}{suffix}{modifier}";
    }

    public static int EstimatedVisibleNameSpace
        => GreekPrefixes.Length * NameStarts.Length * NameMiddles.Length * NameMiddles.Length * NameEndings.Length;

    private static string PlanetSuffix(int typeCode, int seed)
    {
        string[] pool = typeCode switch
        {
            1 => OceanSuffixes,
            2 => GiantSuffixes,
            3 or 4 => IceSuffixes,
            5 => LavaSuffixes,
            6 => SuperEarthSuffixes,
            7 => MiniNeptuneSuffixes,
            8 => CarbonSuffixes,
            9 => DesertSuffixes,
            10 => GreenhouseSuffixes,
            _ => RockySuffixes,
        };

        return pool[Pick(seed * 23 + 7, pool.Length)];
    }

    private static int SpectralSeed(string spectralClass) => spectralClass switch
    {
        "O" => 7,
        "B" => 6,
        "A" => 5,
        "F" => 4,
        "G" => 3,
        "K" => 2,
        "M" => 1,
        _ => 0,
    };

    private static int Pick(int seed, int count)
        => Math.Clamp((int)(Hash01(seed) * count), 0, count - 1);

    private static string ProceduralRoot(int seed, bool uppercase)
    {
        string start = NameStarts[Pick(seed + 3, NameStarts.Length)];
        string middleA = NameMiddles[Pick(seed * 7 + 11, NameMiddles.Length)];
        string middleB = Hash01(seed + 97) > 0.55f
            ? NameMiddles[Pick(seed * 13 + 17, NameMiddles.Length)]
            : string.Empty;
        string ending = NameEndings[Pick(seed * 19 + 23, NameEndings.Length)];
        string root = $"{start}{middleA}{middleB}{ending}";
        return uppercase ? Capitalize(root) : root.ToLowerInvariant();
    }

    private static string CompactRoot(int seed)
    {
        string start = NameStarts[Pick(seed + 5, NameStarts.Length)];
        string middle = NameMiddles[Pick(seed * 11 + 3, NameMiddles.Length)];
        string ending = MoonSuffixes[Pick(seed * 17 + 13, MoonSuffixes.Length)];
        return Capitalize($"{start}{middle}{ending}");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string lower = value.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
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
