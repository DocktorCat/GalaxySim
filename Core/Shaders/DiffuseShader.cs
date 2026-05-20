using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct DiffuseShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<Float4> velocities,
    ReadWriteBuffer<uint> hdrR,
    ReadWriteBuffer<uint> hdrG,
    ReadWriteBuffer<uint> hdrB,
    int particleCount,
    int screenW,
    int screenH,
    Float4x4 viewProj,
    float diffuseRadius,       
    float diffuseIntensity,    
    float tempThreshold) : IComputeShader   
{
    public void Execute()
    {
        int id = ThreadIds.X;
        if (id >= particleCount) return;

        float t = velocities[id].W;

        float edge0 = tempThreshold;
        float edge1 = tempThreshold + 0.15f;
        float s = Hlsl.Saturate((t - edge0) / (edge1 - edge0));
        float tempMask = s * s * (3f - 2f * s);
        if (tempMask <= 0.02f) return;

        Float4 wp = positions[id];
        Float4 clip = Hlsl.Mul(viewProj, new Float4(wp.XYZ, 1f));
        if (clip.W <= 0.01f) return;

        Float3 ndc = clip.XYZ / clip.W;
        if (Hlsl.Abs(ndc.X) > 1f || Hlsl.Abs(ndc.Y) > 1f || ndc.Z < 0f || ndc.Z > 1f)
            return;

        int px = (int)((ndc.X * 0.5f + 0.5f) * screenW);
        int py = (int)((1f - (ndc.Y * 0.5f + 0.5f)) * screenH);

        Float3 color = TempToColor(t);

        int radius = (int)Hlsl.Clamp(diffuseRadius / clip.W, 3f, 14f);
        float r2 = radius * radius;
        float invCore = 1f / (r2 * 0.12f + 0.4f);
        float invHalo = 1f / (r2 * 0.52f + 1.2f);
        float scale = 3328f;

        int step = radius > 9 ? 2 : 1;
        for (int dy = -radius; dy <= radius; dy += step)
            for (int dx = -radius; dx <= radius; dx += step)
            {
                int x = px + dx, y = py + dy;
                if ((uint)x >= (uint)screenW || (uint)y >= (uint)screenH) continue;

                float distSq = dx * dx + dy * dy;
                if (distSq > r2) continue;

                float core = Hlsl.Exp(-distSq * invCore);
                float halo = Hlsl.Exp(-distSq * invHalo);
                float edge = 1f - Hlsl.Saturate(Hlsl.Sqrt(distSq) / (radius + 1e-4f));
                float falloff = core * 0.78f + halo * 0.28f;
                falloff *= (0.35f + 0.65f * edge);

                float grain = Hash(id * 0.173f, dx * 0.331f, dy * 0.271f);
                float structure = 0.86f + 0.24f * grain;

                Float3 c = color * (falloff * structure * diffuseIntensity * tempMask * scale);
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

    private static Float3 TempToColor(float t)
    {
        Float3 classM = new Float3(1.10f, 0.45f, 0.25f);
        Float3 classK = new Float3(1.05f, 0.68f, 0.42f);
        Float3 classG = new Float3(1.00f, 0.95f, 0.82f);
        Float3 classF = new Float3(1.00f, 1.00f, 0.97f);
        Float3 classA = new Float3(0.92f, 0.96f, 1.05f);
        Float3 classB = new Float3(0.55f, 0.75f, 1.15f);
        Float3 classO = new Float3(0.35f, 0.60f, 1.25f);

        if (t < 0.20f) return Hlsl.Lerp(classM, classK, t / 0.20f);
        if (t < 0.40f) return Hlsl.Lerp(classK, classG, (t - 0.20f) / 0.20f);
        if (t < 0.60f) return Hlsl.Lerp(classG, classF, (t - 0.40f) / 0.20f);
        if (t < 0.70f) return Hlsl.Lerp(classF, classA, (t - 0.60f) / 0.10f);
        if (t < 0.80f) return Hlsl.Lerp(classA, classB, (t - 0.70f) / 0.10f);
        return Hlsl.Lerp(classB, classO, (t - 0.80f) / 0.20f);
    }
}
