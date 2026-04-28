using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ClearUIntBuffer2DShader(
    ReadWriteBuffer<uint> buffer,
    int width,
    int height) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        int i = y * width + x;
        buffer[i] = 0u;
    }
}

