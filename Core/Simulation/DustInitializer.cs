using System;
using ComputeSharp;

namespace GalaxySim.Core.Simulation;

public static class DustInitializer
{
    public static Float4[] ExponentialDisk(
    int n,
    float scaleLength = 3.5f,
    float scaleHeight = 0.08f,
    int seed = 7777)
    {
        var rng = new Random(seed);
        var dust = new Float4[n];

        for (int i = 0; i < n; i++)
        {
            float u1 = 1f - (float)rng.NextDouble();
            float u2 = 1f - (float)rng.NextDouble();
            float r = -scaleLength * 0.5f * (MathF.Log(u1) + MathF.Log(u2));
            r = Math.Max(r, 0.1f);

            float phi = (float)(rng.NextDouble() * Math.PI * 2);
            float zu = Math.Clamp(2f * (float)rng.NextDouble() - 1f, -0.999f, 0.999f);
            float z = scaleHeight * 0.5f * MathF.Log((1 + zu) / (1 - zu));

            float x = r * MathF.Cos(phi);
            float y = r * MathF.Sin(phi);

            float opacity = 0.25f + (float)rng.NextDouble() * 0.5f;

            dust[i] = new Float4(x, z, y, opacity);
        }
        return dust;
    }
}