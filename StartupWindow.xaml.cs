using System;
using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GalaxySim.Core.Simulation;

namespace GalaxySim;

public partial class StartupWindow : Window
{
    private MemoryStream? menuCursorStream;
    private Cursor? menuCursor;

    private sealed record ParticlePreset(string Label, int Count, string Description)
    {
        public override string ToString() => Label;
    }

    private sealed record GalaxyTypeOption(string Label, GalaxyPresetType Type, string Description)
    {
        public override string ToString() => Label;
    }

    private sealed record RenderResolutionOption(string Label, int Width, int Height, string Description)
    {
        public override string ToString() => Label;
    }

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
        Closed += (_, _) => Mouse.OverrideCursor = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyMenuCursor();
        TryLoadMenuImages();
        LanguageCombo.SelectedIndex = AppLanguageState.IsEnglish ? 1 : 0;
        ApplyLanguage();
        BindCombos();
        UpdatePreview();
    }

    private void ApplyMenuCursor()
    {
        menuCursorStream = CreateCursorStream();
        menuCursor = new Cursor(menuCursorStream);
        Mouse.OverrideCursor = menuCursor;
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

    private void TryLoadMenuImages()
    {
        if (TryLoadImage("menu-background.png") is { } background)
            MenuBackgroundImage.Source = background;

        if (TryLoadImage("preset-milky-way.png") is { } preview)
        {
            PresetPreviewImage.Source = preview;
            PresetPreviewImage.Opacity = 1;
        }
    }

    private static BitmapImage? TryLoadImage(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "UI", fileName);
        if (!File.Exists(path))
            return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
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
        SettingsHeaderText.Text = en ? "SCENE SETTINGS" : "НАСТРОЙКИ СЦЕНЫ";
        ParticleLabelText.Text = en ? "Star count" : "Количество звёзд";
        GalaxyLabelText.Text = en ? "Galaxy type" : "Тип галактики";
        ResolutionLabelText.Text = en ? "Render resolution" : "Разрешение рендера";
        PreviewTitleText.Text = en ? "Preset preview" : "Предпросмотр пресета";
        TipText.Text = en
            ? "Tip: for 512K/1M start with 1280x720 or 1600x900, then increase resolution."
            : "Совет: для 512K/1M начни с 1280x720 или 1600x900, затем повышай разрешение.";
        ExitButtonText.Text = en ? "Exit application" : "Выйти из приложения";
        StartButtonText.Text = en ? "Start" : "Запустить";
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

}
