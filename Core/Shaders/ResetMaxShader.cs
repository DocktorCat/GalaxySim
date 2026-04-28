using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(1, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ResetMaxShader(
    ReadWriteBuffer<uint> maxSpeedSqFixed) : IComputeShader
{
    public void Execute()
    {
        maxSpeedSqFixed[0] = 0u;
    }
}