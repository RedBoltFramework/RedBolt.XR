// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.XR;

using System;
using System.Collections.Generic;

using RedBolt.Bindings.OpenXR;
using RedBolt.Core;
using RedBolt.Math;

using SharpCpp.Support;

using XrApi = RedBolt.Bindings.OpenXR.OpenXR;

/// <summary>Implementation of XRBinding for OpenXR.</summary>
internal sealed class OpenXRBinding : XRBinding, IDisposable
{
    private readonly XrActionStateGetInfo _getInfo;
    private readonly XrActionStateBoolean _boolState;
    private readonly XrActionStateFloat _floatState;
    private readonly XrActionStateVector2f _vectorState;
    private readonly XrActionStatePose _poseState;
    private OpenXROwnedHandle<XrSpaceT>? _actionSpace;

    public OpenXRBinding(string name, string localizedName, VRHand hand, VariantType dataType,
        bool isPose, bool isAimPose, XrActionSetT set, OpenXROwnedHandle<XrActionT> action,
        ulong subPath, OpenXROwnedHandle<XrSpaceT>? actionSpace)
        : base(name, localizedName, hand, dataType, isPose, isAimPose)
    {
        Set = set;
        Action = action;
        SubPath = subPath;
        _actionSpace = actionSpace;
        StoredData = new Variant(dataType);

        _getInfo = new XrActionStateGetInfo
        {
            Type = XrStructureType.XrTypeActionStateGetInfo,
            Action = action.Value!,
            SubactionPath = subPath
        };
        _boolState = new XrActionStateBoolean { Type = XrStructureType.XrTypeActionStateBoolean };
        _floatState = new XrActionStateFloat { Type = XrStructureType.XrTypeActionStateFloat };
        _vectorState = new XrActionStateVector2f { Type = XrStructureType.XrTypeActionStateVector2f };
        _poseState = new XrActionStatePose { Type = XrStructureType.XrTypeActionStatePose };
        Velocity = new XrSpaceVelocity { Type = XrStructureType.XrTypeSpaceVelocity };
        Location = new XrSpaceLocation
        {
            Type = XrStructureType.XrTypeSpaceLocation,
            Next = Velocity.Handle
        };
    }

    /// <summary>Owning ActionSet that contains this action.</summary>
    public XrActionSetT Set { get; }
    /// <summary>Action itself, possibly shared in the case of sub-path handed actions.</summary>
    public OpenXROwnedHandle<XrActionT> Action { get; }
    /// <summary>Indicates handed-ness for the OXR query.</summary>
    public ulong SubPath { get; }
    /// <summary>If this is a space action, return its action space.</summary>
    public XrSpaceT? ActionSpace => _actionSpace?.Value;

    /// <summary>Position and orientation from space location.</summary>
    public XrSpaceLocation Location { get; }
    /// <summary>Linear and angular velocity from space location.</summary>
    public XrSpaceVelocity Velocity { get; }

    public void Update(XrSessionT session, float scaleCorrection)
    {
        if (!Action.IsValid || IsHaptic)
            return;

        switch (ExpectedType)
        {
        case VariantType.Bool:
            if (OpenXRPrimitives.Check(XrApi.XrGetActionStateBoolean(session, _getInfo, _boolState),
                    nameof(XrApi.XrGetActionStateBoolean)))
            {
                IsChanged = _boolState.ChangedSinceLastSync != 0;
                IsActive = _boolState.IsActive != 0;
                StoredData = _boolState.CurrentState != 0;
            }
            break;

        case VariantType.Float:
            if (OpenXRPrimitives.Check(XrApi.XrGetActionStateFloat(session, _getInfo, _floatState),
                    nameof(XrApi.XrGetActionStateFloat)))
            {
                IsChanged = _floatState.ChangedSinceLastSync != 0 ||
                    !MathDefs.Equals(_floatState.CurrentState, GetFloat());
                IsActive = _floatState.IsActive != 0;
                StoredData = _floatState.CurrentState;
            }
            break;

        case VariantType.Vector2:
            if (OpenXRPrimitives.Check(XrApi.XrGetActionStateVector2f(session, _getInfo, _vectorState),
                    nameof(XrApi.XrGetActionStateVector2f)))
            {
                IsChanged = _vectorState.ChangedSinceLastSync != 0;
                IsActive = _vectorState.IsActive != 0;
                StoredData = new Vector2f(_vectorState.CurrentStateX, _vectorState.CurrentStateY);
            }
            break;

        case VariantType.Vector3:
            if (OpenXRPrimitives.Check(XrApi.XrGetActionStatePose(session, _getInfo, _poseState),
                    nameof(XrApi.XrGetActionStatePose)))
            {
                IsChanged = true;
                IsActive = _poseState.IsActive != 0;
                StoredData = OpenXRPrimitives.ToVector3(
                    Location.PosePositionX, Location.PosePositionY, Location.PosePositionZ) * scaleCorrection;
                LinearVelocity = OpenXRPrimitives.ToVector3(
                    Velocity.LinearVelocityX, Velocity.LinearVelocityY, Velocity.LinearVelocityZ) * scaleCorrection;
            }
            break;

        case VariantType.Matrix3x4:
            if (OpenXRPrimitives.Check(XrApi.XrGetActionStatePose(session, _getInfo, _poseState),
                    nameof(XrApi.XrGetActionStatePose)))
            {
                IsChanged = true;
                IsActive = _poseState.IsActive != 0;
                StoredData = ToMatrix3x4(Location, scaleCorrection);
                LinearVelocity = OpenXRPrimitives.ToVector3(
                    Velocity.LinearVelocityX, Velocity.LinearVelocityY, Velocity.LinearVelocityZ) * scaleCorrection;
                AngularVelocity = OpenXRPrimitives.ToVector3(
                    Velocity.AngularVelocityX, Velocity.AngularVelocityY, Velocity.AngularVelocityZ) * scaleCorrection;
            }
            break;
        }

        // Send events for changed bindings, except spatial bindings which effectively always change.
        if (IsChanged && ExpectedType != VariantType.Vector3 && ExpectedType != VariantType.Matrix3x4)
        {
            var eventData = new VREvents.VRBindingChangedEvent { Binding = this };
            SendEvent(ref eventData);
        }
    }

    public void UpdateBoundState(XrSessionT session)
    {
        using var info = new XrBoundSourcesForActionEnumerateInfo
        {
            Type = XrStructureType.XrTypeBoundSourcesForActionEnumerateInfo,
            Action = Action.Value!
        };
        _ = XrApi.XrEnumerateBoundSourcesForAction(session, info, 0, out var numSources, []);
        IsBound = numSources > 0;
    }

    public void Locate(XrSpaceT baseSpace, long predictedTime)
    {
        if (ActionSpace is { } actionSpace)
            _ = XrApi.XrLocateSpace(actionSpace, baseSpace, predictedTime, Location);
    }

    public override void Dispose()
    {
        Location.Dispose();
        Velocity.Dispose();
        _poseState.Dispose();
        _vectorState.Dispose();
        _floatState.Dispose();
        _boolState.Dispose();
        _getInfo.Dispose();
        _actionSpace?.Dispose();
        _actionSpace = null;
        base.Dispose();
    }

    private static Matrix3x4 ToMatrix3x4(XrSpaceLocation location, float scale) =>
        new(OpenXRPrimitives.ToVector3(location.PosePositionX, location.PosePositionY, location.PosePositionZ),
            OpenXRPrimitives.ToQuaternion(location.PoseOrientationX, location.PoseOrientationY,
                location.PoseOrientationZ, location.PoseOrientationW), scale);
}

/// <summary>Implementation of XRActionGroup for OpenXR.</summary>
internal sealed class OpenXRActionGroup : XRActionGroup, IDisposable
{
    private readonly List<OpenXROwnedHandle<XrActionT>> _actions = [];
    private OpenXROwnedHandle<XrActionSetT>? _actionSet;

    public OpenXRActionGroup(string name, string localizedName, OpenXROwnedHandle<XrActionSetT> actionSet)
        : base(name, localizedName)
    {
        _actionSet = actionSet;
    }

    public XrActionSetT ActionSet => _actionSet!.Value!;

    public void AddAction(OpenXROwnedHandle<XrActionT> action) => _actions.Add(action);

    public void AddBinding(OpenXRBinding binding) => Bindings.Add(binding);

    public OpenXRBinding? FindBindingImpl(string name) => FindBinding(name, VRHand.None) as OpenXRBinding;

    public unsafe void AttachToSession(XrSessionT session)
    {
        Span<nint> actionSetHandles = stackalloc nint[1] { NativeObject.GetHandle(ActionSet) };
        fixed (nint* actionSetHandlesPtr = actionSetHandles)
        {
            using var actionSetArray = XrActionSetT.CreateManagedWrapper((nint)actionSetHandlesPtr, false);
            using var attachInfo = new XrSessionActionSetsAttachInfo
            {
                Type = XrStructureType.XrTypeSessionActionSetsAttachInfo,
                ActionSets = actionSetArray,
                CountActionSets = 1
            };
            _ = OpenXRPrimitives.Check(XrApi.XrAttachSessionActionSets(session, attachInfo),
                nameof(XrApi.XrAttachSessionActionSets));
        }
    }

    public void Synchronize(XrSessionT session)
    {
        using var activeSets = new global::SharpCpp.NativeArray<XrActiveActionSet>(1);
        using var activeSet = new XrActiveActionSet { ActionSet = ActionSet };
        activeSets[0] = activeSet;
        using var activeSetItem = activeSets[0];
        using var sync = new XrActionsSyncInfo
        {
            Type = XrStructureType.XrTypeActionsSyncInfo,
            ActiveActionSets = activeSetItem,
            CountActiveActionSets = 1
        };
        _ = OpenXRPrimitives.Check(XrApi.XrSyncActions(session, sync), nameof(XrApi.XrSyncActions));
    }

    public override void Dispose()
    {
        foreach (var binding in Bindings)
            (binding as IDisposable)?.Dispose();
        Bindings.Clear();
        foreach (var action in _actions)
            action.Dispose();
        _actions.Clear();
        _actionSet?.Dispose();
        _actionSet = null;
        base.Dispose();
    }
}
