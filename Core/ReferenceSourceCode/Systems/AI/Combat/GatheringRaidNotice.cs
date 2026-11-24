using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Notify;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.AI.Combat {
    // A raid gathering where it arrived is no fight yet: no alert, no clock limit, nobody arms
    // up (CombatSystem.md). The player still has to know it is there, and get the time the
    // wait gives to prepare: 19 raiders once turned up with no word at all (STA-1902). Shown
    // while any hostile army gathers; when it attacks, the raid alert takes over.
    public sealed class GatheringRaidNotice {
        private NotificationHandle handle;
        public bool IsShown => handle.IsValid;

        // arrived: a gathering raid just came, which pauses the game like the raid alert does
        public void Update(GameState s, List<Army> armies, bool arrived) {
            var first = FirstGatheringUnit(armies);
            if (first == null) {
                if (handle.IsValid) {
                    Notification.Remove(ref handle);
                }
                return;
            }
            if (handle.IsValid) { return; }
            handle = Notification.Build(IconId.CCombat, T.EvRaidMapEdgeStagingTitle, 0,
                    NotificationType.Alert)
                .WithTooltip(T.EvRaidMapEdgeStagingDesc)
                .WithOnClick(() => SelectGathering(s, armies))
                .Sticky()
                .Emergency(false)
                .Show();
            if (arrived) {
                s.Sig.ShowFanfare.Send(Texts.Red(Texts.LinkTo(T.EvRaidMapEdgeStagingTitle,
                    first.PosIdx)));
                s.Clock.DangerPause();
            }
        }

        private static CombatUnit FirstGatheringUnit(List<Army> armies) {
            for (int i = 0; i < armies.Count; i++) {
                var army = armies[i];
                if (!IsGathering(army)) { continue; }
                if (army.Units.Count > 0) {
                    return army.Units[0];
                }
            }
            return null;
        }

        private static bool IsGathering(Army army) {
            return army.IsStaging && !army.IsDefeated && !army.IsDisbanded
                && army.IsHostileTowards(FactionDef.Player);
        }

        private static void SelectGathering(GameState s, List<Army> armies) {
            var beings = Caches.BeingList;
            beings.Clear();
            foreach (var army in armies) {
                if (!IsGathering(army)) { continue; }
                foreach (var unit in army.Units) {
                    beings.Add(unit.Agent.Being);
                }
            }
            s.Query.SelectBeings.Ask(beings);
        }
    }
}
