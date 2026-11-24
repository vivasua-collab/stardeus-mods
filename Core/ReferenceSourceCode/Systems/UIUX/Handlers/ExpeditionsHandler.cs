using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class ExpeditionsHandler : BaseWidgetHandler<ExpeditionsWidget> {
        private Lifetime miniRootLife;
        private readonly Dictionary<Expedition, Lifetime> miniLives = new();

        public ExpeditionsHandler(GameState s, ExpeditionsWidget widget) : base(s, widget) {
            widget.Initialize(s);
            s.UIQuery.ShowExpedition.SetResponder(OnShowExpedition);
            s.UIQuery.ToggleExpedition.SetResponder(OnToggleExpedition);
            s.UIQuery.ShowExpeditions.SetResponder(OnShowExpeditions);
            s.UIQuery.StartExpeditionTo.SetResponder(OnStartExpeditionTo);
            s.UIQuery.RebuildExpeditions.SetResponder(OnRebuildExpeditions);

            miniRootLife = Lifetime.Create("ExpeditionMinis");
            s.Sig.ExpeditionStarted.AddListener(OnExpeditionStarted);
            s.Sig.ExpeditionEnded.AddListener(OnExpeditionEnded);
            s.Sig.AfterLoadState.AddListener(OnAfterLoadState);
        }

        private bool OnRebuildExpeditions() {
            if (widget.IsShowing) {
                widget.Reload();
                return true;
            }
            return false;
        }

        private bool OnShowExpedition(Expedition exp) {
            widget.Load(exp.Context);
            widget.Toggle(true);
            return true;
        }
        private bool OnToggleExpedition(Expedition exp) {
            if (!UIShowing.Expeditions) {
                return OnShowExpedition(exp);
            }
            if (widget.Ctx?.Expedition == exp) {
                return OnShowExpeditions(false);
            }
            widget.Load(exp.Context);
            widget.Toggle(true);
            return true;
        }

        private bool OnStartExpeditionTo(SpaceObject so) {
            var ctx = new ExpeditionContext();
            ctx.TargetSO.SetSilent(so);
            ctx.MiningOrders = ExpeditionMiningOrders.Default(S);
            widget.Load(ctx);
            if (widget.IsShowing) {
                widget.SubPanel.Close();
            } else {
                widget.Toggle(true);
            }
            return true;
        }

        private bool OnShowExpeditions(bool show) {
            if (show) {
                var ctx = new ExpeditionContext();
                ctx.MiningOrders = ExpeditionMiningOrders.Default(S);
                widget.Load(ctx);
            } else {
                if (widget.IsShowing) {
                    widget.SubPanel.Close();
                }
            }
            widget.Toggle(show);
            return true;
        }

        private void OnExpeditionStarted(Expedition exp) {
            SpawnMini(exp);
            widget.ResumeClockAfterSetup();
        }

        private void OnExpeditionEnded(Expedition exp) {
            DespawnMini(exp);
        }

        private void OnAfterLoadState(GameState s) {
            miniRootLife?.Dispose();
            miniLives.Clear();
            miniRootLife = Lifetime.Create("ExpeditionMinis");
            // ExpeditionSys defers RestoreData to WhenH.BeingsLoaded, so the
            // Expeditions list is empty here. AllSystemsLoaded is set after the
            // entire AfterLoadState dispatch, guaranteeing the list is populated.
            Ready.When(WhenH.ExpeditionsRestored, RepopulateMinis);
        }

        private void RepopulateMinis() {
            foreach (var exp in S.Sys.Expedition.Expeditions) {
                SpawnMini(exp);
            }
        }

        private void SpawnMini(Expedition exp) {
            if (exp?.IsValid != true) { return; }
            if (miniLives.ContainsKey(exp)) { return; }
            var container = widget.MiniWidgetsContainer;
            if (container == null) { return; }
            var life = miniRootLife.CreateChild("expmini");
            miniLives[exp] = life;
            var w = UISpawn.Widget<WExpeditionMini>(life, UIPrefabId.WExpeditionMini,
                container.Container, -1);
            w.Load(S, exp, life);
        }

        private void DespawnMini(Expedition exp) {
            if (miniLives.TryGetValue(exp, out var life)) {
                life.Dispose();
                miniLives.Remove(exp);
            }
        }
    }
}
