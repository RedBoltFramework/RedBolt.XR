# RedBolt.XR

OpenXR integration for [RedBolt](https://github.com/RedBoltFramework/RedBolt), ported from rbfx. The module owns the
generated OpenXR bindings, native dispatch wrapper, managed VR subsystem, default action manifest, and HelloVR sample.

The source build expects RedBolt in `../RedBolt`. The HelloVR sample also expects RedBolt.PhysicsJolt in
`../RedBolt.PhysicsJolt`.

```sh
dotnet build RedBolt.XR.slnx --configuration Debug
```

Register XR during the existing application setup phase, before the engine creates its graphics device:

```csharp
protected override void Setup(EngineParameters parameters)
{
    XR.RegisterVRLibrary(Context);
    parameters.XR = true;
}
```

Initialize the session from `Start()` with the existing `VirtualReality.InitializeSession` API. No separate application
bootstrap API is required.

Linux x64 and Windows x64 desktop builds and Android arm64 packages are supported in Debug and Release. WebAssembly is
unsupported. Tests remain in the sibling RedBolt.Tests repository. See [building](doc/BUILDING.md),
[package production](packaging/nuget/README.md), and the [port-specific deviations](doc/ALLOWED_DEVIATIONS.md).
