using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ClearRgbBuffer2DShader(
    ReadWriteBuffer<uint> r,
    ReadWriteBuffer<uint> g,
    ReadWriteBuffer<uint> b,
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
        r[i] = 0u;
        g[i] = 0u;
        b[i] = 0u;
    }
}

