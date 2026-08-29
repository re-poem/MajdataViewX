# macOS native build

## Requirements

- Unity `6000.3.19f1`
- macOS Build Support (IL2CPP)
- An activated Unity license

## Build

Run the checked-in build entry point from the repository root:

```sh
/Applications/Unity/Hub/Editor/6000.3.19f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "$PWD" \
  -activeBuildProfile "Assets/Settings/Build Profiles/macOS-arm64.asset" \
  -executeMethod MacBuild.Build \
  -logFile -
```

The output is `Builds/macOS/MajdataViewX.app`. The build targets Apple Silicon, matching the `osx-arm64` MajdataEdit-Neo app.

`SFX` and `Skin` are release assets rather than source files. The MajdataEdit-Neo macOS packager copies them into `MajdataViewX.app/Contents/MacOS`, where the macOS player resolves release assets at runtime.

Video recording remains Windows-only. The macOS player returns an explicit error instead of loading `RenderingOut.dll`.
