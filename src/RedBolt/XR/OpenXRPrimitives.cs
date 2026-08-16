// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.XR;

using System;
using System.Text;

using Microsoft.Extensions.Logging;

using RedBolt.Bindings.OpenXR;
using RedBolt.Logging;
using RedBolt.Math;

using SharpCpp.Support;

using XrApi = RedBolt.Bindings.OpenXR.OpenXR;

internal static class OpenXRPrimitives
{
    public const long InfiniteDuration = long.MaxValue;
    public const ulong CurrentApiVersion = 0x0001_0000_0000_001dUL;

    public static bool Check(XrResult result, string operation)
    {
        if ((int)result >= 0)
            return true;

        Log.GetLogger().LogError("OpenXR operation {Operation} failed with {Result}", operation, result);
        return false;
    }

    public static byte[] Utf8Z(string value)
    {
        var result = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, result);
        return result;
    }

    public static void SetUtf8(global::SharpCpp.NativeArray<sbyte> target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var count = Math.Min(bytes.Length, target.Length - 1);
        for (var i = 0; i < count; ++i)
            target[i] = unchecked((sbyte)bytes[i]);
        for (var i = count; i < target.Length; ++i)
            target[i] = 0;
    }

    public static string GetUtf8(global::SharpCpp.NativeArray<sbyte> source)
    {
        var count = 0;
        while (count < source.Length && source[count] != 0)
            ++count;
        var bytes = new byte[count];
        for (var i = 0; i < count; ++i)
            bytes[i] = unchecked((byte)source[i]);
        return Encoding.UTF8.GetString(bytes);
    }

    public static Vector3f ToVector3(float x, float y, float z) => new(x, y, -z);

    public static Quaternion ToQuaternion(float x, float y, float z, float w) => new(w, -x, -y, z);

    public static Matrix3x4 ToMatrix3x4(XrPosef pose, float scale) =>
        new(ToVector3(pose.PositionX, pose.PositionY, pose.PositionZ),
            ToQuaternion(pose.OrientationX, pose.OrientationY, pose.OrientationZ, pose.OrientationW), scale);

    public static Matrix4 ToProjection(float nearDistance, float farDistance, XrFovf fov)
    {
        var tanLeft = MathF.Tan(fov.AngleLeft);
        var tanRight = MathF.Tan(fov.AngleRight);
        var tanDown = MathF.Tan(fov.AngleDown);
        var tanUp = MathF.Tan(fov.AngleUp);
        var tanAngleWidth = tanRight - tanLeft;
        var tanAngleHeight = tanUp - tanDown;
        var q = farDistance / (farDistance - nearDistance);
        var r = -q * nearDistance;

        var projection = Matrix4.Zero;
        projection.M00 = 2.0f / tanAngleWidth;
        projection.M11 = 2.0f / tanAngleHeight;
        projection.M02 = -(tanRight + tanLeft) / tanAngleWidth;
        projection.M12 = -(tanUp + tanDown) / tanAngleHeight;
        projection.M22 = q;
        projection.M23 = r;
        projection.M32 = 1.0f;
        return projection;
    }

    public static XrPosef CreateIdentityPose()
    {
        var pose = new XrPosef { OrientationW = 1.0f };
        return pose;
    }
}

internal sealed class OpenXROwnedHandle<T> : IDisposable where T : NativeObject
{
    private readonly Func<T, XrResult> _destroy;

    public OpenXROwnedHandle(T value, Func<T, XrResult> destroy)
    {
        Value = value;
        _destroy = destroy;
    }

    public T? Value { get; private set; }

    public bool IsValid => Value is not null && !Value.IsClosed && !Value.IsInvalid;

    public void Dispose()
    {
        if (Value is not { } value)
            return;

        _ = _destroy(value);
        value.Dispose();
        Value = null;
    }
}
