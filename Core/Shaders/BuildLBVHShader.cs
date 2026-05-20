using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BuildLBVHShader(
    ReadWriteBuffer<uint> sortedMortonCodes,
    ReadWriteBuffer<int> nodeLeftChild,
    ReadWriteBuffer<int> nodeRightChild,
    ReadWriteBuffer<int> nodeParent,
    ReadWriteBuffer<int> nodeRangeFirst,
    ReadWriteBuffer<int> nodeRangeLast,
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int numInternal = particleCount - 1;
        if (i >= numInternal) return;

        int deltaLeft = Delta(i, i - 1, particleCount);
        int deltaRight = Delta(i, i + 1, particleCount);
        int d = deltaRight > deltaLeft ? 1 : -1;

        int deltaMin = Hlsl.Min(deltaLeft, deltaRight);

        int lmax = 2;
        while (Delta(i, i + lmax * d, particleCount) > deltaMin)
            lmax *= 2;

        int l = 0;
        for (int t = lmax / 2; t >= 1; t /= 2)
        {
            if (Delta(i, i + (l + t) * d, particleCount) > deltaMin)
                l += t;
        }
        int j = i + l * d;
        int first = Hlsl.Min(i, j);
        int last = Hlsl.Max(i, j);

        int deltaNode = Delta(i, j, particleCount);
        int s = 0;
        float divisor = 2f;
        int tStep = (int)Hlsl.Ceil(l / divisor);
        while (tStep >= 1)
        {
            if (Delta(i, i + (s + tStep) * d, particleCount) > deltaNode)
                s += tStep;
            divisor *= 2f;
            tStep = (int)Hlsl.Ceil(l / divisor);
        }
        int split = i + s * d + Hlsl.Min(d, 0);

        int leftChild = (split == first) ? EncodeLeaf(split) : split;
        int rightChild = (split + 1 == last) ? EncodeLeaf(split + 1) : split + 1;

        nodeLeftChild[i] = leftChild;
        nodeRightChild[i] = rightChild;
        nodeRangeFirst[i] = first;
        nodeRangeLast[i] = last;

        SetParent(leftChild, i, particleCount);
        SetParent(rightChild, i, particleCount);
    }

    private int Delta(int i, int j, int n)
    {
        if (j < 0 || j >= n) return -1;

        uint ci = sortedMortonCodes[i];
        uint cj = sortedMortonCodes[j];

        if (ci != cj)
        {
            return 31 - (int)Hlsl.FirstBitHigh(ci ^ cj);
        }

        uint xi = (uint)i;
        uint xj = (uint)j;
        return 32 + 31 - (int)Hlsl.FirstBitHigh(xi ^ xj);
    }

    private static int EncodeLeaf(int leafIdx) => -(leafIdx + 1);

    private void SetParent(int childEncoded, int parentIdx, int n)
    {
        int slot;
        if (childEncoded < 0)
        {
            int leafIdx = -childEncoded - 1;
            slot = (n - 1) + leafIdx;
        }
        else
        {
            slot = childEncoded;
        }
        nodeParent[slot] = parentIdx;
    }
}
