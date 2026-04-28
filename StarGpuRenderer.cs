using ComputeSharp;
using GalaxySim.Core.Shaders;
using GalaxySim.Core.Simulation;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GalaxySim;

internal sealed class StarGpuRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<Float4> _hdrScene;
    private readonly ReadWriteBuffer<Float4>[] _bloomMips;
    private readonly int[] _bloomWidths;
    private readonly int[] _bloomHeights;
    private readonly ReadWriteTexture2D<Bgra32, Float4> _output;
    private readonly ReadBackTexture2D<Bgra32> _readback;
    private readonly byte[] _pixels;
    private readonly Float3 _darkColor;
    private readonly Float3 _midColor;
    private readonly Float3 _hotColor;
    private readonly Float3 _whiteColor;
    private readonly float _seed;
    private readonly float _isBlueStar;

    public StarGpuRenderer(SelectedStarInfo star, int width, int height)
    {
        Width = Math.Max(320, width);
        Height = Math.Max(240, height);
        Bitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);

        _device = GraphicsDevice.GetDefault();
        _hdrScene = _device.AllocateReadWriteBuffer<Float4>(Width * Height);
        _output = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        _readback = _device.AllocateReadBackTexture2D<Bgra32>(Width, Height);
        _pixels = new byte[Width * Height * 4];

        const int mipLevels = 5;
        _bloomMips = new ReadWriteBuffer<Float4>[mipLevels];
        _bloomWidths = new int[mipLevels];
        _bloomHeights = new int[mipLevels];
        int mw = Width;
        int mh = Height;
        for (int i = 0; i < mipLevels; i++)
        {
            _bloomWidths[i] = Math.Max(mw, 1);
            _bloomHeights[i] = Math.Max(mh, 1);
            _bloomMips[i] = _device.AllocateReadWriteBuffer<Float4>(_bloomWidths[i] * _bloomHeights[i]);
            mw = Math.Max(mw / 2, 1);
            mh = Math.Max(mh / 2, 1);
        }

        _darkColor = ToFloat3(DarkColorForStar(star.ModelKey));
        _midColor = ToFloat3(StarModelFactory.ColorForStar(star.ModelKey));
        _hotColor = ToFloat3(HotColorForStar(star.ModelKey));
        _whiteColor = star.ModelKey is "blue_giant" or "blue_white_star"
            ? new Float3(0.96f, 0.99f, 1.0f)
            : new Float3(1.0f, 0.96f, 0.72f);
        _seed = star.Index * 0.017f + star.TemperatureClass * 3.7f;
        _isBlueStar = star.ModelKey is "blue_giant" or "blue_white_star" ? 1f : 0f;
    }

    public int Width { get; }

    public int Height { get; }

    public WriteableBitmap Bitmap { get; }

    public void Render(float time, float yaw, float pitch, float zoom)
    {
        _device.For(Width, Height, new StarRenderShader(
            _hdrScene,
            Width,
            Height,
            time,
            yaw,
            pitch,
            Math.Clamp(zoom, 0.35f, 2.4f),
            _darkColor,
            _midColor,
            _hotColor,
            _whiteColor,
            _seed,
            _isBlueStar));

        _device.For(Width, Height, new BloomThresholdShader(_hdrScene, _bloomMips[0], Width, Height, 1.05f, 0.68f));
        for (int i = 0; i < _bloomMips.Length - 1; i++)
        {
            _device.For(_bloomWidths[i + 1], _bloomHeights[i + 1], new BloomDownsampleShader(
                _bloomMips[i],
                _bloomMips[i + 1],
                _bloomWidths[i],
                _bloomHeights[i],
                _bloomWidths[i + 1],
                _bloomHeights[i + 1]));
        }

        for (int i = _bloomMips.Length - 1; i > 0; i--)
        {
            _device.For(_bloomWidths[i - 1], _bloomHeights[i - 1], new BloomUpsampleShader(
                _bloomMips[i],
                _bloomMips[i - 1],
                _bloomWidths[i],
                _bloomHeights[i],
                _bloomWidths[i - 1],
                _bloomHeights[i - 1],
                0.72f));
        }

        _device.For(Width, Height, new TonemapShader(
            _hdrScene,
            _bloomMips[0],
            _output,
            Width,
            Height,
            0.82f,
            0.72f,
            0,
            Float4.Zero,
            Float4.Zero,
            0f,
            0f,
            0f,
            0.10f));

        _output.CopyTo(_readback);
        CopyReadbackToBuffer(_readback, _pixels, Width, Height);
        Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), _pixels, Width * 4, 0);
    }

    public void Dispose()
    {
        _hdrScene.Dispose();
        foreach (ReadWriteBuffer<Float4> mip in _bloomMips)
            mip.Dispose();
        _output.Dispose();
        _readback.Dispose();
    }

    private static Float3 ToFloat3(Color color)
    {
        return new Float3(color.R / 255f, color.G / 255f, color.B / 255f);
    }

    private static Color DarkColorForStar(string modelKey) => modelKey switch
    {
        "red_dwarf" => Color.FromRgb(95, 12, 8),
        "orange_star" => Color.FromRgb(116, 31, 8),
        "yellow_star" => Color.FromRgb(126, 44, 6),
        "blue_white_star" => Color.FromRgb(28, 67, 138),
        "blue_giant" => Color.FromRgb(22, 70, 150),
        _ => Color.FromRgb(112, 35, 8),
    };

    private static Color HotColorForStar(string modelKey) => modelKey switch
    {
        "red_dwarf" => Color.FromRgb(255, 112, 45),
        "orange_star" => Color.FromRgb(255, 170, 54),
        "yellow_star" => Color.FromRgb(255, 218, 80),
        "blue_white_star" => Color.FromRgb(190, 226, 255),
        "blue_giant" => Color.FromRgb(160, 215, 255),
        _ => Color.FromRgb(255, 204, 72),
    };

    private static unsafe void CopyReadbackToBuffer(ReadBackTexture2D<Bgra32> readback, byte[] destination, int width, int height)
    {
        Bgra32* src = readback.View.DangerousGetAddressAndByteStride(out int srcStride);
        int rowBytes = width * 4;
        fixed (byte* dst = destination)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = (byte*)src + y * srcStride;
                byte* dstRow = dst + y * rowBytes;
                Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
            }
        }
    }
}
