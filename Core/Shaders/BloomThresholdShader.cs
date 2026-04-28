using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BloomThresholdShader(
    ReadWriteBuffer<Float4> input,
    ReadWriteBuffer<Float4> output,
    int width,
    int height,
    float threshold,
    float softKnee) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X, y = ThreadIds.Y;
        if (x >= width || y >= height) return;

        int idx = y * width + x;
        Float3 color = input[idx].XYZ;
        float brightness = Hlsl.Max(color.X, Hlsl.Max(color.Y, color.Z));

        float knee = threshold * softKnee;
        float soft = Hlsl.Clamp(brightness - threshold + knee, 0f, 2f * knee);
        soft = soft * soft / (4f * knee + 1e-4f);

        float contrib = Hlsl.Max(soft, brightness - threshold);
        contrib /= Hlsl.Max(brightness, 1e-4f);

        output[idx] = new Float4(color * contrib, 1f);
    }
}