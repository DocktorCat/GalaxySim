using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct InitLeavesShader(
    ReadWriteBuffer<Float4> positions,        
    ReadWriteBuffer<uint> sortedParticleIds,  
    ReadWriteBuffer<Float4> nodeCOM,          
    ReadWriteBuffer<Float4> nodeAabbMin,      
    ReadWriteBuffer<Float4> nodeAabbMax,      
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= particleCount) return;

        int origId = (int)sortedParticleIds[i];
        Float4 p = positions[origId];

        int slot = (particleCount - 1) + i;   

        nodeCOM[slot] = new Float4(p.XYZ * p.W, p.W);
        nodeAabbMin[slot] = new Float4(p.XYZ, 0f);
        nodeAabbMax[slot] = new Float4(p.XYZ, 0f);
    }
}