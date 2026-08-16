// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.XR;

using System;
using System.Collections.Generic;

using RedBolt.Bindings.OpenXR;
using RedBolt.Core;
using RedBolt.Math;
using RedBolt.Resource;
using RedBolt.Scene;
using RedBolt.Utility;

using SharpCpp;

using XrApi = RedBolt.Bindings.OpenXR.OpenXR;

internal sealed class OpenXRControllerModel : IDisposable
{
    private readonly VRHand _hand;
    private readonly ulong _handPath;
    private ulong _modelKey;
    private NativeArray<XrControllerModelNodePropertiesMSFT>? _properties;
    private NativeArray<XrControllerModelNodeStateMSFT>? _nodeStates;
    private GLTFImporter? _importer;
    private PrefabResource? _prefab;
    private WeakReference<Node>? _cachedControllerNode;
    private Node?[] _cachedPropertyNodes = [];

    public OpenXRControllerModel(VRHand hand, XrInstanceT instance)
    {
        _hand = hand;
        var path = hand == VRHand.Left ? "/user/hand/left" : "/user/hand/right";
        _ = OpenXRPrimitives.Check(XrApi.XrStringToPath(instance, OpenXRPrimitives.Utf8Z(path), out _handPath),
            nameof(XrApi.XrStringToPath));
    }

    public PrefabResource? Prefab => _prefab;

    public void UpdateModel(XrSessionT session)
    {
        using var currentState = new XrControllerModelKeyStateMSFT
        {
            Type = XrStructureType.XrTypeControllerModelKeyStateMsft
        };
        if (!OpenXRPrimitives.Check(XrApi.RedBoltOpenXRGetControllerModelKeyMSFT(
                session, _handPath, currentState), nameof(XrApi.RedBoltOpenXRGetControllerModelKeyMSFT)))
            return;

        if (_modelKey == currentState.ModelKey)
            return;

        _modelKey = currentState.ModelKey;
        if (_modelKey == 0)
        {
            _prefab = null;
            _importer?.Dispose();
            _importer = null;
            return;
        }

        if (!OpenXRPrimitives.Check(XrApi.RedBoltOpenXRLoadControllerModelMSFT(
                session, _modelKey, 0, out var dataSize, []),
                nameof(XrApi.RedBoltOpenXRLoadControllerModelMSFT)))
            return;

        var data = new byte[dataSize];
        if (!OpenXRPrimitives.Check(XrApi.RedBoltOpenXRLoadControllerModelMSFT(
                session, _modelKey, dataSize, out dataSize, data),
                nameof(XrApi.RedBoltOpenXRLoadControllerModelMSFT)))
            return;

        using var properties = new XrControllerModelPropertiesMSFT
        {
            Type = XrStructureType.XrTypeControllerModelPropertiesMsft
        };
        if (!OpenXRPrimitives.Check(XrApi.RedBoltOpenXRGetControllerModelPropertiesMSFT(
                session, _modelKey, properties), nameof(XrApi.RedBoltOpenXRGetControllerModelPropertiesMSFT)))
            return;

        var newProperties = new NativeArray<XrControllerModelNodePropertiesMSFT>(
            checked((int)properties.NodeCountOutput));
        InitializeRecordArray(newProperties, XrStructureType.XrTypeControllerModelNodePropertiesMsft);
        if (newProperties.Length != 0)
        {
            using var firstProperty = newProperties[0];
            properties.NodeCapacityInput = checked((uint)newProperties.Length);
            properties.NodeProperties = firstProperty;
            if (!OpenXRPrimitives.Check(XrApi.RedBoltOpenXRGetControllerModelPropertiesMSFT(
                    session, _modelKey, properties), nameof(XrApi.RedBoltOpenXRGetControllerModelPropertiesMSFT)))
            {
                newProperties.Dispose();
                return;
            }
        }

        _properties?.Dispose();
        _properties = newProperties;

        var settings = new GLTFImporterSettings
        {
            GpuResources = true,
            CleanupRootNodes = false
        };
        var importer = new GLTFImporter(settings);
        var folder = $"manual://OpenXR/ControllerModel/{(_hand == VRHand.Left ? "Left" : "Right")}/";
        if (!importer.LoadFileBinary(data) || !importer.Process(string.Empty, folder, null))
        {
            importer.Dispose();
            return;
        }

        var prefab = Context.Instance.GetSubsystem<ResourceCache>()?
            .GetResourceOrNull<PrefabResource>(folder + "Prefab.prefab");
        if (prefab is null)
        {
            importer.Dispose();
            return;
        }

        _importer?.Dispose();
        _importer = importer;
        _prefab = prefab;
        _cachedControllerNode = null;
    }

    public void UpdateTransforms(XrSessionT session, Node controllerNode)
    {
        if (_properties is null || _properties.Length == 0 || _prefab is null || _importer is null)
            return;

        if (_nodeStates is null || _nodeStates.Length != _properties.Length)
        {
            _nodeStates?.Dispose();
            _nodeStates = new NativeArray<XrControllerModelNodeStateMSFT>(_properties.Length);
            InitializeRecordArray(_nodeStates, XrStructureType.XrTypeControllerModelNodeStateMsft);
        }

        using var state = new XrControllerModelStateMSFT
        {
            Type = XrStructureType.XrTypeControllerModelStateMsft,
            NodeCapacityInput = checked((uint)_nodeStates.Length)
        };
        using var firstState = _nodeStates[0];
        state.NodeStates = firstState;
        if (!OpenXRPrimitives.Check(XrApi.RedBoltOpenXRGetControllerModelStateMSFT(
                session, _modelKey, state), nameof(XrApi.RedBoltOpenXRGetControllerModelStateMSFT)))
            return;

        UpdateCachedNodes(controllerNode);
        var count = Math.Min(checked((int)state.NodeCountOutput), _cachedPropertyNodes.Length);
        for (var index = 0; index < count; ++index)
        {
            var node = _cachedPropertyNodes[index];
            if (node is null)
                continue;

            using var nodeState = _nodeStates[index];
            var source = new Transform(
                new Vector3f(nodeState.NodePosePositionX, nodeState.NodePosePositionY, nodeState.NodePosePositionZ),
                new Quaternion(nodeState.NodePoseOrientationW, nodeState.NodePoseOrientationX,
                    nodeState.NodePoseOrientationY, nodeState.NodePoseOrientationZ));
            var transform = _importer.ConvertTransform(source);
            node.SetTransform(transform);
        }
    }

    public void Dispose()
    {
        ClearModel();
        _modelKey = 0;
    }

    private void UpdateCachedNodes(Node controllerNode)
    {
        if (_cachedControllerNode is not null && _cachedControllerNode.TryGetTarget(out var cached) &&
            ReferenceEquals(cached, controllerNode))
            return;

        _cachedControllerNode = new WeakReference<Node>(controllerNode);
        var cache = new Dictionary<(string Name, string Parent), Node>();
        CacheNodeAndChildren(cache, controllerNode, controllerNode);

        _cachedPropertyNodes = new Node?[_properties!.Length];
        for (var index = 0; index < _properties.Length; ++index)
        {
            using var property = _properties[index];
            var name = OpenXRPrimitives.GetUtf8(property.NodeName);
            var parent = OpenXRPrimitives.GetUtf8(property.ParentNodeName);
            if (cache.TryGetValue((name, parent), out var node) || cache.TryGetValue((name, string.Empty), out node))
                _cachedPropertyNodes[index] = node;
        }
    }

    private static void CacheNodeAndChildren(Dictionary<(string Name, string Parent), Node> cache,
        Node node, Node root)
    {
        var fullKey = (node.Name, ReferenceEquals(node, root) ? string.Empty : node.Parent?.Name ?? string.Empty);
        cache.TryAdd(fullKey, node);
        cache.TryAdd((node.Name, string.Empty), node);
        foreach (var child in node.GetChildren())
            CacheNodeAndChildren(cache, child, root);
    }

    private void ClearModel()
    {
        _cachedControllerNode = null;
        _cachedPropertyNodes = [];
        _prefab = null;
        _importer?.Dispose();
        _importer = null;
        _nodeStates?.Dispose();
        _nodeStates = null;
        _properties?.Dispose();
        _properties = null;
    }

    private static void InitializeRecordArray<T>(NativeArray<T> array, XrStructureType type)
        where T : SharpCpp.Support.NativeObject
    {
        for (var index = 0; index < array.Length; ++index)
        {
            using var item = array[index];
            switch (item)
            {
            case XrControllerModelNodePropertiesMSFT property:
                property.Type = type;
                break;
            case XrControllerModelNodeStateMSFT state:
                state.Type = type;
                break;
            }
        }
    }
}
