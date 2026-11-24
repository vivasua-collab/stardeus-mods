using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    /// <summary>
    /// Raiders a multi-entry raid event generates up front and carries, hidden, inside the
    /// objects that deliver them (hullpiercers, breach and storage capsules). Why generate first:
    /// a raider's point cost (CombatRaid.StrengthOf) reads its live CombatUnit, so the raid's one
    /// spend loop has to spawn the raiders to know what it spent - but a hullpiercer opens minutes
    /// later and must survive a save/load in between. The "hidden, no entity" state used here is
    /// exactly what BeingStorage (stasis pods), AIActionEnterShuttle and escaped raiders already
    /// persist, so nothing new is serialized and every system already tolerates it.
    /// </summary>
    public static class RaidPayload {
        /// <summary>Hides freshly spawned raiders and returns their ids for a carrier to hold.</summary>
        public static int[] Stash(GameState S, List<CombatUnit> raiders) {
            if (raiders == null || raiders.Count == 0) { return null; }
            var ids = new int[raiders.Count];
            for (int i = 0; i < raiders.Count; i++) {
                var data = raiders[i].Data;
                ids[i] = (int) data.Id;
                // Hidden must be set before the entity goes, or Beings.OnDestroy marks the
                // data Dead instead of keeping it alive for a later Release.
                data.StateFlags |= BeingState.Hidden;
                foreach (var cond in data.Conditions) {
                    S.Sig.ActivateCondition.Send(cond.Value, false);
                }
                if (data.Being != null) {
                    S.Beings.Destroy(data.Being);
                }
            }
            return ids;
        }

        /// <summary>
        /// Deals ids round-robin into <paramref name="parts"/> groups (never more groups than
        /// ids), so every carrier gets an even share of the same raid.
        /// </summary>
        public static int[][] Split(int[] ids, int parts) {
            parts = Mathf.Clamp(parts, 1, ids.Length);
            var result = new int[parts][];
            for (int p = 0; p < parts; p++) {
                var size = ids.Length / parts + (p < ids.Length % parts ? 1 : 0);
                result[p] = new int[size];
            }
            var filled = new int[parts];
            for (int i = 0; i < ids.Length; i++) {
                var p = i % parts;
                result[p][filled[p]++] = ids[i];
            }
            return result;
        }

        /// <summary>
        /// Re-creates the stashed raiders at a position and starts them as one hostile army
        /// (same-faction armies already absorb each other in CombatSys, so waves merge).
        /// Returns the army CombatSys accepted, or null if nothing could be released.
        /// </summary>
        public static Army Release(GameState S, int[] ids, int posIdx, FactionDef faction,
                List<BeingData> released = null) {
            if (Arrays.IsEmpty(ids)) { return null; }
            if (!A.IsCombatOn) {
                // Combat got disabled while the carrier was in flight; the raid events hide
                // themselves in that case, so the raiders quietly disappear too.
                Discard(S, ids);
                return null;
            }
            var army = Army.Create(faction).WithObjective(CombatObjectiveIdH.Raid);
            var count = 0;
            foreach (var id in ids) {
                var data = S.Query.FindBeingData.Ask((uint) id);
                if (data == null || !data.HasStateFlag(BeingState.Hidden)) {
                    D.Err("Raid payload raider #{0} is missing or not hidden: {1}", id, data);
                    continue;
                }
                data.StateFlags &= ~BeingState.Hidden;
                var being = S.Beings.Create(EntityUtils.CenterOf(posIdx)
                    + Rng.UInsideUnitCircle() * 0.5f, data);
                if (being == null) {
                    D.Err("Failed to re-create raid payload raider #{0}", id);
                    continue;
                }
                data.RestoreConditions(S);
                army.WithUnit(being.AIAgent.CombatUnit);
                released?.Add(data);
                count++;
            }
            if (count == 0) { return null; }
            return S.Query.StartRaid.Ask(army);
        }

        /// <summary>
        /// For a carrier destroyed before it opened: its raiders are lost. Intended - shooting
        /// down a hullpiercer or capsule is the defense reward, so planned raid strength is an
        /// upper bound rather than a guarantee.
        /// </summary>
        public static void Discard(GameState S, int[] ids) {
            if (Arrays.IsEmpty(ids)) { return; }
            foreach (var id in ids) {
                var data = S.Query.FindBeingData.Ask((uint) id);
                if (data == null) { continue; }
                BeingUtils.DestroyEntity(S, data);
                // DestroyEntity tears down belongings and the entity; a hidden record has no
                // entity, so send the data itself through the same removal path Beings.OnDestroy
                // uses, which drops it from BeingsSys once it is Uninitialized.
                data.StateFlags = BeingState.Uninitialized;
                S.Query.DeactivateBeingData.Ask(data);
            }
        }
    }
}
