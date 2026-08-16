// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.XR;

using System;

using RedBolt.Math;
using RedBolt.Scene;

/// <summary>Helpers for common VR locomotion and controller-input gestures.</summary>
public static class VRUtils
{
    /// <summary>Calculates a motion vector based on the head.</summary>
    public static Vector3f SmoothLocomotionHead(Node? rigNode, XRBinding? joystickBinding, float deadZone,
        bool xzPlanar = true, bool normalized = true)
    {
        if (rigNode is null || joystickBinding is null)
            return Vector3f.Zero;

        var leftEye = rigNode.GetChild("Left_Eye", true);
        var rightEye = rigNode.GetChild("Right_Eye", true);
        if (leftEye is null || rightEye is null)
            return Vector3f.Zero;

        // Head transform is always identity.
        var forward = leftEye.GetWorldDirection() + rightEye.GetWorldDirection();
        var right = leftEye.GetWorldRight() + rightEye.GetWorldRight();

        if (xzPlanar)
        {
            forward.Y = 0;
            right.Y = 0;
        }

        forward.Normalize();
        right.Normalize();

        var stickMotion = joystickBinding.GetVector2();
        var signX = MathDefs.Sign(stickMotion.X);
        var signY = MathDefs.Sign(stickMotion.Y);
        stickMotion.X = MathF.Abs(stickMotion.X) < deadZone
            ? 0.0f
            : signX * MathDefs.InverseLerp(deadZone, 1.0f, MathF.Abs(stickMotion.X));
        stickMotion.Y = MathF.Abs(stickMotion.Y) < deadZone
            ? 0.0f
            : signY * MathDefs.InverseLerp(deadZone, 1.0f, MathF.Abs(stickMotion.Y));

        var vector = forward * stickMotion.Y + right * stickMotion.X;
        return normalized ? vector.Normalized : vector;
    }

    /// <summary>Calculates a motion vector based on a controller aim direction.</summary>
    public static Vector3f SmoothLocomotionAim(Node? rigNode, XRBinding? joystickBinding, VRHand hand,
        float deadZone, bool xzPlanar = true, bool normalized = false)
    {
        if (rigNode is null || joystickBinding is null)
            return Vector3f.Zero;

        var virtualReality = rigNode.GetSubsystem<VirtualReality>();
        var transform = virtualReality.GetHandAimTransform(hand);
        var forward = transform * Vector3f.Forward;
        var right = transform * Vector3f.Right;

        if (xzPlanar)
        {
            forward.Y = 0;
            right.Y = 0;
        }

        forward.Normalize();
        right.Normalize();

        var stickMotion = joystickBinding.GetVector2();
        var signX = MathDefs.Sign(stickMotion.X);
        var signY = MathDefs.Sign(stickMotion.X);
        stickMotion.X = MathF.Abs(stickMotion.X) < deadZone
            ? 0.0f
            : signX * Denormalize(MathF.Abs(stickMotion.X), deadZone, 1.0f);
        stickMotion.Y = MathF.Abs(stickMotion.Y) < deadZone
            ? 0.0f
            : signY * Denormalize(MathF.Abs(stickMotion.Y), deadZone, 1.0f);

        var vector = forward * stickMotion.Y + right * stickMotion.X;
        return normalized ? vector.Normalized : vector;
    }

    /// <summary>Compares old and new positions of the given node to calculate a motion vector.</summary>
    public static Vector3f GrabLocomotion(Node? handNode)
    {
        if (handNode is null)
            return Vector3f.Zero;

        var newPosition = handNode.WorldPosition;
        if (handNode.GetVar("LastTransformWS") is Matrix3x4 oldTransform)
            return newPosition - oldTransform.Translation;
        return Vector3f.Zero;
    }

    /// <summary>Treats the trackpad as a four-button D-pad with an optional center.</summary>
    public static int TrackpadAsDPad(XRBinding trackpadPosition, XRBinding trackpadClick, float centerRadius) =>
        TrackpadAsDPad(trackpadPosition, trackpadClick, centerRadius, out _);

    /// <summary>Treats the trackpad as a four-button D-pad and reports whether the trackpad is down.</summary>
    public static int TrackpadAsDPad(XRBinding trackpadPosition, XRBinding trackpadClick, float centerRadius,
        out bool trackpadDown)
    {
        trackpadDown = false;
        if (!trackpadClick.IsActive || !trackpadClick.IsBound)
            return 0;
        if (!trackpadPosition.IsActive || !trackpadPosition.IsBound)
            return 0;

        trackpadDown = trackpadClick.GetBool();
        if (trackpadClick.GetBool())
        {
            var position = trackpadPosition.GetVector2();
            if (position.X < centerRadius && position.X > -centerRadius &&
                position.Y < centerRadius && position.Y > -centerRadius)
                return 5; // Center.

            if (MathF.Abs(position.X) > MathF.Abs(position.Y)) // Left/right.
                return position.X > 0 ? 2 : 4;
            return position.Y > 0 ? 1 : 3;
        }
        return 0;
    }

    /// <summary>Treats the joystick as a D-pad.</summary>
    public static int JoystickAsDPad(XRBinding joystickPosition, float centerDeadzone)
    {
        if (!joystickPosition.IsActive)
            return 0;

        var position = joystickPosition.GetVector2();
        if (MathF.Abs(position.X) < centerDeadzone && MathF.Abs(position.Y) < centerDeadzone)
            return 0; // Inside deadzone.

        if (MathF.Abs(position.X) > MathF.Abs(position.Y)) // Left/right.
            return position.X > 0 ? 2 : 4;
        return position.Y > 0 ? 1 : 3;
    }

    /// <summary>Treats the trackpad as two buttons.</summary>
    public static int TrackpadAsTwoButton(XRBinding trackpadPosition, XRBinding trackpadClick,
        float centerDeadzone, VRHand hand, bool upDownMode = false) =>
        TrackpadAsTwoButton(trackpadPosition, trackpadClick, centerDeadzone, hand, upDownMode, out _);

    /// <summary>Treats the trackpad as two buttons and reports whether the trackpad is down.</summary>
    public static int TrackpadAsTwoButton(XRBinding trackpadPosition, XRBinding trackpadClick,
        float centerDeadzone, VRHand hand, bool upDownMode, out bool trackpadDown)
    {
        trackpadDown = false;
        if (!trackpadClick.IsActive || !trackpadClick.IsBound)
            return 0;
        if (!trackpadPosition.IsActive || !trackpadPosition.IsBound)
            return 0;

        trackpadDown = trackpadClick.GetBool();
        var position = trackpadPosition.GetVector2();
        if (MathF.Abs(position.X) < centerDeadzone && MathF.Abs(position.Y) < centerDeadzone)
            return 0;

        if (trackpadClick.GetBool())
        {
            if (upDownMode)
                return position.Y > 0 ? 1 : 2;

            if (position.Y > 0 && position.Y > MathF.Abs(position.X))
                return 1;
            if (hand == VRHand.Left && position.X > 0)
                return 2;
            if (hand == VRHand.Right && position.X < 0)
                return 2;
        }
        return 0;
    }

    /// <summary>Returns true when the target code transitions into a released state.</summary>
    public static bool ButtonClicked(int targetCode, ref int currentCode, int nextCode)
    {
        if (currentCode == targetCode && currentCode != nextCode && nextCode == 0)
        {
            currentCode = nextCode;
            return true;
        }

        currentCode = nextCode;
        return false;
    }

    /// <summary>Returns true once a held button reaches the requested duration.</summary>
    public static bool ButtonLongPress(int targetCode, ref int currentCode, ref float time,
        ref bool alreadyDone, int nextCode, float deltaTime, float holdForDuration) =>
        ButtonLongPress(targetCode, ref currentCode, ref time, ref alreadyDone, nextCode, deltaTime,
            holdForDuration, out _);

    /// <summary>Returns true once a held button reaches the requested duration and reports progress.</summary>
    public static bool ButtonLongPress(int targetCode, ref int currentCode, ref float time,
        ref bool alreadyDone, int nextCode, float deltaTime, float holdForDuration, out float fraction)
    {
        fraction = 0.0f;
        if (currentCode != nextCode)
        {
            currentCode = nextCode;
            time = 0.0f;
            alreadyDone = false;
            return false;
        }

        if (currentCode == nextCode && nextCode == targetCode)
        {
            if (alreadyDone)
                return false;

            time += deltaTime;
            if (time > holdForDuration)
            {
                time = 0.0f;
                alreadyDone = true;
                fraction = 1.0f;
                return true;
            }

            fraction = time / holdForDuration;
        }
        return false;
    }

    /// <summary>Returns one for a short press and two for a long press.</summary>
    public static int ButtonShortOrLongPress(int targetCode, ref int currentCode, ref float time,
        ref bool alreadyDone, int nextCode, float deltaTime, float holdForDuration, float shortPressWindow) =>
        ButtonShortOrLongPress(targetCode, ref currentCode, ref time, ref alreadyDone, nextCode,
            deltaTime, holdForDuration, shortPressWindow, out _, out _);

    /// <summary>Returns one for a short press and two for a long press, with long-press state and progress.</summary>
    public static int ButtonShortOrLongPress(int targetCode, ref int currentCode, ref float time,
        ref bool alreadyDone, int nextCode, float deltaTime, float holdForDuration, float shortPressWindow,
        out bool inLongPress, out float fraction)
    {
        fraction = 0.0f;
        inLongPress = false;

        // Code is changing.
        if (currentCode != nextCode)
        {
            var returnValue = targetCode == currentCode ? 1 : 0;
            if (shortPressWindow != 0 && time > shortPressWindow && currentCode == targetCode)
                returnValue = 0;

            currentCode = nextCode;
            time = 0.0f;
            alreadyDone = false;
            return 0;
        }

        if (currentCode == nextCode && nextCode == targetCode)
        {
            if (alreadyDone)
                return 0;

            time += deltaTime;
            if (time > shortPressWindow)
                inLongPress = true;

            if (time > holdForDuration)
            {
                time = 0.0f;
                alreadyDone = true;
                inLongPress = false;
                fraction = 1.0f;
                return 2;
            }

            if (holdForDuration != shortPressWindow)
                fraction = time >= shortPressWindow
                    ? (time - shortPressWindow) / (holdForDuration - shortPressWindow)
                    : 0.0f;
        }
        return 0;
    }

    /// <summary>Returns one for a tap and two while held beyond the short-press window.</summary>
    public static int ButtonTapOrHold(int targetCode, ref int currentCode, ref float time,
        int nextCode, float deltaTime, float shortPressWindow)
    {
        if (currentCode == targetCode && currentCode != nextCode && time < shortPressWindow)
        {
            time = 0.0f;
            currentCode = nextCode;
            return 1;
        }

        if (targetCode == nextCode && currentCode == targetCode)
        {
            if (time > shortPressWindow)
            {
                time += deltaTime;
                return 2;
            }
            time += deltaTime;
        }
        else
        {
            time = 0.0f;
            currentCode = nextCode;
        }
        return 0;
    }

    private static float Denormalize(float value, float minimum, float maximum) =>
        value * (maximum - minimum) + minimum;
}

/// <summary>State helper for the variable-coded button-press functions.</summary>
public struct ButtonCommand
{
    public int TargetCode;
    public float ShortPressWindow;
    public float HoldDuration;

    public int CurrentCode;
    public float Time;
    public bool AlreadyDone;
    public float Fraction;

    public ButtonCommand(int targetCode, float holdDuration = 3.0f, float shortPressWindow = 1.0f)
    {
        TargetCode = targetCode;
        HoldDuration = holdDuration;
        ShortPressWindow = shortPressWindow;
    }

    public void Reset()
    {
        CurrentCode = 0;
        Time = 0.0f;
        Fraction = 0.0f;
        AlreadyDone = false;
    }

    public bool CheckDown(int newCode)
    {
        CurrentCode = newCode;
        return CurrentCode == TargetCode;
    }

    public bool CheckStrict(int newCode)
    {
        if (TargetCode == newCode && CurrentCode != newCode)
        {
            AlreadyDone = true;
            CurrentCode = newCode;
            return true;
        }
        if (newCode == 0)
        {
            AlreadyDone = false;
            CurrentCode = newCode;
        }
        else
            CurrentCode = newCode;
        return false;
    }

    public bool CheckClick(int newCode) => VRUtils.ButtonClicked(TargetCode, ref CurrentCode, newCode);

    public bool CheckLongPress(int newCode, float deltaTime) =>
        VRUtils.ButtonLongPress(TargetCode, ref CurrentCode, ref Time, ref AlreadyDone, newCode,
            deltaTime, HoldDuration, out Fraction);

    public int CheckShortOrLongPress(int newCode, float deltaTime)
    {
        var result = VRUtils.ButtonShortOrLongPress(TargetCode, ref CurrentCode, ref Time, ref AlreadyDone,
            newCode, deltaTime, HoldDuration, ShortPressWindow, out _, out var fraction);
        Fraction = fraction;
        return result;
    }

    public int CheckTapOrHold(int newCode, float deltaTime) =>
        VRUtils.ButtonTapOrHold(TargetCode, ref CurrentCode, ref Time, newCode, deltaTime, ShortPressWindow);
}
