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

## Shared Assembly Hub operation pipeline

A surgical operation has two distinct stages:

1. It is registered in the global `SurgicalQueue`.
2. The queue asks the patient's `BodyComp.TryOrderOp` to create the patient-side AI goal. The goal carries the surgery type and target slot ID through `Surgery_GoTo` / `CarryPatient`, then loads the patient into a `BodyModSpotComp` (Assembly Hub).

The stock queue normally waits while the patient already has an AI goal. The stock single-slot `BodyComp.TryOrderOp` path also only handles vanilla `Remove`, `Install`, and `Swap` operations. Custom single-slot operations must therefore bridge both the task-creation step and the `ResolveOpType` flags that indicate a target body slot is required. The two C# test mods now contain that bridge and try to start the queue head immediately after the player orders it. They interrupt an unrelated interruptible patient goal, but do not interrupt an already-running surgery or a non-interruptible goal.

The Assembly Hub operator is a separate AI job. The robotic Assembly Hub uses the `Robotics` job type; assigning a patient-side surgery goal does not itself assign an operator to the hub.

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

**Baseline build verified on 2026-11-25:** both C# projects compiled successfully against the local **Stardeus 0.15.11b** Managed directory before the latest AI task-routing bridge was added.

**Current source status:** the bridge changes now in `main` have **not yet been rebuilt** against the local game DLLs. Re-run both commands above before treating the current revision as compile-verified.

The earlier baseline builds finished with **five warnings** per project and no C# compilation errors:

- `MSB3245`: the build resolver reports that it cannot resolve `System.Runtime`.
- `MSB3277`: reference-version conflicts are reported for:
  - `System.Collections.Immutable` 8.0.0.0 vs 9.0.0.0 (8.0.0.0 selected);
  - `System.Reflection.Metadata` 8.0.0.0 vs 9.0.0.0 (8.0.0.0 selected);
  - `System.Threading.Tasks.Extensions` 4.2.0.0 vs 4.2.1.0 (4.2.0.0 selected);
  - `System.Text.Encoding.CodePages` 4.1.0.0 vs 8.0.0.0 (4.1.0.0 selected).

The current project files reference the game's `Managed\*.dll` set. The warning details show that this reference graph includes `Microsoft.CodeAnalysis.*` (Roslyn) assemblies, which request newer versions of some libraries than the versions selected from the game's Managed directory. The exact reference set still needs a deliberate review; the warnings have not been hidden or force-resolved.

These warnings did **not** prevent either baseline DLL from being generated in the reported build. They should not be confused with proof of successful in-game loading or operation behavior. Runtime testing remains a separate step.

## Validation boundary

Compilation confirms that the current C# source can be compiled against the supplied local game assemblies. It does not confirm that Harmony patches apply successfully, that the UI operations appear, or that the operations complete correctly in a live save.

Use a copied/test save when validating experimental surgery and body-part operations. See each mod's README for its specific test checklist and known limitations.
