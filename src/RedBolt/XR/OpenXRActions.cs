// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.XR;

using System;
using System.Linq;

using Microsoft.Extensions.Logging;

using RedBolt.Bindings.OpenXR;
using RedBolt.Core;
using RedBolt.Logging;
using RedBolt.Resource;

using SharpCpp;

using XrApi = RedBolt.Bindings.OpenXR.OpenXR;

public sealed partial class OpenXR
{
    private static readonly string[] LeftRightHandPaths = ["/user/hand/left", "/user/hand/right"];

    private void BindActions(XMLFile xmlFile)
    {
        using var root = xmlFile.GetRoot();
        for (var element = root.GetChild("actionset"); element.NotNull; element = element.GetNext("actionset"))
        {
            var actionGroup = CreateActionGroup(element);
            if (actionGroup is not null)
                ActionSets.Add(actionGroup.Name, actionGroup);
        }
        UpdateBindingBound();
    }

    private OpenXRActionGroup? CreateActionGroup(XMLElement element)
    {
        var name = element.GetAttribute("name");
        var localizedName = GetSubsystem<Localization>()?.Get(name) ?? name;
        using var createInfo = new XrActionSetCreateInfo
        {
            Type = XrStructureType.XrTypeActionSetCreateInfo
        };
        OpenXRPrimitives.SetUtf8(createInfo.ActionSetName, name);
        OpenXRPrimitives.SetUtf8(createInfo.LocalizedActionSetName, localizedName);
        if (!OpenXRPrimitives.Check(XrApi.XrCreateActionSet(_instance!.Value!, createInfo, out var actionSet),
                nameof(XrApi.XrCreateActionSet)))
            return null;

        var ownedSet = new OpenXROwnedHandle<XrActionSetT>(actionSet, XrApi.XrDestroyActionSet);
        var actionGroup = new OpenXRActionGroup(name, localizedName, ownedSet);
        using var actions = element.GetChild("actions");
        for (var action = actions.GetChild("action"); action.NotNull; action = action.GetNext("action"))
        {
            if (!CreateBinding(actionSet, action, actionGroup))
            {
                actionGroup.Dispose();
                return null;
            }
        }

        for (var profile = element.GetChild("profile"); profile.NotNull; profile = profile.GetNext("profile"))
        {
            var extension = profile.GetAttribute("extension");
            if (string.IsNullOrEmpty(extension) || _activeExtensions.Contains(extension,
                    StringComparer.OrdinalIgnoreCase))
                SuggestInteractionProfile(profile, actionGroup);
        }
        return actionGroup;
    }

    private unsafe bool CreateBinding(XrActionSetT actionSet, XMLElement element, OpenXRActionGroup actionGroup)
    {
        var handPaths = GetHandPaths();
        var name = element.GetAttribute("name");
        var localizedName = GetSubsystem<Localization>()?.Get(name) ?? name;
        var handed = element.GetBool("handed");
        if (!TryParseBindingType(element.GetAttribute("type"), out var dataType))
        {
            Log.GetLogger().LogError("Unknown XR action type {Type} for action {Action}",
                element.GetAttribute("type"), name);
            return false;
        }

        Span<ulong> subactionPaths = handed
            ? stackalloc ulong[2] { handPaths[0], handPaths[1] }
            : element.HasAttribute("subaction")
                ? stackalloc ulong[1] { StringToPath(element.GetAttribute("subaction")) }
                : [];

        using var createInfo = new XrActionCreateInfo
        {
            Type = XrStructureType.XrTypeActionCreateInfo,
            ActionType = ToActionType(dataType),
            CountSubactionPaths = handed ? 2u : 0u
        };
        OpenXRPrimitives.SetUtf8(createInfo.ActionName, name);
        OpenXRPrimitives.SetUtf8(createInfo.LocalizedActionName, localizedName);

        XrActionT action;
        fixed (ulong* pathsPtr = subactionPaths)
        {
            createInfo.SubactionPaths = (nint)pathsPtr;
            if (!OpenXRPrimitives.Check(XrApi.XrCreateAction(actionSet, createInfo, out action),
                    nameof(XrApi.XrCreateAction)))
                return false;
        }

        var ownedAction = new OpenXROwnedHandle<XrActionT>(action, XrApi.XrDestroyAction);
        actionGroup.AddAction(ownedAction);
        var needsSpace = createInfo.ActionType == XrActionType.XrActionTypePoseInput;
        OpenXROwnedHandle<XrSpaceT>? leftSpace = null;
        OpenXROwnedHandle<XrSpaceT>? rightSpace = null;
        if (needsSpace && !CreateActionSpaces(action, handed, handPaths, out leftSpace, out rightSpace))
            return false;

        if (handed)
        {
            var isGripPose = element.GetBool("grip");
            var isAimPose = element.GetBool("aim");
            actionGroup.AddBinding(new OpenXRBinding(name, localizedName, VRHand.Left, dataType,
                isGripPose, isAimPose, actionSet, ownedAction, handPaths[0], leftSpace));
            actionGroup.AddBinding(new OpenXRBinding(name, localizedName, VRHand.Right, dataType,
                isGripPose, isAimPose, actionSet, ownedAction, handPaths[1], rightSpace));
        }
        else
        {
            actionGroup.AddBinding(new OpenXRBinding(name, localizedName, VRHand.None, dataType,
                false, false, actionSet, ownedAction, 0, leftSpace));
        }
        return true;
    }

    private bool CreateActionSpaces(XrActionT action, bool handed, ulong[] handPaths,
        out OpenXROwnedHandle<XrSpaceT>? left, out OpenXROwnedHandle<XrSpaceT>? right)
    {
        left = null;
        right = null;
        using var info = new XrActionSpaceCreateInfo
        {
            Type = XrStructureType.XrTypeActionSpaceCreateInfo,
            Action = action,
            PoseInActionSpaceOrientationW = 1.0f
        };

        if (!handed)
        {
            if (!OpenXRPrimitives.Check(XrApi.XrCreateActionSpace(_session!.Value!, info, out var space),
                    nameof(XrApi.XrCreateActionSpace)))
                return false;
            left = new OpenXROwnedHandle<XrSpaceT>(space, XrApi.XrDestroySpace);
            return true;
        }

        info.SubactionPath = handPaths[0];
        if (!OpenXRPrimitives.Check(XrApi.XrCreateActionSpace(_session!.Value!, info, out var leftSpace),
                nameof(XrApi.XrCreateActionSpace)))
            return false;
        left = new OpenXROwnedHandle<XrSpaceT>(leftSpace, XrApi.XrDestroySpace);

        info.SubactionPath = handPaths[1];
        if (!OpenXRPrimitives.Check(XrApi.XrCreateActionSpace(_session.Value!, info, out var rightSpace),
                nameof(XrApi.XrCreateActionSpace)))
        {
            left.Dispose();
            left = null;
            return false;
        }
        right = new OpenXROwnedHandle<XrSpaceT>(rightSpace, XrApi.XrDestroySpace);
        return true;
    }

    private void SuggestInteractionProfile(XMLElement element, OpenXRActionGroup actionGroup)
    {
        var interactionProfile = StringToPath(element.GetAttribute("device"));
        using var bindings = new NativeArray<XrActionSuggestedBinding>(CountChildren(element, "bind"));
        var count = 0;
        for (var child = element.GetChild("bind"); child.NotNull; child = child.GetNext("bind"))
        {
            var binding = actionGroup.FindBindingImpl(child.GetAttribute("action"));
            if (binding is null)
                continue;
            using var suggested = new XrActionSuggestedBinding
            {
                Action = binding.Action.Value!,
                Binding = StringToPath(child.GetAttribute("path"))
            };
            bindings[count++] = suggested;
        }

        if (count == 0)
            return;
        using var first = bindings[0];
        using var suggestion = new XrInteractionProfileSuggestedBinding
        {
            Type = XrStructureType.XrTypeInteractionProfileSuggestedBinding,
            InteractionProfile = interactionProfile,
            CountSuggestedBindings = checked((uint)count),
            SuggestedBindings = first
        };
        _ = OpenXRPrimitives.Check(XrApi.XrSuggestInteractionProfileBindings(_instance!.Value!, suggestion),
            nameof(XrApi.XrSuggestInteractionProfileBindings));
    }

    private ulong[] GetHandPaths() =>
        [StringToPath(LeftRightHandPaths[0]), StringToPath(LeftRightHandPaths[1])];

    private ulong StringToPath(string path)
    {
        _ = OpenXRPrimitives.Check(XrApi.XrStringToPath(_instance!.Value!, OpenXRPrimitives.Utf8Z(path),
            out var result), nameof(XrApi.XrStringToPath));
        return result;
    }

    private static int CountChildren(XMLElement parent, string name)
    {
        var result = 0;
        for (var child = parent.GetChild(name); child.NotNull; child = child.GetNext(name))
            ++result;
        return result;
    }

    private static bool TryParseBindingType(string typeName, out VariantType type)
    {
        var result = typeName switch
        {
            "boolean" => (true, VariantType.Bool),
            "vector1" or "single" => (true, VariantType.Float),
            "vector2" => (true, VariantType.Vector2),
            "vector3" => (true, VariantType.Vector3),
            "pose" => (true, VariantType.Matrix3x4),
            "haptic" => (true, VariantType.None),
            _ => (false, VariantType.None)
        };
        type = result.Item2;
        return result.Item1;
    }

    private static XrActionType ToActionType(VariantType type) => type switch
    {
        VariantType.Bool => XrActionType.XrActionTypeBooleanInput,
        VariantType.Float => XrActionType.XrActionTypeFloatInput,
        VariantType.Vector2 => XrActionType.XrActionTypeVector2fInput,
        VariantType.Vector3 or VariantType.Matrix3x4 => XrActionType.XrActionTypePoseInput,
        VariantType.None => XrActionType.XrActionTypeVibrationOutput,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
