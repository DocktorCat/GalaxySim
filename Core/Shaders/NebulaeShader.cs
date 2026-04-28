using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct NebulaeShader(
    ReadWriteBuffer<Float4> nebulae,
    ReadWriteBuffer<uint> hdrR,
    ReadWriteBuffer<uint> hdrG,
    ReadWriteBuffer<uint> hdrB,
    int nebulaCount,
    int screenW,
    int screenH,
    Float4x4 viewProj,
    float baseRadius,
    float intensity) : IComputeShader
{
    public void Execute()
    {
        int id = ThreadIds.X;
        if (id >= nebulaCount) return;

        Float4 neb = nebulae[id];
        Float3 worldPos = neb.XYZ;
        float sizeMul = neb.W;

        Float4 clip = Hlsl.Mul(viewProj, new Float4(worldPos, 1f));
        if (clip.W <= 0.01f) return;

        Float3 ndc = clip.XYZ / clip.W;
        if (Hlsl.Abs(ndc.X) > 1.2f || Hlsl.Abs(ndc.Y) > 1.2f || ndc.Z < 0f || ndc.Z > 1f)
            return;

        int px = (int)((ndc.X * 0.5f + 0.5f) * screenW);
        int py = (int)((1f - (ndc.Y * 0.5f + 0.5f)) * screenH);

        float hue = Hash(id * 1.17f, 3.3f, 7.1f);
        Float3 colorA = new Float3(1.12f, 0.52f, 0.44f);
        Float3 colorB = new Float3(0.92f, 0.66f, 0.74f);
        Float3 color = Hlsl.Lerp(colorA, colorB, hue * 0.35f);

        int radius = (int)Hlsl.Clamp(baseRadius * sizeMul / clip.W, 6f, 34f);
        float r2 = radius * radius;
        float invCore = 1f / (r2 * 0.11f + 0.8f);
        float invHalo = 1f / (r2 * 0.42f + 1.5f);
        float scale = 3456f;
        int step = radius > 16 ? 2 : 1;

        for (int dy = -radius; dy <= radius; dy += step)
            for (int dx = -radius; dx <= radius; dx += step)
            {
                int x = px + dx, y = py + dy;
                if ((uint)x >= (uint)screenW || (uint)y >= (uint)screenH) continue;

                float distSq = dx * dx + dy * dy;
                if (distSq > r2) continue;

                float d = Hlsl.Sqrt(distSq);
                float edge = 1f - Hlsl.Saturate(d / (radius + 1e-4f));
                float core = Hlsl.Exp(-distSq * invCore);
                float halo = Hlsl.Exp(-distSq * invHalo);
                float fil = Hash(id * 0.77f, dx * 0.137f, dy * 0.173f);
                float filament = 0.72f + 0.58f * Hlsl.Pow(fil, 1.8f);
                float falloff = (core * 0.62f + halo * 0.38f) * (0.25f + 0.75f * edge) * filament;

                Float3 c = color * (falloff * intensity * scale);
                int idx = y * screenW + x;

                Hlsl.InterlockedAdd(ref hdrR[idx], (uint)c.X);
                Hlsl.InterlockedAdd(ref hdrG[idx], (uint)c.Y);
                Hlsl.InterlockedAdd(ref hdrB[idx], (uint)c.Z);
            }
    }

    private static float Hash(float a, float b, float c)
    {
        float h = Hlsl.Sin(a * 12.9898f + b * 78.233f + c * 37.719f) * 43758.5453f;
        return h - Hlsl.Floor(h);
    }
}
