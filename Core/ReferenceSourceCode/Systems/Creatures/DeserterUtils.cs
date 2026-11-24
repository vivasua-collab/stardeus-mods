using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Mental;
using KL.Utils;

namespace Game.Systems.Creatures {
    // Shared revival logic for colonists who deserted via the AbandonShip mental breakdown
    // (AIBreakdownActionAbandonShip). Every return pathway (shuttle flies back, stasis pod,
    // expedition rescue) routes through ReturnDeserter so the mechanical revival and the mood
    // boosts are implemented exactly once. Each pathway's own story event is still responsible
    // for its own notification/fanfare/log text - the "found in a stasis pod" reveal reads very
    // differently from "the shuttle landed" - so that stays out of this shared routine.
    public static class DeserterUtils {
        // A deserter stays gone at least this long before any pathway can bring them back.
        // Without it the shuttle could fly back right after the breakdown, which took the
        // weight out of the desertion.
        public const long MinAbsenceTicks = Consts.TicksPer3Days;

        public static bool HasReturnableDeserter(GameState s) {
            return FindReturnableDeserter(s) != null;
        }

        private static bool IsReturnable(BeingData bd, long ticks) {
            if (bd?.Mind?.Role != ColonyRole.Deserter) { return false; }
            return ticks - bd.Mind.DesertedTick >= MinAbsenceTicks;
        }

        // Picks the deserter who left earliest, so multiple return pathways firing over time
        // work through the backlog in a deterministic order.
        public static BeingData FindReturnableDeserter(GameState s) {
            BeingData earliest = null;
            var ticks = s.Ticks;
            foreach (var bd in s.Query.AllBeingData.Ask().Values) {
                if (!IsReturnable(bd, ticks)) { continue; }
                if (earliest == null || bd.Mind.DesertedTick < earliest.Mind.DesertedTick) {
                    earliest = bd;
                }
            }
            return earliest;
        }

        // For the "shuttle flies itself back" pathway only: a lost shuttle remembers who
        // stole it via SaveLastCrew (set in AIBreakdownActionAbandonShip.Abandon), so this
        // finds a still-existing (not deconstructed) lost shuttle whose thief is still gone.
        public static bool HasReturnableDeserterWithShuttle(GameState s,
                out BeingData deserter, out ShuttleComp shuttle) {
            var allShuttles = s.Components.AllByType<ShuttleComp>();
            if (allShuttles != null) {
                foreach (var comp in allShuttles) {
                    var sh = (ShuttleComp) comp;
                    if (!sh.IsLost || !sh.Entity.IsActive) { continue; }
                    var crewIds = sh.GetLastCrewIds();
                    if (crewIds == null || crewIds.Length == 0) { continue; }
                    var bd = s.Query.FindBeingData.Ask((uint) crewIds[0]);
                    if (IsReturnable(bd, s.Ticks)) {
                        deserter = bd;
                        shuttle = sh;
                        return true;
                    }
                }
            }
            deserter = null;
            shuttle = null;
            return false;
        }

        public static Being ReturnDeserter(GameState s, BeingData data, int spawnPosIdx, long ticks) {
            data.Body.Flags &= ~BodyFlags.Missing;
            data.StateFlags &= ~BeingState.Hidden;
            data.Mind.Role = ColonyRole.Unassigned;

            var cmd = new CmdSpawnBeing(data, spawnPosIdx);
            cmd.Execute(s);
            var being = cmd.SpawnedBeing;
            if (being == null) {
                D.Err("Failed to respawn returning deserter: {0}", data);
                return null;
            }
            // BeingUtils.RemoveFromShip (via DestroyEntity) deactivated but did not remove
            // conditions on desertion; re-register whatever is still on the data now that
            // there's an entity again. ignoreRegistered: true because ConditionsComp.InitHealth
            // (run by CmdSpawnBeing above) may have created and registered ConditionInjured
            // for a deserter from a save made before conditions were kept through desertion.
            data.RestoreConditions(s, ignoreRegistered: true);

            // Bigger and longer than the generic newcomer boost (BeingUtils.AddInitialMentalBoostFor,
            // +20/3 days) - this is relief at surviving, not a stranger settling in.
            var personalEffect = MentalEffect.CreateWithIcon(being.PosIdx, IconId.CParty, ticks,
                Consts.TicksPer5Days, T.MoodDeserterReturnedPersonal, +30);
            s.Sig.AddMentalEffect.Send(data, personalEffect);

            ApplyColonyMoodEffect(s, data, IconId.CParty, ticks, T.MoodCrewMemberReturned, +6);
            TrustDeeds.TakenBack(data);

            return being;
        }

        // Shared with AIBreakdownActionAbandonShip's negative mood hit on desertion - same
        // "loop player-faction beings, skip one, apply a fresh effect" shape, positive or
        // negative depending on the caller.
        public static void ApplyColonyMoodEffect(GameState s, BeingData exclude,
                string icon, long ticks, string reason, int effect) {
            foreach (var bd in s.Query.AllBeingData.Ask().Values) {
                if (bd == exclude || !bd.IsPlayer) { continue; }
                if (bd.Mind?.Health == null) { continue; }
                if (bd.StateFlags.HasFlag(BeingState.Dead)) { continue; }
                var pos = bd.Being?.PosIdx ?? -1;
                var eff = MentalEffect.CreateWithIcon(pos, icon, ticks,
                    Consts.TicksPer3Days, reason, effect);
                s.Sig.AddMentalEffect.Send(bd, eff);
            }
        }
    }
}
