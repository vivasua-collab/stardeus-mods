using System;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectCombatHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectCombat";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectCombatHandler());
        }

        public override int Order => 6;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
        }


        public override bool AddSelectedBeingRightClickContext(int playerFaction,
                int canDirectControl, int underDirectControl, QuickChoice choice) {
            if (!ctx.HoveringAssignableAttackTarget) {
                return false;
            }
            // we will order by just right clicking
            if (underDirectControl > 0) { return false; }
            // can't order
            if (canDirectControl == 0) { return false; }

            var target = ctx.HoveredBeing;
            if (target == null) {
                return false;
            }
            // var avatar = S.Query.GetBeingAvatar.Ask(target.Data);
            // Closed by hand: a confirm replaces this menu, and closing it would close the
            // confirm
            var btn = new QuickChoice.Button() {
                AutoClose = false,
                OnClick = () => {
                    var selected = new List<Being>(ctx.SelectedBeings);
                    void Order() {
                        bool worked = false;
                        foreach (var b in selected) {
                            if (SetAttackTarget(ctx, target.AIAgent.CombatUnit, b.AIAgent.CombatUnit)) {
                                worked = true;
                            }
                        }
                        if (!worked) {
                            UISounds.PlayActionDenied();
                        }
                    }
                    if (!ConfirmGuestBetrayal(ctx, target, Order)) {
                        Order();
                        choice.Close();
                    }
                },
                TipFunc = () => {
                    ctx.Tooltip.Clear();
                    ctx.Tooltip.AppendFormat(Texts.Accented(target.Data.Name));
                    AppendUnitData(ctx.Tooltip, target.AIAgent.CombatUnit);
                    return ctx.Tooltip.ToString();
                },
            };
            choice.AddButton(T.CombatObjectiveAttack, btn);

            // res.Add(UDB.Create(prov, UDBT.DTextBtn, null, BeingUtils.DecoratedBeingName(target.Data, true))
            //     .WithSprite(avatar.Sprite)
            //     .WithText2(T.CombatObjectiveAttack)
            //     .WithTooltipFunction((b) => {
            //         ctx.Tooltip.Clear();
            //         ctx.Tooltip.AppendFormat(Texts.Accented(target.Data.Name));
            //         AppendUnitData(ctx.Tooltip, target.AIAgent.CombatUnit);
            //         return ctx.Tooltip.ToString();
            //     })
            //     .WithClickFunction(() => {
            //         bool worked = false;
            //         foreach (var b in ctx.SelectedBeings) {
            //             if (SetAttackTarget(ctx, target.AIAgent.CombatUnit, b.AIAgent.CombatUnit)) {
            //                 worked = true;
            //             }
            //         }
            //         if (!worked) {
            //             UISounds.PlayActionDenied();
            //         } else {
            //
            //         }
            //     }));

            return true;
        }

        // Attacking a calm guest turns it and its people aboard hostile for good and costs
        // standing with its faction, so the order waits for a confirm. False when no confirm
        // is needed and the caller orders at once.
        public static bool ConfirmGuestBetrayal(FreeSelectContext ctx, Being target, Action order) {
            if (!CombatSys.IsGuestBetrayal(target.Data)) { return false; }
            ShowAttackConfirm(ctx, target, order);
            return true;
        }

        // One of our own (the Asimov override, a friendly fight, a breakdown, a Crawler host):
        // a drafted right click on them was an attack at once, so a misclick shot our own bot
        // (STA-1928). The order waits for a confirm, like a guest's. Enemies are never asked.
        public static bool ConfirmFriendlyAttack(FreeSelectContext ctx, Being target, Action order) {
            var by = ctx.TargetBeing ?? (ctx.SelectedBeings.Count > 0 ? ctx.SelectedBeings[0] : null);
            if (by == null || by.Data.EffectiveFaction != target.Data.EffectiveFaction) {
                return false;
            }
            ShowAttackConfirm(ctx, target, order);
            return true;
        }

        private static void ShowAttackConfirm(FreeSelectContext ctx, Being target, Action order) {
            var fac = target.Data.Mind?.Faction;
            var arrow = Texts.Accented(GlyphMap.ArrowRight);
            var qc = QuickChoice.Create(fac != null
                ? $"{T.CombatObjectiveAttack} {arrow} {fac.Glyph} {target.Data.Name}"
                : $"{T.CombatObjectiveAttack} {arrow} {target.Data.Name}");
            qc.AddButton(T.Confirm, () => {
                if (!target.IsActive || !target.IsAlive) {
                    UISounds.PlayActionDenied();
                    return;
                }
                order();
            });
            ctx.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public static bool SetAttackTarget(FreeSelectContext ctx, CombatUnit target, CombatUnit cu,
                CombatOrders.Kind kind = CombatOrders.Kind.Approach) {
            var S = ctx.S;
            if (!A.IsCombatOn) {
                var msg = Texts.Red($"{T.EnableCombat}: {T.Off}");
                if (MouseUtils.IsOverUI) {
                    The.SysSig.ShowTooltip.Send(msg, true);
                } else {
                    WPopup.Create(false, IconId.CAttackTarget, T.OperationForbidden, msg).Spawn();
                }
                return false;
            }
            if (!S.Sys.Combat.IsHostileCombatInProgress) {
                if (!S.Query.StartCombatBetween.Ask(cu, target)) {
                    return false;
                }
            } else {
                // a guest sits outside every army, or with the crew, until it is turned
                if (CombatSys.IsGuestBetrayal(target.Data)) {
                    S.Sys.Combat.TurnGuestHostile(target);
                } else if (target.Data.IsProtectedGuest) {
                    S.Sys.Combat.TurnAgainstPlayer(target);
                }
                cu.SetAttackTarget(target, true);
                BeingUtils.PlaySelectSoundFor(ctx.TargetBeing);
            }
            CombatOrders.Apply(cu, kind);
            cu.Agent.Being.AIAgent.DirectControl.RecordDirectOrderActivity();
            return true;
        }

        public override void OnHover(Vector2 pos) {
            ctx.HoveringAssignableAttackTarget = false;
            if (!A.IsCombatOn) { return; }
            // if (!S.Sys.Combat.IsHostileCombatInProgress) { return; }

            // 2. add data about selected unit first
            StringBuilder sb = ctx.Tooltip;
            CombatUnit selUnit = ctx.TargetBeing?.AIAgent.CombatUnit;
            // if (selUnit != null && selUnit.Army != null) {
            //     // continue without a new header
            //     AppendUnitData(sb, selUnit);
            // }

            // 1. hovered being tooltip
            CombatUnit hovUnit = ctx.HoveredBeing?.AIAgent.CombatUnit;


            // show the cover of this position
            if (ctx.IsCtrlPressed && ctx.TargetBeingIsControllable
                    && ctx.TargetBeing.AIAgent.DirectControl.IsUnderDirectControl) {
                var cover = S.Query.CoverSlotAt.Ask(ctx.HoveredPos);
                sb = ctx.BeginTooltipBlock();
                cover.AppendDescription(sb, false, ctx.HoveredPos);
            }

            if (S.Sys.Combat.IsHostileCombatInProgress) {
                // a neutral guest has no army, but can still be restrained
                if (hovUnit == null || (hovUnit.Army == null && !hovUnit.Data.IsGuest)) {
                    return;
                }
            }

            if (hovUnit == selUnit) { return; }
            if (hovUnit == null) { return; }
            if (selUnit == null) { return; }
            if (!selUnit.Agent.DirectControl.IsControllable) { return; }
            if (hovUnit.Data.HasStateFlag(BeingState.Dead) || hovUnit.Data.Body.IsIncapacitated) {
                return;
            }

            bool canSelUnitAttack = selUnit.Data.Being.IsAlive && !selUnit.Data.Body.IsIncapacitated;
            if (canSelUnitAttack) {
                bool isHovUnitAttackable = hovUnit.Data.Being.IsAlive
                        && !hovUnit.Data.Body.IsIncapacitated
                        && !hovUnit.Data.HasStateFlag(BeingState.InDevice);
                if (isHovUnitAttackable) {
                    bool isDifferentfaction = selUnit.Data.EffectiveFaction != hovUnit.Data.EffectiveFaction;
                    // A guest is attackable at any time: a calm one turns hostile for good
                    // (CombatSys.IsGuestBetrayal), one out of its mind is restrained
                    if (isDifferentfaction) {
                        ctx.HoveringAssignableAttackTarget = true;
                    } else {
                        bool isHovUnitSameFactionAttackable = A.HasAsimovOverride
                                // unit in a friendly fight
                                || (hovUnit.Army?.Flags == CombatFlags.Friendly)
                                || hovUnit.Data.HasCondition(ConditionIdH.CrawlerAttached)
                                || hovUnit.Data.HasCondition(ConditionIdH.MentalBreakdown);
                        if (isHovUnitSameFactionAttackable) {
                            // fuck em up lol
                            ctx.HoveringAssignableAttackTarget = true;
                        }
                    }
                }
            }

            if (ctx.TargetBeingIsUnderDirectControl && ctx.HoveringAssignableAttackTarget) {
                ctx.WantedCursor = CursorTypeH.CursorAttack;
            }
            //if (hovUnit.Agent.BeingData.IsPlayer) {
            if (!S.Sys.Combat.IsHostileCombatInProgress) {
                AppendShotReport(selUnit, hovUnit, true);
                return;
            }
            //}

            sb = ctx.BeginTooltipBlock();
            var isTarget = ctx.HoveringAssignableAttackTarget;
            if (isTarget) {
                ShotTooltip.AppendHeader(sb, hovUnit);
            } else {
                sb.Append(Texts.Accented(hovUnit.Agent.BeingData.Name));
            }
            // the subtitle ends its own line
            AppendUnitData(sb, hovUnit, atLineStart: isTarget);
            AppendShotReport(selUnit, hovUnit, false);
        }

        // Only for one selected shooter: with several, whose shot would it be
        private void AppendShotReport(CombatUnit shooter, CombatUnit target, bool newBlock) {
            if (!ctx.HoveringAssignableAttackTarget || ctx.SelectedBeings.Count != 1) {
                return;
            }
            ShotTooltip.Append(ctx, shooter, target, newBlock);
        }

        private void AppendUnitData(StringBuilder sb, CombatUnit selUnit, bool atLineStart = false) {
            sb.AppendFormat(atLineStart ? " {0} {1}" : "\n {0} {1}", GlyphMap.ArrowRight,
                selUnit.StateName);
            if (S.Sys.Combat.IsHostileCombatInProgress) {
                #if DEBUG_COVER
                sb.AppendFormat("\n * Updated: {0}",
                    Units.TicksAgo((int) (G.Ticks - selUnit.LastTick)));
                sb.AppendFormat("\n * Pos: {0}", Pos.String(selUnit.PosIdx));
                sb.AppendFormat("\n * Cover: {0} ", D.DumpInstance(selUnit.CurrentCover));
                #endif
                // selUnit.CurrentCover.AppendDescription(sb, true, selUnit.PosIdx);
                if (selUnit.Data.IsPlayer && selUnit.AttackTarget != null) {
                    sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.NounAttackTarget,
                        selUnit.AttackTarget.Name);
                }
            }
        }
    }
}
