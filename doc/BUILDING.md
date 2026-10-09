# Building RedBolt.XR

Keep this repository beside RedBolt and RedBolt.Tests. The HelloVR sample also requires RedBolt.PhysicsJolt:

```text
RedBolt/
RedBolt.PhysicsJolt/
RedBolt.Tests/
RedBolt.XR/
```

Build the Linux x64 module and HelloVR sample:

```sh
ps -eo pid=,comm=,args= | rg '[e]mulator|[q]emu-system' || true
dotnet build src/RedBolt/Sample/HelloVR/RedBolt.Sample.HelloVR.csproj \
  --configuration Debug -m:4
```

For Windows x64 cross-builds, configure the Wine 11 and LLVM-MinGW paths required by RedBolt before the first build, then
build the same sample with the Windows platform selection:

```sh
export PATH=/home/rk/.local/opt/wine-11.14-amd64-wow64/bin:/home/rk/.local/opt/llvm-mingw-20260616-msvcrt-ubuntu-22.04-x86_64/bin:/home/rk/.local/mingw-w64/usr/bin:$PATH
dotnet build src/RedBolt/Sample/HelloVR/RedBolt.Sample.HelloVR.csproj \
  --configuration Release -m:4 \
  -p:RedBoltPlatform=win-x64
```

Build the Android arm64 application with the workspace JDK, SDK, and NDK:

```sh
export XDG_DATA_HOME=/home/rk/.local/share
export JAVA_HOME=/home/rk/.local/share/redbolt-android/jdk
export ANDROID_HOME=/home/rk/.local/share/redbolt-android/sdk
export ANDROID_SDK_ROOT=/home/rk/.local/share/redbolt-android/sdk
export ANDROID_NDK_ROOT=/home/rk/.local/share/redbolt-android/sdk/ndk/27.2.12479018
dotnet build src/RedBolt/Sample/Android/RedBolt.Sample.Android.csproj \
  --configuration Release --framework net10.0-android -m:4 \
  -p:RedBoltPlatform=android-arm64 \
  -p:RedBoltBuildAllAndroidPackages=false
```

The XR sample is standalone (`"standalone": true` in `src/RedBolt/Sample/SampleCatalog.json`): it is built and installed only from its own
generated host above and is never part of the aggregate `Samples/` Android host.

Run one build invocation at a time. Use no more than four internal jobs, and use one job while an Android emulator or
QEMU process is running. The aggregate solution accepts the same `RedBoltEnable`, module, and sample selection
properties as RedBolt and saves its selection to the active build root's `BuildConfig.props`: the aggregate solution shares `build/BuildConfig.props` with the root solution, because both use the same build root.

The build root is selected statically rather than recorded: every entry point (solution or direct project) uses the `build/` directory beside the engine directory (the monorepo `build/` in the monorepo), and `-p:RedBoltBuildRoot=<dir>` overrides it.
