using System;
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public sealed class BodyPartComp : BaseComponent<BodyPartComp>,
            IDefDescriptionProvider, IStatProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BodyPartComp());
        }
        private string partDefId;
        public BodyPartDef PartDef => Obj.Data.BodyPart; //partDef ??= BodyPartDef.Get(partDefId);
        public bool HasSubmenuNow => true;
        public string SubmenuTitle => T.NounBodyPart;
        public override string ToString() {
            return " * BodyPart";
        }

        protected override void OnConfig() {
            partDefId = Config.GetString(PropertyIdH.BodyPartDefId);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                Ready.When(WhenH.ObjsLoaded, LoadBodyPart);
            } else {
                entity.AfterInitialize += LoadBodyPart;
            }
        }

        private void LoadBodyPart() {
            if (Obj.Data.BodyPart == null) {
                D.Err("No BodyPart in Obj.Data! Spawning fresh: {0}", Obj);
                Obj.Data.BodyPart = BodyPartDef.Get(partDefId);
            }
            if (Obj.Data.BodyPartState == null) {
                D.Err("No BodyPartState in Obj.Data with BodyPart! Spawning fresh: {0}", Obj);
                Obj.Data.BodyPartState = BodyPartState.BrandNew();
                if (Obj.Data.Quality > 0f) {
                    Obj.Data.BodyPartState.SetQuality(Obj.Data.Quality);
                }
            }
            Obj.Data.Quality = Obj.Data.BodyPartState.Quality;
            // Obj.MainDataProvider.EntityName = Obj.Data.ListName;
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            if (!BodyPartDef.TryForDef(def.IdH, out var partDef)) {
                D.Err("BodyPartComp does not have a BodyPartDef for def: {0}", def);
                return addedAnything;
            }
            if (!partDef.HoldsMind) {
                return addedAnything;
            }
            Def.PrependAdditionalInfo(sb, addedAnything);
            addedAnything = true;
            sb.Append(T.MindTransfer);
            sb.Append(": ");
            if (partDef.SupportsMindTransfer) {
                sb.Append(T.Yes);
            } else {
                sb.Append(T.No);
            }
            sb.Append(", ");
            sb.Append(T.ResearchMachinelearning);
            sb.Append(": ");
            if (partDef.SupportsMLBooth) {
                sb.Append(T.Yes);
            } else {
                sb.Append(T.No);
            }
            return addedAnything;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        public string StatsCategoryName => T.NounBodyPart;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var partDef = Obj.Data.BodyPart;
            if (partDef == null) { return; }

            var abilityRows = new List<(string icon, string name)>();
            if (!Arrays.IsEmpty(partDef.AddsAbilities)) {
                foreach (var ab in partDef.AddsAbilities) {
                    var ability = Ability.Get(ab);
                    if (ability != null && !ability.IsInternal) {
                        abilityRows.Add((ability.IconId ?? IconId.CInfo, ability.NameT));
                    }
                }
            }
            StatListUI.AddExpandableList(life, block, ref idx, T.ProvidesAbilities, abilityRows,
                IconId.COperate);

            var state = Obj.Data.BodyPartState;
            var quality = state?.Quality ?? Obj.Data.Quality;
            var statsBeing = new StatsBeing();
            var providesRows = new List<(string icon, string name, string value, Func<string> tip)>();
            var modifiesRows = new List<(string icon, string name, string value)>();

            if (!Arrays.IsEmpty(partDef.StatSources)) {
                foreach (var ss in partDef.StatSources) {
                    if (!statsBeing.Find(ss.StatId, out var stat)) { continue; }
                    // shows the potential at the part's quality; the tooltip explains what
                    // its current condition does to it once installed
                    stat.UpdateBase(BodyPartStatMath.WithQuality(ss, quality));
                    Func<string> tip = null;
                    if (state != null) {
                        var src = ss;
                        tip = () => {
                            var sb = Caches.StringBuilder2;
                            sb.Clear();
                            BodyPartStatMath.AppendBreakdown(sb, stat, src, state);
                            return sb.ToString();
                        };
                    }
                    providesRows.Add((Stat.IconFor(stat), Stat.NameFor(stat), stat.Display(), tip));
                }
            }

            if (!Arrays.IsEmpty(partDef.StatModifiers)) {
                foreach (var sm in partDef.StatModifiers) {
                    if (!statsBeing.Find(sm.Item1, out var stat)) { continue; }
                    stat.UpdateBase(0);
                    modifiesRows.Add((Stat.IconFor(stat), Stat.NameFor(stat), sm.Item2.DisplayValue(stat)));
                }
            }

            StatListUI.AddExpandableList(life, block, ref idx, T.ProvidesStats, providesRows);
            StatListUI.AddExpandableList(life, block, ref idx, T.ModifiesStats, modifiesRows);
        }
    }
}
