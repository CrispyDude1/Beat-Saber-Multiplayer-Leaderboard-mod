# Building Multiplayer Leaderboard

The mod is a managed Beat Saber plugin. No game assemblies are copied into the repository or the
output; they are referenced from an installed game at build time.

## Requirements

* Windows (the build targets .NET Framework 4.7.2).
* Beat Saber **1.40.3** with **BSIPA 4.3.0+** and **BeatSaberMarkupLanguage 1.12.5+** installed.
* .NET SDK **8.0+** with the **.NET Framework 4.7.2 targeting pack**. On Windows this comes with
  Visual Studio's .NET desktop workload, or with the SDK when the targeting pack is already
  installed.
* No NuGet packages are needed by the plugin itself. The `tools/Verify` project restores
  `Mono.Cecil` from NuGet.

## Build

From the repository root:

```bash
./build.sh "/path/to/Beat Saber"
```

or directly:

```bash
dotnet build src/MultiplayerLeaderboard/MultiplayerLeaderboard.csproj -c Release \
  -p:BeatSaberDir="/path/to/Beat Saber"
```

The DLL is written to `src/MultiplayerLeaderboard/bin/Release/MultiplayerLeaderboard.dll`.
`build.sh` also copies it and the documentation into `dist/`.

`BeatSaberDir` can also be supplied as `BEATSABER_DIR`. When the repository sits inside the game
folder, `./build.sh` with no argument uses the parent directory.

## Verify the compiled DLL

```bash
dotnet run --project tools/Verify -c Release -- \
  --dll src/MultiplayerLeaderboard/bin/Release/MultiplayerLeaderboard.dll \
  --game "/path/to/Beat Saber"
```

The verifier checks the embedded manifest, the BSIPA plugin contract, every referenced game
member and assembly, and that no other plugin or absolute developer path is referenced.

## Install for a local test

Copy `MultiplayerLeaderboard.dll` into `<Beat Saber>/Plugins/` and restart the game. Do not copy a
loose `manifest.json`; it is embedded in the DLL.
