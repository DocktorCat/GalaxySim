using ComputeSharp;
using GalaxySim.Core.Camera;
using GalaxySim.Core.Tree;
using GalaxySim.Core.Simulation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GalaxySim;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions SessionJsonOptions = CreateSessionJsonOptions();
    private const int MaxRecentSaveFiles = 8;
    private const double SystemOrbitTimeRate = 0.060;

    private sealed record RecentSavesState(List<string> Paths);

    private sealed record RecentSaveEntry(string Name, string Updated, string Path);

    private readonly record struct SolarSystemRenderRequest(
        float OrbitTime,
        float EffectTime,
        float Zoom,
        bool Is3D,
        bool RealismMode,
        float Yaw,
        float Pitch,
        int SelectedPlanetIndex,
        int SelectedMoonIndex,
        int FocusIndex,
        bool ShowOrbits,
        bool ShowHabitableZone,
        int QualityLevel);

    private readonly int _renderWidth;
    private readonly int _renderHeight;
    private readonly int _particleCount;
    private readonly GalaxyPresetType _initialPreset;
    private readonly Scenario _initialScenario;
    private bool _suppressUiHandlers;

    private SimulationHost _sim = null!;
    private OrbitCamera _camera = null!;
    private WriteableBitmap _bitmap = null!;

    private static JsonSerializerOptions CreateSessionJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _fpsTextCache = "--";
    private string _starsTextCache = "--";
    private string _frameTimeTextCache = "--";
    private string _extentTextCache = "--";
    private string _aabbTextCache = "--";
    private string _timingsTextCache = "[waiting]";
    private string _timingAabbTextCache = "--";
    private string _timingSortTextCache = "--";
    private string _timingTreeTextCache = "--";
    private string _timingPhysTextCache = "--";
    private string _timingTotalTextCache = "--";

    private Point _lastMouse;
    private Point _mouseDownPoint;
    private bool _dragging;
    private bool _orbitMoved;
    private float _orbitSmoothDx;
    private float _orbitSmoothDy;
    private TimeSpan _lastMouseTick;
    private volatile bool _galaxyInteractiveCameraMode;

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
    private string _activeHudSection = "Visual";
    private bool _telemetryVisible = true;
    private bool _bottomActionsVisible = true;
    private bool _selectedStarCardPinned;
    private bool _trackSelectedStarCamera;
    private TimeSpan _lastSelectedStarTrackUpdate;
    private TimeSpan _lastSelectedStarSnapshotUpdate;
    private readonly List<GalaxyObjectId> _bookmarkedObjectIds = [];
    private readonly List<string> _recentSaveFiles = [];
    private DiscoveryJournalState _discoveryJournal = new();
    private bool _suppressDiscoverySelection;
    private string? _pendingDiscoveryStatus;
    private System.Windows.Threading.DispatcherTimer? _autosaveTimer;
    private bool _starPreviewFullscreen;
    private StarGpuRenderer? _selectedStarRenderer;
    private MemoryStream? _hudCursorStream;
    private Cursor? _hudCursor;
    private bool _starPreviewDragging;
    private Point _lastStarPreviewMouse;
    private float _starPreviewYaw;
    private float _starPreviewPitch = 0.18f;
    private float _starPreviewZoom = 1.08f;
    private volatile bool _solarSystemOverlayOpen;
    private bool _solarSystemWasPaused;
    private SolarSystemRenderer? _solarSystemRenderer;
    private SolarSystemRenderer? _solarSystemPreviewRenderer;
    private SelectedStarInfo? _solarSystemStar;
    private int _systemSelectedPlanetIndex = -1;
    private int _systemSelectedMoonIndex = -1;
    private bool _systemSecondaryStarSelected;
    private int _systemFocusIndex = -1;
    private int _systemRenderQuality = 2;
    private bool _systemSuppressControls = true;
    private bool _systemIsDragging;
    private bool _systemDragMoved;
    private bool _systemPaused;
    private bool _systemMode3D = true;
    private bool _systemSpeedPanelVisible = true;
    private readonly object _systemRenderStateLock = new();
    private readonly object _systemFrameLock = new();
    private Task? _systemRenderLoopTask;
    private CancellationTokenSource? _systemRenderCts;
    private byte[]? _systemFrameFront;
    private byte[]? _systemFrameBack;
    private bool _systemHasNewFrame;
    private bool _systemRenderRequested;
    private SolarSystemRenderRequest _systemRenderRequest;
    private double _systemYawDegrees = 25.0;
    private double _systemPitchDegrees = 58.0;
    private double _systemTargetYawDegrees = 25.0;
    private double _systemTargetPitchDegrees = 58.0;
    private double _systemZoom = 0.70;
    private double _systemTargetZoom = 0.70;
    private double _systemOrbitTime;
    private double _systemEffectTime;
    private double _lastSystemRenderElapsed;
    private double _lastSystemPreviewElapsed;
    private bool _systemPreviewForceRender;
    private Point _lastSystemDragPoint;
    private TimeSpan _lastSystemMouseTick;
    private readonly Dictionary<int, TextBlock> _systemLabelElements = new();
    private int _systemHoverObjectId = SolarSystemRenderer.EmptyObjectId;
    private readonly Dictionary<FrameworkElement, TranslateTransform> _floatingWindowTransforms = new();
    private FrameworkElement? _draggedFloatingWindow;
    private FrameworkElement? _activeFloatingWindow;
    private Point _floatingDragStartPoint;
    private double _floatingDragStartX;
    private double _floatingDragStartY;
    private int _floatingWindowZIndex = 20;
    private bool _galaxySelectionMarkerHasPosition;
    private double _galaxySelectionMarkerLeft;
    private double _galaxySelectionMarkerTop;

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
        Activated += OnActivated;
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
        UpdateGalaxySeedText();
        _suppressUiHandlers = false;

        _frameFront = new byte[_renderWidth * _renderHeight * 4];
        _frameBack = new byte[_renderWidth * _renderHeight * 4];
        _bitmap = new WriteableBitmap(_renderWidth, _renderHeight, 96, 96, PixelFormats.Bgra32, null);
        RenderImage.Source = _bitmap;
        ClearSelectedStar();
        ShowContextSection("Visual", forceOpen: true);
        UpdateDiscoveryJournalList();
        LoadRecentSavesMetadata();
        RefreshRecentSavesList();
        UpdateSessionSavePathText();
        _autosaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        ApplyHudCursor();
        Dispatcher.BeginInvoke(ApplyHudCursor, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        InitializeFloatingWindows();
        AnimateGalaxyRail();

        CompositionTarget.Rendering += OnRendering;
        _renderLoopTask = Task.Run(() => RenderLoop(_lifetimeCts.Token));
        _ = RunBvhSanityCheckDelayedAsync();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (_hudCursor is not null)
            Mouse.OverrideCursor = _hudCursor;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        Activated -= OnActivated;
        Mouse.OverrideCursor = null;
        _autosaveTimer?.Stop();
        _lifetimeCts.Cancel();
        StopSolarSystemRenderLoop();
        try
        {
            _renderLoopTask?.Wait(2000);
        }
        catch (Exception)
        {
        }
        _lifetimeCts.Dispose();
        _selectedStarRenderer?.Dispose();
        _selectedStarRenderer = null;
        _solarSystemRenderer?.Dispose();
        _solarSystemRenderer = null;
        _solarSystemPreviewRenderer?.Dispose();
        _solarSystemPreviewRenderer = null;
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

    private void ApplyHudCursor()
    {
        _hudCursorStream = CreateCursorStream();
        _hudCursor = new Cursor(_hudCursorStream);
        Mouse.OverrideCursor = _hudCursor;
    }

    private static MemoryStream CreateCursorStream()
    {
        const int width = 32;
        const int height = 32;
        byte[] pixels = new byte[width * height * 4];
        byte[] mask = new byte[((width + 31) / 32) * 4 * height];

        static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.0001)
                return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));

            double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
            double x = ax + t * dx;
            double y = ay + t * dy;
            return Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
        }

        static bool IsInsidePolygon(double x, double y, (double X, double Y)[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                bool intersects = polygon[i].Y > y != polygon[j].Y > y &&
                    x < (polygon[j].X - polygon[i].X) * (y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X;
                if (intersects)
                    inside = !inside;
            }

            return inside;
        }

        void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
        {
            if ((uint)x >= width || (uint)y >= height)
                return;

            int index = ((height - 1 - y) * width + x) * 4;
            if (a < pixels[index + 3])
                return;

            pixels[index] = b;
            pixels[index + 1] = g;
            pixels[index + 2] = r;
            pixels[index + 3] = a;
        }

        var arrow = new (double X, double Y)[]
        {
            (3, 2),
            (24, 11),
            (15, 14),
            (11, 25),
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double cx = x + 0.5;
                double cy = y + 0.5;
                double edgeDistance = double.MaxValue;
                for (int i = 0; i < arrow.Length; i++)
                {
                    var a = arrow[i];
                    var b = arrow[(i + 1) % arrow.Length];
                    edgeDistance = Math.Min(edgeDistance, DistanceToSegment(cx, cy, a.X, a.Y, b.X, b.Y));
                }

                bool inside = IsInsidePolygon(cx, cy, arrow);
                if (edgeDistance < 2.8)
                    SetPixel(x, y, 42, 168, 255, (byte)Math.Max(0, 95 - edgeDistance * 24));
                if (inside)
                    SetPixel(x, y, 8, 91, 176, 148);
                if (edgeDistance < 1.15)
                    SetPixel(x, y, 146, 224, 255, 255);
            }
        }

        int imageSize = 40 + pixels.Length + mask.Length;
        var stream = new MemoryStream(22 + imageSize);
        var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)2);
        writer.Write((ushort)1);
        writer.Write((byte)width);
        writer.Write((byte)height);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)3);
        writer.Write((ushort)2);
        writer.Write((uint)imageSize);
        writer.Write((uint)22);

        writer.Write((uint)40);
        writer.Write(width);
        writer.Write(height * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)0);
        writer.Write((uint)pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write(pixels);
        writer.Write(mask);

        stream.Position = 0;
        return stream;
    }

    private void InitializeFloatingWindows()
    {
        RegisterFloatingWindow(ContextPanel);
        RegisterFloatingWindow(SessionFlyout);
        RegisterFloatingWindow(FavoritesPanel);
        RegisterFloatingWindow(TelemetryPanel);
        RegisterFloatingWindow(SelectedStarCard);
        RegisterFloatingWindow(StarPreviewPanel);
        RegisterFloatingWindow(BottomActionsPanel);
        RegisterFloatingWindow(SystemOverviewPanel);
        RegisterFloatingWindow(SystemGraphicsPanel);
        RegisterFloatingWindow(SystemObjectPanel);
    }

    private void RegisterFloatingWindow(FrameworkElement element)
    {
        if (_floatingWindowTransforms.ContainsKey(element))
            return;

        var translate = new TranslateTransform();
        element.RenderTransform = translate;
        _floatingWindowTransforms[element] = translate;
        element.PreviewMouseLeftButtonDown += FloatingWindow_PreviewMouseLeftButtonDown;
        element.PreviewMouseMove += FloatingWindow_PreviewMouseMove;
        element.PreviewMouseLeftButtonUp += FloatingWindow_PreviewMouseLeftButtonUp;
    }

    private void AnimateGalaxyRail()
    {
        if (GalaxyRailPanel is null)
            return;

        var transform = new TranslateTransform { X = -10.0 };
        GalaxyRailPanel.RenderTransform = transform;
        GalaxyRailPanel.Opacity = 0.0;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        GalaxyRailPanel.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easing });
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = easing });
    }

    private void FloatingWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement window)
            return;

        BringFloatingWindowToFront(window);
        if (window == StarPreviewPanel && _starPreviewFullscreen)
            return;

        Point localPoint = e.GetPosition(window);
        if (window != TelemetryPanel && window != BottomActionsPanel && localPoint.Y > 48.0)
            return;

        if (IsFloatingDragBlocked(e.OriginalSource as DependencyObject))
            return;

        _draggedFloatingWindow = window;
        _floatingDragStartPoint = e.GetPosition(this);
        TranslateTransform transform = EnsureFloatingWindowTransform(window);
        _floatingDragStartX = transform.X;
        _floatingDragStartY = transform.Y;
        window.CaptureMouse();
    }

    private void FloatingWindow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedFloatingWindow is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(this);
        System.Windows.Vector delta = current - _floatingDragStartPoint;
        TranslateTransform transform = EnsureFloatingWindowTransform(_draggedFloatingWindow);
        transform.X = _floatingDragStartX + delta.X;
        transform.Y = _floatingDragStartY + delta.Y;
        e.Handled = true;
    }

    private void FloatingWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedFloatingWindow is null)
            return;

        _draggedFloatingWindow.ReleaseMouseCapture();
        _draggedFloatingWindow = null;
    }

    private TranslateTransform EnsureFloatingWindowTransform(FrameworkElement element)
    {
        if (_floatingWindowTransforms.TryGetValue(element, out TranslateTransform? transform))
            return transform;

        transform = new TranslateTransform();
        element.RenderTransform = transform;
        _floatingWindowTransforms[element] = transform;
        return transform;
    }

    private void BringFloatingWindowToFront(FrameworkElement element)
    {
        Panel.SetZIndex(element, ++_floatingWindowZIndex);
        _activeFloatingWindow = element;
        UpdateSelectedStarCardPresentation();
    }

    private void ResetFloatingWindowPosition(FrameworkElement element)
    {
        TranslateTransform transform = EnsureFloatingWindowTransform(element);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0.0;
        transform.Y = 0.0;
    }

    private static bool IsFloatingDragBlocked(DependencyObject? source)
    {
        return FindVisualAncestor<ButtonBase>(source) is not null ||
            FindVisualAncestor<Slider>(source) is not null ||
            FindVisualAncestor<TextBoxBase>(source) is not null ||
            FindVisualAncestor<ComboBox>(source) is not null ||
            FindVisualAncestor<ComboBoxItem>(source) is not null ||
            FindVisualAncestor<ListBoxItem>(source) is not null ||
            FindVisualAncestor<Image>(source) is not null ||
            FindVisualAncestor<ListBox>(source) is not null ||
            FindVisualAncestor<ScrollBar>(source) is not null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = GetUiParent(current);
        }

        return null;
    }

    private static DependencyObject? GetUiParent(DependencyObject source)
    {
        try
        {
            if (source is Visual or System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(source);
        }
        catch (InvalidOperationException)
        {
        }

        return LogicalTreeHelper.GetParent(source);
    }

    private void SetFloatingVisibility(FrameworkElement element, bool visible, double visibleOpacity = 1.0)
    {
        var duration = TimeSpan.FromMilliseconds(visible ? 220 : 165);
        var easing = new CubicEase { EasingMode = visible ? EasingMode.EaseOut : EasingMode.EaseIn };
        if (visible)
        {
            bool wasClosedOrClosing = element.Visibility != Visibility.Visible || !element.IsHitTestVisible;
            if (wasClosedOrClosing)
                ResetFloatingWindowPosition(element);

            if (element.Visibility != Visibility.Visible)
            {
                element.Opacity = 0.0;
                element.Visibility = Visibility.Visible;
            }

            element.IsHitTestVisible = true;
            BringFloatingWindowToFront(element);
            element.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(visibleOpacity, duration) { EasingFunction = easing });
            return;
        }

        element.IsHitTestVisible = false;
        if (_activeFloatingWindow == element)
            _activeFloatingWindow = null;
        var animation = new DoubleAnimation(0.0, duration) { EasingFunction = easing };
        animation.Completed += (_, _) =>
        {
            if (!element.IsHitTestVisible && element.Opacity <= 0.02)
            {
                element.Visibility = Visibility.Collapsed;
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = visibleOpacity;
                ResetFloatingWindowPosition(element);
            }
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation);
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

    private void NavSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string section)
            return;

        if (ContextPanel.Visibility == Visibility.Visible &&
            ContextPanel.IsHitTestVisible &&
            string.Equals(_activeHudSection, section, StringComparison.Ordinal))
        {
            CloseContextPanel();
            return;
        }

        ShowContextSection(section, forceOpen: true);
    }

    private void CloseContextPanel_Click(object sender, RoutedEventArgs e)
    {
        CloseContextPanel();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TelemetryToggle_Click(object sender, RoutedEventArgs e)
    {
        _telemetryVisible = !_telemetryVisible;
        SetFloatingVisibility(TelemetryPanel, _telemetryVisible);
        TelemetryToggleButton.Opacity = _telemetryVisible ? 1.0 : 0.62;
    }

    private void BottomActionsToggle_Click(object sender, RoutedEventArgs e)
    {
        _bottomActionsVisible = !_bottomActionsVisible;
        SetFloatingVisibility(BottomActionsPanel, _bottomActionsVisible);
        BottomActionsToggleButton.Opacity = _bottomActionsVisible ? 1.0 : 0.72;
    }

    private void CopySeed_Click(object sender, RoutedEventArgs e)
    {
        int seed = ReadSim(sim => sim.GalaxySeed, 0);
        Clipboard.SetText(seed.ToString());
        SearchStatusText.Text = $"Seed скопирован: {seed}";
    }

    private void NewSeed_Click(object sender, RoutedEventArgs e)
    {
        ResetGalaxy(Environment.TickCount);
        SearchStatusText.Text = "Создана новая галактика. Закладки и журнал старого seed очищены.";
    }

    private void ApplySeed_Click(object sender, RoutedEventArgs e)
    {
        ApplySeedFromTextBox();
    }

    private void GalaxySeedTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ApplySeedFromTextBox();
        e.Handled = true;
    }

    private void ApplySeedFromTextBox()
    {
        if (!int.TryParse(GalaxySeedTextBox.Text, out int seed))
        {
            SearchStatusText.Text = "Seed должен быть целым числом.";
            return;
        }

        ResetGalaxy(seed);
        SearchStatusText.Text = $"Применен seed: {seed}.";
    }

    private void CloseSelectedStarCard_Click(object sender, RoutedEventArgs e)
    {
        ClearSelectedStar(force: true);
    }

    private void PinSelectedStarCard_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStar is null)
            return;

        _selectedStarCardPinned = !_selectedStarCardPinned;
        SelectedStarPinButton.Opacity = _selectedStarCardPinned ? 1.0 : 0.72;
    }

    private void TrackSelectedStar_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStar is null)
            return;

        _trackSelectedStarCamera = !_trackSelectedStarCamera;
        SelectedStarTrackButton.Opacity = _trackSelectedStarCamera ? 1.0 : 0.62;
        _lastSelectedStarTrackUpdate = TimeSpan.Zero;
        _lastSelectedStarSnapshotUpdate = TimeSpan.Zero;

        if (_trackSelectedStarCamera)
            FocusGalaxyCameraOn(_selectedStar.Position);
    }

    private void ToggleSelectedStarFavorite_Click(object sender, RoutedEventArgs e)
    {
        GalaxyObjectId? id = GetSelectedStarBookmarkId();
        if (id is null)
            return;

        ToggleBookmark(id.Value);

        UpdateSelectedStarFavoriteButton();
        UpdateFavoritesList();
    }

    private void ToggleFavoritesPanel_Click(object sender, RoutedEventArgs e)
    {
        SetFloatingVisibility(FavoritesPanel, FavoritesPanel.Visibility != Visibility.Visible);
    }

    private void ToggleSystemObjectFavorite_Click(object sender, RoutedEventArgs e)
    {
        GalaxyObjectId? id = GetCurrentSystemObjectBookmarkId();
        if (id is null)
            return;

        ToggleBookmark(id.Value);
        UpdateSystemObjectFavoriteButton();
        UpdateSelectedStarFavoriteButton();
        UpdateFavoritesList();
    }

    private void ToggleBookmark(GalaxyObjectId id)
    {
        int existingIndex = _bookmarkedObjectIds.IndexOf(id);
        if (existingIndex >= 0)
            _bookmarkedObjectIds.RemoveAt(existingIndex);
        else
            _bookmarkedObjectIds.Add(id);
    }

    private void OpenStarPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStar is null)
            return;

        SetFloatingVisibility(StarPreviewPanel, true);
    }

    private void CloseStarPreview_Click(object sender, RoutedEventArgs e)
    {
        SetFloatingVisibility(StarPreviewPanel, false);
        SetStarPreviewFullscreen(false);
    }

    private void ToggleStarPreviewFullscreen_Click(object sender, RoutedEventArgs e)
    {
        SetStarPreviewFullscreen(!_starPreviewFullscreen);
    }

    private void StarPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _starPreviewDragging = true;
        _lastStarPreviewMouse = e.GetPosition(this);
        ((UIElement)sender).CaptureMouse();
    }

    private void StarPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _starPreviewDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void StarPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_starPreviewDragging)
            return;

        Point current = e.GetPosition(this);
        System.Windows.Vector delta = current - _lastStarPreviewMouse;
        _lastStarPreviewMouse = current;

        _starPreviewYaw += (float)(delta.X * 0.012);
        _starPreviewPitch = Math.Clamp(_starPreviewPitch + (float)(delta.Y * 0.012), -1.35f, 1.35f);
        RenderSelectedStarFrame();
    }

    private void StarPreview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _starPreviewZoom = Math.Clamp(_starPreviewZoom + Math.Sign(e.Delta) * 0.08f, 0.55f, 2.2f);
        RenderSelectedStarFrame();
    }

    private void SetStarPreviewFullscreen(bool fullscreen)
    {
        _starPreviewFullscreen = fullscreen;

        if (StarPreviewPanel is null)
            return;

        Grid.SetRow(StarPreviewPanel, fullscreen ? 0 : 1);
        Grid.SetColumn(StarPreviewPanel, fullscreen ? 0 : 1);
        Grid.SetRowSpan(StarPreviewPanel, fullscreen ? 3 : 1);
        Grid.SetColumnSpan(StarPreviewPanel, fullscreen ? 4 : 3);

        StarPreviewPanel.Width = fullscreen ? double.NaN : 620;
        StarPreviewPanel.Height = fullscreen ? double.NaN : 470;
        StarPreviewPanel.Margin = fullscreen ? new Thickness(72, 54, 72, 20) : new Thickness(0);
        StarPreviewPanel.HorizontalAlignment = fullscreen ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        StarPreviewPanel.VerticalAlignment = fullscreen ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        StarPreviewFullscreenButton.Opacity = fullscreen ? 1.0 : 0.72;
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void ShowContextSection(string section, bool forceOpen)
    {
        _activeHudSection = section;
        if (forceOpen)
            SetFloatingVisibility(ContextPanel, true);

        SceneSection.Visibility = section == "Scene" ? Visibility.Visible : Visibility.Collapsed;
        PhysicsSection.Visibility = section == "Physics" ? Visibility.Visible : Visibility.Collapsed;
        VisualSection.Visibility = section == "Visual" ? Visibility.Visible : Visibility.Collapsed;
        ParametersSection.Visibility = section == "Parameters" ? Visibility.Visible : Visibility.Collapsed;
        SearchSection.Visibility = section == "Search" ? Visibility.Visible : Visibility.Collapsed;
        DiscoverySection.Visibility = section == "Discovery" ? Visibility.Visible : Visibility.Collapsed;
        ActionsSection.Visibility = section == "Actions" ? Visibility.Visible : Visibility.Collapsed;
        ObjectsSection.Visibility = section == "Objects" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsSection.Visibility = section == "Diagnostics" ? Visibility.Visible : Visibility.Collapsed;

        (ContextPanelTitleText.Text, ContextPanelSubtitleText.Text) = section switch
        {
            "Scene" => ("СЦЕНА", "Пресет и базовая навигация"),
            "Physics" => ("ФИЗИКА", "Динамика и параметры гравитации"),
            "Visual" => ("ВИЗУАЛ", "Постобработка и световое поведение"),
            "Parameters" => ("ПАРАМЕТРЫ", "Seed и параметры сессии"),
            "Search" => ("ПОИСК", "Фильтры и быстрый переход к звёздам"),
            "Discovery" => ("ОТКРЫТИЯ", "Редкие находки и рекорды экспедиции"),
            "Actions" => ("ДЕЙСТВИЯ", "Экспорт и сервисные команды"),
            "Objects" => ("ОБЪЕКТЫ", "Выбор и инспекция звёзд"),
            "Diagnostics" => ("ДИАГНОСТИКА", "Проверка структуры и телеметрия"),
            _ => ("HUD", "Контекстная панель"),
        };

        UpdateNavState(ContextPanel.Visibility == Visibility.Visible && ContextPanel.IsHitTestVisible ? section : null);
        UpdateSelectedStarCardPresentation();
    }

    private void CloseContextPanel()
    {
        SetFloatingVisibility(ContextPanel, false);
        UpdateNavState(null);
        UpdateSelectedStarCardPresentation();
    }

    private void UpdateSelectedStarCardPresentation()
    {
        if (SelectedStarCard is null)
            return;

        bool sessionOpen = SessionFlyout?.Visibility == Visibility.Visible && SessionFlyout.IsHitTestVisible;
        if (ContextPanel.IsHitTestVisible)
            ContextPanel.Opacity = sessionOpen ? 0.46 : 1.0;
        if (SelectedStarCard.IsHitTestVisible)
        {
            bool otherWindowActive = _activeFloatingWindow is not null &&
                _activeFloatingWindow != SelectedStarCard &&
                _activeFloatingWindow.Visibility == Visibility.Visible &&
                _activeFloatingWindow.IsHitTestVisible;
            SelectedStarCard.Opacity = otherWindowActive ? 0.46 : 1.0;
            if (otherWindowActive)
                Panel.SetZIndex(SelectedStarCard, 3);
        }
    }

    private void UpdateNavState(string? activeSection)
    {
        SceneNavButton.IsChecked = activeSection == "Scene";
        PhysicsNavButton.IsChecked = activeSection == "Physics";
        VisualNavButton.IsChecked = activeSection == "Visual";
        ParametersNavButton.IsChecked = activeSection == "Parameters";
        SearchNavButton.IsChecked = activeSection == "Search";
        DiscoveryNavButton.IsChecked = activeSection == "Discovery";
        ActionsNavButton.IsChecked = activeSection == "Actions";
        ObjectsNavButton.IsChecked = activeSection == "Objects";
        DiagnosticsNavButton.IsChecked = activeSection == "Diagnostics";
    }

    private void ClearSelectedStar(bool force = false)
    {
        if (_selectedStarCardPinned && !force)
            return;

        _selectedStar = null;
        _selectedStarCardPinned = false;
        _trackSelectedStarCamera = false;
        _starPreviewFullscreen = false;
        _lastSelectedStarSnapshotUpdate = TimeSpan.Zero;
        _starPreviewDragging = false;
        _starPreviewYaw = 0f;
        _starPreviewPitch = 0.18f;
        _starPreviewZoom = 1.08f;
        SetFloatingVisibility(SelectedStarCard, false);
        SetFloatingVisibility(StarPreviewPanel, false);
        GalaxySelectionMarker.Visibility = Visibility.Collapsed;
        _galaxySelectionMarkerHasPosition = false;
        SetStarPreviewFullscreen(false);
        SelectedStarFavoriteButton.Opacity = 0.62;
        SelectedStarPinButton.Opacity = 0.72;
        SelectedStarTrackButton.Opacity = 0.62;
        ObjectsHintText.Visibility = Visibility.Visible;
        SelectedStarNameText.Text = "—";
        SelectedStarTypeText.Text = "—";
        SelectedStarModelText.Text = "Модель: —";
        StarPreviewTitleText.Text = "ЗВЕЗДА";
        StarPreviewSubtitleText.Text = "3D render";
        SelectedStarDetailsText.Text = string.Empty;
        SelectedStarRenderImage.Source = null;
        LargeStarRenderImage.Source = null;
        _selectedStarRenderer?.Dispose();
        _selectedStarRenderer = null;
    }

    private void SetPauseButtonLabel(bool paused)
    {
        PauseButtonLabelText.Text = paused ? "Продолжить" : "Пауза";
    }

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
        ClearSelectedStar(force: true);

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

    private void SaveSession_Click(object sender, RoutedEventArgs e)
    {
        ToggleSessionFlyout();
    }

    private void LoadSession_Click(object sender, RoutedEventArgs e)
    {
        ToggleSessionFlyout();
    }

    private void ToggleSessionFlyout()
    {
        SetFloatingVisibility(SessionFlyout, SessionFlyout.Visibility != Visibility.Visible);
        UpdateSessionSavePathText();
        UpdateSelectedStarCardPresentation();
    }

    private void CloseSessionFlyout_Click(object sender, RoutedEventArgs e)
    {
        SetFloatingVisibility(SessionFlyout, false);
        UpdateSelectedStarCardPresentation();
    }

    private void SaveSlotName_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateSessionSavePathText();
    }

    private void BrowseSaveSession_Click(object sender, RoutedEventArgs e)
    {
        string saveDirectory = GetDefaultSavesDirectory();
        Directory.CreateDirectory(saveDirectory);
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить сессию Galaxy Sim",
            Filter = "Galaxy Sim save (*.galaxysim.json)|*.galaxysim.json|JSON (*.json)|*.json",
            DefaultExt = ".galaxysim.json",
            AddExtension = true,
            InitialDirectory = saveDirectory,
            FileName = $"galaxy-{DateTime.Now:yyyyMMdd-HHmmss}.galaxysim.json",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SaveCurrentSessionToFile(dialog.FileName);
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FFFE8"));
            ScreenshotStatus.Text = $"Сессия сохранена: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7C7C"));
            ScreenshotStatus.Text = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private void BrowseLoadSession_Click(object sender, RoutedEventArgs e)
    {
        string saveDirectory = GetDefaultSavesDirectory();
        Directory.CreateDirectory(saveDirectory);
        var dialog = new OpenFileDialog
        {
            Title = "Загрузить сессию Galaxy Sim",
            Filter = "Galaxy Sim save (*.galaxysim.json)|*.galaxysim.json|JSON (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = saveDirectory,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            LoadSessionFromFile(dialog.FileName);
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FFFE8"));
            ScreenshotStatus.Text = $"Сессия загружена: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7C7C"));
            ScreenshotStatus.Text = $"Ошибка загрузки: {ex.Message}";
        }
    }

    private void QuickSaveSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string saveDirectory = GetDefaultSavesDirectory();
            Directory.CreateDirectory(saveDirectory);
            string slotName = SanitizeSaveSlotName(SaveSlotNameTextBox.Text);
            string path = Path.Combine(saveDirectory, $"{slotName}.galaxysim.json");
            SaveCurrentSessionToFile(path);
            UpdateSessionSavePathText();
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FFFE8"));
            ScreenshotStatus.Text = $"Слот сохранен: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7C7C"));
            ScreenshotStatus.Text = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private void LoadRecentSession_Click(object sender, RoutedEventArgs e)
    {
        if (RecentSavesList.SelectedItem is not RecentSaveEntry entry)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDF8A"));
            ScreenshotStatus.Text = "Выберите сохранение в списке.";
            return;
        }

        LoadRecentSession(entry.Path);
    }

    private void RecentSavesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentSavesList.SelectedItem is RecentSaveEntry entry)
            LoadRecentSession(entry.Path);
    }

    private void LoadRecentSession(string path)
    {
        try
        {
            LoadSessionFromFile(path);
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FFFE8"));
            ScreenshotStatus.Text = $"Сессия загружена: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7C7C"));
            ScreenshotStatus.Text = $"Ошибка загрузки: {ex.Message}";
            RemoveRecentSave(path);
        }
    }

    private void DeleteRecentSession_Click(object sender, RoutedEventArgs e)
    {
        if (RecentSavesList.SelectedItem is not RecentSaveEntry entry)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDF8A"));
            ScreenshotStatus.Text = "Выберите сохранение для удаления.";
            return;
        }

        string fileName = Path.GetFileName(entry.Path);
        MessageBoxResult firstConfirm = MessageBox.Show(
            this,
            $"Удалить сохранение \"{fileName}\"?\n\nФайл будет удален с диска.",
            "Удаление сохранения",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (firstConfirm != MessageBoxResult.Yes)
            return;

        MessageBoxResult secondConfirm = MessageBox.Show(
            this,
            "Подтвердите удаление еще раз. Это действие нельзя отменить.",
            "Окончательное подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (secondConfirm != MessageBoxResult.Yes)
            return;

        try
        {
            if (File.Exists(entry.Path))
                File.Delete(entry.Path);

            RemoveRecentSave(entry.Path);
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FFFE8"));
            ScreenshotStatus.Text = $"Сохранение удалено: {fileName}";
        }
        catch (Exception ex)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7C7C"));
            ScreenshotStatus.Text = $"Ошибка удаления: {ex.Message}";
        }
    }

    private void Autosave_Changed(object sender, RoutedEventArgs e)
    {
        if (_autosaveTimer is null)
            return;

        if (AutosaveCheck.IsChecked == true)
            _autosaveTimer.Start();
        else
            _autosaveTimer.Stop();
    }

    private void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            string saveDirectory = GetDefaultSavesDirectory();
            Directory.CreateDirectory(saveDirectory);
            string path = Path.Combine(saveDirectory, "autosave.galaxysim.json");
            SaveCurrentSessionToFile(path);
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FFFE8"));
            ScreenshotStatus.Text = $"Автосейв: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ScreenshotStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7C7C"));
            ScreenshotStatus.Text = $"Ошибка автосейва: {ex.Message}";
        }
    }

    private void SaveCurrentSessionToFile(string path)
    {
        GalaxySessionState state = CreateSessionState();
        string json = JsonSerializer.Serialize(state, SessionJsonOptions);
        File.WriteAllText(path, json);
        AddRecentSave(path);
    }

    private void UpdateSessionSavePathText()
    {
        if (SessionSavePathText is null || SaveSlotNameTextBox is null)
            return;

        string slotName = SanitizeSaveSlotName(SaveSlotNameTextBox.Text);
        SessionSavePathText.Text = Path.Combine(GetDefaultSavesDirectory(), $"{slotName}.galaxysim.json");
    }

    private void LoadSessionFromFile(string path)
    {
        string json = File.ReadAllText(path);
        GalaxySessionState? state = JsonSerializer.Deserialize<GalaxySessionState>(json, SessionJsonOptions);
        if (state is null)
            throw new InvalidDataException("Файл не содержит состояние сессии.");

        ApplySessionState(state);
        AddRecentSave(path);
    }

    private static string GetDefaultSavesDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GalaxySim",
            "Saves");

    private static string GetRecentSavesMetadataPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GalaxySim",
            "recent-saves.json");

    private void LoadRecentSavesMetadata()
    {
        _recentSaveFiles.Clear();
        string metadataPath = GetRecentSavesMetadataPath();
        if (!File.Exists(metadataPath))
            return;

        try
        {
            string json = File.ReadAllText(metadataPath);
            RecentSavesState? state = JsonSerializer.Deserialize<RecentSavesState>(json, SessionJsonOptions);
            if (state?.Paths is null)
                return;

            foreach (string path in state.Paths)
            {
                if (_recentSaveFiles.Count >= MaxRecentSaveFiles)
                    break;

                if (File.Exists(path) && !_recentSaveFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _recentSaveFiles.Add(path);
            }
        }
        catch
        {
            _recentSaveFiles.Clear();
        }
    }

    private void SaveRecentSavesMetadata()
    {
        try
        {
            string metadataPath = GetRecentSavesMetadataPath();
            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
            string json = JsonSerializer.Serialize(new RecentSavesState(_recentSaveFiles.ToList()), SessionJsonOptions);
            File.WriteAllText(metadataPath, json);
        }
        catch
        {
            // Recent saves are convenience metadata; failed writes should not break the session.
        }
    }

    private void AddRecentSave(string path)
    {
        string fullPath = Path.GetFullPath(path);
        _recentSaveFiles.RemoveAll(existing => string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase));
        _recentSaveFiles.Insert(0, fullPath);
        if (_recentSaveFiles.Count > MaxRecentSaveFiles)
            _recentSaveFiles.RemoveRange(MaxRecentSaveFiles, _recentSaveFiles.Count - MaxRecentSaveFiles);

        SaveRecentSavesMetadata();
        RefreshRecentSavesList();
    }

    private void RemoveRecentSave(string path)
    {
        string fullPath = Path.GetFullPath(path);
        _recentSaveFiles.RemoveAll(existing => string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase));
        SaveRecentSavesMetadata();
        RefreshRecentSavesList();
    }

    private void RefreshRecentSavesList()
    {
        if (RecentSavesList is null)
            return;

        var entries = new List<RecentSaveEntry>();
        foreach (string path in _recentSaveFiles.ToArray())
        {
            if (!File.Exists(path))
            {
                _recentSaveFiles.Remove(path);
                continue;
            }

            var file = new FileInfo(path);
            string name = Path.GetFileNameWithoutExtension(file.Name);
            if (name.EndsWith(".galaxysim", StringComparison.OrdinalIgnoreCase))
                name = name[..^".galaxysim".Length];

            entries.Add(new RecentSaveEntry(name, file.LastWriteTime.ToString("yyyy.MM.dd HH:mm"), path));
        }

        RecentSavesList.ItemsSource = entries;
        SaveRecentSavesMetadata();
    }

    private static string SanitizeSaveSlotName(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "exploration" : value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray();
        string sanitized = new(chars);
        sanitized = sanitized.Trim('.', ' ', '-');
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"galaxy-{DateTime.Now:yyyyMMdd-HHmmss}"
            : sanitized;
    }

    private GalaxySessionState CreateSessionState()
    {
        int seed;
        int particleCount;
        GalaxyPresetType preset;
        Scenario scenario;

        lock (_simLock)
        {
            seed = _sim.GalaxySeed;
            particleCount = _sim.ParticleCount;
            preset = _sim.CurrentPreset;
            scenario = _sim.CurrentScenario;
        }

        CameraState camera;
        lock (_cameraLock)
        {
            camera = CameraState.FromCamera(_camera);
        }

        return new GalaxySessionState
        {
            GalaxySeed = seed,
            ParticleCount = particleCount,
            Preset = preset,
            Scenario = scenario,
            Camera = camera,
            SelectedObjectId = GetCurrentSelectedObjectId(seed),
            BookmarkedObjectIds = _bookmarkedObjectIds.ToList(),
            SearchSettings = CreateSearchSettings(),
            DiscoveryJournal = _discoveryJournal,
            UiState = new UiState
            {
                SystemViewerOpen = _solarSystemOverlayOpen,
                SelectedStarCardPinned = _selectedStarCardPinned,
                DiscoveryFilter = CurrentDiscoveryFilter(),
            },
        };
    }

    private GalaxyObjectId? GetCurrentSelectedObjectId(int galaxySeed)
    {
        if (_selectedStar is null)
            return null;

        if (_solarSystemOverlayOpen && _solarSystemRenderer is not null)
        {
            if (_systemSelectedMoonIndex >= 0 && _systemSelectedMoonIndex < _solarSystemRenderer.Moons.Length)
            {
                MoonInfo moon = _solarSystemRenderer.Moons[_systemSelectedMoonIndex];
                return GalaxyObjectId.ForMoon(galaxySeed, _selectedStar.Index, moon.ParentPlanetIndex, moon.Index);
            }

            if (_systemSelectedPlanetIndex >= 0)
                return GalaxyObjectId.ForPlanet(galaxySeed, _selectedStar.Index, _systemSelectedPlanetIndex);
        }

        return GalaxyObjectId.ForStar(galaxySeed, _selectedStar.Index);
    }

    private void ApplySessionState(GalaxySessionState state)
    {
        if (_solarSystemOverlayOpen)
            CloseSolarSystemOverlay();

        lock (_simLock)
        {
            if (state.ParticleCount > 0 && state.ParticleCount != _sim.ParticleCount)
                _sim.Reset(newCount: state.ParticleCount, seed: state.GalaxySeed);

            _sim.ApplyPreset(state.Preset, regenerateCurrentScenario: false);
            _sim.LoadScenario(state.Scenario, state.GalaxySeed);
            _sim.Paused = false;
        }

        if (state.Camera is not null)
        {
            lock (_cameraLock)
            {
                state.Camera.ApplyTo(_camera);
            }
        }

        _suppressUiHandlers = true;
        PresetCombo.SelectedIndex = PresetToIndex(state.Preset);
        PresetDescription.Text = GalaxyPreset.Get(state.Preset).Description;
        ApplySearchSettingsToUi(state.SearchSettings);
        SyncUiWithSimulationParams();
        UpdateGalaxySeedText();
        _suppressUiHandlers = false;

        _bookmarkedObjectIds.Clear();
        _bookmarkedObjectIds.AddRange(state.BookmarkedObjectIds.Where(id => id.GalaxySeed == state.GalaxySeed));
        _discoveryJournal = NormalizeDiscoveryJournal(state.DiscoveryJournal, state.GalaxySeed);
        SelectComboItemByTag(DiscoveryFilterCombo, state.UiState.DiscoveryFilter ?? "all");
        UpdateFavoritesList();
        UpdateDiscoveryJournalList();
        RestoreSelectedObject(
            state.SelectedObjectId,
            state.UiState.SystemViewerOpen,
            state.UiState.SelectedStarCardPinned);
    }

    private void RestoreSelectedObject(GalaxyObjectId? objectId, bool openSystemViewer, bool selectedStarCardPinned)
    {
        if (objectId is null)
        {
            ClearSelectedStar(force: true);
            return;
        }

        SelectedStarInfo? star;
        lock (_simLock)
        {
            star = _sim.GetStarByIndex(objectId.Value.StarIndex);
        }

        if (star is null)
        {
            ClearSelectedStar(force: true);
            return;
        }

        _selectedStar = star;
        _trackSelectedStarCamera = false;
        _lastSelectedStarSnapshotUpdate = TimeSpan.Zero;
        ShowSelectedStar(star);
        _selectedStarCardPinned = selectedStarCardPinned;
        SelectedStarPinButton.Opacity = _selectedStarCardPinned ? 1.0 : 0.72;
        SelectedStarTrackButton.Opacity = 0.62;
        if (objectId.Value.IsStar && !openSystemViewer)
            return;

        OpenSolarSystemOverlay(star);

        if (objectId.Value.IsMoon)
            SelectSystemObject(100 + objectId.Value.MoonIndex, focus: false);
        else if (objectId.Value.IsPlanet)
            SelectSystemObject(objectId.Value.PlanetIndex, focus: false);

        RenderSolarSystemFrame();
    }

    private void SearchQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        RunStarSearch();
        e.Handled = true;
    }

    private void RunStarSearch_Click(object sender, RoutedEventArgs e)
    {
        RunStarSearch();
    }

    private void SearchQuickFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset })
            return;

        ResetSearchFilters();

        switch (preset)
        {
            case "life":
                SearchHabitableCheck.IsChecked = true;
                SelectComboItemByTag(SearchTraitCombo, "богатая умеренная зона");
                SelectComboItemByTag(SearchSortCombo, "relevance");
                break;
            case "rings":
                SearchRingedGiantsCheck.IsChecked = true;
                SelectComboItemByTag(SearchTraitCombo, "кольцевые гиганты");
                SelectComboItemByTag(SearchSortCombo, "relevance");
                break;
            case "resonance":
                SelectComboItemByTag(SearchTraitCombo, "резонансная цепочка");
                SelectComboItemByTag(SearchSortCombo, "relevance");
                SelectComboItemByTag(SearchPlanetCountCombo, "5");
                break;
            case "young":
                SelectComboItemByTag(SearchTraitCombo, "молодая яркая");
                SelectComboItemByTag(SearchSortCombo, "relevance");
                break;
            case "debris":
                SelectComboItemByTag(SearchTraitCombo, "пояса обломков");
                SelectComboItemByTag(SearchSortCombo, "relevance");
                break;
            case "binary":
                SelectComboItemByTag(SearchBinaryCombo, "binary");
                SelectComboItemByTag(SearchSortCombo, "relevance");
                break;
        }

        RunStarSearch();
    }

    private void ResetSearchFilters()
    {
        SearchQueryTextBox.Text = string.Empty;
        SearchHabitableCheck.IsChecked = false;
        SearchRingedGiantsCheck.IsChecked = false;
        SelectComboItemByTag(SearchSpectralCombo, string.Empty);
        SelectComboItemByTag(SearchBinaryCombo, string.Empty);
        SelectComboItemByTag(SearchPlanetCountCombo, "0");
        SelectComboItemByTag(SearchSortCombo, "index");
        SelectComboItemByTag(SearchResultLimitCombo, "80");
        SelectComboItemByTag(SearchTraitCombo, string.Empty);
    }

    private void RunStarSearch()
    {
        SearchSettingsState settings = CreateSearchSettings();
        int resultLimit = Math.Clamp(settings.ResultLimit <= 0 ? 80 : settings.ResultLimit, 1, 512);
        IReadOnlyList<StarSearchEntry> results;
        lock (_simLock)
        {
            results = _sim.SearchStars(settings, resultLimit);
        }

        SearchResultsList.ItemsSource = results;
        SearchStatusText.Text = results.Count == 0
            ? "Ничего не найдено. Попробуйте ослабить фильтры."
            : $"Найдено: {results.Count}. Выберите звезду для перехода.";
    }

    private SearchSettingsState CreateSearchSettings()
    {
        string? spectralClass = SearchSpectralCombo.SelectedItem is ComboBoxItem spectralItem
            ? spectralItem.Tag as string
            : null;
        string? interestTrait = SearchTraitCombo.SelectedItem is ComboBoxItem traitItem
            ? traitItem.Tag as string
            : null;
        string? binaryFilter = SearchBinaryCombo.SelectedItem is ComboBoxItem binaryItem
            ? binaryItem.Tag as string
            : null;
        string? sortMode = SearchSortCombo.SelectedItem is ComboBoxItem sortItem
            ? sortItem.Tag as string
            : null;

        int minimumPlanetCount = 0;
        if (SearchPlanetCountCombo.SelectedItem is ComboBoxItem planetItem &&
            planetItem.Tag is string planetTag &&
            int.TryParse(planetTag, out int parsed))
        {
            minimumPlanetCount = parsed;
        }

        int resultLimit = 80;
        if (SearchResultLimitCombo.SelectedItem is ComboBoxItem limitItem &&
            limitItem.Tag is string limitTag &&
            int.TryParse(limitTag, out int parsedLimit))
        {
            resultLimit = parsedLimit;
        }

        return new SearchSettingsState
        {
            Query = SearchQueryTextBox.Text,
            SpectralClass = string.IsNullOrWhiteSpace(spectralClass) ? null : spectralClass,
            BinaryFilter = string.IsNullOrWhiteSpace(binaryFilter) ? null : binaryFilter,
            InterestTrait = string.IsNullOrWhiteSpace(interestTrait) ? null : interestTrait,
            HabitableSystemsOnly = SearchHabitableCheck.IsChecked == true,
            RingedGiantsOnly = SearchRingedGiantsCheck.IsChecked == true,
            MinimumPlanetCount = minimumPlanetCount,
            SortMode = string.IsNullOrWhiteSpace(sortMode) ? null : sortMode,
            ResultLimit = resultLimit,
        };
    }

    private void ApplySearchSettingsToUi(SearchSettingsState settings)
    {
        SearchQueryTextBox.Text = settings.Query ?? string.Empty;
        SelectComboItemByTag(SearchSpectralCombo, settings.SpectralClass ?? string.Empty);
        SelectComboItemByTag(SearchBinaryCombo, settings.BinaryFilter ?? string.Empty);
        SelectComboItemByTag(SearchPlanetCountCombo, settings.MinimumPlanetCount.ToString());
        SelectComboItemByTag(SearchSortCombo, settings.SortMode ?? "index");
        SelectComboItemByTag(SearchResultLimitCombo, (settings.ResultLimit <= 0 ? 80 : settings.ResultLimit).ToString());
        SelectComboItemByTag(SearchTraitCombo, settings.InterestTrait ?? string.Empty);
        SearchHabitableCheck.IsChecked = settings.HabitableSystemsOnly;
        SearchRingedGiantsCheck.IsChecked = settings.RingedGiantsOnly;
    }

    private static void SelectComboItemByTag(ComboBox comboBox, string tag)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                string.Equals(comboBoxItem.Tag as string, tag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResultsList.SelectedItem is not StarSearchEntry entry)
            return;

        FocusSearchResult(entry);
    }

    private void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not FavoriteObjectEntry entry)
            return;

        FocusFavorite(entry);
    }

    private void DiscoveryRecordsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDiscoverySelection)
            return;

        if (DiscoveryRecordsList.SelectedItem is not DiscoveryRecord record)
            return;

        FocusDiscoveryRecord(record);
    }

    private void DiscoveryFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiHandlers)
            return;

        UpdateDiscoveryJournalList();
    }

    private void FocusDiscoveryRecord(DiscoveryRecord record)
    {
        SelectedStarInfo? star;
        lock (_simLock)
        {
            if (record.ObjectId.GalaxySeed != _sim.GalaxySeed)
            {
                DiscoveryStatusText.Text = "Эта запись относится к другому seed.";
                return;
            }

            star = _sim.GetStarByIndex(record.ObjectId.StarIndex);
        }

        if (star is null)
        {
            DiscoveryStatusText.Text = "Система из записи не найдена в текущей галактике.";
            return;
        }

        if (_solarSystemOverlayOpen)
            CloseSolarSystemOverlay();

        _selectedStar = star;
        ShowSelectedStar(star);
        CloseContextPanel();
        _trackSelectedStarCamera = false;
        SelectedStarTrackButton.Opacity = 0.62;
        _lastSelectedStarTrackUpdate = TimeSpan.Zero;
        _lastSelectedStarSnapshotUpdate = TimeSpan.Zero;
        FocusGalaxyCameraOn(star.Position);
        DiscoveryStatusText.Text = $"Переход к открытию: {record.ObjectName}.";
    }

    private void FocusFavorite(FavoriteObjectEntry entry)
    {
        SelectedStarInfo? star;
        lock (_simLock)
        {
            star = _sim.GetStarByIndex(entry.Id.StarIndex);
        }

        if (star is null)
            return;

        if (entry.Id.IsStar)
        {
            if (_solarSystemOverlayOpen)
                CloseSolarSystemOverlay();

            _selectedStar = star;
            ShowSelectedStar(star);
            _trackSelectedStarCamera = false;
            SelectedStarTrackButton.Opacity = 0.62;
            _lastSelectedStarTrackUpdate = TimeSpan.Zero;
            _lastSelectedStarSnapshotUpdate = TimeSpan.Zero;
            FocusGalaxyCameraOn(star.Position);
            return;
        }

        _selectedStar = star;
        ShowSelectedStar(star);
        if (!_solarSystemOverlayOpen || _solarSystemStar?.Index != star.Index)
            OpenSolarSystemOverlay(star);

        int systemId = entry.Id.IsMoon ? 100 + entry.Id.MoonIndex : entry.Id.PlanetIndex;
        SelectSystemObject(systemId, focus: true);
        RenderSolarSystemFrame();
    }

    private void FocusSearchResult(StarSearchEntry entry)
    {
        SelectedStarInfo? star;
        lock (_simLock)
        {
            star = _sim.GetStarByIndex(entry.Index);
        }

        CloseContextPanel();
        if (star is null)
            return;

        if (_solarSystemOverlayOpen)
            CloseSolarSystemOverlay();

        _selectedStar = star;
        ShowSelectedStar(star);
        _trackSelectedStarCamera = false;
        SelectedStarTrackButton.Opacity = 0.62;
        _lastSelectedStarTrackUpdate = TimeSpan.Zero;
        _lastSelectedStarSnapshotUpdate = TimeSpan.Zero;
        FocusGalaxyCameraOn(star.Position);
        RenderSelectedStarFrame();
    }

    private void FocusGalaxyCameraOn(Vector3 target)
    {
        lock (_cameraLock)
        {
            _camera.Target = target;
            _camera.Distance = Math.Clamp(_camera.Distance * 0.38f, 3.2f, 18.0f);
        }
    }

    private void ObserveStarDiscovery(SelectedStarInfo star)
    {
        int seed = ReadSim(sim => sim.GalaxySeed, 0);
        GalaxyObjectId id = GalaxyObjectId.ForStar(seed, star.Index);
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(star);
        PlanetInfo[] planets = SolarSystemCatalog.Generate(star);
        AsteroidBeltInfo[] belts = SolarSystemCatalog.GenerateAsteroidBelts(star, planets);
        string[] tags = SolarSystemCatalog.InterestTags(star, planets, belts);
        int temperateCandidateCount = planets.Count(p => p.IsInHabitableZone && p.TypeCode is 0 or 1 or 6 or 9);
        int totalMoons = planets.Sum(p => p.MoonCount);
        bool changed = false;

        if (stellar.IsBinary)
        {
            changed |= AddFirstDiscovery(
                "first_binary",
                id,
                "Первая двойная система",
                $"{stellar.Summary}: {stellar.SecondarySpectralClass}-компаньон, разделение {stellar.SeparationAu:F2} а.е.",
                star.Name,
                stellar.SecondarySpectralClass);
        }

        if (stellar.Layout == BinaryLayout.CircumbinaryCandidate)
        {
            changed |= AddFirstDiscovery(
                "first_circumbinary",
                id,
                "Первый circumbinary-кандидат",
                "Планетная архитектура подходит для орбит вокруг общей пары звезд.",
                star.Name,
                $"{stellar.EffectiveHabitableZoneAu:F2} AU HZ");
        }

        if (temperateCandidateCount >= 2)
        {
            changed |= AddFirstDiscovery(
                "first_multi_temperate",
                id,
                "Первая богатая умеренная зона",
                $"В системе найдено несколько умеренных кандидатов: {temperateCandidateCount}.",
                star.Name,
                $"{temperateCandidateCount} мира");
        }

        if (tags.Any(tag => tag.Contains("мигрировавший гигант", StringComparison.OrdinalIgnoreCase)))
        {
            changed |= AddFirstDiscovery(
                "first_migrated_giant",
                id,
                "Первый мигрировавший гигант",
                "Газовый гигант оказался близко к звезде и возмутил внутреннюю архитектуру.",
                star.Name,
                "динамика");
        }

        if (tags.Any(tag => tag.Contains("крупная спутниковая экосистема", StringComparison.OrdinalIgnoreCase)))
        {
            changed |= AddFirstDiscovery(
                "first_large_moon_ecosystem",
                id,
                "Первая крупная спутниковая экосистема",
                $"Суммарное число спутников в системе: {totalMoons}.",
                star.Name,
                $"{totalMoons} лун");
        }

        changed |= AddRecordDiscovery(
            "oldest_system",
            id,
            "Самая древняя система",
            $"Возраст звезды {star.AgeGyr:F2} млрд лет.",
            star.Name,
            $"{star.AgeGyr:F2} млрд лет",
            star.AgeGyr);

        changed |= AddRecordDiscovery(
            "most_planet_rich",
            id,
            "Самая богатая планетами система",
            $"Планет в системе: {star.PlanetCount}.",
            star.Name,
            $"{star.PlanetCount} планет",
            star.PlanetCount);

        changed |= AddRecordDiscovery(
            "most_moon_rich",
            id,
            "Самая богатая спутниками система",
            $"Суммарное число спутников: {totalMoons}.",
            star.Name,
            $"{totalMoons} лун",
            totalMoons);

        if (changed)
            UpdateDiscoveryJournalList();
    }

    private bool AddFirstDiscovery(string type, GalaxyObjectId id, string title, string description, string objectName, string value)
    {
        if (_discoveryJournal.Records.Any(record => record.Type == type))
            return false;

        AddDiscoveryRecord(type, id, title, description, objectName, value, 1f);
        return true;
    }

    private bool AddRecordDiscovery(string type, GalaxyObjectId id, string title, string description, string objectName, string value, float score)
    {
        int existingIndex = _discoveryJournal.Records.FindIndex(record => record.Type == type);
        if (existingIndex >= 0 && _discoveryJournal.Records[existingIndex].Score >= score)
            return false;

        if (existingIndex >= 0)
            _discoveryJournal.Records.RemoveAt(existingIndex);

        AddDiscoveryRecord(type, id, title, description, objectName, value, score);
        return true;
    }

    private void AddDiscoveryRecord(string type, GalaxyObjectId id, string title, string description, string objectName, string value, float score)
    {
        int order = _discoveryJournal.Sequence + 1;
        _pendingDiscoveryStatus = $"Новое открытие: {title} — {objectName}.";
        _discoveryJournal.Records.Add(new DiscoveryRecord
        {
            Type = type,
            ObjectId = id,
            Title = title,
            Description = description,
            ObjectName = objectName,
            Value = value,
            Score = score,
            Order = order,
            DiscoveredAt = DateTimeOffset.Now,
        });
        _discoveryJournal = _discoveryJournal with { Sequence = order };
    }

    private void UpdateDiscoveryJournalList()
    {
        if (DiscoveryRecordsList is null)
            return;

        List<DiscoveryRecord> records = _discoveryJournal.Records
            .Where(MatchesDiscoveryFilter)
            .OrderByDescending(record => record.Order)
            .ToList();
        _suppressDiscoverySelection = true;
        DiscoveryRecordsList.ItemsSource = records;
        DiscoveryRecordsList.SelectedItem = null;
        _suppressDiscoverySelection = false;
        DiscoveryStatusText.Text = _pendingDiscoveryStatus ?? (records.Count == 0
            ? EmptyDiscoveryStatus()
            : $"Показано: {records.Count} из {_discoveryJournal.Records.Count}. Рекорды обновляются при новых находках.");
        _pendingDiscoveryStatus = null;
    }

    private bool MatchesDiscoveryFilter(DiscoveryRecord record)
    {
        string filter = CurrentDiscoveryFilter();

        return filter switch
        {
            "first" => record.Type.StartsWith("first_", StringComparison.OrdinalIgnoreCase),
            "records" => !record.Type.StartsWith("first_", StringComparison.OrdinalIgnoreCase),
            "binary" => record.Type.Contains("binary", StringComparison.OrdinalIgnoreCase),
            "habitability" => record.Type.Contains("temperate", StringComparison.OrdinalIgnoreCase) ||
                record.Type.Contains("circumbinary", StringComparison.OrdinalIgnoreCase),
            "moons" => record.Type.Contains("moon", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private string EmptyDiscoveryStatus()
    {
        if (_discoveryJournal.Records.Count == 0)
            return "Открытия появятся после выбора редких систем.";

        return "В этом фильтре пока нет записей.";
    }

    private string CurrentDiscoveryFilter()
        => DiscoveryFilterCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
            : "all";

    private static DiscoveryJournalState NormalizeDiscoveryJournal(DiscoveryJournalState? journal, int galaxySeed)
    {
        if (journal is null)
            return new DiscoveryJournalState();

        List<DiscoveryRecord> records = journal.Records
            .Where(record => record.ObjectId.GalaxySeed == galaxySeed)
            .OrderBy(record => record.Order)
            .ToList();
        int sequence = Math.Max(journal.Sequence, records.Count == 0 ? 0 : records.Max(record => record.Order));
        return journal with
        {
            Records = records,
            Sequence = sequence,
        };
    }

    private GalaxyObjectId? GetSelectedStarBookmarkId()
    {
        if (_selectedStar is null)
            return null;

        int seed = ReadSim(sim => sim.GalaxySeed, 0);
        return GalaxyObjectId.ForStar(seed, _selectedStar.Index);
    }

    private GalaxyObjectId? GetCurrentSystemObjectBookmarkId()
    {
        if (_solarSystemStar is null)
            return null;

        int seed = ReadSim(sim => sim.GalaxySeed, 0);
        if (_systemSelectedMoonIndex >= 0 && _solarSystemRenderer is not null && _systemSelectedMoonIndex < _solarSystemRenderer.Moons.Length)
        {
            MoonInfo moon = _solarSystemRenderer.Moons[_systemSelectedMoonIndex];
            return GalaxyObjectId.ForMoon(seed, _solarSystemStar.Index, moon.ParentPlanetIndex, moon.Index);
        }

        if (_systemSelectedPlanetIndex >= 0)
            return GalaxyObjectId.ForPlanet(seed, _solarSystemStar.Index, _systemSelectedPlanetIndex);

        return GalaxyObjectId.ForStar(seed, _solarSystemStar.Index);
    }

    private bool IsSelectedStarBookmarked()
    {
        GalaxyObjectId? id = GetSelectedStarBookmarkId();
        return id is not null && _bookmarkedObjectIds.Contains(id.Value);
    }

    private void UpdateSelectedStarFavoriteButton()
    {
        SelectedStarFavoriteButton.Opacity = IsSelectedStarBookmarked() ? 1.0 : 0.62;
    }

    private void UpdateSystemObjectFavoriteButton()
    {
        GalaxyObjectId? id = GetCurrentSystemObjectBookmarkId();
        SystemObjectFavoriteButton.Opacity = id is not null && _bookmarkedObjectIds.Contains(id.Value) ? 1.0 : 0.62;
    }

    private void UpdateFavoritesList()
    {
        int seed = ReadSim(sim => sim.GalaxySeed, 0);
        var entries = new List<FavoriteObjectEntry>();

        lock (_simLock)
        {
            foreach (GalaxyObjectId id in _bookmarkedObjectIds)
            {
                if (id.GalaxySeed != seed)
                    continue;

                FavoriteObjectEntry? entry = BuildFavoriteEntry(id);
                if (entry is not null)
                    entries.Add(entry);
            }
        }

        FavoritesList.ItemsSource = entries;
        FavoritesStatusText.Text = entries.Count == 0
            ? "Закладки появятся здесь после выбора звезды."
            : $"Избранных объектов: {entries.Count}.";
    }

    private FavoriteObjectEntry? BuildFavoriteEntry(GalaxyObjectId id)
    {
        StarSearchEntry? starEntry = _sim.GetSearchEntry(id.StarIndex);
        if (starEntry is null)
            return null;

        if (id.IsStar)
            return new FavoriteObjectEntry(id, starEntry.Name, "Звезда", starEntry.Type, starEntry.TraitsSummary);

        SelectedStarInfo? star = _sim.GetStarByIndex(id.StarIndex);
        if (star is null)
            return null;

        PlanetInfo[] planets = SolarSystemCatalog.Generate(star);
        if (id.PlanetIndex < 0 || id.PlanetIndex >= planets.Length)
            return null;

        PlanetInfo planet = planets[id.PlanetIndex];
        if (id.IsPlanet)
            return new FavoriteObjectEntry(id, planet.Name, "Планета", planet.Type, star.Name);

        MoonInfo[] moons = SolarSystemCatalog.GenerateMoons(star, planets);
        if (id.MoonIndex < 0 || id.MoonIndex >= moons.Length)
            return null;

        MoonInfo moon = moons[id.MoonIndex];
        return new FavoriteObjectEntry(id, moon.Name, "Спутник", moon.Type, $"{star.Name} / {planets[moon.ParentPlanetIndex].Name}");
    }

    private void UpdateGalaxySeedText()
    {
        if (GalaxySeedTextBox is null)
            return;

        int seed = ReadSim(sim => sim.GalaxySeed, 0);
        GalaxySeedTextBox.Text = seed.ToString();
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
        string stars;
        string frameTime;
        string extent;
        string aabb;
        string timings;
        string timingAabb;
        string timingSort;
        string timingTree;
        string timingPhys;
        string timingTotal;
        lock (_statsLock)
        {
            fps = _fpsTextCache;
            stars = _starsTextCache;
            frameTime = _frameTimeTextCache;
            extent = _extentTextCache;
            aabb = _aabbTextCache;
            timings = _timingsTextCache;
            timingAabb = _timingAabbTextCache;
            timingSort = _timingSortTextCache;
            timingTree = _timingTreeTextCache;
            timingPhys = _timingPhysTextCache;
            timingTotal = _timingTotalTextCache;
        }
        if (!string.Equals(FpsText.Text, fps, StringComparison.Ordinal))
            FpsText.Text = fps;
        if (!string.Equals(StarsText.Text, stars, StringComparison.Ordinal))
            StarsText.Text = stars;
        if (!string.Equals(FrameTimeText.Text, frameTime, StringComparison.Ordinal))
            FrameTimeText.Text = frameTime;
        if (!string.Equals(ExtentText.Text, extent, StringComparison.Ordinal))
            ExtentText.Text = extent;
        if (!string.Equals(AabbText.Text, aabb, StringComparison.Ordinal))
            AabbText.Text = aabb;
        if (!string.Equals(TimingsText.Text, timings, StringComparison.Ordinal))
            TimingsText.Text = timings;
        if (!string.Equals(TimingAabbText.Text, timingAabb, StringComparison.Ordinal))
            TimingAabbText.Text = timingAabb;
        if (!string.Equals(TimingSortText.Text, timingSort, StringComparison.Ordinal))
            TimingSortText.Text = timingSort;
        if (!string.Equals(TimingTreeText.Text, timingTree, StringComparison.Ordinal))
            TimingTreeText.Text = timingTree;
        if (!string.Equals(TimingPhysText.Text, timingPhys, StringComparison.Ordinal))
            TimingPhysText.Text = timingPhys;
        if (!string.Equals(TimingTotalText.Text, timingTotal, StringComparison.Ordinal))
            TimingTotalText.Text = timingTotal;

        RefreshSelectedStarTracking();
        UpdateGalaxySelectionMarker();
        RenderSelectedStarFrame();
        PresentSolarSystemFrame();
        RenderSolarSystemFrame();
    }

    private void RefreshSelectedStarTracking()
    {
        if (!_trackSelectedStarCamera || _selectedStar is null || _solarSystemOverlayOpen)
            return;

        TimeSpan now = _clock.Elapsed;
        if (now - _lastSelectedStarTrackUpdate < TimeSpan.FromSeconds(0.02))
            return;

        _lastSelectedStarTrackUpdate = now;

        SelectedStarInfo star = _selectedStar;
        lock (_cameraLock)
        {
            _camera.Target = star.Position;
        }
    }

    private void UpdateGalaxySelectionMarker()
    {
        if (_selectedStar is null || _solarSystemOverlayOpen || GalaxySelectionCanvas.ActualWidth <= 0 || GalaxySelectionCanvas.ActualHeight <= 0)
        {
            GalaxySelectionMarker.Visibility = Visibility.Collapsed;
            _galaxySelectionMarkerHasPosition = false;
            return;
        }

        Matrix4x4 viewProj;
        lock (_cameraLock)
        {
            viewProj = _camera.BuildViewProj();
        }

        if (!TryProjectGalaxyPoint(_selectedStar.Position, viewProj, out double renderX, out double renderY))
        {
            GalaxySelectionMarker.Visibility = Visibility.Collapsed;
            _galaxySelectionMarkerHasPosition = false;
            return;
        }

        double scale = Math.Min(GalaxySelectionCanvas.ActualWidth / _renderWidth, GalaxySelectionCanvas.ActualHeight / _renderHeight);
        if (scale <= 0)
        {
            GalaxySelectionMarker.Visibility = Visibility.Collapsed;
            _galaxySelectionMarkerHasPosition = false;
            return;
        }

        double imageWidth = _renderWidth * scale;
        double imageHeight = _renderHeight * scale;
        double imageLeft = (GalaxySelectionCanvas.ActualWidth - imageWidth) * 0.5;
        double imageTop = (GalaxySelectionCanvas.ActualHeight - imageHeight) * 0.5;
        double x = imageLeft + renderX * scale;
        double y = imageTop + renderY * scale;

        if (x < imageLeft || y < imageTop || x > imageLeft + imageWidth || y > imageTop + imageHeight)
        {
            GalaxySelectionMarker.Visibility = Visibility.Collapsed;
            _galaxySelectionMarkerHasPosition = false;
            return;
        }

        double targetLeft = x - GalaxySelectionMarker.Width * 0.5;
        double targetTop = y - GalaxySelectionMarker.Height * 0.5;
        if (!_galaxySelectionMarkerHasPosition ||
            Math.Abs(targetLeft - _galaxySelectionMarkerLeft) > 180.0 ||
            Math.Abs(targetTop - _galaxySelectionMarkerTop) > 180.0)
        {
            _galaxySelectionMarkerLeft = targetLeft;
            _galaxySelectionMarkerTop = targetTop;
            _galaxySelectionMarkerHasPosition = true;
        }
        else
        {
            double alpha = _dragging ? 0.46 : 0.32;
            _galaxySelectionMarkerLeft += (targetLeft - _galaxySelectionMarkerLeft) * alpha;
            _galaxySelectionMarkerTop += (targetTop - _galaxySelectionMarkerTop) * alpha;
        }

        Canvas.SetLeft(GalaxySelectionMarker, _galaxySelectionMarkerLeft);
        Canvas.SetTop(GalaxySelectionMarker, _galaxySelectionMarkerTop);
        GalaxySelectionMarker.Visibility = Visibility.Visible;
    }

    private bool TryProjectGalaxyPoint(Vector3 position, Matrix4x4 viewProj, out double x, out double y)
    {
        x = 0;
        y = 0;

        Vector4 clip = Vector4.Transform(new Vector4(position, 1f), viewProj);
        if (clip.W <= 0.0001f)
            return false;

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        if (!float.IsFinite(ndcX) || !float.IsFinite(ndcY))
            return false;

        x = (ndcX * 0.5 + 0.5) * _renderWidth;
        y = (0.5 - ndcY * 0.5) * _renderHeight;
        return ndcX >= -1.08f && ndcX <= 1.08f && ndcY >= -1.08f && ndcY <= 1.08f;
    }

    private void RenderSelectedStarFrame()
    {
        if (_selectedStarRenderer is null ||
            (SelectedStarCard.Visibility != Visibility.Visible && StarPreviewPanel.Visibility != Visibility.Visible))
        {
            return;
        }

        float time = (float)_clock.Elapsed.TotalSeconds;
        float yaw = StarPreviewPanel.Visibility == Visibility.Visible
            ? _starPreviewYaw
            : time * 0.42f;
        float pitch = StarPreviewPanel.Visibility == Visibility.Visible
            ? _starPreviewPitch
            : 0.18f + MathF.Sin(time * 0.37f) * 0.08f;
        float zoom = StarPreviewPanel.Visibility == Visibility.Visible
            ? _starPreviewZoom
            : 1.04f;
        _selectedStarRenderer.Render(time, yaw, pitch, zoom);
    }

    private void RenderSolarSystemFrame()
    {
        if (!_solarSystemOverlayOpen ||
            _solarSystemRenderer is null ||
            SystemZoomSlider is null ||
            SystemSpeedSlider is null)
        {
            return;
        }

        double elapsed = _clock.Elapsed.TotalSeconds;
        double dt = _lastSystemRenderElapsed > 0.0 ? Math.Clamp(elapsed - _lastSystemRenderElapsed, 0.0, 0.10) : 0.0;
        _lastSystemRenderElapsed = elapsed;
        float speed = _systemPaused ? 0f : (float)SystemSpeedSlider.Value;
        _systemOrbitTime += dt * SystemOrbitTimeRate * speed;
        _systemEffectTime += dt * Math.Max(speed, 0.05f);
        if (SystemAutoRotateCheck?.IsChecked == true && !_systemIsDragging)
        {
            _systemMode3D = true;
            _systemTargetYawDegrees = NormalizeDegrees(_systemTargetYawDegrees + 0.08);
        }

        UpdateSystemCameraMotion(dt);

        float orbitTime = (float)_systemOrbitTime;
        float effectTime = (float)_systemEffectTime;
        QueueSolarSystemRender(new SolarSystemRenderRequest(
            orbitTime,
            effectTime,
            (float)_systemZoom,
            _systemMode3D,
            SystemRealismCheck?.IsChecked == true,
            DegreesToRadians((float)_systemYawDegrees),
            DegreesToRadians((float)_systemPitchDegrees),
            _systemSelectedPlanetIndex,
            _systemSelectedMoonIndex,
            _systemFocusIndex,
            SystemOrbitsCheck?.IsChecked == true,
            SystemHabitableCheck?.IsChecked == true,
            _systemRenderQuality));
        SystemLabelsCanvas.Visibility = Visibility.Visible;
        UpdateSystemLabels(orbitTime);
        if (!_systemIsDragging && (_systemPreviewForceRender || elapsed - _lastSystemPreviewElapsed >= 1.0 / 18.0))
        {
            bool forcePreview = _systemPreviewForceRender;
            _systemPreviewForceRender = false;
            _lastSystemPreviewElapsed = elapsed;
            RenderSystemObjectPreviewFrame(orbitTime, effectTime, forcePreview);
        }

        SystemDateText.Text = DateTime.Today
            .AddDays(_systemOrbitTime / SystemOrbitTimeRate * 14.0)
            .AddYears(362)
            .ToString("yyyy.MM.dd  HH:mm:ss");
    }

    private void QueueSolarSystemRender(SolarSystemRenderRequest request)
    {
        lock (_systemRenderStateLock)
        {
            _systemRenderRequest = request;
            _systemRenderRequested = true;
        }
    }

    private bool TryTakeSolarSystemRenderRequest(out SolarSystemRenderRequest request)
    {
        lock (_systemRenderStateLock)
        {
            if (!_systemRenderRequested)
            {
                request = default;
                return false;
            }

            request = _systemRenderRequest;
            _systemRenderRequested = false;
            return true;
        }
    }

    private void PresentSolarSystemFrame()
    {
        SolarSystemRenderer? renderer = _solarSystemRenderer;
        if (!_solarSystemOverlayOpen || renderer is null)
            return;

        lock (_systemFrameLock)
        {
            if (!_systemHasNewFrame || _systemFrameFront is null)
                return;

            renderer.Bitmap.WritePixels(new Int32Rect(0, 0, renderer.Width, renderer.Height), _systemFrameFront, renderer.Width * 4, 0);
            _systemHasNewFrame = false;
        }
    }

    private void StartSolarSystemRenderLoop(SolarSystemRenderer renderer)
    {
        StopSolarSystemRenderLoop();

        lock (_systemFrameLock)
        {
            _systemFrameFront = new byte[renderer.Width * renderer.Height * 4];
            _systemFrameBack = new byte[renderer.Width * renderer.Height * 4];
            _systemHasNewFrame = false;
        }

        lock (_systemRenderStateLock)
        {
            _systemRenderRequested = false;
            _systemRenderRequest = default;
        }

        _systemRenderCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        CancellationToken token = _systemRenderCts.Token;
        _systemRenderLoopTask = Task.Run(() => SolarSystemRenderLoop(renderer, token), token);
    }

    private void StopSolarSystemRenderLoop()
    {
        CancellationTokenSource? cts = _systemRenderCts;
        Task? task = _systemRenderLoopTask;
        _systemRenderCts = null;
        _systemRenderLoopTask = null;

        if (cts is not null)
        {
            cts.Cancel();
            try
            {
                task?.Wait(1000);
            }
            catch (Exception)
            {
            }
            cts.Dispose();
        }

        lock (_systemRenderStateLock)
        {
            _systemRenderRequested = false;
            _systemRenderRequest = default;
        }

        lock (_systemFrameLock)
        {
            _systemHasNewFrame = false;
            _systemFrameFront = null;
            _systemFrameBack = null;
        }
    }

    private void SolarSystemRenderLoop(SolarSystemRenderer renderer, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!TryTakeSolarSystemRenderRequest(out SolarSystemRenderRequest request))
                {
                    Thread.Sleep(1);
                    continue;
                }

                byte[]? back;
                lock (_systemFrameLock)
                {
                    back = _systemFrameBack;
                }

                if (back is null)
                {
                    Thread.Sleep(1);
                    continue;
                }

                renderer.RenderToBuffer(
                    request.OrbitTime,
                    request.EffectTime,
                    request.Zoom,
                    request.Is3D,
                    request.RealismMode,
                    request.Yaw,
                    request.Pitch,
                    request.SelectedPlanetIndex,
                    request.SelectedMoonIndex,
                    request.FocusIndex,
                    request.ShowOrbits,
                    request.ShowHabitableZone,
                    request.QualityLevel,
                    back);

                lock (_systemFrameLock)
                {
                    if (token.IsCancellationRequested)
                        break;

                    (_systemFrameFront, _systemFrameBack) = (_systemFrameBack, _systemFrameFront);
                    _systemHasNewFrame = true;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void UpdateSystemCameraMotion(double dt)
    {
        if (dt <= 0.0)
        {
            _systemYawDegrees = _systemTargetYawDegrees;
            _systemPitchDegrees = _systemTargetPitchDegrees;
            _systemZoom = _systemTargetZoom;
            return;
        }

        double orbitAlpha = 1.0 - Math.Exp(-22.0 * dt);
        double zoomAlpha = 1.0 - Math.Exp(-26.0 * dt);
        _systemYawDegrees = NormalizeDegrees(_systemYawDegrees + ShortestAngleDelta(_systemYawDegrees, _systemTargetYawDegrees) * orbitAlpha);
        _systemPitchDegrees += (_systemTargetPitchDegrees - _systemPitchDegrees) * orbitAlpha;
        _systemZoom += (_systemTargetZoom - _systemZoom) * zoomAlpha;

        if (Math.Abs(ShortestAngleDelta(_systemYawDegrees, _systemTargetYawDegrees)) < 0.015)
            _systemYawDegrees = _systemTargetYawDegrees;
        if (Math.Abs(_systemTargetPitchDegrees - _systemPitchDegrees) < 0.015)
            _systemPitchDegrees = _systemTargetPitchDegrees;
        if (Math.Abs(_systemTargetZoom - _systemZoom) < 0.001)
            _systemZoom = _systemTargetZoom;
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
                _starsTextCache = "—";
                _frameTimeTextCache = "—";
                _extentTextCache = "—";
                _aabbTextCache = $"{ex.GetType().Name}: {ex.Message}";
                _timingsTextCache = $"{ex.GetType().Name}: {ex.Message}";
                _timingAabbTextCache = "—";
                _timingSortTextCache = "—";
                _timingTreeTextCache = "—";
                _timingPhysTextCache = "—";
                _timingTotalTextCache = "—";
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
            if (_solarSystemOverlayOpen)
            {
                last = sw.Elapsed;
                Thread.Sleep(33);
                continue;
            }

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
                _sim.InteractiveCameraMode = _galaxyInteractiveCameraMode;
                _sim.Step(simDt);
                var readback = _sim.Render(viewProj, eye);
                RefreshSelectedStarSnapshotFromRenderLoop(_sim, frameStart);
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
                string fpsText = $"{fpsFrames / fpsAccum:F1}";
                string starsText = $"{_particleCount:N0}";
                string frameTimeText = $"{timingTotal:F2} ms";
                string extentText = $"{bb.MaxExtent:F2}";
                string aabbText =
                    $"[{bb.Min.X:F1},{bb.Min.Y:F1},{bb.Min.Z:F1}] ->\n" +
                    $"[{bb.Max.X:F1},{bb.Max.Y:F1},{bb.Max.Z:F1}]";

                string timingsText =
                    $"AABB {timingAabb:F2} / Sort {timingSort:F2} / Tree {timingTree:F2} / Phys {timingPhys:F2}";

                lock (_statsLock)
                {
                    _fpsTextCache = fpsText;
                    _starsTextCache = starsText;
                    _frameTimeTextCache = frameTimeText;
                    _extentTextCache = extentText;
                    _aabbTextCache = aabbText;
                    _timingsTextCache = timingsText;
                    _timingAabbTextCache = $"{timingAabb:F2} ms";
                    _timingSortTextCache = $"{timingSort:F2} ms";
                    _timingTreeTextCache = $"{timingTree:F2} ms";
                    _timingPhysTextCache = $"{timingPhys:F2} ms";
                    _timingTotalTextCache = $"{timingTotal:F2} ms";
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

    private void RefreshSelectedStarSnapshotFromRenderLoop(SimulationHost sim, TimeSpan now)
    {
        if (_solarSystemOverlayOpen || _selectedStar is null)
            return;

        if (now - _lastSelectedStarSnapshotUpdate < TimeSpan.FromSeconds(0.12))
            return;

        SelectedStarInfo selected = _selectedStar;
        SelectedStarInfo? current = sim.GetStarByIndex(selected.Index);
        if (current is null)
            return;

        if (_selectedStar?.Index == selected.Index)
        {
            _selectedStar = current;
            _lastSelectedStarSnapshotUpdate = now;
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
        _galaxyInteractiveCameraMode = true;
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
        _galaxyInteractiveCameraMode = false;
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

    private bool TryMapViewportToRenderCoordinates(
        Point viewportPoint,
        FrameworkElement viewport,
        out float screenX,
        out float screenY)
    {
        screenX = 0f;
        screenY = 0f;

        if (viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0)
            return false;

        double scale = Math.Min(viewport.ActualWidth / _renderWidth, viewport.ActualHeight / _renderHeight);
        if (scale <= 0)
            return false;

        double imageWidth = _renderWidth * scale;
        double imageHeight = _renderHeight * scale;
        double imageLeft = (viewport.ActualWidth - imageWidth) * 0.5;
        double imageTop = (viewport.ActualHeight - imageHeight) * 0.5;
        double px = viewportPoint.X - imageLeft;
        double py = viewportPoint.Y - imageTop;

        if (px < 0 || py < 0 || px >= imageWidth || py >= imageHeight)
            return false;

        screenX = (float)(px / imageWidth * _renderWidth);
        screenY = (float)(py / imageHeight * _renderHeight);
        return true;
    }

    private void SelectStarAt(Point viewportPoint, FrameworkElement viewport)
    {
        if (_sim is null || _camera is null)
            return;

        if (!TryMapViewportToRenderCoordinates(viewportPoint, viewport, out float screenX, out float screenY))
            return;

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
        {
            ClearSelectedStar();
            return;
        }

        _selectedStar = star;
        ShowSelectedStar(star);
        _trackSelectedStarCamera = false;
        SelectedStarTrackButton.Opacity = 0.62;
        _lastSelectedStarSnapshotUpdate = TimeSpan.Zero;
    }

    private void ShowSelectedStar(SelectedStarInfo star)
    {
        ObserveStarDiscovery(star);
        _galaxySelectionMarkerHasPosition = false;
        SetFloatingVisibility(SelectedStarCard, true);
        UpdateSelectedStarCardPresentation();
        ObjectsHintText.Visibility = Visibility.Collapsed;
        SelectedStarTrackButton.Opacity = _trackSelectedStarCamera ? 1.0 : 0.62;
        UpdateSelectedStarFavoriteButton();
        SelectedStarNameText.Text = star.Name;
        SelectedStarTypeText.Text = $"{star.Type} · class {star.SpectralClass}";
        SelectedStarModelText.Text = $"Модель: {star.ModelKey}";
        StarPreviewTitleText.Text = star.Name;
        StarPreviewSubtitleText.Text = $"{star.Type} · class {star.SpectralClass} · {star.ModelKey}";
        BuildSelectedStarModel(star);

        float speed = star.Velocity.Length();
        string interestSummary = SolarSystemCatalog.InterestSummary(star);
        string interestReasons = SolarSystemCatalog.InterestExplanationSummary(star);
        string systemProfile = SolarSystemCatalog.SystemProfileDescription(star);
        string encyclopedia = SolarSystemCatalog.StarEncyclopedia(star);
        StellarSystemProfile stellarProfile = StarCatalog.CreateSystemProfile(star);
        string binaryDetails = stellarProfile.IsBinary
            ? $"Тип:     {stellarProfile.Summary}\n" +
              $"Companion: class {stellarProfile.SecondarySpectralClass}, {stellarProfile.SecondarySolarMass:F2} M☉, {stellarProfile.SecondaryLuminositySolar:F2} L☉\n" +
              $"Sep:     {stellarProfile.SeparationAu:F2} AU  ecc {stellarProfile.BinaryEccentricity:F2}\n" +
              $"Combined Lum: {stellarProfile.CombinedLuminositySolar:F2} L☉  HZ {stellarProfile.EffectiveHabitableZoneAu:F2} AU\n" +
              $"{stellarProfile.Description}\n\n"
            : $"{stellarProfile.Summary}\n{stellarProfile.Description}\n\n";
        SelectedStarDetailsText.Text =
            $"ПАРАМЕТРЫ\n" +
            $"Index:   {star.Index}\n" +
            $"Temp:    {star.TemperatureClass:F2}\n" +
            $"Mass:    {star.SolarMass:F2} M☉  raw {star.Mass:E2}\n" +
            $"Radius:  {star.RadiusSolar:F2} R☉\n" +
            $"Lum:     {star.LuminositySolar:F2} L☉\n" +
            $"Age:     {star.AgeGyr:F2} Gyr\n" +
            $"Planets: {star.PlanetCount}  HZ {SolarSystemCatalog.EffectiveHabitableZoneAu(star):F2} AU\n\n" +
            $"АРХИТЕКТУРА\n" +
            $"{star.SystemKind}\n" +
            $"{interestSummary}\n\n" +
            $"ЗВЕЗДНАЯ СИСТЕМА\n" +
            $"{binaryDetails}" +
            $"{systemProfile}\n\n" +
            $"ПОЧЕМУ ЭТИ ПРИЗНАКИ\n" +
            $"{interestReasons}\n\n" +
            $"ЭНЦИКЛОПЕДИЯ\n" +
            $"{star.Description}\n\n" +
            $"{encyclopedia}\n\n" +
            $"ПОЛОЖЕНИЕ\n" +
            $"Pos:   {star.Position.X,7:F2} {star.Position.Y,7:F2} {star.Position.Z,7:F2}\n" +
            $"Speed: {speed,7:F3}";
    }

    private void BuildSelectedStarModel(SelectedStarInfo star)
    {
        _selectedStarRenderer?.Dispose();
        _selectedStarRenderer = new StarGpuRenderer(star, 768, 512);
        _starPreviewYaw = star.Index * 0.017f;
        _starPreviewPitch = 0.18f;
        _starPreviewZoom = 1.08f;
        SelectedStarRenderImage.Source = _selectedStarRenderer.Bitmap;
        LargeStarRenderImage.Source = _selectedStarRenderer.Bitmap;
        RenderSelectedStarFrame();
    }

    private void OpenSelectedStarModel_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStar is null)
            return;

        SetFloatingVisibility(StarPreviewPanel, true);
    }

    private void OpenSelectedSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStar is null)
            return;

        OpenSolarSystemOverlay(_selectedStar);
    }

    private void OpenSolarSystemOverlay(SelectedStarInfo star)
    {
        _solarSystemStar = star;
        SetFloatingVisibility(SelectedStarCard, false);
        SetFloatingVisibility(StarPreviewPanel, false);
        _solarSystemWasPaused = ReadSim(sim =>
        {
            bool wasPaused = sim.Paused;
            sim.Paused = true;
            return wasPaused;
        }, false);
        SetPauseButtonLabel(true);

        StopSolarSystemRenderLoop();
        _solarSystemRenderer?.Dispose();
        _solarSystemRenderer = new SolarSystemRenderer(star, 1920, 1080);
        SystemRenderImage.Source = _solarSystemRenderer.Bitmap;
        StartSolarSystemRenderLoop(_solarSystemRenderer);
        _solarSystemPreviewRenderer?.Dispose();
        _solarSystemPreviewRenderer = new SolarSystemRenderer(star, 256, 256);
        SystemObjectPreviewBrush.ImageSource = _solarSystemPreviewRenderer.Bitmap;
        SystemStarCrumbText.Text = star.Name;
        ResetFloatingWindowPosition(SystemOverviewPanel);
        ResetFloatingWindowPosition(SystemGraphicsPanel);
        ResetFloatingWindowPosition(SystemObjectPanel);
        SystemOverviewPanel.Visibility = Visibility.Visible;
        SystemGraphicsPanel.Visibility = Visibility.Collapsed;
        SystemOverviewPanel.Opacity = 1.0;
        SystemGraphicsPanel.Opacity = 1.0;
        SystemOverviewPanel.IsHitTestVisible = true;
        SystemGraphicsPanel.IsHitTestVisible = false;

        _systemSuppressControls = true;
        UpdateSystemZoomRange(realismMode: false);
        SystemZoomSlider.Value = 0.70;
        SystemSpeedSlider.Value = 1.0;
        SystemRealismCheck.IsChecked = false;
        SystemAutoRotateCheck.IsChecked = false;
        SystemOrbitsCheck.IsChecked = true;
        SystemHabitableCheck.IsChecked = true;
        SystemLabelsCheck.IsChecked = true;
        SystemTimeScaleCombo.SelectedIndex = 1;
        _systemSuppressControls = false;

        _systemSelectedPlanetIndex = -1;
        _systemSelectedMoonIndex = -1;
        _systemSecondaryStarSelected = false;
        _systemHoverObjectId = SolarSystemRenderer.EmptyObjectId;
        _systemFocusIndex = -1;
        _systemMode3D = true;
        _systemYawDegrees = 25.0;
        _systemPitchDegrees = 58.0;
        _systemTargetYawDegrees = _systemYawDegrees;
        _systemTargetPitchDegrees = _systemPitchDegrees;
        _systemZoom = 0.70;
        _systemTargetZoom = _systemZoom;
        _systemPaused = false;
        _systemSpeedPanelVisible = true;
        SystemSpeedPanel.Visibility = Visibility.Visible;
        SystemSpeedPanel.Opacity = 1.0;
        SystemSpeedPanel.IsHitTestVisible = true;
        SystemSpeedToggleButton.Opacity = 1.0;
        _systemOrbitTime = 0.0;
        _systemEffectTime = 0.0;
        _lastSystemRenderElapsed = _clock.Elapsed.TotalSeconds;
        _lastSystemPreviewElapsed = 0.0;
        _systemPreviewForceRender = true;
        UpdateSystemModeButtons();
        UpdateSystemQualityButtons();
        ShowSystemStar();
        UpdateSystemObjectFavoriteButton();
        SetFloatingVisibility(SystemObjectPanel, true);
        SystemObjectPanel.Opacity = 1.0;
        SystemObjectPanel.IsHitTestVisible = true;

        SolarSystemOverlay.Visibility = Visibility.Visible;
        _solarSystemOverlayOpen = true;
        RenderSolarSystemFrame();
    }

    private void CloseSolarSystemOverlay_Click(object sender, RoutedEventArgs e)
    {
        CloseSolarSystemOverlay();
    }

    private void HideSystemOverview_Click(object sender, RoutedEventArgs e)
    {
        SetFloatingVisibility(SystemOverviewPanel, false);
    }

    private void HideSystemGraphics_Click(object sender, RoutedEventArgs e)
    {
        SetFloatingVisibility(SystemGraphicsPanel, false);
    }

    private void ToggleSystemObjectPanel_Click(object sender, RoutedEventArgs e)
    {
        SetFloatingVisibility(SystemObjectPanel, SystemObjectPanel.Visibility != Visibility.Visible);
    }

    private void SystemRailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action })
            return;

        switch (action)
        {
            case "Object":
                ToggleSystemObjectPanel_Click(sender, e);
                break;
            case "Overview":
                ToggleSystemRailPanel(SystemOverviewPanel);
                break;
            case "Graphics":
                ToggleSystemRailPanel(SystemGraphicsPanel);
                break;
        }
    }

    private void ToggleSystemRailPanel(UIElement panel)
    {
        bool shouldOpen = panel.Visibility != Visibility.Visible ||
            panel is FrameworkElement { IsHitTestVisible: false };
        CloseSystemRailPanels();
        if (panel is FrameworkElement element)
            SetFloatingVisibility(element, shouldOpen);
    }

    private void CloseSystemRailPanels()
    {
        SetFloatingVisibility(SystemOverviewPanel, false);
        SetFloatingVisibility(SystemGraphicsPanel, false);
    }

    private void SystemQualityPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int quality))
            return;

        _systemRenderQuality = Math.Clamp(quality, 0, 3);
        UpdateSystemQualityButtons();
        RenderSolarSystemFrame();
    }

    private void UpdateSystemQualityButtons()
    {
        Button[] buttons =
        [
            SystemQualityLowButton,
            SystemQualityBalancedButton,
            SystemQualityHighButton,
            SystemQualityUltraButton,
        ];

        for (int i = 0; i < buttons.Length; i++)
        {
            bool active = i == _systemRenderQuality;
            buttons[i].Background = new SolidColorBrush(active ? Color.FromArgb(0x94, 0x37, 0xD9, 0xFF) : Color.FromArgb(0x34, 0x0A, 0x1A, 0x2A));
            buttons[i].BorderBrush = new SolidColorBrush(active ? Color.FromRgb(0x64, 0xEA, 0xFF) : Color.FromRgb(0x24, 0x3F, 0x5E));
            buttons[i].Foreground = new SolidColorBrush(active ? Colors.White : Color.FromRgb(0xA8, 0xD7, 0xFF));
        }

        SystemQualityDescriptionText.Text = SolarSystemRenderer.RenderQualityProfile.For(_systemRenderQuality).Description;
    }

    private void CloseSolarSystemOverlay()
    {
        _solarSystemOverlayOpen = false;
        SolarSystemOverlay.Visibility = Visibility.Collapsed;
        SystemOverviewPanel.Visibility = Visibility.Collapsed;
        SystemGraphicsPanel.Visibility = Visibility.Collapsed;
        SystemHoverInfoPanel.Visibility = Visibility.Collapsed;
        StopSolarSystemRenderLoop();
        SystemRenderImage.Source = null;
        SystemObjectPreviewBrush.ImageSource = null;
        SystemLabelsCanvas.Children.Clear();
        _systemLabelElements.Clear();
        ResetFloatingWindowPosition(SystemOverviewPanel);
        ResetFloatingWindowPosition(SystemGraphicsPanel);
        ResetFloatingWindowPosition(SystemObjectPanel);
        _solarSystemRenderer?.Dispose();
        _solarSystemRenderer = null;
        _solarSystemPreviewRenderer?.Dispose();
        _solarSystemPreviewRenderer = null;
        _solarSystemStar = null;
        MutateSim(sim => sim.Paused = _solarSystemWasPaused);
        SetPauseButtonLabel(_solarSystemWasPaused);
    }

    private void SystemViewControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_systemSuppressControls)
            return;

        SetSystemTargetZoom(e.NewValue, updateSlider: false);
    }

    private void SystemSpeedControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_systemSuppressControls)
            return;

        RenderSolarSystemFrame();
    }

    private void SystemViewMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_systemSuppressControls)
            return;

        UpdateSystemZoomRange(SystemRealismCheck?.IsChecked == true);
        RenderSolarSystemFrame();
    }

    private void UpdateSystemZoomRange(bool realismMode)
    {
        if (SystemZoomSlider is null)
            return;

        double current = SystemZoomSlider.Value;
        SystemZoomSlider.Minimum = realismMode ? 0.10 : 0.15;
        SystemZoomSlider.Maximum = realismMode ? 72.0 : 48.0;
        double clamped = Math.Clamp(current, SystemZoomSlider.Minimum, SystemZoomSlider.Maximum);
        SystemZoomSlider.Value = clamped;
        _systemZoom = Math.Clamp(_systemZoom, SystemZoomSlider.Minimum, SystemZoomSlider.Maximum);
        _systemTargetZoom = Math.Clamp(_systemTargetZoom, SystemZoomSlider.Minimum, SystemZoomSlider.Maximum);
    }

    private void SetSystemTargetZoom(double zoom, bool updateSlider)
    {
        if (SystemZoomSlider is null)
            return;

        _systemTargetZoom = Math.Clamp(zoom, SystemZoomSlider.Minimum, SystemZoomSlider.Maximum);
        if (!updateSlider)
            return;

        _systemSuppressControls = true;
        SystemZoomSlider.Value = _systemTargetZoom;
        _systemSuppressControls = false;
    }

    private void SystemVisualization_Changed(object sender, RoutedEventArgs e)
    {
        if (_systemSuppressControls)
            return;

        RenderSolarSystemFrame();
    }

    private void SystemMode2D_Click(object sender, RoutedEventArgs e)
    {
        _systemMode3D = true;
    }

    private void SystemMode3D_Click(object sender, RoutedEventArgs e)
    {
        _systemMode3D = true;
        UpdateSystemModeButtons();
        RenderSolarSystemFrame();
    }

    private void UpdateSystemModeButtons()
    {
        _systemMode3D = true;
    }

    private void SystemFocusStar_Click(object sender, RoutedEventArgs e)
    {
        _systemFocusIndex = -1;
        _systemMode3D = true;
        SetSystemTargetZoom(Math.Max(_systemTargetZoom, 2.4), updateSlider: true);
        UpdateSystemModeButtons();
    }

    private void SystemFocusSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_systemSecondaryStarSelected)
        {
            _systemFocusIndex = SolarSystemRenderer.SecondaryStarId;
            _systemMode3D = true;
            SetSystemTargetZoom(Math.Max(_systemTargetZoom, 4.5), updateSlider: true);
            UpdateSystemModeButtons();
            return;
        }

        if (_systemSelectedPlanetIndex < 0 && _systemSelectedMoonIndex < 0)
        {
            SystemFocusStar_Click(sender, e);
            return;
        }

        _systemFocusIndex = _systemSelectedMoonIndex >= 0 ? 100 + _systemSelectedMoonIndex : _systemSelectedPlanetIndex;
        _systemMode3D = true;
        SetSystemTargetZoom(Math.Max(_systemTargetZoom, 4.5), updateSlider: true);
        UpdateSystemModeButtons();
    }

    private void SystemResetView_Click(object sender, RoutedEventArgs e)
    {
        _systemSuppressControls = true;
        UpdateSystemZoomRange(realismMode: false);
        SystemZoomSlider.Value = 0.70;
        SystemSpeedSlider.Value = 1.0;
        SystemRealismCheck.IsChecked = false;
        SystemAutoRotateCheck.IsChecked = false;
        SystemOrbitsCheck.IsChecked = true;
        SystemHabitableCheck.IsChecked = true;
        SystemLabelsCheck.IsChecked = true;
        _systemSuppressControls = false;

        _systemFocusIndex = -1;
        _systemSecondaryStarSelected = false;
        _systemMode3D = true;
        _systemYawDegrees = 25.0;
        _systemPitchDegrees = 58.0;
        _systemTargetYawDegrees = _systemYawDegrees;
        _systemTargetPitchDegrees = _systemPitchDegrees;
        _systemZoom = 0.70;
        _systemTargetZoom = _systemZoom;
        UpdateSystemModeButtons();
        RenderSolarSystemFrame();
    }

    private void SystemPauseToggle_Click(object sender, RoutedEventArgs e)
    {
        _systemPaused = !_systemPaused;
        SystemPauseIconText.Text = _systemPaused ? "▶" : "II";
        SystemBottomPauseIconText.Text = SystemPauseIconText.Text;
    }

    private void SystemSpeedPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !double.TryParse(tag, out double speed))
            return;

        bool wasPaused = _systemPaused;
        SystemSpeedSlider.Value = Math.Clamp(speed, SystemSpeedSlider.Minimum, SystemSpeedSlider.Maximum);
        _systemPaused = false;
        SystemPauseIconText.Text = "II";
        SystemBottomPauseIconText.Text = "II";
        if (wasPaused)
            _lastSystemRenderElapsed = _clock.Elapsed.TotalSeconds;
        RenderSolarSystemFrame();
    }

    private void SystemSpeedToggle_Click(object sender, RoutedEventArgs e)
    {
        _systemSpeedPanelVisible = !_systemSpeedPanelVisible;
        SetFloatingVisibility(SystemSpeedPanel, _systemSpeedPanelVisible);
        SystemSpeedToggleButton.Opacity = _systemSpeedPanelVisible ? 1.0 : 0.72;
    }

    private void SystemTimeScale_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_systemSuppressControls || SystemSpeedSlider is null || SystemTimeScaleCombo is null)
            return;

        SystemSpeedSlider.Value = SystemTimeScaleCombo.SelectedIndex switch
        {
            0 => 0.5,
            2 => 2.0,
            3 => 4.0,
            _ => 1.0,
        };
    }

    private void RenderSystemObjectPreviewFrame(float orbitTime, float effectTime, bool force)
    {
        if (_solarSystemPreviewRenderer is null || SystemObjectPanel.Visibility != Visibility.Visible)
            return;

        int previewFocusIndex = _systemSelectedMoonIndex >= 0
            ? 100 + _systemSelectedMoonIndex
            : _systemSecondaryStarSelected
                ? SolarSystemRenderer.SecondaryStarId
                : _systemSelectedPlanetIndex >= 0
                ? _systemSelectedPlanetIndex
                : -1;
        float previewZoom = PreviewZoomForSystemObject(previewFocusIndex);
        float previewYaw = previewFocusIndex < 0
            ? 0f
            : _solarSystemPreviewRenderer.PreviewViewYaw(previewFocusIndex, orbitTime, false);
        if (force)
        {
            _solarSystemPreviewRenderer.Render(
                orbitTime,
                effectTime,
                previewZoom,
                true,
                false,
                previewYaw,
                DegreesToRadians(previewFocusIndex < 0 ? 4f : 7f),
                _systemSelectedPlanetIndex,
                _systemSelectedMoonIndex,
                previewFocusIndex,
                false,
                false,
                _systemRenderQuality);
            return;
        }

        _solarSystemPreviewRenderer.TryRender(
            orbitTime,
            effectTime,
            previewZoom,
            true,
            false,
            previewYaw,
            DegreesToRadians(previewFocusIndex < 0 ? 4f : 7f),
            _systemSelectedPlanetIndex,
            _systemSelectedMoonIndex,
            previewFocusIndex,
            false,
            false,
            _systemRenderQuality);
    }

    private float PreviewZoomForSystemObject(int previewFocusIndex)
    {
        if (_solarSystemPreviewRenderer is null)
            return 18.0f;

        if (previewFocusIndex < 0)
            return 10.5f;

        if (previewFocusIndex == SolarSystemRenderer.SecondaryStarId)
            return 11.0f;

        if (previewFocusIndex >= 100)
        {
            int moonIndex = previewFocusIndex - 100;
            if (moonIndex < 0 || moonIndex >= _solarSystemPreviewRenderer.Moons.Length)
                return 80.0f;

            MoonInfo moon = _solarSystemPreviewRenderer.Moons[moonIndex];
            return PreviewZoomForRadius(moon.VisualRadius, targetFill: 0.58f);
        }

        if (previewFocusIndex >= _solarSystemPreviewRenderer.Planets.Length)
            return 7.5f;

        PlanetInfo planet = _solarSystemPreviewRenderer.Planets[previewFocusIndex];
        float radius = planet.HasRings
            ? MathF.Max(planet.VisualRadius, planet.RingOuterRadius * 0.72f)
            : planet.VisualRadius;
        float targetFill = planet.TypeCode switch
        {
            2 when planet.HasRings => 0.60f,
            2 => 0.72f,
            3 or 7 => 0.74f,
            _ => 0.78f,
        };
        return PreviewZoomForRadius(radius, targetFill);
    }

    private static float PreviewZoomForRadius(float radius, float targetFill)
    {
        float safeRadius = MathF.Max(radius, 0.0055f);
        float fill = Math.Clamp(targetFill, 0.45f, 0.82f);
        float desiredDistance = MathF.Max(0.030f, safeRadius * 2.0f / fill);
        float baseDistance = MathF.Max(0.004f, desiredDistance - 0.024f);
        float zoom = MathF.Pow(2.85f / baseDistance, 1f / 1.04f);
        return Math.Clamp(zoom, 8f, 220f);
    }

    private void SystemViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement viewport)
        {
            PickSystemObject(e.GetPosition(viewport), viewport, true);
            e.Handled = true;
            return;
        }

        _systemIsDragging = true;
        _systemDragMoved = false;
        _lastSystemDragPoint = e.GetPosition((IInputElement)sender);
        _lastSystemMouseTick = _clock.Elapsed;
        ((UIElement)sender).CaptureMouse();
    }

    private void SystemViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_systemIsDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            if (sender is FrameworkElement hoverViewport)
                UpdateSystemHover(e.GetPosition(hoverViewport), hoverViewport);
            return;
        }

        SystemHoverInfoPanel.Visibility = Visibility.Collapsed;
        Point point = e.GetPosition((IInputElement)sender);
        double totalMoveSq = (point.X - _lastSystemDragPoint.X) * (point.X - _lastSystemDragPoint.X)
                           + (point.Y - _lastSystemDragPoint.Y) * (point.Y - _lastSystemDragPoint.Y);
        if (!_systemDragMoved && totalMoveSq < 16.0)
            return;

        _systemDragMoved = true;
        _systemMode3D = true;
        UpdateSystemModeButtons();

        double dx = Math.Clamp(point.X - _lastSystemDragPoint.X, -54.0, 54.0);
        double dy = Math.Clamp(point.Y - _lastSystemDragPoint.Y, -54.0, 54.0);
        _lastSystemDragPoint = point;
        _lastSystemMouseTick = _clock.Elapsed;
        _systemTargetYawDegrees = NormalizeDegrees(_systemTargetYawDegrees + dx * 0.32);
        _systemTargetPitchDegrees = Math.Clamp(_systemTargetPitchDegrees - dy * 0.32, 0.0, 78.0);
        _systemYawDegrees = _systemTargetYawDegrees;
        _systemPitchDegrees = _systemTargetPitchDegrees;
    }

    private void SystemViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_systemIsDragging)
        {
            _systemIsDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
            if (_systemDragMoved)
                return;
        }

        PickSystemObject(e.GetPosition((IInputElement)sender), (FrameworkElement)sender, e.ClickCount >= 2);
    }

    private void SystemViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double steps = Math.Clamp(e.Delta / 120.0, -4.0, 4.0);
        double factor = Math.Pow(1.14, steps);
        SetSystemTargetZoom(_systemTargetZoom * factor, updateSlider: true);
        e.Handled = true;
    }

    private void UpdateSystemHover(Point point, FrameworkElement viewport)
    {
        if (_solarSystemRenderer is null || !_solarSystemOverlayOpen)
            return;

        int id = PickSystemObjectId(point, viewport);
        if (id == SolarSystemRenderer.EmptyObjectId)
        {
            SystemHoverInfoPanel.Visibility = Visibility.Collapsed;
            _systemHoverObjectId = SolarSystemRenderer.EmptyObjectId;
            return;
        }

        if (id != _systemHoverObjectId)
        {
            FillSystemHover(id);
            _systemHoverObjectId = id;
        }

        double left = Math.Clamp(point.X + 18.0, 12.0, Math.Max(12.0, SystemViewportBorder.ActualWidth - SystemHoverInfoPanel.Width - 12.0));
        double top = Math.Clamp(point.Y + 18.0, 12.0, Math.Max(12.0, SystemViewportBorder.ActualHeight - 126.0));
        SystemHoverInfoPanel.Margin = new Thickness(left + 8.0, top + 66.0, 0, 0);
        SystemHoverInfoPanel.Visibility = Visibility.Visible;
    }

    private int PickSystemObjectId(Point point, FrameworkElement viewport)
    {
        if (_solarSystemRenderer is null)
            return SolarSystemRenderer.EmptyObjectId;

        int picked = _solarSystemRenderer.PickObject(
            point.X,
            point.Y,
            viewport.ActualWidth,
            viewport.ActualHeight,
            (float)_systemOrbitTime,
            (float)_systemZoom,
            _systemMode3D,
            SystemRealismCheck?.IsChecked == true,
            DegreesToRadians((float)_systemYawDegrees),
            DegreesToRadians((float)_systemPitchDegrees),
            _systemFocusIndex);
        return picked;
    }

    private void FillSystemHover(int id)
    {
        if (_solarSystemRenderer is null || _solarSystemStar is null)
            return;

        if (id == SolarSystemRenderer.SecondaryStarId)
        {
            StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(_solarSystemStar);
            SystemHoverNameText.Text = $"{_solarSystemStar.Name} II";
            SystemHoverTypeText.Text = $"class {stellar.SecondarySpectralClass} компаньон";
            SystemHoverDetailsText.Text = stellar.Description;
            return;
        }

        if (id < 0)
        {
            SystemHoverNameText.Text = _solarSystemStar.Name;
            SystemHoverTypeText.Text = $"{_solarSystemStar.SpectralClass} {_solarSystemStar.Type}";
            SystemHoverDetailsText.Text = _solarSystemStar.Description;
            return;
        }

        if (id >= 100)
        {
            int moonIndex = id - 100;
            if (moonIndex < 0 || moonIndex >= _solarSystemRenderer.Moons.Length)
                return;

            MoonInfo moon = _solarSystemRenderer.Moons[moonIndex];
            PlanetInfo parent = _solarSystemRenderer.Planets[moon.ParentPlanetIndex];
            SystemHoverNameText.Text = moon.Name;
            SystemHoverTypeText.Text = moon.Type;
            SystemHoverDetailsText.Text = $"{moon.Description}\n\nПланета: {parent.Name}";
            return;
        }

        if (id >= _solarSystemRenderer.Planets.Length)
            return;

        PlanetInfo planet = _solarSystemRenderer.Planets[id];
        SystemHoverNameText.Text = planet.Name;
        SystemHoverTypeText.Text = planet.Type;
        SystemHoverDetailsText.Text = planet.Description;
    }

    private void PickSystemObject(Point point, FrameworkElement viewport, bool focus)
    {
        if (_solarSystemRenderer is null)
            return;

        int picked = PickSystemObjectId(point, viewport);
        if (picked == SolarSystemRenderer.EmptyObjectId)
        {
            CloseSystemRailPanels();
            SystemHoverInfoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SelectSystemObject(picked, focus);
        RenderSolarSystemFrame();
    }

    private void SelectSystemObject(int id, bool focus)
    {
        if (_solarSystemRenderer is null)
            return;

        if (id == SolarSystemRenderer.EmptyObjectId)
            return;

        _systemPreviewForceRender = true;
        SetFloatingVisibility(SystemObjectPanel, true);

        if (id == SolarSystemRenderer.SecondaryStarId)
        {
            _systemSelectedPlanetIndex = -1;
            _systemSelectedMoonIndex = -1;
            _systemSecondaryStarSelected = true;
            if (focus)
                _systemFocusIndex = SolarSystemRenderer.SecondaryStarId;
            ShowSystemSecondaryStar();
            UpdateSystemObjectFavoriteButton();
            return;
        }

        if (id < 0)
        {
            _systemSelectedPlanetIndex = -1;
            _systemSelectedMoonIndex = -1;
            _systemSecondaryStarSelected = false;
            if (focus)
                _systemFocusIndex = -1;
            ShowSystemStar();
            UpdateSystemObjectFavoriteButton();
            return;
        }

        if (id >= 100)
        {
            int moonIndex = id - 100;
            if (moonIndex < 0 || moonIndex >= _solarSystemRenderer.Moons.Length)
                return;

            MoonInfo moon = _solarSystemRenderer.Moons[moonIndex];
            _systemSelectedPlanetIndex = -1;
            _systemSelectedMoonIndex = moonIndex;
            _systemSecondaryStarSelected = false;
            if (focus)
                _systemFocusIndex = id;
            ShowSystemMoon(moon, _solarSystemRenderer.Planets[moon.ParentPlanetIndex]);
            UpdateSystemObjectFavoriteButton();
            return;
        }

        if (id >= 0 && id < _solarSystemRenderer.Planets.Length)
        {
            _systemSelectedPlanetIndex = id;
            _systemSelectedMoonIndex = -1;
            _systemSecondaryStarSelected = false;
            if (focus)
                _systemFocusIndex = id;
            ShowSystemPlanet(_solarSystemRenderer.Planets[id]);
            UpdateSystemObjectFavoriteButton();
        }
    }

    private void ShowSystemStar()
    {
        if (_solarSystemStar is null)
            return;

        SystemObjectNameText.Text = _solarSystemStar.Name;
        SystemObjectTypeText.Text = $"{_solarSystemStar.SpectralClass} {_solarSystemStar.Type}";
        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(_solarSystemStar);
        string binaryDetails = stellar.IsBinary
            ? $"\nКомпаньон          class {stellar.SecondarySpectralClass}, {stellar.SecondarySolarMass:F2} M☉\n" +
              $"Тип системы        {stellar.Summary}\n" +
              $"Разделение         {stellar.SeparationAu:F2} а.е.\n" +
              $"Эксцентриситет     {stellar.BinaryEccentricity:F2}\n" +
              $"Сумм. светимость   {stellar.CombinedLuminositySolar:F2} L☉"
            : "\nТип системы        Одиночная звезда";
        SystemObjectDetailsText.Text =
            $"Масса              {_solarSystemStar.SolarMass:F2} M☉\n" +
            $"Радиус             {_solarSystemStar.RadiusSolar:F2} R☉\n" +
            $"Светимость         {_solarSystemStar.LuminositySolar:F2} L☉\n" +
            $"Возраст            {_solarSystemStar.AgeGyr:F2} млрд лет\n" +
            $"Планеты            {_solarSystemStar.PlanetCount}\n" +
            $"Зона обитаемости   {SolarSystemCatalog.EffectiveHabitableZoneAu(_solarSystemStar):F2} а.е." +
            binaryDetails;
        SystemObjectDescriptionText.Text = SolarSystemCatalog.StarEncyclopedia(_solarSystemStar);
    }

    private void ShowSystemSecondaryStar()
    {
        if (_solarSystemStar is null)
            return;

        StellarSystemProfile stellar = StarCatalog.CreateSystemProfile(_solarSystemStar);
        if (!stellar.IsBinary)
        {
            ShowSystemStar();
            return;
        }

        SystemObjectNameText.Text = $"{_solarSystemStar.Name} II";
        SystemObjectTypeText.Text = $"class {stellar.SecondarySpectralClass} звездный компаньон";
        SystemObjectDetailsText.Text =
            $"Масса              {stellar.SecondarySolarMass:F2} M☉\n" +
            $"Радиус             {stellar.SecondaryRadiusSolar:F2} R☉\n" +
            $"Светимость         {stellar.SecondaryLuminositySolar:F2} L☉\n" +
            $"Тип системы        {stellar.Summary}\n" +
            $"Разделение         {stellar.SeparationAu:F2} а.е.\n" +
            $"Эксцентриситет     {stellar.BinaryEccentricity:F2}\n" +
            $"Общая HZ           {stellar.EffectiveHabitableZoneAu:F2} а.е.";
        SystemObjectDescriptionText.Text = stellar.Description;
    }

    private void ShowSystemPlanet(PlanetInfo planet)
    {
        SystemObjectNameText.Text = planet.Name;
        SystemObjectTypeText.Text = planet.Type;
        SystemObjectDetailsText.Text =
            $"Масса              {planet.MassEarth:F2} M⊕\n" +
            $"Радиус             {planet.RadiusEarth:F2} R⊕\n" +
            $"Орбитальный период {MathF.Pow(MathF.Max(planet.OrbitAu, 0.01f), 1.5f):F2} лет\n" +
            $"Расстояние         {planet.OrbitAu:F2} а.е.\n" +
            $"Эксцентриситет     {planet.Eccentricity:F2}\n" +
            $"Спутники           {planet.MoonCount}\n" +
            $"Зона обитаемости   {(planet.IsInHabitableZone ? "да" : "нет")}";
        SystemObjectDescriptionText.Text = SolarSystemCatalog.PlanetEncyclopedia(_solarSystemStar!, planet);
    }

    private void ShowSystemMoon(MoonInfo moon, PlanetInfo parent)
    {
        SystemObjectNameText.Text = moon.Name;
        SystemObjectTypeText.Text = moon.Type;
        SystemObjectDetailsText.Text =
            $"Планета            {parent.Name}\n" +
            $"Орбита             {moon.OrbitRenderRadius:F3} render\n" +
            $"Радиус             {moon.VisualRadius:F3} render\n" +
            $"Ледяной            {(moon.IsIcy ? "да" : "нет")}";
        SystemObjectDescriptionText.Text = SolarSystemCatalog.MoonEncyclopedia(_solarSystemStar!, parent, moon);
    }

    private void UpdateSystemLabels(float orbitTime)
    {
        if (SystemLabelsCanvas is null)
            return;

        if (SystemLabelsCheck?.IsChecked != true || _solarSystemRenderer is null || _solarSystemStar is null)
        {
            SystemLabelsCanvas.Children.Clear();
            _systemLabelElements.Clear();
            return;
        }

        var visibleLabels = new HashSet<int>();
        var occupiedLabelRects = new List<Rect>();

        AddSystemLabel(-1, _solarSystemStar.Name, orbitTime, true, visibleLabels, occupiedLabelRects);
        if (_solarSystemRenderer.HasSecondaryStar)
            AddSystemLabel(
                SolarSystemRenderer.SecondaryStarId,
                $"{_solarSystemStar.Name} II",
                orbitTime,
                true,
                visibleLabels,
                occupiedLabelRects);

        PlanetInfo[] planets = _solarSystemRenderer.Planets;
        for (int i = 0; i < planets.Length; i++)
            AddSystemLabel(i, planets[i].Name, orbitTime, i == _systemSelectedPlanetIndex, visibleLabels, occupiedLabelRects);

        MoonInfo[] moons = _solarSystemRenderer.Moons;
        for (int i = 0; i < moons.Length; i++)
        {
            MoonInfo moon = moons[i];
            AddSystemLabel(100 + i, moon.Name, orbitTime, i == _systemSelectedMoonIndex, visibleLabels, occupiedLabelRects);
        }

        foreach (int id in _systemLabelElements.Keys.ToArray())
        {
            if (visibleLabels.Contains(id))
                continue;

            SystemLabelsCanvas.Children.Remove(_systemLabelElements[id]);
            _systemLabelElements.Remove(id);
        }
    }

    private void AddSystemLabel(int id, string text, float orbitTime, bool emphasized, HashSet<int> visibleLabels, List<Rect> occupiedLabelRects)
    {
        if (_solarSystemRenderer is null ||
            !_solarSystemRenderer.TryGetObjectScreenPosition(
                id,
                SystemViewportBorder.ActualWidth,
                SystemViewportBorder.ActualHeight,
                orbitTime,
                (float)_systemZoom,
                _systemMode3D,
                SystemRealismCheck?.IsChecked == true,
                DegreesToRadians((float)_systemYawDegrees),
                DegreesToRadians((float)_systemPitchDegrees),
                _systemFocusIndex,
                out Point point))
        {
            return;
        }

        visibleLabels.Add(id);
        if (!_systemLabelElements.TryGetValue(id, out TextBlock? label))
        {
            label = new TextBlock
            {
                Padding = new Thickness(5, 1, 5, 2),
                MaxWidth = 150,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _systemLabelElements[id] = label;
            SystemLabelsCanvas.Children.Add(label);
        }

        label.Text = text;
        label.FontSize = emphasized ? 12 : 10.5;
        label.FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal;
        label.Foreground = new SolidColorBrush(emphasized ? Color.FromRgb(147, 238, 255) : Color.FromRgb(152, 183, 214));
        label.Background = new SolidColorBrush(Color.FromArgb(emphasized ? (byte)165 : (byte)70, 5, 11, 18));

        label.Measure(new Size(label.MaxWidth, double.PositiveInfinity));
        double labelWidth = Math.Min(label.DesiredSize.Width, label.MaxWidth);
        double labelHeight = Math.Max(label.DesiredSize.Height, emphasized ? 18.0 : 16.0);
        double left = Math.Clamp(point.X - labelWidth * 0.5, 4.0, Math.Max(4.0, SystemLabelsCanvas.ActualWidth - labelWidth - 4.0));
        double belowOffset = id < 0 ? 18.0 : id >= 100 ? 7.0 : 13.0;
        double top = Math.Clamp(point.Y + belowOffset, 4.0, Math.Max(4.0, SystemLabelsCanvas.ActualHeight - 20.0));
        Rect labelRect = ResolveSystemLabelRect(left, top, labelWidth, labelHeight, occupiedLabelRects);
        occupiedLabelRects.Add(InflateRect(labelRect, 3.0, 2.0));
        Canvas.SetLeft(label, labelRect.Left);
        Canvas.SetTop(label, labelRect.Top);
        Panel.SetZIndex(label, emphasized ? 4 : id >= 100 ? 1 : 2);
    }

    private Rect ResolveSystemLabelRect(double preferredLeft, double preferredTop, double width, double height, List<Rect> occupiedRects)
    {
        double canvasWidth = Math.Max(SystemLabelsCanvas.ActualWidth, width + 8.0);
        double canvasHeight = Math.Max(SystemLabelsCanvas.ActualHeight, height + 8.0);
        double maxLeft = Math.Max(4.0, canvasWidth - width - 4.0);
        double maxTop = Math.Max(4.0, canvasHeight - height - 4.0);

        double[] yOffsets = [0, height + 3, -(height + 3), (height + 3) * 2, -(height + 3) * 2, (height + 3) * 3, -(height + 3) * 3];
        double[] xOffsets = [0, width * -0.34, width * 0.34, width * -0.68, width * 0.68];
        foreach (double yOffset in yOffsets)
        {
            foreach (double xOffset in xOffsets)
            {
                var candidate = new Rect(
                    Math.Clamp(preferredLeft + xOffset, 4.0, maxLeft),
                    Math.Clamp(preferredTop + yOffset, 4.0, maxTop),
                    width,
                    height);

                if (!IntersectsAny(candidate, occupiedRects))
                    return candidate;
            }
        }

        for (int i = 1; i <= 10; i++)
        {
            var candidate = new Rect(
                Math.Clamp(preferredLeft + (i % 2 == 0 ? width * 0.42 : width * -0.42), 4.0, maxLeft),
                Math.Clamp(preferredTop + i * (height + 3.0), 4.0, maxTop),
                width,
                height);

            if (!IntersectsAny(candidate, occupiedRects))
                return candidate;
        }

        return new Rect(Math.Clamp(preferredLeft, 4.0, maxLeft), Math.Clamp(preferredTop, 4.0, maxTop), width, height);
    }

    private static bool IntersectsAny(Rect rect, List<Rect> occupiedRects)
    {
        foreach (Rect occupied in occupiedRects)
        {
            if (rect.IntersectsWith(occupied))
                return true;
        }

        return false;
    }

    private static Rect InflateRect(Rect rect, double x, double y)
    {
        rect.Inflate(x, y);
        return rect;
    }

    private static float DegreesToRadians(float degrees)
        => degrees * MathF.PI / 180f;

    private static double NormalizeDegrees(double degrees)
    {
        while (degrees > 180.0)
            degrees -= 360.0;
        while (degrees < -180.0)
            degrees += 360.0;
        return degrees;
    }

    private static double ShortestAngleDelta(double currentDegrees, double targetDegrees)
        => NormalizeDegrees(targetDegrees - currentDegrees);

    private void ShowPausedDialog(Func<Window> createWindow)
    {
        bool wasPaused = ReadSim(sim => sim.Paused, false);
        MutateSim(sim => sim.Paused = true);
        SetPauseButtonLabel(true);

        try
        {
            var window = createWindow();
            window.Owner = this;
            window.ShowDialog();
        }
        finally
        {
            MutateSim(sim => sim.Paused = wasPaused);
            SetPauseButtonLabel(wasPaused);
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
        SetPauseButtonLabel(paused);
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ResetGalaxy(Environment.TickCount);
    }

    private void ResetGalaxy(int seed)
    {
        lock (_simLock)
        {
            _sim.Reset(seed: seed);
        }

        ClearSelectedStar(force: true);
        _bookmarkedObjectIds.Clear();
        _discoveryJournal = new DiscoveryJournalState();
        SearchResultsList.ItemsSource = null;
        UpdateGalaxySeedText();
        UpdateFavoritesList();
        UpdateDiscoveryJournalList();
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
        ShowContextSection("Diagnostics", forceOpen: true);
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




