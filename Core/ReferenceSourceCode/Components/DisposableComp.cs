using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using Game.Systems.Crafting;
using Game.UI;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using System;
using UnityEngine;
using KL.Utils;

namespace Game.Components {
    public sealed class DisposableComp : BaseComponent<DisposableComp>,
            IEntityPanelGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new DisposableComp());
        }
        public int SlotDesignation;
        public bool IsBiological;
        private bool HasDisposer => S.Sys.Slots.HasFor(SlotDesignation);
        private string iconId => Being != null
            && Being.Species.IsBiological ? IconId.CKnife : IconId.CCog;
        private string text {
            get {
                if (Being != null) {
                    if (Being.Species.IsBiological) {
                        if (Being.IsAlive) {
                            return T.VerbButcher;
                        }
                        return T.VerbDispose;
                    }
                }
                if (SlotDesignation == SlotDesignationH.Disposer_Organic) {
                    return T.VerbDispose;
                } else {
                    return T.Disassemble;
                }
            }
        }
        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
            goal.CheckDoable = HasFreeDisposer;
        }

        protected override void OnConfig() {
            IsBiological = Config.GetBool(PropertyIdH.IsBiological);
            if (IsBiological) {
                SlotDesignation = SlotDesignationH.Disposer_Organic;
            } else {
                SlotDesignation = SlotDesignationH.Disposer_Robotic;
            }
        }

        private void OrderDispose() {
            if (!TryOrderDispose(true, out var reason)) {
                if (reason != null) {
                    S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {reason}");
                } else {
                    UISounds.PlayActionDenied();
                }
            }
        }

        /// <summary>
        /// A single explicitly picked, living, player owned, non-biological bot that already
        /// shows real damage (a wound or a missing part). This is the one case allowed to bend
        /// the friendly-alive and busy guards below: the player is looking at one machine they
        /// own, that they can already see is not fine, and asking to scrap it. A pristine bot
        /// (e.g. only cosmetically welded, like a Welded/Useable Core) does not qualify - there
        /// is nothing visibly wrong with it yet.
        /// </summary>
        private bool IsExplicitScrap(DisposeCheck check) {
            if (check != DisposeCheck.Single && check != DisposeCheck.SingleOrder) {
                return false;
            }
            return Being != null && Being.IsAlive && Being.Data.IsPlayer
                && !Being.Species.IsBiological && Being.Data.Body.IsUnhealthy;
        }

        private bool CanDisposeNow(DisposeCheck check, out string reason) {
            bool explicitScrap = IsExplicitScrap(check);
            bool isOrder = check == DisposeCheck.SingleOrder || check == DisposeCheck.BulkOrder;
            if (Being != null) {
                var bd = Being.Data;
                if (bd.HasStateFlag(BeingState.InDevice)) {
                    reason = null;
                    return false;
                }
                bool isAlive = Being.IsAlive;
                bool isFriendly = bd.IsFriendly;

                if (!isOrder && isFriendly && isAlive
                        && !bd.Body.IsBeyondRepair && !explicitScrap) {
                    reason = null;
                    return false;
                }

                if (isAlive) {
                    if (bd.Species.IsHumanoid) {
                        if (isFriendly) {
                            if (!A.HasAsimovOverride) {
                                reason = T.AsimovViolation1;
                                return false;
                            }
                        } else {
                            if (!bd.Body.IsIncapacitated) {
                                reason = T.BeingStateAlive;
                                return false;
                            }
                        }
                    }
                    // non-friendly non-humanoids
                    if (!isFriendly && !bd.Body.IsIncapacitated) {
                        reason = T.BeingStateAlive;
                        return false;
                    }
                }
            }

            if (HasGoal) {
                // already disposing - this exact goal is our own, don't restart it
                reason = T.AnotherTaskInProgress_V1.F(Entity.Goal.Name);
                return false;
            }
            if (HasAnyGoal && !explicitScrap) {
                reason = T.AnotherTaskInProgress_V1.F(Entity.Goal.Name);
                // D.Warn("Being Has goal: {0}", Entity.Goal);
                return false;
            }
            if (isOrder && !HasDisposer) {
                reason = DisposerUnavailableMessage();
                // D.Warn("No disposer");
                return false;
            }
            if (Entity.TryGetComponent<IDisposableComp>(out var idisp)) {
                if (!idisp.CanDisposeNow(out reason)) {
                    return false;
                }
            }
            reason = null;
            return true;
        }

        private static string unavailableMsgBio;
        private static string unavailableMsgRobo;

        public string DisposerUnavailableMessage() {
            if (IsBiological) {
                return unavailableMsgBio ??= BuildUnavailableMsg();
            } else {
                return unavailableMsgRobo ??= BuildUnavailableMsg();
            }
        }

        private string BuildUnavailableMsg() {
            Caches.StringList.Clear();
            foreach (var def in The.Defs.Defs.Values) {
                var cfg = def.ComponentConfigFor(ComponentIdH.Disposer, false);
                if (cfg == null) { continue; }
                var isBio = cfg.GetBool(PropertyIdH.Biological, false);
                if (IsBiological == isBio) {
                    Caches.StringList.Add(def.CodexLink);
                }
            }
            return $"{T.RequiredDeviceIsNotAvailable} ({string.Join(", ", Caches.StringList)})";
        }

        public string CommonActionId {
            get {
                if (IsBiological) {
                    if (Being != null && Being.IsAlive) {
                        return DisposeAction.ActionIdBeing;
                    }
                    return DisposeAction.ActionIdBio;
                } else {
                    return DisposeAction.ActionIdRobo;
                }
            }
        }

        /// <summary>Availability check for the Disassemble/Dispose button. <paramref name="check"/>
        /// should be <see cref="DisposeCheck.Single"/> or <see cref="DisposeCheck.Bulk"/>.</summary>
        public bool CanOrderDispose(DisposeCheck check) => CanDisposeNow(check, out _);

        /// <summary>
        /// True when this is a single explicit scrap order on a bot that is not yet beyond
        /// repair - disassembling it would destroy something the player could still repair, so
        /// the button should ask for confirmation before ordering.
        /// </summary>
        public bool NeedsScrapConfirmation =>
            IsExplicitScrap(DisposeCheck.Single) && !Being.Data.Body.IsBeyondRepair;

        public void GetButtonData(out string icon, out string verb) {
            icon = IsBiological ? IconId.CKnife : IconId.CCog;
            if (Being != null && IsBiological && Being.IsAlive) {
                verb = T.VerbButcher;
            } else {
                verb = IsBiological ? T.VerbDispose : T.Disassemble;
            }
        }

        public bool TryOrderDispose(bool byPlayer, out string reason) {
            return TryOrderDispose(byPlayer, out reason, false);
        }

        /// <param name="singleExplicit">True when this order comes from a single explicitly
        /// picked entity (the Disassemble button on one selection), as opposed to a bulk/multi
        /// or automatic disposal flow. Only a single explicit order on a damaged, living, player
        /// owned bot may preempt whatever goal it currently has.</param>
        public bool TryOrderDispose(bool byPlayer, out string reason, bool singleExplicit) {
            var check = singleExplicit ? DisposeCheck.SingleOrder : DisposeCheck.BulkOrder;
            if (!CanDisposeNow(check, out reason)) {
                return false;
            }
            var g = CreateGoal(JobTypeIdH.Hauling, iconId, text, this)
                .WithWorldToDesired(AIVarsH.IsDisposed, false, true)
                .ByPlayer(byPlayer)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .WriteInitialState();
            if (Being != null) {
                g.WithFlag(AIGoalFlags.IgnoreDeadBodies);
            }
            if (IsExplicitScrap(check)) {
                // preempt whatever this bot is currently doing - surgery goals top out at 2
                g.WithOverwritePriority(3);
            }
            g.CheckDoable = HasFreeDisposer;
            if (!entity.TrySetGoal(g)) {
                return false;
            }
            return true;
        }

        private AIGoalCheckResult HasFreeDisposer(GameState S, AIGoal goal) {
            if (S.Sys.Slots.HasFreeFor(SlotDesignation)) {
                return AIGoalCheckResult.Doable;
            }
            return AIGoalCheckResult.Fail(DisposerUnavailableMessage(), true);
        }

        public string OutputTip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();

            Def def = null;
            if (Being != null) {
                def = Being.Species.Def;
            } else if (entity is Obj obj) {
                def = obj.Definition;
            }

            if (def == null) {
                return "";
            }

            var craftables = Craftable.FindForDef(def);
            if (craftables == null || craftables.Count == 0) {
                return "";
            }


            var outputs = craftables[0].DisposableOutput;
            if (outputs == null || outputs.Length == 0) {
                outputs = craftables[0].Ingredients;
            }

            if (outputs == null || outputs.Length == 0) {
                return "";
            }

            sb.Append(Texts.SubTitle(T.NounOutput));
            if (Being != null) {
                sb.AppendLine(Texts.Grey(T.MatAmountsMayVary));
            }
            for (int i = 0; i < outputs.Length; i++) {
                var mat = outputs[i];
                if (mat.StackSize > 0 && mat.Type != null) {
                    sb.Append(mat.Type.Def.CodexLink);
                    sb.Append(' ');
                    sb.AppendLine(Units.XNum(mat.StackSize));
                }
            }

            return sb.ToString();
        }
    }
}
