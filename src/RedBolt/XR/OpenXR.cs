// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.XR;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using RedBolt.Bindings.Diligent;
using RedBolt.Bindings.OpenXR;
using RedBolt.Core;
using RedBolt.Engine;
using RedBolt.Graphics;
using RedBolt.Input;
using RedBolt.Logging;
using RedBolt.Math;
using RedBolt.RenderAPI;
using RedBolt.Resource;
using RedBolt.Scene;

using SharpCpp;
using SharpCpp.Support;

#if REDBOLT_PLATFORM_ANDROID
using Java.Interop;

using SdlApi = RedBolt.Bindings.Sdl.Sdl;
#endif

using TextureFormat = RedBolt.Bindings.Diligent.TextureFormat;
using XrApi = RedBolt.Bindings.OpenXR.OpenXR;

/// <summary>OpenXR implementation of the virtual-reality subsystem.</summary>
public sealed partial class OpenXR : VirtualReality, IGraphicsInitializationExtension
{
    private const string D3D11Extension = "XR_KHR_D3D11_enable";
    private const string D3D12Extension = "XR_KHR_D3D12_enable";
    private const string VulkanExtension = "XR_KHR_vulkan_enable";
    private const string OpenGLExtension = "XR_KHR_opengl_enable";
    private const string OpenGLESExtension = "XR_KHR_opengl_es_enable";
    private const string DepthExtension = "XR_KHR_composition_layer_depth";
    private const string VisibilityMaskExtension = "XR_KHR_visibility_mask";
    private const string DebugUtilsExtension = "XR_EXT_debug_utils";
    private const string ControllerModelExtension = "XR_MSFT_controller_model";

    private const ulong PositionValid = 0x0000_0001UL;
    private const ulong OrientationValid = 0x0000_0002UL;
    private const ulong PositionTracked = 0x0000_0004UL;
    private const ulong OrientationTracked = 0x0000_0008UL;
    private const ulong LinearVelocityValid = 0x0000_0001UL;
    private const ulong AngularVelocityValid = 0x0000_0002UL;

    private readonly List<string> _supportedExtensions = [];
    private readonly List<string> _userExtensions = [];
    private readonly List<string> _activeExtensions = [];
    private readonly NativeArray<XrView> _views = new(2);
    private readonly NativeArray<XrCompositionLayerProjectionView> _eyes = new(2);
    private readonly XrCompositionLayerDepthInfoKHR[] _depth =
    [
        new() { Type = XrStructureType.XrTypeCompositionLayerDepthInfoKhr },
        new() { Type = XrStructureType.XrTypeCompositionLayerDepthInfoKhr }
    ];
    private readonly XrSpaceLocation _headLocation;
    private readonly XrSpaceVelocity _headVelocity;
    private readonly XrCompositionLayerProjection _projectionLayer;

    private OpenXROwnedHandle<XrInstanceT>? _instance;
    private OpenXROwnedHandle<XrSessionT>? _session;
    private OpenXROwnedHandle<XrSpaceT>? _headSpace;
    private OpenXROwnedHandle<XrSpaceT>? _viewSpace;
    private OpenXRSwapChain? _swapChain;
    private OpenXRSwapChain? _depthChain;
    private OpenXRAttribs? _graphicsAttributes;
    private XMLFile? _manifest;
    private readonly OpenXRBinding?[] _handGrips = new OpenXRBinding?[(int)VRHand.Count];
    private readonly OpenXRBinding?[] _handAims = new OpenXRBinding?[(int)VRHand.Count];
    private readonly OpenXRControllerModel?[] _controllerModels = new OpenXRControllerModel?[(int)VRHand.Count];

    private ulong _system;
    private long _predictedTime;
    private XrEnvironmentBlendMode _blendMode = XrEnvironmentBlendMode.XrEnvironmentBlendModeOpaque;
    private XrSessionState _sessionState;
    private bool _depthLayer;
    private bool _visibilityMask;
    private bool _controllerModel;
    private bool _debugOutput;
    private ulong _debugMessenger;
    private bool _disposed;

    public OpenXR()
    {
        _headVelocity = new XrSpaceVelocity { Type = XrStructureType.XrTypeSpaceVelocity };
        _headLocation = new XrSpaceLocation
        {
            Type = XrStructureType.XrTypeSpaceLocation,
            Next = _headVelocity.Handle
        };
        _projectionLayer = new XrCompositionLayerProjection
        {
            Type = XrStructureType.XrTypeCompositionLayerProjection
        };

        InitializeRecordArray(_views, XrStructureType.XrTypeView, static (view, type) => view.Type = type);
        InitializeRecordArray(_eyes, XrStructureType.XrTypeCompositionLayerProjectionView,
            static (eye, type) => eye.Type = type);

        SubscribeToEvent<CoreEvents.BeginFrameEvent>(HandleBeginFrame);
        SubscribeToEvent<GraphicsEvents.EndRenderingEvent>(HandleEndRendering);
    }

    public override VRRuntime Runtime => VRRuntime.OpenXR;
    public override string RuntimeName => "OpenXR";
    public override bool IsConnected => _instance?.IsValid == true && _session?.IsValid == true;

    public override bool IsRunning => IsConnected && _sessionState is
        XrSessionState.XrSessionStateReady or
        XrSessionState.XrSessionStateSynchronized or
        XrSessionState.XrSessionStateVisible or
        XrSessionState.XrSessionStateFocused;

    public override bool IsVisible => IsConnected && _sessionState is
        XrSessionState.XrSessionStateVisible or XrSessionState.XrSessionStateFocused;

    public override bool IsFocused => IsConnected && _sessionState == XrSessionState.XrSessionStateFocused;

    /// <summary>Configure the user extensions. Call before graphics initialization.</summary>
    public void SetUserExtensions(IReadOnlyList<string> extensions)
    {
        _userExtensions.Clear();
        for (var index = 0; index < extensions.Count; ++index)
            _userExtensions.Add(extensions[index]);
    }

    /// <summary>Extensions advertised by the active OpenXR loader.</summary>
    public IReadOnlyList<string> Extensions => _supportedExtensions;

    public bool InitializeGraphics(EngineParameters parameters, RenderBackend backend)
    {
        if (_instance?.IsValid == true)
        {
            Log.GetLogger().LogError("OpenXR is already initialized");
            return false;
        }

#if REDBOLT_PLATFORM_ANDROID
        if (!InitializeAndroidLoader())
            return false;
#endif

        if (!EnumerateExtensions())
            return false;

        var backendExtension = GetBackendExtensionName(backend);
        if (string.IsNullOrEmpty(backendExtension) || !IsExtensionSupported(backendExtension))
        {
            Log.GetLogger().LogError("Renderer backend {Backend} is not supported by the OpenXR runtime", backend);
            return false;
        }

        InitializeActiveExtensions(backendExtension);
        if (!CreateInstance(parameters.ApplicationName))
            return false;

        var instance = _instance!.Value!;
        if (_debugOutput)
        {
            if (!OpenXRPrimitives.Check(XrApi.RedBoltOpenXRCreateDebugMessenger(instance, out _debugMessenger),
                    nameof(XrApi.RedBoltOpenXRCreateDebugMessenger)))
                _debugMessenger = 0;
        }
        if (_controllerModel && !OpenXRPrimitives.Check(
                XrApi.RedBoltOpenXRLoadControllerModelFunctions(instance),
                nameof(XrApi.RedBoltOpenXRLoadControllerModelFunctions)))
            _controllerModel = false;
        using (var properties = new XrInstanceProperties { Type = XrStructureType.XrTypeInstanceProperties })
        {
            if (XrApi.XrGetInstanceProperties(instance, properties) == XrResult.XrSuccess)
            {
                Log.GetLogger().LogInformation("OpenXR runtime is {Runtime} version 0x{Version:x}",
                    OpenXRPrimitives.GetUtf8(properties.RuntimeName), properties.RuntimeVersion);
            }
        }

        using (var getInfo = new XrSystemGetInfo
        {
            Type = XrStructureType.XrTypeSystemGetInfo,
            FormFactor = XrFormFactor.XrFormFactorHeadMountedDisplay
        })
        {
            if (!OpenXRPrimitives.Check(XrApi.XrGetSystem(instance, getInfo, out _system), nameof(XrApi.XrGetSystem)))
                return FailSystemInitialization();
        }

        using (var properties = new XrSystemProperties { Type = XrStructureType.XrTypeSystemProperties })
        {
            if (!OpenXRPrimitives.Check(XrApi.XrGetSystemProperties(instance, _system, properties),
                    nameof(XrApi.XrGetSystemProperties)))
                return FailSystemInitialization();
            SystemName = OpenXRPrimitives.GetUtf8(properties.SystemName);
        }

        if (!InitializeViewConfiguration(instance))
            return FailSystemInitialization();

        if (SystemName.Contains("steamvr", StringComparison.OrdinalIgnoreCase))
            _depthLayer = false;

        if (!OpenXRPrimitives.Check(
                XrApi.XrGetInstanceProcAddr(instance, OpenXRPrimitives.Utf8Z("xrGetInstanceProcAddr"), out var getProc),
                nameof(XrApi.XrGetInstanceProcAddr)))
            return FailSystemInitialization();

        _graphicsAttributes = new OpenXRAttribs
        {
            GetInstanceProcAddr = getProc,
            Instance = checked((ulong)NativeObject.GetHandle(instance)),
            SystemId = _system
        };

        // Arbitrary value high enough that the XR swap chain is never throttled.
        var engine = GetSubsystem<Engine>()!;
        engine.SetMaxFps(500);
        engine.SetMaxInactiveFps(500);
        parameters.VSync = false;
        return true;
    }

    public void ConfigureGraphicsCreateInfo(RenderBackend backend, EngineCreateInfo createInfo)
    {
        if (_graphicsAttributes is null)
            throw new InvalidOperationException("OpenXR must be initialized before the graphics create-info is configured.");
        createInfo.PXRAttribs = _graphicsAttributes;
    }

    public override bool InitializeSession(in VRSessionParameters parameters)
    {
        if (_instance?.IsValid != true)
        {
            Log.GetLogger().LogError("OpenXR system has not been initialized through application Setup()");
            return false;
        }

        _manifest = GetSubsystem<ResourceCache>()?.GetResource<XMLFile>(parameters.ManifestPath);
        if (_manifest is null)
        {
            Log.GetLogger().LogError("Unable to load OpenXR manifest {Manifest}", parameters.ManifestPath);
            return false;
        }

        MultiSample = parameters.MultiSample != 0 ? parameters.MultiSample : RecommendedMultiSample;
        EyeTextureSize = new Vector2i(
            (int)MathF.Round(RecommendedEyeTextureSize.X * parameters.ResolutionScale),
            (int)MathF.Round(RecommendedEyeTextureSize.Y * parameters.ResolutionScale));

        if (!OpenSession())
        {
            ShutdownSession();
            return false;
        }

        CreateDefaultRig();
        return true;
    }

    public override void ShutdownSession()
    {
        Array.Clear(_handGrips);
        Array.Clear(_handAims);
        foreach (var actionSet in ActionSets.Values)
            (actionSet as IDisposable)?.Dispose();
        ActionSets.Clear();
        ActiveActionSet = null;
        _sessionState = 0;
        _manifest = null;
        CurrentBackBufferColor = null;
        CurrentBackBufferDepth = null;

        _swapChain?.Dispose();
        _swapChain = null;
        _depthChain?.Dispose();
        _depthChain = null;
        _headSpace?.Dispose();
        _headSpace = null;
        _viewSpace?.Dispose();
        _viewSpace = null;
        _session?.Dispose();
        _session = null;
        for (var index = 0; index < _controllerModels.Length; ++index)
        {
            _controllerModels[index]?.Dispose();
            _controllerModels[index] = null;
        }
    }

    public override void SetCurrentActionSet(XRActionGroup actionSet)
    {
        ArgumentNullException.ThrowIfNull(actionSet);
        if (_session?.Value is not { } session || actionSet is not OpenXRActionGroup implementation)
            return;

        ActiveActionSet = actionSet;
        implementation.AttachToSession(session);
        UpdateBindingBound();
    }

    public override void TriggerHaptic(VRHand hand, float durationSeconds, float cyclesPerSecond, float amplitude)
    {
        if (ActiveActionSet is null || !IsFocused || _session?.Value is not { } session)
            return;

        var bindings = ActiveActionSet.GetBindings();
        for (var index = 0; index < bindings.Count; ++index)
        {
            if (bindings[index] is not OpenXRBinding binding)
                continue;
            if (!binding.IsHaptic || binding.Hand != hand)
                continue;

            using var info = new XrHapticActionInfo
            {
                Type = XrStructureType.XrTypeHapticActionInfo,
                Action = binding.Action.Value!,
                SubactionPath = binding.SubPath
            };
            using var vibration = new XrHapticVibration
            {
                Type = XrStructureType.XrTypeHapticVibration,
                Amplitude = amplitude,
                Frequency = cyclesPerSecond,
                Duration = checked((long)(durationSeconds * 1000.0f))
            };
            using var feedback = XrHapticBaseHeader.CreateManagedWrapper(vibration.Handle, false);
            _ = XrApi.XrApplyHapticFeedback(session, info, feedback);
        }
    }

    public override Matrix3x4 GetHandTransform(VRHand hand)
    {
        if (!TryGetHandBinding(_handGrips, hand, out var binding))
            return Matrix3x4.Identity;
        return GetHeadTransform().Inverse * GetBindingTransform(binding);
    }

    public override Matrix3x4 GetHandAimTransform(VRHand hand) =>
        TryGetHandBinding(_handAims, hand, out var binding) ? GetBindingTransform(binding) : Matrix3x4.Identity;

    public override Ray GetHandAimRay(VRHand hand)
    {
        if (!TryGetHandBinding(_handAims, hand, out var binding))
            return new Ray();
        var rotation = GetBindingRotation(binding);
        var position = GetBindingPosition(binding);
        return new Ray(position, (rotation * Vector3f.Forward).Normalized);
    }

    public override void GetHandVelocity(VRHand hand, out Vector3f linear, out Vector3f angular)
    {
        linear = Vector3f.Zero;
        angular = Vector3f.Zero;
        if (!TryGetHandBinding(_handGrips, hand, out var binding))
            return;
        if ((binding.Velocity.VelocityFlags & LinearVelocityValid) != 0)
            linear = OpenXRPrimitives.ToVector3(binding.Velocity.LinearVelocityX,
                binding.Velocity.LinearVelocityY, binding.Velocity.LinearVelocityZ);
        if ((binding.Velocity.VelocityFlags & AngularVelocityValid) != 0)
            angular = OpenXRPrimitives.ToVector3(binding.Velocity.AngularVelocityX,
                binding.Velocity.AngularVelocityY, binding.Velocity.AngularVelocityZ);
    }

    public override Matrix3x4 GetEyeLocalTransform(VREye eye)
    {
        using var view = _views[(int)eye];
        return GetHeadTransform().Inverse * ToMatrix3x4(view, ScaleCorrection);
    }

    public override Matrix4 GetProjection(VREye eye, float nearDistance, float farDistance)
    {
        using var view = _views[(int)eye];
        using var fov = new XrFovf
        {
            AngleLeft = view.FovAngleLeft,
            AngleRight = view.FovAngleRight,
            AngleDown = view.FovAngleDown,
            AngleUp = view.FovAngleUp
        };
        return OpenXRPrimitives.ToProjection(nearDistance, farDistance, fov);
    }

    public override Matrix3x4 GetHeadTransform() => new(
        OpenXRPrimitives.ToVector3(_headLocation.PosePositionX, _headLocation.PosePositionY,
            _headLocation.PosePositionZ),
        OpenXRPrimitives.ToQuaternion(_headLocation.PoseOrientationX, _headLocation.PoseOrientationY,
            _headLocation.PoseOrientationZ, _headLocation.PoseOrientationW), ScaleCorrection);

    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ShutdownSession();
        _graphicsAttributes?.Dispose();
        _graphicsAttributes = null;
        if (_debugMessenger != 0 && _instance?.Value is { } instance)
        {
            _ = OpenXRPrimitives.Check(XrApi.RedBoltOpenXRDestroyDebugMessenger(instance, _debugMessenger),
                nameof(XrApi.RedBoltOpenXRDestroyDebugMessenger));
            _debugMessenger = 0;
        }
        _instance?.Dispose();
        _instance = null;
        _projectionLayer.Dispose();
        _headLocation.Dispose();
        _headVelocity.Dispose();
        foreach (var depth in _depth)
            depth.Dispose();
        _eyes.Dispose();
        _views.Dispose();
        base.Dispose();
    }

    private static void InitializeRecordArray<T>(NativeArray<T> array, XrStructureType type,
        Action<T, XrStructureType> initialize) where T : NativeObject
    {
        for (var index = 0; index < array.Length; ++index)
        {
            using var item = array[index];
            initialize(item, type);
        }
    }

    private bool EnumerateExtensions()
    {
        if (!OpenXRPrimitives.Check(
                XrApi.XrEnumerateInstanceExtensionProperties([], 0, out var count, null!),
                nameof(XrApi.XrEnumerateInstanceExtensionProperties)))
            return false;

        _supportedExtensions.Clear();
        if (count == 0)
            return true;

        using var properties = new NativeArray<XrExtensionProperties>(checked((int)count));
        InitializeRecordArray(properties, XrStructureType.XrTypeExtensionProperties,
            static (property, type) => property.Type = type);
        using var first = properties[0];
        if (!OpenXRPrimitives.Check(
                XrApi.XrEnumerateInstanceExtensionProperties([], count, out count, first),
                nameof(XrApi.XrEnumerateInstanceExtensionProperties)))
            return false;

        for (var index = 0; index < count; ++index)
        {
            using var property = properties[index];
            _supportedExtensions.Add(OpenXRPrimitives.GetUtf8(property.ExtensionName));
        }
        return true;
    }

    private void InitializeActiveExtensions(string backendExtension)
    {
        _activeExtensions.Clear();
        _activeExtensions.Add(backendExtension);

        // Debug callbacks and visibility masks are optional and do not affect base session operation.
        _debugOutput = ActivateOptionalExtension(DebugUtilsExtension);
        _visibilityMask = ActivateOptionalExtension(VisibilityMaskExtension);
        _controllerModel = ActivateOptionalExtension(ControllerModelExtension);
        _depthLayer = ActivateOptionalExtension(DepthExtension);

        ActivateOptionalExtension("XR_HTC_vive_cosmos_controller_interaction");
        ActivateOptionalExtension("XR_HTC_vive_focus3_controller_interaction");
        ActivateOptionalExtension("XR_EXT_hp_mixed_reality_controller");
        ActivateOptionalExtension("XR_EXT_samsung_odyssey_controller");
        ActivateOptionalExtension("XR_HTCX_vive_tracker_interaction");
        foreach (var extension in _userExtensions)
            ActivateOptionalExtension(extension);
    }

    private bool ActivateOptionalExtension(string extension)
    {
        if (!IsExtensionSupported(extension))
            return false;
        if (!_activeExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            _activeExtensions.Add(extension);
        return true;
    }

    private bool IsExtensionSupported(string extension) =>
        _supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static string GetBackendExtensionName(RenderBackend backend) => backend switch
    {
        RenderBackend.D3D11 => D3D11Extension,
        RenderBackend.D3D12 => D3D12Extension,
        RenderBackend.Vulkan => VulkanExtension,
#if REDBOLT_PLATFORM_ANDROID
        RenderBackend.OpenGL => OpenGLESExtension,
#else
        RenderBackend.OpenGL => OpenGLExtension,
#endif
        _ => string.Empty
    };

    private unsafe bool CreateInstance(string applicationName)
    {
        var encodedExtensions = _activeExtensions.Select(OpenXRPrimitives.Utf8Z).ToArray();
        var handles = new GCHandle[encodedExtensions.Length];
        var addresses = new nint[encodedExtensions.Length];
        try
        {
            for (var index = 0; index < encodedExtensions.Length; ++index)
            {
                handles[index] = GCHandle.Alloc(encodedExtensions[index], GCHandleType.Pinned);
                addresses[index] = handles[index].AddrOfPinnedObject();
            }

            fixed (nint* addressesPtr = addresses)
            {
                using var createInfo = new XrInstanceCreateInfo
                {
                    Type = XrStructureType.XrTypeInstanceCreateInfo,
                    ApplicationInfoApplicationVersion = 0,
                    ApplicationInfoEngineVersion = 1u << 24,
                    ApplicationInfoApiVersion = OpenXRPrimitives.CurrentApiVersion,
                    EnabledExtensionCount = checked((uint)addresses.Length),
                    EnabledExtensionNames = (nint)addressesPtr
                };
#if REDBOLT_PLATFORM_ANDROID
                using var androidInfo = new XrInstanceCreateInfoAndroidKHR
                {
                    Type = XrStructureType.XrTypeInstanceCreateInfoAndroidKhr,
                    ApplicationVM = JniEnvironment.Runtime.InvocationPointer,
                    ApplicationActivity = SdlApi.SdlAndroidGetActivity()
                };
                createInfo.Next = androidInfo.Handle;
#endif
                OpenXRPrimitives.SetUtf8(createInfo.ApplicationInfoEngineName, "RedBolt");
                OpenXRPrimitives.SetUtf8(createInfo.ApplicationInfoApplicationName, applicationName);

                if (!OpenXRPrimitives.Check(XrApi.XrCreateInstance(createInfo, out var instance),
                        nameof(XrApi.XrCreateInstance)))
                    return false;
                _instance = new OpenXROwnedHandle<XrInstanceT>(instance, XrApi.XrDestroyInstance);
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
        return true;
    }

#if REDBOLT_PLATFORM_ANDROID
    private static bool InitializeAndroidLoader()
    {
        return OpenXRPrimitives.Check(XrApi.RedBoltOpenXRInitializeAndroidLoader(
                JniEnvironment.Runtime.InvocationPointer, SdlApi.SdlAndroidGetActivity()),
            nameof(XrApi.RedBoltOpenXRInitializeAndroidLoader));
    }
#endif

    private bool InitializeViewConfiguration(XrInstanceT instance)
    {
        if (!OpenXRPrimitives.Check(XrApi.XrEnumerateEnvironmentBlendModes(instance, _system,
                XrViewConfigurationType.XrViewConfigurationTypePrimaryStereo, 0, out var blendCount, []),
                nameof(XrApi.XrEnumerateEnvironmentBlendModes)) || blendCount == 0)
            return false;
        var blendModes = new XrEnvironmentBlendMode[blendCount];
        if (!OpenXRPrimitives.Check(XrApi.XrEnumerateEnvironmentBlendModes(instance, _system,
                XrViewConfigurationType.XrViewConfigurationTypePrimaryStereo, blendCount, out blendCount, blendModes),
                nameof(XrApi.XrEnumerateEnvironmentBlendModes)))
            return false;
        _blendMode = blendModes[0];

        if (!OpenXRPrimitives.Check(XrApi.XrEnumerateViewConfigurations(instance, _system, 0,
                out var configurationCount, []), nameof(XrApi.XrEnumerateViewConfigurations)))
            return false;
        var configurations = new XrViewConfigurationType[configurationCount];
        if (!OpenXRPrimitives.Check(XrApi.XrEnumerateViewConfigurations(instance, _system, configurationCount,
                out configurationCount, configurations), nameof(XrApi.XrEnumerateViewConfigurations)) ||
            !configurations.Contains(XrViewConfigurationType.XrViewConfigurationTypePrimaryStereo))
        {
            Log.GetLogger().LogError("Stereo rendering is not supported on this OpenXR device");
            return false;
        }

        using var views = new NativeArray<XrViewConfigurationView>(2);
        InitializeRecordArray(views, XrStructureType.XrTypeViewConfigurationView,
            static (view, type) => view.Type = type);
        using var first = views[0];
        if (!OpenXRPrimitives.Check(XrApi.XrEnumerateViewConfigurationViews(instance, _system,
                XrViewConfigurationType.XrViewConfigurationTypePrimaryStereo, 2, out var viewCount, first),
                nameof(XrApi.XrEnumerateViewConfigurationViews)) || viewCount != 2)
            return false;

        using var left = views[0];
        using var right = views[1];
        RecommendedMultiSample = checked((int)left.RecommendedSwapchainSampleCount);
        RecommendedEyeTextureSize = new Vector2i(
            checked((int)Math.Min(left.RecommendedImageRectWidth, right.RecommendedImageRectWidth)),
            checked((int)Math.Min(left.RecommendedImageRectHeight, right.RecommendedImageRectHeight)));
        return true;
    }

    private bool OpenSession()
    {
        var renderDevice = GetSubsystem<RenderDevice>();
        if (renderDevice?.GetRenderDevice() is not { } nativeDevice ||
            renderDevice.GetImmediateContext() is not { } nativeContext)
            return false;

        Diligent.GetOpenXRGraphicsBinding(nativeDevice, nativeContext, out var graphicsBinding);
        if (graphicsBinding is null || graphicsBinding.IsNull)
            return false;

        try
        {
            using var createInfo = new XrSessionCreateInfo
            {
                Type = XrStructureType.XrTypeSessionCreateInfo,
                Next = graphicsBinding.GetDataPtr(),
                SystemId = _system
            };
            if (!OpenXRPrimitives.Check(XrApi.XrCreateSession(_instance!.Value!, createInfo, out var session),
                    nameof(XrApi.XrCreateSession)))
                return false;
            _session = new OpenXROwnedHandle<XrSessionT>(session, XrApi.XrDestroySession);
        }
        finally
        {
            _ = graphicsBinding.Release();
            graphicsBinding.Dispose();
        }

        (_headSpace, IsRoomScale) = CreateReferenceSpace(XrReferenceSpaceType.XrReferenceSpaceTypeStage);
        if (_headSpace is null)
        {
            (_headSpace, _) = CreateReferenceSpace(XrReferenceSpaceType.XrReferenceSpaceTypeLocal);
            IsRoomScale = false;
        }
        (_viewSpace, _) = CreateReferenceSpace(XrReferenceSpaceType.XrReferenceSpaceTypeView);
        if (_headSpace is null || _viewSpace is null)
            return false;

        BindActions(_manifest!);
        SetCurrentActionSet("default");

        if (_controllerModel)
        {
            _controllerModels[(int)VRHand.Left] = new OpenXRControllerModel(VRHand.Left, _instance!.Value!);
            _controllerModels[(int)VRHand.Right] = new OpenXRControllerModel(VRHand.Right, _instance.Value!);
        }

        var formats = GetSwapchainFormats(_session.Value!);
        var color = SelectColorFormat(renderDevice.GetBackend(), formats);
        var depth = SelectDepthFormat(renderDevice.GetBackend(), formats);
        if (color.Format == TextureFormat.TexFormatUnknown)
            return false;

        _swapChain = OpenXRSwapChain.Create(_session.Value!, renderDevice, color.Format, color.Internal,
            EyeTextureSize, MultiSample);
        if (_swapChain is null)
            return false;
        if (_depthLayer && depth.Internal != 0)
        {
            _depthChain = OpenXRSwapChain.Create(_session.Value!, renderDevice, depth.Format, depth.Internal,
                EyeTextureSize, MultiSample);
        }
        return true;
    }

    private (OpenXROwnedHandle<XrSpaceT>? Space, bool RoomScale) CreateReferenceSpace(XrReferenceSpaceType type)
    {
        using var createInfo = new XrReferenceSpaceCreateInfo
        {
            Type = XrStructureType.XrTypeReferenceSpaceCreateInfo,
            ReferenceSpaceType = type,
            PoseInReferenceSpaceOrientationW = 1.0f
        };
        if (XrApi.XrCreateReferenceSpace(_session!.Value!, createInfo, out var space) != XrResult.XrSuccess)
            return (null, false);
        return (new OpenXROwnedHandle<XrSpaceT>(space, XrApi.XrDestroySpace),
            type == XrReferenceSpaceType.XrReferenceSpaceTypeStage);
    }

    private static long[] GetSwapchainFormats(XrSessionT session)
    {
        if (XrApi.XrEnumerateSwapchainFormats(session, 0, out var count, []) != XrResult.XrSuccess)
            return [];
        var result = new long[count];
        return XrApi.XrEnumerateSwapchainFormats(session, count, out count, result) == XrResult.XrSuccess
            ? result
            : [];
    }

    private static (TextureFormat Format, long Internal) SelectColorFormat(RenderBackend backend, long[] formats)
    {
        foreach (var fallback in new[] { false, true })
        {
            foreach (var internalFormat in formats)
            {
                var format = RenderAPIUtils.GetTextureFormatFromInternal(backend, checked((uint)internalFormat));
                var isFallback = RenderAPIUtils.SetTextureFormatSRGB(format, true) != format;
                if (RenderAPIUtils.IsColorTextureFormat(format) && isFallback == fallback)
                    return (format, internalFormat);
            }
        }
        return (TextureFormat.TexFormatUnknown, 0);
    }

    private static (TextureFormat Format, long Internal) SelectDepthFormat(RenderBackend backend, long[] formats)
    {
        foreach (var fallback in new[] { false, true })
        {
            foreach (var internalFormat in formats)
            {
                var format = RenderAPIUtils.GetTextureFormatFromInternal(backend, checked((uint)internalFormat));
                if (RenderAPIUtils.IsDepthTextureFormat(format) &&
                    (format == TextureFormat.TexFormatD16Unorm) == fallback)
                    return (format, internalFormat);
            }
        }
        return (TextureFormat.TexFormatUnknown, 0);
    }

    private void HandleBeginFrame(ref CoreEvents.BeginFrameEvent eventData)
    {
        if (!IsConnected)
            return;
        PollEvents();
        if (!IsRunning)
            return;
        BeginFrame();
        if (!IsVisible)
            return;
        AcquireSwapChainImages();
        LocateViewsAndSpaces();
        SynchronizeActions();
        ValidateCurrentRig();
        UpdateCurrentRig();
        UpdateHands();
    }

    private void HandleEndRendering(ref GraphicsEvents.EndRenderingEvent eventData)
    {
        if (!IsConnected || !IsRunning)
            return;
        using var endInfo = new XrFrameEndInfo
        {
            Type = XrStructureType.XrTypeFrameEndInfo,
            EnvironmentBlendMode = _blendMode,
            DisplayTime = _predictedTime
        };
        if (IsVisible)
        {
            ReleaseSwapChainImages();
            if (TryEndFrameWithProjectionLayer(endInfo))
                return;
        }
        _ = OpenXRPrimitives.Check(XrApi.XrEndFrame(_session!.Value!, endInfo), nameof(XrApi.XrEndFrame));
    }

    private void PollEvents()
    {
        using var eventBuffer = new XrEventDataBuffer { Type = XrStructureType.XrTypeEventDataBuffer };
        while (XrApi.XrPollEvent(_instance!.Value!, eventBuffer) == XrResult.XrSuccess)
        {
            switch (eventBuffer.Type)
            {
            case XrStructureType.XrTypeEventDataVisibilityMaskChangedKhr:
                // TODO: Implement visibility mask.
                break;
            case XrStructureType.XrTypeEventDataInstanceLossPending:
                SendEvent<VREvents.VRExitEvent>();
                SendEvent<InputEvents.ExitRequestedEvent>();
                ShutdownSession();
                return;
            case XrStructureType.XrTypeEventDataInteractionProfileChanged:
                UpdateBindingBound();
                SendEvent<VREvents.VRInteractionProfileChangedEvent>();
                break;
            case XrStructureType.XrTypeEventDataSessionStateChanged:
                using (var changed = XrEventDataSessionStateChanged.CreateManagedWrapper(eventBuffer.Handle, false))
                {
                    if (!UpdateSessionState(changed.State))
                    {
                        ShutdownSession();
                        return;
                    }
                }
                break;
            }
            eventBuffer.Type = XrStructureType.XrTypeEventDataBuffer;
            eventBuffer.Next = 0;
        }
    }

    private bool UpdateSessionState(XrSessionState state)
    {
        _sessionState = state;
        switch (state)
        {
        case XrSessionState.XrSessionStateReady:
            using (var beginInfo = new XrSessionBeginInfo
            {
                Type = XrStructureType.XrTypeSessionBeginInfo,
                PrimaryViewConfigurationType = XrViewConfigurationType.XrViewConfigurationTypePrimaryStereo
            })
            {
                return OpenXRPrimitives.Check(XrApi.XrBeginSession(_session!.Value!, beginInfo),
                    nameof(XrApi.XrBeginSession));
            }
        case XrSessionState.XrSessionStateFocused:
            SendEvent<VREvents.VRResumeEvent>();
            break;
        case XrSessionState.XrSessionStateStopping:
            SendEvent<VREvents.VRPauseEvent>();
            return OpenXRPrimitives.Check(XrApi.XrEndSession(_session!.Value!), nameof(XrApi.XrEndSession));
        case XrSessionState.XrSessionStateExiting:
        case XrSessionState.XrSessionStateLossPending:
            SendEvent<VREvents.VRExitEvent>();
            break;
        }
        return true;
    }

    private void BeginFrame()
    {
        using var waitInfo = new XrFrameWaitInfo { Type = XrStructureType.XrTypeFrameWaitInfo };
        using var frameState = new XrFrameState { Type = XrStructureType.XrTypeFrameState };
        _ = XrApi.XrWaitFrame(_session!.Value!, waitInfo, frameState);
        using var beginInfo = new XrFrameBeginInfo { Type = XrStructureType.XrTypeFrameBeginInfo };
        _ = XrApi.XrBeginFrame(_session.Value!, beginInfo);
        _predictedTime = frameState.PredictedDisplayTime;
    }

    private void AcquireSwapChainImages()
    {
        if (_swapChain?.AcquireImage() is not { } color)
            return;
        CurrentBackBufferColor = color;
        if (_depthChain?.AcquireImage() is not { } depth)
            return;
        CurrentBackBufferDepth = depth;
        color.GetRenderSurface()?.SetLinkedDepthStencil(depth.GetRenderSurface());
    }

    private void ReleaseSwapChainImages()
    {
        GetSubsystem<RenderDevice>()?.GetImmediateContext()?.Flush();
        _swapChain?.ReleaseImage();
        _depthChain?.ReleaseImage();
    }

    private void LocateViewsAndSpaces()
    {
        _ = XrApi.XrLocateSpace(_viewSpace!.Value!, _headSpace!.Value!, _predictedTime, _headLocation);
        if (ActiveActionSet is not null)
        {
            var bindings = ActiveActionSet.GetBindings();
            for (var index = 0; index < bindings.Count; ++index)
            {
                if (bindings[index] is not OpenXRBinding binding)
                    continue;
                if (binding.IsBound && binding.ExpectedType is VariantType.Matrix3x4 or VariantType.Vector3)
                    binding.Locate(_headSpace.Value!, _predictedTime);
            }
        }

        using var viewInfo = new XrViewLocateInfo
        {
            Type = XrStructureType.XrTypeViewLocateInfo,
            ViewConfigurationType = XrViewConfigurationType.XrViewConfigurationTypePrimaryStereo,
            Space = _headSpace.Value!,
            DisplayTime = _predictedTime
        };
        using var viewState = new XrViewState { Type = XrStructureType.XrTypeViewState };
        using var firstView = _views[0];
        _ = XrApi.XrLocateViews(_session!.Value!, viewInfo, viewState, 2, out _, firstView);
    }

    private void SynchronizeActions()
    {
        if (ActiveActionSet is not OpenXRActionGroup actionSet)
            return;
        actionSet.Synchronize(_session!.Value!);
        var bindings = actionSet.GetBindings();
        for (var index = 0; index < bindings.Count; ++index)
        {
            if (bindings[index] is OpenXRBinding binding)
                binding.Update(_session.Value!, ScaleCorrection);
        }
    }

    private void UpdateHands()
    {
        if (!Rig.IsValid())
            return;

        // Check for changes in controller model state, if so, do reload as required.
        UpdateControllerModels();

        var leftHand = VRRigDesc.TryGet(Rig.LeftHandPose)!;
        var rightHand = VRRigDesc.TryGet(Rig.RightHandPose)!;
        var leftAim = VRRigDesc.TryGet(Rig.LeftHandAim)!;
        var rightAim = VRRigDesc.TryGet(Rig.RightHandAim)!;

        // We need valid handles for these guys.
        if (!TryGetHandBinding(_handGrips, VRHand.Left, out var leftBinding) ||
            !TryGetHandBinding(_handGrips, VRHand.Right, out var rightBinding))
            return;

        // These fields are super important to rationalize what's happened between sample points.
        // Sensor reads are effectively Planck timing it between quantum space-time.
        leftHand.SetVar("PreviousTransformLocal", leftHand.GetTransformMatrix());
        leftHand.SetVar("PreviousTransformWorld", leftHand.GetWorldTransform());
        leftHand.SetEnabled((leftBinding.Location.LocationFlags & (PositionValid | PositionTracked)) != 0);
        leftHand.SetPosition(GetBindingPosition(leftBinding));
        if ((leftBinding.Location.LocationFlags & (OrientationValid | OrientationTracked)) != 0)
            leftHand.SetRotation(GetBindingRotation(leftBinding));

        rightHand.SetVar("PreviousTransformLocal", leftHand.GetTransformMatrix());
        rightHand.SetVar("PreviousTransformWorld", leftHand.GetWorldTransform());
        rightHand.SetEnabled((rightBinding.Location.LocationFlags & (PositionValid | PositionTracked)) != 0);
        rightHand.SetPosition(GetBindingPosition(rightBinding));
        if ((rightBinding.Location.LocationFlags & (OrientationValid | OrientationTracked)) != 0)
            rightHand.SetRotation(GetBindingRotation(rightBinding));

        // Setup aim nodes too.
        leftAim.SetTransformMatrix(GetHandAimTransform(VRHand.Left));
        rightAim.SetTransformMatrix(GetHandAimTransform(VRHand.Right));
    }

    private void UpdateControllerModels()
    {
        if (!_controllerModel || _session?.Value is not { } session)
            return;

        foreach (var model in _controllerModels)
            model?.UpdateModel(session);
        UpdateControllerModel(VRHand.Left, VRRigDesc.TryGet(Rig.LeftController), session);
        UpdateControllerModel(VRHand.Right, VRRigDesc.TryGet(Rig.RightController), session);
    }

    private void UpdateControllerModel(VRHand hand, Node? instanceNode, XrSessionT session)
    {
        var model = _controllerModels[(int)hand];
        if (model is null || instanceNode is null)
            return;

        var prefabReference = instanceNode.GetOrCreateComponent<PrefabReference>();
        if (!ReferenceEquals(prefabReference.GetPrefab(), model.Prefab))
        {
            prefabReference.SetPrefab(model.Prefab);
            var eventData = new VREvents.VRControllerChangedEvent { Hand = hand };
            SendEvent(ref eventData);
        }

        instanceNode.SetRotation(new Quaternion(180.0f, Vector3f.Up));
        model.UpdateTransforms(session, instanceNode);
    }

    private unsafe bool TryEndFrameWithProjectionLayer(XrFrameEndInfo endInfo)
    {
        using var leftView = _views[0];
        using var rightView = _views[1];
        if (leftView.FovAngleLeft == 0 || leftView.FovAngleRight == 0 || leftView.FovAngleUp == 0 ||
            leftView.FovAngleDown == 0 || rightView.FovAngleLeft == 0 || rightView.FovAngleRight == 0 ||
            rightView.FovAngleUp == 0 || rightView.FovAngleDown == 0)
            return false;

        ConfigureProjectionView(0, leftView, 0);
        ConfigureProjectionView(1, rightView, EyeTextureSize.X);
        using var firstEye = _eyes[0];
        _projectionLayer.Space = _headSpace!.Value!;
        _projectionLayer.ViewCount = 2;
        _projectionLayer.Views = firstEye;

        Span<nint> layers = stackalloc nint[1] { _projectionLayer.Handle };
        fixed (nint* layersPtr = layers)
        {
            using var layerArray = XrCompositionLayerBaseHeader.CreateManagedWrapper((nint)layersPtr, false);
            endInfo.LayerCount = 1;
            endInfo.Layers = layerArray;
            _ = OpenXRPrimitives.Check(XrApi.XrEndFrame(_session!.Value!, endInfo), nameof(XrApi.XrEndFrame));
        }
        return true;
    }

    private void ConfigureProjectionView(int index, XrView view, int offsetX)
    {
        using var eye = _eyes[index];
        eye.SubImageImageArrayIndex = 0;
        eye.SubImageSwapchain = _swapChain!.Handle;
        eye.SubImageImageRectOffsetX = offsetX;
        eye.SubImageImageRectOffsetY = 0;
        eye.SubImageImageRectExtentWidth = EyeTextureSize.X;
        eye.SubImageImageRectExtentHeight = EyeTextureSize.Y;
        eye.FovAngleLeft = view.FovAngleLeft;
        eye.FovAngleRight = view.FovAngleRight;
        eye.FovAngleDown = view.FovAngleDown;
        eye.FovAngleUp = view.FovAngleUp;
        eye.PoseOrientationX = view.PoseOrientationX;
        eye.PoseOrientationY = view.PoseOrientationY;
        eye.PoseOrientationZ = view.PoseOrientationZ;
        eye.PoseOrientationW = view.PoseOrientationW;
        eye.PosePositionX = view.PosePositionX;
        eye.PosePositionY = view.PosePositionY;
        eye.PosePositionZ = view.PosePositionZ;

        if (_depthChain is null)
        {
            eye.Next = 0;
            return;
        }

        var depth = _depth[index];
        depth.SubImageImageArrayIndex = 0;
        depth.SubImageSwapchain = _depthChain.Handle;
        depth.SubImageImageRectOffsetX = offsetX;
        depth.SubImageImageRectOffsetY = 0;
        depth.SubImageImageRectExtentWidth = EyeTextureSize.X;
        depth.SubImageImageRectExtentHeight = EyeTextureSize.Y;
        depth.MinDepth = 0.0f;
        depth.MaxDepth = 1.0f;
        depth.NearZ = Rig.NearDistance;
        depth.FarZ = Rig.FarDistance;
        eye.Next = depth.Handle;
    }

    private void UpdateBindingBound()
    {
        if (_session?.Value is not { } session || ActiveActionSet is null)
            return;
        Array.Clear(_handGrips);
        Array.Clear(_handAims);
        var bindings = ActiveActionSet.GetBindings();
        for (var index = 0; index < bindings.Count; ++index)
        {
            if (bindings[index] is not OpenXRBinding binding)
                continue;
            binding.UpdateBoundState(session);
            if (binding.Hand is VRHand.Left or VRHand.Right)
            {
                if (binding.IsGripPose)
                    _handGrips[(int)binding.Hand] = binding;
                if (binding.IsAimPose)
                    _handAims[(int)binding.Hand] = binding;
            }
        }
    }

    private bool FailSystemInitialization()
    {
        _graphicsAttributes?.Dispose();
        _graphicsAttributes = null;
        _instance?.Dispose();
        _instance = null;
        _system = 0;
        return false;
    }

    private static bool TryGetHandBinding(OpenXRBinding?[] bindings, VRHand hand, out OpenXRBinding binding)
    {
        binding = null!;
        if (hand is not (VRHand.Left or VRHand.Right))
            return false;
        binding = bindings[(int)hand]!;
        return binding is not null;
    }

    private static Matrix3x4 GetBindingTransform(OpenXRBinding binding) => new(
        GetBindingPosition(binding), GetBindingRotation(binding), 1.0f);

    private static Vector3f GetBindingPosition(OpenXRBinding binding) => OpenXRPrimitives.ToVector3(
        binding.Location.PosePositionX, binding.Location.PosePositionY, binding.Location.PosePositionZ);

    private static Quaternion GetBindingRotation(OpenXRBinding binding) => OpenXRPrimitives.ToQuaternion(
        binding.Location.PoseOrientationX, binding.Location.PoseOrientationY,
        binding.Location.PoseOrientationZ, binding.Location.PoseOrientationW);

    private static Matrix3x4 ToMatrix3x4(XrView view, float scale) => new(
        OpenXRPrimitives.ToVector3(view.PosePositionX, view.PosePositionY, view.PosePositionZ),
        OpenXRPrimitives.ToQuaternion(view.PoseOrientationX, view.PoseOrientationY,
            view.PoseOrientationZ, view.PoseOrientationW), scale);
}
