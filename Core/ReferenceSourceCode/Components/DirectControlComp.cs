using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.UI;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using KL.Utils;

namespace Game.Components {
    public sealed class DirectControlComp : BaseComponent<DirectControlComp>, IRareTickListener {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new DirectControlComp());
        }

        public bool IsUnderDirectControl;
        // Fire at Will: off, a drafted unit only attacks what the player orders it to. Kept
        // across drafts.
        public bool FireAtWill = true;
        private BCallback directControlChangedB;
        public BCallback DirectControlChangedB =>
            directControlChangedB ??= new BCallback();

        private long lastOrderTick;
        private bool combatMode;
        private long combatEndedTick = -1;
        private bool isSubscribed;

        public bool CheckCanToggleDirectControl(out string tip) {
            if (IsUnderDirectControl) {
                tip = T.DirectControlRelease;
                return true;
            }
            if (!Being.IsAlive) {
                tip = Texts.Red(T.Dead);
                return false;
            }
            if (Being.Data.Body.IsIncapacitated) {
                tip = Texts.Red(T.BeingStateIncapacitated);
                return false;
            }
            // e.g. lost both legs: conscious, so "Incapacitated" would be wrong
            if (Being.Motion.IsImmobilized) {
                tip = Texts.Red(T.InfoBeingImmobilized);
                return false;
            }
            if (!Being.Data.EffectiveFaction.IsPlayer) {
                tip = Texts.Red(T.DirectOrderWrongFaction);
                return false;
            }
            if (!IsControllable) {
                tip = Texts.Red(T.AdjNotControllable);
                return false;
            }
            tip = Texts.Green(T.DirectOrderAvailable);
            return true;
        }

        public void SetFireAtWill(bool yes) {
            if (FireAtWill == yes) { return; }
            FireAtWill = yes;
            Being.AIAgent.CombatUnit.OnFireAtWillChanged();
        }

        public bool IsControllable =>
            (Being.Data.EnvFlags & BeingEnvFlags.Controllable) == BeingEnvFlags.Controllable;

        public void RecordDirectOrderActivity() {
            if (!IsUnderDirectControl) { return; }
            lastOrderTick = G.Ticks;
        }

        public void RareTick(long currentTick) {
            if (!IsUnderDirectControl) { return; }
            if (lastOrderTick == 0) {
                lastOrderTick = currentTick;
                if (S.Sys.Combat.IsAnyCombatInProgress) {
                    combatMode = true;
                    combatEndedTick = -1;
                }
                return;
            }
            if (S.Sys.Combat.IsAnyCombatInProgress) { return; }
            if (combatMode) {
                if (combatEndedTick < 0) { return; }
                var deadline = combatEndedTick > lastOrderTick ? combatEndedTick : lastOrderTick;
                if (currentTick - deadline >= Consts.TicksPer4Hours) {
                    SetDirectControl(false);
                    RebuildActionsIfSelected();
                }
            } else {
                if (currentTick - lastOrderTick >= Consts.TicksPer8Hours) {
                    SetDirectControl(false);
                    RebuildActionsIfSelected();
                }
            }
        }

        private void RebuildActionsIfSelected() {
            if (S.Query.IsSelected.Ask(Being)) {
                UIUtils.RefreshSelectedEntityUI();
            }
        }

        private void OnCombatChanged(bool yes, CombatFlags flags) {
            if (!IsUnderDirectControl) { return; }
            if (yes) {
                combatMode = true;
                combatEndedTick = -1;
            } else if (!S.Sys.Combat.IsAnyCombatInProgress) {
                combatEndedTick = G.Ticks;
            }
        }

        public bool SetDirectControl(bool yes) {
            if (yes) {
                if (!IsControllable) {
                    D.Err("Attempt to set direct control on non-controllable being! {0}", Being);
                    return false;
                }
                var g = Being.AIAgent.Goal;
                if (g != null) {
                    if (g.IsNonInterruptable) {
                        D.Err("Attempt to set direct control in mid non-interruptable goal: {0} | {1}",
                            g, Being);
                        return false;
                    }
                    AIGoal.Interrupt(g, G.Ticks);
                }
                Being.Motion.HidePathViz();
                lastOrderTick = G.Ticks;
                combatMode = S.Sys.Combat.IsAnyCombatInProgress;
                combatEndedTick = -1;
                if (!isSubscribed) {
                    isSubscribed = true;
                    S.Sig.CombatChanged.AddListener(OnCombatChanged);
                }
            }
            IsUnderDirectControl = yes;
            directControlChangedB?.Invoke();
            Being.Data.Inventory?.UpdateLooks(force: true);
            // drafted armor shows, and slows
            Being.Data.Inventory?.UpdateShownStatModifiers();
            Being.Data.UpdateAvatarName();
            // avatars refresh on ticks, and drafting usually happens while paused
            Being.Data.UpdateAvatarStatus();
            CheckThoughtBubble();
            S.Sig.DirectControlChanged.Send(Being);
            BeingUtils.PlaySelectSoundFor(Being);
            if (yes) {
                Transmit(this, MsgIdH.DirectControlOn);
            } else {
                Transmit(this, MsgIdH.DirectControlOff);
                if (Being.Motion.IsForcedWalk) {
                    Being.Motion.Abort(T.AdRejCancelled);
                }
                if (isSubscribed) {
                    isSubscribed = false;
                    S.Sig.CombatChanged.RemoveListener(OnCombatChanged);
                }
                lastOrderTick = 0;
                combatMode = false;
                combatEndedTick = -1;
            }
            return true;
        }

        private void CheckThoughtBubble() {
            if (IsUnderDirectControl) {
                ShowThoughtBubble(IconId.CDraft, T.AdjDrafted);
            } else {
                HideThoughtBubble();
            }
        }

        // public override void OnLateReady(bool wasLoaded) {
            // too early
            // if (wasLoaded) {
            //     CheckThoughtBubble();
            // }
        // }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.BeingDataWasSet) {
                CheckThoughtBubble();
            } else if (message == MsgIdH.ImmobilizeStarted) {
                if (IsUnderDirectControl) {
                    SetDirectControl(false);
                }
            }
        }

        public override void OnRemove() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig.CombatChanged.RemoveListener(OnCombatChanged);
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsDirectControl", IsUnderDirectControl);
            data.SetLong("LastOrderTick", lastOrderTick);
            data.SetBool("CombatMode", combatMode);
            data.SetLong("CombatEndedTick", combatEndedTick);
            data.SetBool("FireAtWill", FireAtWill);
        }

        protected override void OnLoad(ComponentData data) {
            IsUnderDirectControl = data.GetBool("IsDirectControl", false);
            lastOrderTick = data.GetLong("LastOrderTick", 0);
            combatMode = data.GetBool("CombatMode", false);
            combatEndedTick = data.GetLong("CombatEndedTick", -1);
            FireAtWill = data.GetBool("FireAtWill", true);
            if (IsUnderDirectControl) {
                if (!isSubscribed) {
                    isSubscribed = true;
                    S.Sig.CombatChanged.AddListener(OnCombatChanged);
                }
            }
        }

        public override string ToString() {
            return " * DirectControl []";
        }
    }
}
