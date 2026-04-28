using ComputeSharp;

namespace GalaxySim.Core.Shaders;


[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BackgroundStarsShader(
    ReadWriteBuffer<Float4> stars,
    ReadWriteBuffer<uint> hdrR,
    ReadWriteBuffer<uint> hdrG,
    ReadWriteBuffer<uint> hdrB,
    int starCount,
    int screenW,
    int screenH,
    Float4x4 viewProj,
    float skyRadius,
    float intensity) : IComputeShader
{
    public void Execute()
    {
        int id = ThreadIds.X;
        if (id >= starCount) return;

        Float4 star = stars[id];
        Float3 worldPos = star.XYZ * skyRadius;
        float brightness = star.W;

        Float4 clip = Hlsl.Mul(viewProj, new Float4(worldPos, 1f));
        if (clip.W <= 0.01f) return;

        Float3 ndc = clip.XYZ / clip.W;
        if (Hlsl.Abs(ndc.X) > 1f || Hlsl.Abs(ndc.Y) > 1f) return;

        int px = (int)((ndc.X * 0.5f + 0.5f) * screenW);
        int py = (int)((1f - (ndc.Y * 0.5f + 0.5f)) * screenH);
        if ((uint)px >= (uint)screenW || (uint)py >= (uint)screenH) return;


        Float3 colorCool = new Float3(0.75f, 0.85f, 1.00f);
        Float3 colorMid = new Float3(1.00f, 0.97f, 0.92f);
        Float3 colorWarm = new Float3(1.00f, 0.80f, 0.55f);

        Float3 color = brightness > 0.7f
            ? Hlsl.Lerp(colorMid, colorCool, (brightness - 0.7f) / 0.3f)
            : Hlsl.Lerp(colorWarm, colorMid, brightness / 0.7f);

        Float3 c = color * (brightness * intensity * 4096f);
        int idx = py * screenW + px;

        Hlsl.InterlockedAdd(ref hdrR[idx], (uint)c.X);
        Hlsl.InterlockedAdd(ref hdrG[idx], (uint)c.Y);
        Hlsl.InterlockedAdd(ref hdrB[idx], (uint)c.Z);
    }
}