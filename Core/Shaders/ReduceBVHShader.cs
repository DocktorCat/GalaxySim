using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ReduceBVHShader(
    ReadWriteBuffer<int> nodeParent,
    ReadWriteBuffer<int> nodeLeftChild,
    ReadWriteBuffer<int> nodeRightChild,
    ReadWriteBuffer<uint> readyCounter,
    ReadWriteBuffer<Float4> nodeCOM,
    ReadWriteBuffer<Float4> nodeAabbMin,
    ReadWriteBuffer<Float4> nodeAabbMax,
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        int leafIdx = ThreadIds.X;
        if (leafIdx >= particleCount) return;

        int leafSlot = (particleCount - 1) + leafIdx;
        int parent = nodeParent[leafSlot];

        while (parent >= 0)
        {
            uint prev;
            Hlsl.InterlockedAdd(ref readyCounter[parent], 1u, out prev);
            if (prev == 0u) return;

            int lc = nodeLeftChild[parent];
            int rc = nodeRightChild[parent];
            int lcSlot = lc < 0 ? (particleCount - 1) + (-lc - 1) : lc;
            int rcSlot = rc < 0 ? (particleCount - 1) + (-rc - 1) : rc;

            Float4 lCom = nodeCOM[lcSlot];
            Float4 rCom = nodeCOM[rcSlot];
            nodeCOM[parent] = new Float4(lCom.XYZ + rCom.XYZ, lCom.W + rCom.W);

            Float4 lMin = nodeAabbMin[lcSlot];
            Float4 rMin = nodeAabbMin[rcSlot];
            Float4 lMax = nodeAabbMax[lcSlot];
            Float4 rMax = nodeAabbMax[rcSlot];
            nodeAabbMin[parent] = new Float4(Hlsl.Min(lMin.XYZ, rMin.XYZ), 0f);
            nodeAabbMax[parent] = new Float4(Hlsl.Max(lMax.XYZ, rMax.XYZ), 0f);

            Hlsl.DeviceMemoryBarrier();

            parent = nodeParent[parent];
        }
    }
}