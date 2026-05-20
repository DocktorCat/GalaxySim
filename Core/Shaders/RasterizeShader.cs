using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct RasterizeShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<Float4> velocities,
    ReadWriteBuffer<uint> hdrR,
    ReadWriteBuffer<uint> hdrG,
    ReadWriteBuffer<uint> hdrB,
    int particleCount,
    int screenW,
    int screenH,
    Float4x4 viewProj,
    float splatScale,
    float intensity) : IComputeShader
{
    public void Execute()
    {
        int id = ThreadIds.X;
        if (id >= particleCount) return;

        Float4 wp = positions[id];
        Float4 clip = Hlsl.Mul(viewProj, new Float4(wp.XYZ, 1f));
        if (clip.W <= 0.01f) return;

        Float3 ndc = clip.XYZ / clip.W;
        if (Hlsl.Abs(ndc.X) > 1f || Hlsl.Abs(ndc.Y) > 1f || ndc.Z < 0f || ndc.Z > 1f)
            return;

        float fx = (ndc.X * 0.5f + 0.5f) * screenW;
        float fy = (1f - (ndc.Y * 0.5f + 0.5f)) * screenH;

        float t = velocities[id].W;
        Float3 color = TempToColor(t);

        float depthFactor = Hlsl.Saturate(ndc.Z);
        Float3 depthTint = new Float3(0.75f, 0.85f, 1.15f);
        color = Hlsl.Lerp(color, color * depthTint, depthFactor * 0.3f);

        float n0 = Hash(id * 0.173f + wp.X * 1.71f + wp.Z * 0.63f);
        float n1 = Hash(id * 0.419f + wp.X * 0.59f - wp.Z * 1.21f);
        float giant = SmoothStep(0.992f, 0.9995f, n0);
        float super = SmoothStep(0.9987f, 0.99995f, n1);

        float resolvedProb = Hlsl.Saturate(0.02f + t * 0.16f + giant * 0.75f + super * 0.98f);
        float keepRoll = Hash(id * 0.811f + wp.Y * 0.73f - wp.Z * 0.37f);
        if (keepRoll > resolvedProb)
            return;

        float radiusMul = 0.52f + giant * 0.95f + super * 1.35f;
        int radius = (int)Hlsl.Clamp(splatScale * radiusMul / clip.W, 1f, 4f);
        float radiusF = radius + 0.35f;
        float maxDistSq = radiusF * radiusF;
        float invAlphaSq = 1f / (radiusF * radiusF * 0.55f + 0.22f);

        float scale = 2048f;

        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = (int)Hlsl.Floor(fx) + dx;
                int y = (int)Hlsl.Floor(fy) + dy;
                if ((uint)x >= (uint)screenW || (uint)y >= (uint)screenH) continue;

                float ddx = (x + 0.5f) - fx;
                float ddy = (y + 0.5f) - fy;
                float distSq = ddx * ddx + ddy * ddy;
                if (distSq > maxDistSq) continue;

                float falloff = Hlsl.Pow(1f + distSq * invAlphaSq, -2.2f);

                float unresolved = Hlsl.Pow(Hlsl.Saturate(1f - n0), 2.2f);
                float luminosity = (0.006f + t * t * 0.11f) * (0.30f + unresolved * 0.55f);
                luminosity += giant * (0.55f + t * 1.00f);
                luminosity += super * (1.6f + t * 2.6f);
                Float3 c = color * (falloff * intensity * luminosity * scale);
                int idx = y * screenW + x;

                Hlsl.InterlockedAdd(ref hdrR[idx], (uint)c.X);
                Hlsl.InterlockedAdd(ref hdrG[idx], (uint)c.Y);
                Hlsl.InterlockedAdd(ref hdrB[idx], (uint)c.Z);
            }
    }

    private static float Hash(float x)
    {
        float h = Hlsl.Sin(x * 12.9898f) * 43758.5453f;
        return h - Hlsl.Floor(h);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Hlsl.Saturate((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static Float3 TempToColor(float t)
    {
        Float3 classM = new Float3(1.18f, 0.56f, 0.43f);
        Float3 classK = new Float3(1.10f, 0.76f, 0.56f);
        Float3 classG = new Float3(1.00f, 0.95f, 0.84f);
        Float3 classF = new Float3(0.99f, 1.00f, 0.95f);
        Float3 classA = new Float3(0.90f, 0.96f, 1.04f);
        Float3 classB = new Float3(0.70f, 0.83f, 1.10f);
        Float3 classO = new Float3(0.58f, 0.74f, 1.16f);

        if (t < 0.20f) return Hlsl.Lerp(classM, classK, t / 0.20f);
        if (t < 0.40f) return Hlsl.Lerp(classK, classG, (t - 0.20f) / 0.20f);
        if (t < 0.60f) return Hlsl.Lerp(classG, classF, (t - 0.40f) / 0.20f);
        if (t < 0.70f) return Hlsl.Lerp(classF, classA, (t - 0.60f) / 0.10f);
        if (t < 0.80f) return Hlsl.Lerp(classA, classB, (t - 0.70f) / 0.10f);
        return Hlsl.Lerp(classB, classO, (t - 0.80f) / 0.20f);
    }
}
