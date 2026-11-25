# Assembly Hub Slot Repair Test (0.1.0)

Target game: **Stardeus 0.15.11**.

This is an experimental C# mod, not a JSON-only mod. It adds a **Repair Damaged Slot** order to the robotic surgery selection UI. The queued operation is handled by the vanilla robotic BodyModSpot / Assembly Hub pipeline.

## Scope of this test build

- Adds an order for a selected robotic body slot when `BodyPartSlot.IsDestroyed` is true.
- The operation is represented as a custom `SurgicalOpType` value and intercepted at the Assembly Hub finalization point.
- The operation clears only the slot-level `IsDestroyed` flag and asks the body to recalculate its condition.
- It does **not** repair a destroyed installed part, clear `Welded` / `Crippled`, heal wounds, or repair a destroyed parent slot.
- It does not modify enemy robots or ordinary part extraction.

## Known incomplete part

The requested price is one quarter of a new Orbotron. The operation currently has **no material debit**. I have deliberately not fabricated a resource-inventory API without compiling against the user's exact 0.15.11 Managed DLLs. Therefore this build tests the operation/UI/state transition only; it is not the balanced final version.

## Build and install

Open this folder in the configured `stardeus-modding-with-ai` workspace, create/configure its C# project with `tools/stardeus new OrbotronSlotRepairMod --kind csharp,content` if needed, then copy this folder's `Code/` and `ModInfo.json` into that generated mod. Build against the local 0.15.11 game DLLs, enable the mod, and restart.

Do not test on the only copy of a valuable save. Removing mods that add operations can leave pending operations in saves.

## Test checklist

1. Load a test save with a friendly robot whose body contains a destroyed slot (`IsDestroyed=true`).
2. Open the robot in the surgery selection UI.
3. Confirm **Repair Damaged Slot: [slot]** appears only for destroyed slots.
4. Queue it and confirm an Assembly Hub takes the robot.
5. Confirm the slot becomes selectable for normal installation after completion.
6. Confirm the operation does not change wounds, part flags, or other slots.

## Validation status

- Source logic mapped against the repository's Stardeus 0.15.11 Core snapshot.
- C# compilation against local game DLLs: **not run**.
- `tools/stardeus validate`: **not run**.
- In-game runtime: **not run**.
