using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct UnresolvedStarlightShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<Float4> velocities,
    ReadWriteBuffer<uint> unresolvedR,
    ReadWriteBuffer<uint> unresolvedG,
    ReadWriteBuffer<uint> unresolvedB,
    int particleCount,
    int lowW,
    int lowH,
    Float4x4 viewProj,
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
        if (Hlsl.Abs(ndc.X) > 1.1f || Hlsl.Abs(ndc.Y) > 1.1f || ndc.Z < 0f || ndc.Z > 1f)
            return;

        float t = velocities[id].W;
        Float3 color = TempToColor(t);

        float fx = (ndc.X * 0.5f + 0.5f) * lowW - 0.5f;
        float fy = (1f - (ndc.Y * 0.5f + 0.5f)) * lowH - 0.5f;
        int x0 = (int)Hlsl.Floor(fx);
        int y0 = (int)Hlsl.Floor(fy);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        float tx = fx - x0;
        float ty = fy - y0;

        float w00 = (1f - tx) * (1f - ty);
        float w10 = tx * (1f - ty);
        float w01 = (1f - tx) * ty;
        float w11 = tx * ty;

        float h = Hash(id * 0.371f + wp.X * 0.59f + wp.Z * 1.31f);
        float depthFade = 1f - ndc.Z * 0.4f;
        float lum = (0.048f + t * t * 0.34f) * (0.80f + 0.42f * h) * depthFade * intensity;
        float scale = 2304f;

        Add(x0, y0, w00, color, lum, scale);
        Add(x1, y0, w10, color, lum, scale);
        Add(x0, y1, w01, color, lum, scale);
        Add(x1, y1, w11, color, lum, scale);
    }

    private void Add(int x, int y, float weight, Float3 color, float lum, float scale)
    {
        if (weight <= 0f) return;
        if ((uint)x >= (uint)lowW || (uint)y >= (uint)lowH) return;

        int idx = y * lowW + x;
        Float3 c = color * (weight * lum * scale);
        Hlsl.InterlockedAdd(ref unresolvedR[idx], (uint)c.X);
        Hlsl.InterlockedAdd(ref unresolvedG[idx], (uint)c.Y);
        Hlsl.InterlockedAdd(ref unresolvedB[idx], (uint)c.Z);
    }

    private static float Hash(float x)
    {
        float h = Hlsl.Sin(x * 12.9898f) * 43758.5453f;
        return h - Hlsl.Floor(h);
    }

    private static Float3 TempToColor(float t)
    {
        Float3 classM = new Float3(1.10f, 0.60f, 0.46f);
        Float3 classK = new Float3(1.08f, 0.78f, 0.60f);
        Float3 classG = new Float3(1.00f, 0.95f, 0.86f);
        Float3 classF = new Float3(0.98f, 1.00f, 0.95f);
        Float3 classA = new Float3(0.91f, 0.97f, 1.03f);
        Float3 classB = new Float3(0.75f, 0.86f, 1.07f);
        Float3 classO = new Float3(0.64f, 0.79f, 1.12f);

        if (t < 0.20f) return Hlsl.Lerp(classM, classK, t / 0.20f);
        if (t < 0.40f) return Hlsl.Lerp(classK, classG, (t - 0.20f) / 0.20f);
        if (t < 0.60f) return Hlsl.Lerp(classG, classF, (t - 0.40f) / 0.20f);
        if (t < 0.70f) return Hlsl.Lerp(classF, classA, (t - 0.60f) / 0.10f);
        if (t < 0.80f) return Hlsl.Lerp(classA, classB, (t - 0.70f) / 0.10f);
        return Hlsl.Lerp(classB, classO, (t - 0.80f) / 0.20f);
    }
}
