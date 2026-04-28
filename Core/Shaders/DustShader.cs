using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct DustShader(
    ReadWriteBuffer<Float4> dustPositions,
    ReadWriteBuffer<uint> opacityBuffer,
    int dustCount,
    int screenW,
    int screenH,
    Float4x4 viewProj,
    float splatScale,
    float opacityScale,
    float haloV0,
    float haloRcSq,
    float dt,
    float time,
    float patchiness,
    float spiralStrength,
    float spiralPitch,
    float blobSuppression) : IComputeShader
{
    public void Execute()
    {
        int id = ThreadIds.X;
        if (id >= dustCount) return;

        Float4 p = dustPositions[id];
        Float3 pos = p.XYZ;

        float rXZsq = pos.X * pos.X + pos.Z * pos.Z;
        float rXZ = Hlsl.Sqrt(rXZsq + 1e-6f);
        float vCirc = haloV0 * rXZ / Hlsl.Sqrt(rXZsq + haloRcSq);
        float angVel = vCirc / rXZ;
        float sinDphi = Hlsl.Sin(angVel * dt);
        float cosDphi = Hlsl.Cos(angVel * dt);

        float newX = pos.X * cosDphi - pos.Z * sinDphi;
        float newZ = pos.X * sinDphi + pos.Z * cosDphi;
        pos = new Float3(newX, pos.Y, newZ);
        dustPositions[id] = new Float4(pos, p.W);

        Float3 timeOffset = new Float3(time * 0.03f, 0f, -time * 0.02f);
        float noise = ValueNoise3D((pos + timeOffset) * 0.45f) * 0.65f
                    + ValueNoise3D((pos - timeOffset) * 1.2f) * 0.35f;
        float lowBound = 1f - patchiness;        
        float patchMask = Hlsl.Lerp(lowBound, 1f, noise);

        float phi = Hlsl.Atan2(pos.Z, pos.X);
        float spiralPhase = spiralPitch * Hlsl.Log(rXZ + 0.1f) + time * (0.10f + 0.22f * spiralStrength);
        float armField = Hlsl.Cos(2f * phi - spiralPhase);  

        float armMask = armField * 0.5f + 0.5f;
        armMask = Hlsl.Pow(armMask, 2.5f);  
        float spiralFactor = Hlsl.Lerp(1f, armMask, spiralStrength);

        float suppressT = Hlsl.Saturate(blobSuppression);
        float outerEdge = SmoothStep(5.2f, 9.5f, rXZ);
        float compactNoise = Hlsl.Saturate((noise - 0.35f) / 0.65f);
        float edgeParticleKeep = 1f - suppressT * outerEdge * (0.68f + 0.22f * (1f - compactNoise));
        float finalOpacity = p.W * patchMask * spiralFactor * opacityScale * Hlsl.Saturate(edgeParticleKeep);
        if (finalOpacity < 0.01f) return;  


        Float4 clip = Hlsl.Mul(viewProj, new Float4(pos, 1f));
        if (clip.W <= 0.01f) return;

        Float3 ndc = clip.XYZ / clip.W;
        if (Hlsl.Abs(ndc.X) > 1f || Hlsl.Abs(ndc.Y) > 1f || ndc.Z < 0f || ndc.Z > 1f)
            return;

        int px = (int)((ndc.X * 0.5f + 0.5f) * screenW);
        int py = (int)((1f - (ndc.Y * 0.5f + 0.5f)) * screenH);

        int radius = (int)Hlsl.Clamp(splatScale / clip.W, 1f, 5f);
        float invSigmaSq = 1f / (radius * radius * 0.5f + 0.25f);

        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = px + dx, y = py + dy;
                if ((uint)x >= (uint)screenW || (uint)y >= (uint)screenH) continue;

                float falloff = Hlsl.Exp(-(dx * dx + dy * dy) * invSigmaSq);
                uint contrib = (uint)(finalOpacity * falloff * 4096f);
                int idx = y * screenW + x;

                Hlsl.InterlockedAdd(ref opacityBuffer[idx], contrib);
            }
    }

    private static float ValueNoise3D(Float3 p)
    {
        Float3 i = Hlsl.Floor(p);
        Float3 f = p - i;
        Float3 u = f * f * (new Float3(3f, 3f, 3f) - 2f * f);

        float n000 = Hash3(i + new Float3(0, 0, 0));
        float n100 = Hash3(i + new Float3(1, 0, 0));
        float n010 = Hash3(i + new Float3(0, 1, 0));
        float n110 = Hash3(i + new Float3(1, 1, 0));
        float n001 = Hash3(i + new Float3(0, 0, 1));
        float n101 = Hash3(i + new Float3(1, 0, 1));
        float n011 = Hash3(i + new Float3(0, 1, 1));
        float n111 = Hash3(i + new Float3(1, 1, 1));

        float nx00 = Hlsl.Lerp(n000, n100, u.X);
        float nx10 = Hlsl.Lerp(n010, n110, u.X);
        float nx01 = Hlsl.Lerp(n001, n101, u.X);
        float nx11 = Hlsl.Lerp(n011, n111, u.X);
        float nxy0 = Hlsl.Lerp(nx00, nx10, u.Y);
        float nxy1 = Hlsl.Lerp(nx01, nx11, u.Y);
        return Hlsl.Lerp(nxy0, nxy1, u.Z);
    }

    private static float Hash3(Float3 p)
    {
        float h = Hlsl.Dot(p, new Float3(127.1f, 311.7f, 74.7f));
        return Hlsl.Frac(Hlsl.Sin(h) * 43758.5453f);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Hlsl.Saturate((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
