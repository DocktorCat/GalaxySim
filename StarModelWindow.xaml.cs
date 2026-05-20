using GalaxySim.Core.Simulation;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GalaxySim;

public partial class StarModelWindow : Window
{
    private readonly SelectedStarInfo _star;
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private StarGpuRenderer? _renderer;
    private Point _lastMousePoint;
    private bool _dragging;
    private bool _suppressControls = true;

    public StarModelWindow(SelectedStarInfo star)
    {
        _star = star;

        InitializeComponent();
        InitializeRenderer();
        SyncText();
        ResetView();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void InitializeRenderer()
    {
        _renderer = new StarGpuRenderer(_star, 768, 512);
        StarRenderImage.Source = _renderer.Bitmap;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _animationClock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _renderer?.Dispose();
        _renderer = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        RenderStarFrame();
    }

    private void SyncText()
    {
        StarNameText.Text = _star.Name;
        StarTypeText.Text = $"{_star.Type} - class {_star.SpectralClass} - {_star.ModelKey}";

        float speed = _star.Velocity.Length();
        StarDetailsText.Text =
            $"Index: {_star.Index}\n" +
            $"Temp:  {_star.TemperatureClass:F2}\n" +
            $"Mass:  {_star.SolarMass:F2} Msol\n" +
            $"Radius:{_star.RadiusSolar:F2} Rsol\n" +
            $"Lum:   {_star.LuminositySolar:F2} Lsol\n" +
            $"Age:   {_star.AgeGyr:F2} Gyr\n" +
            $"System:{_star.SystemKind}\n" +
            $"Planets: {_star.PlanetCount}\n" +
            $"HZ:    {_star.HabitableZoneAu:F2} AU\n" +
            $"{_star.Description}\n" +
            $"Pos:   {_star.Position.X,7:F2} {_star.Position.Y,7:F2} {_star.Position.Z,7:F2}\n" +
            $"Vel:   {_star.Velocity.X,7:F3} {_star.Velocity.Y,7:F3} {_star.Velocity.Z,7:F3}\n" +
            $"Speed: {speed,7:F3}";
    }

    private void ResetView()
    {
        _suppressControls = true;                                                        
        ZoomSlider.Value = 1.0;
        RotateXSlider.Value = 12.0;
        RotateYSlider.Value = _star.Index % 90 - 45;
        _suppressControls = false;
        ApplyViewControls();
    }

    private void ApplyViewControls()
    {
        RenderStarFrame();
    }

    private void ViewControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressControls || ZoomSlider is null || RotateXSlider is null || RotateYSlider is null)
            return;

        ApplyViewControls();
    }

    private void RenderStarFrame()
    {
        if (_renderer is null || ZoomSlider is null || RotateXSlider is null || RotateYSlider is null)
            return;

        float time = (float)_animationClock.Elapsed.TotalSeconds;
        float yaw = (float)(RotateYSlider.Value * Math.PI / 180.0);
        float pitch = (float)(RotateXSlider.Value * Math.PI / 180.0);
        float zoom = (float)ZoomSlider.Value;
        _renderer.Render(time, yaw, pitch, zoom);
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _lastMousePoint = e.GetPosition(this);
        Mouse.Capture((IInputElement)sender);
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Mouse.Capture(null);
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        Point current = e.GetPosition(this);
        Vector delta = current - _lastMousePoint;
        _lastMousePoint = current;

        _suppressControls = true;
        RotateYSlider.Value = Math.Clamp(RotateYSlider.Value + delta.X * 0.45, RotateYSlider.Minimum, RotateYSlider.Maximum);
        RotateXSlider.Value = Math.Clamp(RotateXSlider.Value + delta.Y * 0.45, RotateXSlider.Minimum, RotateXSlider.Maximum);
        _suppressControls = false;

        ApplyViewControls();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double next = ZoomSlider.Value + Math.Sign(e.Delta) * 0.08;
        ZoomSlider.Value = Math.Clamp(next, ZoomSlider.Minimum, ZoomSlider.Maximum);
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        ResetView();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
