# RedBolt.XR workspace instructions

This repository contains the optional OpenXR module ported from the sibling rbfx source tree.

- RedBolt is expected at `../RedBolt` unless `RedBoltRoot` is supplied; tests remain in `../RedBolt.Tests`.
- RedBolt.PhysicsJolt is expected at `../RedBolt.PhysicsJolt` for the HelloVR sample only.
- Preserve the public `RedBolt.XR` and `RedBolt.Bindings.OpenXR` assemblies and namespaces.
- Use generated SharpCpp bindings for OpenXR. Do not add manual P/Invoke bindings.
- Keep Linux x64 host builds, Windows x64, and Android arm64 working. WebAssembly is unsupported.
- Follow the monorepo `doc/VERIFICATION.md`: build affected projects and run focused tests; validate affected platform samples and packages when their behavior or integration changes. Run the full supported matrix for release validation or when requested.
- For XR runtime or interaction changes, review HelloVR on an OpenXR runtime; report unavailable runtime checks explicitly.
