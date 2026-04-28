using ComputeSharp;

namespace GalaxySim.Core.Shaders;


[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct CoherentDustLanesShader(
    ReadWriteBuffer<uint> opacityBuffer,
    int screenW,
    int screenH,
    int galaxyCount,
    Float4 center0,   
    Float4 center1,
    float strength,
    float pitch,
    float sharpness,
    float spiralness,
    float edgeRaggedness,
    float time,
    float faceOnFactor) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        if (x >= screenW || y >= screenH) return;

        Float2 uv = new Float2((x + 0.5f) / screenW, (y + 0.5f) / screenH);
        float aspect = (float)screenW / Hlsl.Max(screenH, 1);

        float tau = 0f;
        tau += DustFromGalaxy(uv, aspect, center0);
        if (galaxyCount > 1)
            tau += DustFromGalaxy(uv, aspect, center1);

        if (tau <= 1e-4f) return;

        int idx = y * screenW + x;
        uint contrib = (uint)Hlsl.Min(tau * 4096f, 4_000_000f);
        Hlsl.InterlockedAdd(ref opacityBuffer[idx], contrib);
    }

    private float DustFromGalaxy(Float2 uv, float aspect, Float4 center)
    {
        if (center.W <= 0f)
            return 0f;

        Float2 d = uv - center.XY;
        d.X *= aspect;

        float r = Hlsl.Sqrt(Hlsl.Dot(d, d) + 1e-10f);
        float scale = Hlsl.Clamp(0.082f / (center.W + 0.055f), 0.005f, 0.072f);
        float u = r / Hlsl.Max(scale, 1e-5f);
        if (u < 0.01f || u > 3.00f)
            return 0f;

        float theta = Hlsl.Atan2(d.Y, d.X);
        float massScale = Hlsl.Max(center.Z, 0.05f);
        float animTime = time * (0.16f + 0.28f * Hlsl.Saturate(spiralness));

        float phase = 2.0f * theta - pitch * Hlsl.Log(u + 0.07f) - animTime;
        float arm = Hlsl.Cos(phase) * 0.5f + 0.5f;
        arm = Hlsl.Pow(Hlsl.Saturate(arm), Hlsl.Max(sharpness, 1f));
        float ring = 0.90f + 0.10f * Hlsl.Cos(theta * 6.3f + u * 2.8f);
        arm = Hlsl.Lerp(ring, arm, Hlsl.Saturate(spiralness));

        float radial = Hlsl.Exp(-u * 1.08f);
        float innerFade = SmoothStep(0.012f, 0.24f, u);
        float outerFade = 1f - SmoothStep(1.55f, 2.45f, u);
        float outerBand = SmoothStep(1.45f, 2.65f, u);

        float nearCameraFade = 0.26f + 0.74f * SmoothStep(0.16f, 0.92f, center.W);
        float farDistanceFade = 1f - SmoothStep(0.78f, 1.70f, center.W);

        Float2 noiseTimeShift = new Float2(time * 0.071f, -time * 0.053f);
        float n = Noise(uv * (95f * scale + 23f) + center.XY * 11.3f + noiseTimeShift);
        float filaments = 0.58f + 0.86f * Hlsl.Pow(n, 1.7f);

        float edgeNoise = Noise(uv * (138f * scale + 17f) + center.XY * 7.9f - noiseTimeShift * 0.6f);
        float rag = 1f - outerBand * Hlsl.Saturate(edgeRaggedness) * 0.22f * (0.35f + 0.65f * edgeNoise);
        rag = Hlsl.Saturate(rag);
        float faceOnMask = SmoothStep(0.18f, 0.82f, Hlsl.Saturate(faceOnFactor));
        float edgeDensity = 0.84f + 0.20f * SmoothStep(1.15f, 2.35f, u);

        radial *= innerFade * outerFade * rag;

        float lane = arm * radial * filaments;
        lane *= strength * massScale * nearCameraFade * farDistanceFade * faceOnMask * edgeDensity;
        return lane;
    }

    private static float Noise(Float2 p)
    {
        float n = Hlsl.Sin(Hlsl.Dot(p, new Float2(127.1f, 311.7f))) * 43758.5453f;
        n = n - Hlsl.Floor(n);
        return n;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Hlsl.Saturate((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
