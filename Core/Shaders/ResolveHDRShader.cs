using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ResolveHDRShader(
    ReadWriteBuffer<uint> hdrR,
    ReadWriteBuffer<uint> hdrG,
    ReadWriteBuffer<uint> hdrB,
    ReadWriteBuffer<uint> unresolvedR,
    ReadWriteBuffer<uint> unresolvedG,
    ReadWriteBuffer<uint> unresolvedB,
    int unresolvedWidth,
    int unresolvedHeight,
    ReadWriteBuffer<uint> opacity,
    ReadWriteBuffer<Float4> output,
    int width,
    int height,
    float dustStrength,
    float bodyGradientStrength,
    int bodyColorTheme,
    float unresolvedBlobSuppression,
    float dustBlobSuppression) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        if (x >= width || y >= height) return;

        int idx = y * width + x;
        Float3 resolved = new Float3(hdrR[idx], hdrG[idx], hdrB[idx]) / 4096f;
        Float3 unresolved = SampleUnresolvedBilinear(x, y);

        float tau = ApplyDustBlobSuppression(x, y, opacity[idx] / 4096f * dustStrength);
        tau = Hlsl.Min(tau, 4.2f);
        float resolvedLum = Hlsl.Dot(resolved, new Float3(0.2126f, 0.7152f, 0.0722f));
        float structureMask = Hlsl.Saturate((resolvedLum - 0.008f) * 4.3f + tau * 0.34f + 0.20f);
        float suppressionT = Hlsl.Saturate(unresolvedBlobSuppression);
        float isolated = 1f - structureMask;
        float dustKeep = Hlsl.Saturate(tau * 0.52f);
        float suppress = 1f - suppressionT * isolated * (0.55f + 0.45f * isolated) * (1f - dustKeep * 0.85f);
        float gate = Hlsl.Lerp(1f, structureMask, suppressionT);
        unresolved *= gate * suppress;

        Float3 hdr = resolved + unresolved;

        Float3 tauRgb = new Float3(tau * 0.82f, tau * 1.00f, tau * 1.34f);
        Float3 transmission = Hlsl.Exp(-tauRgb);
        hdr *= transmission;

        Float3 innerColor = GetBodyInnerColor(bodyColorTheme);
        Float3 outerColor = GetBodyOuterColor(bodyColorTheme);

        float gradient = Hlsl.Saturate(Hlsl.Pow(Hlsl.Min(tau / 2.2f, 1f), 0.72f));
        float gradientMix = gradient * Hlsl.Saturate(0.35f + 0.65f * bodyGradientStrength);
        Float3 bodyTint = Hlsl.Lerp(outerColor, innerColor, gradientMix);

        float lumGate = Hlsl.Saturate(hdr.Y * 0.38f + 0.12f);

        float scatter = (1f - transmission.Y) * (0.05f + 0.09f * bodyGradientStrength);
        hdr += bodyTint * scatter * lumGate;

        float veil = Hlsl.Saturate(1f - transmission.Y) * 0.24f * bodyGradientStrength;
        hdr += bodyTint * veil * (0.10f + 0.25f * lumGate);

        output[idx] = new Float4(hdr, 1f);
    }

    private static Float3 GetBodyInnerColor(int theme)
    {
        if (theme == 1) return new Float3(1.12f, 0.88f, 0.68f);
        if (theme == 2) return new Float3(1.05f, 0.74f, 0.72f);
        if (theme == 3) return new Float3(0.90f, 1.00f, 1.08f);
        return new Float3(1.15f, 0.78f, 0.46f);
    }

    private static Float3 GetBodyOuterColor(int theme)
    {
        if (theme == 1) return new Float3(0.80f, 0.92f, 1.06f);
        if (theme == 2) return new Float3(0.78f, 0.86f, 1.02f);
        if (theme == 3) return new Float3(0.70f, 0.84f, 1.12f);
        return new Float3(0.92f, 0.66f, 0.34f);
    }

    private Float3 SampleUnresolvedBilinear(int x, int y)
    {
        if (unresolvedWidth <= 0 || unresolvedHeight <= 0)
            return Float3.Zero;

        float fx = ((x + 0.5f) / width) * unresolvedWidth - 0.5f;
        float fy = ((y + 0.5f) / height) * unresolvedHeight - 0.5f;

        int x0 = Hlsl.Clamp((int)Hlsl.Floor(fx), 0, unresolvedWidth - 1);
        int y0 = Hlsl.Clamp((int)Hlsl.Floor(fy), 0, unresolvedHeight - 1);
        int x1 = Hlsl.Min(x0 + 1, unresolvedWidth - 1);
        int y1 = Hlsl.Min(y0 + 1, unresolvedHeight - 1);

        float tx = Hlsl.Saturate(fx - x0);
        float ty = Hlsl.Saturate(fy - y0);

        Float3 c00 = FetchUnresolved(x0, y0);
        Float3 c10 = FetchUnresolved(x1, y0);
        Float3 c01 = FetchUnresolved(x0, y1);
        Float3 c11 = FetchUnresolved(x1, y1);

        Float3 cx0 = Hlsl.Lerp(c00, c10, tx);
        Float3 cx1 = Hlsl.Lerp(c01, c11, tx);
        return Hlsl.Lerp(cx0, cx1, ty);
    }

    private Float3 FetchUnresolved(int x, int y)
    {
        int idx = y * unresolvedWidth + x;
        return new Float3(unresolvedR[idx], unresolvedG[idx], unresolvedB[idx]) / 1900f;
    }

    private float ApplyDustBlobSuppression(int x, int y, float tau)
    {
        float t = Hlsl.Saturate(dustBlobSuppression);
        if (t <= 1e-4f || tau <= 1e-5f)
            return tau;

        float n = SampleTau(x, y - 2);
        float s = SampleTau(x, y + 2);
        float e = SampleTau(x + 2, y);
        float w = SampleTau(x - 2, y);
        float ne = SampleTau(x + 2, y - 2);
        float nw = SampleTau(x - 2, y - 2);
        float se = SampleTau(x + 2, y + 2);
        float sw = SampleTau(x - 2, y + 2);

        float crossMean = (n + s + e + w) * 0.25f;
        float diagMean = (ne + nw + se + sw) * 0.25f;
        float ringMean = crossMean * 0.65f + diagMean * 0.35f;
        float ringMax = Hlsl.Max(Hlsl.Max(Hlsl.Max(n, s), Hlsl.Max(e, w)),
            Hlsl.Max(Hlsl.Max(ne, nw), Hlsl.Max(se, sw)));

        float isolatedPeak = Hlsl.Saturate((tau - ringMean * 1.55f - 0.018f) * 3.5f);
        float lowSupport = 1f - Hlsl.Saturate(ringMax / Hlsl.Max(tau * 0.88f, 1e-4f));
        float outerOnly = 1f - Hlsl.Saturate(ringMean * 1.75f);
        float suppress = t * isolatedPeak * (0.45f + 0.55f * lowSupport) * outerOnly;

        float clampedTau = Hlsl.Min(tau, ringMean * 1.22f + ringMax * 0.30f + 0.012f);
        float smoothedTau = tau * 0.62f + ringMean * 0.38f;
        float target = Hlsl.Min(smoothedTau, clampedTau);
        return Hlsl.Lerp(tau, target, Hlsl.Saturate(suppress));
    }

    private float SampleTau(int x, int y)
    {
        x = Hlsl.Clamp(x, 0, width - 1);
        y = Hlsl.Clamp(y, 0, height - 1);
        return opacity[y * width + x] / 4096f * dustStrength;
    }
}
