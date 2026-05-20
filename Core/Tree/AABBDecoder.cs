using ComputeSharp;

namespace GalaxySim.Core.Tree;

public static class AABBDecoder
{
    public static AABB Decode(uint[] raw)
    {
        return new AABB
        {
            Min = new Float3(
                SortableUIntToFloat(raw[0]),
                SortableUIntToFloat(raw[1]),
                SortableUIntToFloat(raw[2])),
            Max = new Float3(
                SortableUIntToFloat(raw[3]),
                SortableUIntToFloat(raw[4]),
                SortableUIntToFloat(raw[5])),
        };
    }

    private static float SortableUIntToFloat(uint u)
    {
        uint mask = (u & 0x80000000u) != 0 ? 0x80000000u : 0xFFFFFFFFu;
        uint restored = u ^ mask;
        return BitConverter.UInt32BitsToSingle(restored);
    }
}