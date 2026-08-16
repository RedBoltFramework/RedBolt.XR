# Copyright (c) 2026 RedBolt and contributors.
# This work is licensed under the terms of the MIT license.

if(NOT REDBOLT_DEPENDENCIES_GENERATOR_DLL)
    set(REDBOLT_DEPENDENCIES_GENERATOR_DLL
        "${PROJECT_SOURCE_DIR}/build/bin/$<CONFIG>/host/tools/RedBolt.XR.Generator.Dependencies.dll"
        CACHE FILEPATH "RedBolt.XR dependency generator assembly")
endif()

file(GLOB_RECURSE REDBOLT_SHARPCPP_GENERATOR_INPUTS CONFIGURE_DEPENDS
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Model/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Metadata/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Parsing/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Pipeline/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Rendering/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/src/SharpCpp.Support/*.cs"
    "${REDBOLT_SHARPCPP_ROOT}/include/SharpCpp/*.h"
    "${REDBOLT_SHARPCPP_ROOT}/include/SharpCpp/*.hpp")
file(GLOB_RECURSE REDBOLT_DEPENDENCIES_GENERATOR_INPUTS CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/*.cs"
    "${CMAKE_CURRENT_SOURCE_DIR}/*.csproj"
    "${REDBOLT_ROOT}/src/RedBolt/Generator/Dependencies/BindingTargetGenerator.cs"
    "${REDBOLT_ROOT}/src/RedBolt/Generator/Dependencies/CompilerToolchainDiscovery.cs"
    "${REDBOLT_ROOT}/src/RedBolt/Generator/Dependencies/FixedWidthAliasNormalizer.cs"
    "${REDBOLT_ROOT}/src/RedBolt/Generator/Dependencies/GeneratorOptions.cs")

set(REDBOLT_SHARPCPP_GENERATOR_INPUTS "${REDBOLT_SHARPCPP_GENERATOR_INPUTS}" CACHE INTERNAL "SharpCpp generator inputs")
set(REDBOLT_DEPENDENCIES_GENERATOR_INPUTS "${REDBOLT_DEPENDENCIES_GENERATOR_INPUTS}" CACHE INTERNAL "RedBolt.XR generator inputs")

set(REDBOLT_OPENXR_GENERATED_DIR "${REDBOLT_NATIVE_GENERATED_DIR}/openxr" CACHE INTERNAL "OpenXR binding output")
set(REDBOLT_OPENXR_BINDINGS_STEM "RedBolt.Bindings.OpenXR" CACHE INTERNAL "OpenXR binding stem")
set(REDBOLT_OPENXR_BINDINGS_CS "${REDBOLT_OPENXR_GENERATED_DIR}/${REDBOLT_OPENXR_BINDINGS_STEM}.Bindings.g.cs" CACHE INTERNAL "OpenXR generated C# binding")
set(REDBOLT_OPENXR_BINDINGS_CPP "${REDBOLT_OPENXR_GENERATED_DIR}/${REDBOLT_OPENXR_BINDINGS_STEM}.Bindings.g.cpp" CACHE INTERNAL "OpenXR generated native binding")
set(REDBOLT_OPENXR_BINDINGS_JSON "${REDBOLT_OPENXR_GENERATED_DIR}/${REDBOLT_OPENXR_BINDINGS_STEM}.json" CACHE INTERNAL "OpenXR binding metadata")
set(REDBOLT_OPENXR_PARSE_HEADER "${REDBOLT_OPENXR_GENERATED_DIR}/OpenXRAll.h" CACHE INTERNAL "OpenXR parse header")
set(REDBOLT_OPENXR_EXTENSION_HEADER "${PROJECT_SOURCE_DIR}/src/RedBolt/Native/OpenXR/OpenXRExtensions.hpp"
    CACHE INTERNAL "OpenXR extension dispatch helpers")
file(MAKE_DIRECTORY "${REDBOLT_OPENXR_GENERATED_DIR}")
file(CONFIGURE OUTPUT "${REDBOLT_OPENXR_PARSE_HEADER}" CONTENT [=[
#pragma once
#if defined(XR_USE_PLATFORM_ANDROID)
#include <jni.h>
#endif
#if defined(XR_USE_PLATFORM_WIN32)
#include <windows.h>
#include <unknwn.h>
#if defined(XR_USE_GRAPHICS_API_D3D11)
#include <d3d11.h>
#endif
#if defined(XR_USE_GRAPHICS_API_D3D12)
#include <d3d12.h>
#endif
#endif
#if defined(XR_USE_GRAPHICS_API_VULKAN)
#include <vulkan/vulkan.h>
#endif
#if defined(XR_USE_GRAPHICS_API_OPENGL_ES)
#include <EGL/egl.h>
#endif
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include "@REDBOLT_OPENXR_EXTENSION_HEADER@"
]=] @ONLY)
