using System;
using System.Numerics;
using ComputeSharp;
using GalaxySim.Core.Physics;
using GalaxySim.Core.Shaders;
using GalaxySim.Core.Tree;

namespace GalaxySim.Core.Simulation;

public sealed class SimulationHost : IDisposable
{
    private readonly GraphicsDevice _device;

    public int ParticleCount { get; private set; }
    public int Width { get; }
    public int Height { get; }

    private ReadWriteBuffer<Float4> _positions;
    private ReadWriteBuffer<Float4> _velocities;
    private readonly ReadWriteBuffer<uint> _maxSpeedSqFixed;
    private readonly ReadWriteBuffer<uint> _aabbBuffer;
    private readonly uint[] _aabbCpu = new uint[6];
    private readonly SortBuffers _sortBuffers;
    private RadixSorter _radixSorter;
    private BVHBuffers _bvh;

    private readonly ReadWriteBuffer<uint> _hdrR;
    private readonly ReadWriteBuffer<uint> _hdrG;
    private readonly ReadWriteBuffer<uint> _hdrB;
    private readonly ReadWriteBuffer<uint> _unresolvedR;
    private readonly ReadWriteBuffer<uint> _unresolvedG;
    private readonly ReadWriteBuffer<uint> _unresolvedB;
    private readonly int _unresolvedWidth;
    private readonly int _unresolvedHeight;
    private readonly ReadWriteTexture2D<Bgra32, Float4> _output;
    private readonly ReadBackTexture2D<Bgra32> _readback;
    private readonly ReadWriteBuffer<uint> _pickResult;
    private readonly ReadWriteBuffer<Float4> _pickSelected;
    private readonly uint[] _pickResultCpu = new uint[1];
    private readonly Float4[] _pickSelectedCpu = new Float4[2];
    private readonly uint[] _pickResetCpu = [uint.MaxValue];

    public GalaxyParams Params = GalaxyParams.Default();
    public float TimeScale { get; set; } = 1.0f;
    public float Exposure { get; set; } = 1.0f;
    public float SplatScale { get; set; } = 20f;
    public float Intensity { get; set; } = 0.22f;
    public bool Paused { get; set; } = false;
    public bool AdaptiveSofteningEnabled { get; set; } = true;
    public float AdaptiveSofteningStrength { get; set; } = 0.85f;
    public float LastDtUsed { get; private set; }
    public AABB LastAABB { get; private set; }

    public float TimingAabb { get; private set; }
    public float TimingSort { get; private set; }
    public float TimingTree { get; private set; }
    public float TimingPhys { get; private set; }
    public float TimingTotal => TimingAabb + TimingSort + TimingTree + TimingPhys;


    private const int AabbReadbackInterval = 30;
    private const int SpeedReadbackInterval = 4;
    private int _framesSinceAabbReadback;
    private int _framesSinceSpeedReadback;
    private float _cachedMaxSpeedSq;

    private readonly bool _enableForcedGpuSyncForTiming = false;
    private static readonly uint[] OneUintReadback = new uint[1];
    private static readonly Float4[] OneFloat4Readback = new Float4[1];
    private const float CenterGravityScale = 8f;
    private const float CenterDamping = 0.08f;
    private const float CenterSoftening = 3.0f;
    private const float CenterMergeDistanceFactor = 3.2f;
    private const bool RebuildTreeForSecondKickInMulti = true;
    private readonly bool _enableCenterMerge = true;

    private readonly ReadWriteBuffer<Float4> _hdrScene;
    private readonly ReadWriteBuffer<Float4>[] _bloomMips;
    private readonly int[] _bloomWidths;
    private readonly int[] _bloomHeights;

    public float BloomThreshold { get; set; } = 1.45f;
    public float BloomIntensity { get; set; } = 0.52f;
    public float BloomSoftKnee { get; set; } = 0.5f;
    public float LocalContrastAmount { get; set; } = 0.34f;
    public bool BlackHoleLensingEnabled { get; set; } = true;
    public float BlackHoleLensingStrength { get; set; } = 0.0030f;
    public float BlackHoleLensingRadius { get; set; } = 0.050f;
    public float BlackHoleLensingNearDistance { get; set; } = 0.85f;
    public float BlackHoleLensingFarDistance { get; set; } = 1.75f;

    private readonly ReadWriteBuffer<Float4> _backgroundStars;
    private readonly int _backgroundStarCount;

    public float SkyRadius { get; set; } = 200f;
    public float SkyIntensity { get; set; } = 0.45f;

    private readonly ReadWriteBuffer<Float4> _dustPositions;
    private readonly ReadWriteBuffer<uint> _opacity;
    private readonly int _dustCount;

    public float DustStrength { get; set; } = 1.28f;
    public float DustOpacity { get; set; } = 1.22f;
    public bool DustEnabled { get; set; } = true;
    public bool CoherentDustLanesEnabled { get; set; } = true;
    public float CoherentDustLanesStrength { get; set; } = 1.80f;
    public float BodySpiralness { get; set; } = 0.72f;
    public int BodyColorTheme { get; set; } = 0;
    public float BodyGradientStrength { get; set; } = 0.86f;
    public bool InteractiveCameraMode { get; set; } = false;

    public float DustPatchiness { get; set; } = 0.72f;
    public float DustSpiralStrength { get; set; } = 0.55f;
    public float DustSpiralPitch { get; set; } = 2.2f;
    public float DustBlobSuppressionStrength { get; set; } = 0.78f;

    private float _simTime = 0f;

    private ReadWriteBuffer<Float4> _galaxyCenterPosMass;
    private ReadWriteBuffer<Float4> _galaxyCenterAcceleration;
    private ReadWriteBuffer<int> _particleHomeCenterId;
    private int _galaxyCount;

    public float DiffuseRadius { get; set; } = 14f;
    public float DiffuseIntensity { get; set; } = 0.12f;
    public float DiffuseTempMin { get; set; } = 0.02f;
    public bool DiffuseEnabled { get; set; } = true;
    public bool UnresolvedStarlightEnabled { get; set; } = true;
    public float UnresolvedStarlightIntensity { get; set; } = 1.55f;
    public float BlobSuppressionStrength { get; set; } = 0.72f;

    private ReadWriteBuffer<Float4>? _nebulae;
    private int _nebulaCount;
    public float NebulaRadius { get; set; } = 22f;
    public float NebulaIntensity { get; set; } = 0.12f;
    public bool NebulaeEnabled { get; set; } = true;
    public bool StarFormationFeedbackEnabled { get; set; } = true;

    private float _presetScaleLength = 3.0f;
    private float _presetScaleHeight = 0.22f;
    private float _presetSigmaR = 0.18f;
    private float _presetSigmaZ = 0.035f;
    private float _presetDiskMass = 5f;
    private float _presetBulgeFraction = 0.15f;
    private bool _presetUseTwoComponentDisk = true;
    private float _presetThinDiskFraction = 0.82f;
    private float _presetThickScaleLengthMultiplier = 1.20f;
    private float _presetThickScaleHeightMultiplier = 2.60f;
    private float _presetThickSigmaRMultiplier = 1.25f;
    private float _presetThickSigmaZMultiplier = 2.40f;

    public SimulationHost(GraphicsDevice device, int particleCount, int width, int height)
    {
        _device = device;
        Width = width; Height = height;
        ParticleCount = particleCount;

        _positions = device.AllocateReadWriteBuffer<Float4>(ParticleCount);
        _velocities = device.AllocateReadWriteBuffer<Float4>(ParticleCount);
        _maxSpeedSqFixed = device.AllocateReadWriteBuffer<uint>(1);
        _aabbBuffer = device.AllocateReadWriteBuffer<uint>(6);
        _sortBuffers = new SortBuffers(device, ParticleCount);
        _radixSorter = new RadixSorter(device, ParticleCount);
        _bvh = new BVHBuffers(device, ParticleCount);

        int pixels = width * height;
        _hdrR = device.AllocateReadWriteBuffer<uint>(pixels);
        _hdrG = device.AllocateReadWriteBuffer<uint>(pixels);
        _hdrB = device.AllocateReadWriteBuffer<uint>(pixels);
        _unresolvedWidth = Math.Max((width * 3) / 4, 1);
        _unresolvedHeight = Math.Max((height * 3) / 4, 1);
        int unresolvedPixels = _unresolvedWidth * _unresolvedHeight;
        _unresolvedR = device.AllocateReadWriteBuffer<uint>(unresolvedPixels);
        _unresolvedG = device.AllocateReadWriteBuffer<uint>(unresolvedPixels);
        _unresolvedB = device.AllocateReadWriteBuffer<uint>(unresolvedPixels);
        _output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _readback = device.AllocateReadBackTexture2D<Bgra32>(width, height);
        _pickResult = device.AllocateReadWriteBuffer<uint>(1);
        _pickSelected = device.AllocateReadWriteBuffer<Float4>(2);

        _backgroundStarCount = 5000;
        var starsData = BackgroundStars.Generate(_backgroundStarCount);
        _backgroundStars = device.AllocateReadWriteBuffer(starsData);

        _dustCount = 40_000;
        var dustData = DustInitializer.ExponentialDisk(_dustCount);
        _dustPositions = device.AllocateReadWriteBuffer(dustData);
        _opacity = device.AllocateReadWriteBuffer<uint>(width * height);

        _nebulaCount = 60;
        _nebulae = device.AllocateReadWriteBuffer<Float4>(_nebulaCount);

        _galaxyCount = 1;
        _galaxyCenterPosMass = device.AllocateReadWriteBuffer<Float4>(8);
        _galaxyCenterAcceleration = device.AllocateReadWriteBuffer<Float4>(8);
        _particleHomeCenterId = device.AllocateReadWriteBuffer<int>(ParticleCount);

        _hdrScene = device.AllocateReadWriteBuffer<Float4>(width * height);

        int mipLevels = 6;
        _bloomMips = new ReadWriteBuffer<Float4>[mipLevels];
        _bloomWidths = new int[mipLevels];
        _bloomHeights = new int[mipLevels];
        int mw = width, mh = height;
        for (int i = 0; i < mipLevels; i++)
        {
            _bloomWidths[i] = Math.Max(mw, 1);
            _bloomHeights[i] = Math.Max(mh, 1);
            _bloomMips[i] = device.AllocateReadWriteBuffer<Float4>(
                _bloomWidths[i] * _bloomHeights[i]);
            mw = Math.Max(mw / 2, 1);
            mh = Math.Max(mh / 2, 1);
        }

        _particleHomeCenterCpu = new int[ParticleCount];
    }

    public void Reset(int? newCount = null, int seed = 42)
    {
        int n = newCount ?? ParticleCount;
        if (n != ParticleCount)
        {
            ResizeParticleBuffers(n);
        }

        LoadScenario(_currentScenario, seed);
    }

    public void Step(float frameDt)
    {
        if (Paused) { LastDtUsed = 0; return; }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long t0 = sw.ElapsedTicks;

        ComputeGlobalAABB();
        if (_enableForcedGpuSyncForTiming)
            _aabbBuffer.CopyTo(_aabbCpu);
        long tAabb = sw.ElapsedTicks;

        ComputeMortonCodes();
        SortParticlesByMorton();
        if (_enableForcedGpuSyncForTiming)
            _sortBuffers.MortonCodes.CopyTo(OneUintReadback);
        long tSort = sw.ElapsedTicks;

        BuildLBVH();
        ReduceBVH();
        PackTraversal();
        if (_enableForcedGpuSyncForTiming)
            _bvh.COM.CopyTo(OneFloat4Readback);
        long tTree = sw.ElapsedTicks;

        float requestedScale = Math.Clamp(TimeScale, 0f, 8f);
        float maxAcceleratedDt = Params.MaxDt * MathF.Min(1f + MathF.Max(requestedScale - 1f, 0f) * 0.65f, 3f);
        float dt = MathF.Min(frameDt * requestedScale, maxAcceleratedDt);
        dt = AdaptiveDt(dt, requestedScale);
        LastDtUsed = dt;
        _simTime += dt;

        float halfDt = dt * 0.5f;
        float softSq = Params.Softening * Params.Softening;
        float haloV0Factor = 1.0f;
        float haloV0 = Params.HaloV0 * haloV0Factor;
        float haloV0Sq = haloV0 * haloV0;
        float haloScaleRadius = Params.HaloCoreRadius;
        float bhSoftFactor = _galaxyCount > 1 ? 1.45f : 1.0f;
        float bhMass = _galaxyCount > 1 ? Params.BlackHoleMass * 0.65f : Params.BlackHoleMass;
        float bhSoft = MathF.Max(
            Params.BlackHoleSoftening * bhSoftFactor,
            0.055f * MathF.Sqrt(MathF.Max(bhMass, 0f)));
        float bhSoftSq = bhSoft * bhSoft;
        float thetaSq = Params.Theta * Params.Theta;
        float adaptiveSofteningStrength = AdaptiveSofteningEnabled
            ? Math.Clamp(AdaptiveSofteningStrength, 0f, 1f)
            : 0f;

        _device.For(1, new ResetMaxShader(_maxSpeedSqFixed));
        _device.For(ParticleCount, new KickBVHShader(
    _positions, _velocities, _maxSpeedSqFixed,
    _bvh.TravLeft, _bvh.TravNext,
    _bvh.TravComMass, _bvh.TravSizeMax,
    _galaxyCenterPosMass,
    _particleHomeCenterId,
    _galaxyCount,
    ParticleCount,
    halfDt, Params.Gravity, softSq, haloV0Sq, haloScaleRadius,
    bhMass, bhSoftSq, thetaSq,
    _presetDiskMass, _presetScaleLength, adaptiveSofteningStrength));

        _device.For(ParticleCount, new DriftShader(
            _positions, _velocities, ParticleCount, dt));

        UpdateGalaxyCenters(dt);

        if (RebuildTreeForSecondKickInMulti && _galaxyCount > 1)
        {
            ComputeGlobalAABB();
            ComputeMortonCodes();
            SortParticlesByMorton();
            BuildLBVH();
            ReduceBVH();
            PackTraversal();
        }

        _device.For(1, new ResetMaxShader(_maxSpeedSqFixed));
        _device.For(ParticleCount, new KickBVHShader(
    _positions, _velocities, _maxSpeedSqFixed,
    _bvh.TravLeft, _bvh.TravNext,
    _bvh.TravComMass, _bvh.TravSizeMax,
    _galaxyCenterPosMass,
    _particleHomeCenterId,
    _galaxyCount,
    ParticleCount,
    halfDt, Params.Gravity, softSq, haloV0Sq, haloScaleRadius,
    bhMass, bhSoftSq, thetaSq,
    _presetDiskMass, _presetScaleLength, adaptiveSofteningStrength));

        if (_enableForcedGpuSyncForTiming)
            _velocities.CopyTo(OneFloat4Readback);
        long tPhys = sw.ElapsedTicks;

        double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
        TimingAabb = (float)((tAabb - t0) / ticksPerMs);
        TimingSort = (float)((tSort - tAabb) / ticksPerMs);
        TimingTree = (float)((tTree - tSort) / ticksPerMs);
        TimingPhys = (float)((tPhys - tTree) / ticksPerMs);
    }
    private void PackTraversal()
    {
        _device.For(_bvh.TotalNodes, new PackTraversalShader(
            _bvh.COM, _bvh.AabbMin, _bvh.AabbMax,
            _bvh.TravComMass, _bvh.TravSizeMax,
            ParticleCount));

        _device.For(_bvh.TotalNodes, new BuildTraversalLinksShader(
            _bvh.LeftChild, _bvh.RightChild, _bvh.Parent,
            _bvh.TravLeft, _bvh.TravNext,
            ParticleCount));
    }
    private void ComputeGlobalAABB()
    {
        _device.For(6, new ResetAABBShader(_aabbBuffer));
        _device.For(ParticleCount, new ComputeAABBShader(
            _positions, _aabbBuffer, ParticleCount));

        _framesSinceAabbReadback++;
        if (_framesSinceAabbReadback >= AabbReadbackInterval)
        {
            _aabbBuffer.CopyTo(_aabbCpu);
            LastAABB = AABBDecoder.Decode(_aabbCpu);
            _framesSinceAabbReadback = 0;
        }
    }

    private void ComputeMortonCodes()
    {
        _device.For(ParticleCount, new MortonShader(
            _positions, _aabbBuffer,
            _sortBuffers.MortonCodes, _sortBuffers.ParticleIds,
            ParticleCount));
    }

    private void SortParticlesByMorton()
    {
        _radixSorter.Sort(
            _sortBuffers.MortonCodes, _sortBuffers.ParticleIds,
            _sortBuffers.MortonCodesAlt, _sortBuffers.ParticleIdsAlt);
    }

    private void BuildLBVH()
    {
        _device.For(_bvh.TotalNodes, new ClearBufferShaderInt(_bvh.Parent, _bvh.TotalNodes, -1));

        _device.For(_bvh.InternalCount, new BuildLBVHShader(
            _sortBuffers.MortonCodes,
            _bvh.LeftChild, _bvh.RightChild, _bvh.Parent,
            _bvh.RangeFirst, _bvh.RangeLast,
            ParticleCount));
    }

    private void ReduceBVH()
    {
        _device.For(_bvh.InternalCount, new ClearBufferShader(
            _bvh.ReadyCounter, _bvh.InternalCount));

        _device.For(ParticleCount, new InitLeavesShader(
            _positions, _sortBuffers.ParticleIds,
            _bvh.COM, _bvh.AabbMin, _bvh.AabbMax,
            ParticleCount));

        _device.For(ParticleCount, new ReduceBVHShader(
            _bvh.Parent, _bvh.LeftChild, _bvh.RightChild,
            _bvh.ReadyCounter,
            _bvh.COM, _bvh.AabbMin, _bvh.AabbMax,
            ParticleCount));
    }

    public (uint[] codes, uint[] ids) DebugReadMortonData()
    {
        var codes = new uint[ParticleCount];
        var ids = new uint[ParticleCount];
        _sortBuffers.MortonCodes.CopyTo(codes);
        _sortBuffers.ParticleIds.CopyTo(ids);
        return (codes, ids);
    }

    public uint[] DebugHistogram(int digitShift)
        => _radixSorter.DebugHistogramOnly(_sortBuffers.MortonCodes, digitShift);

    public (int[] lc, int[] rc, int[] par, int[] rf, int[] rl) DebugReadBVH()
    {
        var lc = new int[_bvh.InternalCount];
        var rc = new int[_bvh.InternalCount];
        var par = new int[_bvh.TotalNodes];
        var rf = new int[_bvh.InternalCount];
        var rl = new int[_bvh.InternalCount];
        _bvh.LeftChild.CopyTo(lc);
        _bvh.RightChild.CopyTo(rc);
        _bvh.Parent.CopyTo(par);
        _bvh.RangeFirst.CopyTo(rf);
        _bvh.RangeLast.CopyTo(rl);
        return (lc, rc, par, rf, rl);
    }

    public Float4[] DebugReadCOM()
    {
        var com = new Float4[_bvh.TotalNodes];
        _bvh.COM.CopyTo(com);
        return com;
    }

    private float AdaptiveDt(float dt, float timeScale)
    {
        _framesSinceSpeedReadback++;
        if (_framesSinceSpeedReadback >= SpeedReadbackInterval)
        {
            var buf = new uint[1];
            _maxSpeedSqFixed.CopyTo(buf);
            _cachedMaxSpeedSq = buf[0] / 1e6f;
            _framesSinceSpeedReadback = 0;
        }

        if (_cachedMaxSpeedSq <= 1e-6f) return dt;

        float maxSpeed = MathF.Sqrt(_cachedMaxSpeedSq);
        float cflDt = Params.CourantFactor * Params.Softening / maxSpeed;
        cflDt *= MathF.Max(1f, MathF.Min(timeScale, 3f));
        return MathF.Min(dt, cflDt);
    }

    public ReadBackTexture2D<Bgra32> Render(Matrix4x4 viewProj, Vector3 cameraWorldPos)
    {
        _device.For(Width, Height, new ClearRgbBuffer2DShader(_hdrR, _hdrG, _hdrB, Width, Height));
        _device.For(Width, Height, new ClearUIntBuffer2DShader(_opacity, Width, Height));
        _device.For(_unresolvedWidth, _unresolvedHeight,
            new ClearRgbBuffer2DShader(
                _unresolvedR, _unresolvedG, _unresolvedB,
                _unresolvedWidth, _unresolvedHeight));

        var vp = Matrix4x4.Transpose(viewProj);
        var hlslVp = new Float4x4(
            vp.M11, vp.M12, vp.M13, vp.M14,
            vp.M21, vp.M22, vp.M23, vp.M24,
            vp.M31, vp.M32, vp.M33, vp.M34,
            vp.M41, vp.M42, vp.M43, vp.M44);

        if (DiffuseEnabled)
        {
            _device.For(ParticleCount, new DiffuseShader(
                _positions, _velocities, _hdrR, _hdrG, _hdrB,
                ParticleCount, Width, Height, hlslVp,
                DiffuseRadius, DiffuseIntensity, DiffuseTempMin));
        }

        bool interactiveMode = InteractiveCameraMode;

        if (NebulaeEnabled && !interactiveMode && _nebulae != null)
        {
            _device.For(_nebulaCount, new NebulaeShader(
                _nebulae, _hdrR, _hdrG, _hdrB,
                _nebulaCount, Width, Height, hlslVp,
                NebulaRadius, NebulaIntensity));
        }

        _device.For(_backgroundStarCount, new BackgroundStarsShader(
            _backgroundStars, _hdrR, _hdrG, _hdrB,
            _backgroundStarCount, Width, Height, hlslVp,
            SkyRadius, SkyIntensity));

        _device.For(ParticleCount, new RasterizeShader(
            _positions, _velocities, _hdrR, _hdrG, _hdrB,
            ParticleCount, Width, Height, hlslVp, SplatScale, Intensity));

        if (UnresolvedStarlightEnabled)
        {
            float unresolvedIntensity = Math.Clamp(UnresolvedStarlightIntensity, 0f, 3f);
            if (interactiveMode)
                unresolvedIntensity *= 0.72f;
            _device.For(ParticleCount, new UnresolvedStarlightShader(
                _positions, _velocities,
                _unresolvedR, _unresolvedG, _unresolvedB,
                ParticleCount,
                _unresolvedWidth, _unresolvedHeight,
                hlslVp,
                unresolvedIntensity));
        }

        if (DustEnabled)
        {
            float haloRcSq = Params.HaloCoreRadius * Params.HaloCoreRadius;
            _device.For(_dustCount, new DustShader(
                _dustPositions, _opacity,
                _dustCount, Width, Height, hlslVp,
                SplatScale * 1.2f, DustOpacity,
                Params.HaloV0, haloRcSq, LastDtUsed,
                _simTime,
                DustPatchiness, DustSpiralStrength, DustSpiralPitch,
                Math.Clamp(DustBlobSuppressionStrength, 0f, 1.5f)));
        }

        if (CoherentDustLanesEnabled && !interactiveMode)
        {
            var (laneCount, lane0, lane1) = BuildCoherentDustCenterData(viewProj);
            if (laneCount > 0)
            {
                float laneStrength = Math.Clamp(CoherentDustLanesStrength, 0f, 4f);
                float spiralness = Math.Clamp(BodySpiralness, 0f, 1f);
                float lanePitch = Math.Clamp(DustSpiralPitch * 1.1f, 1.1f, 6.0f);
                float laneSharpness = 2.1f + spiralness * 3.6f;
                float faceOn = EstimateFaceOnFactor(cameraWorldPos);
                _device.For(Width, Height, new CoherentDustLanesShader(
                    _opacity,
                    Width, Height,
                    laneCount,
                    lane0, lane1,
                    laneStrength,
                    lanePitch,
                    laneSharpness,
                    spiralness,
                    0.50f,
                    _simTime,
                    faceOn));
            }
        }

        _device.For(Width, Height, new ResolveHDRShader(
            _hdrR, _hdrG, _hdrB,
            _unresolvedR, _unresolvedG, _unresolvedB,
            _unresolvedWidth, _unresolvedHeight,
            _opacity, _hdrScene,
            Width, Height, DustStrength,
            Math.Clamp(BodyGradientStrength, 0f, 1f),
            BodyColorTheme,
            Math.Clamp(BlobSuppressionStrength, 0f, 1.25f),
            Math.Clamp(DustBlobSuppressionStrength, 0f, 1.5f)));

        _device.For(Width, Height, new BloomThresholdShader(
            _hdrScene, _bloomMips[0], Width, Height,
            BloomThreshold, BloomSoftKnee));

        for (int i = 0; i < _bloomMips.Length - 1; i++)
        {
            _device.For(_bloomWidths[i + 1], _bloomHeights[i + 1],
                new BloomDownsampleShader(
                    _bloomMips[i], _bloomMips[i + 1],
                    _bloomWidths[i], _bloomHeights[i],
                    _bloomWidths[i + 1], _bloomHeights[i + 1]));
        }

        for (int i = _bloomMips.Length - 1; i > 0; i--)
        {
            _device.For(_bloomWidths[i - 1], _bloomHeights[i - 1],
                new BloomUpsampleShader(
                    _bloomMips[i], _bloomMips[i - 1],
                    _bloomWidths[i], _bloomHeights[i],
                    _bloomWidths[i - 1], _bloomHeights[i - 1],
                    1.0f));
        }

        var (bhLensCount, bhLens0, bhLens1) = BuildBlackHoleLensingData(viewProj, cameraWorldPos);
        float bhLensStrength = BlackHoleLensingEnabled
            ? Math.Clamp(BlackHoleLensingStrength, 0f, 0.04f)
            : 0f;
        float bhLensRadius = Math.Clamp(BlackHoleLensingRadius, 0.015f, 0.12f);

        _device.For(Width, Height, new TonemapShader(
            _hdrScene, _bloomMips[0], _output,
            Width, Height, Exposure, BloomIntensity,
            bhLensCount, bhLens0, bhLens1,
            bhLensStrength, bhLensRadius, 0.0022f,
            Math.Clamp(LocalContrastAmount, 0f, 1.5f)));

        _output.CopyTo(_readback);
        return _readback;
    }

    private (int count, Float4 center0, Float4 center1) BuildCoherentDustCenterData(Matrix4x4 viewProj)
    {
        if (_galaxyCount <= 0)
            return (0, default, default);

        int count = 0;
        Float4 c0 = default;
        Float4 c1 = default;
        int limit = Math.Min(_galaxyCount, 2);

        for (int i = 0; i < limit; i++)
        {
            Float3 center = _centerPosMassCpu[i].XYZ;
            var clip = Vector4.Transform(new Vector4(center.X, center.Y, center.Z, 1f), viewProj);
            if (clip.W <= 0.02f)
                continue;

            float invW = 1f / clip.W;
            float ndcX = clip.X * invW;
            float ndcY = clip.Y * invW;
            float ndcZ = clip.Z * invW;
            if (MathF.Abs(ndcX) > 1.5f || MathF.Abs(ndcY) > 1.5f || ndcZ < 0f || ndcZ > 1f)
                continue;

            float uvX = ndcX * 0.5f + 0.5f;
            float uvY = 1f - (ndcY * 0.5f + 0.5f);
            float massScale = MathF.Max(_centerPosMassCpu[i].W, 0.05f);
            Float4 packed = new Float4(uvX, uvY, massScale, clip.W);

            if (count == 0) c0 = packed;
            else if (count == 1) c1 = packed;
            count++;
        }

        return (count, c0, c1);
    }

    private (int count, Float4 lens0, Float4 lens1) BuildBlackHoleLensingData(
        Matrix4x4 viewProj,
        Vector3 cameraWorldPos)
    {
        if (!BlackHoleLensingEnabled || _galaxyCount <= 0)
            return (0, default, default);

        Float4 lens0 = default;
        Float4 lens1 = default;
        int count = 0;
        int limit = Math.Min(_galaxyCount, 2);

        for (int i = 0; i < limit; i++)
        {
            Float3 center = _centerPosMassCpu[i].XYZ;
            var clip = Vector4.Transform(new Vector4(center.X, center.Y, center.Z, 1f), viewProj);
            if (clip.W <= 0.02f)
                continue;

            float invW = 1f / clip.W;
            float ndcX = clip.X * invW;
            float ndcY = clip.Y * invW;
            float ndcZ = clip.Z * invW;
            if (MathF.Abs(ndcX) > 1.35f || MathF.Abs(ndcY) > 1.35f || ndcZ < 0f || ndcZ > 1f)
                continue;

            float uvX = ndcX * 0.5f + 0.5f;
            float uvY = 1f - (ndcY * 0.5f + 0.5f);
            float massScale = MathF.Max(_centerPosMassCpu[i].W, 0.05f);
            float distToCamera = Vector3.Distance(
                cameraWorldPos,
                new Vector3(center.X, center.Y, center.Z));
            float nearDist = MathF.Max(BlackHoleLensingNearDistance, 0.2f);
            float farDist = MathF.Max(BlackHoleLensingFarDistance, nearDist + 0.15f);
            if (distToCamera >= farDist)
                continue;
            if (clip.W > 1.6f)
                continue;
            float t = (distToCamera - nearDist) / (farDist - nearDist);
            float distProximity = 1f - Math.Clamp(t, 0f, 1f);
            distProximity *= distProximity;
            distProximity *= distProximity;

            float apparent = 1f / (clip.W + 1e-3f);
            float screenProximity = Math.Clamp((apparent - 0.52f) / 0.66f, 0f, 1f);
            screenProximity *= screenProximity;
            screenProximity *= screenProximity;

            float proximity = distProximity * screenProximity;
            if (proximity <= 0.08f)
                continue;

            Float4 lens = new Float4(uvX, uvY, massScale, proximity);

            if (count == 0) lens0 = lens;
            else if (count == 1) lens1 = lens;
            count++;
        }

        return (count, lens0, lens1);
    }

    private float EstimateFaceOnFactor(Vector3 cameraWorldPos)
    {
        if (_galaxyCount <= 0)
            return 1f;

        Float3 center = _centerPosMassCpu[0].XYZ;
        Vector3 toCamera = cameraWorldPos - new Vector3(center.X, center.Y, center.Z);
        float lenSq = toCamera.LengthSquared();
        if (lenSq <= 1e-6f)
            return 1f;

        Vector3 dir = toCamera / MathF.Sqrt(lenSq);
        float faceOn = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY));
        return Math.Clamp(faceOn, 0f, 1f);
    }

    public void ApplyPreset(GalaxyPresetType presetType, bool regenerateCurrentScenario = true)
    {
        var preset = GalaxyPreset.Get(presetType);

        Params = new GalaxyParams
        {
            Gravity = preset.Gravity,
            HaloV0 = preset.HaloV0,
            HaloCoreRadius = preset.HaloCoreRadius,
            BlackHoleMass = preset.BlackHoleMass,
            Softening = Params.Softening,
            BlackHoleSoftening = Params.BlackHoleSoftening,
            MaxDt = Params.MaxDt,
            CourantFactor = Params.CourantFactor,
            Theta = Params.Theta,
        };

        _presetScaleLength = preset.ScaleLength;
        _presetScaleHeight = preset.ScaleHeight;
        _presetSigmaR = preset.SigmaR;
        _presetSigmaZ = preset.SigmaZ;
        _presetDiskMass = preset.DiskMass;
        _presetBulgeFraction = preset.BulgeFraction;
        _presetUseTwoComponentDisk = preset.UseTwoComponentDisk;
        _presetThinDiskFraction = preset.ThinDiskFraction;
        _presetThickScaleLengthMultiplier = preset.ThickScaleLengthMultiplier;
        _presetThickScaleHeightMultiplier = preset.ThickScaleHeightMultiplier;
        _presetThickSigmaRMultiplier = preset.ThickSigmaRMultiplier;
        _presetThickSigmaZMultiplier = preset.ThickSigmaZMultiplier;

        if (regenerateCurrentScenario)
            LoadScenario(_currentScenario);
    }

    private void ResizeParticleBuffers(int particleCount)
    {
        _positions.Dispose();
        _velocities.Dispose();
        _radixSorter.Dispose();
        _bvh.Dispose();
        _particleHomeCenterId.Dispose();

        ParticleCount = particleCount;
        _positions = _device.AllocateReadWriteBuffer<Float4>(particleCount);
        _velocities = _device.AllocateReadWriteBuffer<Float4>(particleCount);
        _sortBuffers.Resize(particleCount);
        _radixSorter = new RadixSorter(_device, particleCount);
        _bvh = new BVHBuffers(_device, particleCount);
        _particleHomeCenterId = _device.AllocateReadWriteBuffer<int>(particleCount);
        _particleHomeCenterCpu = new int[particleCount];
    }

    public void LoadScenario(Scenario scenario, int seedSalt = 0)
    {
        var placements = Scenarios.Build(scenario);
        if (seedSalt != 0)
        {
            for (int i = 0; i < placements.Length; i++)
                placements[i].Seed = unchecked(placements[i].Seed + seedSalt + i * 9973);
        }

        var (pos, vel) = GalaxyInitializer.MultiGalaxy(
            ParticleCount, placements, Params,
            scaleLength: _presetScaleLength,
            scaleHeight: _presetScaleHeight,
            sigmaR: _presetSigmaR,
            sigmaZ: _presetSigmaZ,
            diskTotalMass: _presetDiskMass,
            bulgeFraction: _presetBulgeFraction,
            useTwoComponentDisk: _presetUseTwoComponentDisk,
            thinDiskFraction: _presetThinDiskFraction,
            thickScaleLengthMultiplier: _presetThickScaleLengthMultiplier,
            thickScaleHeightMultiplier: _presetThickScaleHeightMultiplier,
            thickSigmaRMultiplier: _presetThickSigmaRMultiplier,
            thickSigmaZMultiplier: _presetThickSigmaZMultiplier);

        _positions.CopyFrom(pos);
        _velocities.CopyFrom(vel);

        _galaxyCount = Math.Min(placements.Length, _centerPosMassCpu.Length);
        Array.Clear(_centerPosMassCpu);
        Array.Clear(_centerVelCpu);
        Array.Clear(_centerAccCpu);
        Array.Clear(_centerAccPackedCpu);
        Array.Clear(_particleHomeCenterCpu);

        for (int i = 0; i < _galaxyCount; i++)
        {
            var p = placements[i];
            _centerPosMassCpu[i] = new Float4(p.Offset, p.MassScale);
            _centerVelCpu[i] = new Float4(p.BulkVelocity, Params.HaloCoreRadius);
        }

        int perGalaxy = _galaxyCount > 0 ? ParticleCount / _galaxyCount : ParticleCount;
        int offset = 0;
        for (int g = 0; g < _galaxyCount; g++)
        {
            int count = (g == _galaxyCount - 1) ? ParticleCount - offset : perGalaxy;
            for (int i = 0; i < count; i++)
                _particleHomeCenterCpu[offset + i] = g;
            offset += count;
        }

        _galaxyCenterPosMass.CopyFrom(_centerPosMassCpu);
        _galaxyCenterAcceleration.CopyFrom(_centerAccPackedCpu);
        _particleHomeCenterId.CopyFrom(_particleHomeCenterCpu);

        RegenerateNebulae(pos, vel, placements);

        _framesSinceAabbReadback = 0;
        _framesSinceSpeedReadback = 0;
        _cachedMaxSpeedSq = 0f;
        _simTime = 0f;
        _currentScenario = scenario;
    }

    private void RegenerateNebulae(
        Float4[] positions,
        Float4[] velocities,
        GalaxyInitializer.GalaxyPlacement[] placements)
    {
        if (_nebulae == null || _nebulaCount <= 0)
            return;

        Float4[] nebData = StarFormationFeedbackEnabled
            ? NebulaInitializer.GenerateFromStarFormationFeedback(
                _nebulaCount,
                positions,
                velocities,
                placements,
                scaleLength: _presetScaleLength,
                innerCutoff: MathF.Max(_presetScaleLength * 0.32f, 1.1f),
                tempThreshold: 0.58f,
                gridResolution: 56,
                seed: 4242)
            : NebulaInitializer.GenerateInDisks(
                _nebulaCount,
                placements,
                scaleLength: _presetScaleLength,
                innerCutoff: MathF.Max(_presetScaleLength * 0.32f, 1.1f),
                seed: 9999);

        _nebulae.CopyFrom(nebData);
    }

    public Scenario CurrentScenario => _currentScenario;
    private Scenario _currentScenario = Scenario.Single;

    private readonly Float4[] _centerPosMassCpu = new Float4[8];
    private readonly Float4[] _centerVelCpu = new Float4[8];
    private readonly Float3[] _centerAccCpu = new Float3[8];
    private readonly Float4[] _centerAccPackedCpu = new Float4[8];
    private int[] _particleHomeCenterCpu;

    private void UpdateGalaxyCenters(float dt)
    {
        if (_galaxyCount <= 0)
            return;

        Array.Clear(_centerAccCpu, 0, _galaxyCount);

        if (_galaxyCount > 1)
        {
            float pairG = Params.Gravity * CenterGravityScale;
            float softSq = CenterSoftening * CenterSoftening;

            for (int i = 0; i < _galaxyCount; i++)
            {
                Float3 pi = _centerPosMassCpu[i].XYZ;
                float mi = MathF.Max(_centerPosMassCpu[i].W, 0.05f);

                for (int j = i + 1; j < _galaxyCount; j++)
                {
                    Float3 pj = _centerPosMassCpu[j].XYZ;
                    float mj = MathF.Max(_centerPosMassCpu[j].W, 0.05f);

                    Float3 r = pj - pi;
                    float distSq = r.X * r.X + r.Y * r.Y + r.Z * r.Z + softSq;
                    float invDist = 1f / MathF.Sqrt(distSq);
                    float invDist3 = invDist * invDist * invDist;
                    Float3 a = r * (pairG * invDist3);

                    _centerAccCpu[i] += a * mj;
                    _centerAccCpu[j] -= a * mi;
                }
            }
        }

        float damp = 1f / (1f + CenterDamping * dt);
        for (int i = 0; i < _galaxyCount; i++)
        {
            var p = _centerPosMassCpu[i];
            var v = _centerVelCpu[i];
            Float3 vNext = (v.XYZ + _centerAccCpu[i] * dt) * damp;

            _centerVelCpu[i] = new Float4(vNext, v.W);
            _centerPosMassCpu[i] = new Float4(p.XYZ + vNext * dt, p.W);
        }

        if (_enableCenterMerge)
            MergeCloseCentersIfNeeded();

        Array.Clear(_centerAccPackedCpu);
        for (int i = 0; i < _galaxyCount; i++)
            _centerAccPackedCpu[i] = new Float4(_centerAccCpu[i], 0f);

        _galaxyCenterPosMass.CopyFrom(_centerPosMassCpu);
        _galaxyCenterAcceleration.CopyFrom(_centerAccPackedCpu);
    }

    private void MergeCloseCentersIfNeeded()
    {
        if (_galaxyCount < 2)
            return;

        float mergeDistance = MathF.Max(Params.HaloCoreRadius * CenterMergeDistanceFactor, 3.0f);
        float mergeDistanceSq = mergeDistance * mergeDistance;

        bool merged;
        do
        {
            merged = false;

            for (int i = 0; i < _galaxyCount && !merged; i++)
            {
                for (int j = i + 1; j < _galaxyCount && !merged; j++)
                {
                    Float3 r = _centerPosMassCpu[j].XYZ - _centerPosMassCpu[i].XYZ;
                    float distSq = r.X * r.X + r.Y * r.Y + r.Z * r.Z;
                    if (distSq > mergeDistanceSq)
                        continue;

                    Float3 rv = _centerVelCpu[j].XYZ - _centerVelCpu[i].XYZ;
                    float closing = r.X * rv.X + r.Y * rv.Y + r.Z * rv.Z;
                    if (closing > 0f && distSq > mergeDistanceSq * 0.78f)
                        continue;

                    float mi = MathF.Max(_centerPosMassCpu[i].W, 0.05f);
                    float mj = MathF.Max(_centerPosMassCpu[j].W, 0.05f);
                    float m = mi + mj;

                    Float3 pos = (_centerPosMassCpu[i].XYZ * mi + _centerPosMassCpu[j].XYZ * mj) / m;
                    Float3 vel = (_centerVelCpu[i].XYZ * mi + _centerVelCpu[j].XYZ * mj) / m;

                    _centerPosMassCpu[i] = new Float4(pos, m);
                    _centerVelCpu[i] = new Float4(vel, _centerVelCpu[i].W);
                    RehomeParticlesAfterCenterMerge(i, j);
                    RemoveCenterAt(j);
                    merged = true;
                }
            }
        } while (merged);
    }

    private void RehomeParticlesAfterCenterMerge(int survivorIndex, int removedIndex)
    {
        for (int i = 0; i < _particleHomeCenterCpu.Length; i++)
        {
            int home = _particleHomeCenterCpu[i];
            if (home == removedIndex)
                _particleHomeCenterCpu[i] = survivorIndex;
            else if (home > removedIndex)
                _particleHomeCenterCpu[i] = home - 1;
        }

        _particleHomeCenterId.CopyFrom(_particleHomeCenterCpu);
    }

    private void RemoveCenterAt(int index)
    {
        for (int k = index; k < _galaxyCount - 1; k++)
        {
            _centerPosMassCpu[k] = _centerPosMassCpu[k + 1];
            _centerVelCpu[k] = _centerVelCpu[k + 1];
            _centerAccCpu[k] = _centerAccCpu[k + 1];
        }

        int last = _galaxyCount - 1;
        _centerPosMassCpu[last] = new Float4(0f, 0f, 0f, 0f);
        _centerVelCpu[last] = new Float4(0f, 0f, 0f, 0f);
        _centerAccCpu[last] = new Float3(0f, 0f, 0f);
        _galaxyCount--;
    }

    public SelectedStarInfo? PickStar(
        Matrix4x4 viewProj,
        float screenX,
        float screenY,
        int screenWidth,
        int screenHeight,
        float pickRadiusPixels = 12f)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            return null;

        _pickResult.CopyFrom(_pickResetCpu);
        var vp = Matrix4x4.Transpose(viewProj);
        _device.For(ParticleCount, new PickStarShader(
            _positions,
            _pickResult,
            ParticleCount,
            new Float4x4(
                vp.M11, vp.M12, vp.M13, vp.M14,
                vp.M21, vp.M22, vp.M23, vp.M24,
                vp.M31, vp.M32, vp.M33, vp.M34,
                vp.M41, vp.M42, vp.M43, vp.M44),
            screenX,
            screenY,
            screenWidth,
            screenHeight,
            pickRadiusPixels));

        _pickResult.CopyTo(_pickResultCpu);
        uint packed = _pickResultCpu[0];
        if (packed == uint.MaxValue)
            return null;

        int bestIndex = (int)(packed & 0x000FFFFFu);
        if (bestIndex < 0 || bestIndex >= ParticleCount)
            return null;

        _device.For(1, new CopyPickedStarShader(_positions, _velocities, _pickSelected, bestIndex, ParticleCount));
        _pickSelected.CopyTo(_pickSelectedCpu);

        Float4 pos = _pickSelectedCpu[0];
        Float4 vel = _pickSelectedCpu[1];
        float temp = Math.Clamp(vel.W, 0f, 1f);
        return StarCatalog.Create(
            bestIndex,
            new Vector3(pos.X, pos.Y, pos.Z),
            new Vector3(vel.X, vel.Y, vel.Z),
            temp,
            pos.W);
    }

    public void Dispose()
    {
        _positions.Dispose();
        _velocities.Dispose();
        _maxSpeedSqFixed.Dispose();
        _aabbBuffer.Dispose();
        _sortBuffers.Dispose();
        _radixSorter.Dispose();
        _bvh.Dispose();
        _hdrR.Dispose(); _hdrG.Dispose(); _hdrB.Dispose();
        _unresolvedR.Dispose(); _unresolvedG.Dispose(); _unresolvedB.Dispose();
        _output.Dispose(); _readback.Dispose();
        _pickResult.Dispose();
        _pickSelected.Dispose();
        _backgroundStars.Dispose();
        _dustPositions.Dispose();
        _opacity.Dispose();
        _galaxyCenterPosMass.Dispose();
        _galaxyCenterAcceleration.Dispose();
        _particleHomeCenterId.Dispose();
        _nebulae?.Dispose();
        _hdrScene.Dispose();
        foreach (var m in _bloomMips) m.Dispose();
    }
}
