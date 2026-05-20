using GalaxySim.Core.Camera;
using System.Numerics;

namespace GalaxySim.Core.Simulation;

public sealed record CameraState
{
    public float TargetX { get; init; }
    public float TargetY { get; init; }
    public float TargetZ { get; init; }
    public float Yaw { get; init; }
    public float Pitch { get; init; }
    public float Distance { get; init; }
    public float Fov { get; init; }

    public static CameraState FromCamera(OrbitCamera camera) => new()
    {
        TargetX = camera.Target.X,
        TargetY = camera.Target.Y,
        TargetZ = camera.Target.Z,
        Yaw = camera.Yaw,
        Pitch = camera.Pitch,
        Distance = camera.Distance,
        Fov = camera.Fov,
    };

    public void ApplyTo(OrbitCamera camera)
    {
        camera.Target = new Vector3(TargetX, TargetY, TargetZ);
        camera.Yaw = Yaw;
        camera.Pitch = Pitch;
        camera.Distance = Distance;
        camera.Fov = Fov;
    }
}
