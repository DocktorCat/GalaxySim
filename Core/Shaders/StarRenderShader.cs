using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct StarRenderShader(
    ReadWriteBuffer<Float4> output,
    int width,
    int height,
    float time,
    float yaw,
    float pitch,
    float zoom,
    Float3 darkColor,
    Float3 midColor,
    Float3 hotColor,
    Float3 whiteColor,
    float seed,
    float isBlueStar) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        if (x >= width || y >= height) return;

        Float2 uv = new Float2((x + 0.5f) / width, (y + 0.5f) / height) * 2f - 1f;
        uv.X *= (float)width / height;
        uv /= Hlsl.Max(zoom, 0.2f);

        Float3 color = Background(uv);
        float radius = 0.78f;
        float r2 = Hlsl.Dot(uv, uv);

        Float3 corona = Corona(uv, radius);
        Float3 loops = MagneticLoops(uv, radius);
        color += corona + loops;

        if (r2 <= radius * radius)
        {
            float z = Hlsl.Sqrt(Hlsl.Max(radius * radius - r2, 0f));
            Float3 n = Hlsl.Normalize(new Float3(uv.X, -uv.Y, z));
            Float3 sampleN = RotateX(RotateY(n, yaw), pitch);

            Float3 plasma = Photosphere(sampleN);
            float view = Hlsl.Saturate(n.Z);
            float limb = Hlsl.Pow(1f - view, 2.25f);
            float rim = Hlsl.Pow(1f - view, 8f);

            float cells = Fbm(sampleN.XY * 6.4f + new Float2(time * 0.035f, -time * 0.028f), 5);
            float sparks = Hlsl.Pow(Fbm(sampleN.YZ * 24f + seed + time * 0.06f, 3), 7f);
            plasma *= 0.82f + cells * 0.36f;
            plasma += whiteColor * sparks * 1.8f;
            plasma += hotColor * rim * 2.8f;
            plasma += midColor * limb * 0.75f;

            float edge = Hlsl.Saturate((radius - Hlsl.Sqrt(r2)) / 0.018f);
            color = Hlsl.Lerp(color, plasma, edge);
            color += hotColor * rim * 0.9f;
        }

        output[y * width + x] = new Float4(Hlsl.Max(color, Float3.Zero), 1f);
    }

    private Float3 Photosphere(Float3 n)
    {
        Float2 p = SphericalUv(n);
        Float2 flow1 = new Float2(time * 0.05f, -time * 0.022f);
        Float2 flow2 = new Float2(-time * 0.035f, time * 0.041f);

        float warpA = Fbm(p * 3.6f + flow1 + seed, 5);
        float warpB = Fbm(p * 5.2f + flow2 - seed * 0.7f, 4);
        Float2 q = p * 9.0f + new Float2(warpA - 0.5f, warpB - 0.5f) * 4.5f;

        float convection = Fbm(q * 0.85f + time * 0.025f, 5);
        float fine = Fbm(q * 3.8f + convection * 2.2f - time * 0.05f, 4);
        float vein = 1f - Hlsl.Abs(Hlsl.Sin(q.X * 2.4f + fine * 4.5f) * Hlsl.Cos(q.Y * 2.8f - convection * 3.2f));
        vein = Hlsl.Pow(Hlsl.Saturate(vein), 2.4f);
        float darkCrack = Hlsl.Pow(1f - Hlsl.Abs(fine * 2f - 1f), 4.6f);

        float heat = 0.18f + convection * 0.48f + vein * 0.56f - darkCrack * 0.42f;
        heat += Hlsl.Pow(Fbm(q * 8.5f + seed, 3), 6f) * 0.45f;
        heat = Hlsl.Saturate(heat);

        Float3 c = heat < 0.46f
            ? Hlsl.Lerp(darkColor, midColor, heat / 0.46f)
            : heat < 0.82f
                ? Hlsl.Lerp(midColor, hotColor, (heat - 0.46f) / 0.36f)
                : Hlsl.Lerp(hotColor, whiteColor, (heat - 0.82f) / 0.18f);

        return c * (1.4f + heat * 2.2f);
    }

    private Float3 Corona(Float2 uv, float radius)
    {
        float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
        float angle = Hlsl.Atan2(uv.Y, uv.X) + yaw + pitch * 0.35f;
        float outside = Hlsl.Saturate((r - radius) / 0.64f);
        float near = Hlsl.Saturate(1f - outside);

        float ringX = Hlsl.Cos(angle);
        float ringY = Hlsl.Sin(angle);
        float angular = Fbm(new Float2(ringX * 3.2f + ringY * 1.7f + time * 0.08f + seed,
                                       ringY * 3.0f - ringX * 1.9f + r * 3.8f - time * 0.12f), 5);
        float rays = Hlsl.Pow(Hlsl.Saturate(angular), 2.0f);
        float halo = Hlsl.Exp(-Hlsl.Max(r - radius, 0f) * 3.1f);
        float rim = Hlsl.Exp(-Hlsl.Abs(r - radius) * 34f);
        float longRays = Hlsl.Pow(Hlsl.Saturate(1f - outside), 2.2f) * rays;

        Float3 baseColor = Hlsl.Lerp(midColor, hotColor, 0.65f + 0.35f * rays);
        float coronaBoost = 1f + isBlueStar * 0.22f;
        Float3 c = baseColor * halo * (0.45f + rays * 0.9f) * near;
        c += hotColor * rim * 1.8f;
        c += baseColor * longRays * 0.34f;
        return c * coronaBoost;
    }

    private Float3 MagneticLoops(Float2 uv, float radius)
    {
        Float3 sum = Float3.Zero;
        float modelAngle = yaw + pitch * 0.35f;
        for (int i = 0; i < 8; i++)
        {
            float h = Hash(seed + i * 17.31f);
            float lifeSpeed = 0.085f + Hash(seed + i * 12.41f) * 0.055f;
            float phase = Hlsl.Frac(time * lifeSpeed + Hash(seed + i * 33.7f));
            float grow = Smooth01(Hlsl.Saturate(phase / 0.30f));
            float hold = 1f - Smooth01(Hlsl.Saturate((phase - 0.56f) / 0.26f));
            float retract = 1f - Smooth01(Hlsl.Saturate((phase - 0.76f) / 0.24f));
            float life = grow * hold;
            float fragment = Smooth01(Hlsl.Saturate((phase - 0.44f) / 0.30f));

            float center = h * 6.283185f + modelAngle + time * (0.018f + i * 0.0025f);
            float span = (0.24f + Hash(seed + i * 9.13f) * 0.48f) * (0.42f + 0.58f * grow);
            float lift = (0.20f + Hash(seed + i * 5.77f) * 0.36f) * (0.18f + 0.82f * life) * (0.45f + 0.55f * retract);

            float a = Hlsl.Atan2(uv.Y, uv.X);
            float dAng = WrapAngle(a - center);
            float t = Hlsl.Saturate(dAng / span + 0.5f);
            float arch = Hlsl.Sin(t * 3.1415926f);
            float targetR = radius * (1.005f + arch * lift);
            float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
            float width = (0.010f + arch * (0.016f + Hash(seed + i * 2.79f) * 0.018f)) * (0.55f + 0.45f * life);
            float dist = Hlsl.Abs(r - targetR) + Hlsl.Abs(dAng) * 0.010f;
            float breakupNoise = Fbm(new Float2(t * 11.0f + seed + i, time * 0.85f + i * 1.7f), 4);
            float tear = Hlsl.Lerp(1f, Hlsl.Saturate((breakupNoise - 0.28f) * 1.65f), fragment);
            float clumps = 0.55f + 0.45f * Hlsl.Pow(Hlsl.Saturate(breakupNoise), 2.2f);
            float mask = Hlsl.Exp(-(dist * dist) / (width * width + 1e-5f)) * Hlsl.Saturate(arch * 1.6f) * clumps * tear * life;
            float fade = Hlsl.Saturate(1f - Hlsl.Abs(dAng) / span);
            float outside = Hlsl.Saturate((r - radius * 0.965f) / (radius * 0.09f));
            float footGlow = Hlsl.Min(t, 1f - t);
            float foot = Hlsl.Exp(-(footGlow * footGlow) / 0.0045f) *
                         Hlsl.Exp(-((r - radius) * (r - radius)) / 0.0012f) *
                         (0.35f + 0.65f * (1f - grow + fragment)) * retract;
            Float3 loopColor = Hlsl.Lerp(hotColor, whiteColor, 0.20f + 0.45f * Hash(seed + i * 4.33f));
            float spray = Hlsl.Exp(-(Hlsl.Abs(r - radius * (1.04f + lift * arch * 0.72f)) * Hlsl.Abs(r - radius * (1.04f + lift * arch * 0.72f))) / 0.006f) *
                          Hlsl.Saturate(fragment * (breakupNoise - 0.35f) * 1.7f) * fade * outside;
            sum += loopColor * (mask * fade * outside * (0.35f + Hash(seed + i * 3.21f) * 0.55f) + foot * 0.46f + spray * 0.22f);
        }

        return sum;
    }

    private Float3 Background(Float2 uv)
    {
        float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv));
        float vignette = Hlsl.Saturate(1.25f - r * 0.32f);
        Float2 p = uv * 42f + seed;
        Float2 cell = Hlsl.Floor(p);
        Float2 f = p - cell;
        float rnd = Hash2(cell);
        Float2 starPos = new Float2(Hash2(cell + 17.13f), Hash2(cell + 41.71f));
        Float2 d = f - starPos;
        float stars = rnd > 0.965f ? Hlsl.Exp(-Hlsl.Dot(d, d) / 0.0035f) * (0.10f + rnd * 0.26f) : 0f;
        Float2 p2 = uv * 87f + seed * 1.73f;
        Float2 cell2 = Hlsl.Floor(p2);
        Float2 f2 = p2 - cell2;
        float rnd2 = Hash2(cell2 + 13.7f);
        Float2 starPos2 = new Float2(Hash2(cell2 + 5.31f), Hash2(cell2 + 29.19f));
        Float2 d2 = f2 - starPos2;
        stars += rnd2 > 0.988f ? Hlsl.Exp(-Hlsl.Dot(d2, d2) / 0.0020f) * 0.24f : 0f;
        Float3 bg = Hlsl.Lerp(new Float3(0.006f, 0.008f, 0.014f), new Float3(0.035f, 0.030f, 0.038f), vignette);
        bg += new Float3(stars, stars * 0.92f, stars * 1.2f);
        return bg;
    }

    private static Float2 SphericalUv(Float3 n)
    {
        float u = Hlsl.Atan2(n.Z, n.X) / 6.283185f + 0.5f;
        float v = Hlsl.Asin(Hlsl.Clamp(n.Y, -1f, 1f)) / 3.1415926f + 0.5f;
        return new Float2(u, v);
    }

    private static Float3 RotateY(Float3 p, float a)
    {
        float s = Hlsl.Sin(a);
        float c = Hlsl.Cos(a);
        return new Float3(p.X * c + p.Z * s, p.Y, -p.X * s + p.Z * c);
    }

    private static Float3 RotateX(Float3 p, float a)
    {
        float s = Hlsl.Sin(a);
        float c = Hlsl.Cos(a);
        return new Float3(p.X, p.Y * c - p.Z * s, p.Y * s + p.Z * c);
    }

    private static float Fbm(Float2 p, int octaves)
    {
        float value = 0f;
        float amp = 0.5f;
        float freq = 1f;
        float norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            value += Noise(p * freq) * amp;
            norm += amp;
            amp *= 0.52f;
            freq *= 2.03f;
        }
        return value / Hlsl.Max(norm, 1e-4f);
    }

    private static float Noise(Float2 p)
    {
        Float2 i = Hlsl.Floor(p);
        Float2 f = p - i;
        Float2 u = f * f * (3f - 2f * f);
        float a = Hash2(i);
        float b = Hash2(i + new Float2(1f, 0f));
        float c = Hash2(i + new Float2(0f, 1f));
        float d = Hash2(i + new Float2(1f, 1f));
        return Hlsl.Lerp(Hlsl.Lerp(a, b, u.X), Hlsl.Lerp(c, d, u.X), u.Y);
    }

    private static float Hash2(Float2 p)
    {
        return Hlsl.Frac(Hlsl.Sin(Hlsl.Dot(p, new Float2(127.1f, 311.7f))) * 43758.5453f);
    }

    private static float Hash(float x)
    {
        return Hlsl.Frac(Hlsl.Sin(x * 91.3458f) * 47453.5453f);
    }

    private static float WrapAngle(float a)
    {
        return Hlsl.Atan2(Hlsl.Sin(a), Hlsl.Cos(a));
    }

    private static float Smooth01(float x)
    {
        x = Hlsl.Saturate(x);
        return x * x * (3f - 2f * x);
    }
}
