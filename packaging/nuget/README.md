# RedBolt.XR NuGet packages

RedBolt.XR versions independently from RedBolt, while recording the exact base RedBolt package version. Produce the
matching base feed first in `<build root>/package/nuget/RedBolt/out` (`<build root>` is the `build/` directory beside the engine directory; the packaging script reads `REDBOLT_BUILD_ROOT` to override it).

Create Linux x64 packages from Release outputs, building the module first:

```sh
dotnet run --file packaging/nuget/package.cs -- create --rids linux-x64
```

Use `--no-build` when all selected outputs already exist. Select additional prebuilt targets with a comma-separated
list such as `--rids linux-x64,win-x64,android-x64,android-arm64`. Override dependency version discovery only for an
intentional local feed with `--redbolt-version VERSION`.

The command writes independently versioned packages below `<build root>/package/nuget/RedBolt.XR/out`:

- `RedBolt.XR`
- `RedBolt.XR.Runtime.<rid>`

The runtime package brings in the managed XR package, the matching `RedBolt.Core.Runtime.<rid>` package, the generated
OpenXR binding assembly, the OpenXR dispatch wrapper, and the platform OpenXR loader.

Validate package contents, exact dependency versions, and a package-only consumer with:

```sh
dotnet run --file packaging/nuget/package.cs -- validate --rids linux-x64
```
