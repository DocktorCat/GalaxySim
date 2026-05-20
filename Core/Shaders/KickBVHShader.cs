using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct KickBVHShader(
    ReadWriteBuffer<Float4> positions,
    ReadWriteBuffer<Float4> velocities,
    ReadWriteBuffer<uint> maxSpeedSqFixed,
    ReadWriteBuffer<int> travLeft,
    ReadWriteBuffer<int> travNext,
    ReadWriteBuffer<Float4> travComMass,
    ReadWriteBuffer<float> travSizeMax,
    ReadWriteBuffer<Float4> galaxyCenterPosMass, 
    ReadWriteBuffer<int> particleHomeCenterId,      
    int galaxyCount,
    int particleCount,
    float halfDt,
    float gravity,
    float softeningSq,
    float haloV0Sq,
    float haloScaleRadius,
    float bhMass,
    float bhSoftSq,
    float thetaSq,
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

        int centerIdx = particleHomeCenterId[i];
        bool validCenter = (uint)centerIdx < (uint)galaxyCount;

        if (!validCenter || galaxyCount > 1)
        {
            int nearestCenterIdx = validCenter ? centerIdx : 0;
            float bestSq = float.MaxValue;
            for (int gi = 0; gi < galaxyCount; gi++)
            {
                Float3 relTry = pi - galaxyCenterPosMass[gi].XYZ;
                float rSqTry = Hlsl.Dot(relTry, relTry);
                if (rSqTry < bestSq)
                {
                    bestSq = rSqTry;
                    nearestCenterIdx = gi;
                }
            }
            centerIdx = nearestCenterIdx;
            validCenter = (uint)centerIdx < (uint)galaxyCount;
        }

        Float4 center = validCenter ? galaxyCenterPosMass[centerIdx] : default;
        float massScale = validCenter ? center.W : 1f;
        Float3 relToCenter = validCenter ? (pi - center.XYZ) : pi;
        float rSqCenter = Hlsl.Dot(relToCenter, relToCenter);

        float localSofteningSq = softeningSq;
        if (adaptiveSofteningStrength > 1e-4f)
        {
            float rs = Hlsl.Max(diskScaleLength, 0.15f);
            float radius = Hlsl.Sqrt(rSqCenter + 1e-8f);
            float diskMass = Hlsl.Max(diskMassRef * Hlsl.Max(massScale, 0.05f), 1e-4f);
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

        int internalCount = particleCount - 1;
        int current = 0;            
        int maxIter = 4 * particleCount;
        int iter = 0;

        while (current >= 0 && iter < maxIter)
        {
            iter++;

            if (current < internalCount)
            {
                Float4 com = travComMass[current];
                float mass = com.W;

                if (mass <= 0f)
                {
                    current = travNext[current];
                    continue;
                }

                Float3 r = com.XYZ - pi;
                float dSq = Hlsl.Dot(r, r);
                float sMax = travSizeMax[current];
                float sMaxSq = sMax * sMax;

                if (sMaxSq < thetaSq * dSq)
                {
                    
                    float distSq = dSq + localSofteningSq;
                    float invDist3 = Hlsl.Rsqrt(distSq * distSq * distSq);
                    acc += r * (gravity * mass * invDist3);
                    current = travNext[current];
                }
                else
                {
                    current = travLeft[current];
                }
            }
            else
            {
                Float4 com = travComMass[current];
                float m = com.W;
                if (m > 0f)
                {
                    Float3 r = com.XYZ - pi;
                    float distSq = Hlsl.Dot(r, r) + localSofteningSq;
                    float invDist3 = Hlsl.Rsqrt(distSq * distSq * distSq);
                    acc += r * (gravity * m * invDist3);
                }
                current = travNext[current];
            }
        }

        if (validCenter)
        {
            float haloRs = Hlsl.Max(haloScaleRadius, 1e-3f);
            float radius = Hlsl.Sqrt(rSqCenter + 1e-8f);
            float x = radius / haloRs;
            float nfwShape = x < 1e-3f
                ? x * x * (0.5f - 0.6666667f * x + 0.75f * x * x)
                : Hlsl.Log(1f + x) - x / (1f + x);
            const float nfwAtScaleRadius = 0.19314718f;
            float vScaleSq = haloV0Sq / nfwAtScaleRadius;
            float vHaloSq = vScaleSq * (nfwShape / Hlsl.Max(x, 1e-4f));
            acc -= relToCenter * (vHaloSq * massScale / Hlsl.Max(rSqCenter, 1e-8f));

            float bhDistSq = rSqCenter + bhSoftSq;
            float bhInv3 = Hlsl.Rsqrt(bhDistSq * bhDistSq * bhDistSq);
            acc -= relToCenter * (gravity * bhMass * massScale * bhInv3);
        }

        Float4 v = velocities[i];
        v.XYZ += acc * halfDt;
        velocities[i] = v;

        float speedSq = Hlsl.Dot(v.XYZ, v.XYZ);
        uint speedFixed = (uint)Hlsl.Min(speedSq * 1e6f, 4.2e9f);
        Hlsl.InterlockedMax(ref maxSpeedSqFixed[0], speedFixed);
    }
}
