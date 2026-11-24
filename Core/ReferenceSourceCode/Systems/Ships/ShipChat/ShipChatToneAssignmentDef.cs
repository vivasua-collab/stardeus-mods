using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data.Factions;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipChatToneAssignmentDef {
        public static Dictionary<int, ShipChatToneAssignmentDef> All = new();
        private static readonly Dictionary<ShipChatToneAssignmentDef, float> weighed = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
            weighed.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public string ToneType;
        public ShipChatTone Tone;
        public string Id;
        public int IdH;
        public string[] FactionChanceKey;
        public float[] FactionChanceValue;
        public float DefaultFactionChance;
        public float[] AttitudeChance;
        private Curve attitudeCurve;
        public Curve AttitudeCurve => attitudeCurve ??= AttitudeChance;
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipChatToneAssignmentDef>("Config/Ships/ChatTones");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (Arrays.IsEmpty(type.AttitudeChance)) {
                    D.Err("AttitudeChance[] not defined for ShipChatToneAssignmentDef: {0}", type.Id);
                    type.AttitudeChance = new float[] {
                        -1f, 1f,
                        1f, 1f
                    };
                }
                if (Arrays.Count(type.FactionChanceKey) != Arrays.Count(type.FactionChanceValue)) {
                    D.Err("Incorrectly defined ShipChatToneAssignmentDef! Missmatched FactionChanceKey / FactionChanceValue, not same array size! {0}",
                        type.Id);
                }
                if (!Enums.TryFromString(type.ToneType, out type.Tone)) {
                    D.Err("Unknown tone type for ShipChatToneAssignmentDef: {0} -> {1}", type.ToneType, type.Id);
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipChatToneAssignmentDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public static ShipChatTone GetToneFor(NPCShip ship, Rng rng) {
            weighed.Clear();
            var attitude = ship.SO.S.Query.FactionAttitude.Ask(ship.Faction,
                FactionDef.Player);
            foreach (var res in All.Values) {
                if (res.Matches(ship, attitude, out float weight)) {
                    if (weighed.TryGetValue(res, out float curW)) {
                        weighed[res] = Math.Max(curW, weight);
                    } else {
                        weighed[res] = weight;
                    }
                }
            }
            if (rng.TryWeightedFrom(weighed, out var def)) {
                return def.Tone;
            } else {
                D.Err("Couldn't find ShipChatToneDef for attitude: {0}. Ship: {1}",
                    attitude, ship);
                return ShipChatTone.Polite;
            }
        }

        private bool Matches(NPCShip ship, float attitude, out float chance) {
            chance = 1f;
            if (!Arrays.IsEmpty(FactionChanceKey)) {
                var factionIdx = Array.IndexOf(FactionChanceKey,
                    ship.Faction.Id);
                if (factionIdx == -1) {
                    chance *= DefaultFactionChance;
                } else {
                    chance *= FactionChanceValue[factionIdx];
                }
                if (chance <= 0f) { return false; }
            }
            chance *= AttitudeCurve.Evaluate(attitude);
            return chance > 0f;
        }
        public override string ToString() {
            return $"ShipChatTone[{Id}]";
        }
    }
}
