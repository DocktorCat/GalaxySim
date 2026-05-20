using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct HistogramShader(
    ReadWriteBuffer<uint> keys,
    ReadWriteBuffer<uint> histograms,
    int particleCount,
    int digitShift,          
    int blockSize) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= particleCount) return;

        uint key = keys[i];
        uint digit = (key >> digitShift) & 0xFFu;

        int blockIdx = i / blockSize;
        int histIdx = blockIdx * 256 + (int)digit;

        Hlsl.InterlockedAdd(ref histograms[histIdx], 1u);
    }
}