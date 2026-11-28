# Orbotron Slot Repair (0.1.2)

Target game: **Stardeus 0.15.11**.

This experimental C# mod adds two operations to the robotic surgery selection UI:

- **Repair Damaged Slot** — restores a destroyed, empty body slot (added in 0.1.0).
- **Remove Welded Part** (new in 0.1.2) — extracts a body part that became permanently `Welded` after a failed surgical procedure.

Both are intentionally restricted to the **Orbotron** body species; they do not apply to other robots or non-biological beings.

## Research unlock

- **Research:** Orbotron Slot Repair
- **ID:** `Research/Robotics/BodySlotRepair`
- **Tier:** 5
- **Prerequisite:** `Research/Robotics/Orbotron` — the Orbotron research node

The repair command is hidden until Orbotron research and this additional research node are both unlocked.

## Eligibility and effect

### Repair Damaged Slot

- The target must have body species ID `Orbotron`.
- The target slot must be destroyed (`IsDestroyed = true`) and empty (`IsPartRemoved = true`, `InstalledDef = null`).
- Completion clears only the slot-level `IsDestroyed` flag. The part is not recreated; the slot remains empty and can use the normal installation workflow afterward.
- It does **not** repair a destroyed installed part, clear `Welded` / `Crippled`, heal wounds, or repair a destroyed parent slot.

### Remove Welded Part (0.1.2)

- The target must have body species ID `Orbotron`, the research node must be unlocked, and the slot must hold an installed part whose state carries `BodyPartStateFlags.Welded` and whose type allows removal (`BodyPartFlags.Removable`).
- Core blocks welded parts from removal (the vanilla popup also stops at the `BodyPartWeldedWarning` notice). The mod adds its footer button in a Harmony postfix, so it appears exactly where the vanilla popup stopped.
- At the Assembly Hub the operation temporarily lifts only the `Welded` flag so the vanilla `CanRemovePart()` check passes, then performs the **standard** `BodyPartSlot.TryRemovePart` with the surgical failure roll disabled (`canFail: false`) — the extraction cannot weld the part again or roll any other complication.
- The extraction itself is the vanilla routine: stat modifiers, abilities, needs, attack methods and body hierarchy are removed cleanly, a mind inside the part is transferred by the vanilla path, and the part object is created with its original quality, state, wounds and flags (except `Welded`, which is intentionally removed).
- The extracted part is ejected through the vanilla `EjectRemovedPart` (out port) and orphaned attached parts follow the vanilla detach path. If Core considers an extracted part irreparable (`Severity.Irreparable` or condition < 0.1), the vanilla automatic disposal order still applies.
- If the removal fails, the `Welded` flag is restored — an installed part never silently loses its welded status.
- It does **not** heal or repair the part, restore its condition, touch other slots, or reset the slot's `IsDestroyed` state.

Both operations enforce the species restriction in the UI, when the surgical operation is ordered, and again when the Assembly Hub finalizes it. Other robot types cannot use these operations by opening another UI route or by submitting a custom operation.

## Why these are custom operations

The game's `DestroySlot` surgical failure sets `BodyPartSlot.IsDestroyed`. Unless the failure is `WeldPart`, extraction continues and the removal routine clears `InstalledDef`, sets `IsPartRemoved = true`, and clears the part state. Thus a destroyed slot is already an empty slot, not a removable installed part.

The normal removal operation cannot be reused for either case: it rejects slots already marked as removed, refuses removal from a living non-player being unless the being is incapacitated, and `CanRemovePart()` blocks welded parts outright. The two operations reuse the same research node, the same patient transport goal factory and the same Assembly Hub finalize route, differing only in the operation type (8 and 9) and eligibility checks.

## AI task routing

Each custom operation creates its own patient-side transport goal, following the vanilla part-installation goal pattern. The goal carries the custom operation type, operation ID and target slot ID, requires the `Move` ability (so another robot can carry an immobile patient), and uses the vanilla Assembly Hub availability check.

A custom operation is treated as a slot-targeted action with no part to deliver. The normal `Surgery_GoTo` / `CarryPatient` chain therefore moves the Orbotron to a robotic Assembly Hub. The Assembly Hub still requires its separate `Robotics` operator job.

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
5. Open an Orbotron with a part welded by a failed surgery and confirm **Remove Welded Part** appears for that slot.
6. Open another robot species with a destroyed empty slot or a welded part and confirm neither command appears.
7. Confirm both commands are hidden while the research node is locked.
8. Queue the Orbotron repair and confirm an Assembly Hub takes the Orbotron.
9. Confirm the slot becomes selectable for normal installation after completion.
10. Queue the welded part removal (including with an immobilized Orbotron that has to be carried) and confirm the part appears at the Assembly Hub out port and the slot becomes empty.
11. Confirm the extracted part keeps its quality, wounds, damage and remaining flags (only `Welded` is gone), and that an irreparable extracted part still gets the vanilla disposal order.
12. Confirm removing a mind-holding part (Core) transfers the mind to the extracted part through the vanilla path.
13. Confirm the operation does not change wounds, part flags, or other slots.

## Validation status

- Species identity verified against the Stardeus 0.15.11 Core data: `Config/Species/Types/Orbotron.json` resolves the body species as `Orbotron`, with `DefId = Beings/Orbotron01`.
- The previous unrestricted repair operation was confirmed by the user to complete in-game.
- The 0.1.2 welded-part operation requires a fresh local build and in-game validation (see the report ORBOTRON-SLOT-REPAIR-WELDED-01-report.md for what was verified statically).
- `tools/stardeus validate`: **not run**.
