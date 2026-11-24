using System;
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Input;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceBiome {
        public SpaceBiomeType Type;
        public const int NumBiomes = 7;
        public int BiomeId => (int) (Difficulty * ((float) NumBiomes - 0.0001f));
        // Green on the starmap
        public bool IsEasiest => BiomeId == 0;
        public float Difficulty;
        public Color IconColor {
            get {
                var group = Palette.ForGroup("SpaceBiome");
                var bId = BiomeId;
                if (group != null && bId < group.Count) {
                    return group[bId].Color;
                }
                D.Err("Not enough / no colors in SpaceBiome for id {0}: {1}",
                    Difficulty, D.Dump(group));
                return Color.magenta;
            }
        }
        public string DifficultyText => Units.PercentageColoredInverse(Difficulty, 0.25f, 0.75f);
        public SpaceEntityTemplate Template { get; private set; }
        public readonly Dictionary<DepositConfig, int> Deposits = new();
        public readonly Dictionary<SpaceEffectConfig, int> Effects = new();
        public readonly Dictionary<POIConfig, int> POIs = new();
        public Dictionary<FactionDef, float> FactionsByDominance;
        public HashSet<DepositConfig> UnavailableDeposits;
        public HashSet<SpaceEffectConfig> UnavailableEffects;
        public HashSet<POIConfig> UnavailablePOIs;
        public HashSet<SpaceEntityTemplate> AllowedTemplates;
        public HashSet<SpaceEntityTemplate> UnavailableTemplates;

        public bool IsTemplate(int idH) {
            return Template?.IdH == idH;
        }

        public void AllowTemplate(SpaceEntityTemplate tpl, bool allow) {
            if (allow) {
                AllowedTemplates ??= new();
                AllowedTemplates.Add(tpl);
            } else {
                UnavailableTemplates ??= new();
                UnavailableTemplates.Add(tpl);
            }
        }

        public bool IsTemplateAllowed(SpaceEntityTemplate tpl, out bool hasMemory) {
            bool hasInAllowed = AllowedTemplates?.Contains(tpl) == true;
            bool hasInUnallowed = UnavailableTemplates?.Contains(tpl) == true;
            hasMemory = hasInAllowed | hasInUnallowed;
            return !hasInUnallowed;
        }

        public void SetTemplate(SpaceEntityTemplate tpl) {
            Template = tpl;
        }

        public void RemoveEffect(string id) {
            foreach (var eff in Effects) {
                if (eff.Key.Id == id) {
                    Effects.Remove(eff.Key);
                    return;
                }
            }
        }

        public void AddAssignable(SpaceAssignable cfg, int q = 1, bool checkDifficulty = true) {
            if (cfg is DepositConfig dcfg) {
                if (checkDifficulty && Type == SpaceBiomeType.Sector && dcfg.MinDifficulty > Difficulty) {
                    D.Err("Assigning deposit that isn't supposed to be assigned due to difficulty: {0} -> {1}",
                        dcfg, this);
                }
                if (!Deposits.TryGetValue(dcfg, out var cnt)) {
                    cnt = 0;
                }
                Deposits[dcfg] = cnt + q;
            }
            if (cfg is SpaceEffectConfig ecfg) {
                if (Difficulty < 0.1f && ecfg.Id == "MicroMeteoroids") {
                    D.Err("Assigning meteorites to diff < 0.1");
                }
                if (!Effects.TryGetValue(ecfg, out var cnt)) {
                    cnt = 0;
                }
                Effects[ecfg] = cnt + q;
            }
            if (cfg is POIConfig pcfg) {
                if (!POIs.TryGetValue(pcfg, out var cnt)) {
                    cnt = 0;
                }
                POIs[pcfg] = cnt + q;
            }
        }

        public bool HasAssignable(SpaceAssignable cfg, bool includeUnavailable) {
            if (cfg is DepositConfig dcfg) {
                if (includeUnavailable && UnavailableDeposits?.Contains(dcfg) == true) {
                    return true;
                }
                return Deposits.ContainsKey(dcfg);
            }
            if (cfg is SpaceEffectConfig ecfg) {
                if (includeUnavailable && UnavailableEffects?.Contains(ecfg) == true) {
                    return true;
                }
                return Effects.ContainsKey(ecfg);
            }
            if (cfg is POIConfig pcfg) {
                if (includeUnavailable && UnavailablePOIs?.Contains(pcfg) == true) {
                    return true;
                }
                return POIs.ContainsKey(pcfg);
            }
            return false;
        }

        public bool HasEffect(int effectIdH) {
            foreach (var eff in Effects) {
                if (eff.Key.IdH == effectIdH) { return true; }
            }
            return false;
        }

        public void AddUnavailableAssignable(SpaceAssignable cfg) {
            if (cfg.NeverUnavailable) {
                return;
            }
            if (cfg is DepositConfig dcfg) {
                UnavailableDeposits ??= new();
                UnavailableDeposits.Add(dcfg);
            }
            if (cfg is SpaceEffectConfig ecfg) {
                UnavailableEffects ??= new();
                UnavailableEffects.Add(ecfg);
            }
            if (cfg is POIConfig pcfg) {
                UnavailablePOIs ??= new();
                UnavailablePOIs.Add(pcfg);
            }
        }

        public void ClearUnavailable(Type type) {
            if (type == typeof(SpaceEffectConfig)) {
                UnavailableEffects = null;
            }
            if (type == typeof(DepositConfig)) {
                UnavailableDeposits = null;
            }
            if (type == typeof(POIConfig)) {
                UnavailablePOIs = null;
            }
        }

        public bool HasDepositsFor(MatType mat) {
            foreach (var dep in Deposits) {
                if (dep.Key.Mat == mat) {
                    return true;
                }
            }
            return false;
        }

        public void AddFaction(FactionDef fac, float dominance) {
            FactionsByDominance ??= new();
            if (!FactionsByDominance.TryAdd(fac, dominance)) {
                D.Err("Faction already added to biome: {0} | {1}", fac, this);
            }
        }

        public override string ToString() {
            return $"Biome[{Difficulty}|{Type}|{Template?.Id}]";
        }

        public string DepositsDebugText {
            get {
                if (Arrays.IsEmpty(Deposits)) {
                    return T.Empty;
                }
                var sb = Caches.StringBuilder3;
                sb.Clear();
                foreach (var dep in Deposits) {
                    if (dep.Value > 1) {
                        sb.AppendFormat("\n - {0} x{1}", dep.Key.Mat.Def.CodexLink, dep.Value);
                    } else {
                        sb.AppendFormat("\n - {0}", dep.Key.Mat.Def.CodexLink);
                    }
                }
                return sb.ToString();
            }
        }
        public string EffectsDebugText {
            get {
                if (Arrays.IsEmpty(Effects)) {
                    return T.Empty;
                }
                var sb = Caches.StringBuilder3;
                sb.Clear();
                foreach (var eff in Effects) {
                    if (eff.Value > 1) {
                        sb.AppendFormat("\n - {0} x{1}", Texts.Accented3(eff.Key.Id), eff.Value);
                    } else {
                        sb.AppendFormat("\n - {0}", Texts.Accented3(eff.Key.Id));
                    }
                }
                return sb.ToString();
            }
        }
        public string POIsDebugText {
            get {
                if (Arrays.IsEmpty(POIs)) {
                    return T.Empty;
                }
                var sb = Caches.StringBuilder3;
                sb.Clear();
                foreach (var poi in POIs) {
                    if (poi.Value > 1) {
                        sb.AppendFormat("\n - {0} x{1}", Texts.Yellow(poi.Key.Id), poi.Value);
                    } else {
                        sb.AppendFormat("\n - {0}", Texts.Yellow(poi.Key.Id));
                    }
                }
                return sb.ToString();
            }
        }
        public string FactionsDebugText {
            get {
                if (Arrays.IsEmpty(FactionsByDominance)) {
                    return T.Empty;
                }
                var sb = Caches.StringBuilder3;
                sb.Clear();
                foreach (var fac in FactionsByDominance) {
                    sb.AppendFormat("\n - {0} {1}", Texts.Green(fac.Key.NameT), Units.Percentage(fac.Value));
                }
                return sb.ToString();
            }
        }

        public bool CantBeInbound {
            get {
                if (Template is SectorTemplate secTpl) {
                    return secTpl.CantBeInbound;
                }
                return false;
            }
        }

        public void DebugTooltip(StringBuilder sb) {
            var biome = this;
            sb.Append(GlyphMap.BRHR);
            sb.Append("\nDEV Biome Debug:\n");
            sb.Append("Template: ");
            sb.Append(Texts.Accented(biome.Template?.Id) ?? Texts.Red("NONE"));
            sb.AppendLine();
            sb.Append("Deposits: ");
            sb.AppendJoin(", ", biome.DepositsDebugText);
            sb.AppendLine();
            sb.Append("Effects: ");
            sb.AppendJoin(", ", biome.EffectsDebugText);
            sb.AppendLine();
            sb.Append("POIs: ");
            sb.AppendJoin(", ", biome.POIsDebugText);
            sb.AppendLine();
            sb.Append("Factions: ");
            sb.AppendJoin(", ", biome.FactionsDebugText);
        }
    }
}
