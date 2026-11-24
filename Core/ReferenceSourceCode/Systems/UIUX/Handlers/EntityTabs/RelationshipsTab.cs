using System;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class RelationshipsTab : BaseEntityTab {
        public const string TabId = "Relationships";
        [SelfInit]
        private static void Register() {
            RegisterTab(new RelationshipsTab());
        }

        private BeingData being;
        private BeingBonds bonds;
        private GameState S;
        public override string TabName => T.NeedSocial;

        public override int Order => 10;

        protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
            being.Mind.Bonds?.Changed.Bind(parentLifetime, _ => {
                ReloadTab();
            }, false);
        }

        // Who a sleeper is tied to is part of who they are, and the reason to care
        public override bool ShowForUnrevealed => true;

        public override bool ShowFor(GameState s, BeingData being) {
            S = s;
            if (being.Mind.Health != null) {
                this.being = being;
                this.bonds = being.Mind.Bonds;
                return true;
            }
            return false;
        }

        public override bool LoadContent(WGroup group) {
            int idx = 0;
            LoadRelationships(group, idx++);
            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var btn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WPanelButton, group.Container, 0);
            btn.Load(T.ViewSocialInteractions, () => {
                WRelationshipPanel.ShowInteractionsFor(lifetime, being.Mind, null);
            });
            return true;
        }


        private void LoadRelationships(WGroup group, int gidx) {
            var block = UISpawn.Block(lifetime, group, gidx);
            int idx = 0;

            int added = 0;
            if (bonds?.Opinions.Count > 0) {
                foreach (var opinion in bonds.Opinions.Values
                        .Where(o => o.TargetMind != null)
                        .OrderByDescending(op => OpinionImportance(op))
                        .ThenBy(op => op.TargetMind.Name.FullName)) {
                    if (AddRelationship(block.Container, opinion, idx)) {
                        idx++;
                        added++;
                    }
                }
            }
            if (added == 0) {
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }
        }

        private int OpinionImportance(BeingOpinion value) {
            if (value.Flags.HasFlag(BeingOpinionFlags.Master)) {
                return 100;
            } else if (value.Flags.HasFlag(BeingOpinionFlags.Spouse)) {
                return 90;
            } else if (value.Flags.HasFlag(BeingOpinionFlags.Lover)) {
                return 80;
            } else if (value.Flags.HasFlag(BeingOpinionFlags.Crush)) {
                return 70;
            } else if (value.Flags.HasFlag(BeingOpinionFlags.Pet)) {
                return 60;
            } else if (value.Flags.HasFlag(BeingOpinionFlags.Nemesis)) {
                return 50;
            } else if (value.Flags.HasFlag(BeingOpinionFlags.Friend)) {
                return 40;
            } else if (value.Flags.HasFlag(BeingOpinionFlags.Acquaintance)) {
                return 30;
            } else {
                return 0;
            }
        }

        private bool AddRelationship(RectTransform rt, BeingOpinion opinion, int idx) {
            var otherM = opinion.TargetMind;
            var myM = bonds.Owner;
            var otherData = otherM.Owner ?? (otherM.FormerOwnerId != 0
                ? S.Query.FindBeingData.Ask(otherM.FormerOwnerId) : null);
            var otherOp = S.Query.GetOpinionAbout.Ask(otherM, myM);
            string status = opinion.StatusOf(otherOp, out _);
            string name;
            if (otherData != null) {
                name = BeingUtils.DecoratedBeingName(otherData, true, otherM.Name.FullNameOrNameless);
            } else {
                name = otherM.LastTimeOfDeath != 0
                    ? $"{GlyphMap.SpriteSkull} {otherM.Name.FullNameOrNameless}"
                    : otherM.Name.FullNameOrNameless;
            }
            var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, rt, idx);
            w.Load(null, name, Texts.Grey(status), () => {
                WRelationshipPanel.SpawnFor(opinion, otherOp);
            });
            S.Query.GetMindAvatar.Ask(otherM)?.Load(w.Image);
            var tip = UIUtils.AddTipFunc(lifetime, w, () => opinion.OpinionTip(otherOp));
            tip.HoverFun = (h) => EntityUtils.ShowEntityLink(null, otherM.Owner?.Being, h);
            return true;
        }

        protected override void Cleanup() {
            being = null;
            bonds = null;
            S = null;
        }

        public override string Id => TabId;
    }
}
