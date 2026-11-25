# Orbotron Slot Repair (0.1.1)

Target game: **Stardeus 0.15.11**.

This experimental C# mod adds a **Repair Damaged Slot** operation to the robotic surgery selection UI. It is intentionally restricted to the **Orbotron** body species; it does not apply to other robots or non-biological beings.

## Research unlock

- **Research:** Orbotron Slot Repair
- **ID:** `Research/Robotics/BodySlotRepair`
- **Tier:** 5
- **Prerequisite:** `Research/Robotics/Orbotron` — the Orbotron research node

The repair command is hidden until Orbotron research and this additional research node are both unlocked.

## Eligibility and effect

- The target must have body species ID `Orbotron`.
- The target slot must be destroyed (`IsDestroyed = true`) and empty (`IsPartRemoved = true`, `InstalledDef = null`).
- The species restriction is enforced in the UI, when the surgical operation is ordered, and again when the Assembly Hub finalizes it. Other robot types cannot use this operation by opening another UI route or by submitting a custom operation.
- Completion clears only the slot-level `IsDestroyed` flag. The part is not recreated; the slot remains empty and can use the normal installation workflow afterward.
- It does **not** repair a destroyed installed part, clear `Welded` / `Crippled`, heal wounds, or repair a destroyed parent slot.
- It does not modify other robot species or ordinary part extraction.

## Why this is a custom operation

The game's `DestroySlot` surgical failure sets `BodyPartSlot.IsDestroyed`. Unless the failure is `WeldPart`, extraction continues and the removal routine clears `InstalledDef`, sets `IsPartRemoved = true`, and clears the part state. Thus a destroyed slot is already an empty slot, not a removable installed part.

The normal removal operation cannot be reused: it rejects slots already marked as removed, and also refuses removal from a living non-player being unless the being is incapacitated.

## AI task routing

The custom operation creates its own patient-side transport goal, following the vanilla part-installation goal pattern. The goal carries the custom operation type, operation ID and target slot ID, requires the `Move` ability (so another robot can carry an immobile patient), and uses the vanilla Assembly Hub availability check.

The custom operation is treated as a slot-targeted action with no part to deliver. The normal `Surgery_GoTo` / `CarryPatient` chain therefore moves the Orbotron to a robotic Assembly Hub. The Assembly Hub still requires its separate `Robotics` operator job.

After completion, only `IsDestroyed` is cleared. `IsPartRemoved` stays true, which is correct: the socket is repaired, but it is still empty. The vanilla install options should then become available.

## Known incomplete part

The operation currently has **no material debit**. The previously discussed target price of one quarter of a new Orbotron has not been implemented; no resource-inventory API has been added without compiling against the user's exact 0.15.11 Managed DLLs.

## Build and install

The folder includes `.vscode/mod.csproj`. Set its `StardeusManagedPath` property to the game's `Stardeus_Data/Managed` directory if the default Steam path differs, then run:

```powershell
dotnet build .vscode/mod.csproj
```

The build writes the DLL to `Libraries/OrbotronSlotRepairMod.dll`. Copy the complete `OrbotronSlotRepairMod` folder into the game's Mods directory, enable it in Main Menu > Mods, and restart.

Do not test on the only copy of a valuable save. Removing mods that add operations can leave pending operations in saves.

## Test checklist

1. Research **Orbotron**.
2. Research **Orbotron Slot Repair**.
3. On a copied test save, open an Orbotron with a destroyed empty body slot.
4. Confirm **Repair Damaged Slot** appears for that Orbotron.
5. Open another robot species with a destroyed empty slot and confirm the repair command does **not** appear.
6. Queue the Orbotron repair and confirm an Assembly Hub takes the Orbotron.
7. Confirm the slot becomes selectable for normal installation after completion.
8. Confirm the operation does not change wounds, part flags, or other slots.

## Validation status

- Species identity verified against the Stardeus 0.15.11 Core data: `Config/Species/Types/Orbotron.json` resolves the body species as `Orbotron`, with `DefId = Beings/Orbotron01`.
- The previous unrestricted repair operation was confirmed by the user to complete in-game.
- The Orbotron-only checks and Tier 5 research prerequisite in this revision require a fresh local build and in-game validation.
- `tools/stardeus validate`: **not run**.
