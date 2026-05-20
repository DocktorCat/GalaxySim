using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct CopyPickedStarShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<Float4> velocities,
    ReadWriteBuffer<Float4> selected,
    int selectedIndex,
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        if (ThreadIds.X != 0) return;

        if (selectedIndex < 0 || selectedIndex >= particleCount)
        {
            selected[0] = Float4.Zero;
            selected[1] = Float4.Zero;
            return;
        }

        selected[0] = positions[selectedIndex];
        selected[1] = velocities[selectedIndex];
    }
}
