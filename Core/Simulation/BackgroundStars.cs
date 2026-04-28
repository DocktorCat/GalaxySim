using System;
using ComputeSharp;

namespace GalaxySim.Core.Simulation;

public static class BackgroundStars
{
    public static Float4[] Generate(int count, int seed = 1337)
    {
        var rng = new Random(seed);
        var stars = new Float4[count];

        for (int i = 0; i < count; i++)
        {
            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();
            float theta = 2f * MathF.PI * u;
            float phi = MathF.Acos(2f * v - 1f);

            float sinPhi = MathF.Sin(phi);
            float x = sinPhi * MathF.Cos(theta);
            float y = MathF.Cos(phi);
            float z = sinPhi * MathF.Sin(theta);

            float r = (float)rng.NextDouble();
            float brightness = MathF.Pow(r, 3.5f) * 0.9f + 0.05f;

            stars[i] = new Float4(x, y, z, brightness);
        }
        return stars;
    }
}