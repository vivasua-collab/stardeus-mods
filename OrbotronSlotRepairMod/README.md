# Assembly Hub Slot Repair Test (0.1.0)

Target game: **Stardeus 0.15.11**.

This is an experimental C# mod, not a JSON-only mod. It adds a **Repair Damaged Slot** order to the robotic surgery selection UI. The queued operation is handled by the vanilla robotic BodyModSpot / Assembly Hub pipeline.

## Scope of this test build

- Adds an order for a selected robotic body slot when `BodyPartSlot.IsDestroyed` is true.
- The operation is represented as a custom `SurgicalOpType` value and intercepted at the Assembly Hub finalization point.
- The operation clears only the slot-level `IsDestroyed` flag and asks the body to recalculate its condition.
- It does **not** repair a destroyed installed part, clear `Welded` / `Crippled`, heal wounds, or repair a destroyed parent slot.
- It does not modify enemy robots or ordinary part extraction.

## AI task routing

A registered surgical operation is not itself an AI job. The vanilla `SurgicalQueue` normally waits until the patient has no active `Being.Goal`, then asks `BodyComp.TryOrderOp` to create the patient-side task. The stock single-slot path only handles `Remove`, `Install`, and `Swap`, so custom operation types need an explicit bridge.

The current implementation reuses the vanilla remove-task setup, then writes the custom operation type back into the goal. It also marks the custom type as a single-slot operation so the target slot ID is carried through the vanilla `Surgery_GoTo` / `CarryPatient` actions into the Assembly Hub. After the button is pressed, it attempts to interrupt an unrelated interruptible patient goal and start the head of that patient's surgery queue immediately. An already-running surgery is not interrupted; a non-interruptible goal can keep the operation queued until it finishes.

This bridge was added after the last successful local compilation report. Rebuild before testing this revision.

## Known incomplete part

The requested price is one quarter of a new Orbotron. The operation currently has **no material debit**. I have deliberately not fabricated a resource-inventory API without compiling against the user's exact 0.15.11 Managed DLLs. Therefore this build tests the operation/UI/state transition only; it is not the balanced final version.

## Build and install

The folder includes `.vscode/mod.csproj`. Set its `StardeusManagedPath` property to the game's `Stardeus_Data/Managed` directory if the default Steam path differs, then run:

```powershell
dotnet build .vscode/mod.csproj
```

The build writes the DLL to `Libraries/OrbotronSlotRepairMod.dll`. Copy the complete `OrbotronSlotRepairMod` folder into the game's Mods directory, enable it in Main Menu > Mods, and restart.

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
- Last reported local C# build (2026-11-25), before the AI task-routing bridge: **passed with five warnings**. The current bridge revision: **rebuild pending**.
- `tools/stardeus validate`: **not run**.
- In-game runtime: **not run**.
