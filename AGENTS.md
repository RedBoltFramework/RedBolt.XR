# RedBolt.XR workspace instructions

This repository contains the optional OpenXR module ported from the sibling rbfx source tree.

- RedBolt is expected at `../RedBolt` unless `RedBoltRoot` is supplied; tests remain in `../RedBolt.Tests`.
- RedBolt.PhysicsJolt is expected at `../RedBolt.PhysicsJolt` for the HelloVR sample only.
- Preserve the public `RedBolt.XR` and `RedBolt.Bindings.OpenXR` assemblies and namespaces.
- Use generated SharpCpp bindings for OpenXR. Do not add manual P/Invoke bindings.
- Keep Linux x64 host builds, Windows x64, and Android arm64 working. WebAssembly is unsupported.
- Run build invocations sequentially. Internal build parallelism may use at most four jobs, and only when no Android emulator or QEMU process is running.
- Build the smallest affected project and run focused XR tests after implementation changes.
- At completion, build all supported platform/configuration cells, create and validate packages, run focused tests, and review HelloVR on an OpenXR runtime.
