using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(256, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScatterShader(
    ReadWriteBuffer<uint> inKeys,
    ReadWriteBuffer<uint> inValues,
    ReadWriteBuffer<uint> outKeys,
    ReadWriteBuffer<uint> outValues,
    ReadWriteBuffer<uint> offsets,
    int particleCount,
    int digitShift,
    int blockSize) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= particleCount) return;

        uint key = inKeys[i];
        uint value = inValues[i];
        uint digit = (key >> digitShift) & 0xFFu;

        int blockIdx = i / blockSize;
        int blockStart = blockIdx * blockSize;

        uint localOffset = 0u;
        for (int j = blockStart; j < i; j++)
        {
            uint jDigit = (inKeys[j] >> digitShift) & 0xFFu;
            if (jDigit == digit) localOffset++;
        }

        uint globalOffset = offsets[blockIdx * 256 + (int)digit];
        int writePos = (int)(globalOffset + localOffset);
        if ((uint)writePos >= (uint)particleCount) return;

        outKeys[writePos] = key;
        outValues[writePos] = value;
    }
}
