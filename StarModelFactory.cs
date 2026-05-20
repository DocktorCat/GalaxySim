using GalaxySim.Core.Simulation;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;

namespace GalaxySim;

internal enum StarModelDetail
{
    Compact,
    Full,
}

internal sealed class StarModelInstance
{
    private readonly AxisAngleRotation3D _surfaceRotation;
    private readonly double _surfaceBaseAngle;
    private readonly List<StarSurfaceLayer> _surfaceLayers = [];
    private readonly List<StarCoronaLayer> _coronaLayers = [];
    private readonly List<StarPulseLayer> _pulseLayers = [];

    public StarModelInstance(Model3DGroup root, AxisAngleRotation3D surfaceRotation)
    {
        Root = root;
        _surfaceRotation = surfaceRotation;
        _surfaceBaseAngle = surfaceRotation.Angle;
    }

    public Model3DGroup Root { get; }

    public void AddCoronaLayer(AxisAngleRotation3D rotation, ScaleTransform3D scale, SolidColorBrush brush, double speed, double phase, byte baseAlpha)
    {
        _coronaLayers.Add(new StarCoronaLayer(rotation, scale, brush, rotation.Angle, speed, phase, baseAlpha));
    }

    public void AddPulseLayer(ScaleTransform3D scale, SolidColorBrush brush, double frequency, double phase, byte baseAlpha)
    {
        _pulseLayers.Add(new StarPulseLayer(scale, brush, frequency, phase, baseAlpha));
    }

    public void AddSurfaceLayer(AxisAngleRotation3D rotation, double speed)
    {
        _surfaceLayers.Add(new StarSurfaceLayer(rotation, rotation.Angle, speed));
    }

    public void Update(double seconds)
    {
        _surfaceRotation.Angle = _surfaceBaseAngle + seconds * 4.5;

        foreach (StarSurfaceLayer layer in _surfaceLayers)
        {
            layer.Rotation.Angle = layer.BaseAngle + seconds * layer.Speed;
        }

        foreach (StarCoronaLayer layer in _coronaLayers)
        {
            double wave = Math.Sin(seconds * layer.Speed * 1.7 + layer.Phase);
            double scale = 1.0 + wave * 0.045;
            layer.Rotation.Angle = layer.BaseAngle + seconds * layer.Speed * 22.0;
            layer.Scale.ScaleX = scale;
            layer.Scale.ScaleY = scale;
            layer.Scale.ScaleZ = 1.0;
            layer.Brush.Color = WithAlpha(layer.Brush.Color, (byte)Math.Clamp(layer.BaseAlpha + wave * 34.0, 18.0, 230.0));
        }

        foreach (StarPulseLayer layer in _pulseLayers)
        {
            double wave = Math.Sin(seconds * layer.Frequency + layer.Phase);
            double scale = 1.0 + wave * 0.12;
            layer.Scale.ScaleX = scale;
            layer.Scale.ScaleY = scale;
            layer.Scale.ScaleZ = 1.0;
            layer.Brush.Color = WithAlpha(layer.Brush.Color, (byte)Math.Clamp(layer.BaseAlpha + wave * 42.0, 18.0, 230.0));
        }
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}

internal sealed record StarCoronaLayer(
    AxisAngleRotation3D Rotation,
    ScaleTransform3D Scale,
    SolidColorBrush Brush,
    double BaseAngle,
    double Speed,
    double Phase,
    byte BaseAlpha);

internal sealed record StarPulseLayer(
    ScaleTransform3D Scale,
    SolidColorBrush Brush,
    double Frequency,
    double Phase,
    byte BaseAlpha);

internal sealed record StarSurfaceLayer(
    AxisAngleRotation3D Rotation,
    double BaseAngle,
    double Speed);

internal static class StarModelFactory
{
    public static Model3DGroup CreateScene(SelectedStarInfo star, StarModelDetail detail)
    {
        return CreateInstance(star, detail).Root;
    }

    public static StarModelInstance CreateInstance(SelectedStarInfo star, StarModelDetail detail)
    {
        Color color = ColorForStar(star.ModelKey);
        double radius = RadiusForStar(star.ModelKey, detail);

        var scene = new Model3DGroup();

        var surfaceRotation = new AxisAngleRotation3D(new Vector3D(0.22, 1.0, 0.08), star.Index % 360);
        var bodyTransform = new Transform3DGroup();
        bodyTransform.Children.Add(new RotateTransform3D(surfaceRotation));
        var instance = new StarModelInstance(scene, surfaceRotation);

        int textureSize = detail == StarModelDetail.Full ? 384 : 160;
        var body = new GeometryModel3D(
            CreateSphereMesh(radius, detail == StarModelDetail.Full ? 96 : 40, detail == StarModelDetail.Full ? 54 : 22),
            CreateStarMaterial(CreatePlasmaTexture(star, textureSize, textureSize / 2)));
        body.BackMaterial = body.Material;
        body.Transform = bodyTransform;
        scene.Children.Add(body);

        if (detail == StarModelDetail.Full)
            AddSurfaceActivity(scene, star, radius, instance);

        AddGlowShell(scene, color, radius, detail, instance);

        if (detail == StarModelDetail.Full)
        {
            AddCorona(scene, star, color, radius, instance);
            AddProminences(scene, star, color, radius, instance);
        }

        return instance;
    }

    public static Color ColorForStar(string modelKey) => modelKey switch
    {
        "red_dwarf" => (Color)ColorConverter.ConvertFromString("#FF8A5C"),
        "orange_star" => (Color)ColorConverter.ConvertFromString("#FFB35C"),
        "yellow_star" => (Color)ColorConverter.ConvertFromString("#FFE08A"),
        "blue_white_star" => (Color)ColorConverter.ConvertFromString("#DDEBFF"),
        "blue_giant" => (Color)ColorConverter.ConvertFromString("#9CC9FF"),
        _ => (Color)ColorConverter.ConvertFromString("#FFD37A"),
    };

    public static double CameraDistanceForStar(string modelKey, StarModelDetail detail)
    {
        if (detail == StarModelDetail.Compact)
            return modelKey == "blue_giant" ? 4.5 : 4.0;

        return modelKey == "blue_giant" ? 5.8 : 5.4;
    }

    private static double RadiusForStar(string modelKey, StarModelDetail detail)
    {
        double radius = modelKey switch
        {
            "red_dwarf" => 0.66,
            "orange_star" => 0.76,
            "yellow_star" => 0.82,
            "blue_white_star" => 0.86,
            "blue_giant" => 0.95,
            _ => 0.8,
        };

        return detail == StarModelDetail.Compact ? radius * 0.86 : radius;
    }

    private static void AddGlowShell(Model3DGroup scene, Color color, double radius, StarModelDetail detail, StarModelInstance instance)
    {
        double scale = detail == StarModelDetail.Full ? 1.035 : 1.025;
        byte alpha = detail == StarModelDetail.Full ? (byte)70 : (byte)34;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        var shellScale = new ScaleTransform3D(1, 1, 1);

        var glow = new GeometryModel3D(
            CreateSphereMesh(radius * scale, detail == StarModelDetail.Full ? 72 : 32, detail == StarModelDetail.Full ? 38 : 18),
            new EmissiveMaterial(brush));
        glow.BackMaterial = glow.Material;
        glow.Transform = shellScale;
        scene.Children.Add(glow);

        if (detail == StarModelDetail.Full)
            instance.AddPulseLayer(shellScale, brush, 1.45, 0.4, alpha);
    }

    private static void AddSurfaceActivity(Model3DGroup scene, SelectedStarInfo star, double radius, StarModelInstance instance)
    {
        for (int i = 0; i < 2; i++)
        {
            var rotation = new AxisAngleRotation3D(
                i == 0 ? new Vector3D(0.1, 1.0, 0.16) : new Vector3D(0.24, -1.0, 0.08),
                star.Index % 360 + i * 71);
            var transform = new Transform3DGroup();
            transform.Children.Add(new RotateTransform3D(rotation));

            var layer = new GeometryModel3D(
                CreateSphereMesh(radius * (1.006 + i * 0.007), 96, 54),
                CreateStarMaterial(CreatePlasmaTexture(star, 384, 192, i * 19.37, transparentFilaments: true)));
            layer.BackMaterial = layer.Material;
            layer.Transform = transform;
            scene.Children.Add(layer);
            instance.AddSurfaceLayer(rotation, i == 0 ? 7.5 : -5.2);
        }
    }

    private static void AddCorona(Model3DGroup scene, SelectedStarInfo star, Color color, double radius, StarModelInstance instance)
    {
        var halo = new GeometryModel3D(
            CreateBillboardQuad(radius * 3.45),
            new EmissiveMaterial(CreateRadialGlowBrush(color, 0.55, 0.08)));
        halo.BackMaterial = halo.Material;
        halo.Transform = new TranslateTransform3D(0, 0, -0.08);
        scene.Children.Insert(0, halo);

        for (int i = 0; i < 3; i++)
        {
            byte alpha = (byte)(150 - i * 34);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            var rotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), i * 37 + star.Index % 53);
            var scale = new ScaleTransform3D(1, 1, 1);
            var transform = new Transform3DGroup();
            transform.Children.Add(new ScaleTransform3D(1.0 + i * 0.025, 1.0 - i * 0.018, 1));
            transform.Children.Add(new RotateTransform3D(rotation));
            transform.Children.Add(scale);

            var rim = new GeometryModel3D(
                CreateJaggedCoronaMesh(radius * (0.985 + i * 0.02), radius * (1.23 + i * 0.16), 176, star.Index + i * 409),
                new EmissiveMaterial(brush));
            rim.BackMaterial = rim.Material;
            rim.Transform = transform;
            scene.Children.Add(rim);
            instance.AddCoronaLayer(rotation, scale, brush, i % 2 == 0 ? 1.15 + i * 0.2 : -0.95 - i * 0.16, i * 1.7, alpha);
        }
    }

    private static void AddProminences(Model3DGroup scene, SelectedStarInfo star, Color color, double radius, StarModelInstance instance)
    {
        int count = star.ModelKey == "blue_giant" ? 7 : 9;
        for (int i = 0; i < count; i++)
        {
            double angle = Math.PI * 2.0 * Hash01(star.Index + i * 97);
            double arc = 0.22 + Hash01(star.Index * 3 + i * 41) * 0.34;
            double lift = 0.18 + Hash01(star.Index * 7 + i * 53) * 0.27;
            double width = 0.019 + Hash01(star.Index * 11 + i * 31) * 0.026;
            byte alpha = (byte)(95 + Hash01(star.Index * 13 + i * 17) * 95);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            var pulse = new ScaleTransform3D(1, 1, 1);

            var prominence = new GeometryModel3D(
                CreateProminenceMesh(radius, angle, arc, lift, width, 28),
                new EmissiveMaterial(brush));
            prominence.BackMaterial = prominence.Material;
            prominence.Transform = pulse;
            scene.Children.Add(prominence);
            instance.AddPulseLayer(pulse, brush, 1.0 + Hash01(star.Index * 19 + i * 23) * 2.1, i * 0.73, alpha);
        }

        int loopCount = star.ModelKey == "blue_giant" ? 4 : 6;
        for (int i = 0; i < loopCount; i++)
        {
            double angle = Math.PI * 2.0 * Hash01(star.Index * 5 + i * 107);
            double span = 0.32 + Hash01(star.Index * 17 + i * 43) * 0.38;
            double lift = 0.34 + Hash01(star.Index * 23 + i * 61) * 0.34;
            double width = 0.012 + Hash01(star.Index * 29 + i * 13) * 0.012;
            byte alpha = (byte)(58 + Hash01(star.Index * 31 + i * 47) * 72);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            var pulse = new ScaleTransform3D(1, 1, 1);

            var loop = new GeometryModel3D(
                CreateMagneticLoopMesh(radius, angle, span, lift, width, 30),
                new EmissiveMaterial(brush));
            loop.BackMaterial = loop.Material;
            loop.Transform = pulse;
            scene.Children.Add(loop);
            instance.AddPulseLayer(pulse, brush, 0.7 + Hash01(star.Index * 37 + i * 71) * 1.25, i * 1.21, alpha);
        }
    }

    private static Material CreateStarMaterial(ImageSource texture)
    {
        var brush = new ImageBrush(texture)
        {
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            TileMode = TileMode.Tile,
        };
        brush.Freeze();
        return new EmissiveMaterial(brush);
    }

    private static MeshGeometry3D CreateSphereMesh(double radius, int longitudeSegments, int latitudeSegments)
    {
        var mesh = new MeshGeometry3D();

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            double v = lat / (double)latitudeSegments;
            double phi = Math.PI * v;
            double y = Math.Cos(phi);
            double ring = Math.Sin(phi);

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                double u = lon / (double)longitudeSegments;
                double theta = Math.PI * 2.0 * u;
                double x = ring * Math.Cos(theta);
                double z = ring * Math.Sin(theta);

                mesh.Positions.Add(new Point3D(x * radius, y * radius, z * radius));
                mesh.Normals.Add(new Vector3D(x, y, z));
                mesh.TextureCoordinates.Add(new Point(u, v));
            }
        }

        int stride = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int a = lat * stride + lon;
                int b = a + stride;
                int c = b + 1;
                int d = a + 1;

                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(d);
                mesh.TriangleIndices.Add(d);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(c);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateBillboardQuad(double size)
    {
        var mesh = new MeshGeometry3D();
        double h = size * 0.5;

        mesh.Positions.Add(new Point3D(-h, -h, 0));
        mesh.Positions.Add(new Point3D(h, -h, 0));
        mesh.Positions.Add(new Point3D(h, h, 0));
        mesh.Positions.Add(new Point3D(-h, h, 0));
        for (int i = 0; i < 4; i++)
            mesh.Normals.Add(new Vector3D(0, 0, 1));
        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(0, 0));

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);

        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateJaggedCoronaMesh(double innerRadius, double outerRadius, int segments, int seed)
    {
        var mesh = new MeshGeometry3D();

        for (int i = 0; i <= segments; i++)
        {
            double u = i / (double)segments;
            double theta = Math.PI * 2.0 * u;
            double x = Math.Cos(theta);
            double y = Math.Sin(theta);
            double flare = 0.72 + 0.42 * Fbm(u * 8.0 + seed * 0.013, seed * 0.021, 4);

            mesh.Positions.Add(new Point3D(x * innerRadius, y * innerRadius, 0));
            mesh.Positions.Add(new Point3D(x * outerRadius * flare, y * outerRadius * flare, 0));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.TextureCoordinates.Add(new Point(u, 0));
            mesh.TextureCoordinates.Add(new Point(u, 1));
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(d);
        }

        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateProminenceMesh(double radius, double angle, double arc, double lift, double width, int segments)
    {
        var mesh = new MeshGeometry3D();
        var tangent = new Vector3D(-Math.Sin(angle), Math.Cos(angle), 0);

        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            double local = (t - 0.5) * arc;
            double curve = Math.Sin(Math.PI * t);
            double a = angle + local;
            var r = new Vector3D(Math.Cos(a), Math.Sin(a), 0);
            double distance = radius * (1.04 + lift * curve);
            var center = new Point3D(r.X * distance, r.Y * distance, 0.035 * curve);
            var side = tangent * (width * (0.45 + curve));

            mesh.Positions.Add(new Point3D(center.X - side.X, center.Y - side.Y, center.Z));
            mesh.Positions.Add(new Point3D(center.X + side.X, center.Y + side.Y, center.Z));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.TextureCoordinates.Add(new Point(t, 0));
            mesh.TextureCoordinates.Add(new Point(t, 1));
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(d);
        }

        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateMagneticLoopMesh(double radius, double angle, double span, double lift, double width, int segments)
    {
        var mesh = new MeshGeometry3D();
        var side = new Vector3D(-Math.Sin(angle), Math.Cos(angle), 0);

        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            double local = (t - 0.5) * span;
            double a = angle + local;
            double arch = Math.Sin(Math.PI * t);
            double r = radius * (1.01 + lift * arch);
            var center = new Point3D(Math.Cos(a) * r, Math.Sin(a) * r, 0.08 + 0.1 * arch);
            double taper = 0.32 + arch * 0.95;
            var offset = side * (width * taper);

            mesh.Positions.Add(new Point3D(center.X - offset.X, center.Y - offset.Y, center.Z));
            mesh.Positions.Add(new Point3D(center.X + offset.X, center.Y + offset.Y, center.Z));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            mesh.TextureCoordinates.Add(new Point(t, 0));
            mesh.TextureCoordinates.Add(new Point(t, 1));
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);
            mesh.TriangleIndices.Add(d);
        }

        mesh.Freeze();
        return mesh;
    }

    private static Brush CreateRadialGlowBrush(Color color, double innerAlpha, double outerAlpha)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * innerAlpha), color.R, color.G, color.B), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * outerAlpha), color.R, color.G, color.B), 0.58));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
        brush.Freeze();
        return brush;
    }

    private static ImageSource CreatePlasmaTexture(
        SelectedStarInfo star,
        int width,
        int height,
        double seedOffset = 0.0,
        bool transparentFilaments = false)
    {
        Color dark = DarkColorForStar(star.ModelKey);
        Color mid = ColorForStar(star.ModelKey);
        Color hot = HotColorForStar(star.ModelKey);
        Color white = star.ModelKey is "blue_giant" or "blue_white_star"
            ? Color.FromRgb(244, 250, 255)
            : Color.FromRgb(255, 250, 188);

        byte[] pixels = new byte[width * height * 4];
        double seed = star.Index * 0.017 + star.TemperatureClass * 3.7 + seedOffset;

        for (int y = 0; y < height; y++)
        {
            double v = y / (double)Math.Max(1, height - 1);
            for (int x = 0; x < width; x++)
            {
                double u = x / (double)Math.Max(1, width - 1);
                double px = u * 8.0;
                double py = v * 4.0;

                double warpA = Fbm(px * 0.9 + seed, py * 1.2 - seed, 5);
                double warpB = Fbm(px * 1.6 - seed * 0.7, py * 1.4 + seed * 0.5, 4);
                double sx = px + (warpA - 0.5) * 3.2;
                double sy = py + (warpB - 0.5) * 2.4;

                double convection = Fbm(sx * 2.2, sy * 2.8, 5);
                double fine = Fbm(sx * 7.0 + convection * 1.8, sy * 7.4 - convection * 1.6, 4);
                double filaments = 1.0 - Math.Abs(Math.Sin((sx + fine * 0.85) * 9.5) * Math.Cos((sy - convection * 0.7) * 10.0));
                filaments = Math.Pow(Math.Clamp(filaments, 0.0, 1.0), 2.1);

                double cracks = Math.Pow(1.0 - Math.Abs(fine * 2.0 - 1.0), 4.2);
                double heat = 0.22 + convection * 0.52 + filaments * 0.42 - cracks * 0.46;
                heat += Math.Pow(Fbm(sx * 15.0, sy * 15.0, 3), 5.0) * 0.32;
                heat = Math.Clamp(heat, 0.0, 1.0);

                Color c = heat < 0.48
                    ? Lerp(dark, mid, heat / 0.48)
                    : heat < 0.82
                        ? Lerp(mid, hot, (heat - 0.48) / 0.34)
                        : Lerp(hot, white, (heat - 0.82) / 0.18);

                byte alpha = 255;
                if (transparentFilaments)
                {
                    double filamentAlpha = Math.Pow(Math.Clamp(filaments * 0.9 + heat * 0.55 - cracks * 0.7, 0.0, 1.0), 1.6);
                    alpha = (byte)Math.Clamp(filamentAlpha * 135.0, 0.0, 135.0);
                }

                int i = (y * width + x) * 4;
                pixels[i + 0] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
                pixels[i + 3] = alpha;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
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

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static double Fbm(double x, double y, int octaves)
    {
        double value = 0.0;
        double amplitude = 0.5;
        double frequency = 1.0;
        double norm = 0.0;

        for (int i = 0; i < octaves; i++)
        {
            value += ValueNoise(x * frequency, y * frequency) * amplitude;
            norm += amplitude;
            frequency *= 2.03;
            amplitude *= 0.52;
        }

        return value / Math.Max(0.0001, norm);
    }

    private static double ValueNoise(double x, double y)
    {
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double tx = Smooth(x - xi);
        double ty = Smooth(y - yi);

        double a = Hash01(xi * 374761393 + yi * 668265263);
        double b = Hash01((xi + 1) * 374761393 + yi * 668265263);
        double c = Hash01(xi * 374761393 + (yi + 1) * 668265263);
        double d = Hash01((xi + 1) * 374761393 + (yi + 1) * 668265263);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
    }

    private static double Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x / (double)uint.MaxValue;
        }
    }

    private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
