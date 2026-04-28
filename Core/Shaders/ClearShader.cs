using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ClearShader(
    ReadWriteBuffer<uint> hdrR,
    ReadWriteBuffer<uint> hdrG,
    ReadWriteBuffer<uint> hdrB,
    int length) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= length) return;
        hdrR[i] = 0u;
        hdrG[i] = 0u;
        hdrB[i] = 0u;
    }
}