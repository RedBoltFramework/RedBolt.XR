// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

#pragma once

#include <stdint.h>
#include <stdio.h>

static PFN_xrGetControllerModelKeyMSFT redbolt_xr_get_controller_model_key;
static PFN_xrLoadControllerModelMSFT redbolt_xr_load_controller_model;
static PFN_xrGetControllerModelPropertiesMSFT redbolt_xr_get_controller_model_properties;
static PFN_xrGetControllerModelStateMSFT redbolt_xr_get_controller_model_state;

static inline XrResult RedBoltOpenXRLoadFunction(
    XrInstance instance, const char* name, PFN_xrVoidFunction* function)
{
    const XrResult result = xrGetInstanceProcAddr(instance, name, function);
    return XR_SUCCEEDED(result) && *function ? XR_SUCCESS : result;
}

static XrBool32 XRAPI_PTR RedBoltOpenXRDebugMessageLogger(XrDebugUtilsMessageSeverityFlagsEXT severity,
    XrDebugUtilsMessageTypeFlagsEXT types, const XrDebugUtilsMessengerCallbackDataEXT* message, void* userData)
{
    const char* level = severity & XR_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT ? "error"
        : severity & XR_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT ? "warning"
        : severity & XR_DEBUG_UTILS_MESSAGE_SEVERITY_INFO_BIT_EXT ? "info" : "debug";
    (void)types;
    (void)userData;
    fprintf(stderr, "OpenXR %s: %s: %s\n", level,
        message && message->functionName ? message->functionName : "",
        message && message->message ? message->message : "");
    return XR_FALSE;
}

static inline XrResult RedBoltOpenXRInitializeAndroidLoader(void* applicationVM, void* applicationContext)
{
#if defined(XR_USE_PLATFORM_ANDROID)
    PFN_xrVoidFunction function = NULL;
    const XrResult result = xrGetInstanceProcAddr(XR_NULL_HANDLE, "xrInitializeLoaderKHR", &function);
    if (XR_FAILED(result) || !function)
        return result;

    XrLoaderInitInfoAndroidKHR loaderInfo = {XR_TYPE_LOADER_INIT_INFO_ANDROID_KHR};
    loaderInfo.applicationVM = applicationVM;
    loaderInfo.applicationContext = applicationContext;
    return ((PFN_xrInitializeLoaderKHR)function)((const XrLoaderInitInfoBaseHeaderKHR*)&loaderInfo);
#else
    (void)applicationVM;
    (void)applicationContext;
    return XR_ERROR_FUNCTION_UNSUPPORTED;
#endif
}

static inline XrResult RedBoltOpenXRLoadControllerModelFunctions(XrInstance instance)
{
    XrResult result = RedBoltOpenXRLoadFunction(instance, "xrGetControllerModelKeyMSFT",
        (PFN_xrVoidFunction*)&redbolt_xr_get_controller_model_key);
    if (XR_FAILED(result))
        return result;
    result = RedBoltOpenXRLoadFunction(instance, "xrLoadControllerModelMSFT",
        (PFN_xrVoidFunction*)&redbolt_xr_load_controller_model);
    if (XR_FAILED(result))
        return result;
    result = RedBoltOpenXRLoadFunction(instance, "xrGetControllerModelPropertiesMSFT",
        (PFN_xrVoidFunction*)&redbolt_xr_get_controller_model_properties);
    if (XR_FAILED(result))
        return result;
    return RedBoltOpenXRLoadFunction(instance, "xrGetControllerModelStateMSFT",
        (PFN_xrVoidFunction*)&redbolt_xr_get_controller_model_state);
}

static inline XrResult RedBoltOpenXRGetControllerModelKeyMSFT(
    XrSession session, XrPath path, XrControllerModelKeyStateMSFT* state)
{
    return redbolt_xr_get_controller_model_key
        ? redbolt_xr_get_controller_model_key(session, path, state) : XR_ERROR_FUNCTION_UNSUPPORTED;
}

static inline XrResult RedBoltOpenXRLoadControllerModelMSFT(XrSession session, XrControllerModelKeyMSFT modelKey,
    uint32_t bufferCapacityInput, uint32_t* bufferCountOutput, uint8_t* buffer)
{
    return redbolt_xr_load_controller_model
        ? redbolt_xr_load_controller_model(session, modelKey, bufferCapacityInput, bufferCountOutput, buffer)
        : XR_ERROR_FUNCTION_UNSUPPORTED;
}

static inline XrResult RedBoltOpenXRGetControllerModelPropertiesMSFT(
    XrSession session, XrControllerModelKeyMSFT modelKey, XrControllerModelPropertiesMSFT* properties)
{
    return redbolt_xr_get_controller_model_properties
        ? redbolt_xr_get_controller_model_properties(session, modelKey, properties) : XR_ERROR_FUNCTION_UNSUPPORTED;
}

static inline XrResult RedBoltOpenXRGetControllerModelStateMSFT(
    XrSession session, XrControllerModelKeyMSFT modelKey, XrControllerModelStateMSFT* state)
{
    return redbolt_xr_get_controller_model_state
        ? redbolt_xr_get_controller_model_state(session, modelKey, state) : XR_ERROR_FUNCTION_UNSUPPORTED;
}

static inline XrResult RedBoltOpenXRCreateDebugMessenger(XrInstance instance, uint64_t* messengerHandle)
{
    PFN_xrVoidFunction createFunction = NULL;
    XrResult result = RedBoltOpenXRLoadFunction(instance, "xrCreateDebugUtilsMessengerEXT", &createFunction);
    if (XR_FAILED(result))
        return result;

    XrDebugUtilsMessengerCreateInfoEXT createInfo = {XR_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT};
    createInfo.userCallback = RedBoltOpenXRDebugMessageLogger;
    createInfo.messageTypes = XR_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT
        | XR_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT
        | XR_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT
        | XR_DEBUG_UTILS_MESSAGE_TYPE_CONFORMANCE_BIT_EXT;
    createInfo.messageSeverities = XR_DEBUG_UTILS_MESSAGE_SEVERITY_VERBOSE_BIT_EXT
        | XR_DEBUG_UTILS_MESSAGE_SEVERITY_INFO_BIT_EXT
        | XR_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT
        | XR_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;

    XrDebugUtilsMessengerEXT messenger = XR_NULL_HANDLE;
    result = ((PFN_xrCreateDebugUtilsMessengerEXT)createFunction)(instance, &createInfo, &messenger);
    if (XR_SUCCEEDED(result))
        *messengerHandle = (uint64_t)(uintptr_t)messenger;
    return result;
}

static inline XrResult RedBoltOpenXRDestroyDebugMessenger(XrInstance instance, uint64_t messengerHandle)
{
    PFN_xrVoidFunction destroyFunction = NULL;
    const XrResult result = RedBoltOpenXRLoadFunction(instance, "xrDestroyDebugUtilsMessengerEXT", &destroyFunction);
    if (XR_FAILED(result))
        return result;
    return ((PFN_xrDestroyDebugUtilsMessengerEXT)destroyFunction)(
        (XrDebugUtilsMessengerEXT)(uintptr_t)messengerHandle);
}
