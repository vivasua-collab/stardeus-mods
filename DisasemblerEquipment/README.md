# Disasembler Equipment (0.1.0)

Target game: **Stardeus 0.15.11b**.

This experimental C# / Harmony mod extends the existing robotic Disassembler. It does not add a new building and does not change the vanilla robot/body-part recycling path.

## Current implementation

The existing Disassembler tab receives an **Automatic equipment recycling** section with two independent rule lists:

- **Weapons:** select one weapon type or all weapon types, then choose any quality or a maximum quality.
- **Clothing and armor:** select one clothing type or all clothing types, choose any quality or a maximum quality, and independently include/exclude New, Second Hand, and Belonged to dead origin states.

Rules are stored per Disassembler in its component data. They are copied with building configuration and retained through save/load. A matching enabled rule is sufficient for an item to qualify.

The automatic scanner checks at a one in-game-hour interval. It examines the global object registry and accepts items in Active (on-map) or Stored state. Stored items are ejected using the game's own storage API and then submitted to the vanilla `DisposableComp.TryOrderDispose` workflow. This reuses the normal worker hauling job and Disassembler queue.

For Active items, the scanner honours `StorableObjComp.IsForbidden`, active reservations and existing disposal goals. Stored items have no live `Obj` component while in storage; the 0.15.11 `ObjDataData` record does not contain the per-object `IsForbidden` property. The stock storage-disposal flow also ejects stored items directly, so stored items are treated as transferable. This is a known boundary of the current Core data model.

## Equipment recycling output

For weapon and clothing items, the mod captures quality when the vanilla Disassembler accepts the item. On processing, it uses the same first `Craftable` recipe selected by the game's own disposal path.

For each ingredient independently:

```text
returned_amount = floor(craft_ingredient_amount * 0.5 * item_quality)
```

Quality is stored as a multiplier (100% = 1.0). Examples: 50% quality returns 25% of each ingredient amount; 100% returns 50%; 150% returns 75%. Results are rounded down per ingredient, with no stochastic fractional rounding. The equipment path deliberately bypasses the Disassembler's difficulty/output multipliers. The vanilla `ConstructableComp.AddDeconstructedMat` material conversion step is retained.

This override applies to weapon/clothing objects processed by the Disassembler, including manually ordered disposals. It does not change robot body, body-part, biological, or other non-equipment disposal output.

The audited base-game weapon/clothing recipes use `Ingredients`. If a future equipment recipe has no ingredient list, the mod logs a warning and returns no materials rather than inventing a recipe cost.

## Important vanilla routing behaviour

The stock `AIActionCarryToDisposer` searches the global Disposer slot designation. Therefore, when multiple robotic Disassemblers exist, an item selected by the rules on one Disassembler may be delivered to another available robotic Disassembler. The rules decide **which items to order**; the vanilla system still decides **which Disassembler receives them**. All robotic Disassemblers use the same equipment-output formula. Strict per-machine destination routing is not patched in this first version.

## Build and install

The project follows the repository's proven Stardeus DLL setup:

- .NET Framework `net481` / `.NET Framework 4.8.1`
- C# language version 9.0
- x64 class library
- References taken from the exact local game `Stardeus_Data\Managed` directory
- All game assembly references use `Private=False`; game DLLs are not copied into the mod

Build with the current local installation paths:

```powershell
dotnet build "C:\Users\user\AppData\LocalLow\Kodo Linija\Stardeus\Mods\DisasemblerEquipment\.vscode\mod.csproj" "-p:StardeusManagedPath=D:\Games\Stardeus v0.15.11b\Stardeus_Data\Managed" -v:minimal
```

The build output is:

```text
DisasemblerEquipment/Libraries/DisasemblerEquipment.dll
```

Copy the complete `DisasemblerEquipment` folder into the game's Mods directory, enable it in the Mods menu, and restart the game.

## First test checklist

1. Build the current source against the local Stardeus 0.15.11b Managed assemblies.
2. Start the game with the mod enabled and open a robotic Disassembler.
3. Confirm the new Automatic equipment recycling section appears in the existing Disassembler tab.
4. Add a weapon rule for **All types** with **Any quality**; confirm a weapon is ordered and hauled to a Disassembler.
5. Add a clothing rule for one type, select only **Belonged to dead**, and set **Any quality**; confirm a new/second-hand item does not match while a corpse-origin item does.
6. Set a maximum quality and test one item below and one above that threshold.
7. Test a weapon and a clothing item with 50% quality. Compare returned ingredient amounts against `floor(ingredient * 0.25)`.
8. Save and reload with rules configured and at least one item in the Disassembler queue; confirm both rules and queued quality metadata survive.
9. Confirm existing robot body-part recycling behaves exactly as before.

## Validation status

- Source APIs mapped against the repository's Stardeus 0.15.11 Core snapshot.
- Initial DLL source and project scaffold added; **local compilation has not yet been run**.
- Harmony patch activation and in-game behaviour remain unverified until the user builds and tests this version.
- Previous repository build warnings (`MSB3245` for `System.Runtime` and `MSB3277` assembly-version conflicts) are not suppressed; inspect them if they appear in this build.
