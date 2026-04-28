using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BloomUpsampleShader(
    ReadWriteBuffer<Float4> input,
    ReadWriteBuffer<Float4> output,
    int inputWidth,
    int inputHeight,
    int outputWidth,
    int outputHeight,
    float blendStrength) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X, y = ThreadIds.Y;
        if (x >= outputWidth || y >= outputHeight) return;

        int sx = x / 2;
        int sy = y / 2;

        int sxm = Hlsl.Clamp(sx - 1, 0, inputWidth - 1);
        int sxc = Hlsl.Clamp(sx, 0, inputWidth - 1);
        int sxp = Hlsl.Clamp(sx + 1, 0, inputWidth - 1);
        int sym = Hlsl.Clamp(sy - 1, 0, inputHeight - 1);
        int syc = Hlsl.Clamp(sy, 0, inputHeight - 1);
        int syp = Hlsl.Clamp(sy + 1, 0, inputHeight - 1);

        Float4 c = input[syc * inputWidth + sxc] * 4f;
        Float4 u = input[sym * inputWidth + sxc] * 2f;
        Float4 d = input[syp * inputWidth + sxc] * 2f;
        Float4 l = input[syc * inputWidth + sxm] * 2f;
        Float4 r = input[syc * inputWidth + sxp] * 2f;
        Float4 tl = input[sym * inputWidth + sxm];
        Float4 tr = input[sym * inputWidth + sxp];
        Float4 bl = input[syp * inputWidth + sxm];
        Float4 br = input[syp * inputWidth + sxp];

        Float4 sum = (c + u + d + l + r + tl + tr + bl + br) / 16f;

        int dstIdx = y * outputWidth + x;
        Float4 existing = output[dstIdx];
        output[dstIdx] = existing + sum * blendStrength;
    }
}