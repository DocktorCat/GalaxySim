using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct KickShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<Float4> velocities,
    ReadWriteBuffer<uint> maxSpeedSqFixed,
    int particleCount,
    float halfDt,
    float gravity,
    float softeningSq,
    float haloV0Sq,
    float haloScaleRadius,
    float bhMass,
    float bhSoftSq,
    float diskMassRef,
    float diskScaleLength,
    float adaptiveSofteningStrength) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        if (i >= particleCount) return;

        Float3 pi = positions[i].XYZ;
        Float3 acc = Float3.Zero;
        float rSq = Hlsl.Dot(pi, pi);

        float localSofteningSq = softeningSq;
        if (adaptiveSofteningStrength > 1e-4f)
        {
            float rs = Hlsl.Max(diskScaleLength, 0.15f);
            float radius = Hlsl.Sqrt(rSq + 1e-8f);
            float diskMass = Hlsl.Max(diskMassRef, 1e-4f);
            const float inv8Pi = 0.039788736f;
            float rho0 = diskMass * inv8Pi / (rs * rs * rs);
            float rho = rho0 * Hlsl.Exp(-radius / rs);
            const float rhoRef = 0.0035f;
            float adapt = Hlsl.Pow(rhoRef / Hlsl.Max(rho, 1e-7f), 0.33333334f);
            adapt = Hlsl.Clamp(adapt, 0.55f, 2.8f);

            float t = Hlsl.Clamp(adaptiveSofteningStrength, 0f, 1f);
            float factor = 1f + (adapt - 1f) * t;
            localSofteningSq = softeningSq * factor * factor;
        }

        for (int j = 0; j < particleCount; j++)
        {
            Float4 pj = positions[j];
            Float3 r = pj.XYZ - pi;
            float distSq = Hlsl.Dot(r, r) + localSofteningSq;
            float invDist3 = Hlsl.Rsqrt(distSq * distSq * distSq);
            acc += r * (gravity * pj.W * invDist3);
        }

        float haloRs = Hlsl.Max(haloScaleRadius, 1e-3f);
        float haloRadius = Hlsl.Sqrt(rSq + 1e-8f);
        float x = haloRadius / haloRs;
        float nfwShape = x < 1e-3f
            ? x * x * (0.5f - 0.6666667f * x + 0.75f * x * x)
            : Hlsl.Log(1f + x) - x / (1f + x);
        const float nfwAtScaleRadius = 0.19314718f;
        float vScaleSq = haloV0Sq / nfwAtScaleRadius;
        float vHaloSq = vScaleSq * (nfwShape / Hlsl.Max(x, 1e-4f));
        acc -= pi * (vHaloSq / Hlsl.Max(rSq, 1e-8f));

        float bhDistSq = rSq + bhSoftSq;
        float bhInv3 = Hlsl.Rsqrt(bhDistSq * bhDistSq * bhDistSq);
        acc -= pi * (gravity * bhMass * bhInv3);

        Float4 v = velocities[i];
        v.XYZ += acc * halfDt;
        velocities[i] = v;

        float speedSq = Hlsl.Dot(v.XYZ, v.XYZ);
        uint speedFixed = (uint)Hlsl.Min(speedSq * 1e6f, 4.2e9f);
        Hlsl.InterlockedMax(ref maxSpeedSqFixed[0], speedFixed);
    }
}
