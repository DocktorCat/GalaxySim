using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ComputeAABBShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<uint> aabbBuffer,
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= particleCount) return;

        Float3 p = positions[i].XYZ;

        uint ux = FloatToSortableUInt(p.X);
        uint uy = FloatToSortableUInt(p.Y);
        uint uz = FloatToSortableUInt(p.Z);

        Hlsl.InterlockedMin(ref aabbBuffer[0], ux);
        Hlsl.InterlockedMin(ref aabbBuffer[1], uy);
        Hlsl.InterlockedMin(ref aabbBuffer[2], uz);
        Hlsl.InterlockedMax(ref aabbBuffer[3], ux);
        Hlsl.InterlockedMax(ref aabbBuffer[4], uy);
        Hlsl.InterlockedMax(ref aabbBuffer[5], uz);
    }

    private static uint FloatToSortableUInt(float f)
    {
        uint u = Hlsl.AsUInt(f);
        uint mask = (u & 0x80000000u) != 0 ? 0xFFFFFFFFu : 0x80000000u;
        return u ^ mask;
    }
}