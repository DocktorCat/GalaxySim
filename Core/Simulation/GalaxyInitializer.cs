using System;
using ComputeSharp;
using GalaxySim.Core.Physics;

namespace GalaxySim.Core.Simulation;

public static class GalaxyInitializer
{
    private const float NfwAtScaleRadius = 0.19314718056f;

    public static (Float4[] pos, Float4[] vel) ExponentialDisk(
        int n,
        GalaxyParams p,
        float scaleLength = 3.0f,
        float scaleHeight = 0.22f,
        float sigmaR = 0.22f,
        float sigmaZ = 0.035f,
        float diskTotalMass = 5f,
        bool useTwoComponentDisk = false,
        float thinDiskFraction = 0.82f,
        float thickScaleLengthMultiplier = 1.20f,
        float thickScaleHeightMultiplier = 2.60f,
        float thickSigmaRMultiplier = 1.25f,
        float thickSigmaZMultiplier = 2.40f,
        float haloV0Scale = 1f,
        float bhMassScale = 1f,
        float bhSofteningScale = 1f,
        float bulgeFraction = 0f,
        float bulgeScale = 0.35f,
        float bulgeDispersion = 0.45f,
        int seed = 42)
    {
        var rng = new Random(seed);
        var pos = new Float4[n];
        var vel = new Float4[n];
        float mPerStar = diskTotalMass / n;

        float haloV0 = p.HaloV0 * haloV0Scale;
        float bhMass = p.BlackHoleMass * bhMassScale;
        float bhSoft = EffectiveBlackHoleSoftening(p.BlackHoleSoftening * bhSofteningScale, bhMass);
        float clampedBulgeFraction = Math.Clamp(bulgeFraction, 0f, 0.95f);
        float clampedThinFraction = Math.Clamp(thinDiskFraction, 0.05f, 0.98f);
        float clampedThickScaleLengthMultiplier = Math.Clamp(thickScaleLengthMultiplier, 1.0f, 4.0f);
        float clampedThickScaleHeightMultiplier = Math.Clamp(thickScaleHeightMultiplier, 1.0f, 8.0f);
        float clampedThickSigmaRMultiplier = Math.Clamp(thickSigmaRMultiplier, 1.0f, 6.0f);
        float clampedThickSigmaZMultiplier = Math.Clamp(thickSigmaZMultiplier, 1.0f, 8.0f);

        for (int i = 0; i < n; i++)
        {
            bool isBulge = clampedBulgeFraction > 0f && (float)rng.NextDouble() < clampedBulgeFraction;

            float x, y, z;
            float vx, vy, vZ;
            float r;

            if (isBulge)
            {
                float u1 = 1f - (float)rng.NextDouble();
                float u2 = 1f - (float)rng.NextDouble();
                float u3 = 1f - (float)rng.NextDouble();
                float rBulge = bulgeScale * (-MathF.Log(u1) - MathF.Log(u2) - MathF.Log(u3));

                float cosTheta = 2f * (float)rng.NextDouble() - 1f;
                float sinTheta = MathF.Sqrt(MathF.Max(1f - cosTheta * cosTheta, 0f));
                float phi3d = (float)(rng.NextDouble() * Math.PI * 2);

                x = rBulge * sinTheta * MathF.Cos(phi3d);
                y = rBulge * sinTheta * MathF.Sin(phi3d);
                z = rBulge * cosTheta * 0.6f;

                r = MathF.Sqrt(x * x + y * y) + 1e-4f;
                float vHaloSqBulge = NfwVcircSq(r, haloV0, p.HaloCoreRadius);
                float vBhSqBulge = p.Gravity * bhMass * r * r
                                    / MathF.Pow(r * r + bhSoft * bhSoft, 1.5f);
                float sigmaBulge = bulgeDispersion * MathF.Sqrt(MathF.Max(vHaloSqBulge + vBhSqBulge, 1e-6f));

                vx = GaussNoise(rng) * sigmaBulge;
                vy = GaussNoise(rng) * sigmaBulge;
                vZ = GaussNoise(rng) * sigmaBulge * 0.9f;
            }
            else
            {
                bool isThin = !useTwoComponentDisk || (float)rng.NextDouble() < clampedThinFraction;
                float localScaleLength = isThin
                    ? scaleLength
                    : scaleLength * clampedThickScaleLengthMultiplier;
                float localScaleHeight = isThin
                    ? scaleHeight
                    : scaleHeight * clampedThickScaleHeightMultiplier;
                float localSigmaR = isThin
                    ? sigmaR
                    : sigmaR * clampedThickSigmaRMultiplier;
                float localSigmaZ = isThin
                    ? sigmaZ
                    : sigmaZ * clampedThickSigmaZMultiplier;
                float localDiskMass = diskTotalMass * (isThin ? clampedThinFraction : (1f - clampedThinFraction));

                float u1 = 1f - (float)rng.NextDouble();
                float u2 = 1f - (float)rng.NextDouble();
                r = -localScaleLength * 0.5f * (MathF.Log(u1) + MathF.Log(u2));
                r = Math.Max(r, 1e-3f);

                float phi = (float)(rng.NextDouble() * Math.PI * 2);
                float zu = Math.Clamp(2f * (float)rng.NextDouble() - 1f, -0.999f, 0.999f);
                z = localScaleHeight * 0.5f * MathF.Log((1 + zu) / (1 - zu));

                x = r * MathF.Cos(phi);
                y = r * MathF.Sin(phi);

                float vHaloSq = NfwVcircSq(r, haloV0, p.HaloCoreRadius);
                float vBhSq = p.Gravity * bhMass * r * r
                                / MathF.Pow(r * r + bhSoft * bhSoft, 1.5f);
                float x_ = r / localScaleLength;
                float mEnc = localDiskMass * (1f - (1f + x_) * MathF.Exp(-x_));
                float vDiskSq = p.Gravity * mEnc / r;

                float vCirc = MathF.Sqrt(MathF.Max(vHaloSq + vBhSq + vDiskSq, 1e-6f));

                float vR = GaussNoise(rng) * localSigmaR;
                float vPhi = vCirc + GaussNoise(rng) * localSigmaR * 0.4f;
                vZ = GaussNoise(rng) * localSigmaZ;

                vx = vR * MathF.Cos(phi) - vPhi * MathF.Sin(phi);
                vy = vR * MathF.Sin(phi) + vPhi * MathF.Cos(phi);
            }

            float radialShift = MathF.Exp(-r / (scaleLength * 1.5f)) * 0.25f;
            float u = Math.Clamp((float)rng.NextDouble() - radialShift, 0f, 1f);

            float temp;
            if (u < 0.35f)
                temp = 0.05f + (float)rng.NextDouble() * 0.25f;
            else if (u < 0.60f)
                temp = 0.25f + (float)rng.NextDouble() * 0.25f;
            else if (u < 0.80f)
                temp = 0.45f + (float)rng.NextDouble() * 0.20f;
            else if (u < 0.92f)
                temp = 0.65f + (float)rng.NextDouble() * 0.15f;
            else
                temp = 0.80f + (float)rng.NextDouble() * 0.20f;

            pos[i] = new Float4(x, z, y, mPerStar);
            vel[i] = new Float4(vx, vZ, vy, temp);
        }
        return (pos, vel);
    }

    private static float NfwVcircSq(float radius, float vRefAtScaleRadius, float scaleRadius)
    {
        float rs = MathF.Max(scaleRadius, 1e-3f);
        float r = MathF.Max(radius, 1e-5f);
        float x = r / rs;
        float shape = NfwShape(x);
        float vScaleSq = (vRefAtScaleRadius * vRefAtScaleRadius) / NfwAtScaleRadius;
        return vScaleSq * (shape / MathF.Max(x, 1e-4f));
    }

    private static float EffectiveBlackHoleSoftening(float baseSoftening, float blackHoleMass)
        => MathF.Max(baseSoftening, 0.055f * MathF.Sqrt(MathF.Max(blackHoleMass, 0f)));

    private static float NfwShape(float x)
    {
        if (x < 1e-3f)
        {
            return x * x * (0.5f - (2f / 3f) * x + 0.75f * x * x);
        }

        return MathF.Log(1f + x) - x / (1f + x);
    }

    public struct GalaxyPlacement
    {
        public Float3 Offset;
        public Float3 BulkVelocity;
        public float Inclination;
        public float Azimuth;
        public float Spin;
        public float MassScale;
        public int Seed;
    }

    public static (Float4[] pos, Float4[] vel) MultiGalaxy(
        int totalParticles,
        GalaxyPlacement[] placements,
        GalaxyParams p,
        float scaleLength = 3.0f,
        float scaleHeight = 0.22f,
        float sigmaR = 0.18f,
        float sigmaZ = 0.035f,
        float diskTotalMass = 5f,
        float bulgeFraction = 0.10f,
        bool useTwoComponentDisk = true,
        float thinDiskFraction = 0.82f,
        float thickScaleLengthMultiplier = 1.20f,
        float thickScaleHeightMultiplier = 2.60f,
        float thickSigmaRMultiplier = 1.25f,
        float thickSigmaZMultiplier = 2.40f)
    {
        int ng = placements.Length;
        int perGalaxy = totalParticles / ng;
        var pos = new Float4[totalParticles];
        var vel = new Float4[totalParticles];
        bool multi = ng > 1;
        float haloV0Scale = multi ? 1.0f : 1f;
        float bhMassScale = multi ? 0.65f : 1f;
        float bhSofteningScale = multi ? 1.45f : 1f;
        float bulgeFractionLocal = multi ? MathF.Max(bulgeFraction, 0.26f) : bulgeFraction;

        int offset = 0;
        for (int g = 0; g < ng; g++)
        {
            var plc = placements[g];
            int n = (g == ng - 1) ? totalParticles - offset : perGalaxy;

            var (lp, lv) = ExponentialDisk(n, p, scaleLength, scaleHeight,
                                            sigmaR, sigmaZ,
                                            diskTotalMass: diskTotalMass * plc.MassScale,
                                            useTwoComponentDisk: useTwoComponentDisk,
                                            thinDiskFraction: thinDiskFraction,
                                            thickScaleLengthMultiplier: thickScaleLengthMultiplier,
                                            thickScaleHeightMultiplier: thickScaleHeightMultiplier,
                                            thickSigmaRMultiplier: thickSigmaRMultiplier,
                                            thickSigmaZMultiplier: thickSigmaZMultiplier,
                                            haloV0Scale: haloV0Scale,
                                            bhMassScale: bhMassScale,
                                            bhSofteningScale: bhSofteningScale,
                                            bulgeFraction: bulgeFractionLocal,
                                            bulgeScale: scaleLength * 0.14f,
                                            bulgeDispersion: 0.55f,
                                            seed: plc.Seed);

            float ci = MathF.Cos(plc.Inclination), si = MathF.Sin(plc.Inclination);
            float ca = MathF.Cos(plc.Azimuth), sa = MathF.Sin(plc.Azimuth);

            for (int i = 0; i < n; i++)
            {
                var lpi = lp[i];
                var lvi = lv[i];

                float vx = lvi.X * plc.Spin;
                float vz = lvi.Z * plc.Spin;

                float y1 = lpi.Y * ci - lpi.Z * si;
                float z1 = lpi.Y * si + lpi.Z * ci;
                float vy1 = lvi.Y * ci - vz * si;
                float vz1 = lvi.Y * si + vz * ci;

                float x2 = lpi.X * ca + z1 * sa;
                float z2 = -lpi.X * sa + z1 * ca;
                float vx2 = vx * ca + vz1 * sa;
                float vz2 = -vx * sa + vz1 * ca;

                pos[offset + i] = new Float4(
                    x2 + plc.Offset.X,
                    y1 + plc.Offset.Y,
                    z2 + plc.Offset.Z,
                    lpi.W);

                vel[offset + i] = new Float4(
                    vx2 + plc.BulkVelocity.X,
                    vy1 + plc.BulkVelocity.Y,
                    vz2 + plc.BulkVelocity.Z,
                    lvi.W);
            }

            offset += n;
        }
        return (pos, vel);
    }

    private static float GaussNoise(Random r)
    {
        double u1 = 1 - r.NextDouble(), u2 = r.NextDouble();
        return (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }
}
