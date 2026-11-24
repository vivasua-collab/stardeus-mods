using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ResearchUnlocksSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new ResearchUnlocksSearchAdapter());
        }

        private const string AdapterId = "ResearchUnlocks";
        public override string Id => AdapterId;
        public override string ResultLabel => T.VerbResearchUnlocks;
        public override int ResultOrder => 20;

        public override bool ShowInMap => false;
        public override bool ShowInResearch => true;

        public struct Result {
            public string Id;
            public string Name;
            public string SearchName;
            public string Desc;
            public Sprite Preview;
            public Def Research;
            public Def Def;
            public int IdH;
            public Result(string id, IResearchUnlockEffect eff) {
                Id = id;
                Name = eff.Title;
                SearchName = Strings.ToSearchString(eff.Title);
                Desc = eff.Tooltip;
                Preview = Images.Sprite(eff.Icon);
                Research = null;
                Def = null;
                IdH = Hashes.S(id);
            }
            public Result(Def def) {
                Id = def.Id;
                Name = def.NameT;
                SearchName = def.SearchNameT;
                Desc = def.DescT;
                Preview = def.PreviewSprite;
                var research = def.Researchable.Prerequisites[0];
                var researchDef = The.Defs.Get(research);
                Research = researchDef;
                Def = def;
                IdH = def.IdH;
            }
        }

        protected override void Query(string input, string q, Regex wordStart) {
            var research = A.S.Research;
            List<Result> unlockables = new();
            foreach (var def in The.Defs.Defs.Values) {
                if (def.IsAbstract || def.IsTemplate) { continue; }
                if (Researchable.IsEmptyPrereq(def)) { continue; }
                if (def.Researchable.IsTreeNode) {
                    if (!research.IsDiscovered(def.IdH)) { continue; }
                    if (def.Researchable.HasUnlockEffects) {
                        foreach (var eff in def.Researchable.UnlockEffects) {
                            var unlockEff = Registries.UnlockEffects.Get(eff);
                            if (unlockEff == null) { continue; }
                            unlockables.Add(new Result(eff, unlockEff));
                        }
                    }
                    continue;
                }
                if (!research.IsDiscovered(Hashes.S(def.Researchable.Prerequisites[0]))) { continue; }
                if (!def.IsInSeason) { continue; }
                unlockables.Add(new Result(def));
            }

            QueryItems(unlockables.OrderBy(d => d.Name), q, wordStart,
                d => d.IdH,
                d => d.SearchName,
                d => d.Name,
                d => d.Desc,
                (d, idx) => new ResearchUnlocksSearchResult(this, d, idx));
        }


        public override BaseSearchResult LoadResult(string resultId, int order) {
            // these can't be pinned
            return null;
        }
    }
}
