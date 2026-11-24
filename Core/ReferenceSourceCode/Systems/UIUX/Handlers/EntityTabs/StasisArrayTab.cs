using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class StasisArrayTab : BaseEntityTab {
        public const string TabId = "StasisArray";
        private const string SendSubId = "SendToStasis";

        [SelfInit]
        private static void Register() {
            RegisterTab(new StasisArrayTab());
        }

        private Tile tile;
        private StasisArrayComp stasis;
        private StasisArrayMatrixComp matrix;
        private GameState S;
        private WBase sendBtn;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.ObjectsDevicesStasisarray;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<StasisArrayComp>(out var sc)) { return false; }
            this.tile = tile;
            this.stasis = sc;
            if (!tile.TryGetComponent(out matrix)) {
                D.Err("StasisArrayMatrixComp is missing for the StasisArray! {0}", tile.Definition.Id);
            }
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            stasis.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var statusBlock = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, statusBlock.Container, idx++);
            header.Label.Load(T.CryoUsed);
            var beingPercent = stasis.Capacity > 0
                ? (float) stasis.BeingCount / stasis.Capacity
                : 0f;
            header.Progress.Load(Units.XOutOfY(stasis.BeingCount, stasis.Capacity), beingPercent);
            UIUtils.AddTip(lifetime, header.Progress, T.CryoUsedDesc);



            if (!stasis.IsPowerOK && !stasis.IsElysium) {
                var unpoweredRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, statusBlock.Container, idx++);
                unpoweredRow.Load(IconId.CWarning, T.CryoNotEnoughPower);
                UIUtils.AddTip(lifetime, unpoweredRow, T.CryoNotEnoughPowerDesc);
            }

            if (!stasis.IsElysium) {
                var stabilityRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                    UIPrefabId.WContentProgress, statusBlock.Container, idx++);
                stabilityRow.Load(
                    stasis.Stability > 0.6f ? IconId.CCheck : IconId.CCross,
                    stasis.StabilityTitle(),
                    stasis.Stability,
                    Units.Percentage(stasis.Stability));
                UIUtils.AddTip(lifetime, stabilityRow, T.CryoStabilityDesc);
            }

            var sendRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, group.Container, idx++);
            sendBtn = sendRow;
            sendRow.Load(IconId.CCryogenics, T.StasisSendButton, null, () => {
                ShowSendToStasisSubPanel(sendRow);
            });

            if (SubPanelWidget.IsOpenFor == SendSubId && sendBtn != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowSendToStasisSubPanel(sendBtn);
                var btn = sendBtn;
                Coroutines.OnNextFrame(() => {
                    S.UIQuery.GetEntitySubPanel.Ask().RepositionWidget(btn, connectorOnly: false);
                });
            }

            if (A.InstantBuild) {
                var devBtn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WContent2ButtonSimple, group.Container, idx++);
                devBtn.Load("Dev: Spawn Being Now", () => {
                    var data = StoryEventData.CreateFor(StoryEventDef.Get("OnDemand/StasisWakeUp"));
                    data.Seed = Rng.UInt;
                    S.Query.StartStoryEvent.Ask(data);
                });
            }

            return true;
        }

        private void ShowSendToStasisSubPanel(WBase forWidget) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.StasisSendButton);

            var block = UISpawn.Block(l, subPanel.Container, 0);
            int idx = 0;
            int added = 0;
            foreach (var b in S.Beings.Alive) {
                var bd = b.Data;
                if (!bd.IsPlayer || !bd.Species.IsBiological) { continue; }
                if (bd.Body.IsIncapacitated) { continue; }
                if (bd.Mind.Role != ColonyRole.Unassigned) { continue; }
                var w = UISpawn.Widget<WContentButton2>(l,
                    UIPrefabId.WContent2Button2, block.Container, idx++);
                w.Item.Load(null, bd.Name, () => {
                    BeingUtils.Select(bd, true);
                });
                w.Button.Load(T.VerbSend, () => {
                    SendToStasis(bd);
                });
                bd.Avatar.Load(w.Item.Image);
                added++;
            }
            if (added == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }

            subPanel.Show(forWidget, SendSubId);
        }

        private void SendToStasis(BeingData bd) {
            if (!A.HasAsimovOverride && stasis.Stability <= StasisArrayComp.PreventWakeUpThreshold) {
                S.Sig.ShowErrorMessage.Send(T.StasisSendStabilityTooLow);
                return;
            }
            var being = bd.Being;
            if (being == null) { return; }
            var goal = AIGoal.DirectOrder(JobTypeIdH.Task, IconId.CCryogenics,
                    T.StasisSendButton, being.AIAgent, G.Ticks)
                .WithVar("StasisArrayId", stasis.EntityId)
                .WithWorldToDesired(AIVarsH.IsSentToStasis, false, true);
            S.Query.ForceGoal.Ask(goal, true);
            S.UIQuery.CloseEntitySubPanel.Ask();
        }

        public override bool LoadFooter(WGroup group) {
            bool hasFooter = false;
            int idx = 0;
            if (!stasis.IsElysium) {
                var alsRow = UISpawn.Widget<WToggleText>(lifetime,
                    UIPrefabId.WContent4Toggle, group.Container, idx++);
                alsRow.Load(T.NounAdvancedLifeSupport, stasis.IsALSOn, isOn => {
                    stasis.IsALSOn = isOn;
                    ReloadTab();
                });
                UIUtils.AddTip(lifetime, alsRow, T.LifeSupportLevelDesc);

                if (stasis.IsALSOn) {
                    string preventIcon = stasis.PreventWakeUps
                        ? (stasis.WakeUpsPrevented ? IconId.CCheck : IconId.WCheck)
                        : IconId.CCross;
                    var preventRow = UISpawn.Widget<WToggleText>(lifetime,
                        UIPrefabId.WContent4Toggle, group.Container, idx++);
                    preventRow.Load(T.StasisPreventWakeUps,
                        stasis.PreventWakeUps, isOn => {
                            stasis.PreventWakeUps = isOn;
                            ReloadTab();
                        });
                    UIUtils.AddTipFunc(lifetime, preventRow, () =>
                        T.StasisPreventWakeUpsTip_V1.F(
                            Units.Percentage(StasisArrayComp.PreventWakeUpThreshold)));
                }
                hasFooter = true;
            }
            if (!stasis.IsElysium && matrix != null && tile.ENode?.IsReachable == true && A.HasAsimovOverride) {
                var elysiumBtn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WPanelButton_Red, group.Container, idx++);
                elysiumBtn.Load(T.EnableElysiumMode, () => {
                    if (!S.BB.Get(BBKeyH.ElysiumAvailable, false)) {
                        WPopup.Create(false, IconId.CRobotEye, T.StasisElysium, T.ElysiumModeUnavailable)
                            .WithAction(T.OpenResearch, () => {
                                S.UIQuery.ShowInResearchTree.Ask(The.Defs.FastGet(DefIdsH.ResearchRogueAIElysium));
                            })
                            .WithAction2(T.Close, null)
                            .Spawn();
                        return;
                    }
                    WPopup.Create(false, IconId.CRobotEye, T.StasisElysium, T.ElysiumModeConfirm)
                        .WithAction(T.Confirm, () => {
                            Coroutines.Start(matrix.EnableElysium(), this, "Elysium", false, false);
                        })
                        .WithAction2(T.Cancel, null)
                        .Spawn();
                });
                hasFooter = true;
            }
            return hasFooter;
        }

        protected override void Cleanup() {
            tile = null;
            stasis = null;
            matrix = null;
            S = null;
            sendBtn = null;
        }
    }
}
