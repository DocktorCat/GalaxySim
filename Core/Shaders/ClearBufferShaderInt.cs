using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ClearBufferShaderInt(
    ReadWriteBuffer<int> buffer,
    int length,
    int value) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= length) return;
        buffer[i] = value;
    }
}