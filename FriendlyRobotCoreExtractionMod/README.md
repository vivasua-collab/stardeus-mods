# Friendly Robot Safe Core Extraction Test (0.1.0)

Target game: **Stardeus 0.15.11**.

This is an experimental C# mod. It adds a separate **Safe Core Extraction** order for friendly robotic beings and unlocks it through a new Tier 3 research node.

## Research placement

The Core snapshot identifies **Construction Drone** as `Research/Robotics/Drone`, **Tier 3**. The new research node is also Tier 3 and has a direct prerequisite on that node. This confirms the requested connection to the construction drone research; it is not Tier 4.

## Intended operation

- The order appears only for a friendly robotic being with a mind-holding slot whose type ID is `Core`.
- It is available only after `Research/Robotics/SafeCoreExtraction` is unlocked.
- The operation is queued through the vanilla surgical queue and processed by a robotic BodyModSpot / Assembly Hub.
- The custom operation invokes the vanilla body-part removal procedure.
- During this operation only, the extraction failure roll is disabled. The vanilla `TransferMindAfterPartRemoval` path remains in use, so the Mind is transferred into the extracted Core by the game's own code.
- It does not alter extraction rules for enemy robots, non-Core slots, or ordinary surgery.

## AI task routing

A registered surgical operation is not itself an AI job. The vanilla `SurgicalQueue` normally waits until the patient has no active `Being.Goal`, then asks `BodyComp.TryOrderOp` to create the patient-side task. The stock single-slot path only handles `Remove`, `Install`, and `Swap`, so custom operation types need an explicit bridge.

The current implementation reuses the vanilla remove-task setup, then writes the custom extraction type back into the goal. It also marks the custom type as a single-slot operation so the Core slot ID is carried through the vanilla `Surgery_GoTo` / `CarryPatient` actions into the Assembly Hub. After the button is pressed, it attempts to interrupt an unrelated interruptible patient goal and start the head of that patient's surgery queue immediately. An already-running surgery is not interrupted; a non-interruptible goal can keep the operation queued until it finishes.

The Assembly Hub still requires a Robotics operator under its normal `OperatableDevice` rules. The patient-side AI task and the hub operator task are separate jobs.

This bridge was added after the last successful local compilation report. Rebuild before testing this revision.

## Build and install

The folder includes `.vscode/mod.csproj`. Set its `StardeusManagedPath` property to the game's `Stardeus_Data/Managed` directory if the default Steam path differs, then run:

```powershell
dotnet build .vscode/mod.csproj
```

The build writes the DLL to `Libraries/FriendlyRobotCoreExtractionMod.dll`. Copy the complete `FriendlyRobotCoreExtractionMod` folder into the game's Mods directory, enable it in Main Menu > Mods, and restart.

Do not test on the only copy of a valuable save. Removing mods that add research or operations can leave pending operations in saves.
## Test checklist

1. Research `Drone` (Construction Drone, Tier 3).
2. Research the new `Safe Core Extraction` node.
3. Select a friendly robotic being with an installed Core containing a Mind.
4. Confirm the separate **Safe Core Extraction** order appears for Core only.
5. Queue it and confirm an Assembly Hub takes the robot.
6. Confirm the extracted Core object appears at the hub output and retains the original Mind.
7. Confirm the robot is left with an empty Core slot and no surgical failure message.
8. Confirm hostile robots and non-Core slots do not get this safe operation.

## Validation status

- Core extraction call path and Mind transfer mapped in the 0.15.11 Core snapshot.
- Last reported local C# build (2026-11-25), before the AI task-routing bridge: **passed with five warnings**. The current bridge revision: **rebuild pending**.
- `tools/stardeus validate`: **not run**.
- In-game runtime: **not run**.
