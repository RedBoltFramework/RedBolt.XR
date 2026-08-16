// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.XR;

using System;

using RedBolt.Core;
using RedBolt.Graphics;
using RedBolt.RenderAPI;
using RedBolt.Scene;

[Reflected(ObjectCategory.Category_Logic, InheritAttributes = false)]
public partial class VRRig : LogicComponent
{
    private readonly WeakReference<Node> _headNode = new(null!);
    private readonly WeakReference<Node> _leftEyeNode = new(null!);
    private readonly WeakReference<Node> _rightEyeNode = new(null!);
    private readonly WeakReference<Camera> _leftEyeCamera = new(null!);
    private readonly WeakReference<Camera> _rightEyeCamera = new(null!);
    private readonly WeakReference<Node> _leftHandPoseNode = new(null!);
    private readonly WeakReference<Node> _rightHandPoseNode = new(null!);
    private readonly WeakReference<Node> _leftHandAimNode = new(null!);
    private readonly WeakReference<Node> _rightHandAimNode = new(null!);
    private readonly WeakReference<Node> _leftHandControllerNode = new(null!);
    private readonly WeakReference<Node> _rightHandControllerNode = new(null!);

    [Callable("Activate", Label = "Use this rig for the headset display")]
    public void Activate()
    {
        if (GetSubsystem<VirtualReality>() is not { } virtualReality)
            return;

        var description = new VRRigDesc
        {
            Scene = new WeakReference<Scene>(Scene!),
            Head = _headNode,
            LeftEye = _leftEyeCamera,
            RightEye = _rightEyeCamera,
            LeftHandPose = _leftHandPoseNode,
            RightHandPose = _rightHandPoseNode,
            LeftHandAim = _leftHandAimNode,
            RightHandAim = _rightHandAimNode,
            LeftController = _leftHandControllerNode,
            RightController = _rightHandControllerNode,
            NearDistance = 0.01f,
            FarDistance = 150.0f,
        };
        virtualReality.ConnectToRig(description);
    }

    public Node? GetHead() => TryGet(_headNode);
    public Node? GetLeftEye() => TryGet(_leftEyeNode);
    public Node? GetRightEye() => TryGet(_rightEyeNode);
    public Node? GetLeftHandPose() => TryGet(_leftHandPoseNode);
    public Node? GetRightHandPose() => TryGet(_rightHandPoseNode);
    public Node? GetLeftHandAim() => TryGet(_leftHandAimNode);
    public Node? GetRightHandAim() => TryGet(_rightHandAimNode);
    public Node? GetLeftHandController() => TryGet(_leftHandControllerNode);
    public Node? GetRightHandController() => TryGet(_rightHandControllerNode);

    protected override void OnNodeSet(Node? previousNode, Node? currentNode)
    {
        if (currentNode is null)
        {
            base.OnNodeSet(previousNode, currentNode);
            return;
        }

        var renderDevice = GetSubsystem<RenderDevice>();

        static Node GetOrCreateTemporaryChild(Node parent, string name)
        {
            var child = parent.GetChild(name);
            return child ?? parent.CreateTemporaryChild(name);
        }

        var headNode = GetOrCreateTemporaryChild(currentNode, "Head");
        var leftEyeNode = GetOrCreateTemporaryChild(headNode, "Left_Eye");
        var rightEyeNode = GetOrCreateTemporaryChild(headNode, "Right_Eye");
        var leftHandPoseNode = GetOrCreateTemporaryChild(currentNode, "Left_Hand");
        var rightHandPoseNode = GetOrCreateTemporaryChild(currentNode, "Right_Hand");
        var leftHandAimNode = GetOrCreateTemporaryChild(currentNode, "Left_Aim");
        var rightHandAimNode = GetOrCreateTemporaryChild(currentNode, "Right_Aim");
        var leftHandControllerNode = GetOrCreateTemporaryChild(leftHandPoseNode, "Left_Controller");
        var rightHandControllerNode = GetOrCreateTemporaryChild(rightHandPoseNode, "Right_Controller");

        _headNode.SetTarget(headNode);
        _leftEyeNode.SetTarget(leftEyeNode);
        _rightEyeNode.SetTarget(rightEyeNode);
        _leftHandPoseNode.SetTarget(leftHandPoseNode);
        _rightHandPoseNode.SetTarget(rightHandPoseNode);
        _leftHandAimNode.SetTarget(leftHandAimNode);
        _rightHandAimNode.SetTarget(rightHandAimNode);
        _leftHandControllerNode.SetTarget(leftHandControllerNode);
        _rightHandControllerNode.SetTarget(rightHandControllerNode);

        var leftEyeCamera = leftEyeNode.GetOrCreateComponent<Camera>();
        var rightEyeCamera = rightEyeNode.GetOrCreateComponent<Camera>();
        _leftEyeCamera.SetTarget(leftEyeCamera);
        _rightEyeCamera.SetTarget(rightEyeCamera);

        if (renderDevice?.GetBackend() == RenderBackend.OpenGL)
        {
            leftEyeCamera.SetFlipVertical(true);
            rightEyeCamera.SetFlipVertical(true);
        }

        base.OnNodeSet(previousNode, currentNode);
    }

    private static T? TryGet<T>(WeakReference<T> reference) where T : class =>
        reference.TryGetTarget(out var value) ? value : null;
}
