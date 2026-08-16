// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

#:project ../../../RedBolt/src/RedBolt/Build/Packaging/RedBolt.ExternalModule.Packaging.csproj

using System.Runtime.CompilerServices;
using RedBolt.ExternalModule.Packaging;

return ExternalModulePackager.Run(args, new ExternalModulePackageManifest
{
    Name = "RedBolt.XR",
    BasePackages = ["RedBolt.Core", "SharpCpp.Std", "SharpCpp.Support"],
    RequireBaseRuntime = true,
    SupportedRids = ["linux-x64", "win-x64", "android-x64", "android-arm64"],
    DefaultRids = ["linux-x64"],
    BuildSteps =
    [
        new("Native/RedBolt.XR.Native.csproj", BuildWhen.Linux),
        new("Native/OpenXR/RedBolt.Bindings.OpenXR.csproj", BuildWhen.Linux, true),
        new("XR/RedBolt.XR.csproj", BuildWhen.Linux, true, "-p:BuildProjectReferences=false"),
        new("XR/RedBolt.XR.csproj", BuildWhen.Windows, false,
            "-p:RedBoltPlatform=win-x64", "-p:RedBoltNativePreset=win-x64"),
        new("Sample/Android/RedBolt.Sample.Android.csproj", BuildWhen.Android, false,
            "--framework", "net10.0-android", "-p:RedBoltEnablePlatforms=android",
            "-p:RedBoltEnableArchitectures=all")
    ],
    ManagedPackages =
    [
        new(
            "RedBolt.XR",
            "OpenXR virtual-reality integration for RedBolt.",
            [
                new("build/bin/Release/linux-x64/core/RedBolt.XR.dll", "lib/net10.0/RedBolt.XR.dll"),
                new("build/bin/Release/linux-x64/extern/RedBolt.Bindings.OpenXR.dll",
                    "lib/net10.0/RedBolt.Bindings.OpenXR.dll")
            ],
            [
                new("RedBolt.Core", PackageVersions.RedBolt),
                new("SharpCpp.Std", PackageVersions.RedBolt),
                new("SharpCpp.Support", PackageVersions.RedBolt)
            ])
    ],
    RuntimePackage = new(
        "RedBolt.XR.Runtime",
        "OpenXR",
        "*",
        "",
        [
            new("RedBolt.XR", PackageVersions.Module),
            new("RedBolt.Core.Runtime.{rid}", PackageVersions.RedBolt)
        ]),
    Consumer = new(
        "RedBolt.XR.Runtime.linux-x64",
        """
        using System;
        using RedBolt.XR;
        Console.WriteLine(typeof(VirtualReality).FullName);
        """,
        OnlyWithLinuxRuntime: true)
}, SourcePath());

static string SourcePath([CallerFilePath] string path = "") => path;
