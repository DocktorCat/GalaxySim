using ComputeSharp;

namespace GalaxySim.Core.Tree;

public struct AABB
{
    public Float3 Min;
    public Float3 Max;

    public Float3 Center => (Min + Max) * 0.5f;
    public Float3 Extent => Max - Min;
    public float MaxExtent => MathF.Max(Extent.X, MathF.Max(Extent.Y, Extent.Z));

    public override string ToString()
        => $"Min({Min.X:F2},{Min.Y:F2},{Min.Z:F2}) Max({Max.X:F2},{Max.Y:F2},{Max.Z:F2})";
}