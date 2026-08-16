# XR porting checklist

Source baseline: rbfx commit `8833f54b0de75c9c0a65ef71d8a66074bd51b03a`.

- [x] `VREvents.h`
- [x] `VRUtils.h` / `VRUtils.cpp`
  - [x] `ButtonCommand`
- [x] `VirtualReality.h` / `VirtualReality.cpp`
  - [x] `VRSessionParameters`
  - [x] `VRRigDesc`
  - [x] `VRRuntime`, `VRHand`, `VREye`
  - [x] `XRBinding`
  - [x] `XRActionGroup`
  - [x] `VirtualReality`
- [x] `VRRig.h` / `VRRig.cpp`
- [-] `OpenXRAPI.h` / `OpenXRAPI.cpp` — replaced by generated OpenXR interop
- [x] `OpenXR.h` / `OpenXR.cpp`
  - [x] OpenXR handle ownership
  - [x] graphics bindings and swapchains
  - [x] action bindings and manifests
  - [x] controller models
  - [x] session and frame lifecycle
- [x] `120_HelloVR`
- [ ] Windows x64 headset validation — requires a host with an active OpenXR runtime
- [ ] Android arm64 Quest validation — requires a connected Quest device
