using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ClearBufferShader(
    ReadWriteBuffer<uint> buffer,
    int length) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= length) return;
        buffer[i] = 0u;
    }
}