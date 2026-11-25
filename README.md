# Stardeus Mods

A collection of experimental mods and development/reference material for **Stardeus 0.15.11**.

This repository is a mod workspace, not the game's source repository. The `Core/` directory contains a reference snapshot used while inspecting game definitions and source. For compiling C# mods, use the `Managed` assemblies from the exact local Stardeus installation being targeted.

## Repository contents

| Folder | Type | Purpose |
|---|---|---|
| [`OrbotronSlotRepairMod/`](OrbotronSlotRepairMod/README.md) | C# / Harmony + research definition | Adds **Repair Damaged Slot**, unlocked by a separate Tier 3 research node requiring Construction Drone research. The current implementation does not charge materials. |
| [`FriendlyRobotCoreExtractionMod/`](FriendlyRobotCoreExtractionMod/README.md) | C# / Harmony + research definition | Adds a research-gated **Safe Core Extraction** operation for friendly robotic beings. |
| [`OrbotronArmorMod/`](OrbotronArmorMod/README.md) | JSON-only | Adds Orbotron armor equipment, recipes, and research definitions. It has no C# project and does not need the build steps below. |
| `Core/` | Reference data | Snapshot of game configuration and reference source used during mod development. |

Each mod folder has its own `ModInfo.json`, and its own README describes its behavior, limitations, and in-game test checklist.

## Shared Assembly Hub operation pipeline

A surgical operation has two separate stages:

1. It is registered in the global `SurgicalQueue` as a pending operation.
2. The queue asks the patient's `BodyComp.TryOrderOp` to create the patient-side AI goal. That goal carries the surgery type, operation ID and target slot ID through `Surgery_GoTo` / `CarryPatient` into `BodyModSpotComp` (Assembly Hub).

A destroyed slot is not an installed part with a damaged condition. The game's `DestroySlot` surgical failure sets `BodyPartSlot.IsDestroyed`; the extraction itself still completes, leaving the slot empty with `IsPartRemoved = true` and `InstalledDef = null`. The normal surgery popup returns early for `IsDestroyed`, which is why the repair action must be added by the mod.

The vanilla single-slot removal task is not a valid transport-task factory for either custom operation. It rejects a slot that is already `IsPartRemoved`, and it also rejects a living non-player patient unless its body is incapacitated. Those guards are correct for ordinary harvesting, but conflict with repairing an empty destroyed socket and with safe extraction from a living friendly robot.

Both custom operations now create their own patient-side transport goal, following the vanilla install-goal pattern. The goal carries `SurgeryType_Int`, `SurgeryId`, `BodySlotId_String`, the patient-delivery state and the normal Assembly Hub availability check. `BodyModSpotComp.ResolveOpType` is patched so each custom operation is treated as a slot-targeted procedure that needs no delivered part. The vanilla queue skips a patient while `Being.Goal` is occupied; it does not automatically interrupt unrelated work. The custom mods also try to start the queue head immediately after registration, interrupting only an unrelated interruptible goal. An ongoing surgery and a non-interruptible goal are left alone. The Assembly Hub operator remains a separate AI job; the patient transport goal does not assign the operator.

For slot repair, completion only clears the slot's `IsDestroyed` flag. The slot remains empty (`IsPartRemoved = true`), so the vanilla installation flow becomes available again. The repair test build still has no material debit.

The slot repair node is `Research/Robotics/BodySlotRepair` (Tier 3), with a direct prerequisite on `Research/Robotics/Drone` (Construction Drone, Tier 3). The Repair Damaged Slot command is hidden until this research is unlocked.

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

**User-confirmed build on 2026-11-25:** both C# projects compiled successfully against the local **Stardeus 0.15.11b** Managed directory after the previous patient-side goal implementation. The user also confirmed in-game that the Slot Repair operation now completes.

**Current source status:** this revision changes both C# projects (Safe Core failure prevention and research gating for Slot Repair), so both projects must be rebuilt before the current commit is treated as compile-verified.

Prior local builds reported five warnings:

- `MSB3245`: the build resolver reports that it cannot resolve `System.Runtime`.
- `MSB3277`: reference-version conflicts are reported for:
  - `System.Collections.Immutable` 8.0.0.0 vs 9.0.0.0 (8.0.0.0 selected);
  - `System.Reflection.Metadata` 8.0.0.0 vs 9.0.0.0 (8.0.0.0 selected);
  - `System.Threading.Tasks.Extensions` 4.2.0.0 vs 4.2.1.0 (4.2.0.0 selected);
  - `System.Text.Encoding.CodePages` 4.1.0.0 vs 8.0.0.0 (4.1.0.0 selected).

The project files currently reference the game's `Managed\*.dll` set, including Roslyn assemblies whose dependency versions differ from some assemblies selected from the game directory. The reference set still needs a deliberate review; these warnings have not been hidden or force-resolved.

**In-game findings (2026-11-25):**
- Repair Damaged Slot: the user confirmed that the operation works. The new Tier 3 research lock has not yet been tested in-game.
- Safe Core Extraction: patient transport and extraction work, but the extracted Core received a damaged condition and could not be used by the replicator. The current source revision disables the failure roll at the typed `TryRemovePart.canFail` argument and also suppresses Safe Core extraction failure outcomes in `SurgeryUtils.ApplySurgicalOpFailure`. Runtime validation of this correction remains pending.
      
## Validation boundary

Compilation confirms that the current C# source can be compiled against the supplied local game assemblies. It does not confirm that Harmony patches apply successfully, that the UI operations appear, or that the operations complete correctly in a live save.

Use a copied/test save when validating experimental surgery and body-part operations. See each mod's README for its specific test checklist and known limitations.
