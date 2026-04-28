using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct TonemapShader(
    ReadWriteBuffer<Float4> hdrScene,
    ReadWriteBuffer<Float4> bloom,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int width,
    int height,
    float exposure,
    float bloomIntensity,
    int bhLensCount,
    Float4 bhLens0,
    Float4 bhLens1,
    float bhLensStrength,
    float bhLensRadius,
    float bhLensCore,
    float localContrastAmount) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X, y = ThreadIds.Y;
        if (x >= width || y >= height) return;

        Float2 baseUv = new Float2((x + 0.5f) / width, (y + 0.5f) / height);
        Float2 uv = baseUv;
        float bhOcclusion = 0f;
        Float3 bhRing = Float3.Zero;

        if (bhLensCount > 0 && bhLensStrength > 1e-6f && bhLensRadius > 1e-5f)
        {
            uv = ApplyLens(uv, bhLens0, bhLensStrength, bhLensRadius, bhLensCore);
            bhOcclusion += ComputeBlackDisk(baseUv, bhLens0, bhLensRadius);
            bhRing += ComputePhotonRing(baseUv, bhLens0, bhLensRadius, bhLensStrength);
            if (bhLensCount > 1)
            {
                uv = ApplyLens(uv, bhLens1, bhLensStrength, bhLensRadius, bhLensCore);
                bhOcclusion += ComputeBlackDisk(baseUv, bhLens1, bhLensRadius);
                bhRing += ComputePhotonRing(baseUv, bhLens1, bhLensRadius, bhLensStrength);
            }
        }

        Float3 scene = SampleSceneBilinear(uv);
        if (localContrastAmount > 1e-4f)
        {
            float offX = 1.65f / width;
            float offY = 1.65f / height;
            Float3 sL = SampleSceneBilinear(uv + new Float2(-offX, 0f));
            Float3 sR = SampleSceneBilinear(uv + new Float2(offX, 0f));
            Float3 sU = SampleSceneBilinear(uv + new Float2(0f, -offY));
            Float3 sD = SampleSceneBilinear(uv + new Float2(0f, offY));
            Float3 blur = (sL + sR + sU + sD) * 0.25f;
            Float3 detail = scene - blur;

            float lum = Hlsl.Dot(scene, new Float3(0.2126f, 0.7152f, 0.0722f));
            float mask = Hlsl.Saturate((lum - 0.02f) * 2.4f) * Hlsl.Saturate(1f - lum * 0.34f);
            scene += detail * localContrastAmount * mask;
        }
        Float3 bl = SampleBloomBilinear(uv);

        Float3 combined = scene + bl * bloomIntensity;
        float occ = Hlsl.Saturate(bhOcclusion);
        combined = combined * (1f - occ) + bhRing;

        Float3 mapped = AcesTonemap(Hlsl.Max(combined * exposure, Float3.Zero));
        mapped = Hlsl.Pow(Hlsl.Max(mapped, Float3.Zero), 1f / 2.2f);

        output[x, y] = new Float4(mapped, 1f);
    }

    private static Float2 ApplyLens(Float2 uv, Float4 lens, float strength, float radius, float core)
    {
        if (lens.W <= 0.01f)
            return uv;

        Float2 center = new Float2(lens.X, lens.Y);
        Float2 d = uv - center;
        float rSq = Hlsl.Dot(d, d);
        float proximity = Hlsl.Saturate(lens.W);
        float localRadius = radius * (0.2f + 0.8f * proximity);
        float radiusSq = localRadius * localRadius;
        if (rSq >= radiusSq)
            return uv;

        float r = Hlsl.Sqrt(rSq + 1e-10f);
        Float2 dir = d / Hlsl.Max(r, 1e-5f);
        float edgeFade = 1f - Hlsl.Saturate(r / localRadius);
        edgeFade = edgeFade * edgeFade * (3f - 2f * edgeFade);
        float massScale = Hlsl.Max(lens.Z, 0.05f);
        float einsteinR = localRadius * (0.30f + 0.13f * Hlsl.Sqrt(proximity));
        float ringW = localRadius * (0.10f + 0.035f * proximity);
        float ringPull = Hlsl.Exp(-((r - einsteinR) * (r - einsteinR)) / (ringW * ringW + 1e-8f));
        float gravityBend = (strength * massScale * proximity * proximity) / (r * r + core);
        float shift = gravityBend * edgeFade * 0.34f + (einsteinR - r) * ringPull * edgeFade * 0.20f;
        shift = Hlsl.Clamp(shift, -localRadius * 0.10f, localRadius * 0.38f);

        return uv + dir * shift;
    }

    private static Float3 AcesTonemap(Float3 x)
    {
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;
        Float3 num = x * (a * x + b);
        Float3 den = x * (c * x + d) + e;
        return Hlsl.Saturate(num / den);
    }

    private static float ComputeBlackDisk(Float2 uv, Float4 lens, float radius)
    {
        if (lens.W <= 0.01f)
            return 0f;

        float proximity = Hlsl.Saturate(lens.W);
        Float2 d = uv - new Float2(lens.X, lens.Y);
        float r = Hlsl.Sqrt(Hlsl.Dot(d, d) + 1e-10f);
        float diskR = radius * (0.10f + 0.18f * proximity);
        float t = Hlsl.Saturate(1f - r / (diskR + 1e-5f));
        float mask = t * t * (3f - 2f * t);
        return mask * proximity * 0.98f;
    }

    private static Float3 ComputePhotonRing(Float2 uv, Float4 lens, float radius, float strength)
    {
        if (lens.W <= 0.01f)
            return Float3.Zero;

        float proximity = Hlsl.Saturate(lens.W);
        Float2 d = uv - new Float2(lens.X, lens.Y);
        float r = Hlsl.Sqrt(Hlsl.Dot(d, d) + 1e-10f);
        float ringR = radius * (0.25f + 0.18f * proximity);
        float ringW = radius * (0.030f + 0.018f * proximity);
        float dr = r - ringR;
        float ring = Hlsl.Exp(-(dr * dr) / (ringW * ringW + 1e-8f));

        float massScale = Hlsl.Max(lens.Z, 0.05f);
        float amp = ring * proximity * proximity * massScale *
                    (0.14f + 6.5f * Hlsl.Sqrt(Hlsl.Max(strength, 1e-6f)));
        Float3 color = new Float3(1.05f, 0.95f, 0.82f);
        return color * amp;
    }

    private Float3 SampleSceneBilinear(Float2 uv)
    {
        float fx = Hlsl.Clamp(uv.X * (width - 1), 0f, width - 1);
        float fy = Hlsl.Clamp(uv.Y * (height - 1), 0f, height - 1);

        int x0 = (int)Hlsl.Floor(fx);
        int y0 = (int)Hlsl.Floor(fy);
        int x1 = Hlsl.Min(x0 + 1, width - 1);
        int y1 = Hlsl.Min(y0 + 1, height - 1);

        float tx = fx - x0;
        float ty = fy - y0;

        Float3 c00 = hdrScene[y0 * width + x0].XYZ;
        Float3 c10 = hdrScene[y0 * width + x1].XYZ;
        Float3 c01 = hdrScene[y1 * width + x0].XYZ;
        Float3 c11 = hdrScene[y1 * width + x1].XYZ;

        Float3 cx0 = Hlsl.Lerp(c00, c10, tx);
        Float3 cx1 = Hlsl.Lerp(c01, c11, tx);
        return Hlsl.Lerp(cx0, cx1, ty);
    }

    private Float3 SampleBloomBilinear(Float2 uv)
    {
        float fx = Hlsl.Clamp(uv.X * (width - 1), 0f, width - 1);
        float fy = Hlsl.Clamp(uv.Y * (height - 1), 0f, height - 1);

        int x0 = (int)Hlsl.Floor(fx);
        int y0 = (int)Hlsl.Floor(fy);
        int x1 = Hlsl.Min(x0 + 1, width - 1);
        int y1 = Hlsl.Min(y0 + 1, height - 1);

        float tx = fx - x0;
        float ty = fy - y0;

        Float3 c00 = bloom[y0 * width + x0].XYZ;
        Float3 c10 = bloom[y0 * width + x1].XYZ;
        Float3 c01 = bloom[y1 * width + x0].XYZ;
        Float3 c11 = bloom[y1 * width + x1].XYZ;

        Float3 cx0 = Hlsl.Lerp(c00, c10, tx);
        Float3 cx1 = Hlsl.Lerp(c01, c11, tx);
        return Hlsl.Lerp(cx0, cx1, ty);
    }
}
