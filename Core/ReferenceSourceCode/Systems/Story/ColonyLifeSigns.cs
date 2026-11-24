using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;

namespace Game.Systems.Story {
    // Answers one question for the second chance: can this colony still do anything at all?
    // Read hourly by SecondChanceStoryAspect and once more by StasisPodRescueCrewStoryEvent
    // right before the pod opens, so both sides of the rescue agree on what "helpless" means.
    public static class ColonyLifeSigns {
        // BeingData rather than AISys.Agents on purpose: a crew that is entirely off-map on an
        // expedition has no spawned agents but is very much alive and coming back.
        public static bool IsColonyHelpless(GameState s) {
            // BeingsSys.All, not S.Beings.All: the latter is the list of spawned Being
            // entities, which drops the dead and anyone off-map. This needs the roster.
            var roster = s.Sys.Get<BeingsSys>(BeingsSys.SysId);
            if (roster == null) { return false; }
            var hadCrew = false;
            foreach (var b in roster.All.Values) {
                if (b.IsGuest || !b.IsPlayer) { continue; }
                if (b.Species.IsPet) { continue; }
                // Missing covers deserters and everyone asleep in a stasis pod alike
                // (StasisUtils sets it together with ColonyRole.InStasis). Neither is crew
                // the colony can count on, and neither makes this a colony that lost its crew.
                if (b.Body.HasFlag(BodyFlags.Missing)) { continue; }
                // Counting the dead is what separates a wiped colony, which gets a rescue,
                // from a map that never had anybody, which is not a colony at all.
                hadCrew = true;
                if (b.HasStateFlag(BeingState.Dead)) { continue; }
                // Dead-or-incapacitated is the same test AIAgentComp.IsDisabled uses, and it
                // already covers a discharged bot (ConditionDischarged.Incapacitates). Such a
                // bot recovers on its own and stands the beacon down, so nothing here has to
                // predict recovery - the beacon delay is the test.
                if (b.Body.IsIncapacitated) { continue; }
                return false;
            }
            return hadCrew;
        }
    }
}
