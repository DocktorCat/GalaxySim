using ComputeSharp;
using GalaxySim.Core.Physics;

namespace GalaxySim.Core.Simulation;

public enum GalaxyPresetType
{
    MilkyWay,
    Andromeda,
    NGC1300,
    M74Pinwheel,
    DwarfGalaxy,
}

public class GalaxyPreset
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    public float HaloV0 { get; init; } = 1.4f;
    public float HaloCoreRadius { get; init; } = 1.5f;
    public float BlackHoleMass { get; init; } = 4f;
    public float Gravity { get; init; } = 0.5f;

    public float ScaleLength { get; init; } = 3.0f;
    public float ScaleHeight { get; init; } = 0.22f;
    public float SigmaR { get; init; } = 0.18f;
    public float SigmaZ { get; init; } = 0.035f;
    public float DiskMass { get; init; } = 5f;

    public float BulgeFraction { get; init; } = 0.15f;

    public bool UseTwoComponentDisk { get; init; } = true;
    public float ThinDiskFraction { get; init; } = 0.82f;
    public float ThickScaleLengthMultiplier { get; init; } = 1.20f;
    public float ThickScaleHeightMultiplier { get; init; } = 2.60f;
    public float ThickSigmaRMultiplier { get; init; } = 1.25f;
    public float ThickSigmaZMultiplier { get; init; } = 2.40f;

    public static GalaxyPreset Get(GalaxyPresetType type) => type switch
    {
        GalaxyPresetType.MilkyWay => new GalaxyPreset
        {
            Name = "Млечный Путь",
            Description = "Спиральная галактика, умеренный halo, яркое ядро",
            HaloV0 = 1.4f,
            HaloCoreRadius = 1.5f,
            BlackHoleMass = 4f,
            ScaleLength = 3.0f,
            ScaleHeight = 0.22f,
            SigmaR = 0.18f,
            SigmaZ = 0.035f,
            DiskMass = 5f,
            BulgeFraction = 0.18f,
            UseTwoComponentDisk = true,
            ThinDiskFraction = 0.84f,
            ThickScaleLengthMultiplier = 1.20f,
            ThickScaleHeightMultiplier = 2.60f,
            ThickSigmaRMultiplier = 1.20f,
            ThickSigmaZMultiplier = 2.30f,
        },

        GalaxyPresetType.Andromeda => new GalaxyPreset
        {
            Name = "Андромеда (M31)",
            Description = "Крупнее Млечного Пути, большой балдж и массивный halo",
            HaloV0 = 1.7f,
            HaloCoreRadius = 2.0f,
            BlackHoleMass = 8f,
            ScaleLength = 4.0f,
            ScaleHeight = 0.28f,
            SigmaR = 0.20f,
            SigmaZ = 0.04f,
            DiskMass = 7f,
            BulgeFraction = 0.25f,
            UseTwoComponentDisk = true,
            ThinDiskFraction = 0.78f,
            ThickScaleLengthMultiplier = 1.22f,
            ThickScaleHeightMultiplier = 2.40f,
            ThickSigmaRMultiplier = 1.30f,
            ThickSigmaZMultiplier = 2.10f,
        },

        GalaxyPresetType.NGC1300 => new GalaxyPreset
        {
            Name = "NGC 1300 (барная)",
            Description = "Барная спиральная галактика, вытянутое ядро",
            HaloV0 = 1.3f,
            HaloCoreRadius = 1.2f,
            BlackHoleMass = 6f,
            ScaleLength = 3.5f,
            ScaleHeight = 0.18f,
            SigmaR = 0.25f,
            SigmaZ = 0.03f,
            DiskMass = 5f,
            BulgeFraction = 0.12f,
            UseTwoComponentDisk = true,
            ThinDiskFraction = 0.86f,
            ThickScaleLengthMultiplier = 1.15f,
            ThickScaleHeightMultiplier = 2.20f,
            ThickSigmaRMultiplier = 1.35f,
            ThickSigmaZMultiplier = 2.00f,
        },

        GalaxyPresetType.M74Pinwheel => new GalaxyPreset
        {
            Name = "M74 Вертушка",
            Description = "Face-on спиральная, мягкая структура, небольшой балдж",
            HaloV0 = 1.2f,
            HaloCoreRadius = 1.8f,
            BlackHoleMass = 3f,
            ScaleLength = 3.2f,
            ScaleHeight = 0.16f,
            SigmaR = 0.14f,
            SigmaZ = 0.03f,
            DiskMass = 4.5f,
            BulgeFraction = 0.10f,
            UseTwoComponentDisk = true,
            ThinDiskFraction = 0.90f,
            ThickScaleLengthMultiplier = 1.18f,
            ThickScaleHeightMultiplier = 2.40f,
            ThickSigmaRMultiplier = 1.18f,
            ThickSigmaZMultiplier = 2.20f,
        },

        GalaxyPresetType.DwarfGalaxy => new GalaxyPreset
        {
            Name = "Карликовая галактика",
            Description = "Малая масса, диффузный диск, слабый halo",
            HaloV0 = 0.7f,
            HaloCoreRadius = 1.0f,
            BlackHoleMass = 0.5f,
            ScaleLength = 1.8f,
            ScaleHeight = 0.25f,
            SigmaR = 0.30f,
            SigmaZ = 0.08f,
            DiskMass = 1.5f,
            BulgeFraction = 0.05f,
            UseTwoComponentDisk = true,
            ThinDiskFraction = 0.65f,
            ThickScaleLengthMultiplier = 1.28f,
            ThickScaleHeightMultiplier = 2.80f,
            ThickSigmaRMultiplier = 1.45f,
            ThickSigmaZMultiplier = 2.60f,
        },

        _ => Get(GalaxyPresetType.MilkyWay),
    };
}
