// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.XR;

using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using RedBolt.Core;
using RedBolt.Graphics;
using RedBolt.IO;
using RedBolt.Logging;
using RedBolt.Math;
using RedBolt.RenderPipeline;
using RedBolt.Resource;
using RedBolt.Scene;

/// <summary>Parameters for initializing a VR session.</summary>
public struct VRSessionParameters
{
    public string ManifestPath = string.Empty;
    public int MultiSample;
    public float ResolutionScale = 1.0f;

    public VRSessionParameters()
    {
    }
}

/// <summary>Description of VR rig that links VR subsystem to the scene.</summary>
public struct VRRigDesc
{
    public Viewport? Viewport;
    public StereoRenderPipeline? Pipeline;
    public WeakReference<Scene>? Scene;
    public WeakReference<Node>? Head;
    public WeakReference<Camera>? LeftEye;
    public WeakReference<Camera>? RightEye;
    public WeakReference<Node>? LeftHandPose;
    public WeakReference<Node>? RightHandPose;
    public WeakReference<Node>? LeftHandAim;
    public WeakReference<Node>? RightHandAim;
    public WeakReference<Node>? LeftController;
    public WeakReference<Node>? RightController;
    public float NearDistance;
    public float FarDistance;

    public readonly bool IsValid() =>
        TryGet(Scene) is not null && TryGet(Head) is not null &&
        TryGet(LeftEye) is not null && TryGet(RightEye) is not null &&
        TryGet(LeftHandPose) is not null && TryGet(RightHandPose) is not null &&
        TryGet(LeftHandAim) is not null && TryGet(RightHandAim) is not null;

    internal static T? TryGet<T>(WeakReference<T>? reference) where T : class =>
        reference is not null && reference.TryGetTarget(out var value) ? value : null;
}

/// <summary>Backend implementation of VirtualReality interface.</summary>
public enum VRRuntime
{
    OpenXR,
}

/// <summary>Hand ID.</summary>
public enum VRHand
{
    None = -1,
    Left = 0,
    Right = 1,
    Count
}

/// <summary>Eye ID.</summary>
public enum VREye
{
    None = -1,
    Left = 0,
    Right = 1,
    Count
}

/// <summary>Wraps an input binding. Subclassed as required by interface implementations.</summary>
public class XRBinding : RedObject
{
    public XRBinding(string name, string localizedName, VRHand hand, VariantType dataType, bool isPose, bool isAimPose)
    {
        Name = name;
        LocalizedName = localizedName;
        Hand = hand;
        ExpectedType = dataType;
        IsHaptic = dataType == VariantType.None;
        IsGripPose = isPose;
        IsAimPose = isAimPose;
    }

    public string Name { get; }
    public string LocalizedName { get; }

    /// <summary>Returns true if this action has changed state since the last update.</summary>
    public bool IsChanged { get; protected set; }
    /// <summary>Returns true if this action is actively being used.</summary>
    public bool IsActive { get; protected set; }
    /// <summary>Returns true if this action is bound to a hand.</summary>
    public bool IsHanded => Hand != VRHand.None;
    /// <summary>Returns the hand this action is bound to.</summary>
    public VRHand Hand { get; }

    /// <summary>Variant accessors.</summary>
    /// <remarks>Float bindings may be interpreted as buttons with a caller-selected press threshold.</remarks>
    public bool GetBool(float pressThreshold) => StoredData.GetFloat() > pressThreshold;
    public bool GetBool() => StoredData.GetBool();
    public float GetFloat() => StoredData.GetFloat();
    public Vector2f GetVector2() => StoredData.GetVector2();
    public Vector3f GetVector3() => StoredData.GetVector3();
    public Vector3f GetPosition() => StoredData.GetMatrix3x4().Translation;
    public Quaternion GetRotation() => StoredData.GetMatrix3x4().Rotation;
    public Matrix3x4 GetTransformMatrix() => StoredData.GetMatrix3x4();
    public VariantType ExpectedType { get; }

    /// <summary>Returns stored variant value.</summary>
    public Variant Data => StoredData;
    /// <summary>Returns linear velocity of the pose.</summary>
    public Vector3f LinearVelocity { get; protected set; }
    /// <summary>Returns angular velocity of the pose.</summary>
    public Vector3f AngularVelocity { get; protected set; }

    /// <summary>Returns true if this action is bound as a live input possibility.</summary>
    public bool IsBound { get; protected set; }

    /// <summary>Returns true if this is an input method action.</summary>
    public bool IsInput => !IsHaptic;
    /// <summary>Returns true if this is an output haptic action.</summary>
    public bool IsHaptic { get; }
    /// <summary>Return true if this action is hand grip pose.</summary>
    public bool IsGripPose { get; }
    /// <summary>Return true if this action is hand aim pose.</summary>
    public bool IsAimPose { get; }

    protected Variant StoredData;
}

/// <summary>Represents a logical action set in the underlying APIs.</summary>
public class XRActionGroup : RedObject
{
    public XRActionGroup(string name, string localizedName)
    {
        Name = name;
        LocalizedName = localizedName;
    }

    /// <summary>Find binding by name, case insensitive.</summary>
    public XRBinding? FindBinding(string name, VRHand hand)
    {
        foreach (var binding in Bindings)
        {
            if (string.Equals(binding.Name, name, StringComparison.OrdinalIgnoreCase) &&
                (hand == VRHand.None || hand == binding.Hand))
                return binding;
        }
        return null;
    }

    /// <summary>Return all bindings.</summary>
    public IReadOnlyList<XRBinding> GetBindings() => Bindings;

    /// <summary>Identifier of this action set.</summary>
    public string Name { get; }
    /// <summary>Localized identifier.</summary>
    public string LocalizedName { get; }

    /// <summary>Contained action bindings.</summary>
    protected List<XRBinding> Bindings { get; } = [];
}

/// <summary>Base interface for a VR related subsystem.</summary>
public abstract class VirtualReality : RedObject
{
    /// <summary>Initializes the VR session.</summary>
    public abstract bool InitializeSession(in VRSessionParameters parameters);
    /// <summary>Shuts down the VR session.</summary>
    public abstract void ShutdownSession();
    /// <summary>Connects session to the rig.</summary>
    public virtual void ConnectToRig(in VRRigDesc rig)
    {
        if (!rig.IsValid())
        {
            Log.GetLogger().LogError("Invalid VR rig description");
            return;
        }

        Rig = rig;
        var scene = VRRigDesc.TryGet(Rig.Scene)!;
        var leftEye = VRRigDesc.TryGet(Rig.LeftEye)!;
        var rightEye = VRRigDesc.TryGet(Rig.RightEye)!;

        Rig.Pipeline ??= scene.GetDerivedComponent<StereoRenderPipeline>();
        if (Rig.Pipeline is null)
        {
            Rig.Pipeline = scene.CreateComponent<StereoRenderPipeline>();

            // Note: Render paths are not generally compatible between standard and stereo rendering.
            // Render path settings are not copied.
            if (scene.GetComponent<RenderPipeline>() is { } sourcePipeline)
                Rig.Pipeline.SetSettings(sourcePipeline.Settings);
        }
        Rig.Viewport ??= new Viewport(scene, leftEye, rightEye, Rig.Pipeline);

        Rig.Viewport.SetEye(leftEye, 0);
        Rig.Viewport.SetEye(rightEye, 1);
    }

    /// <summary>Returns true if this VR configuration is running at room scale.</summary>
    public bool IsRoomScale { get; protected set; }
    /// <summary>Returns currently connected rig.</summary>
    public ref readonly VRRigDesc GetRig() => ref Rig;

    /// <summary>IPD correction factor in millimeters.</summary>
    public float IPDCorrection { get; set; }
    /// <summary>Scale correction factor, premultiplied into all transforms.</summary>
    public float ScaleCorrection { get; set; } = 1.0f;

    /// <summary>Returns recommended MSAA level.</summary>
    public int RecommendedMultiSample { get; protected set; } = 1;
    /// <summary>Returns the currently chosen MSAA level.</summary>
    public int MultiSample { get; protected set; }

    /// <summary>Viewport rectangle for left eye, required for multipass single-RT.</summary>
    public IntRect GetLeftEyeRect() => new(0, 0, EyeTextureSize.X, EyeTextureSize.Y);
    /// <summary>Viewport rectangle for right eye, required for multipass single-RT.</summary>
    public IntRect GetRightEyeRect() => new(EyeTextureSize.X, 0, EyeTextureSize.X * 2, EyeTextureSize.Y);

    public abstract VRRuntime Runtime { get; }
    public abstract string RuntimeName { get; }

    public abstract void TriggerHaptic(VRHand hand, float durationSeconds, float cyclesPerSecond, float amplitude);
    public abstract Matrix3x4 GetHandTransform(VRHand hand);
    public abstract Matrix3x4 GetHandAimTransform(VRHand hand);
    public abstract Ray GetHandAimRay(VRHand hand);
    public abstract void GetHandVelocity(VRHand hand, out Vector3f linear, out Vector3f angular);
    public abstract Matrix3x4 GetHeadTransform();
    public abstract Matrix3x4 GetEyeLocalTransform(VREye eye);
    public abstract Matrix4 GetProjection(VREye eye, float nearDistance, float farDistance);

    public abstract bool IsConnected { get; }
    public abstract bool IsRunning { get; }
    public abstract bool IsVisible { get; }
    public abstract bool IsFocused { get; }

    public XRBinding? GetInputBinding(string path) => ActiveActionSet?.FindBinding(path, VRHand.None);
    public XRBinding? GetInputBinding(string path, VRHand hand) => ActiveActionSet?.FindBinding(path, hand);
    public XRActionGroup? GetCurrentActionSet() => ActiveActionSet;

    public virtual void SetCurrentActionSet(string setName)
    {
        if (ActionSets.TryGetValue(setName, out var actionSet))
            SetCurrentActionSet(actionSet);
    }

    public abstract void SetCurrentActionSet(XRActionGroup actionSet);

    /// <summary>Returns the system name, for example Windows Mixed Reality.</summary>
    public string SystemName { get; protected set; } = string.Empty;

    protected void CreateDefaultRig()
    {
        var cache = GetSubsystem<ResourceCache>();

        DefaultScene = new Scene();
        DefaultScene.CreateComponent<Octree>();

        var skyboxNode = DefaultScene.CreateChild("Skybox");
        var skybox = skyboxNode.CreateComponent<Skybox>();
        skybox.SetModel(cache.GetResource<Model>("Models/Box.mdl"));
        skybox.SetMaterial(cache.GetResource<Material>("Materials/DefaultSkybox.xml"));

        var rigNode = DefaultScene.CreateChild("VRRig");
        DefaultRig = rigNode.CreateComponent<VRRig>();
    }

    protected void ValidateCurrentRig()
    {
        if (!Rig.IsValid())
            DefaultRig?.Activate();
    }

    protected void UpdateCurrentRig()
    {
        System.Diagnostics.Debug.Assert(Runtime == VRRuntime.OpenXR, "Only OpenXR is supported at this time");

        // Skip update if we are not ready.
        var currentSurface = CurrentBackBufferColor?.GetRenderSurface();
        if (!Rig.IsValid() || currentSurface is null)
            return;

        // Update transforms and cameras.
        var head = VRRigDesc.TryGet(Rig.Head)!;
        head.SetVar("PreviousTransformLocal", head.GetTransformMatrix());
        head.SetVar("PreviousTransformWorld", head.GetWorldTransform());
        head.SetTransformMatrix(GetHeadTransform());

        var leftEyeCamera = VRRigDesc.TryGet(Rig.LeftEye)!;
        var rightEyeCamera = VRRigDesc.TryGet(Rig.RightEye)!;
        var leftEyeNode = leftEyeCamera.Node;
        var rightEyeNode = rightEyeCamera.Node;

        leftEyeCamera.SetFov(100.0f); // Junk mostly, the eye matrices will be overridden.
        leftEyeCamera.SetNearClip(Rig.NearDistance);
        leftEyeCamera.SetFarClip(Rig.FarDistance);
        rightEyeCamera.SetFov(100.0f);
        rightEyeCamera.SetNearClip(Rig.NearDistance);
        rightEyeCamera.SetFarClip(Rig.FarDistance);

        leftEyeCamera.SetProjection(GetProjection(VREye.Left, Rig.NearDistance, Rig.FarDistance));
        rightEyeCamera.SetProjection(GetProjection(VREye.Right, Rig.NearDistance, Rig.FarDistance));

        leftEyeNode.SetTransformMatrix(GetEyeLocalTransform(VREye.Left));
        rightEyeNode.SetTransformMatrix(GetEyeLocalTransform(VREye.Right));

        var ipdAdjust = IPDCorrection * 0.5f * 0.001f;
        leftEyeNode.Translate(new Vector3f(ipdAdjust, 0, 0), TransformSpace.Local);
        rightEyeNode.Translate(new Vector3f(-ipdAdjust, 0, 0), TransformSpace.Local);

        // Connect to the current surface in the swap chain.
        if (!ReferenceEquals(currentSurface.GetViewport(0), Rig.Viewport))
            currentSurface.SetViewport(0, Rig.Viewport);

        Rig.Viewport!.SetRect(new IntRect(0, 0, CurrentBackBufferColor!.Width, CurrentBackBufferColor.Height));
        currentSurface.QueueUpdate();
    }

    protected Vector2i RecommendedEyeTextureSize;
    protected Vector2i EyeTextureSize;
    protected bool AutoClearMasks = true;
    protected Scene? DefaultScene;
    protected VRRig? DefaultRig;
    protected VRRigDesc Rig;
    protected Texture2D? CurrentBackBufferColor;
    protected Texture2D? CurrentBackBufferDepth;
    protected XRActionGroup? ActiveActionSet;
    protected Dictionary<string, XRActionGroup> ActionSets { get; } = new(StringComparer.Ordinal);
}
