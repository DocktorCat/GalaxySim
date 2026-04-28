using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MortonShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<uint> aabbBuffer,
    ReadWriteBuffer<uint> mortonCodes,
    ReadWriteBuffer<uint> particleIds,
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= particleCount) return;

        float minX = SortableUIntToFloat(aabbBuffer[0]);
        float minY = SortableUIntToFloat(aabbBuffer[1]);
        float minZ = SortableUIntToFloat(aabbBuffer[2]);
        float maxX = SortableUIntToFloat(aabbBuffer[3]);
        float maxY = SortableUIntToFloat(aabbBuffer[4]);
        float maxZ = SortableUIntToFloat(aabbBuffer[5]);

        float ex = Hlsl.Max(maxX - minX, 1e-6f);
        float ey = Hlsl.Max(maxY - minY, 1e-6f);
        float ez = Hlsl.Max(maxZ - minZ, 1e-6f);

        Float3 p = positions[i].XYZ;
        float nx = Hlsl.Saturate((p.X - minX) / ex);
        float ny = Hlsl.Saturate((p.Y - minY) / ey);
        float nz = Hlsl.Saturate((p.Z - minZ) / ez);

        uint ux = (uint)Hlsl.Min(nx * 1024f, 1023f);
        uint uy = (uint)Hlsl.Min(ny * 1024f, 1023f);
        uint uz = (uint)Hlsl.Min(nz * 1024f, 1023f);

        uint code = ExpandBits(ux) | (ExpandBits(uy) << 1) | (ExpandBits(uz) << 2);

        mortonCodes[i] = code;
        particleIds[i] = (uint)i;
    }

    private static uint ExpandBits(uint v)
    {
        v = (v * 0x00010001u) & 0xFF0000FFu;
        v = (v * 0x00000101u) & 0x0F00F00Fu;
        v = (v * 0x00000011u) & 0xC30C30C3u;
        v = (v * 0x00000005u) & 0x49249249u;
        return v;
    }

    private static float SortableUIntToFloat(uint u)
    {
        uint mask = (u & 0x80000000u) != 0 ? 0x80000000u : 0xFFFFFFFFu;
        return Hlsl.AsFloat(u ^ mask);
    }
}