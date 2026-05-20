using ComputeSharp;
using GalaxySim.Core.Shaders;
using GalaxySim.Core.Simulation;
using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GalaxySim;

internal sealed class SolarSystemRenderer : IDisposable
{
    public const int EmptyObjectId = int.MinValue;
    public const int SecondaryStarId = -2;
    private static readonly object RenderSync = new();

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
    private readonly int _secondaryStarEnabled;
    private readonly float _secondaryOrbitRenderRadius;
    private readonly float _secondaryVisualRadius;
    private readonly float _secondaryPhase;
    private readonly Float3 _secondaryStarColor;
    private readonly Float3 _secondaryStarHotColor;
    private readonly Float3 _secondaryStarWhiteColor;

    public SolarSystemRenderer(SelectedStarInfo star, int width, int height)
    {
        Width = Math.Max(420, width);
        Height = Math.Max(260, height);
        Bitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
        _device = GraphicsDevice.GetDefault();
        _output = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        _readback = _device.AllocateReadBackTexture2D<Bgra32>(Width, Height);
        _pixels = new byte[Width * Height * 4];
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
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
        _habitableZoneAu = SolarSystemCatalog.EffectiveHabitableZoneAu(star);
        _secondaryStarEnabled = stellar.IsBinary ? 1 : 0;
        _secondaryOrbitRenderRadius = stellar.IsBinary ? SecondaryOrbitRenderRadius(stellar, _habitableZoneAu, Planets) : 0f;
        _secondaryVisualRadius = stellar.IsBinary ? SecondaryVisualRadius(stellar) : 0f;
        _secondaryPhase = Hash01(star.Index * 2753 + 331) * MathF.Tau;
        _secondaryStarColor = stellar.IsBinary ? ToFloat3(StarModelFactory.ColorForStar(stellar.SecondaryModelKey)) : Float3.Zero;
        _secondaryStarHotColor = stellar.IsBinary ? ToFloat3(HotColorForStar(stellar.SecondaryModelKey)) : Float3.Zero;
        _secondaryStarWhiteColor = stellar.SecondaryModelKey is "blue_giant" or "blue_white_star"
            ? new Float3(0.96f, 0.99f, 1.0f)
            : new Float3(1.0f, 0.96f, 0.72f);

        var planetData = new Float4[9];
        var planetExtra = new Float4[9];
        var planetRings = new Float4[9];
        var moonData = new Float4[32];
        var moonExtra = new Float4[32];
        MoonInfo[] moonInfos = SolarSystemCatalog.GenerateMoons(star, Planets);
        for (int i = 0; i < Planets.Length && i < 9; i++)
        {
            PlanetInfo p = Planets[i];
            float hotGas = p.TypeCode == 2
                ? Math.Clamp((_habitableZoneAu * 0.62f - p.OrbitAu) / Math.Max(_habitableZoneAu * 0.42f, 0.04f), 0f, 1f)
                : 0f;
            planetData[i] = new Float4(p.OrbitRenderRadius, p.Eccentricity, p.VisualRadius, p.TypeCode);
            planetExtra[i] = new Float4(p.Phase, p.OrbitSpeed, p.Seed, hotGas);
            planetRings[i] = new Float4(p.HasRings ? 1f : 0f, p.RingInnerRadius, p.RingOuterRadius, p.RingTilt);
        }

        for (int i = 0; i < moonInfos.Length && i < moonData.Length; i++)
        {
            MoonInfo moon = moonInfos[i];
            moonData[i] = new Float4(moon.ParentPlanetIndex, moon.OrbitRenderRadius, moon.VisualRadius, moon.IsIcy ? 1f : 0f);
            moonExtra[i] = new Float4(moon.Phase, moon.OrbitSpeed, moon.Seed, moon.Tilt);
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
        MoonCount = Math.Min(moonInfos.Length, moonData.Length);
        Moons = moonInfos[..MoonCount];
    }

    public int Width { get; }

    public int Height { get; }

    public WriteableBitmap Bitmap { get; }

    public PlanetInfo[] Planets { get; }

    public AsteroidBeltInfo[] AsteroidBelts { get; }

    public int MoonCount { get; }

    public MoonInfo[] Moons { get; }

    public bool HasSecondaryStar => _secondaryStarEnabled != 0;

    public void Render(float orbitTime, float effectTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int selectedIndex, int selectedMoonIndex, int focusIndex, bool showOrbits, bool showHabitableZone, int qualityLevel)
    {
        RenderToBuffer(
            orbitTime,
            effectTime,
            zoom,
            is3D,
            realismMode,
            yaw,
            pitch,
            selectedIndex,
            selectedMoonIndex,
            focusIndex,
            showOrbits,
            showHabitableZone,
            qualityLevel,
            _pixels);
        Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), _pixels, Width * 4, 0);
    }

    public bool TryRender(float orbitTime, float effectTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int selectedIndex, int selectedMoonIndex, int focusIndex, bool showOrbits, bool showHabitableZone, int qualityLevel)
    {
        if (!TryRenderToBuffer(
                orbitTime,
                effectTime,
                zoom,
                is3D,
                realismMode,
                yaw,
                pitch,
                selectedIndex,
                selectedMoonIndex,
                focusIndex,
                showOrbits,
                showHabitableZone,
                qualityLevel,
                _pixels))
        {
            return false;
        }

        Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), _pixels, Width * 4, 0);
        return true;
    }

    public void RenderToBuffer(float orbitTime, float effectTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int selectedIndex, int selectedMoonIndex, int focusIndex, bool showOrbits, bool showHabitableZone, int qualityLevel, byte[] destination)
    {
        if (destination.Length < Width * Height * 4)
            throw new ArgumentException("Destination buffer is too small for the solar system frame.", nameof(destination));

        lock (RenderSync)
        {
            RenderToBufferCore(
                orbitTime,
                effectTime,
                zoom,
                is3D,
                realismMode,
                yaw,
                pitch,
                selectedIndex,
                selectedMoonIndex,
                focusIndex,
                showOrbits,
                showHabitableZone,
                qualityLevel,
                destination);
        }
    }

    public bool TryRenderToBuffer(float orbitTime, float effectTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int selectedIndex, int selectedMoonIndex, int focusIndex, bool showOrbits, bool showHabitableZone, int qualityLevel, byte[] destination)
    {
        if (destination.Length < Width * Height * 4)
            throw new ArgumentException("Destination buffer is too small for the solar system frame.", nameof(destination));

        if (!Monitor.TryEnter(RenderSync))
            return false;

        try
        {
            RenderToBufferCore(
                orbitTime,
                effectTime,
                zoom,
                is3D,
                realismMode,
                yaw,
                pitch,
                selectedIndex,
                selectedMoonIndex,
                focusIndex,
                showOrbits,
                showHabitableZone,
                qualityLevel,
                destination);
            return true;
        }
        finally
        {
            Monitor.Exit(RenderSync);
        }
    }

    private void RenderToBufferCore(float orbitTime, float effectTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int selectedIndex, int selectedMoonIndex, int focusIndex, bool showOrbits, bool showHabitableZone, int qualityLevel, byte[] destination)
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
            Math.Clamp(zoom, 0.08f, 220.0f),
            _seed,
            Planets.Length,
            AsteroidBelts.Length,
            MoonCount,
            _starDarkColor,
            _starColor,
            _starHotColor,
            _starWhiteColor,
            _isBlueStar,
            _secondaryStarEnabled,
            _secondaryOrbitRenderRadius,
            _secondaryVisualRadius,
            _secondaryPhase,
            _secondaryStarColor,
            _secondaryStarHotColor,
            _secondaryStarWhiteColor,
            _habitableZoneAu,
            is3D ? 1 : 0,
            realismMode ? 1 : 0,
            yaw,
            pitch,
            selectedIndex,
            selectedMoonIndex,
            focusIndex,
            showOrbits ? 1 : 0,
            showHabitableZone ? 1 : 0,
            profile.TextureQuality,
            profile.ShadowQuality,
            profile.DebrisQuality));

        _output.CopyTo(_readback);
        CopyReadbackToBuffer(_readback, destination, Width, Height);
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
            return EmptyObjectId;

        double scale = Math.Min(viewWidth / Width, viewHeight / Height);
        double imageWidth = Width * scale;
        double imageHeight = Height * scale;
        double imageLeft = (viewWidth - imageWidth) * 0.5;
        double imageTop = (viewHeight - imageHeight) * 0.5;
        double px = (screenX - imageLeft) / imageWidth * Width;
        double py = (screenY - imageTop) / imageHeight * Height;
        if (px < 0 || py < 0 || px >= Width || py >= Height)
            return EmptyObjectId;

        float aspect = Width / (float)Height;
        float normalizedZoom = Math.Clamp(zoom, 0.08f, 220.0f);
        float uvX = ((float)((px + 0.5) / Width) * 2f - 1f) * aspect;
        float uvY = (float)((py + 0.5) / Height) * 2f - 1f;
        if (!is3D)
        {
            uvX /= normalizedZoom;
            uvY /= normalizedZoom;
        }

        if (is3D)
            return PickObject3D(uvX, uvY, orbitTime, normalizedZoom, realismMode, yaw, pitch, focusIndex);

        int best = EmptyObjectId;
        float bestScore = float.MaxValue;
        if (HasSecondaryStar)
        {
            Vector3 secondary = SecondaryStarWorldPosition(orbitTime, realismMode);
            float dx = uvX - secondary.X;
            float dy = uvY - secondary.Z;
            float hit = MathF.Max(VisualRadius(_secondaryVisualRadius, realismMode) * 1.45f, 0.020f);
            float score = dx * dx + dy * dy;
            if (score <= hit * hit)
            {
                best = SecondaryStarId;
                bestScore = score;
            }
        }

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

    public bool TryGetObjectScreenPosition(int id, double viewWidth, double viewHeight, float orbitTime, float zoom, bool is3D, bool realismMode, float yaw, float pitch, int focusIndex, out Point point)
    {
        point = default;
        if (viewWidth <= 0 || viewHeight <= 0)
            return false;

        if (id == EmptyObjectId)
            return false;

        float normalizedZoom = Math.Clamp(zoom, 0.08f, 220.0f);
        float aspect = Width / (float)Height;
        float uvX;
        float uvY;

        if (id == SecondaryStarId)
        {
            if (!HasSecondaryStar)
                return false;

            Vector3 world = SecondaryStarWorldPosition(orbitTime, realismMode);
            (uvX, uvY) = is3D
                ? ProjectWorldToUv(world, orbitTime, normalizedZoom, realismMode, yaw, pitch, focusIndex)
                : (world.X, world.Z);
        }
        else if (id < 0)
        {
            (uvX, uvY) = is3D
                ? ProjectWorldToUv(Vector3.Zero, orbitTime, normalizedZoom, realismMode, yaw, pitch, focusIndex)
                : (0f, 0f);
        }
        else if (id >= 100)
        {
            int moonIndex = id - 100;
            if (moonIndex < 0 || moonIndex >= Moons.Length)
                return false;

            MoonInfo moon = Moons[moonIndex];
            PlanetInfo parent = Planets[moon.ParentPlanetIndex];
            if (is3D)
            {
                Vector3 world = MoonWorldPosition(moon, parent, orbitTime, realismMode);
                (uvX, uvY) = ProjectWorldToUv(world, orbitTime, normalizedZoom, realismMode, yaw, pitch, focusIndex);
            }
            else
            {
                Vector3 world = MoonWorldPosition(moon, parent, orbitTime, realismMode);
                uvX = world.X;
                uvY = world.Z;
            }
        }
        else
        {
            if (id >= Planets.Length)
                return false;

            PlanetInfo planet = Planets[id];
            if (is3D)
            {
                Vector3 world = PlanetWorldPosition(planet, orbitTime, realismMode);
                (uvX, uvY) = ProjectWorldToUv(world, orbitTime, normalizedZoom, realismMode, yaw, pitch, focusIndex);
            }
            else
            {
                (float x, float y, _, _) = ProjectPlanet(planet, orbitTime, is3D, realismMode, yaw, pitch);
                uvX = x;
                uvY = y;
            }
        }

        if (float.IsNaN(uvX) || float.IsNaN(uvY) || float.IsInfinity(uvX) || float.IsInfinity(uvY))
            return false;

        double ndcX = is3D ? uvX / aspect : uvX * normalizedZoom / aspect;
        double ndcY = is3D ? uvY : uvY * normalizedZoom;
        if (ndcX < -1.60 || ndcX > 1.60 || ndcY < -1.60 || ndcY > 1.60)
            return false;

        double scale = Math.Min(viewWidth / Width, viewHeight / Height);
        double imageWidth = Width * scale;
        double imageHeight = Height * scale;
        double imageLeft = (viewWidth - imageWidth) * 0.5;
        double imageTop = (viewHeight - imageHeight) * 0.5;
        point = new Point(
            imageLeft + (ndcX * 0.5 + 0.5) * imageWidth,
            imageTop + (ndcY * 0.5 + 0.5) * imageHeight);
        return true;
    }

    public float ViewYawFromStarSide(int id, float orbitTime, bool realismMode)
    {
        Vector3 center = id switch
        {
            SecondaryStarId => SecondaryStarWorldPosition(orbitTime, realismMode),
            >= 0 and < 100 when id < Planets.Length => PlanetWorldPosition(Planets[id], orbitTime, realismMode),
            >= 100 when id - 100 >= 0 && id - 100 < Moons.Length => MoonWorldPosition(
                Moons[id - 100],
                Planets[Moons[id - 100].ParentPlanetIndex],
                orbitTime,
                realismMode),
            _ => new Vector3(0f, 0f, -1f),
        };

        if (center.LengthSquared() < 1e-6f)
            return 0f;

        return MathF.Atan2(-center.X, -center.Z);
    }

    public float PreviewViewYaw(int id, float orbitTime, bool realismMode)
    {
        float starSideYaw = ViewYawFromStarSide(id, orbitTime, realismMode);
        if (id < 100)
            return starSideYaw;

        int moonIndex = id - 100;
        if (moonIndex < 0 || moonIndex >= Moons.Length)
            return starSideYaw;

        MoonInfo moon = Moons[moonIndex];
        PlanetInfo parent = Planets[moon.ParentPlanetIndex];
        Vector3 parentCenter = PlanetWorldPosition(parent, orbitTime, realismMode);
        Vector3 moonCenter = MoonWorldPosition(moon, parent, orbitTime, realismMode);
        Vector3 fromParent = moonCenter - parentCenter;
        if (fromParent.LengthSquared() < 1e-6f)
            return starSideYaw + 0.62f;

        return MathF.Atan2(fromParent.X, fromParent.Z) + 0.42f;
    }

    private int PickObject3D(float uvX, float uvY, float orbitTime, float zoom, bool realismMode, float yaw, float pitch, int focusIndex)
    {
        Vector3 focus = FocusPosition(focusIndex, orbitTime, realismMode);
        Vector3 camPos = focus + CameraOffset(yaw, pitch, zoom, realismMode);
        Vector3 forward = Vector3.Normalize(focus - camPos);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
        Vector3 rayDir = Vector3.Normalize(forward + right * uvX * 0.50f - up * uvY * 0.50f);

        float starT = RaySphere(camPos, rayDir, Vector3.Zero, 0.105f);
        int best = EmptyObjectId;
        float bestT = float.MaxValue;
        if (starT > 0f)
        {
            best = -1;
            bestT = starT;
        }

        if (HasSecondaryStar)
        {
            Vector3 center = SecondaryStarWorldPosition(orbitTime, realismMode);
            float radius = VisualRadius(_secondaryVisualRadius, realismMode);
            float t = RaySphere(camPos, rayDir, center, MathF.Max(radius * 1.25f, 0.018f));
            if (t > 0f && t < bestT)
            {
                best = SecondaryStarId;
                bestT = t;
            }
        }

        for (int i = 0; i < Planets.Length; i++)
        {
            PlanetInfo p = Planets[i];
            Vector3 center = PlanetWorldPosition(p, orbitTime, realismMode);
            float radius = VisualRadius(p.VisualRadius, realismMode);
            float hitRadius = realismMode
                ? MathF.Max(radius * 4.6f, 0.055f)
                : MathF.Max(radius * 2.8f, 0.040f);
            float t = RaySphere(camPos, rayDir, center, hitRadius);
            if (t <= 0f || t >= bestT)
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
            float hitRadius = realismMode
                ? MathF.Max(radius * 4.2f, 0.026f)
                : MathF.Max(radius * 2.8f, 0.020f);
            float t = RaySphere(camPos, rayDir, center, hitRadius);
            if (t <= 0f || t >= bestT)
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

    private static (float x, float y, float scaleMul, float depth) ProjectMoon(MoonInfo moon, PlanetInfo parent, float orbitTime, bool realismMode, float yaw, float pitch)
    {
        Vector3 world = MoonWorldPosition(moon, parent, orbitTime, realismMode);
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        float rx = world.X * cy + world.Z * sy;
        float rz = -world.X * sy + world.Z * cy;
        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);
        float ry = world.Y * cp - rz * sp;
        float depth = world.Y * sp + rz * cp;
        float perspective = 1f / MathF.Max(0.58f, 1f + depth * 0.38f);
        return (rx * perspective, ry * perspective, perspective, depth);
    }

    private (float uvX, float uvY) ProjectWorldToUv(Vector3 world, float orbitTime, float zoom, bool realismMode, float yaw, float pitch, int focusIndex)
    {
        Vector3 focus = FocusPosition(focusIndex, orbitTime, realismMode);
        Vector3 camPos = focus + CameraOffset(yaw, pitch, zoom, realismMode);
        Vector3 forward = Vector3.Normalize(focus - camPos);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
        Vector3 rel = world - camPos;
        float depth = MathF.Max(Vector3.Dot(rel, forward), 0.0001f);
        return (Vector3.Dot(rel, right) / (depth * 0.50f), -Vector3.Dot(rel, up) / (depth * 0.50f));
    }

    private Vector3 FocusPosition(int focusIndex, float orbitTime, bool realismMode)
    {
        if (focusIndex == SecondaryStarId)
            return SecondaryStarWorldPosition(orbitTime, realismMode);

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
        float z = MathF.Max(zoom, 0.08f);
        float dist = realismMode
            ? 12.0f / MathF.Pow(z, 1.06f) + 0.20f
            : 2.85f / MathF.Pow(z, 1.04f) + 0.024f;
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

    private Vector3 SecondaryStarWorldPosition(float orbitTime, bool realismMode)
    {
        float baseOrbit = MathF.Abs(_secondaryOrbitRenderRadius);
        float orbit = OrbitRadius(baseOrbit, realismMode);
        float speed = 0.060f / MathF.Sqrt(MathF.Max(baseOrbit, 0.18f));
        float phase = _secondaryPhase + orbitTime * speed;
        return new Vector3(
            MathF.Cos(phase) * orbit,
            0f,
            MathF.Sin(phase) * orbit * 0.92f);
    }

    private static float OrbitRadius(float renderOrbit, bool realismMode)
        => realismMode ? renderOrbit * 4.75f : renderOrbit;

    private static float VisualRadius(float renderRadius, bool realismMode)
        => realismMode ? MathF.Max(renderRadius * 0.42f, 0.0065f) : renderRadius;

    private static float SecondaryOrbitRenderRadius(StellarSystemProfile stellar, float effectiveHzAu, PlanetInfo[] planets)
    {
        float hzRender = Math.Clamp(0.46f + MathF.Pow(MathF.Max(effectiveHzAu, 0.03f), 0.86f) * 1.18f, 0.28f, 8.20f);
        float relative = stellar.SeparationAu / MathF.Max(effectiveHzAu, 0.05f);
        float mapped = hzRender * MathF.Pow(MathF.Max(relative, 0.12f), 0.52f);
        float outerPlanetOrbit = planets.Length == 0 ? hzRender : planets.Max(p => p.OrbitRenderRadius);
        return stellar.Layout switch
        {
            BinaryLayout.CloseBinary => -Math.Clamp(mapped * 0.42f, 0.10f, 0.22f),
            BinaryLayout.CircumbinaryCandidate => -Math.Clamp(mapped * 0.46f, 0.14f, 0.30f),
            BinaryLayout.DisturbedYoungBinary => Math.Clamp(MathF.Max(mapped, outerPlanetOrbit + 0.48f), 0.80f, 8.80f),
            BinaryLayout.WideCompanion => Math.Clamp(MathF.Max(mapped, outerPlanetOrbit + 0.86f), 1.10f, 10.50f),
            _ => 0f,
        };
    }

    private static float SecondaryVisualRadius(StellarSystemProfile stellar)
        => Math.Clamp(0.043f + MathF.Sqrt(MathF.Max(stellar.SecondaryRadiusSolar, 0.05f)) * 0.024f, 0.045f, 0.118f);

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

