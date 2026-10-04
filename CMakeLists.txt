# Copyright (c) 2026 RedBolt and contributors.
# This work is licensed under the terms of the MIT license.

cmake_minimum_required(VERSION 3.28)

set(CMAKE_POLICY_VERSION_MINIMUM 3.5 CACHE STRING "Minimum CMake policy version accepted by dependencies")

project(RedBoltXR VERSION 0.1.0 LANGUAGES C CXX)

set(BUILD_SHARED_LIBS OFF CACHE BOOL "Build dependencies statically" FORCE)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_SCAN_FOR_MODULES OFF)

if(ANDROID)
    add_compile_options(-fsigned-char)
    add_link_options("-Wl,-z,max-page-size=16384")
endif()

if(MINGW)
    set(CMAKE_CXX_LINK_LIBRARY_USING_WHOLE_ARCHIVE
        "LINKER:--whole-archive"
        "<LINK_ITEM>"
        "LINKER:--no-whole-archive")
    set(CMAKE_CXX_LINK_LIBRARY_USING_WHOLE_ARCHIVE_SUPPORTED TRUE)
endif()

set(REDBOLT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../RedBolt" CACHE PATH "RedBolt source root")
set(REDBOLT_SHARPCPP_ROOT "${REDBOLT_ROOT}/extern/SharpCpp" CACHE PATH "SharpCpp source root")
set(REDBOLT_DEPENDENCY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/extern" CACHE PATH "RedBolt.XR dependency cache")
set(REDBOLT_GENERATED_ROOT "${CMAKE_BINARY_DIR}/generated")
set(REDBOLT_NATIVE_GENERATED_DIR "${REDBOLT_GENERATED_ROOT}/redbolt-xr-native")
set(REDBOLT_RUNTIME_OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/build/bin/$<CONFIG>/linux-x64/extern/runtimes/linux-x64/native" CACHE PATH "Native runtime output")
set(REDBOLT_PDB_OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/build/bin/$<CONFIG>/linux-x64/extern/pdb" CACHE PATH "Native PDB output")

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)

list(PREPEND CMAKE_MODULE_PATH "${REDBOLT_ROOT}/cmake/Modules")
set(CPM_DONT_UPDATE_MODULE_PATH ON CACHE BOOL "Use RedBolt CMake modules" FORCE)
set(CPM_SOURCE_CACHE "${REDBOLT_DEPENDENCY_ROOT}" CACHE PATH "CPM source cache" FORCE)
unset(CPM_DIRECTORY CACHE)
unset(CPM_VERSION CACHE)
include("${REDBOLT_ROOT}/cmake/CPM.cmake")
set(FETCHCONTENT_BASE_DIR "${CMAKE_BINARY_DIR}/deps")

include("${REDBOLT_ROOT}/cmake/RedBoltCPM.cmake")
include("${REDBOLT_ROOT}/cmake/RedBoltSharpCpp.cmake")
include("${REDBOLT_ROOT}/cmake/RedBoltNativeModuleManifest.cmake")

include("${REDBOLT_ROOT}/extern/OpenXR.cmake")
set(REDBOLT_OPENXR_NATIVE_LIBRARY_NAME "RedBolt.Native.OpenXR")

if(ANDROID)
    if(ANDROID_ABI STREQUAL "x86_64")
        set(REDBOLT_BINDING_TARGET_TRIPLE "x86_64-linux-android24")
    elseif(ANDROID_ABI STREQUAL "arm64-v8a")
        set(REDBOLT_BINDING_TARGET_TRIPLE "aarch64-linux-android24")
    else()
        message(FATAL_ERROR "Unsupported Android ABI: ${ANDROID_ABI}")
    endif()
    get_filename_component(_redbolt_android_compiler_bin "${CMAKE_CXX_COMPILER}" DIRECTORY)
    get_filename_component(_redbolt_android_toolchain_root "${_redbolt_android_compiler_bin}" DIRECTORY)
    set(REDBOLT_BINDING_SYSROOT "${_redbolt_android_toolchain_root}/sysroot")
endif()

set(REDBOLT_BINDING_CXX_COMPILER "${CMAKE_CXX_COMPILER}")
set(REDBOLT_OPENXR_BINDING_INCLUDE_DIRS ${REDBOLT_OPENXR_INCLUDE_DIRS})
set(_redbolt_openxr_binding_defines)
if(NOT ANDROID)
    list(APPEND REDBOLT_OPENXR_BINDING_INCLUDE_DIRS
        "${REDBOLT_ROOT}/extern/Diligent/ThirdParty/Vulkan-Headers/include")
endif()
if(WIN32)
    list(APPEND _redbolt_openxr_binding_defines
        XR_USE_PLATFORM_WIN32=1
        XR_USE_GRAPHICS_API_D3D11=1
        XR_USE_GRAPHICS_API_D3D12=1
        XR_USE_GRAPHICS_API_OPENGL=1
        XR_USE_GRAPHICS_API_VULKAN=1)
elseif(ANDROID)
    list(APPEND _redbolt_openxr_binding_defines
        XR_USE_PLATFORM_ANDROID=1
        XR_USE_GRAPHICS_API_OPENGL_ES=1)
else()
    list(APPEND _redbolt_openxr_binding_defines XR_USE_GRAPHICS_API_VULKAN=1)
endif()
set(REDBOLT_BINDING_DEFINES ${_redbolt_openxr_binding_defines})

add_subdirectory(src/RedBolt/Generator/Dependencies)
add_subdirectory(src/RedBolt/Native/OpenXR)

add_library(redbolt-xr-native-openxr SHARED "${REDBOLT_OPENXR_BINDINGS_CPP}")
add_dependencies(redbolt-xr-native-openxr redbolt-generate-openxr-bindings)
target_link_libraries(redbolt-xr-native-openxr PRIVATE SharpCpp::NativeSupport openxr_loader)
target_include_directories(redbolt-xr-native-openxr PRIVATE ${REDBOLT_OPENXR_BINDING_INCLUDE_DIRS})
target_compile_definitions(redbolt-xr-native-openxr PRIVATE REDBOLT_NATIVE_BUILD ${_redbolt_openxr_binding_defines})
if(MINGW)
    target_compile_options(redbolt-xr-native-openxr PRIVATE "-Wa,-mbig-obj")
endif()
set_target_properties(redbolt-xr-native-openxr PROPERTIES
    OUTPUT_NAME "${REDBOLT_OPENXR_NATIVE_LIBRARY_NAME}"
    RUNTIME_OUTPUT_DIRECTORY "${REDBOLT_RUNTIME_OUTPUT_DIR}"
    LIBRARY_OUTPUT_DIRECTORY "${REDBOLT_RUNTIME_OUTPUT_DIR}"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib/$<CONFIG>"
    WINDOWS_EXPORT_ALL_SYMBOLS ON
    C_VISIBILITY_PRESET default)
if(NOT WIN32 AND NOT ANDROID)
    set_target_properties(redbolt-xr-native-openxr PROPERTIES
        BUILD_RPATH "$ORIGIN"
        INSTALL_RPATH "$ORIGIN"
        INSTALL_RPATH_USE_LINK_PATH OFF
        BUILD_WITH_INSTALL_RPATH TRUE)
endif()

if(CMAKE_CONFIGURATION_TYPES)
    foreach(_redbolt_xr_configuration IN LISTS CMAKE_CONFIGURATION_TYPES)
        string(TOUPPER "${_redbolt_xr_configuration}" _redbolt_xr_configuration_upper)
        set_target_properties(redbolt-xr-native-openxr PROPERTIES
            RUNTIME_OUTPUT_DIRECTORY_${_redbolt_xr_configuration_upper} "${REDBOLT_RUNTIME_OUTPUT_DIR}"
            LIBRARY_OUTPUT_DIRECTORY_${_redbolt_xr_configuration_upper} "${REDBOLT_RUNTIME_OUTPUT_DIR}")
    endforeach()
endif()

if(WIN32)
    set_target_properties(redbolt-xr-native-openxr PROPERTIES PREFIX "")
endif()

# The OpenXR loader is linked statically on Windows and shipped as a shared library elsewhere.
set(_redbolt_xr_runtime_artifacts)
if(NOT WIN32)
    set(_redbolt_xr_runtime_artifacts "OPENXR=$<TARGET_SONAME_FILE_NAME:openxr_loader>")
endif()
redbolt_write_native_module_manifest(
    MODULE OPENXR
    TARGET redbolt-xr-native-openxr
    SELECTOR RedBolt.Bindings.OpenXR
    LIBRARY_NAME "${REDBOLT_OPENXR_NATIVE_LIBRARY_NAME}"
    RUNTIME_ARTIFACTS ${_redbolt_xr_runtime_artifacts}
    OUTPUT_DIR "${REDBOLT_GENERATED_ROOT}/native-modules")

add_custom_target(redbolt-xr-native-all DEPENDS redbolt-xr-native-openxr)
add_custom_target(redbolt-xr-android-native DEPENDS redbolt-xr-native-openxr)
if(NOT EMSCRIPTEN)
    add_dependencies(redbolt-xr-native-all redbolt-xr-native-openxr-module)
    add_dependencies(redbolt-xr-android-native redbolt-xr-native-openxr-module)
endif()
