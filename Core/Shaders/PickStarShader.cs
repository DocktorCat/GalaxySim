using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct PickStarShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<uint> pickResult,
    int particleCount,
    Float4x4 viewProj,
    float screenX,
    float screenY,
    int screenWidth,
    int screenHeight,
    float pickRadiusPixels) : IComputeShader
{
    public void Execute()
    {
        int id = ThreadIds.X;
        if (id >= particleCount) return;

        Float4 p = positions[id];
        Float4 clip = Hlsl.Mul(viewProj, new Float4(p.XYZ, 1f));
        if (clip.W <= 0.01f) return;

        Float3 ndc = clip.XYZ / clip.W;
        if (Hlsl.Abs(ndc.X) > 1f || Hlsl.Abs(ndc.Y) > 1f || ndc.Z < 0f || ndc.Z > 1f)
            return;

        float px = (ndc.X * 0.5f + 0.5f) * screenWidth;
        float py = (1f - (ndc.Y * 0.5f + 0.5f)) * screenHeight;
        float dx = px - screenX;
        float dy = py - screenY;
        float distSq = dx * dx + dy * dy;
        float radiusSq = pickRadiusPixels * pickRadiusPixels;
        if (distSq > radiusSq)
            return;

        float score = distSq + ndc.Z * 0.35f;
        uint scoreFixed = (uint)Hlsl.Clamp(score * 16f, 0f, 4094f);
        uint indexBits = (uint)id & 0x000FFFFFu;
        uint packed = (scoreFixed << 20) | indexBits;
        Hlsl.InterlockedMin(ref pickResult[0], packed);
    }
}
