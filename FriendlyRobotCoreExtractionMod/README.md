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

A registered operation needs a patient-side AI goal before the global queue will consider it in progress. The previous implementation reused `BodyComp.TryOrderRemove`; that was incorrect for this operation. Vanilla removal has an intentional eligibility check that rejects a living non-player patient unless it is incapacitated. Safe Core Extraction is specifically intended to work on a living friendly robot, so that path prevented the task from being created.

The current revision creates a custom patient-side transport goal following the vanilla installation-goal pattern. It carries the custom operation type, operation ID and Core slot ID, uses the `Move` ability so another robot can carry an immobile patient, and keeps the vanilla Assembly Hub availability check. It then uses the normal `Surgery_GoTo` / `CarryPatient` path to load the patient into the Assembly Hub.

The Assembly Hub still requires a separate `Robotics` operator job. The patient-side transport goal and the hub operator task are independent.

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
- User-confirmed local C# build (2026-11-25): the previous queue-query correction compiled with five warnings and no errors. The current dedicated-goal revision was committed afterward and still needs a local rebuild.
- `tools/stardeus validate`: **not run**.
- In-game runtime: **not run**.
