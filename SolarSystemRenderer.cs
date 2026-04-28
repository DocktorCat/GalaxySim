using ComputeSharp;
using GalaxySim.Core.Shaders;
using GalaxySim.Core.Simulation;
using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GalaxySim;

internal sealed class SolarSystemRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteTexture2D<Bgra32, Float4> _output;
    private readonly ReadBackTexture2D<Bgra32> _readback;
    private readonly ReadWriteBuffer<Float4> _planetData;
    private readonly ReadWriteBuffer<Float4> _planetExtra;
    private readonly ReadWriteBuffer<Float4> _planetRings;
    private readonly ReadWriteBuffer<Float4> _beltData;
    private readonly ReadWriteBuffer<Float4> _moonData;
    private readonly ReadWriteBuffer<Float4> _moonExtra;
    private readonly byte[] _pixels;
    private readonly float _seed;
    private readonly Float3 _starDarkColor;
    private readonly Float3 _starColor;
    private readonly Float3 _starHotColor;
    private readonly Float3 _starWhiteColor;
    private readonly float _isBlueStar;
    private readonly float _habitableZoneAu;

    public SolarSystemRenderer(SelectedStarInfo star, int width, int height)
    {
        Width = Math.Max(420, width);
        Height = Math.Max(260, height);
        Bitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
        _device = GraphicsDevice.GetDefault();
        _output = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        _readback = _device.AllocateReadBackTexture2D<Bgra32>(Width, Height);
        _pixels = new byte[Width * Height * 4];
        Planets = SolarSystemCatalog.Generate(star);
        AsteroidBelts = SolarSystemCatalog.GenerateAsteroidBelts(star, Planets);

        _seed = star.Index * 0.021f + star.TemperatureClass * 5.13f;
        _starDarkColor = ToFloat3(DarkColorForStar(star.ModelKey));
        _starColor = ToFloat3(StarModelFactory.ColorForStar(star.ModelKey));
        _starHotColor = ToFloat3(HotColorForStar(star.ModelKey));
        _starWhiteColor = star.ModelKey is "blue_giant" or "blue_white_star"
            ? new Float3(0.96f, 0.99f, 1.0f)
            : new Float3(1.0f, 0.96f, 0.72f);
        _isBlueStar = star.ModelKey is "blue_giant" or "blue_white_star" ? 1f : 0f;
        _habitableZoneAu = star.HabitableZoneAu;

        var planetData = new Float4[9];
        var planetExtra = new Float4[9];
        var planetRings = new Float4[9];
        var moonData = new Float4[32];
        var moonExtra = new Float4[32];
        var moonInfos = new MoonInfo[32];
        int moonCount = 0;
        for (int i = 0; i < Planets.Length && i < 9; i++)
        {
            PlanetInfo p = Planets[i];
            float hotGas = p.TypeCode == 2
                ? Math.Clamp((_habitableZoneAu * 0.62f - p.OrbitAu) / Math.Max(_habitableZoneAu * 0.42f, 0.04f), 0f, 1f)
                : 0f;
            planetData[i] = new Float4(p.OrbitRenderRadius, p.Eccentricity, p.VisualRadius, p.TypeCode);
            planetExtra[i] = new Float4(p.Phase, p.OrbitSpeed, p.Seed, hotGas);
            planetRings[i] = new Float4(p.HasRings ? 1f : 0f, p.RingInnerRadius, p.RingOuterRadius, p.RingTilt);

            for (int m = 0; m < p.MoonCount && moonCount < moonData.Length; m++, moonCount++)
            {
                float h0 = Hash01(star.Index * 1009 + i * 197 + m * 53 + 11);
                float h1 = Hash01(star.Index * 1217 + i * 211 + m * 67 + 17);
                float h2 = Hash01(star.Index * 1471 + i * 229 + m * 71 + 23);
                float orbit = p.VisualRadius * (2.35f + m * 1.18f + h0 * 1.00f);
                float radius = Math.Clamp(p.VisualRadius * (0.13f + h1 * 0.13f), 0.006f, 0.020f);
                float phase = h2 * MathF.Tau;
                float speed = (0.62f + h0 * 0.56f) / MathF.Sqrt(m + 1.4f);
                float seed = h0 * 41f + h1 * 17f;
                float tilt = -0.45f + h2 * 0.90f;
                bool isIcy = h1 < 0.35f;
                moonData[moonCount] = new Float4(i, orbit, radius, isIcy ? 1f : 0f);
                moonExtra[moonCount] = new Float4(phase, speed, seed, tilt);
                moonInfos[moonCount] = new MoonInfo(
                    moonCount,
                    i,
                    CelestialNameGenerator.MoonName(star.Index, i, m, isIcy),
                    isIcy ? "Ледяной спутник" : "Каменистый спутник",
                    isIcy
                        ? "Небольшое холодное тело с ледяной корой, кратерами и слабой отражающей поверхностью."
                        : "Небольшой каменистый спутник с кратерированной поверхностью и слабой собственной геологией.",
                    orbit,
                    radius,
                    phase,
                    speed,
                    seed,
                    tilt,
                    isIcy);
            }
        }

        var beltData = new Float4[2];
        for (int i = 0; i < AsteroidBelts.Length && i < 2; i++)
        {
            AsteroidBeltInfo belt = AsteroidBelts[i];
            beltData[i] = new Float4(belt.OrbitRenderRadius, belt.Width, belt.Density, belt.Seed);
        }

        _planetData = _device.AllocateReadWriteBuffer(planetData);
        _planetExtra = _device.AllocateReadWriteBuffer(planetExtra);
        _planetRings = _device.AllocateReadWriteBuffer(planetRings);
        _beltData = _device.AllocateReadWriteBuffer(beltData);
        _moonData = _device.AllocateReadWriteBuffer(moonData);
        _moonExtra = _device.AllocateReadWriteBuffer(moonExtra);
        MoonCount = moonCount;
        Moons = moonInfos[..moonCount];
    }

    public int Width { get; }

    public int Height { get; }

    public WriteableBitmap Bitmap { get; }

    public PlanetInfo[] Planets { get; }

    public AsteroidBeltInfo[] AsteroidBelts { get; }

    public int MoonCount { get; }

    public MoonInfo[] Moons { get; }

    public void Render(float orbitTime, float effectTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int selectedIndex, int selectedMoonIndex, int focusIndex, int qualityLevel)
    {
        RenderQualityProfile profile = RenderQualityProfile.For(qualityLevel);
        _device.For(Width, Height, new SolarSystemRenderShader(
            _output,
            _planetData,
            _planetExtra,
            _planetRings,
            _beltData,
            _moonData,
            _moonExtra,
            Width,
            Height,
            orbitTime,
            effectTime,
            Math.Clamp(zoom, 0.08f, 12.0f),
            _seed,
            Planets.Length,
            AsteroidBelts.Length,
            MoonCount,
            _starDarkColor,
            _starColor,
            _starHotColor,
            _starWhiteColor,
            _isBlueStar,
            _habitableZoneAu,
            is3D ? 1 : 0,
            realismMode ? 1 : 0,
            yaw,
            pitch,
            selectedIndex,
            selectedMoonIndex,
            focusIndex,
            profile.TextureQuality,
            profile.ShadowQuality,
            profile.DebrisQuality));

        _output.CopyTo(_readback);
        CopyReadbackToBuffer(_readback, _pixels, Width, Height);
        Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), _pixels, Width * 4, 0);
    }

    public void Dispose()
    {
        _planetData.Dispose();
        _planetExtra.Dispose();
        _planetRings.Dispose();
        _beltData.Dispose();
        _moonData.Dispose();
        _moonExtra.Dispose();
        _output.Dispose();
        _readback.Dispose();
    }

    public int PickObject(double screenX, double screenY, double viewWidth, double viewHeight, float orbitTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int focusIndex)
    {
        if (viewWidth <= 0 || viewHeight <= 0)
            return -1;

        double scale = Math.Min(viewWidth / Width, viewHeight / Height);
        double imageWidth = Width * scale;
        double imageHeight = Height * scale;
        double imageLeft = (viewWidth - imageWidth) * 0.5;
        double imageTop = (viewHeight - imageHeight) * 0.5;
        double px = (screenX - imageLeft) / imageWidth * Width;
        double py = (screenY - imageTop) / imageHeight * Height;
        if (px < 0 || py < 0 || px >= Width || py >= Height)
            return -1;

        float aspect = Width / (float)Height;
        float normalizedZoom = Math.Clamp(zoom, 0.08f, 12.0f);
        float uvX = ((float)((px + 0.5) / Width) * 2f - 1f) * aspect / normalizedZoom;
        float uvY = ((float)((py + 0.5) / Height) * 2f - 1f) / normalizedZoom;

        if (is3D)
            return PickObject3D(uvX, uvY, orbitTime, normalizedZoom, realismMode, yaw, pitch, focusIndex);

        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < Planets.Length; i++)
        {
            PlanetInfo p = Planets[i];
            (float x, float y, float scaleMul, _) = ProjectPlanet(p, orbitTime, is3D, realismMode, yaw, pitch);
            float r = VisualRadius(p.VisualRadius, realismMode) * scaleMul;
            float dx = uvX - x;
            float dy = uvY - y;
            float score = dx * dx + dy * dy;
            float hit = realismMode ? MathF.Max(r * 5.5f, 0.030f) : MathF.Max(r * 1.7f, 0.028f);
            if (score > hit * hit)
                continue;

            if (score < bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        return best;
    }

    private int PickObject3D(float uvX, float uvY, float orbitTime, float zoom, bool realismMode, float yaw, float pitch, int focusIndex)
    {
        Vector3 focus = FocusPosition(focusIndex, orbitTime, realismMode);
        Vector3 camPos = focus + CameraOffset(yaw, pitch, zoom, realismMode);
        Vector3 forward = Vector3.Normalize(focus - camPos);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
        Vector3 rayDir = Vector3.Normalize(forward + right * uvX * 0.72f - up * uvY * 0.72f);

        float starT = RaySphere(camPos, rayDir, Vector3.Zero, 0.105f);
        int best = -1;
        float bestT = float.MaxValue;

        for (int i = 0; i < Planets.Length; i++)
        {
            PlanetInfo p = Planets[i];
            Vector3 center = PlanetWorldPosition(p, orbitTime, realismMode);
            float radius = VisualRadius(p.VisualRadius, realismMode);
            float hitRadius = realismMode ? MathF.Max(radius * 5.0f, 0.035f) : radius * 1.35f;
            float t = RaySphere(camPos, rayDir, center, hitRadius);
            if (t <= 0f || t >= bestT)
                continue;

            if (starT > 0f && starT < t)
                continue;

            best = i;
            bestT = t;
        }

        for (int i = 0; i < Moons.Length; i++)
        {
            MoonInfo moon = Moons[i];
            PlanetInfo parent = Planets[moon.ParentPlanetIndex];
            Vector3 center = MoonWorldPosition(moon, parent, orbitTime, realismMode);
            float radius = VisualRadius(moon.VisualRadius, realismMode);
            float hitRadius = realismMode ? MathF.Max(radius * 5.0f, 0.020f) : radius;
            float t = RaySphere(camPos, rayDir, center, hitRadius);
            if (t <= 0f || t >= bestT)
                continue;

            if (starT > 0f && starT < t)
                continue;

            best = 100 + i;
            bestT = t;
        }

        return best;
    }

    private static (float x, float y, float scaleMul, float depth) ProjectPlanet(PlanetInfo p, float orbitTime, bool is3D, bool realismMode, float yaw, float pitch)
    {
        float orbit = OrbitRadius(p.OrbitRenderRadius, realismMode);
        float phase = p.Phase + orbitTime * p.OrbitSpeed;
        float x = MathF.Cos(phase) * orbit;
        float z = MathF.Sin(phase) * orbit * p.Eccentricity;
        if (!is3D)
            return (x, z, 1f, 0f);

        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        float rx = x * cy + z * sy;
        float rz = -x * sy + z * cy;
        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);
        float ry = -rz * sp;
        float depth = rz * cp;
        float perspective = 1f / MathF.Max(0.58f, 1f + depth * 0.38f);
        return (rx * perspective, ry * perspective, perspective, depth);
    }

    private Vector3 FocusPosition(int focusIndex, float orbitTime, bool realismMode)
    {
        if (focusIndex >= 0 && focusIndex < Planets.Length)
            return PlanetWorldPosition(Planets[focusIndex], orbitTime, realismMode);

        int moonIndex = focusIndex - 100;
        if (moonIndex >= 0 && moonIndex < Moons.Length)
        {
            MoonInfo moon = Moons[moonIndex];
            return MoonWorldPosition(moon, Planets[moon.ParentPlanetIndex], orbitTime, realismMode);
        }

        return Vector3.Zero;
    }

    private static Vector3 CameraOffset(float yaw, float pitch, float zoom, bool realismMode)
    {
        float close = Math.Clamp((zoom - 1f) / 11f, 0f, 1f);
        float overview = zoom < 1f ? 1f / MathF.Sqrt(MathF.Max(zoom, 0.07f)) : 1f;
        float dist = (realismMode ? 13.2f : 3.20f) * overview - close * (realismMode ? 12.50f : 2.82f);
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);
        return new Vector3(sy * cp * dist, sp * dist, cy * cp * dist);
    }

    private static Vector3 PlanetWorldPosition(PlanetInfo p, float orbitTime, bool realismMode)
    {
        float orbit = OrbitRadius(p.OrbitRenderRadius, realismMode);
        float phase = p.Phase + orbitTime * p.OrbitSpeed;
        return new Vector3(
            MathF.Cos(phase) * orbit,
            0f,
            MathF.Sin(phase) * orbit * p.Eccentricity);
    }

    private static Vector3 MoonWorldPosition(MoonInfo moon, PlanetInfo parent, float orbitTime, bool realismMode)
    {
        Vector3 parentPosition = PlanetWorldPosition(parent, orbitTime, realismMode);
        float phase = moon.Phase + orbitTime * moon.OrbitSpeed;
        float c = MathF.Cos(phase);
        float s = MathF.Sin(phase);
        float ct = MathF.Cos(moon.Tilt);
        float st = MathF.Sin(moon.Tilt);
        return parentPosition + new Vector3(c * moon.OrbitRenderRadius, s * st * moon.OrbitRenderRadius, s * ct * moon.OrbitRenderRadius);
    }

    private static float OrbitRadius(float renderOrbit, bool realismMode)
        => realismMode ? renderOrbit * 4.75f : renderOrbit;

    private static float VisualRadius(float renderRadius, bool realismMode)
        => realismMode ? MathF.Max(renderRadius * 0.18f, 0.0032f) : renderRadius;

    private static float RaySphere(Vector3 rayOrigin, Vector3 rayDir, Vector3 center, float radius)
    {
        Vector3 oc = rayOrigin - center;
        float b = Vector3.Dot(oc, rayDir);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float h = b * b - c;
        if (h < 0f)
            return -1f;

        h = MathF.Sqrt(h);
        float t = -b - h;
        if (t > 0.001f)
            return t;

        t = -b + h;
        return t > 0.001f ? t : -1f;
    }

    private static float Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x / (float)uint.MaxValue;
        }
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

    public readonly record struct RenderQualityProfile(int TextureQuality, int ShadowQuality, int DebrisQuality)
    {
        public static RenderQualityProfile For(int quality) => Math.Clamp(quality, 0, 3) switch
        {
            0 => new RenderQualityProfile(0, 0, 0),
            1 => new RenderQualityProfile(1, 1, 1),
            2 => new RenderQualityProfile(2, 2, 2),
            _ => new RenderQualityProfile(3, 3, 3),
        };

        public string Description => $"текстуры {TextureQuality}, тени {ShadowQuality}, астероиды {DebrisQuality}";

        public string DescriptionEn => $"textures {TextureQuality}, shadows {ShadowQuality}, debris {DebrisQuality}";
    }
}
