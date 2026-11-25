# Stardeus Mods

A collection of experimental mods and development/reference material for **Stardeus 0.15.11**.

This repository is a mod workspace, not the game's source repository. The `Core/` directory contains a reference snapshot used while inspecting game definitions and source. For compiling C# mods, use the `Managed` assemblies from the exact local Stardeus installation being targeted.

## Repository contents

| Folder | Type | Purpose |
|---|---|---|
| [`OrbotronSlotRepairMod/`](OrbotronSlotRepairMod/README.md) | C# / Harmony | Adds the experimental **Repair Damaged Slot** Assembly Hub operation for destroyed robotic body slots. The current test implementation does not charge materials. |
| [`FriendlyRobotCoreExtractionMod/`](FriendlyRobotCoreExtractionMod/README.md) | C# / Harmony + research definition | Adds a research-gated **Safe Core Extraction** operation for friendly robotic beings. |
| [`OrbotronArmorMod/`](OrbotronArmorMod/README.md) | JSON-only | Adds Orbotron armor equipment, recipes, and research definitions. It has no C# project and does not need the build steps below. |
| `Core/` | Reference data | Snapshot of game configuration and reference source used during mod development. |

Each mod folder has its own `ModInfo.json`, and its own README describes its behavior, limitations, and in-game test checklist.

## Building the C# mods

### Requirements

- Windows with PowerShell.
- The .NET SDK and command-line tool `dotnet`.
- Stardeus installed locally.
- The `Stardeus_Data/Managed` directory from the same game version you intend to build against.

The C# projects currently target **.NET Framework 4.8.1** (`net481`), use **C# 9.0**, and build as **x64** class libraries.

### Build commands

Pass the local game's `Managed` directory through the MSBuild property `StardeusManagedPath`. This is important when the game is installed outside the default Steam location.

The following commands use the paths from the current Stardeus 0.15.11b development setup. Adjust only the paths if your installation or Mods directory is different.

**OrbotronSlotRepairMod**

```powershell
dotnet build "C:\Users\user\AppData\LocalLow\Kodo Linija\Stardeus\Mods\OrbotronSlotRepairMod\.vscode\mod.csproj" "-p:StardeusManagedPath=D:\Games\Stardeus v0.15.11b\Stardeus_Data\Managed" -v:minimal
```

**FriendlyRobotCoreExtractionMod**

```powershell
dotnet build "C:\Users\user\AppData\LocalLow\Kodo Linija\Stardeus\Mods\FriendlyRobotCoreExtractionMod\.vscode\mod.csproj" "-p:StardeusManagedPath=D:\Games\Stardeus v0.15.11b\Stardeus_Data\Managed" -v:minimal
```

You can also run the same command from another checkout by replacing the absolute project path with the corresponding path to that checkout's `.vscode\mod.csproj`.

### Build output

A successful build produces these files:

- `OrbotronSlotRepairMod/Libraries/OrbotronSlotRepairMod.dll`
- `FriendlyRobotCoreExtractionMod/Libraries/FriendlyRobotCoreExtractionMod.dll`

The projects are configured not to copy the game's referenced assemblies to the mod output directory (`Private=False`). The build creates the mod DLL; it does not by itself enable the mod in the game.

To install or refresh a mod, keep its complete mod folder (including `ModInfo.json`, definitions/translations where applicable, and the built `Libraries` directory) under the game's user Mods directory. Enable it in the game's Mods menu and restart as required.

## Current compilation status

**Verified by local build report on 2026-11-25:** both C# projects compile successfully against the user's local **Stardeus 0.15.11b** Managed directory.

The builds currently finish with **five warnings** per project and no C# compilation errors:

- `MSB3245`: the build resolver reports that it cannot resolve `System.Runtime`.
- `MSB3277`: reference-version conflicts are reported for:
  - `System.Collections.Immutable` 8.0.0.0 vs 9.0.0.0 (8.0.0.0 selected);
  - `System.Reflection.Metadata` 8.0.0.0 vs 9.0.0.0 (8.0.0.0 selected);
  - `System.Threading.Tasks.Extensions` 4.2.0.0 vs 4.2.1.0 (4.2.0.0 selected);
  - `System.Text.Encoding.CodePages` 4.1.0.0 vs 8.0.0.0 (4.1.0.0 selected).

The current project files reference the game's `Managed\*.dll` set. The warning details show that this reference graph includes `Microsoft.CodeAnalysis.*` (Roslyn) assemblies, which request newer versions of some libraries than the versions selected from the game's Managed directory. The exact reference set still needs a deliberate review; the warnings have not been hidden or force-resolved.

These warnings did **not** prevent either DLL from being generated in the reported build. They should not be confused with proof of successful in-game loading or operation behavior. Runtime testing remains a separate step.

## Validation boundary

Compilation confirms that the current C# source can be compiled against the supplied local game assemblies. It does not confirm that Harmony patches apply successfully, that the UI operations appear, or that the operations complete correctly in a live save.

Use a copied/test save when validating experimental surgery and body-part operations. See each mod's README for its specific test checklist and known limitations.
