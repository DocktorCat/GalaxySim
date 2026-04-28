using System;
using System.Collections.Generic;
using ComputeSharp;

namespace GalaxySim.Core.Simulation;

public static class NebulaInitializer
{
    public static Float4[] GenerateInDisks(
        int count,
        GalaxyInitializer.GalaxyPlacement[] galaxies,
        float scaleLength = 3.0f,
        float innerCutoff = 1.5f,
        int seed = 9999)
    {
        if (count <= 0)
            return Array.Empty<Float4>();

        if (galaxies.Length == 0)
            return Array.Empty<Float4>();

        var rng = new Random(seed);
        var nebulae = new Float4[count];

        for (int i = 0; i < count; i++)
        {
            int gi = i % galaxies.Length;
            var g = galaxies[gi];

            float r;
            do
            {
                float u1 = 1f - (float)rng.NextDouble();
                float u2 = 1f - (float)rng.NextDouble();
                r = -scaleLength * 0.5f * (MathF.Log(u1) + MathF.Log(u2));
            } while (r < innerCutoff);

            float phi = (float)(rng.NextDouble() * Math.PI * 2);
            float yJitter = ((float)rng.NextDouble() - 0.5f) * 0.3f;

            float x = r * MathF.Cos(phi);
            float z = r * MathF.Sin(phi);

            float ci = MathF.Cos(g.Inclination), si = MathF.Sin(g.Inclination);
            float ca = MathF.Cos(g.Azimuth), sa = MathF.Sin(g.Azimuth);
            float y1 = yJitter * ci - z * si;
            float z1 = yJitter * si + z * ci;
            float x2 = x * ca + z1 * sa;
            float z2 = -x * sa + z1 * ca;

            float wx = x2 + g.Offset.X;
            float wy = y1 + g.Offset.Y;
            float wz = z2 + g.Offset.Z;

            float sizeRoll = (float)rng.NextDouble();
            float size = 0.3f + MathF.Pow(sizeRoll, 3f) * 1.2f;

            nebulae[i] = new Float4(wx, wy, wz, size);
        }

        return nebulae;
    }

    public static Float4[] GenerateFromStarFormationFeedback(
        int count,
        Float4[] starPositions,
        Float4[] starVelocities,
        GalaxyInitializer.GalaxyPlacement[] galaxies,
        float scaleLength = 3.0f,
        float innerCutoff = 1.5f,
        float tempThreshold = 0.58f,
        int gridResolution = 56,
        int seed = 4242)
    {
        if (count <= 0)
            return Array.Empty<Float4>();

        if (galaxies.Length == 0 ||
            starPositions.Length == 0 ||
            starPositions.Length != starVelocities.Length)
        {
            return GenerateInDisks(count, galaxies, scaleLength, innerCutoff, seed);
        }

        int grid = Math.Clamp(gridResolution, 24, 96);
        int cells = grid * grid;
        var result = new Float4[count];
        var rng = new Random(seed);

        int written = 0;
        int galaxyCount = galaxies.Length;
        int starsPerGalaxy = starPositions.Length / galaxyCount;

        for (int gi = 0; gi < galaxyCount && written < count; gi++)
        {
            int remaining = count - written;
            int perGalaxyNeb = (gi == galaxyCount - 1)
                ? remaining
                : Math.Max(1, count / galaxyCount);
            perGalaxyNeb = Math.Min(perGalaxyNeb, remaining);

            int start = gi * starsPerGalaxy;
            int starCount = (gi == galaxyCount - 1)
                ? starPositions.Length - start
                : starsPerGalaxy;

            if (starCount <= 0)
            {
                var fallback = GenerateInDisks(perGalaxyNeb, new[] { galaxies[gi] }, scaleLength, innerCutoff, seed + gi * 997);
                for (int k = 0; k < fallback.Length && written < count; k++)
                    result[written++] = fallback[k];
                continue;
            }

            var g = galaxies[gi];
            float ci = MathF.Cos(g.Inclination), si = MathF.Sin(g.Inclination);
            float ca = MathF.Cos(g.Azimuth), sa = MathF.Sin(g.Azimuth);

            float rMax = innerCutoff * 2f;
            for (int i = start; i < start + starCount; i++)
            {
                Float3 rel = starPositions[i].XYZ - g.Offset;
                ToLocalDisk(rel, ci, si, ca, sa, out float lx, out _, out float lz);
                float r = MathF.Sqrt(lx * lx + lz * lz);
                if (r > rMax) rMax = r;
            }

            float mapRadius = Math.Clamp(rMax * 0.92f, MathF.Max(scaleLength * 3.5f, innerCutoff * 2f), scaleLength * 14f);
            if (mapRadius <= innerCutoff + 0.25f)
            {
                var fallback = GenerateInDisks(perGalaxyNeb, new[] { g }, scaleLength, innerCutoff, seed + gi * 997 + 1);
                for (int k = 0; k < fallback.Length && written < count; k++)
                    result[written++] = fallback[k];
                continue;
            }

            var weightedMass = new float[cells];
            var tempSum = new float[cells];
            var countSum = new int[cells];
            var sumX = new float[cells];
            var sumY = new float[cells];
            var sumZ = new float[cells];

            float invSpan = 1f / (2f * mapRadius);
            for (int i = start; i < start + starCount; i++)
            {
                float temp = starVelocities[i].W;
                if (temp < tempThreshold) continue;

                Float3 rel = starPositions[i].XYZ - g.Offset;
                ToLocalDisk(rel, ci, si, ca, sa, out float lx, out float ly, out float lz);

                float r = MathF.Sqrt(lx * lx + lz * lz);
                if (r < innerCutoff || r > mapRadius) continue;

                float u = lx * invSpan + 0.5f;
                float v = lz * invSpan + 0.5f;
                if (u <= 0f || u >= 1f || v <= 0f || v >= 1f) continue;

                int gx = Math.Clamp((int)(u * grid), 0, grid - 1);
                int gy = Math.Clamp((int)(v * grid), 0, grid - 1);
                int idx = gy * grid + gx;

                float tempWeight = 0.25f + temp * temp;
                weightedMass[idx] += MathF.Max(starPositions[i].W, 1e-4f) * tempWeight;
                tempSum[idx] += temp;
                countSum[idx]++;
                sumX[idx] += lx;
                sumY[idx] += ly;
                sumZ[idx] += lz;
            }

            var rankedCells = new List<(int cell, float score)>(cells / 4);
            for (int c = 0; c < cells; c++)
            {
                int n = countSum[c];
                if (n < 2) continue;

                float avgTemp = tempSum[c] / n;
                float score = weightedMass[c] * (0.35f + avgTemp * avgTemp) * MathF.Log(1f + n);
                if (score > 1e-6f)
                    rankedCells.Add((c, score));
            }

            rankedCells.Sort((a, b) => b.score.CompareTo(a.score));
            int topK = Math.Min(rankedCells.Count, 24);

            if (topK == 0)
            {
                var fallback = GenerateInDisks(perGalaxyNeb, new[] { g }, scaleLength, innerCutoff, seed + gi * 997 + 2);
                for (int k = 0; k < fallback.Length && written < count; k++)
                    result[written++] = fallback[k];
                continue;
            }

            float[] prefix = new float[topK];
            float total = 0f;
            for (int k = 0; k < topK; k++)
            {
                total += rankedCells[k].score;
                prefix[k] = total;
            }

            float cellSize = 2f * mapRadius / grid;
            for (int j = 0; j < perGalaxyNeb && written < count; j++)
            {
                float pick = (float)rng.NextDouble() * total;
                int selected = 0;
                while (selected < topK - 1 && pick > prefix[selected])
                    selected++;

                int cell = rankedCells[selected].cell;
                int n = Math.Max(countSum[cell], 1);

                float lx = sumX[cell] / n + ((float)rng.NextDouble() - 0.5f) * cellSize * 0.7f;
                float ly = sumY[cell] / n + ((float)rng.NextDouble() - 0.5f) * 0.18f;
                float lz = sumZ[cell] / n + ((float)rng.NextDouble() - 0.5f) * cellSize * 0.7f;

                ToWorldDisk(lx, ly, lz, ci, si, ca, sa, out float wx, out float wy, out float wz);
                wx += g.Offset.X;
                wy += g.Offset.Y;
                wz += g.Offset.Z;

                float avgTemp = tempSum[cell] / n;
                float densityBoost = MathF.Min(countSum[cell] / 5f, 2.6f);
                float size = 0.35f + densityBoost * 0.35f + avgTemp * 0.65f;
                size *= 0.85f + (float)rng.NextDouble() * 0.35f;
                size = Math.Clamp(size, 0.30f, 2.20f);

                result[written++] = new Float4(wx, wy, wz, size);
            }
        }

        if (written < count)
        {
            var fallback = GenerateInDisks(count - written, galaxies, scaleLength, innerCutoff, seed + 1337);
            for (int i = 0; i < fallback.Length && written < count; i++)
                result[written++] = fallback[i];
        }

        return result;
    }

    private static void ToLocalDisk(
        Float3 worldRelative,
        float ci, float si,
        float ca, float sa,
        out float x,
        out float y,
        out float z)
    {
        float x1 = worldRelative.X * ca - worldRelative.Z * sa;
        float z1 = worldRelative.X * sa + worldRelative.Z * ca;

        x = x1;
        y = worldRelative.Y * ci + z1 * si;
        z = -worldRelative.Y * si + z1 * ci;
    }

    private static void ToWorldDisk(
        float x, float y, float z,
        float ci, float si,
        float ca, float sa,
        out float wx,
        out float wy,
        out float wz)
    {
        float y1 = y * ci - z * si;
        float z1 = y * si + z * ci;

        wx = x * ca + z1 * sa;
        wz = -x * sa + z1 * ca;
        wy = y1;
    }
}
