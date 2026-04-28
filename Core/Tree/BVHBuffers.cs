using System;
using ComputeSharp;

namespace GalaxySim.Core.Tree;

public sealed class BVHBuffers : IDisposable
{
    public int Capacity { get; private set; }
    public int InternalCount => Capacity - 1;
    public int TotalNodes => 2 * Capacity - 1;

    public ReadWriteBuffer<int> LeftChild { get; private set; }
    public ReadWriteBuffer<int> RightChild { get; private set; }
    public ReadWriteBuffer<int> Parent { get; private set; }
    public ReadWriteBuffer<int> RangeFirst { get; private set; }
    public ReadWriteBuffer<int> RangeLast { get; private set; }

    public ReadWriteBuffer<Float4> COM { get; private set; }
    public ReadWriteBuffer<Float4> AabbMin { get; private set; }
    public ReadWriteBuffer<Float4> AabbMax { get; private set; }
    public ReadWriteBuffer<uint> ReadyCounter { get; private set; }

    public ReadWriteBuffer<Float4> TravComMass { get; private set; }
    public ReadWriteBuffer<float> TravSizeMax { get; private set; }

    public ReadWriteBuffer<int> TravNext { get; private set; }

    public ReadWriteBuffer<int> TravLeft { get; private set; }

    private readonly GraphicsDevice _device;

    public BVHBuffers(GraphicsDevice device, int capacity)
    {
        _device = device;
        Capacity = capacity;
        int intCount = capacity - 1;
        int totalNodes = 2 * capacity - 1;

        LeftChild = device.AllocateReadWriteBuffer<int>(intCount);
        RightChild = device.AllocateReadWriteBuffer<int>(intCount);
        RangeFirst = device.AllocateReadWriteBuffer<int>(intCount);
        RangeLast = device.AllocateReadWriteBuffer<int>(intCount);
        Parent = device.AllocateReadWriteBuffer<int>(totalNodes);

        COM = device.AllocateReadWriteBuffer<Float4>(totalNodes);
        AabbMin = device.AllocateReadWriteBuffer<Float4>(totalNodes);
        AabbMax = device.AllocateReadWriteBuffer<Float4>(totalNodes);
        ReadyCounter = device.AllocateReadWriteBuffer<uint>(intCount);

        TravComMass = device.AllocateReadWriteBuffer<Float4>(totalNodes);
        TravSizeMax = device.AllocateReadWriteBuffer<float>(intCount);

        TravNext = device.AllocateReadWriteBuffer<int>(totalNodes);
        TravLeft = device.AllocateReadWriteBuffer<int>(totalNodes);
    }

    public void Dispose()
    {
        LeftChild.Dispose();
        RightChild.Dispose();
        Parent.Dispose();
        RangeFirst.Dispose();
        RangeLast.Dispose();
        COM.Dispose();
        AabbMin.Dispose();
        AabbMax.Dispose();
        ReadyCounter.Dispose();
        TravComMass.Dispose();
        TravSizeMax.Dispose();
        TravNext.Dispose();
        TravLeft.Dispose();
    }
}