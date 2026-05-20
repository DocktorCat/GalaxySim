namespace GalaxySim.Core.Physics;

public struct GalaxyParams
{
    public float Gravity;
    public float Softening;


    public float HaloV0;
    public float HaloCoreRadius;

    public float BlackHoleMass;
    public float BlackHoleSoftening;

    public float MaxDt;
    public float CourantFactor;    

    public float Theta;

    public static GalaxyParams Default() => new()
    {
        Gravity = 0.5f,
        Softening = 0.08f,
        HaloV0 = 1.6f,
        HaloCoreRadius = 1.2f,
        BlackHoleMass = 4.0f,
        BlackHoleSoftening = 0.15f,
        MaxDt = 0.008f,
        CourantFactor = 0.2f,
        Theta = 0.8f,
    };
}
