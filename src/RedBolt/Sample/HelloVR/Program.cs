// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.Sample.HelloVR;

using System;
using System.Text;

using RedBolt.Bindings.DearImGui;
using RedBolt.DearImGui;
using RedBolt.Engine;
using RedBolt.Graphics;
using RedBolt.Input;
using RedBolt.Math;
using RedBolt.PhysicsJolt;
using RedBolt.Resource;
using RedBolt.Scene;
using RedBolt.XR;

internal static class HelloVRProgram
{
    private static int Main()
    {
        try
        {
            using var application = new HelloVRSample();
            return application.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

/// <summary>
/// Simple VR sample demonstrating OpenXR initialization, stereo rendering, controller input,
/// object grabbing, haptics, smooth locomotion, and snap turning.
/// </summary>
public sealed class HelloVRSample : Application
{
    private ButtonCommand _turnLeft = new(4);
    private ButtonCommand _turnRight = new(2);
    private Scene? _scene;
    private Node? _dynamicObjects;
    private Node? _cameraNode;

    protected override void Setup(EngineParameters parameters)
    {
        // XR must be registered during Setup so it can extend graphics-device creation.
        XR.RegisterVRLibrary(Context);
        Physics.RegisterPhysicsLibrary(Context);

        parameters.ApplicationName = "RedBolt.Sample.HelloVR";
        parameters.WindowTitle = "RedBolt Hello VR";
        parameters.ResourcePaths = "CoreData;Data";
        parameters.ResourcePrefixPaths = ".;..;../Base";
        parameters.XR = true;
        parameters.Borderless = false;
        parameters.FullScreen = false;
        parameters.WindowMaximize = false;
#if !REDBOLT_PLATFORM_ANDROID
        parameters.WindowWidth = 1280;
        parameters.WindowHeight = 720;
#endif
    }

    protected override void Start()
    {
        base.Start();

        if (GetSubsystem<VirtualReality>() is not { } virtualReality ||
            !virtualReality.InitializeSession(new VRSessionParameters { ManifestPath = "XR/DefaultManifest.xml" }))
        {
            Exit();
            return;
        }

        CreateScene();
        Renderer.SetViewport(0, new Viewport(_scene!, _cameraNode!.GetComponent<Camera>()!));
        Input.SetMouseMode(MouseMode.Relative);
        Input.SetMouseVisible(false);
    }

    protected override void Update(float timeStep)
    {
        DrawInstructions();

        if (Input.ExitRequested || Input.GetKeyPress(Key.Escape))
        {
            Exit();
            return;
        }

        var virtualReality = GetSubsystem<VirtualReality>();
        var rigNode = _scene?.GetChild("VRRig");
        if (virtualReality is null || !virtualReality.IsVisible || rigNode is null ||
            !virtualReality.GetRig().IsValid())
            return;

        ref readonly var rig = ref virtualReality.GetRig();
        UpdateGrab(virtualReality, rig.LeftHandPose, VRHand.Left);
        UpdateGrab(virtualReality, rig.RightHandPose, VRHand.Right);
        UpdateTracker(virtualReality, rigNode, "left_foot");
        UpdateTracker(virtualReality, rigNode, "right_foot");

        if (virtualReality.GetInputBinding("stick", VRHand.Left) is { } leftStick)
        {
            var delta = VRUtils.SmoothLocomotionHead(rigNode, leftStick, 0.3f);
            rigNode.Translate(delta * (1.5f * timeStep), TransformSpace.World);
        }

        if (virtualReality.GetInputBinding("stick", VRHand.Right) is { } rightStick)
        {
            var command = VRUtils.JoystickAsDPad(rightStick, 0.3f);
            var head = TryGetNode(rig.Head)!;
            var pivot = new Vector3f(head.WorldPosition.X, rigNode.WorldPosition.Y, head.WorldPosition.Z);
            if (_turnLeft.CheckStrict(command))
                rigNode.RotateAround(pivot, new Quaternion(-45.0f, Vector3f.Up), TransformSpace.World);
            if (_turnRight.CheckStrict(command))
                rigNode.RotateAround(pivot, new Quaternion(45.0f, Vector3f.Up), TransformSpace.World);
        }

        // Draw debug geometry.
        var debug = _scene!.GetOrCreateComponent<DebugRenderer>();
        debug.AddNode(rigNode, 1.0f, false);
        debug.AddNode(TryGetNode(rig.LeftHandPose), 0.15f, false);
        debug.AddNode(TryGetNode(rig.RightHandPose), 0.15f, false);
        DrawAim(debug, TryGetNode(rig.LeftHandAim));
        DrawAim(debug, TryGetNode(rig.RightHandAim));
    }

    protected override void Stop()
    {
        GetSubsystem<VirtualReality>()?.ShutdownSession();
        Renderer.SetNumViewports(0);
        _dynamicObjects = null;
        _cameraNode = null;
        _scene?.Dispose();
        _scene = null;
        base.Stop();
    }

    public override void Dispose()
    {
        if (_scene is not null)
            Renderer.SetNumViewports(0);
        _dynamicObjects = null;
        _cameraNode = null;
        _scene?.Dispose();
        _scene = null;
        base.Dispose();
    }

    private void CreateScene()
    {
        _scene = new Scene();
        var sceneResource = Resources.GetResource<XMLFile>("Scenes/HelloVR.scene")
            ?? throw new InvalidOperationException("Unable to load Scenes/HelloVR.scene");
        using (var root = sceneResource.GetRoot())
        {
            if (!_scene.LoadXML(root))
                throw new InvalidOperationException("Unable to deserialize Scenes/HelloVR.scene");
        }

        _dynamicObjects = _scene.GetChild("Dynamic Objects")
            ?? throw new InvalidOperationException("HelloVR scene is missing Dynamic Objects");

        _cameraNode = _scene.CreateChild("Camera");
        _cameraNode.SetPosition(new Vector3f(0.0f, 5.0f, 0.0f));
        _cameraNode.CreateComponent<Camera>();
        _cameraNode.CreateComponent<FreeFlyController>();

        var rigNode = _scene.GetChild("VRRig")
            ?? throw new InvalidOperationException("HelloVR scene is missing VRRig");
        var rig = rigNode.GetComponent<VRRig>()
            ?? throw new InvalidOperationException("HelloVR scene is missing its VRRig component");
        rig.Activate();

        SetupHandComponents(rig.GetLeftHandPose(), rig.GetLeftHandAim());
        SetupHandComponents(rig.GetRightHandPose(), rig.GetRightHandAim());
        CreateTrackerDisplay(rigNode, "left_foot");
        CreateTrackerDisplay(rigNode, "right_foot");
    }

    private void SetupHandComponents(Node? handPoseNode, Node? handAimNode)
    {
        if (handPoseNode is null || handAimNode is null)
            return;

        const float handSize = 0.08f;
        const float aimSize = 0.02f;
        const float aimLength = 0.08f;

        var displayNode = handPoseNode.CreateChild("Display");
        displayNode.SetScale(handSize);
        ConfigureDisplay(displayNode);

        handPoseNode.CreateComponent<CollisionShape>().SetBox(Vector3f.One * handSize);
        handPoseNode.CreateComponent<RigidBody>().IsKinematic = true;

        var aimNode = handAimNode.CreateChild("Aim");
        aimNode.SetScale(new Vector3f(aimSize, aimSize, aimLength));
        aimNode.SetPosition(new Vector3f(0.0f, 0.0f, aimLength / 2.0f));
        ConfigureDisplay(aimNode);
    }

    private void CreateTrackerDisplay(Node rigNode, string name)
    {
        var node = rigNode.CreateChild(name);
        ConfigureDisplay(node);
        node.SetEnabled(false);
    }

    private void ConfigureDisplay(Node node)
    {
        var model = node.CreateComponent<StaticModel>();
        model.SetModel(Resources.GetResource<Model>("Models/Box.mdl"));
        model.SetMaterial(Resources.GetResource<Material>("Materials/Constant/MattTransparent.xml"));
    }

    private void UpdateGrab(VirtualReality virtualReality, WeakReference<Node>? handReference, VRHand hand)
    {
        var handNode = TryGetNode(handReference);
        var grab = virtualReality.GetInputBinding("grab", hand);
        if (handNode is null || grab is null || !grab.IsChanged)
            return;

        if (grab.GetFloat() == 1.0f)
            GrabDynamicObject(virtualReality, handNode, hand);
        else
            ReleaseDynamicObject(handNode);
    }

    private void GrabDynamicObject(VirtualReality virtualReality, Node handNode, VRHand hand)
    {
        Node? closestObject = null;
        var closestDistance = float.PositiveInfinity;
        foreach (var candidate in _dynamicObjects!.GetChildren())
        {
            var distance = (candidate.WorldPosition - handNode.WorldPosition).Length;
            if (distance < closestDistance)
            {
                closestObject = candidate;
                closestDistance = distance;
            }
        }

        if (closestObject is null || closestDistance > 0.25f ||
            closestObject.GetComponent<Constraint>() is not { } constraint)
            return;

        constraint.OtherBody = handNode.GetComponent<RigidBody>();
        constraint.OtherPosition = handNode.WorldToLocal(closestObject.WorldPosition);
        constraint.OtherRotation = handNode.WorldRotation.Inverse * closestObject.WorldRotation;
        constraint.SetEnabled(true);
        virtualReality.TriggerHaptic(hand, 0.1f, 0.0f, 0.5f);
    }

    private void ReleaseDynamicObject(Node handNode)
    {
        var handBody = handNode.GetComponent<RigidBody>();
        foreach (var candidate in _dynamicObjects!.GetChildren())
        {
            if (candidate.GetComponent<Constraint>() is not { } constraint ||
                !ReferenceEquals(constraint.OtherBody, handBody))
                continue;
            constraint.SetEnabled(false);
            constraint.OtherBody = null;
        }
    }

    private static void UpdateTracker(VirtualReality virtualReality, Node rigNode, string name)
    {
        var node = rigNode.GetChild(name);
        var binding = virtualReality.GetInputBinding(name);
        if (node is null || binding is null)
            return;

        node.SetEnabled(binding.IsBound);
        if (binding.IsBound)
        {
            node.SetTransformMatrix(binding.GetTransformMatrix());
            node.SetScale(0.2f);
        }
    }

    private void DrawInstructions()
    {
        var windowOpen = true;
        if (ImGui.Begin("VR Status"u8, ref windowOpen,
                ImGuiWindowFlags.ImGuiWindowFlagsNoResize |
                ImGuiWindowFlags.ImGuiWindowFlagsNoSavedSettings))
        {
            ImGui.TextUnformatted(Encoding.UTF8.GetBytes(GetStatus()));
        }
        ImGui.End();
    }

    private string GetStatus()
    {
        var virtualReality = GetSubsystem<VirtualReality>();
        if (virtualReality is null || !virtualReality.IsConnected)
            return "VR is not initialized";
        if (virtualReality.IsFocused)
            return "VR is visible and receives input";
        if (virtualReality.IsVisible)
            return "VR is visible but the input is disabled";
        return virtualReality.IsRunning ? "VR is running but is not rendered" : "VR is idle";
    }

    private static void DrawAim(DebugRenderer debug, Node? handAim)
    {
        if (handAim is null)
            return;

        var position = handAim.WorldPosition;
        debug.AddLine(position, position + handAim.WorldDirection * 2.0f, Color.White, false);
    }

    private static Node? TryGetNode(WeakReference<Node>? reference) =>
        reference is not null && reference.TryGetTarget(out var node) ? node : null;
}
