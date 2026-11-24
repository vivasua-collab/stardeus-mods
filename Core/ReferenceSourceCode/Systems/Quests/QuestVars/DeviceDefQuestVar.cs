using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Tools;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class DeviceDefQuestVar : QuestVar, IQuestVar<Def> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new DeviceDefQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "DeviceDef";
        private Def pickedDef;
        public Def Value => pickedDef;

        public DeviceDefQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                : base(s, def, data, seed, isLoading) {
            if (!isLoading) {
                PickRandomDevice(def, data, seed);
                return;
            }
            var defId = data.Data?.GetString(def.DataKey(VarId), null);
            if (defId == null) {
                throw new System.Exception($"Failed to load DeviceDef id for var {def} | {data}");
            }
            pickedDef = The.Defs.Get(defId);
            if (pickedDef == null) {
                D.Warn("DeviceDefQuestVar: failed to find def by id {0} on load | {1}", defId, data);
            }
        }

        private static bool AllPrerequisitesUnlocked(GameState s, Def d) {
            if (Researchable.IsEmptyPrereq(d)) { return true; }
            foreach (var prereqId in d.Researchable.Prerequisites) {
                if (!s.Research.IsUnlocked(Hashes.S(prereqId))) { return false; }
            }
            return true;
        }

        /// <summary>
        /// Cheap existence check mirroring PickRandomDevice's eligibility filter, without
        /// picking a def or throwing - lets callers (e.g. ColonistDeviceRequestStoryEvent)
        /// avoid offering a quest that would immediately fail to generate because every
        /// constructable device is already built. maxTier default matches
        /// Personal_ColonistDeviceRequest.json, the only current caller of this quest var.
        /// </summary>
        public static bool HasEligibleDevice(GameState s, int maxTier = 4) {
            foreach (var d in The.Defs.Defs.Values) {
                if (!d.IsConstructable) { continue; }
                if (d.LayerId < WorldLayer.Objects) { continue; }
                if (!ToolPlaceTiles.CanShowInBuildMenu(d)) { continue; }
                if (s.CountByDef(d) > 0) { continue; }
                if (s.Research.IsDefUnlocked(d)) { return true; }
                if (d.Tier > maxTier) { continue; }
                if (!Researchable.IsEmptyPrereq(d) && AllPrerequisitesUnlocked(s, d)) {
                    return true;
                }
            }
            return false;
        }

        private void PickRandomDevice(QuestVarDef varDef, QuestData data, int seed) {
            var maxTier = config.GetInt(PropertyIdH.MaxTier, 4);
            var unlockedWeight = (float)config.GetInt(PropertyIdH.UnlockedWeight, 4);
            var lockedWeight = (float)config.GetInt(PropertyIdH.LockedWeight, 1);

            var unlocked = new Dictionary<Def, float>();
            var locked = new Dictionary<Def, float>();

            foreach (var d in The.Defs.Defs.Values) {
                if (!d.IsConstructable) { continue; }
                // don't ask to build floors / walls
                if (d.LayerId < WorldLayer.Objects) { continue; }
                if (!ToolPlaceTiles.CanShowInBuildMenu(d)) { continue; }
                if (S.CountByDef(d) > 0) { continue; }
                var isUnlocked = S.Research.IsDefUnlocked(d);
                if (!isUnlocked && d.Tier > maxTier) { continue; }
                if (isUnlocked) {
                    unlocked[d] = unlockedWeight;
                } else if (!Researchable.IsEmptyPrereq(d) && AllPrerequisitesUnlocked(S, d)) {
                    locked[d] = lockedWeight;
                }
            }

            var rng = new Rng(Hashes.Quick(seed, Primes.Prime016));
            if (unlocked.Count > 0 && locked.Count > 0) {
                // merge both buckets with their respective weights
                var combined = new Dictionary<Def, float>(unlocked);
                foreach (var kv in locked) {
                    combined[kv.Key] = kv.Value;
                }
                rng.TryWeightedFrom(combined, out pickedDef);
            } else if (unlocked.Count > 0) {
                rng.TryWeightedFrom(unlocked, out pickedDef);
            } else if (locked.Count > 0) {
                rng.TryWeightedFrom(locked, out pickedDef);
            }

            if (pickedDef == null) {
                throw new System.Exception($"DeviceDefQuestVar: no eligible device found | {data}");
            }
            data.GetOrCreateData().SetString(varDef.DataKey(VarId), pickedDef.Id);
        }
    }
}
