using System;
using ComputeSharp;
using GalaxySim.Core.Shaders;

namespace GalaxySim.Core.Tree;

public sealed class RadixSorter : IDisposable
{
    public const int BlockSize = 256;
    public const int NumBins = 256;

    private readonly GraphicsDevice _device;
    private readonly int _capacity;
    private readonly int _numBlocks;

    private readonly ReadWriteBuffer<uint> _histograms;
    private readonly ReadWriteBuffer<uint> _offsets;

    private readonly uint[] _histogramsCpu;
    private readonly uint[] _offsetsCpu;

    public RadixSorter(GraphicsDevice device, int capacity)
    {
        _device = device;
        _capacity = capacity;
        _numBlocks = (capacity + BlockSize - 1) / BlockSize;

        int histSize = _numBlocks * NumBins;
        _histograms = device.AllocateReadWriteBuffer<uint>(histSize);
        _offsets = device.AllocateReadWriteBuffer<uint>(histSize);

        _histogramsCpu = new uint[histSize];
        _offsetsCpu = new uint[histSize];
    }

    public uint[] DebugHistogramOnly(ReadWriteBuffer<uint> keys, int digitShift)
    {
        _device.For(_histograms.Length, new ClearBufferShader(_histograms, _histograms.Length));
        _device.For(_capacity, new HistogramShader(
            keys, _histograms, _capacity, digitShift, BlockSize));

        _histograms.CopyTo(_histogramsCpu);
        return _histogramsCpu;
    }

    public void Sort(
        ReadWriteBuffer<uint> keys, ReadWriteBuffer<uint> values,
        ReadWriteBuffer<uint> keysAlt, ReadWriteBuffer<uint> valuesAlt)
    {
        SortOnePass(keys, values, keysAlt, valuesAlt, 0);
        SortOnePass(keysAlt, valuesAlt, keys, values, 8);
        SortOnePass(keys, values, keysAlt, valuesAlt, 16);
        SortOnePass(keysAlt, valuesAlt, keys, values, 24);
    }

    private void SortOnePass(
        ReadWriteBuffer<uint> keysIn, ReadWriteBuffer<uint> valuesIn,
        ReadWriteBuffer<uint> keysOut, ReadWriteBuffer<uint> valuesOut,
        int digitShift)
    {
        _device.For(_histograms.Length, new ClearBufferShader(_histograms, _histograms.Length));
        _device.For(_capacity, new HistogramShader(
            keysIn, _histograms, _capacity, digitShift, BlockSize));

        _histograms.CopyTo(_histogramsCpu);
        ComputeOffsets(_histogramsCpu, _offsetsCpu, _numBlocks);
        _offsets.CopyFrom(_offsetsCpu);

        _device.For(_capacity, new ScatterShader(
            keysIn, valuesIn, keysOut, valuesOut,
            _offsets, _capacity, digitShift, BlockSize));
    }

    private static void ComputeOffsets(uint[] hist, uint[] offsets, int numBlocks)
    {
        Span<uint> binTotals = stackalloc uint[256];
        for (int b = 0; b < numBlocks; b++)
        {
            int bOff = b * 256;
            for (int d = 0; d < 256; d++)
                binTotals[d] += hist[bOff + d];
        }

        Span<uint> cumulative = stackalloc uint[256];
        uint running = 0;
        for (int d = 0; d < 256; d++)
        {
            cumulative[d] = running;
            running += binTotals[d];
        }

        for (int b = 0; b < numBlocks; b++)
        {
            int bOff = b * 256;
            for (int d = 0; d < 256; d++)
            {
                offsets[bOff + d] = cumulative[d];
                cumulative[d] += hist[bOff + d];
            }
        }
    }

    public void Dispose()
    {
        _histograms.Dispose();
        _offsets.Dispose();
    }
}