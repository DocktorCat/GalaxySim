using System;
using System.Windows;
using System.Windows.Controls;
using GalaxySim.Core.Simulation;

namespace GalaxySim;

public partial class StartupWindow : Window
{
    private sealed record ParticlePreset(string Label, int Count, string Description);
    private sealed record GalaxyTypeOption(string Label, GalaxyPresetType Type, string Description);
    private sealed record RenderResolutionOption(string Label, int Width, int Height, string Description);

    private static ParticlePreset[] ParticlePresets => AppLanguageState.IsEnglish ? ParticlePresetsEn : ParticlePresetsRu;
    private static GalaxyTypeOption[] GalaxyOptions => AppLanguageState.IsEnglish ? GalaxyOptionsEn : GalaxyOptionsRu;
    private static RenderResolutionOption[] RenderResolutions => AppLanguageState.IsEnglish ? RenderResolutionsEn : RenderResolutionsRu;

    private static readonly ParticlePreset[] ParticlePresetsRu =
    [
        new("8K", 8_000, "Быстрый профиль запуска для слабых или мобильных GPU."),
        new("16K", 16_000, "Лёгкий профиль с более плотной структурой, чем 8K."),
        new("32K", 32_000, "Сбалансированный профиль для быстрой настройки сцены."),
        new("64K", 64_000, "Детальный профиль для видеокарт среднего класса."),
        new("128K", 128_000, "Профиль по умолчанию с высокой визуальной плотностью."),
        new("256K", 256_000, "Тяжёлый профиль для мощных GPU."),
        new("512K", 512_000, "Ультра-профиль для топовых GPU; запуск медленнее."),
        new("1M", 1_000_000, "Экстремальный профиль; нужен сильный GPU и запас VRAM."),
    ];

    private static readonly ParticlePreset[] ParticlePresetsEn =
    [
        new("8K", 8_000, "Fast startup profile for weak or mobile GPUs."),
        new("16K", 16_000, "Light profile with a denser structure than 8K."),
        new("32K", 32_000, "Balanced profile for quick scene iteration."),
        new("64K", 64_000, "Detailed profile for mid-range GPUs."),
        new("128K", 128_000, "Default profile with high visual density."),
        new("256K", 256_000, "Heavy profile for powerful GPUs."),
        new("512K", 512_000, "Ultra profile for high-end GPUs; slower startup."),
        new("1M", 1_000_000, "Extreme profile; requires top-tier GPU and VRAM."),
    ];

    private static readonly GalaxyTypeOption[] GalaxyOptionsRu =
    [
        new("Млечный Путь", GalaxyPresetType.MilkyWay, "Умеренное гало, яркое ядро, стабильный спиральный диск."),
        new("Андромеда (M31)", GalaxyPresetType.Andromeda, "Более крупный диск, сильнее гало и больше балдж."),
        new("NGC 1300 (перемычка)", GalaxyPresetType.NGC1300, "Спираль с перемычкой и вытянутой внутренней структурой."),
        new("M74 Вертушка", GalaxyPresetType.M74Pinwheel, "Холодная спираль анфас с мягким балджем."),
        new("Карликовая галактика", GalaxyPresetType.DwarfGalaxy, "Маленькая рассеянная галактика со слабым гало."),
    ];

    private static readonly GalaxyTypeOption[] GalaxyOptionsEn =
    [
        new("Milky Way", GalaxyPresetType.MilkyWay, "Moderate halo, bright core, stable spiral disk."),
        new("Andromeda (M31)", GalaxyPresetType.Andromeda, "Larger disk, stronger halo and larger bulge."),
        new("NGC 1300 (barred)", GalaxyPresetType.NGC1300, "Barred morphology with elongated inner structure."),
        new("M74 Pinwheel", GalaxyPresetType.M74Pinwheel, "Cold face-on spiral with softer bulge."),
        new("Dwarf Galaxy", GalaxyPresetType.DwarfGalaxy, "Small diffuse galaxy with weaker halo."),
    ];

    private static readonly RenderResolutionOption[] RenderResolutionsRu =
    [
        new("1280x720 (HD)", 1280, 720, "Быстрый рендер и минимальная задержка."),
        new("1600x900 (HD+)", 1600, 900, "Более чёткое изображение при умеренной нагрузке."),
        new("1920x1080 (Full HD)", 1920, 1080, "Баланс детализации для скриншотов."),
        new("2560x1440 (QHD)", 2560, 1440, "Высокое качество для мощных GPU."),
        new("3840x2160 (4K)", 3840, 2160, "Максимальная детализация, высокая нагрузка на GPU."),
    ];

    private static readonly RenderResolutionOption[] RenderResolutionsEn =
    [
        new("1280x720 (HD)", 1280, 720, "Fast rendering and lowest latency."),
        new("1600x900 (HD+)", 1600, 900, "Sharper image with moderate load."),
        new("1920x1080 (Full HD)", 1920, 1080, "Balanced detail for screenshots."),
        new("2560x1440 (QHD)", 2560, 1440, "High-quality image for powerful GPUs."),
        new("3840x2160 (4K)", 3840, 2160, "Maximum detail, heavy GPU load."),
    ];

    public StartupWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LanguageCombo.SelectedIndex = AppLanguageState.IsEnglish ? 1 : 0;
        ApplyLanguage();
        BindCombos();
        UpdatePreview();
    }

    private void BindCombos()
    {
        ParticlePresetCombo.ItemsSource = ParticlePresets;
        GalaxyPresetCombo.ItemsSource = GalaxyOptions;
        RenderResolutionCombo.ItemsSource = RenderResolutions;

        ParticlePresetCombo.SelectedIndex = 4;
        GalaxyPresetCombo.SelectedIndex = 0;
        RenderResolutionCombo.SelectedIndex = 0;
    }

    private void AnyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        AppLanguageState.Current = LanguageCombo.SelectedIndex == 1 ? AppLanguage.English : AppLanguage.Russian;
        ApplyLanguage();
        BindCombos();
        UpdatePreview();
    }

    private void ApplyLanguage()
    {
        bool en = AppLanguageState.IsEnglish;
        Title = en ? "Galaxy Sim Launcher" : "Запуск Galaxy Sim";
        SubtitleText.Text = en ? "Initial scene settings" : "Стартовые настройки сцены";
        ParticleLabelText.Text = en ? "Star count" : "Количество звёзд";
        GalaxyLabelText.Text = en ? "Galaxy type" : "Тип галактики";
        ResolutionLabelText.Text = en ? "Render resolution" : "Разрешение рендера";
        PreviewTitleText.Text = en ? "Preset preview" : "Предпросмотр пресета";
        TipText.Text = en
            ? "Tip: for 512K/1M start with 1280x720 or 1600x900, then increase resolution."
            : "Совет: для 512K/1M начни с 1280x720 или 1600x900, затем повышай разрешение.";
        ExitButton.Content = en ? "Exit" : "Выйти из приложения";
        StartButton.Content = en ? "Start" : "Запустить";
    }

    private void UpdatePreview()
    {
        var particle = ParticlePresetCombo.SelectedItem as ParticlePreset ?? ParticlePresets[4];
        var galaxy = GalaxyPresetCombo.SelectedItem as GalaxyTypeOption ?? GalaxyOptions[0];
        var resolution = RenderResolutionCombo.SelectedItem as RenderResolutionOption ?? RenderResolutions[0];

        PresetNameText.Text = galaxy.Label;
        PresetStarsText.Text = AppLanguageState.IsEnglish
            ? $"{particle.Count:N0} stars | {resolution.Width}x{resolution.Height}"
            : $"{particle.Count:N0} звёзд | {resolution.Width}x{resolution.Height}";
        PresetDescriptionText.Text =
            $"{particle.Description}\n{galaxy.Description}\n{resolution.Description}";
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        int selectedCount = (ParticlePresetCombo.SelectedItem as ParticlePreset)?.Count ?? 128_000;
        GalaxyPresetType selectedGalaxy = (GalaxyPresetCombo.SelectedItem as GalaxyTypeOption)?.Type ?? GalaxyPresetType.MilkyWay;
        var selectedResolution = (RenderResolutionCombo.SelectedItem as RenderResolutionOption) ?? RenderResolutions[0];

        var main = new MainWindow(
            selectedCount,
            selectedGalaxy,
            Scenario.Single,
            selectedResolution.Width,
            selectedResolution.Height);
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
