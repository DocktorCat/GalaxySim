using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct PackTraversalShader(
    ReadWriteBuffer<Float4> nodeCOM,       
    ReadWriteBuffer<Float4> nodeAabbMin,
    ReadWriteBuffer<Float4> nodeAabbMax,
    ReadWriteBuffer<Float4> travComMass,   
    ReadWriteBuffer<float> travSizeMax,   
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int totalNodes = 2 * particleCount - 1;
        if (i >= totalNodes) return;

        Float4 com = nodeCOM[i];
        float mass = com.W;
        Float3 pos = mass > 0f ? com.XYZ / mass : Float3.Zero;
        travComMass[i] = new Float4(pos, mass);

        int internalCount = particleCount - 1;
        if (i < internalCount)
        {
            Float3 size = nodeAabbMax[i].XYZ - nodeAabbMin[i].XYZ;
            travSizeMax[i] = Hlsl.Max(size.X, Hlsl.Max(size.Y, size.Z));
        }
    }
}