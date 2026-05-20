using System;
using System.Numerics;

namespace GalaxySim.Core.Camera;

public sealed class OrbitCamera
{
    public Vector3 Target { get; set; } = Vector3.Zero;
    public float Yaw { get; set; } = 0f;
    public float Pitch { get; set; } = 0.6f;
    public float Distance { get; set; } = 18f;
    public float Fov { get; set; } = MathF.PI / 3f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 1000f;

    public float AspectRatio { get; set; } = 16f / 9f;

    public Matrix4x4 BuildViewProj()
    {
        Vector3 eye = GetEyePosition();

        var view = Matrix4x4.CreateLookAt(eye, Target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(Fov, AspectRatio, Near, Far);
        return view * proj;
    }

    public Vector3 GetEyePosition()
    {
        Pitch = Math.Clamp(Pitch, -1.55f, 1.55f);
        Distance = Math.Clamp(Distance, 1f, 500f);

        float cp = MathF.Cos(Pitch), sp = MathF.Sin(Pitch);
        float cy = MathF.Cos(Yaw), sy = MathF.Sin(Yaw);

        return Target + new Vector3(
            Distance * cp * sy,
            Distance * sp,
            Distance * cp * cy);
    }

    public void Orbit(float dx, float dy, float sensitivity = 0.005f)
    {
        Yaw -= dx * sensitivity;
        Pitch += dy * sensitivity;
    }

    public void Zoom(float delta) => Distance *= MathF.Pow(0.9f, delta);
}
