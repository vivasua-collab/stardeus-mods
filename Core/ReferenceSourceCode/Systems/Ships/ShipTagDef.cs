using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Input;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// Ship tags are name prefixes, like "USS"
    /// </summary>
    [Serializable]
    public sealed class ShipTagDef {
        public static Dictionary<int, ShipTagDef> All = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public string Id;
        public int IdH;
        private string tagT;
        public string TagT => tagT ??= $"ship.tag.{Id.ToLowerInvariant()}".T();
        private string descT;
        public string DescT => descT ??= $"ship.tag.{Id.ToLowerInvariant()}.desc".T();
        public string[] CompatibleFactions;
        public string[] CompatibleClasses;
        public float MinOffensiveness;
        public float MaxOffensiveness;
        public float ChanceWillTrade;
        /// <summary>
        /// Chance of this ship attacking illegal ships (if they're weaker)
        /// Multiplied by FactionDef.ChancePolice
        /// </summary>
        public float ChancePolice;
        public float Rarity;
        public string CodexLink => Texts.LinkToCodex(TagT, "ShipTags", Id);
        public int WantedShieldsFor(NPCShip ship) {
            var rng = ship.SeededRng(Primes.Prime097);
            if (ship.Class.NoShieldsChance > 0 && rng.Chance(ship.Class.NoShieldsChance)) {
                return 0;
            }
            var off = rng.Range(MinOffensiveness, MaxOffensiveness);
            var wSH = Mathf.Lerp(ship.Class.MinShields, ship.Class.MaxShields, off);
            if (wSH < 0.5f) {
                if (rng.Chance(0.85f)) {
                    return 1;
                }
                return 0;
            }
            return Mathf.RoundToInt(wSH);
        }

        public float OffensivenessFor(NPCShip ship) {
            var rng = ship.SeededRng(Primes.Prime034);
            return rng.Range(MinOffensiveness, MaxOffensiveness);
        }
        public bool CanAssignCombatStrategy(ShipCombatStrategyDef strat, NPCShip ship) {
            var off = OffensivenessFor(ship);
            return strat.MinOffensiveness <= off && strat.MaxOffensiveness >= off;
        }
        public static ShipTagDef Get(string id) {
            if (All.TryGetValue(Hashes.S(id), out var tag)) {
                return tag;
            }
            D.Err("Tag not found by id: {0}", id);
            return All.Values.First();
        }
        public static ShipTagDef FastGet(int idH) {
            if (All.TryGetValue(idH, out var def)) {
                return def;
            }
            D.Err("Tag not found by id hash: {0}", idH);
            return All.Values.First();
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipTagDef>("Config/Ships/Tags");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (type.Rarity == 0f) {
                    type.Rarity = 1f;
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipTag with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"ShipTag[{Id}]";
        }

        public bool IsCompatibleWithPlayer(GameState s) {
            var ship = s.Universe.Player.Ship;
            return IsCompatibleWith(ship);
        }

        public string Tooltip(NPCShip ship, Func<FactionDef, string> factionName = null,
                IEnumerable<FactionDef> orderedFactions = null) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.AppendFormat("{0}: {1}", Texts.Accented(TagT), DescT);
            sb.Append(GlyphMap.BRHR);
            sb.AppendFormat("{0}: {1}-{2}\n", T.NounOffensivenessAllowed,
                Units.Percentage(MinOffensiveness),
                Units.Percentage(MaxOffensiveness));

            if (ship != null) {
                var shipOff = ship.CombatState.Offensiveness;
                bool fits = shipOff >= MinOffensiveness && shipOff <= MaxOffensiveness;
                var offTxt = Units.Percentage(shipOff);
                if (fits) {
                    offTxt = Texts.Green(offTxt);
                } else {
                    offTxt = Texts.Red(offTxt);
                }
                sb.AppendFormat("{0}: {1}\n", T.NounOffensivenessCurrent, offTxt);
            }

            var offTip = string.Format("{0} = {1} / ({2} * {3})",
                T.NounOffensiveness, T.NounWeaponClassPoints,
                Texts.LinkToCodexManual(T.NounClassPoints, ClassPointsPage.PageId),
                Tunables.ShipOffensivenessClassPointRatio);
            sb.Append(Texts.Grey(offTip));

            sb.Append(GlyphMap.BRHR);
            sb.AppendFormat("{0}", T.CompatibleShipClasses);
            if (!Arrays.IsEmpty(CompatibleClasses)) {
                foreach (var cl in ShipClassDef.AllOrdered) {
                    if (Arrays.Contains(CompatibleClasses, cl.Id)) {
                        var className = cl.NameT;
                        if (ship != null) {
                            if (ship.Class == cl) {
                                className = Texts.Green(className);
                            } else {
                                className = Texts.Red(className);
                            }
                        }
                        sb.AppendFormat("\n - {0}", Texts.LinkToCodex(className, "ShipClasses", cl.Id));
                    }
                }
            } else {
                sb.AppendFormat("\n - {0}", Texts.Green(T.PronounAny));
            }

            sb.Append(GlyphMap.BRHR);
            sb.AppendFormat("{0}", T.CompatibleFactions);
            if (!Arrays.IsEmpty(CompatibleFactions)) {
                foreach (var fac in orderedFactions ?? FactionDef.AllOrdered) {
                    if (Arrays.Contains(CompatibleFactions, fac.Id)) {
                        var facName = factionName?.Invoke(fac) ?? fac.NameT;
                        if (ship != null) {
                            if (fac.IsPlayer || (fac.IdH == FactionIdH.Virus && A.HasAsimovOverride)) {
                                facName = Texts.Green(facName);
                            } else {
                                facName = Texts.Red(facName);
                            }
                        }
                        sb.AppendFormat("\n - {0}", Texts.LinkToCodex(facName, "Factions", fac.Id));
                    }
                }
            } else {
                sb.AppendFormat("\n - {0}", Texts.Green(T.PronounAny));
            }

            return sb.ToString();
        }

        public bool IsCompatibleWith(NPCShip template) {
            if (template == null) { return true;}
            if (template.CombatState != null) {
                float offensiveness = template.CombatState.Offensiveness;
                if (offensiveness < MinOffensiveness) { return false; }
                if (offensiveness > MaxOffensiveness) { return false; }
            } else if (template?.IsPolice == true) {
                if (MaxOffensiveness < 0.5f) { return false; }
            }
            if (template.Class != null && !Arrays.IsEmpty(CompatibleClasses)) {
                if (!Arrays.Contains(CompatibleClasses, template.Class.Id)) {
                    return false;
                }
            }
            if (template.Faction != null && !Arrays.IsEmpty(CompatibleFactions)) {
                if (!Arrays.Contains(CompatibleFactions, FactionId.Player)) {
                    // asimov override will make player == virus faction
                    // when choosing a tag, so we can pick INF
                    if (A.HasAsimovOverride) {
                        if (!Arrays.Contains(CompatibleFactions, FactionId.Virus)) {
                            return false;
                        }
                    } else {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
