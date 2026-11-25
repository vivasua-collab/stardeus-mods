# Assembly Hub Slot Repair Test (0.1.0)

Target game: **Stardeus 0.15.11**.

This is an experimental C# mod, not a JSON-only mod. It adds a **Repair Damaged Slot** order to the robotic surgery selection UI. The queued operation is handled by the vanilla robotic BodyModSpot / Assembly Hub pipeline.

## Scope of this test build

- Adds an order for a robotic body slot when `BodyPartSlot.IsDestroyed` is true and the slot is empty (`IsPartRemoved = true`, `InstalledDef = null`).
- The operation is represented by a custom `SurgicalOpType` and intercepted at the Assembly Hub finalization point.
- Completion clears only the slot-level `IsDestroyed` flag. The part is not recreated; the slot remains empty and can use the normal installation workflow afterward.
- It does **not** repair a destroyed installed part, clear `Welded` / `Crippled`, heal wounds, or repair a destroyed parent slot.
- It does not modify enemy robots or ordinary part extraction.

## Why the first task-routing attempt failed

The failure named `DestroySlot` is applied during a surgical extraction. The game sets `slot.IsDestroyed = true`, but unless the failure was `WeldPart`, the extraction continues. The normal removal routine then clears `InstalledDef`, sets `IsPartRemoved = true`, and clears the slot's part state. Thus a destroyed slot is already an empty slot, not a removable installed part.

The first mod version incorrectly rejected `IsPartRemoved` and then attempted to reuse `BodyComp.TryOrderRemove`. That vanilla method also rejects already-removed slots. Separately, it refuses removal from a living non-player being unless the being is incapacitated. Those guards are intentional for ordinary harvesting; they are not appropriate for repairing a socket on a friendly active robot.

## AI task routing in the current revision

The custom operation now creates its own patient-side transport goal, following the vanilla part-installation goal pattern instead of calling `TryOrderRemove`. The goal carries the custom operation type, operation ID and target slot ID, requires the `Move` ability (so another robot can carry an immobile patient), and uses the vanilla Assembly Hub availability check.

The custom operation is marked as a slot-targeted action with no part to deliver. The normal `Surgery_GoTo` / `CarryPatient` chain therefore moves the patient to a robotic Assembly Hub. The Assembly Hub still requires its separate `Robotics` operator job.

After completion, only `IsDestroyed` is cleared. `IsPartRemoved` stays true, which is correct: the socket is repaired, but it is still empty. The vanilla install options should then become available.

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
- User-confirmed local C# build (2026-11-25): the previous queue-query correction compiled with five warnings and no errors. The current dedicated-goal revision was committed afterward and still needs a local rebuild.
- `tools/stardeus validate`: **not run**.
- In-game runtime: user reported that the previous repair action did not enter or remain in the queue; the dedicated-goal correction is pending a new runtime test.
