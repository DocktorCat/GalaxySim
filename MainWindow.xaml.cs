using ComputeSharp;
using GalaxySim.Core.Camera;
using GalaxySim.Core.Tree;
using GalaxySim.Core.Simulation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GalaxySim;

public partial class MainWindow : Window
{
    private readonly int _renderWidth;
    private readonly int _renderHeight;
    private readonly int _particleCount;
    private readonly GalaxyPresetType _initialPreset;
    private readonly Scenario _initialScenario;
    private bool _suppressUiHandlers;

    private SimulationHost _sim = null!;
    private OrbitCamera _camera = null!;
    private WriteableBitmap _bitmap = null!;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _fpsTextCache = "—";
    private string _timingsTextCache = "[waiting]";

    private Point _lastMouse;
    private Point _mouseDownPoint;
    private bool _dragging;
    private bool _orbitMoved;
    private float _orbitSmoothDx;
    private float _orbitSmoothDy;
    private TimeSpan _lastMouseTick;

    private readonly object _simLock = new();
    private readonly object _cameraLock = new();
    private readonly object _frameLock = new();
    private readonly object _statsLock = new();
    private byte[] _frameFront = null!;
    private byte[] _frameBack = null!;
    private bool _hasNewFrame;
    private Task? _renderLoopTask;

    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _autoBvhVerified;
    private SelectedStarInfo? _selectedStar;

    public MainWindow() : this(128000, GalaxyPresetType.MilkyWay, Scenario.Single, 1280, 720)
    {
    }

    public MainWindow(int particleCount, GalaxyPresetType initialPreset, Scenario initialScenario)
        : this(particleCount, initialPreset, initialScenario, 1280, 720)
    {
    }

    public MainWindow(
        int particleCount,
        GalaxyPresetType initialPreset,
        Scenario initialScenario,
        int renderWidth,
        int renderHeight)
    {
        _particleCount = Math.Max(8_000, particleCount);
        _renderWidth = Math.Clamp(renderWidth, 640, 7680);
        _renderHeight = Math.Clamp(renderHeight, 360, 4320);
        _initialPreset = initialPreset;
        _initialScenario = initialScenario;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _sim = new SimulationHost(GraphicsDevice.GetDefault(), _particleCount, _renderWidth, _renderHeight);
        _suppressUiHandlers = true;
        lock (_simLock)
        {
            _sim.ApplyPreset(_initialPreset, regenerateCurrentScenario: false);
            _sim.LoadScenario(_initialScenario);
        }
        _camera = new OrbitCamera { AspectRatio = (float)_renderWidth / _renderHeight };
        SyncUiWithSimulationParams();
        SyncSceneSelections();
        _suppressUiHandlers = false;

        _frameFront = new byte[_renderWidth * _renderHeight * 4];
        _frameBack = new byte[_renderWidth * _renderHeight * 4];
        _bitmap = new WriteableBitmap(_renderWidth, _renderHeight, 96, 96, PixelFormats.Bgra32, null);
        RenderImage.Source = _bitmap;

        CompositionTarget.Rendering += OnRendering;
        _renderLoopTask = Task.Run(() => RenderLoop(_lifetimeCts.Token));
        _ = RunBvhSanityCheckDelayedAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _lifetimeCts.Cancel();
        try
        {
            _renderLoopTask?.Wait(2000);
        }
        catch (Exception)
        {
        }
        _lifetimeCts.Dispose();
        if (_sim is not null)
        {
            lock (_simLock)
            {
                _sim.Dispose();
            }
        }
    }

    private void SyncSceneSelections()
    {
        PresetCombo.SelectedIndex = PresetToIndex(_initialPreset);
        PresetDescription.Text = GalaxyPreset.Get(_initialPreset).Description;
    }

    private static int PresetToIndex(GalaxyPresetType preset) => preset switch
    {
        GalaxyPresetType.MilkyWay => 0,
        GalaxyPresetType.Andromeda => 1,
        GalaxyPresetType.NGC1300 => 2,
        GalaxyPresetType.M74Pinwheel => 3,
        GalaxyPresetType.DwarfGalaxy => 4,
        _ => 0,
    };

    private static GalaxyPresetType IndexToPreset(int index) => index switch
    {
        0 => GalaxyPresetType.MilkyWay,
        1 => GalaxyPresetType.Andromeda,
        2 => GalaxyPresetType.NGC1300,
        3 => GalaxyPresetType.M74Pinwheel,
        4 => GalaxyPresetType.DwarfGalaxy,
        _ => GalaxyPresetType.MilkyWay,
    };

    private static int ThemeToIndex(int theme) => theme switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        _ => 0,
    };

    private void MutateSim(Action<SimulationHost> apply)
    {
        if (_sim is null) return;
        lock (_simLock)
        {
            apply(_sim);
        }
    }

    private T ReadSim<T>(Func<SimulationHost, T> read, T fallback)
    {
        if (_sim is null) return fallback;
        lock (_simLock)
        {
            return read(_sim);
        }
    }

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_sim == null || _suppressUiHandlers) return;

        var preset = IndexToPreset(PresetCombo.SelectedIndex);

        var presetInfo = GalaxyPreset.Get(preset);
        PresetDescription.Text = presetInfo.Description;

        float haloV0;
        float haloRc;
        float bhMass;
        float gravity;
        lock (_simLock)
        {
            _sim.ApplyPreset(preset);
            haloV0 = _sim.Params.HaloV0;
            haloRc = _sim.Params.HaloCoreRadius;
            bhMass = _sim.Params.BlackHoleMass;
            gravity = _sim.Params.Gravity;
        }

        _suppressUiHandlers = true;
        HaloV0Slider.Value = haloV0;
        HaloRcSlider.Value = haloRc;
        BhMassSlider.Value = bhMass;
        GravitySlider.Value = gravity;
        _suppressUiHandlers = false;
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (RenderImage.Source is not BitmapSource source)
        {
            ScreenshotStatus.Text = "Нет кадра для сохранения";
            return;
        }

        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "GalaxySim");
            Directory.CreateDirectory(dir);

            string fileName = $"galaxy_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string fullPath = Path.Combine(dir, fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var fs = new FileStream(fullPath, FileMode.Create))
                encoder.Save(fs);

            ScreenshotStatus.Text = $"Сохранено: {fileName}";
            ScreenshotStatus.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#7CFF9D"));
        }
        catch (Exception ex) 
        {
            ScreenshotStatus.Text = $"Ошибка: {ex.Message}";
            ScreenshotStatus.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FF7C7C"));
        }
    }
    private void SyncUiWithSimulationParams()
    {
        float timeScale;
        float gravity;
        float softening;
        float exposure;
        float splat;
        float haloV0;
        float haloRc;
        float bhMass;
        float bloomThreshold;
        float bloomIntensity;
        float localContrast;
        bool diffuseEnabled;
        float diffuseIntensity;
        float diffuseRadius;
        bool unresolvedEnabled;
        float unresolvedIntensity;
        float blobSuppression;
        bool nebulaeEnabled;
        float nebulaIntensity;
        bool coherentDustEnabled;
        float coherentDustStrength;
        float bodySpiralness;
        float bodyGradient;
        int bodyTheme;
        float dustStrength;
        float dustBlobSuppression;
        bool bhLensingEnabled;
        float bhLensingStrength;
        float bhLensingNear;
        float bhLensingFar;

        lock (_simLock)
        {
            timeScale = _sim.TimeScale;
            gravity = _sim.Params.Gravity;
            softening = _sim.Params.Softening;
            exposure = _sim.Exposure;
            splat = _sim.SplatScale;
            haloV0 = _sim.Params.HaloV0;
            haloRc = _sim.Params.HaloCoreRadius;
            bhMass = _sim.Params.BlackHoleMass;
            bloomThreshold = _sim.BloomThreshold;
            bloomIntensity = _sim.BloomIntensity;
            localContrast = _sim.LocalContrastAmount;
            diffuseEnabled = _sim.DiffuseEnabled;
            diffuseIntensity = _sim.DiffuseIntensity;
            diffuseRadius = _sim.DiffuseRadius;
            unresolvedEnabled = _sim.UnresolvedStarlightEnabled;
            unresolvedIntensity = _sim.UnresolvedStarlightIntensity;
            blobSuppression = _sim.BlobSuppressionStrength;
            nebulaeEnabled = _sim.NebulaeEnabled;
            nebulaIntensity = _sim.NebulaIntensity;
            coherentDustEnabled = _sim.CoherentDustLanesEnabled;
            coherentDustStrength = _sim.CoherentDustLanesStrength;
            bodySpiralness = _sim.BodySpiralness;
            bodyGradient = _sim.BodyGradientStrength;
            bodyTheme = _sim.BodyColorTheme;
            dustStrength = _sim.DustStrength;
            dustBlobSuppression = _sim.DustBlobSuppressionStrength;
            bhLensingEnabled = _sim.BlackHoleLensingEnabled;
            bhLensingStrength = _sim.BlackHoleLensingStrength;
            bhLensingNear = _sim.BlackHoleLensingNearDistance;
            bhLensingFar = _sim.BlackHoleLensingFarDistance;
        }

        TimeScaleSlider.Value = timeScale;
        GravitySlider.Value = gravity;
        SofteningSlider.Value = softening;
        ExposureSlider.Value = exposure;
        SplatSlider.Value = splat;
        HaloV0Slider.Value = haloV0;
        HaloRcSlider.Value = haloRc;
        BhMassSlider.Value = bhMass;
        BloomThresholdSlider.Value = bloomThreshold;
        BloomIntensitySlider.Value = bloomIntensity;
        LocalContrastSlider.Value = localContrast;
        DiffuseCheck.IsChecked = diffuseEnabled;
        DiffuseIntensitySlider.Value = diffuseIntensity;
        DiffuseRadiusSlider.Value = diffuseRadius;
        UnresolvedCheck.IsChecked = unresolvedEnabled;
        UnresolvedIntensitySlider.Value = unresolvedIntensity;
        BlobSuppressionSlider.Value = blobSuppression;
        NebulaeCheck.IsChecked = nebulaeEnabled;
        NebulaIntensitySlider.Value = nebulaIntensity;
        CoherentDustCheck.IsChecked = coherentDustEnabled;
        CoherentDustStrengthSlider.Value = coherentDustStrength;
        BodySpiralnessSlider.Value = bodySpiralness;
        BodyGradientSlider.Value = bodyGradient;
        BodyColorThemeCombo.SelectedIndex = ThemeToIndex(bodyTheme);
        DustSlider.Value = dustStrength;
        DustBlobSuppressionSlider.Value = dustBlobSuppression;
        BhLensingCheck.IsChecked = bhLensingEnabled;
        BhLensingStrengthSlider.Value = bhLensingStrength;
        BhLensingNearSlider.Value = bhLensingNear;
        BhLensingFarSlider.Value = bhLensingFar;
    }

    private void HaloV0_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.Params.HaloV0 = (float)e.NewValue);
    }

    private void HaloRc_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.Params.HaloCoreRadius = (float)e.NewValue);
    }
    private void Dust_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    { MutateSim(sim => sim.DustStrength = (float)e.NewValue); }

    private void DustBlobSuppression_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    { MutateSim(sim => sim.DustBlobSuppressionStrength = (float)e.NewValue); }
    private void BhMass_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.Params.BlackHoleMass = (float)e.NewValue);
    }
    private void BloomThreshold_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    { MutateSim(sim => sim.BloomThreshold = (float)e.NewValue); }

    private void BloomIntensity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    { MutateSim(sim => sim.BloomIntensity = (float)e.NewValue); }

    private void LocalContrast_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    { MutateSim(sim => sim.LocalContrastAmount = (float)e.NewValue); }

    private void Diffuse_Toggle(object s, RoutedEventArgs e)
    {
        bool enabled = DiffuseCheck.IsChecked ?? true;
        MutateSim(sim => sim.DiffuseEnabled = enabled);
    }

    private void DiffuseIntensity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.DiffuseIntensity = (float)e.NewValue);
    }

    private void DiffuseRadius_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.DiffuseRadius = (float)e.NewValue);
    }

    private void Unresolved_Toggle(object s, RoutedEventArgs e)
    {
        bool enabled = UnresolvedCheck.IsChecked ?? true;
        MutateSim(sim => sim.UnresolvedStarlightEnabled = enabled);
    }

    private void UnresolvedIntensity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.UnresolvedStarlightIntensity = (float)e.NewValue);
    }

    private void BlobSuppression_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.BlobSuppressionStrength = (float)e.NewValue);
    }

    private void Nebulae_Toggle(object s, RoutedEventArgs e)
    {
        bool enabled = NebulaeCheck.IsChecked ?? true;
        MutateSim(sim => sim.NebulaeEnabled = enabled);
    }

    private void NebulaIntensity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.NebulaIntensity = (float)e.NewValue);
    }

    private void CoherentDust_Toggle(object s, RoutedEventArgs e)
    {
        bool enabled = CoherentDustCheck.IsChecked ?? true;
        MutateSim(sim => sim.CoherentDustLanesEnabled = enabled);
    }

    private void CoherentDustStrength_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.CoherentDustLanesStrength = (float)e.NewValue);
    }

    private void BodySpiralness_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sim == null) return;
        float v = (float)e.NewValue;
        MutateSim(sim =>
        {
            sim.BodySpiralness = v;
            sim.DustSpiralStrength = v;
        });
    }

    private void BodyGradient_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.BodyGradientStrength = (float)e.NewValue);
    }

    private void BodyColorTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_sim == null || _suppressUiHandlers) return;
        int idx = Math.Max(BodyColorThemeCombo.SelectedIndex, 0);
        MutateSim(sim => sim.BodyColorTheme = idx);
    }

    private void BhLensing_Toggle(object s, RoutedEventArgs e)
    {
        bool enabled = BhLensingCheck.IsChecked ?? true;
        MutateSim(sim => sim.BlackHoleLensingEnabled = enabled);
    }

    private void BhLensingStrength_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.BlackHoleLensingStrength = (float)e.NewValue);
    }

    private void BhLensingNear_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sim == null) return;
        float near = (float)e.NewValue;
        float far;
        lock (_simLock)
        {
            _sim.BlackHoleLensingNearDistance = near;
            if (_sim.BlackHoleLensingFarDistance < _sim.BlackHoleLensingNearDistance + 0.15f)
                _sim.BlackHoleLensingFarDistance = _sim.BlackHoleLensingNearDistance + 0.15f;
            far = _sim.BlackHoleLensingFarDistance;
        }
        BhLensingFarSlider.Value = far;
    }

    private void BhLensingFar_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sim == null) return;
        float far = (float)e.NewValue;
        float near;
        lock (_simLock)
        {
            _sim.BlackHoleLensingFarDistance = far;
            if (_sim.BlackHoleLensingFarDistance < _sim.BlackHoleLensingNearDistance + 0.15f)
                _sim.BlackHoleLensingNearDistance = _sim.BlackHoleLensingFarDistance - 0.15f;
            near = _sim.BlackHoleLensingNearDistance;
        }
        BhLensingNearSlider.Value = near;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_bitmap is null)
            return;

        byte[]? frame = null;
        lock (_frameLock)
        {
            if (_hasNewFrame)
            {
                frame = _frameFront;
                _hasNewFrame = false;
            }
        }

        if (frame is not null)
        {
            _bitmap.WritePixels(new Int32Rect(0, 0, _renderWidth, _renderHeight), frame, _renderWidth * 4, 0);
        }

        string fps;
        string timings;
        lock (_statsLock)
        {
            fps = _fpsTextCache;
            timings = _timingsTextCache;
        }
        if (!string.Equals(FpsText.Text, fps, StringComparison.Ordinal))
            FpsText.Text = fps;
        if (!string.Equals(TimingsText.Text, timings, StringComparison.Ordinal))
            TimingsText.Text = timings;
    }

    private void RenderLoop(CancellationToken token)
    {
        try
        {
            RenderLoopCore(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            lock (_statsLock)
            {
                _fpsTextCache = "Render loop stopped";
                _timingsTextCache = $"{ex.GetType().Name}: {ex.Message}";
            }
        }
    }

    private unsafe void RenderLoopCore(CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        TimeSpan last = sw.Elapsed;
        double fpsAccum = 0.0;
        int fpsFrames = 0;

        while (!token.IsCancellationRequested)
        {
            TimeSpan frameStart = sw.Elapsed;
            float frameDt = (float)(frameStart - last).TotalSeconds;
            last = frameStart;
            if (frameDt <= 0f || frameDt > 0.25f)
                frameDt = 1f / 60f;
            float simDt = Math.Min(frameDt, 1f / 30f);

            Matrix4x4 viewProj;
            Vector3 eye;
            lock (_cameraLock)
            {
                viewProj = _camera.BuildViewProj();
                eye = _camera.GetEyePosition();
            }

            AABB bb;
            float timingAabb;
            float timingSort;
            float timingTree;
            float timingPhys;
            float timingTotal;

            lock (_simLock)
            {
                _sim.Step(simDt);
                var readback = _sim.Render(viewProj, eye);
                CopyReadbackToBuffer(readback, _frameBack);

                bb = _sim.LastAABB;
                timingAabb = _sim.TimingAabb;
                timingSort = _sim.TimingSort;
                timingTree = _sim.TimingTree;
                timingPhys = _sim.TimingPhys;
                timingTotal = _sim.TimingTotal;
            }

            lock (_frameLock)
            {
                (_frameFront, _frameBack) = (_frameBack, _frameFront);
                _hasNewFrame = true;
            }

            fpsAccum += frameDt;
            fpsFrames++;
            if (fpsAccum >= 0.5)
            {
                string fpsText =
                    $"FPS: {fpsFrames / fpsAccum:F1}  |  {_particleCount:N0} stars\n" +
                    $"AABB: [{bb.Min.X:F1},{bb.Min.Y:F1},{bb.Min.Z:F1}] -> " +
                    $"[{bb.Max.X:F1},{bb.Max.Y:F1},{bb.Max.Z:F1}]\n" +
                    $"Extent: {bb.MaxExtent:F2}";

                string timingsText =
                    $"AABB:  {timingAabb,6:F2} ms\n" +
                    $"Sort:  {timingSort,6:F2} ms\n" +
                    $"Tree:  {timingTree,6:F2} ms\n" +
                    $"Phys:  {timingPhys,6:F2} ms\n" +
                    $"-------------\n" +
                    $"Total: {timingTotal,6:F2} ms";

                lock (_statsLock)
                {
                    _fpsTextCache = fpsText;
                    _timingsTextCache = timingsText;
                }

                fpsAccum = 0.0;
                fpsFrames = 0;
            }

            float elapsed = (float)(sw.Elapsed - frameStart).TotalSeconds;
            float sleepSec = (1f / 60f) - elapsed;
            if (sleepSec > 0.001f)
                Thread.Sleep((int)(sleepSec * 1000f));
            else
                Thread.Yield();
        }
    }

    private unsafe void CopyReadbackToBuffer(ReadBackTexture2D<Bgra32> readback, byte[] destination)
    {
        Bgra32* src = readback.View.DangerousGetAddressAndByteStride(out int srcStride);
        int rowBytes = _renderWidth * 4;
        fixed (byte* dst = destination)
        {
            for (int y = 0; y < _renderHeight; y++)
            {
                byte* srcRow = (byte*)src + y * srcStride;
                byte* dstRow = dst + y * rowBytes;
                Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
            }
        }
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _dragging = true;
        _lastMouse = e.GetPosition((IInputElement)sender);
        _mouseDownPoint = _lastMouse;
        _orbitMoved = false;
        _orbitSmoothDx = 0f;
        _orbitSmoothDy = 0f;
        _lastMouseTick = _clock.Elapsed;
        if (_sim != null)
        {
            lock (_simLock)
            {
                _sim.InteractiveCameraMode = true;
            }
        }
        ((UIElement)sender).CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _camera is null) return;
        var p = e.GetPosition((IInputElement)sender);
        double totalMoveSq = (p.X - _mouseDownPoint.X) * (p.X - _mouseDownPoint.X)
                           + (p.Y - _mouseDownPoint.Y) * (p.Y - _mouseDownPoint.Y);
        if (!_orbitMoved && totalMoveSq < 16.0)
            return;

        _orbitMoved = true;
        float dx = (float)(p.X - _lastMouse.X);
        float dy = (float)(p.Y - _lastMouse.Y);
        dx = Math.Clamp(dx, -36f, 36f);
        dy = Math.Clamp(dy, -36f, 36f);

        var now = _clock.Elapsed;
        float mouseDt = (float)(now - _lastMouseTick).TotalSeconds;
        _lastMouseTick = now;
        if (mouseDt <= 0f || mouseDt > 0.15f)
            mouseDt = 1f / 120f;

        float alpha = 1f - MathF.Exp(-38f * mouseDt);
        _orbitSmoothDx += (dx - _orbitSmoothDx) * alpha;
        _orbitSmoothDy += (dy - _orbitSmoothDy) * alpha;
        lock (_cameraLock)
        {
            _camera.Orbit(_orbitSmoothDx, _orbitSmoothDy, 0.0047f);
        }
        _lastMouse = p;
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var releasePoint = e.GetPosition((IInputElement)sender);
        bool selectClick = !_orbitMoved;
        _dragging = false;
        _orbitSmoothDx = 0f;
        _orbitSmoothDy = 0f;
        if (_sim != null)
        {
            lock (_simLock)
            {
                _sim.InteractiveCameraMode = false;
            }
        }
        ((UIElement)sender).ReleaseMouseCapture();

        if (selectClick && sender is FrameworkElement viewport)
            SelectStarAt(releasePoint, viewport);
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        lock (_cameraLock)
        {
            _camera.Zoom(e.Delta / 120f);
        }
    }

    private void SelectStarAt(Point viewportPoint, FrameworkElement viewport)
    {
        if (_sim is null || _camera is null || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0)
            return;

        float screenX = (float)(viewportPoint.X / viewport.ActualWidth * _renderWidth);
        float screenY = (float)(viewportPoint.Y / viewport.ActualHeight * _renderHeight);

        SelectedStarInfo? star;
        Matrix4x4 viewProj;
        lock (_cameraLock)
        {
            viewProj = _camera.BuildViewProj();
        }

        lock (_simLock)
        {
            star = _sim.PickStar(viewProj, screenX, screenY, _renderWidth, _renderHeight);
        }

        if (star is null)
            return;

        _selectedStar = star;
        ShowSelectedStar(star);
    }

    private void ShowSelectedStar(SelectedStarInfo star)
    {
        SelectedStarCard.Visibility = Visibility.Visible;
        SelectedStarNameText.Text = star.Name;
        SelectedStarTypeText.Text = $"{star.Type} · class {star.SpectralClass}";
        SelectedStarModelText.Text = $"Модель: {star.ModelKey}";
        BuildSelectedStarModel(star);

        float speed = star.Velocity.Length();
        SelectedStarDetailsText.Text =
            $"Index: {star.Index}\n" +
            $"Temp:  {star.TemperatureClass:F2}\n" +
            $"Mass:  {star.SolarMass:F2} M☉  raw {star.Mass:E2}\n" +
            $"Radius:{star.RadiusSolar:F2} R☉\n" +
            $"Lum:   {star.LuminositySolar:F2} L☉\n" +
            $"Age:   {star.AgeGyr:F2} Gyr\n" +
            $"System:{star.SystemKind}\n" +
            $"Planets: {star.PlanetCount}  HZ {star.HabitableZoneAu:F2} AU\n" +
            $"{star.Description}\n" +
            $"Pos:   {star.Position.X,7:F2} {star.Position.Y,7:F2} {star.Position.Z,7:F2}\n" +
            $"Speed: {speed,7:F3}";
    }

    private void BuildSelectedStarModel(SelectedStarInfo star)
    {
        SelectedStarScene.Children.Clear();
        SelectedStarScene.Children.Add(StarModelFactory.CreateScene(star, StarModelDetail.Compact));

        double distance = StarModelFactory.CameraDistanceForStar(star.ModelKey, StarModelDetail.Compact);
        SelectedStarCamera.Position = new System.Windows.Media.Media3D.Point3D(0, 0, distance);
        SelectedStarCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -distance);
    }

    private void OpenSelectedStarModel_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStar is null)
            return;

        ShowPausedDialog(() => new StarModelWindow(_selectedStar));
    }

    private void OpenSelectedSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStar is null)
            return;

        ShowPausedDialog(() => new SolarSystemWindow(_selectedStar));
    }

    private void ShowPausedDialog(Func<Window> createWindow)
    {
        bool wasPaused = ReadSim(sim => sim.Paused, false);
        MutateSim(sim => sim.Paused = true);
        PauseBtn.Content = "Продолжить";

        try
        {
            var window = createWindow();
            window.Owner = this;
            window.ShowDialog();
        }
        finally
        {
            MutateSim(sim => sim.Paused = wasPaused);
            PauseBtn.Content = wasPaused ? "Продолжить" : "Пауза";
        }
    }

    private void Theta_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    { MutateSim(sim => sim.Params.Theta = (float)e.NewValue); }

    private void TimeScale_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.TimeScale = (float)e.NewValue);
    }

    private void Gravity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.Params.Gravity = (float)e.NewValue);
    }

    private void Softening_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.Params.Softening = (float)e.NewValue);
    }

    private void Exposure_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.Exposure = (float)e.NewValue);
    }

    private void Splat_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        MutateSim(sim => sim.SplatScale = (float)e.NewValue);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        bool paused = ReadSim(sim =>
        {
            sim.Paused = !sim.Paused;
            return sim.Paused;
        }, false);
        PauseBtn.Content = paused ? "Продолжить" : "Пауза";
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        lock (_simLock)
        {
            _sim.Reset(seed: Environment.TickCount);
        }
    }

    private void BackToMenu_Click(object sender, RoutedEventArgs e)
    {
        var startup = new StartupWindow();
        Application.Current.MainWindow = startup;
        startup.Show();
        Close();
    }

    private void VerifyHistogram_Click(object sender, RoutedEventArgs e)
    {
        VerifyText.Text = $"Проверка BVH: выполняется ({DateTime.Now:HH:mm:ss})...";
        RunBvhSanityCheck(force: true);
    }
    private async Task RunBvhSanityCheckDelayedAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), _lifetimeCts.Token);
            RunBvhSanityCheck(force: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RunBvhSanityCheck(bool force)
    {
        if (_sim is null)
        {
            VerifyText.Text = "Проверка BVH: симуляция ещё не инициализирована";
            return;
        }

        if (!force && _autoBvhVerified)
            return;

        if (!force)
            _autoBvhVerified = true;

        try
        {
            int n;
            int[] lc;
            int[] rc;
            int[] par;
            int[] rf;
            int[] rl;
            lock (_simLock)
            {
                (lc, rc, par, rf, rl) = _sim.DebugReadBVH();
                n = _sim.ParticleCount;
            }

            int rootFirst = rf[0];
            int rootLast = rl[0];
            string line1 = $"Root range: [{rootFirst}, {rootLast}] (expected [0, {n - 1}])";

            int badChildren = 0;
            for (int i = 0; i < lc.Length; i++)
            {
                foreach (int c in new[] { lc[i], rc[i] })
                {
                    if (c >= 0)
                    {
                        if (c < 0 || c >= n - 1) badChildren++;
                    }
                    else
                    {
                        int leafIdx = -c - 1;
                        if (leafIdx < 0 || leafIdx >= n) badChildren++;
                    }
                }
            }
            string line2 = $"Invalid child indices: {badChildren} (expected 0)";

            int leavesWithBadParent = 0;
            for (int leaf = 0; leaf < n; leaf++)
            {
                int p = par[(n - 1) + leaf];
                if (p < 0 || p >= n - 1) leavesWithBadParent++;
            }
            string line3 = $"Leaves with invalid parent: {leavesWithBadParent} (expected 0)";

            int internalsWithBadParent = 0;
            for (int i = 1; i < n - 1; i++)
            {
                int p = par[i];
                if (p < 0 || p >= n - 1) internalsWithBadParent++;
            }
            string line4 = $"Internals with invalid parent (skip root): {internalsWithBadParent} (expected 0)";

            int leavesReached = 0;
            var stack = new Stack<int>();
            stack.Push(0);
            int safety = 0;
            while (stack.Count > 0 && safety++ < 10 * n)
            {
                int node = stack.Pop();
                foreach (int c in new[] { lc[node], rc[node] })
                {
                    if (c < 0) leavesReached++;
                    else stack.Push(c);
                }
            }
            string line5 = $"Leaves reachable from root: {leavesReached} (expected {n})";

            Debug.WriteLine(line1);
            Debug.WriteLine(line2);
            Debug.WriteLine(line3);
            Debug.WriteLine(line4);
            Debug.WriteLine(line5);

            VerifyText.Text = $"{line1}\n{line2}\n{line3}\n{line4}\n{line5}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            VerifyText.Text = $"Проверка BVH: ошибка {ex.GetType().Name}: {ex.Message}";
        }
    }
}




