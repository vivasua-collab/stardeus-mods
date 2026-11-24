using Game.Constants;
using Game.Utils;
using UnityEngine;
using Game.Systems.BodyParts;
using System.Collections.Generic;
using Game.Systems.Creatures;
using Game.CodeGen;
using Game.Data;

namespace Game.Systems.Story {
    public sealed class BodyPartFailureStoryEvent : StoryEvent {
        public const string EventId = "BodyPartFailure";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BodyPartFailureStoryEvent());
        }

        private readonly Dictionary<BodyPartSlot, float> weightedOrgans = new();

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            weightedOrgans.Clear();
            var isBiological = data.Def.Vars.Get(PropertyIdH.IsBiological, true);
            foreach (var b in ctx.S.Beings.Alive) {
                if (b.Species.IsBiological != isBiological) { continue; }
                foreach (var slot in b.Data.Body.Slots) {
                    if (isBiological && !slot.IsInternal) { continue; }
                    if (slot.AttachedToPart == null) { continue; }
                    if (slot.InstalledDef == null) { continue; }
                    if (slot.Quality < 0.5f) {
                        weightedOrgans.Add(slot, 0.51f - slot.Quality);
                    }
                }
            }
            var rng = data.SeededRng;
            if (!rng.TryWeightedFrom(weightedOrgans, out var target)) {
                data.FailEvent(ctx.S, "No bad organs", needsFlagsCooldown: true,
                    isHardFailure: false);
                return;
            }

            string title;
            string desc;
            DamageType damageType;
            var q = target.Quality;
            float dmg = 1f - q + rng.Range(-0.15f, 0.15f);
            var sev = Severities.For(dmg, q < 0.25f);

            if (target.InstalledDef.IsBiological) {
                title = T.EvtOrganFailureTitle;
                desc = T.EvtOrganFailureDesc_V2;
                damageType = DamageType.Organ;
            } else {
                title = T.EvtComponentFailureTitle;
                desc = T.EvtComponentFailureDesc_V2;
                damageType = DamageType.Energy;
            }

            data.CreateLogEntry(IconId.CHealth, title,
                desc.F(target.Body.Owner.Mind.LinkToShortName,
                    target.InstalledDef.Def.CodexLink));
            data.TargetBeing = target.Body.Owner;
            var text = data.LogEntry.Desc;
            ctx.S.Sig.ShowFanfare.Send(text);
            ctx.S.Sys.Log.AddLine(text);
            target.TryAddOrUpdateWound(NamedEntity.For(title),
                damageType, sev, ctx.Ticks, dmg);
            data.SetState(ctx.S, StoryEventState.Ending);
        }

        protected override void Cleanup() {
            weightedOrgans.Clear();
        }
    }
}
