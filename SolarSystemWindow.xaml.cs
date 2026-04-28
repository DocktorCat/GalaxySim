using GalaxySim.Core.Simulation;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GalaxySim;

public partial class SolarSystemWindow : Window
{
    private readonly SelectedStarInfo _star;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SolarSystemRenderer? _renderer;
    private bool _suppressControls = true;
    private int _selectedPlanetIndex = -1;
    private int _selectedMoonIndex = -1;
    private int _focusIndex = -1;
    private int _renderQuality = 1;
    private bool _isDragging;
    private bool _dragMoved;
    private bool _suppressObjectSelection;
    private Point _lastDragPoint;
    private TimeSpan _lastMouseTick;
    private float _orbitSmoothDx;
    private float _orbitSmoothDy;

    public SolarSystemWindow(SelectedStarInfo star)
    {
        _star = star;
        InitializeComponent();
        ApplyLanguage();
        InitializeRenderer();
        SyncText();
        ResetView();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void InitializeRenderer()
    {
        (int width, int height) = RenderSizeForQuality(_renderQuality);
        _renderer?.Dispose();
        _renderer = new SolarSystemRenderer(_star, width, height);
        SystemRenderImage.Source = _renderer.Bitmap;
        PopulateObjectList();
    }

    private void SyncText()
    {
        bool en = AppLanguageState.IsEnglish;
        SystemNameText.Text = en ? $"{_star.Name} system" : $"Система {_star.Name}";
        SystemTypeText.Text = en
            ? $"{SolarSystemCatalog.ArchitectureName(_star)} - {_star.PlanetCount} planets"
            : $"{SolarSystemCatalog.ArchitectureName(_star)} - планет: {_star.PlanetCount}";
        string metrics = en
            ? $"Star:    {_star.SpectralClass} {_star.Type}\n" +
              $"Mass:    {_star.SolarMass:F2} M☉\n" +
              $"Lum.:    {_star.LuminositySolar:F2} L☉\n" +
              $"Age:     {_star.AgeGyr:F2} Gyr\n" +
              $"HZ:      {_star.HabitableZoneAu:F2} AU\n" +
              $"Planets: {_star.PlanetCount}\n" +
              $"Moons:   {_renderer?.MoonCount ?? 0}\n" +
              $"Belts:   {_renderer?.AsteroidBelts.Length ?? 0}\n\n"
            : $"Звезда:   {_star.SpectralClass} {_star.Type}\n" +
              $"Масса:    {_star.SolarMass:F2} M☉\n" +
              $"Светим.:  {_star.LuminositySolar:F2} L☉\n" +
              $"Возраст:  {_star.AgeGyr:F2} млрд лет\n" +
              $"ОЗ:       {_star.HabitableZoneAu:F2} а.е.\n" +
              $"Планеты:  {_star.PlanetCount}\n" +
              $"Спутники: {_renderer?.MoonCount ?? 0}\n" +
              $"Пояса:    {_renderer?.AsteroidBelts.Length ?? 0}\n\n";
        SystemDetailsText.Text =
            metrics +
            $"{SolarSystemCatalog.ArchitectureDescription(_star)}\n\n" +
            $"{_star.Description}\n\n" +
            (en
                ? "Planet palette:\nrocky / oceanic / gas / ice\n\n"
                : "Палитра планет:\nкаменные / океанические / газовые / ледяные\n\n") +
            (en
                ? $"Quality: {SolarSystemRenderer.RenderQualityProfile.For(_renderQuality).DescriptionEn}"
                : $"Качество: {SolarSystemRenderer.RenderQualityProfile.For(_renderQuality).Description}");
    }

    private void ApplyLanguage()
    {
        bool en = AppLanguageState.IsEnglish;
        Title = en ? "Star System" : "Звёздная система";
        ParamsTitleText.Text = en ? "PARAMETERS" : "ПАРАМЕТРЫ";
        ZoomLabelText.Text = en ? "Scale" : "Масштаб";
        SpeedLabelText.Text = en ? "Animation speed" : "Скорость анимаций";
        QualityLabelText.Text = en ? "Quality" : "Качество";
        QualityFastItem.Content = en ? "Fast" : "Быстро";
        QualityBalancedItem.Content = en ? "Balanced" : "Баланс";
        QualityHighItem.Content = en ? "High" : "Высокое";
        QualityUltraItem.Content = en ? "Ultra 2K" : "Ультра 2K";
        Mode3DCheck.Content = en ? "3D mode" : "3D режим";
        RealismCheck.Content = en ? "Realistic scale" : "Реализм масштаба";
        YawLabelText.Text = en ? "Yaw" : "Поворот";
        PitchLabelText.Text = en ? "Pitch" : "Наклон";
        FocusStarButton.Content = en ? "To star" : "К звезде";
        FocusSelectedButton.Content = en ? "To selection" : "К выбору";
        ResetViewButton.Content = en ? "Reset view" : "Сбросить вид";
        ObjectsTitleText.Text = en ? "OBJECTS" : "ОБЪЕКТЫ";
        PausedStatusText.Text = en ? "Simulation paused" : "Симуляция приостановлена";
        CloseButton.Content = en ? "Close" : "Закрыть";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _clock.Restart();
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
        RenderFrame();
    }

    private void ObjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressControls || _suppressObjectSelection || _renderer is null || ObjectList.SelectedItem is not SystemObjectListItem item)
            return;

        SelectObject(item.Id, focus: true, syncList: false);
        RenderFrame();
    }

    private void RenderFrame()
    {
        if (_renderer is null || ZoomSlider is null || SpeedSlider is null)
            return;

        double elapsed = _clock.Elapsed.TotalSeconds;
        float speed = (float)(SpeedSlider?.Value ?? 1.0);
        float orbitTime = (float)(elapsed * 0.035 * speed);
        float effectTime = (float)(elapsed * speed);
        _renderer.Render(
            orbitTime,
            effectTime,
            (float)ZoomSlider.Value,
            Mode3DCheck?.IsChecked == true,
            RealismCheck?.IsChecked == true,
            DegreesToRadians((float)(YawSlider?.Value ?? 0)),
            DegreesToRadians((float)(PitchSlider?.Value ?? 0)),
            _selectedPlanetIndex,
            _selectedMoonIndex,
            _focusIndex,
            _renderQuality);
    }

    private void ResetView()
    {
        _suppressControls = true;
        ZoomSlider.Value = 0.34;
        SpeedSlider.Value = 1.0;
        Mode3DCheck.IsChecked = false;
        RealismCheck.IsChecked = false;
        YawSlider.Value = 25.0;
        PitchSlider.Value = 58.0;
        _focusIndex = -1;
        _suppressControls = false;
        RenderFrame();
    }

    private void ViewControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressControls)
            return;

        if (_isDragging)
            return;

        RenderFrame();
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressControls)
            return;

        if (RealismCheck?.IsChecked == true && ZoomSlider.Value > 0.16)
            ZoomSlider.Value = 0.12;

        RenderFrame();
    }

    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressControls)
            return;

        _renderQuality = Math.Clamp(QualityCombo?.SelectedIndex ?? 1, 0, 3);
        InitializeRenderer();
        SyncText();
        RenderFrame();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _orbitSmoothDx = 0f;
            _orbitSmoothDy = 0f;
            Mouse.Capture(null);
            if (_dragMoved)
                return;
        }

        if (_renderer is null || ZoomSlider is null || SpeedSlider is null)
            return;

        Point point = e.GetPosition((IInputElement)sender);
        float orbitTime = (float)(_clock.Elapsed.TotalSeconds * 0.035 * (SpeedSlider?.Value ?? 1.0));
        int picked = _renderer.PickObject(
            point.X,
            point.Y,
            ((FrameworkElement)sender).ActualWidth,
            ((FrameworkElement)sender).ActualHeight,
            orbitTime,
            (float)ZoomSlider.Value,
            Mode3DCheck?.IsChecked == true,
            RealismCheck?.IsChecked == true,
            DegreesToRadians((float)(YawSlider?.Value ?? 0)),
            DegreesToRadians((float)(PitchSlider?.Value ?? 0)),
            _focusIndex);

        if (picked < 0)
            return;

        if (picked >= 100)
        {
            int moonIndex = picked - 100;
            if (moonIndex < 0 || moonIndex >= _renderer.Moons.Length)
                return;

            SelectObject(picked, focus: true, syncList: true);
        }
        else
        {
            SelectObject(picked, focus: true, syncList: true);
        }
        RenderFrame();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double next = ZoomSlider.Value * (e.Delta > 0 ? 1.12 : 0.88);
        ZoomSlider.Value = Math.Clamp(next, ZoomSlider.Minimum, ZoomSlider.Maximum);
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragMoved = false;
        _lastDragPoint = e.GetPosition((IInputElement)sender);
        _orbitSmoothDx = 0f;
        _orbitSmoothDy = 0f;
        _lastMouseTick = _clock.Elapsed;
        Mouse.Capture((IInputElement)sender);
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point point = e.GetPosition((IInputElement)sender);
        double totalMoveSq = (point.X - _lastDragPoint.X) * (point.X - _lastDragPoint.X)
                           + (point.Y - _lastDragPoint.Y) * (point.Y - _lastDragPoint.Y);
        if (!_dragMoved && totalMoveSq < 16.0)
            return;

        _dragMoved = true;
        if (Mode3DCheck.IsChecked != true)
            Mode3DCheck.IsChecked = true;

        float dx = (float)(point.X - _lastDragPoint.X);
        float dy = (float)(point.Y - _lastDragPoint.Y);
        dx = Math.Clamp(dx, -36f, 36f);
        dy = Math.Clamp(dy, -36f, 36f);

        TimeSpan now = _clock.Elapsed;
        float mouseDt = (float)(now - _lastMouseTick).TotalSeconds;
        _lastMouseTick = now;
        if (mouseDt <= 0f || mouseDt > 0.15f)
            mouseDt = 1f / 120f;

        float alpha = 1f - MathF.Exp(-38f * mouseDt);
        _orbitSmoothDx += (dx - _orbitSmoothDx) * alpha;
        _orbitSmoothDy += (dy - _orbitSmoothDy) * alpha;

        _lastDragPoint = point;
        YawSlider.Value = NormalizeDegrees(YawSlider.Value + _orbitSmoothDx * 0.35);
        PitchSlider.Value = Math.Clamp(PitchSlider.Value - _orbitSmoothDy * 0.35, PitchSlider.Minimum, PitchSlider.Maximum);
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        ResetView();
    }

    private void FocusStar_Click(object sender, RoutedEventArgs e)
    {
        _focusIndex = -1;
        Mode3DCheck.IsChecked = true;
        ZoomSlider.Value = Math.Max(ZoomSlider.Value, 2.4);
        RenderFrame();
    }

    private void FocusSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPlanetIndex < 0 && _selectedMoonIndex < 0)
            return;

        _focusIndex = _selectedMoonIndex >= 0 ? 100 + _selectedMoonIndex : _selectedPlanetIndex;
        Mode3DCheck.IsChecked = true;
        ZoomSlider.Value = Math.Max(ZoomSlider.Value, 4.5);
        RenderFrame();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowPlanet(PlanetInfo planet)
    {
        PlanetCard.Visibility = Visibility.Visible;
        PlanetNameText.Text = planet.Name;
        PlanetTypeText.Text = planet.Type;
        PlanetDetailsText.Text = AppLanguageState.IsEnglish
            ? $"Orbit: {planet.OrbitAu:F2} AU\n" +
              $"Ecc:   {planet.Eccentricity:F2}\n" +
              $"Radius:{planet.RadiusEarth:F2} R⊕\n" +
              $"Mass:  {planet.MassEarth:F2} M⊕\n" +
              $"HZ:    {(planet.IsInHabitableZone ? "yes" : "no")}\n" +
              $"Rings: {(planet.HasRings ? "yes" : "no")}\n" +
              $"Moons: {planet.MoonCount}"
            : $"Орбита:  {planet.OrbitAu:F2} а.е.\n" +
              $"Эксц.:   {planet.Eccentricity:F2}\n" +
              $"Радиус:  {planet.RadiusEarth:F2} R⊕\n" +
              $"Масса:   {planet.MassEarth:F2} M⊕\n" +
              $"ОЗ:      {(planet.IsInHabitableZone ? "да" : "нет")}\n" +
              $"Кольца:  {(planet.HasRings ? "да" : "нет")}\n" +
              $"Луны:    {planet.MoonCount}";
        PlanetDescriptionText.Text = planet.Description;
    }

    private void ShowMoon(MoonInfo moon, PlanetInfo parent)
    {
        PlanetCard.Visibility = Visibility.Visible;
        PlanetNameText.Text = moon.Name;
        PlanetTypeText.Text = moon.Type;
        PlanetDetailsText.Text = AppLanguageState.IsEnglish
            ? $"Parent: {parent.Name}\n" +
              $"Orbit:  {moon.OrbitRenderRadius:F3} render\n" +
              $"Radius: {moon.VisualRadius:F3} render\n" +
              $"Icy:    {(moon.IsIcy ? "yes" : "no")}"
            : $"Планета: {parent.Name}\n" +
              $"Орбита:  {moon.OrbitRenderRadius:F3} render\n" +
              $"Радиус:  {moon.VisualRadius:F3} render\n" +
              $"Ледяной: {(moon.IsIcy ? "да" : "нет")}";
        PlanetDescriptionText.Text = moon.Description;
    }

    private void ShowStar()
    {
        PlanetCard.Visibility = Visibility.Visible;
        PlanetNameText.Text = _star.Name;
        PlanetTypeText.Text = $"{_star.SpectralClass} {_star.Type}";
        PlanetDetailsText.Text = AppLanguageState.IsEnglish
            ? $"Mass: {_star.SolarMass:F2} M☉\n" +
              $"Lum:  {_star.LuminositySolar:F2} L☉\n" +
              $"Age:  {_star.AgeGyr:F2} Gyr\n" +
              $"HZ:   {_star.HabitableZoneAu:F2} AU"
            : $"Масса:   {_star.SolarMass:F2} M☉\n" +
              $"Светим.: {_star.LuminositySolar:F2} L☉\n" +
              $"Возраст: {_star.AgeGyr:F2} млрд лет\n" +
              $"ОЗ:      {_star.HabitableZoneAu:F2} а.е.";
        PlanetDescriptionText.Text = _star.Description;
    }

    private void PopulateObjectList()
    {
        if (ObjectList is null || _renderer is null)
            return;

        _suppressObjectSelection = true;
        ObjectList.Items.Clear();
        ObjectList.Items.Add(new SystemObjectListItem(-1, $"★ {_star.Name}"));
        foreach (PlanetInfo planet in _renderer.Planets)
        {
            ObjectList.Items.Add(new SystemObjectListItem(planet.Index, $"{planet.Index + 1}. {planet.Name}"));
            foreach (MoonInfo moon in _renderer.Moons)
            {
                if (moon.ParentPlanetIndex == planet.Index)
                    ObjectList.Items.Add(new SystemObjectListItem(100 + moon.Index, $"   ↳ {moon.Name}"));
            }
        }

        _suppressObjectSelection = false;
    }

    private void SelectObject(int id, bool focus, bool syncList)
    {
        if (_renderer is null)
            return;

        if (id == -1)
        {
            _selectedPlanetIndex = -1;
            _selectedMoonIndex = -1;
            if (focus)
                _focusIndex = -1;
            ShowStar();
        }
        else if (id >= 100)
        {
            int moonIndex = id - 100;
            if (moonIndex < 0 || moonIndex >= _renderer.Moons.Length)
                return;

            MoonInfo moon = _renderer.Moons[moonIndex];
            _selectedPlanetIndex = -1;
            _selectedMoonIndex = moonIndex;
            if (focus)
                _focusIndex = id;
            ShowMoon(moon, _renderer.Planets[moon.ParentPlanetIndex]);
        }
        else if (id >= 0 && id < _renderer.Planets.Length)
        {
            _selectedPlanetIndex = id;
            _selectedMoonIndex = -1;
            if (focus)
                _focusIndex = id;
            ShowPlanet(_renderer.Planets[id]);
        }

        if (syncList)
            SelectObjectInList(id);
    }

    private void SelectObjectInList(int id)
    {
        if (ObjectList is null)
            return;

        _suppressObjectSelection = true;
        for (int i = 0; i < ObjectList.Items.Count; i++)
        {
            if (ObjectList.Items[i] is SystemObjectListItem item && item.Id == id)
            {
                ObjectList.SelectedIndex = i;
                break;
            }
        }
        _suppressObjectSelection = false;
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

    private static (int width, int height) RenderSizeForQuality(int quality) => quality switch
    {
        0 => (760, 456),
        2 => (1536, 922),
        3 => (2048, 1229),
        _ => (960, 576),
    };

    private sealed record SystemObjectListItem(int Id, string Label);
}
