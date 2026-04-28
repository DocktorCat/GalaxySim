using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SolarSystemRenderShader(
    ReadWriteTexture2D<Bgra32, Float4> output,
    ReadWriteBuffer<Float4> planetData,
    ReadWriteBuffer<Float4> planetExtra,
    ReadWriteBuffer<Float4> planetRings,
    ReadWriteBuffer<Float4> beltData,
    ReadWriteBuffer<Float4> moonData,
    ReadWriteBuffer<Float4> moonExtra,
    int width,
    int height,
    float orbitTime,
    float time,
    float zoom,
    float seed,
    int planetCount,
    int beltCount,
    int moonCount,
    Float3 starDarkColor,
    Float3 starColor,
    Float3 starHotColor,
    Float3 starWhiteColor,
    float isBlueStar,
    float habitableZoneAu,
    int viewMode,
    int realismMode,
    float yaw,
    float pitch,
    int selectedIndex,
    int selectedMoonIndex,
    int focusIndex,
    int qualityLevel,
    int shadowQuality,
    int debrisQuality) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        if (x >= width || y >= height) return;

        Float2 uv = new Float2((x + 0.5f) / width, (y + 0.5f) / height) * 2f - 1f;
        uv.X *= (float)width / height;
        uv /= Hlsl.Max(zoom, 0.06f);

        Float3 color = Background(uv);
        float hz = HabitableRenderRadius();
        color += HabitableZone(uv, hz);

        if (viewMode != 0)
        {
            color = RenderRaycast3D(color, uv);
            Float3 mapped3D = Aces(color * 1.08f);
            mapped3D = Hlsl.Pow(Hlsl.Max(mapped3D, Float3.Zero), 1f / 2.2f);
            output[x, y] = new Float4(mapped3D, 1f);
            return;
        }

        color += AsteroidBelts2D(uv);

        for (int i = 0; i < 9; i++)
        {
            if (i >= planetCount) continue;

            Float4 data = planetData[i];
            Float4 extra = planetExtra[i];
            Float4 ring = planetRings[i];
            float orbit = OrbitRadius(data.X);
            float radius = VisualRadius(data.Z);
            color += OrbitLine(uv, orbit, data.Y);
            Float3 projected = ProjectPlanet(data, extra);
            color = DrawPlanetMarker(color, uv, projected.XY, radius * projected.Z, (int)data.W, extra.Z, i == selectedIndex);
            color = DrawPlanetRings2D(color, uv, projected.XY, radius * projected.Z, ring, extra.Z);
            color = DrawPlanet(color, uv, projected.XY, radius * projected.Z, (int)data.W, extra.Z, extra.W, i == selectedIndex);
        }

        for (int i = 0; i < 32; i++)
        {
            if (i >= moonCount) continue;

            Float4 data = moonData[i];
            Float4 extra = moonExtra[i];
            Float4 parentData = planetData[(int)data.X];
            Float4 parentExtra = planetExtra[(int)data.X];
            Float3 projected = ProjectMoon(data, extra, parentData, parentExtra);
            color = DrawMoon(color, uv, projected.XY, VisualRadius(data.Z) * projected.Z, extra.Z, i == selectedMoonIndex);
        }

        color += DrawStar(uv);
        Float3 mapped = Aces(color * 1.08f);
        mapped = Hlsl.Pow(Hlsl.Max(mapped, Float3.Zero), 1f / 2.2f);
        output[x, y] = new Float4(mapped, 1f);
    }

    private Float3 RenderRaycast3D(Float3 color, Float2 uv)
    {
        Float3 focus = FocusPosition();
        Float3 camPos = focus + CameraOffset();
        Float3 forward = Hlsl.Normalize(focus - camPos);
        Float3 right = Hlsl.Normalize(Cross(new Float3(0f, 1f, 0f), forward));
        Float3 up = Hlsl.Normalize(Cross(forward, right));
        Float3 rayDir = Hlsl.Normalize(forward + right * uv.X * 0.72f - up * uv.Y * 0.72f);

        color += OrbitsRaycast3D(camPos, rayDir);
        color += DrawStarGlowRaycast(camPos, rayDir, right, up);

        float bestT = 1e20f;
        Float3 bestColor = Float3.Zero;
        bool hasHit = false;
        float selectionGlow = 0f;

        float starRadius = 0.105f;
        float starT = RaySphere(camPos, rayDir, Float3.Zero, starRadius);
        if (starT > 0f && starT < bestT)
        {
            Float3 hit = camPos + rayDir * starT;
            Float3 n = Hlsl.Normalize(hit / starRadius);
            Float3 sampleN = RotateY3(n, time * 0.18f + seed);
            float rim = Hlsl.Pow(1f - Hlsl.Saturate(Hlsl.Dot(n, -rayDir)), 3.6f);
            bestColor = Photosphere3D(sampleN) + starColor * rim * 2.2f;
            bestT = starT;
            hasHit = true;
        }

        for (int i = 0; i < 9; i++)
        {
            if (i >= planetCount) continue;

            Float4 data = planetData[i];
            Float4 extra = planetExtra[i];
            Float4 ring = planetRings[i];
            Float3 center = PlanetWorldPosition(data, extra);
            float radius = VisualRadius(data.Z) * 1.35f;
            Float4 ringHit = PlanetRingHit(rayDir, camPos, center, radius, ring, extra.Z);
            if (ringHit.X > 0f && ringHit.X < bestT)
            {
                bestColor = color + ringHit.YZW;
                bestT = ringHit.X;
                hasHit = true;
            }

            float t = RaySphere(camPos, rayDir, center, radius);
            float closest = RayPointDistance(camPos, rayDir, center);
            float markerRadius = realismMode != 0 ? Hlsl.Max(radius * 4.2f, 0.030f) : radius;
            float markerGlow = Hlsl.Exp(-(closest * closest) / (markerRadius * markerRadius * 1.8f + 1e-5f));
            Float3 markerColor = PlanetAlbedo(Hlsl.Normalize(center + new Float3(0.2f, 0.6f, 0.1f)), (int)data.W, extra.Z, extra.W);
            color += markerColor * markerGlow * (realismMode != 0 ? 0.13f : 0.0f);
            if (i == selectedIndex)
                selectionGlow += Hlsl.Exp(-(closest * closest) / (markerRadius * markerRadius * 3.4f + 1e-5f));

            if (t <= 0f || t >= bestT)
                continue;

            Float3 hit = camPos + rayDir * t;
            Float3 n = Hlsl.Normalize((hit - center) / radius);
            Float3 albedo = PlanetAlbedo(n, (int)data.W, extra.Z, extra.W);
            Float3 lightDir = Hlsl.Normalize(-center);
            float diffuse = Hlsl.Saturate(Hlsl.Dot(n, lightDir));
            float irradiance = Hlsl.Saturate(0.70f + 0.32f / (Hlsl.Sqrt(Hlsl.Dot(center, center)) + 0.36f));
            float moonShadow = shadowQuality > 0 ? MoonTransitShadow(hit, lightDir, i) : 0f;
            float ringShadow = shadowQuality > 0 ? RingSurfaceShadow(hit, center, lightDir, radius, ring) : 0f;
            float totalShadow = Hlsl.Saturate(moonShadow + ringShadow);
            float softDiffuse = diffuse * (1f - totalShadow * 0.78f);
            float viewRim = Hlsl.Pow(1f - Hlsl.Saturate(Hlsl.Dot(n, -rayDir)), 2.6f);
            float forwardScatter = Hlsl.Pow(Hlsl.Saturate(Hlsl.Dot(Hlsl.Normalize(lightDir - rayDir), n)), 5.0f);
            float night = Hlsl.Saturate(0.040f + extra.W * 0.18f);
            Float3 planetColor = albedo * (softDiffuse * irradiance + night) + albedo * viewRim * 0.20f;
            planetColor += albedo * Hlsl.Pow(Hlsl.Saturate(diffuse), 0.45f) * 0.055f;
            planetColor += new Float3(1.0f, 0.86f, 0.58f) * forwardScatter * (0.06f + extra.W * 0.05f);
            planetColor += HotGasEmission(n, extra.Z, extra.W) * (0.25f + 0.75f * (1f - softDiffuse));
            planetColor *= 1f - ringShadow * 0.18f;
            if ((int)data.W == 1)
            {
                float cloudGlow = SeamlessNoise(Hlsl.Normalize(n + new Float3(Hlsl.Sin(time * 0.035f), Hlsl.Cos(time * 0.028f), Hlsl.Sin(time * 0.041f)) * 0.08f), 34f, extra.Z + 6.3f, qualityLevel == 2 ? 4 : 3);
                planetColor += new Float3(0.36f, 0.62f, 0.98f) * viewRim * (0.42f + cloudGlow * 0.30f);
            }
            if ((int)data.W == 3)
                planetColor += new Float3(0.30f, 0.72f, 0.95f) * viewRim * 0.20f;
            bestColor = planetColor;
            bestT = t;
            hasHit = true;
        }

        for (int i = 0; i < 32; i++)
        {
            if (i >= moonCount) continue;

            Float4 data = moonData[i];
            Float4 extra = moonExtra[i];
            Float4 parentData = planetData[(int)data.X];
            Float4 parentExtra = planetExtra[(int)data.X];
            Float3 center = MoonWorldPosition(data, extra, parentData, parentExtra);
            float radius = VisualRadius(data.Z);
            float t = RaySphere(camPos, rayDir, center, radius);
            float closest = RayPointDistance(camPos, rayDir, center);
            float markerRadius = realismMode != 0 ? Hlsl.Max(radius * 4.5f, 0.018f) : radius;
            color += MoonAlbedo(Hlsl.Normalize(center + new Float3(0.31f, 0.19f, 0.27f)), extra.Z, data.W) *
                Hlsl.Exp(-(closest * closest) / (markerRadius * markerRadius * 1.7f + 1e-5f)) *
                (realismMode != 0 ? 0.10f : 0.0f);
            if (i == selectedMoonIndex)
                selectionGlow += Hlsl.Exp(-(closest * closest) / (markerRadius * markerRadius * 4.5f + 1e-5f));
            if (t <= 0f || t >= bestT)
                continue;

            Float3 hit = camPos + rayDir * t;
            Float3 n = Hlsl.Normalize((hit - center) / radius);
            Float3 parentCenter = PlanetWorldPosition(parentData, parentExtra);
            Float3 lightDir = Hlsl.Normalize(-center);
            float diffuse = Hlsl.Saturate(Hlsl.Dot(n, lightDir));
            float rim = Hlsl.Pow(1f - Hlsl.Saturate(Hlsl.Dot(n, -rayDir)), 2.8f);
            float eclipse = shadowQuality > 0 ? PlanetEclipseShadow(hit, lightDir, parentCenter, VisualRadius(parentData.Z) * 1.35f) : 0f;
            float softDiffuse = diffuse * (1f - eclipse * 0.86f);
            Float3 moonColor = MoonAlbedo(n, extra.Z, data.W) * (softDiffuse * 0.86f + 0.08f) + new Float3(0.65f, 0.72f, 0.82f) * rim * 0.08f;
            moonColor *= 1f - eclipse * 0.20f;
            bestColor = moonColor;
            bestT = t;
            hasHit = true;
        }

        color += new Float3(0.96f, 0.86f, 0.58f) * selectionGlow * 0.15f;
        if (hasHit)
            color = Hlsl.Lerp(color, bestColor, 0.98f);

        return color;
    }

    private Float3 FocusPosition()
    {
        if (focusIndex >= 0 && focusIndex < planetCount)
            return PlanetWorldPosition(planetData[focusIndex], planetExtra[focusIndex]);

        int moonIndex = focusIndex - 100;
        if (moonIndex >= 0 && moonIndex < moonCount)
        {
            Float4 moon = moonData[moonIndex];
            return MoonWorldPosition(moon, moonExtra[moonIndex], planetData[(int)moon.X], planetExtra[(int)moon.X]);
        }

        return Float3.Zero;
    }

    private Float3 CameraOffset()
    {
        float close = Hlsl.Saturate((zoom - 1f) / 11f);
        float overview = zoom < 1f ? 1f / Hlsl.Sqrt(Hlsl.Max(zoom, 0.07f)) : 1f;
        float baseDist = realismMode != 0 ? 13.20f : 3.20f;
        float closeDist = realismMode != 0 ? 12.50f : 2.82f;
        float dist = baseDist * overview - close * closeDist;
        float cy = Hlsl.Cos(yaw);
        float sy = Hlsl.Sin(yaw);
        float cp = Hlsl.Cos(pitch);
        float sp = Hlsl.Sin(pitch);
        return new Float3(sy * cp * dist, sp * dist, cy * cp * dist);
    }

    private Float3 PlanetWorldPosition(Float4 data, Float4 extra)
    {
        float orbit = OrbitRadius(data.X);
        float phase = extra.X + orbitTime * extra.Y;
        return new Float3(
            Hlsl.Cos(phase) * orbit,
            0f,
            Hlsl.Sin(phase) * orbit * data.Y);
    }

    private Float3 MoonWorldPosition(Float4 moon, Float4 extra, Float4 parentData, Float4 parentExtra)
    {
        Float3 parent = PlanetWorldPosition(parentData, parentExtra);
        float phase = extra.X + orbitTime * extra.Y;
        float c = Hlsl.Cos(phase);
        float s = Hlsl.Sin(phase);
        float tilt = extra.W;
        float ct = Hlsl.Cos(tilt);
        float st = Hlsl.Sin(tilt);
        Float3 offset = new Float3(c * moon.Y, s * st * moon.Y, s * ct * moon.Y);
        return parent + offset;
    }

    private Float3 OrbitsRaycast3D(Float3 rayOrigin, Float3 rayDir)
    {
        Float3 sum = Float3.Zero;
        if (Hlsl.Abs(rayDir.Y) < 1e-4f)
            return sum;

        float t = -rayOrigin.Y / rayDir.Y;
        if (t <= 0f)
            return sum;

        Float3 p = rayOrigin + rayDir * t;
        float hz = HabitableRenderRadius();
        float hr = Hlsl.Sqrt(p.X * p.X + p.Z * p.Z + 1e-6f);
        float hBand = Hlsl.Saturate((hr - hz * 0.82f) / 0.030f) * Hlsl.Saturate((hz * 1.18f - hr) / 0.030f);
        sum += new Float3(0.12f, 0.40f, 0.22f) * hBand * 0.10f;
        sum += AsteroidBeltsAtPlane(p);
        sum += AsteroidBeltVolume(rayOrigin, rayDir);

        for (int i = 0; i < 9; i++)
        {
            if (i >= planetCount) continue;
            Float4 data = planetData[i];
            float e = Hlsl.Max(data.Y, 0.1f);
            float orbit = OrbitRadius(data.X);
            float d = Hlsl.Abs(Hlsl.Sqrt((p.X * p.X) / (orbit * orbit) + (p.Z * p.Z) / (orbit * orbit * e * e) + 1e-6f) - 1f);
            float line = Hlsl.Exp(-(d * d) / 0.000026f);
            sum += new Float3(0.26f, 0.34f, 0.42f) * line * 0.20f;
        }

        return sum;
    }

    private Float3 DrawStarGlowRaycast(Float3 rayOrigin, Float3 rayDir, Float3 cameraRight, Float3 cameraUp)
    {
        float d = RayPointDistance(rayOrigin, rayDir, Float3.Zero);
        Float3 closest = rayOrigin + rayDir * Hlsl.Max(Hlsl.Dot(-rayOrigin, rayDir), 0f);
        float angle = Hlsl.Atan2(closest.Z, closest.X) + yaw + pitch * 0.35f;
        float rays = Fbm(new Float2(Hlsl.Cos(angle) * 3.2f + time * 0.08f + seed, Hlsl.Sin(angle) * 3.0f - time * 0.12f), 5);
        float glow = Hlsl.Exp(-d * 10.5f) * (1.10f + rays * 0.70f) + Hlsl.Exp(-d * 31.0f) * 0.95f;
        float rim = Hlsl.Exp(-Hlsl.Abs(d - 0.105f) * 50f) * 1.4f;
        return Hlsl.Lerp(starColor, starHotColor, 0.55f + rays * 0.35f) * glow * (1f + isBlueStar * 0.18f) +
            starHotColor * rim +
            MagneticLoopsRaycast(closest, cameraRight, cameraUp, 0.105f);
    }

    private static float RaySphere(Float3 rayOrigin, Float3 rayDir, Float3 center, float radius)
    {
        Float3 oc = rayOrigin - center;
        float b = Hlsl.Dot(oc, rayDir);
        float c = Hlsl.Dot(oc, oc) - radius * radius;
        float h = b * b - c;
        if (h < 0f)
            return -1f;
        h = Hlsl.Sqrt(h);
        float t = -b - h;
        if (t > 0.001f)
            return t;
        t = -b + h;
        return t > 0.001f ? t : -1f;
    }

    private static float RayPointDistance(Float3 rayOrigin, Float3 rayDir, Float3 point)
    {
        Float3 v = point - rayOrigin;
        float t = Hlsl.Max(Hlsl.Dot(v, rayDir), 0f);
        Float3 closest = rayOrigin + rayDir * t;
        Float3 d = point - closest;
        return Hlsl.Sqrt(Hlsl.Dot(d, d) + 1e-6f);
    }

    private Float3 Photosphere3D(Float3 n)
    {
        Float3 flowN = Hlsl.Normalize(n + new Float3(Hlsl.Sin(time * 0.050f + seed), Hlsl.Cos(time * 0.041f), Hlsl.Sin(time * 0.035f - seed)) * 0.085f);
        float warpA = SeamlessNoise(flowN, 4.2f, seed, 5);
        float warpB = SeamlessNoise(flowN, 7.0f, seed * 1.37f - time * 0.04f, 4);
        Float3 q = Hlsl.Normalize(flowN + new Float3(warpA - 0.5f, warpB - 0.5f, warpA - warpB) * 0.62f);
        float convection = SeamlessNoise(q, 8.0f, seed + time * 0.025f, 5);
        float fine = SeamlessNoise(q, 30.0f + qualityLevel * 12f, seed * 0.7f - time * 0.05f, qualityLevel == 0 ? 4 : 5);
        float cellular = Hlsl.Sin((q.X + fine * 0.25f) * 13.0f) * Hlsl.Cos((q.Y - convection * 0.30f) * 15.0f) * Hlsl.Sin((q.Z + warpB * 0.20f) * 11.0f);
        float vein = 1f - Hlsl.Abs(cellular);
        vein = Hlsl.Pow(Hlsl.Saturate(vein), 2.4f);
        float darkCrack = Hlsl.Pow(1f - Hlsl.Abs(fine * 2f - 1f), 4.6f);
        float heat = 0.18f + convection * 0.48f + vein * 0.56f - darkCrack * 0.42f;
        heat += Hlsl.Pow(SeamlessNoise(q, 72f, seed + 9.1f, qualityLevel == 2 ? 4 : 3), 6f) * 0.45f;
        heat = Hlsl.Saturate(heat);
        Float3 c = heat < 0.46f
            ? Hlsl.Lerp(starDarkColor, starColor, heat / 0.46f)
            : heat < 0.82f
                ? Hlsl.Lerp(starColor, starHotColor, (heat - 0.46f) / 0.36f)
                : Hlsl.Lerp(starHotColor, starWhiteColor, (heat - 0.82f) / 0.18f);
        return c * (1.55f + heat * 2.65f);
    }

    private static Float3 RotateY3(Float3 p, float a)
    {
        float s = Hlsl.Sin(a);
        float c = Hlsl.Cos(a);
        return new Float3(p.X * c + p.Z * s, p.Y, -p.X * s + p.Z * c);
    }

    private static Float2 Rotate2(Float2 p, float a)
    {
        float s = Hlsl.Sin(a);
        float c = Hlsl.Cos(a);
        return new Float2(p.X * c - p.Y * s, p.X * s + p.Y * c);
    }

    private static Float3 Cross(Float3 a, Float3 b)
    {
        return new Float3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
    }

    private Float3 DrawStar(Float2 uv)
    {
        float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
        float radius = 0.085f;
        Float3 color = StarCorona2D(uv, radius);
        if (r <= radius)
        {
            float z = Hlsl.Sqrt(Hlsl.Max(radius * radius - r * r, 0f));
            Float3 n = Hlsl.Normalize(new Float3(uv.X, -uv.Y, z));
            Float3 sampleN = RotateY3(n, time * 0.18f + seed);
            float view = Hlsl.Saturate(n.Z);
            float limb = Hlsl.Pow(1f - view, 2.25f);
            float rim = Hlsl.Pow(1f - view, 8f);
            Float3 plasma = Photosphere3D(sampleN);
            float sparks = Hlsl.Pow(Fbm(sampleN.YZ * 24f + seed + time * 0.06f, 3), 7f);
            plasma += starWhiteColor * sparks * 1.5f;
            plasma += starHotColor * rim * 2.3f;
            plasma += starColor * limb * 0.65f;
            float edge = Hlsl.Saturate((radius - r) / 0.006f);
            color = Hlsl.Lerp(color, plasma, edge);
            color += starHotColor * rim * 0.75f;
        }

        return color;
    }

    private Float3 StarCorona2D(Float2 uv, float radius)
    {
        float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
        float angle = Hlsl.Atan2(uv.Y, uv.X) + time * 0.04f + seed;
        float outside = Hlsl.Saturate((r - radius) / 0.12f);
        float near = Hlsl.Saturate(1f - outside);
        float angular = Fbm(new Float2(Hlsl.Cos(angle) * 3.2f + time * 0.08f, Hlsl.Sin(angle) * 3.0f - time * 0.12f + r * 3.8f), 5);
        float halo = Hlsl.Exp(-Hlsl.Max(r - radius, 0f) * 14.0f);
        float rim = Hlsl.Exp(-Hlsl.Abs(r - radius) * 65f);
        Float3 baseColor = Hlsl.Lerp(starColor, starHotColor, 0.65f + 0.35f * angular);
        Float3 c = baseColor * halo * (0.42f + angular * 0.95f) * near;
        c += starHotColor * rim * 1.55f;
        c += MagneticLoops2D(uv, radius);
        return c * (1f + isBlueStar * 0.16f);
    }

    private Float3 MagneticLoops2D(Float2 uv, float radius)
    {
        Float3 sum = Float3.Zero;
        for (int i = 0; i < 5; i++)
        {
            float h = Hash(seed + i * 17.31f);
            float phase = Hlsl.Frac(time * (0.075f + Hash(seed + i * 12.41f) * 0.045f) + Hash(seed + i * 33.7f));
            float grow = Smooth01(Hlsl.Saturate(phase / 0.30f));
            float hold = 1f - Smooth01(Hlsl.Saturate((phase - 0.58f) / 0.25f));
            float life = grow * hold;
            float center = h * 6.283185f + time * (0.018f + i * 0.0025f);
            float span = (0.28f + Hash(seed + i * 9.13f) * 0.42f) * (0.45f + 0.55f * grow);
            float lift = (0.18f + Hash(seed + i * 5.77f) * 0.30f) * life;
            float a = Hlsl.Atan2(uv.Y, uv.X);
            float dAng = WrapAngle(a - center);
            float t = Hlsl.Saturate(dAng / span + 0.5f);
            float arch = Hlsl.Sin(t * 3.1415926f);
            float targetR = radius * (1.01f + arch * lift);
            float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
            float width = (0.0025f + arch * 0.0045f) * (0.55f + 0.45f * life);
            float dist = Hlsl.Abs(r - targetR) + Hlsl.Abs(dAng) * 0.0026f;
            float breakup = Fbm(new Float2(t * 11.0f + seed + i, time * 0.85f + i * 1.7f), 4);
            float mask = Hlsl.Exp(-(dist * dist) / (width * width + 1e-5f)) * Hlsl.Saturate(arch * 1.6f) * (0.50f + 0.50f * breakup) * life;
            float outside = Hlsl.Saturate((r - radius * 0.965f) / (radius * 0.09f));
            sum += Hlsl.Lerp(starHotColor, starWhiteColor, 0.30f + 0.35f * h) * mask * outside * 0.46f;
        }

        return sum;
    }

    private Float3 MagneticLoopsRaycast(Float3 closest, Float3 cameraRight, Float3 cameraUp, float radius)
    {
        Float2 uv = new Float2(Hlsl.Dot(closest, cameraRight), Hlsl.Dot(closest, cameraUp));
        float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
        if (r < radius * 0.92f || r > radius * 1.62f)
            return Float3.Zero;

        Float3 sum = Float3.Zero;
        for (int i = 0; i < 5; i++)
        {
            float h = Hash(seed + i * 17.31f);
            float phase = Hlsl.Frac(time * (0.075f + Hash(seed + i * 12.41f) * 0.045f) + Hash(seed + i * 33.7f));
            float grow = Smooth01(Hlsl.Saturate(phase / 0.30f));
            float hold = 1f - Smooth01(Hlsl.Saturate((phase - 0.58f) / 0.25f));
            float life = grow * hold;
            float center = h * 6.283185f + time * (0.018f + i * 0.0025f);
            float span = (0.28f + Hash(seed + i * 9.13f) * 0.42f) * (0.45f + 0.55f * grow);
            float lift = (0.18f + Hash(seed + i * 5.77f) * 0.30f) * life;
            float a = Hlsl.Atan2(uv.Y, uv.X);
            float dAng = WrapAngle(a - center);
            float t = Hlsl.Saturate(dAng / span + 0.5f);
            float arch = Hlsl.Sin(t * 3.1415926f);
            float targetR = radius * (1.01f + arch * lift);
            float width = (0.0025f + arch * 0.0045f) * (0.55f + 0.45f * life);
            float dist = Hlsl.Abs(r - targetR) + Hlsl.Abs(dAng) * 0.0026f;
            float breakup = Fbm(new Float2(t * 11.0f + seed + i, time * 0.85f + i * 1.7f), 4);
            float mask = Hlsl.Exp(-(dist * dist) / (width * width + 1e-5f)) * Hlsl.Saturate(arch * 1.6f) * (0.50f + 0.50f * breakup) * life;
            float outside = Hlsl.Saturate((r - radius * 0.965f) / (radius * 0.09f));
            sum += Hlsl.Lerp(starHotColor, starWhiteColor, 0.30f + 0.35f * h) * mask * outside * 0.50f;
        }

        return sum;
    }

    private Float3 AsteroidBelts2D(Float2 uv)
    {
        Float3 sum = Float3.Zero;
        if (debrisQuality <= 0)
            return sum;

        for (int i = 0; i < 2; i++)
        {
            if (i >= beltCount) continue;

            Float4 belt = beltData[i];
            float beltOrbit = BeltOrbitRadius(belt.X);
            float beltWidth = BeltWidth(belt.Y);
            float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
            float band = Hlsl.Saturate((beltWidth - Hlsl.Abs(r - beltOrbit)) / Hlsl.Max(beltWidth, 1e-4f));
            float spin = time * (0.020f + i * 0.009f);
            Float2 buv = Rotate2(uv, spin);
            Float2 cell = Hlsl.Floor(buv * 360f + belt.W);
            float grain = Hash2(cell);
            float fine = Hash2(cell + 19.7f);
            float visible = grain > 1f - belt.Z * 0.24f ? 1f : 0f;
            float dust = Hlsl.Exp(-Hlsl.Abs(r - beltOrbit) * 48f) * (0.055f + fine * 0.045f);
            float sparkle = Hlsl.Pow(fine, 18f) * Hlsl.Saturate(Hlsl.Sin(time * 4.0f + grain * 31f) * 0.5f + 0.5f);
            sum += new Float3(0.58f, 0.50f, 0.40f) * (visible * band * (0.22f + sparkle * 0.35f) + dust * band);
        }

        return sum;
    }

    private Float3 AsteroidBeltsAtPlane(Float3 p)
    {
        Float3 sum = Float3.Zero;
        float r = Hlsl.Sqrt(p.X * p.X + p.Z * p.Z + 1e-6f);
        for (int i = 0; i < 2; i++)
        {
            if (i >= beltCount) continue;

            Float4 belt = beltData[i];
            float beltOrbit = BeltOrbitRadius(belt.X);
            float beltWidth = BeltWidth(belt.Y);
            float band = Hlsl.Saturate((beltWidth - Hlsl.Abs(r - beltOrbit)) / Hlsl.Max(beltWidth, 1e-4f));
            float angle = Hlsl.Atan2(p.Z, p.X) + time * (0.026f + i * 0.011f);
            Float2 cell = Hlsl.Floor(new Float2(angle * 52f, r * 310f) + belt.W);
            float grain = Hash2(cell);
            float fine = Hash2(cell + 31.3f);
            float visible = grain > 1f - belt.Z * 0.25f ? 1f : 0f;
            float sparkle = Hlsl.Pow(fine, 16f) * Hlsl.Saturate(Hlsl.Sin(time * 4.5f + grain * 29f) * 0.5f + 0.5f);
            sum += new Float3(0.58f, 0.50f, 0.40f) * band * (visible * (0.24f + sparkle * 0.42f) + 0.032f);
        }

        return sum;
    }

    private Float3 AsteroidBeltVolume(Float3 rayOrigin, Float3 rayDir)
    {
        Float3 sum = Float3.Zero;
        for (int i = 0; i < 2; i++)
        {
            if (i >= beltCount) continue;

            Float4 belt = beltData[i];
            float beltOrbit = BeltOrbitRadius(belt.X);
            float beltWidth = BeltWidth(belt.Y);
            for (int s = -4; s <= 4; s++)
            {
                if (Hlsl.Abs(s) > debrisQuality + 1)
                    continue;

                float layerY = s * beltWidth * 0.13f;
                if (Hlsl.Abs(rayDir.Y) < 1e-4f)
                    continue;

                float t = (layerY - rayOrigin.Y) / rayDir.Y;
                if (t <= 0f)
                    continue;

                Float3 p = rayOrigin + rayDir * t;
                float r = Hlsl.Sqrt(p.X * p.X + p.Z * p.Z + 1e-6f);
                float band = Hlsl.Saturate((beltWidth - Hlsl.Abs(r - beltOrbit)) / Hlsl.Max(beltWidth, 1e-4f));
                float angle = Hlsl.Atan2(p.Z, p.X) + time * (0.026f + i * 0.011f);
                Float2 cell = Hlsl.Floor(new Float2(angle * (95f + qualityLevel * 45f), r * (520f + qualityLevel * 180f)) + belt.W + s * 17.3f);
                float grain = Hash2(cell);
                float fine = Hash2(cell + 31.3f);
                float visible = grain > 1f - belt.Z * (0.20f + debrisQuality * 0.075f) ? 1f : 0f;
                float sparkle = Hlsl.Pow(fine, 14f) * Hlsl.Saturate(Hlsl.Sin(time * 5.2f + grain * 29f) * 0.5f + 0.5f);
                float clump = Hlsl.Pow(Hash2(cell + 71.9f), 7f);
                float layerFade = Hlsl.Exp(-layerY * layerY / (beltWidth * beltWidth * 0.26f + 1e-5f));
                sum += new Float3(0.66f, 0.59f, 0.48f) * band * layerFade * (visible * (0.10f + sparkle * 0.28f + clump * 0.18f));
            }
        }

        return sum;
    }

    private Float3 DrawPlanetRings2D(Float3 baseColor, Float2 uv, Float2 center, float planetRadius, Float4 ring, float planetSeed)
    {
        if (ring.X <= 0f)
            return baseColor;

        float visualRadius = Hlsl.Max(ring.Y / 1.35f, 1e-4f);
        float scale = planetRadius / visualRadius;
        float inner = ring.Y * scale;
        float outer = ring.Z * scale;
        Float2 d = uv - center;
        float flatten = 0.32f + Hlsl.Abs(Hlsl.Sin(ring.W)) * 0.24f;
        Float2 rp = new Float2(d.X, d.Y / flatten);
        float r = Hlsl.Sqrt(Hlsl.Dot(rp, rp) + 1e-6f);
        float band = Hlsl.Saturate((r - inner) / 0.0045f) * Hlsl.Saturate((outer - r) / 0.0045f);
        float cut = Hlsl.Saturate((Hlsl.Abs(d.Y) - planetRadius * 0.20f) / (planetRadius * 0.85f + 1e-4f));
        Float2 ringFlow = Rotate2(rp, time * 0.08f + planetSeed * 0.01f);
        float lane = Hlsl.Saturate(0.50f + 0.30f * Hlsl.Sin(r * 190f + ringFlow.X * 8.0f - time * 1.2f + planetSeed));
        float gapA = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.38f)) * 150f);
        float gapB = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.62f)) * 130f);
        float gapC = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.82f)) * 115f);
        float gap = 1f - gapA * 0.30f - gapB * 0.45f - gapC * 0.22f;
        float grain = Fbm(ringFlow * (95f + qualityLevel * 45f) + r * 23f + planetSeed, qualityLevel == 0 ? 2 : 3);
        Float2 asteroidCell = Hlsl.Floor(ringFlow * (260f + qualityLevel * 110f) + planetSeed);
        float asteroid = Hash2(asteroidCell);
        float asteroidMask = asteroid > 0.965f - qualityLevel * 0.010f ? Hlsl.Pow(Hash2(asteroidCell + 8.7f), 5f) : 0f;
        Float3 ringColor = new Float3(0.80f, 0.72f, 0.58f) * band * lane * gap * (0.46f + grain * 0.38f + cut * 0.28f);
        ringColor += new Float3(0.92f, 0.86f, 0.72f) * band * asteroidMask * (0.30f + qualityLevel * 0.13f);
        return baseColor + ringColor;
    }

    private Float4 PlanetRingHit(Float3 rayDir, Float3 rayOrigin, Float3 center, float planetRadius, Float4 ring, float planetSeed)
    {
        if (ring.X <= 0f)
            return new Float4(-1f, 0f, 0f, 0f);

        Float3 normal = Hlsl.Normalize(new Float3(Hlsl.Sin(ring.W) * 0.35f, 1f, Hlsl.Cos(ring.W) * 0.28f));
        float denom = Hlsl.Dot(rayDir, normal);
        if (Hlsl.Abs(denom) < 1e-4f)
            return new Float4(-1f, 0f, 0f, 0f);

        float inner = Hlsl.Max(ring.Y, planetRadius * 1.02f);
        float outer = Hlsl.Max(ring.Z, inner + planetRadius * 0.35f);
        Float3 color = Float3.Zero;
        float bestT = 1e20f;
        for (int layer = -3; layer <= 3; layer++)
        {
            if (Hlsl.Abs(layer) > debrisQuality)
                continue;

            float offset = layer * planetRadius * 0.055f;
            float t = (Hlsl.Dot(center - rayOrigin, normal) + offset) / denom;
            if (t <= 0.001f)
                continue;

            Float3 hit = rayOrigin + rayDir * t;
            Float3 rel = hit - center;
            float r = Hlsl.Sqrt(Hlsl.Dot(rel, rel) + 1e-6f);
            float band = Hlsl.Saturate((r - inner) / (planetRadius * 0.08f + 1e-5f)) *
                Hlsl.Saturate((outer - r) / (planetRadius * 0.08f + 1e-5f));
            if (band <= 0f)
                continue;

            Float2 ringUv = Rotate2(new Float2(rel.X, rel.Z), time * 0.08f + planetSeed * 0.01f);
            float lane = Hlsl.Saturate(0.52f + 0.28f * Hlsl.Sin(r * 175f + ringUv.X * 7.5f - time * 1.1f + planetSeed * 1.7f));
            float gapA = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.36f)) * 135f);
            float gapB = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.60f)) * 110f);
            float gapC = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.82f)) * 100f);
            float gap = 1f - gapA * 0.30f - gapB * 0.45f - gapC * 0.22f;
            float grain = Fbm(ringUv * (82f + qualityLevel * 48f) + r * 17f + planetSeed + layer * 11.3f, qualityLevel == 0 ? 2 : 3);
            float light = Hlsl.Saturate(Hlsl.Dot(normal, Hlsl.Normalize(-center)) * 0.5f + 0.5f) * 0.75f + 0.25f;
            Float2 asteroidCell = Hlsl.Floor(ringUv * (290f + qualityLevel * 180f) + planetSeed + layer * 19.7f);
            float asteroid = Hash2(asteroidCell);
            float asteroidMask = asteroid > 0.950f - qualityLevel * 0.014f ? Hlsl.Pow(Hash2(asteroidCell + 8.7f), 4f) : 0f;
            float layerFade = Hlsl.Exp(-(offset * offset) / (planetRadius * planetRadius * 0.030f + 1e-5f));
            Float3 layerColor = new Float3(0.80f, 0.72f, 0.58f) * band * lane * gap * (0.28f + grain * 0.18f) * light * layerFade;
            layerColor += new Float3(0.92f, 0.86f, 0.72f) * band * asteroidMask * (0.32f + qualityLevel * 0.14f) * light * layerFade;
            color += layerColor;
            bestT = Hlsl.Min(bestT, t);
        }

        if (bestT >= 1e19f)
            return new Float4(-1f, 0f, 0f, 0f);

        return new Float4(bestT, color.X, color.Y, color.Z);
    }

    private float MoonTransitShadow(Float3 surfacePoint, Float3 lightDir, int planetIndex)
    {
        if (shadowQuality <= 0)
            return 0f;

        float shadow = 0f;
        for (int i = 0; i < 32; i++)
        {
            if (i >= moonCount) continue;

            Float4 moon = moonData[i];
            if ((int)moon.X != planetIndex)
                continue;

            Float3 moonCenter = MoonWorldPosition(moon, moonExtra[i], planetData[planetIndex], planetExtra[planetIndex]);
            Float3 toMoon = moonCenter - surfacePoint;
            float along = Hlsl.Dot(toMoon, lightDir);
            if (along <= 0f)
                continue;

            Float3 closest = surfacePoint + lightDir * along;
            Float3 d = moonCenter - closest;
            float dist = Hlsl.Sqrt(Hlsl.Dot(d, d) + 1e-6f);
            float penumbra = moon.Z * (1.55f + along * 0.10f);
            float umbra = moon.Z * 0.62f;
            float soft = Hlsl.Saturate((penumbra - dist) / Hlsl.Max(penumbra - umbra, 1e-5f));
            shadow = Hlsl.Max(shadow, soft * soft * (0.45f + moon.Z * 8.0f));
        }

        return Hlsl.Saturate(shadow);
    }

    private float PlanetEclipseShadow(Float3 surfacePoint, Float3 lightDir, Float3 planetCenter, float planetRadius)
    {
        if (shadowQuality <= 0)
            return 0f;

        Float3 toPlanet = planetCenter - surfacePoint;
        float along = Hlsl.Dot(toPlanet, lightDir);
        if (along <= 0f)
            return 0f;

        Float3 closest = surfacePoint + lightDir * along;
        Float3 d = planetCenter - closest;
        float dist = Hlsl.Sqrt(Hlsl.Dot(d, d) + 1e-6f);
        float penumbra = planetRadius * (1.24f + along * 0.025f);
        float umbra = planetRadius * 0.78f;
        float soft = Hlsl.Saturate((penumbra - dist) / Hlsl.Max(penumbra - umbra, 1e-5f));
        return soft * soft;
    }

    private float RingSurfaceShadow(Float3 surfacePoint, Float3 planetCenter, Float3 lightDir, float planetRadius, Float4 ring)
    {
        if (shadowQuality <= 0)
            return 0f;

        if (ring.X <= 0f)
            return 0f;

        Float3 normal = Hlsl.Normalize(new Float3(Hlsl.Sin(ring.W) * 0.35f, 1f, Hlsl.Cos(ring.W) * 0.28f));
        float denom = Hlsl.Dot(lightDir, normal);
        if (Hlsl.Abs(denom) < 1e-4f)
            return 0f;

        float t = Hlsl.Dot(planetCenter - surfacePoint, normal) / denom;
        if (t <= 0f)
            return 0f;

        Float3 p = surfacePoint + lightDir * t - planetCenter;
        float r = Hlsl.Sqrt(Hlsl.Dot(p, p) + 1e-6f);
        float inner = Hlsl.Max(ring.Y, planetRadius * 1.02f);
        float outer = Hlsl.Max(ring.Z, inner + planetRadius * 0.35f);
        float band = Hlsl.Saturate((r - inner) / (planetRadius * 0.10f + 1e-5f)) *
            Hlsl.Saturate((outer - r) / (planetRadius * 0.10f + 1e-5f));
        if (band <= 0f)
            return 0f;

        Float2 ringUv = Rotate2(new Float2(p.X, p.Z), time * 0.08f + ring.W * 1.7f);
        float lane = Hlsl.Saturate(0.56f + 0.30f * Hlsl.Sin(r * 155f + ringUv.X * 7.0f - time * 1.1f));
        float gapA = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.36f)) * 135f);
        float gapB = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.60f)) * 110f);
        float gapC = Hlsl.Exp(-Hlsl.Abs(r - Hlsl.Lerp(inner, outer, 0.82f)) * 100f);
        float gaps = 1f - gapA * 0.30f - gapB * 0.45f - gapC * 0.22f;
        return Hlsl.Saturate(band * lane * gaps * 0.42f);
    }

    private Float3 DrawPlanet(Float3 baseColor, Float2 uv, Float2 center, float radius, int typeCode, float planetSeed, float hotGas, bool selected)
    {
        Float2 d = uv - center;
        float r2 = Hlsl.Dot(d, d);
        float selectionRing = selected ? Hlsl.Exp(-Hlsl.Abs(Hlsl.Sqrt(r2) - radius * 1.65f) * 90f) : 0f;
        if (r2 > radius * radius)
            return baseColor + new Float3(0.96f, 0.86f, 0.58f) * selectionRing * 0.72f;

        if (radius <= 1e-5f)
            return baseColor;

        float z = Hlsl.Sqrt(Hlsl.Max(radius * radius - r2, 0f));
        Float3 n = Hlsl.Normalize(new Float3(d.X, -d.Y, z));
        Float3 albedo = PlanetAlbedo(n, typeCode, planetSeed, hotGas);
        Float3 lightDir = Hlsl.Normalize(new Float3(-center.X, center.Y, 0.42f));
        float diffuse = Hlsl.Saturate(Hlsl.Dot(n, lightDir));
        float rim = Hlsl.Pow(1f - Hlsl.Saturate(n.Z), 3.0f);
        float halfLambert = diffuse * 0.78f + Hlsl.Pow(diffuse, 0.45f) * 0.16f;
        Float3 planet = albedo * (halfLambert * 0.92f + 0.085f + hotGas * 0.12f) + albedo * rim * 0.26f;
        planet += HotGasEmission(n, planetSeed, hotGas) * (0.35f + 0.65f * (1f - halfLambert));
        if (typeCode == 1)
        {
            float atmosphere = rim * (0.28f + 0.22f * Hlsl.Sin(time * 0.55f + planetSeed));
            planet += new Float3(0.35f, 0.60f, 0.95f) * atmosphere;
        }
        float edge = Hlsl.Saturate((radius - Hlsl.Sqrt(r2)) / 0.006f);
        return Hlsl.Lerp(baseColor, planet, edge);
    }

    private Float3 DrawPlanetMarker(Float3 baseColor, Float2 uv, Float2 center, float radius, int typeCode, float planetSeed, bool selected)
    {
        if (realismMode == 0)
            return baseColor;

        Float2 d = uv - center;
        float r = Hlsl.Sqrt(Hlsl.Dot(d, d) + 1e-6f);
        float marker = Hlsl.Max(radius * 4.6f, 0.014f);
        float glow = Hlsl.Exp(-(r * r) / (marker * marker + 1e-6f));
        float core = Hlsl.Exp(-(r * r) / (marker * marker * 0.10f + 1e-6f));
        float ring = selected ? Hlsl.Exp(-Hlsl.Abs(r - marker * 1.55f) * 120f) : 0f;
        Float3 c = PlanetAlbedo(new Float3(0.34f, 0.68f, 0.64f), typeCode, planetSeed, 0f);
        return baseColor + c * glow * 0.32f + new Float3(1.0f, 0.92f, 0.66f) * (core * 0.42f + ring * 0.70f);
    }

    private Float3 PlanetAlbedo(Float3 n, int typeCode, float planetSeed, float hotGas)
    {
        float axialSpin = time * (0.12f + Hash(planetSeed + 5.1f) * 0.24f) * (Hash(planetSeed + 9.7f) > 0.18f ? 1f : -1f);
        n = RotateY3(n, axialSpin + planetSeed * 0.07f);
        Float2 p = new Float2(Hlsl.Atan2(n.Z, n.X) / 6.283185f + 0.5f, n.Y * 0.5f + 0.5f);
        float type = typeCode / 10f;
        float flow = time * (0.010f + type * 0.018f);
        Float3 moved = Hlsl.Normalize(n + new Float3(Hlsl.Sin(flow + planetSeed), Hlsl.Cos(flow * 0.7f), Hlsl.Sin(flow * 1.3f)) * 0.045f);
        float baseNoise = SeamlessNoise(moved, 8f + type * 9f, planetSeed + seed, 4);
        float fineNoise = SeamlessNoise(moved, 24f + qualityLevel * 8f, planetSeed * 0.37f + seed, qualityLevel == 0 ? 2 : 3);
        float bands = Hlsl.Sin((p.Y + SeamlessNoise(moved, 9f, planetSeed, 3) * 0.060f) * (24f + type * 18f) + time * (typeCode == 2 ? 0.35f : 0.05f));
        float noise = Hlsl.Saturate(baseNoise * 0.78f + fineNoise * 0.22f);

        if (typeCode == 0)
        {
            Float3 rockA = new Float3(0.46f, 0.34f, 0.25f);
            Float3 rockB = new Float3(0.78f, 0.62f, 0.46f);
            float craters = Hlsl.Pow(1f - Hlsl.Abs(SeamlessNoise(n, 42f, planetSeed * 0.37f, qualityLevel == 0 ? 2 : 3) * 2f - 1f), 8f);
            float ridges = SeamlessNoise(n, 92f, planetSeed + 12.7f, qualityLevel == 2 ? 3 : 2);
            return Hlsl.Lerp(rockA * (0.68f + craters * 0.22f), rockB, noise) + rockB * Hlsl.Pow(ridges, 10f) * 0.06f;
        }
        if (typeCode == 1)
        {
            Float3 ocean = new Float3(0.05f, 0.22f, 0.46f);
            Float3 land = new Float3(0.32f, 0.52f, 0.24f);
            Float3 cloud = new Float3(0.86f, 0.90f, 0.88f);
            Float3 surface = Hlsl.Lerp(ocean, land, Hlsl.Saturate((noise - 0.48f) * 3f));
            Float3 cloudN = Hlsl.Normalize(n + new Float3(Hlsl.Sin(time * 0.045f), Hlsl.Cos(time * 0.029f), Hlsl.Sin(time * 0.036f)) * 0.085f);
            float cloudNoise = SeamlessNoise(cloudN, 20f + qualityLevel * 8f, planetSeed + 3.1f, qualityLevel == 0 ? 3 : 4);
            float cloudMask = Hlsl.Saturate((cloudNoise - 0.62f) * 2.8f);
            float weather = SeamlessNoise(cloudN, 58f, planetSeed + time * 0.04f, qualityLevel == 2 ? 3 : 2);
            return Hlsl.Lerp(surface, cloud, cloudMask * (0.72f + weather * 0.28f));
        }
        if (typeCode == 2)
        {
            float jetA = time * (0.24f + hotGas * 0.22f);
            float jetB = time * (0.42f + hotGas * 0.30f);
            Float3 gasN = Hlsl.Normalize(n + new Float3(Hlsl.Sin(jetA + n.Y * 9f), Hlsl.Sin(jetB + n.X * 4f) * 0.18f, Hlsl.Cos(jetA * 0.9f + n.Y * 7f)) * 0.18f);
            float gasWarp = SeamlessNoise(gasN, 14f, planetSeed + time * 0.20f, qualityLevel == 0 ? 3 : 5);
            float fineFlow = SeamlessNoise(Hlsl.Normalize(gasN + new Float3(Hlsl.Sin(jetB), 0f, Hlsl.Cos(jetB)) * 0.12f), 36f + qualityLevel * 10f, planetSeed - time * 0.32f, qualityLevel >= 2 ? 4 : 3);
            float fastBands = Hlsl.Sin((p.Y + gasWarp * 0.120f + fineFlow * 0.030f) * 54f + time * (1.15f + hotGas * 0.65f) + Hlsl.Sin(p.Y * 19f + time * 0.9f) * 0.38f);
            float storm = Hlsl.Pow(SeamlessNoise(gasN, 24f, planetSeed + 9.4f - time * 0.26f, qualityLevel >= 2 ? 4 : 3), 4.0f);
            Float3 gasA = Hlsl.Lerp(new Float3(0.54f, 0.36f, 0.22f), new Float3(0.92f, 0.38f, 0.10f), hotGas);
            Float3 gasB = Hlsl.Lerp(new Float3(0.95f, 0.79f, 0.54f), new Float3(1.0f, 0.76f, 0.30f), hotGas);
            Float3 c = Hlsl.Lerp(gasA, gasB, Hlsl.Saturate(fastBands * 0.44f + 0.52f + noise * 0.20f));
            c += new Float3(0.85f, 0.62f, 0.40f) * storm * 0.24f;
            c += new Float3(1.0f, 0.94f, 0.76f) * Hlsl.Pow(Hlsl.Saturate(fineFlow - 0.62f), 3.2f) * 0.22f;
            return c;
        }
        if (typeCode == 3)
        {
            Float3 iceGiantA = new Float3(0.26f, 0.58f, 0.76f);
            Float3 iceGiantB = new Float3(0.64f, 0.88f, 0.96f);
            Float3 hazeN = Hlsl.Normalize(n + new Float3(Hlsl.Sin(-time * 0.02f), Hlsl.Cos(time * 0.015f), Hlsl.Sin(time * 0.019f)) * 0.060f);
            float haze = SeamlessNoise(hazeN, 16f, planetSeed + 4.7f, 3);
            return Hlsl.Lerp(iceGiantA, iceGiantB, Hlsl.Saturate(bands * 0.38f + 0.46f + haze * 0.18f));
        }
        if (typeCode == 5)
        {
            Float3 crust = new Float3(0.18f, 0.08f, 0.05f);
            Float3 lava = new Float3(1.0f, 0.32f, 0.04f);
            return Hlsl.Lerp(crust, lava, Hlsl.Pow(noise, 3.5f));
        }
        if (typeCode == 6)
        {
            Float3 basalt = new Float3(0.26f, 0.24f, 0.22f);
            Float3 highland = new Float3(0.62f, 0.50f, 0.38f);
            float plates = Hlsl.Pow(1f - Hlsl.Abs(SeamlessNoise(moved, 18f, planetSeed + 14.2f, 4) * 2f - 1f), 5.5f);
            return Hlsl.Lerp(basalt, highland, noise) + new Float3(0.86f, 0.74f, 0.56f) * plates * 0.10f;
        }
        if (typeCode == 7)
        {
            Float3 mistA = new Float3(0.48f, 0.68f, 0.78f);
            Float3 mistB = new Float3(0.78f, 0.86f, 0.92f);
            float softBands = Hlsl.Saturate(bands * 0.30f + 0.50f + noise * 0.18f);
            return Hlsl.Lerp(mistA, mistB, softBands);
        }
        if (typeCode == 8)
        {
            Float3 carbonA = new Float3(0.035f, 0.032f, 0.030f);
            Float3 carbonB = new Float3(0.18f, 0.16f, 0.14f);
            float facets = Hlsl.Pow(Hlsl.Saturate(fineNoise), 7.0f);
            return Hlsl.Lerp(carbonA, carbonB, noise) + new Float3(0.80f, 0.90f, 1.0f) * facets * 0.11f;
        }
        if (typeCode == 9)
        {
            Float3 sandA = new Float3(0.62f, 0.42f, 0.23f);
            Float3 sandB = new Float3(0.94f, 0.72f, 0.42f);
            float dunes = Hlsl.Sin((p.X + noise * 0.06f) * 70f + time * 0.025f) * 0.5f + 0.5f;
            return Hlsl.Lerp(sandA, sandB, Hlsl.Saturate(noise * 0.72f + dunes * 0.28f));
        }
        if (typeCode == 10)
        {
            Float3 hazeA = new Float3(0.52f, 0.42f, 0.16f);
            Float3 hazeB = new Float3(0.98f, 0.82f, 0.34f);
            float acidClouds = SeamlessNoise(moved, 26f, planetSeed + time * 0.025f, qualityLevel == 0 ? 3 : 4);
            return Hlsl.Lerp(hazeA, hazeB, Hlsl.Saturate(noise * 0.45f + acidClouds * 0.55f));
        }

        Float3 iceA = new Float3(0.40f, 0.62f, 0.78f);
        Float3 iceB = new Float3(0.88f, 0.95f, 1.00f);
        return Hlsl.Lerp(iceA, iceB, noise);
    }

    private static float SeamlessNoise(Float3 n, float scale, float offset, int octaves)
    {
        Float3 w = Hlsl.Abs(n);
        w = w / Hlsl.Max(w.X + w.Y + w.Z, 1e-4f);
        float xy = Fbm(new Float2(n.X, n.Y) * scale + offset, octaves);
        float yz = Fbm(new Float2(n.Y, n.Z) * scale + offset * 1.37f, octaves);
        float zx = Fbm(new Float2(n.Z, n.X) * scale + offset * 1.73f, octaves);
        return xy * w.Z + yz * w.X + zx * w.Y;
    }

    private Float3 HotGasEmission(Float3 n, float planetSeed, float hotGas)
    {
        if (hotGas <= 0f)
            return Float3.Zero;

        n = RotateY3(n, time * (0.18f + Hash(planetSeed + 13.4f) * 0.16f) + planetSeed * 0.05f);
        Float3 flowN = Hlsl.Normalize(n + new Float3(Hlsl.Sin(time * 0.075f), Hlsl.Cos(time * 0.034f), Hlsl.Sin(time * 0.057f)) * 0.11f);
        float warp = SeamlessNoise(flowN, 8.0f, planetSeed, 4);
        float cracks = 1f - Hlsl.Abs(Hlsl.Sin((flowN.X + warp * 0.22f) * 28f + time * 0.72f) * Hlsl.Cos((flowN.Y - warp * 0.18f) * 18f + flowN.Z * 9f));
        cracks = Hlsl.Pow(Hlsl.Saturate(cracks), 5.0f);
        float islands = Hlsl.Pow(SeamlessNoise(flowN, 24f, planetSeed * 0.31f - time * 0.05f, qualityLevel == 2 ? 4 : 3), 5.5f);
        float mask = Hlsl.Saturate((cracks * 0.85f + islands * 0.45f) * hotGas);
        return new Float3(1.0f, 0.18f, 0.035f) * mask * (1.3f + hotGas * 1.9f);
    }

    private Float3 MoonAlbedo(Float3 n, float moonSeed, float icy)
    {
        float baseNoise = SeamlessNoise(n, 24f + qualityLevel * 14f, moonSeed, qualityLevel == 0 ? 3 : 4);
        float craters = Hlsl.Pow(1f - Hlsl.Abs(SeamlessNoise(n, 70f + qualityLevel * 30f, moonSeed + 7.1f, 3) * 2f - 1f), 9.0f);
        Float3 rockA = Hlsl.Lerp(new Float3(0.36f, 0.34f, 0.31f), new Float3(0.56f, 0.66f, 0.72f), icy);
        Float3 rockB = Hlsl.Lerp(new Float3(0.72f, 0.68f, 0.58f), new Float3(0.86f, 0.94f, 1.0f), icy);
        return Hlsl.Lerp(rockA, rockB, baseNoise) * (0.78f + craters * 0.18f);
    }

    private Float3 OrbitLine(Float2 uv, float orbit, float ecc)
    {
        float viewScale = viewMode == 0 ? ecc : Hlsl.Max(0.16f, Hlsl.Abs(Hlsl.Cos(pitch)) * ecc);
        Float2 p = new Float2(uv.X / orbit, uv.Y / (orbit * viewScale));
        float d = Hlsl.Abs(Hlsl.Sqrt(Hlsl.Dot(p, p) + 1e-6f) - 1f);
        float line = Hlsl.Exp(-(d * d) / 0.000018f);
        return new Float3(0.26f, 0.34f, 0.42f) * line * 0.18f;
    }

    private Float3 ProjectPlanet(Float4 data, Float4 extra)
    {
        float orbit = OrbitRadius(data.X);
        float phase = extra.X + orbitTime * extra.Y;
        float x = Hlsl.Cos(phase) * orbit;
        float z = Hlsl.Sin(phase) * orbit * data.Y;
        if (viewMode == 0)
            return new Float3(x, z, 1f);

        float cy = Hlsl.Cos(yaw);
        float sy = Hlsl.Sin(yaw);
        float rx = x * cy + z * sy;
        float rz = -x * sy + z * cy;
        float cp = Hlsl.Cos(pitch);
        float sp = Hlsl.Sin(pitch);
        float ry = -rz * sp;
        float rz2 = rz * cp;
        float persp = 1f / Hlsl.Max(0.62f, 1f + rz2 * 0.32f);
        return new Float3(rx * persp, ry * persp, persp);
    }

    private Float3 ProjectMoon(Float4 moon, Float4 extra, Float4 parentData, Float4 parentExtra)
    {
        Float3 p = MoonWorldPosition(moon, extra, parentData, parentExtra);
        if (viewMode == 0)
            return new Float3(p.X, p.Z, 1f);

        Float3 focus = FocusPosition();
        Float3 rel = p - focus;
        float cy = Hlsl.Cos(yaw);
        float sy = Hlsl.Sin(yaw);
        float rx = rel.X * cy + rel.Z * sy;
        float rz = -rel.X * sy + rel.Z * cy;
        float cp = Hlsl.Cos(pitch);
        float sp = Hlsl.Sin(pitch);
        float ry = -rz * sp + rel.Y * cp;
        float depth = rz * cp + rel.Y * sp;
        float persp = 1f / Hlsl.Max(0.58f, 1f + depth * 0.38f);
        return new Float3(rx * persp, ry * persp, persp);
    }

    private Float3 DrawMoon(Float3 baseColor, Float2 uv, Float2 center, float radius, float moonSeed, bool selected)
    {
        Float2 d = uv - center;
        float r2 = Hlsl.Dot(d, d);
        float selectionRing = selected ? Hlsl.Exp(-Hlsl.Abs(Hlsl.Sqrt(r2) - radius * 1.9f) * 130f) : 0f;
        if (r2 > radius * radius)
            return baseColor + new Float3(0.96f, 0.86f, 0.58f) * selectionRing * 0.64f;

        float z = Hlsl.Sqrt(Hlsl.Max(radius * radius - r2, 0f));
        Float3 n = Hlsl.Normalize(new Float3(d.X, -d.Y, z));
        Float3 albedo = MoonAlbedo(n, moonSeed, 0f);
        Float3 lightDir = Hlsl.Normalize(new Float3(-center.X, center.Y, 0.42f));
        float diffuse = Hlsl.Saturate(Hlsl.Dot(n, lightDir));
        float rim = Hlsl.Pow(1f - Hlsl.Saturate(n.Z), 3.2f);
        Float3 moon = albedo * (diffuse * 0.84f + 0.10f) + albedo * rim * 0.12f;
        float edge = Hlsl.Saturate((radius - Hlsl.Sqrt(r2)) / 0.003f);
        return Hlsl.Lerp(baseColor, moon, edge);
    }

    private Float4 ProjectPlanet3D(Float4 data, Float4 extra)
    {
        float orbit = OrbitRadius(data.X);
        float phase = extra.X + orbitTime * extra.Y;
        float x = Hlsl.Cos(phase) * orbit;
        float z = Hlsl.Sin(phase) * orbit * data.Y;
        float cy = Hlsl.Cos(yaw);
        float sy = Hlsl.Sin(yaw);
        float rx = x * cy + z * sy;
        float rz = -x * sy + z * cy;
        float cp = Hlsl.Cos(pitch);
        float sp = Hlsl.Sin(pitch);
        float ry = -rz * sp;
        float depth = rz * cp;
        float persp = 1f / Hlsl.Max(0.58f, 1f + depth * 0.38f);
        return new Float4(rx * persp, ry * persp, persp, depth);
    }

    private static Float3 HabitableZone(Float2 uv, float hz)
    {
        float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv) + 1e-6f);
        float inner = Hlsl.Saturate((r - hz * 0.82f) / 0.020f);
        float outer = Hlsl.Saturate((hz * 1.18f - r) / 0.020f);
        float band = inner * outer * 0.035f;
        return new Float3(0.20f, 0.65f, 0.36f) * band;
    }

    private Float3 Background(Float2 uv)
    {
        float r = Hlsl.Sqrt(Hlsl.Dot(uv, uv));
        Float3 bg = Hlsl.Lerp(new Float3(0.006f, 0.008f, 0.014f), new Float3(0.018f, 0.024f, 0.036f), Hlsl.Saturate(1.2f - r * 0.45f));
        Float2 p = uv * 55f + seed;
        Float2 cell = Hlsl.Floor(p);
        Float2 f = p - cell;
        float rnd = Hash2(cell);
        Float2 sp = new Float2(Hash2(cell + 21.1f), Hash2(cell + 43.7f));
        float star = rnd > 0.982f ? Hlsl.Exp(-Hlsl.Dot(f - sp, f - sp) / 0.003f) * 0.35f : 0f;
        return bg + new Float3(star, star * 0.92f, star * 1.1f);
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
        => Hlsl.Frac(Hlsl.Sin(Hlsl.Dot(p, new Float2(127.1f, 311.7f))) * 43758.5453f);

    private static float Hash(float x)
        => Hlsl.Frac(Hlsl.Sin(x * 91.3458f) * 47453.5453f);

    private static Float2 SphericalUv(Float3 n)
    {
        return new Float2(
            Hlsl.Atan2(n.Z, n.X) / 6.283185f + 0.5f,
            Hlsl.Asin(Hlsl.Clamp(n.Y, -1f, 1f)) / 3.1415926f + 0.5f);
    }

    private static float WrapAngle(float a)
        => Hlsl.Atan2(Hlsl.Sin(a), Hlsl.Cos(a));

    private static float Smooth01(float x)
    {
        x = Hlsl.Saturate(x);
        return x * x * (3f - 2f * x);
    }

    private static Float3 Aces(Float3 x)
    {
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;
        return Hlsl.Saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    private float OrbitRadius(float renderOrbit)
        => realismMode != 0 ? renderOrbit * 4.75f : renderOrbit;

    private float BeltOrbitRadius(float renderOrbit)
        => realismMode != 0 ? renderOrbit * 4.75f : renderOrbit;

    private float BeltWidth(float renderWidth)
        => realismMode != 0 ? renderWidth * 4.75f : renderWidth;

    private float VisualRadius(float renderRadius)
        => realismMode != 0 ? Hlsl.Max(renderRadius * 0.18f, 0.0032f) : renderRadius;

    private float HabitableRenderRadius()
    {
        float hz = Hlsl.Clamp(habitableZoneAu * 0.55f, 0.52f, 2.10f);
        return realismMode != 0 ? hz * 4.75f : hz;
    }
}
