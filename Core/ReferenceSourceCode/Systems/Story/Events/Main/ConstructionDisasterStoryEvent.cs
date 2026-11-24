using System;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.AI;
using Game.Input;

namespace Game.Systems.Story {
    public sealed class ConstructionDisasterStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new ConstructionDisasterStoryEvent());
        }

        public const string EventId = "ConstructionDisaster";
        public override string Id => EventId;

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            ListenToConstructionEvent(ctx, data);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            ListenToConstructionEvent(ctx, data);
        }

        private void ListenToConstructionEvent(StoryContext ctx, StoryEventData data) {
            if (data.EventListener != null) { return; }
            var listener = ctx.S.Sig.ConstructionCompleted.AddListener(
                (tile) => OnConstructionCompleted(ctx, data, tile));
            data.SetState(ctx.S, StoryEventState.WaitingForHook);
            data.EventListener = listener;
        }

        private bool isConsumed;
        private int attempts;

        private void OnConstructionCompleted(StoryContext ctx, StoryEventData data, Tile tile) {
            if (isConsumed) { return; }
            if (!Ready.Gen) { return; }
            if (tile == null || tile.ENode == null) { return; }
            if (tile.Reinstallable != null) {
                if (tile.Reinstallable.IsPending == true) { return; }
                if (tile.Reinstallable.IsMidRelocation) { return; }
            }
            if (!EntityUtils.IsWithinMargin(tile.PosIdx, false, false)) {
                return;
            }
            try {
                AIAgentComp worker = tile.Constructable.ConstructedBy;
                if (worker == null) {
                    if (tile.Goal != null && !tile.Goal.IsNanobotWorkable) {
                        if (!A.InstantBuild && Ready.Game) {
                            D.Err("Construction completed but worker is not there! {0}. {1}. constr: {2}",
                                tile, tile.Constructable, tile.IsConstructed);
                        }
                        return;
                    }
                } else {
                    int id = JobTypeIdH.Construction;
                    if (worker.Jobs.CanDo(id, out byte level)) {
                        if (level >= 7) {
                            if (attempts++ > 50) {
                                D.Warn("Could not find a candidate for construction disaster in 50 attempts, everyone is too skilled");
                                data.SetState(ctx.S, StoryEventState.Ending);
                                RemoveListener(ctx, data);
                            }
                            return;
                        }
                    } else {
                        D.Err("Construction completed by someone who can't do construction! {0}->{1}",
                            tile, worker);
                    }
                }
                var rng = data.SeededRng;
                var wt = ctx.S.Difficulty.Level * rng.Range(0.5f, 1.5f);

                if (worker != null) {
                    var handSlot = rng.OneOf(BodyPartSlotIdH.Hand_L, BodyPartSlotIdH.Hand_R);
                    var sev = Severities.For(wt / 10f, false);

                    if (worker.BeingData.Body.TryGetSlot(handSlot, out var hand)) {
                        if (!hand.IsPartRemoved) {
                            var source = NamedEntity.For(T.EvConstructFailTitle);
                            if (rng.Chance(1.1f - hand.Quality)) {
                                BeingUtils.DamageBodyPart(ctx.S, worker.BeingData, handSlot, rng,
                                    DamageType.Sharp, sev, source,
                                        subHitChanceMultiplier: 0f);
                            }
                        }
                    }
                }

                isConsumed = true;
                var dmgPct = Mathf.Lerp(0.15f, 0.4f, wt / 10f);
                NotifyFailure(ctx, data, tile, worker?.Being);
                // First deal some damage
                var tileH = tile.Health.MaxHealth;
                tile.Health.TakeDamage(tileH * dmgPct, DamageType.Energy);
                bool canExplode = tile.Flammable.CanExplode &&
                    !ctx.S.Prefs.GetBool(Pref.NoHeavyDisasters,
                        PrefH.NoHeavyDisasters, false);

                var explodeChance = Mathf.Lerp(0f, 0.05f, wt / 15f);
                if (canExplode && Explosions.CanExplode(explodeChance, rng)) {
                    Explosion.Create(ctx.S, tile.Flammable.ExplosionLevel,
                        position: tile.Position,
                        seed: rng.Int);
                } else {
                    var pos = worker == null ? tile.PosIdx : worker.PosIdx;
                    if (ctx.S.Map.O2.At(pos) > 0f) {
                        ctx.S.Sys.Firex.TrySetOnFire(tile, Fire.CreateBalanced(ctx.S, 1.2f));
                        if (worker != null && rng.Chance(ctx.S.Difficulty.FireChanceScale * 0.5f)) {
                            ctx.S.Sys.Firex.TrySetOnFire(worker.Being, Fire.CreateBalanced(ctx.S, 1f));
                        }
                    }
                }
                // End event after disaster
                data.SetState(ctx.S, StoryEventState.Ending);
                RemoveListener(ctx, data);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to execute ConstructionDisaster on {0}", tile);
                data.SetState(ctx.S, StoryEventState.Ending);
                RemoveListener(ctx, data);
            }
        }

        private void RemoveListener(StoryContext ctx, StoryEventData data) {
            if (data.EventListener is KL.Signals.Signal1<Tile>.SignalListener listener) {
                ctx.S.Sig.ConstructionCompleted.RemoveListener(listener);
                data.EventListener = null;
            }
        }

        private void NotifyFailure(StoryContext ctx, StoryEventData data, Tile tile, Being worker) {
            data.TargetTile = tile;
            data.TargetBeing = worker?.Data;
            BuildLogEntry(data, tile);
            var S = tile.S;

            Notification.BuildFor(tile, IconId.CDemolition, data.LogEntry.Title, Consts.TicksPerDay)
                .Group("inc", T.NounIncident, IconId.CWarning)
                .WithOnClick(() => EntityUtils.CameraFocusOn(tile))
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            string title = $"{Texts.LinkToTile(tile)} {GlyphMap.ArrowRight} {T.EvConstructFailTitle}";
            if (worker == null) {
                S.Sys.Log.AddLine($"{Texts.Yellow(T.ObjMaterialsNanobots)}: {title}");
            } else {
                S.Sys.Log.AddLine(worker, title);
            }
            S.Sig.ShowFanfare.Send(title);
        }

        private void BuildLogEntry(StoryEventData data, Tile tile) {
            var evDesc = T.EvConstructFailDesc_V1.F(tile.Definition.CodexLink);
            data.CreateLogEntry(IconId.CDemolition, T.EvConstructFailTitle, evDesc);
        }
    }
}
