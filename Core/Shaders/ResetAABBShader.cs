using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ResetAABBShader(
    ReadWriteBuffer<uint> aabbBuffer) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= 6) return;

        if (i < 3)
            aabbBuffer[i] = 0xFFFFFFFFu;
        else
            aabbBuffer[i] = 0x00000000u;
    }
}