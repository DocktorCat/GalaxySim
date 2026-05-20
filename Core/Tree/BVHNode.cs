using ComputeSharp;

namespace GalaxySim.Core.Tree;

public struct BVHNode
{
    public int LeftChild;
    public int RightChild;
    public int Parent;
    public int RangeFirst;

    public int RangeLast;
    public int _pad0;
    public int _pad1;
    public int _pad2;

    public const int LeafFlag = -1;

    public static int EncodeLeaf(int leafIdx) => -(leafIdx + 1);
    public static int DecodeLeaf(int encoded) => -encoded - 1;
    public static bool IsLeaf(int encoded) => encoded < 0;
}