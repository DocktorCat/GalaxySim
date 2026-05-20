using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct DriftShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<Float4> velocities,
    int particleCount,
    float dt) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= particleCount) return;

        Float4 p = positions[i];
        Float4 v = velocities[i];
        p.XYZ += v.XYZ * dt;
        positions[i] = p;
    }
}