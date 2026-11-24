#if !PRODUCTION_BUILD
// #define DEBUG_COMBAT
#endif
using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.UI;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class EntityOverview_BeingManagement : EntityOverviewBlock {
        public const string BlockId = "BeingManagement";
        public override int Order => 0;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() {
            Register(new EntityOverview_BeingManagement());
        }

        [ClearOnReload] private static bool attackMethodsExpanded;

        public override bool ShowFor(GameState s, BeingData being) {
            return true;
        }

        public override void LoadContent(EntityOverviewContext ctx, WGroup group, ref int gIdx) {
            var being = ctx.Being;
            if (being == null) { return; }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.UiGroupManagement);

            AddBodyRow(ctx, block.Container, ref idx, being);

            var subEquipment = new BeingEquipmentSubTab(ctx);
            if (subEquipment.Spawn(block.Container, idx)) {
                idx++;
            }

            var subJobs = new BeingJobsSubTab(ctx);
            if (subJobs.Spawn(block.Container, idx)) {
                idx++;
            }

            if (being.IsPlayer && being.Being != null
                    && being.Being.TryGetComponent<BotDockableComp>(out _)) {
                var subDockable = new BeingDockableSubTab(ctx);
                if (subDockable.Spawn(block.Container, idx)) {
                    idx++;
                }
            }

            if (being.IsPlayer && being.Being != null
                    && being.Being.TryGetComponent<CleaningBotComp>(out _)) {
                var subCleaning = new CleaningBotSubTab(ctx);
                if (subCleaning.Spawn(block.Container, idx)) {
                    idx++;
                }
            }

            if (being.IsFriendly && being.Being != null
                    && being.Being.TryGetComponent<BedUserComp>(out _)
                    && being.HasNeed(NeedIdH.Sleep)) {
                var subBedUser = new BeingBedUserSubTab(ctx);
                if (subBedUser.Spawn(block.Container, idx)) {
                    idx++;
                }
            }

            AddCombatRows(ctx, block.Container, ref idx, being);
        }

        private void AddBodyRow(EntityOverviewContext ctx, RectTransform container, ref int idx, BeingData being) {
            if (!being.IsPlayer && !being.Body.IsIncapacitated && !being.HasStateFlag(BeingState.Dead)) {
                return;
            }
            var row = UISpawn.Widget<WButtonIconText2>(ctx.Lifetime,
                UIPrefabId.WContent2Button, container, idx++);
            being.Body.StateB.Bind(ctx.Lifetime, (st) => {
                var state = BodyUtils.StateName(st, being.Species.IsBiological);
                row.Load(IconId.CBodyGeneric, T.NounBody, state, () => {
                    A.S.UIQuery.ToggleSurgeriesFor.Ask(being);
                });
            });
        }

        private static void AddCombatRows(EntityOverviewContext ctx, RectTransform container,
                ref int idx, BeingData being) {
            if (being.Being == null) { return; }
            bool showCombat = being.IsPlayer;
            #if DEBUG_COMBAT
            showCombat = true;
            #endif
            if (!showCombat) { return; }
            if (being.HasStateFlag(BeingState.Dead)) { return; }
            if (!being.Being.TryGetComponent<CombatUnitComp>(out _)) { return; }


            var unit = being.Being.AIAgent.CombatUnit;
            var lifetime = ctx.Lifetime;

            // Combat Reaction
            var curConfig = unit.Config;
            if (being.Being.AIAgent.DirectControl.IsControllable) {
                var cfgOpts = new System.Collections.Generic.List<string>();
                foreach (var opt in CombatUnitComp.CfgOpts) {
                    cfgOpts.Add($"{T.CombatReaction}: {opt}");
                }
                var cfgRow = UISpawn.Widget<WDropdownWithIcon>(lifetime,
                    UIPrefabId.WContent2Dropdown, container, idx++);
                cfgRow.Icon.Load(CombatUnitComp.IconFor(curConfig));
                cfgRow.Dropdown.Load(cfgOpts, (int)curConfig, v => {
                    var chosen = (CombatUnitConfig)v;
                    CombatReactionUI.Apply(unit, chosen);
                    cfgRow.Icon.Load(CombatUnitComp.IconFor(chosen));
                });
            } else {
                var cfgRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, container, idx++);
                cfgRow.Load(CombatUnitComp.IconFor(curConfig), T.CombatReaction,
                    CombatUnitComp.NameFor(curConfig));
                UIUtils.AddTip(lifetime, cfgRow, Texts.Red(T.AdjNotControllable));
            }

            // Current attack target
            // var target = unit.AttackTarget;
            // var targetRow = UISpawn.Widget<WIconText2>(lifetime,
            //     UIPrefabId.WContent2Text2, container, idx++);
            // targetRow.Load(IconId.CAttackTarget, T.NounAttackTarget,
            //     target != null ? target.Agent.Being.Data.Name : Texts.Grey(T.Empty));
        }
    }
}
