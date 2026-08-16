# Copyright (c) 2026 RedBolt and contributors.
# This work is licensed under the terms of the MIT license.

redbolt_add_native_binding_generation(
    TARGET redbolt-generate-openxr-bindings
    SURFACE openxr
    HEADER "${REDBOLT_OPENXR_PARSE_HEADER}"
    HEADER_INCLUDE_PATH "OpenXRAll.h"
    FILE_STEM "${REDBOLT_OPENXR_BINDINGS_STEM}"
    OUTPUT_DIR "${REDBOLT_OPENXR_GENERATED_DIR}"
    OUTPUT_METADATA "${REDBOLT_OPENXR_BINDINGS_JSON}"
    OUTPUT_CS "${REDBOLT_OPENXR_BINDINGS_CS}"
    OUTPUT_CPP "${REDBOLT_OPENXR_BINDINGS_CPP}"
    NATIVE_LIBRARY_NAME "${REDBOLT_OPENXR_NATIVE_LIBRARY_NAME}"
    INCLUDE_DIRS ${REDBOLT_OPENXR_BINDING_INCLUDE_DIRS}
    DEFINES ${REDBOLT_BINDING_DEFINES}
    OWNED_HEADERS
        "${REDBOLT_OPENXR_PARSE_HEADER}"
        "${REDBOLT_OPENXR_EXTENSION_HEADER}"
        "${REDBOLT_OPENXR_INCLUDE_DIR}/openxr")

add_dependencies(redbolt-generate-openxr-bindings openxr_loader)
