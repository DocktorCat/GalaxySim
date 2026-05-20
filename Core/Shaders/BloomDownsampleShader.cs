using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BloomDownsampleShader(
    ReadWriteBuffer<Float4> input,
    ReadWriteBuffer<Float4> output,
    int inputWidth,
    int inputHeight,
    int outputWidth,
    int outputHeight) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X, y = ThreadIds.Y;
        if (x >= outputWidth || y >= outputHeight) return;

        int cx = x * 2 + 1;
        int cy = y * 2 + 1;


        int cx0 = Hlsl.Clamp(cx, 0, inputWidth - 1);
        int cy0 = Hlsl.Clamp(cy, 0, inputHeight - 1);
        int cxm = Hlsl.Clamp(cx - 1, 0, inputWidth - 1);
        int cxp = Hlsl.Clamp(cx + 1, 0, inputWidth - 1);
        int cym = Hlsl.Clamp(cy - 1, 0, inputHeight - 1);
        int cyp = Hlsl.Clamp(cy + 1, 0, inputHeight - 1);

        Float4 center = input[cy0 * inputWidth + cx0];
        Float4 tl = input[cym * inputWidth + cxm];
        Float4 tr = input[cym * inputWidth + cxp];
        Float4 bl = input[cyp * inputWidth + cxm];
        Float4 br = input[cyp * inputWidth + cxp];

        Float4 sum = center * 0.5f + (tl + tr + bl + br) * 0.125f;
        output[y * outputWidth + x] = sum;
    }
}