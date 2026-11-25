# Building for Android

The Windows build does not need anything on this page.

## What you need

- The .NET 11 SDK with the Android workload.
- The Android SDK (platform tools, a platform the workload accepts, build tools, accepted licenses: `sdkmanager --licenses`)
  and a JDK.
- FFmpeg shared libraries (below).

## FFmpeg

Run `scripts\get-ffmpeg-android.ps1` once. It downloads LGPL builds of FFmpeg 8.1 from
[tqk2811/FFmpegBuild](https://github.com/tqk2811/FFmpegBuild), checks their SHA-256 and unpacks the libraries into
`src/Pardeh.Android/lib/<abi>/`. The build itself downloads nothing, and the `.so` files are git-ignored. The builds are
made by a third party and are not signed.

To use your own LGPL build, provide for each ABI (`arm64-v8a`, and `x86_64` for the emulator):

```
libavcodec.so libavformat.so libavutil.so libswresample.so libswscale.so libc++_shared.so
```

- File names have no version suffix (Android only packages `lib*.so`).
- The major versions are the ones `FFmpeg.AutoGen 8.1.0` expects: avcodec 62, avformat 62, avutil 60, swresample 6, swscale 9.
- Build with `--enable-shared --disable-static`, against API level 24 or lower.

Without the libraries the app starts, but cannot decode media.

## Debug build

```powershell
dotnet build src/Pardeh.Android -f net11.0-android `
  -p:AndroidSdkDirectory=<Android SDK folder> `
  -p:JavaSdkDirectory=<JDK folder>
```

## Release build

You need your own signing key. Keep the keystore and its passwords outside the repository, and back them up: Android only
installs an update over an installed app when both are signed with the same key.

Describe the key in `$HOME\.pardeh\keystore.properties` (or pass `-KeystoreProperties`, or set
`PARDEH_KEYSTORE_PROPERTIES`) with the keys `storeFile`, `storePassword`, `keyAlias` and `keyPassword`. Then:

```powershell
.\scripts\publish-android.ps1                  # arm64-v8a; -Abi x86_64 or -Abi all
.\scripts\release-android-arm64.ps1 -Install   # arm64 only, then install over USB
```

The signed APK goes to `artifacts/android/`. The passwords are handed to the build through environment variables and are
not printed. For each release you hand out, raise `ApplicationVersion` in `src/Pardeh.Android/Pardeh.Android.csproj`.

## Branding

The launcher name and icon are Pardeh's own. `-ClassicBranding $true` (scripts) or `-p:ClassicBranding=true`
(`dotnet build`) selects the classic launcher name and icon instead, for a personal build.
