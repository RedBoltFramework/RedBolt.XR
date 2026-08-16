# Allowed deviations

These deviations apply to the XR module extracted from RedBolt:

- XR is an optional sibling assembly. Applications register its startup extension during `Application.Setup`; RedBolt.Core does not reference RedBolt.XR.
- `OpenXRAPI.h` is replaced by generated SharpCpp bindings to the pinned Khronos OpenXR SDK and loader.
- Diligent's OpenXR graphics-binding and swapchain-image helpers replace backend-specific raw native handle access and the SDL2 EGL configuration workaround.
- The rbfx `OpenXRTweaks` object is folded into the registered OpenXR subsystem. It uses the existing engine frame-rate and VSync settings during graphics initialization instead of adding another public initialization API.
- HelloVR's debug overlay uses RedBolt.DearImGui in place of rbfx's legacy UI debug controls.
- OpenXR extension entry points not exported by the loader are resolved through `xrGetInstanceProcAddr` by the generated native dispatch helpers.
