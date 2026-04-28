using System;
using ComputeSharp;

namespace GalaxySim.Core.Tree;

public sealed class SortBuffers : IDisposable
{
    public int Capacity { get; private set; }

    public ReadWriteBuffer<uint> MortonCodes { get; private set; }
    public ReadWriteBuffer<uint> ParticleIds { get; private set; }

    public ReadWriteBuffer<uint> MortonCodesAlt { get; private set; }
    public ReadWriteBuffer<uint> ParticleIdsAlt { get; private set; }

    private readonly GraphicsDevice _device;

    public SortBuffers(GraphicsDevice device, int capacity)
    {
        _device = device;
        Capacity = capacity;
        MortonCodes = device.AllocateReadWriteBuffer<uint>(capacity);
        ParticleIds = device.AllocateReadWriteBuffer<uint>(capacity);
        MortonCodesAlt = device.AllocateReadWriteBuffer<uint>(capacity);
        ParticleIdsAlt = device.AllocateReadWriteBuffer<uint>(capacity);
    }

    public void Resize(int newCapacity)
    {
        if (newCapacity == Capacity) return;

        MortonCodes.Dispose();
        ParticleIds.Dispose();
        MortonCodesAlt.Dispose();
        ParticleIdsAlt.Dispose();

        Capacity = newCapacity;
        MortonCodes = _device.AllocateReadWriteBuffer<uint>(newCapacity);
        ParticleIds = _device.AllocateReadWriteBuffer<uint>(newCapacity);
        MortonCodesAlt = _device.AllocateReadWriteBuffer<uint>(newCapacity);
        ParticleIdsAlt = _device.AllocateReadWriteBuffer<uint>(newCapacity);
    }

    public void Dispose()
    {
        MortonCodes.Dispose();
        ParticleIds.Dispose();
        MortonCodesAlt.Dispose();
        ParticleIdsAlt.Dispose();
    }
}