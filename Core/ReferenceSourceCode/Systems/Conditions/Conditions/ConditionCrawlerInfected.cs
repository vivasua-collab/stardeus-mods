using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using Game.Utils;
using UnityEngine;
using Game.Systems.Naming;
using Game.Achievements;
using Game.Systems.Story;

namespace Game.Systems.Conditions {
    public sealed class ConditionCrawlerInfected : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionCrawlerInfected());
        }
        public override Priority Importance => Priority.High;
        public override bool ShowOnHoverFor(Being b) {
            return true;
        }

        public const string EggsNumKey = "CrawlerEggs";
        private float hatchDurationTicks;

        public override void OnStart(ConditionState cond, long ticks) {
            // people want to see it
            // cond.Flags |= ConditionFlags.Undetected;
            cond.Flags |= ConditionFlags.TickAfterDeath;
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            CacheHatchDurationTicks();
            var body = cond.Owner.Body;
            // infect affected organs
            if (GetAffectedBodyParts(body, out var p1, out var p2)) {
                Infect(p1);
                Infect(p2);
            }
        }

        private void CacheHatchDurationTicks() {
            hatchDurationTicks = Tunables.CrawlerHatchTimeDays * Consts.TicksPerDay;
            var diff = The.R.State.Difficulty.Level;
            if (diff < 1f) {
                if (diff < 0.1f) {
                    diff = 0.1f;
                }
                hatchDurationTicks /= diff;
            }
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            CacheHatchDurationTicks();
        }

        private int EggsIn(BodyPartSlot part) {
            if (part?.State.Vars?.TryGetInt(EggsNumKey, out var eggs) == true) {
                return eggs;
            }
            return 0;
        }

        private void Infect(BodyPartSlot part) {
            if (part == null) { return; }
            var eggs = Rng.UOneOf(1, 2);
            if (eggs == 1 && The.R.State.Difficulty.Level > 1f) {
                if (Rng.UChance(0.3f)) {
                    eggs++;
                }
            }
            // D.Warn("Infecting {0} with {1} eggs | {2}", part, eggs, part.Body.Owner);
            part.State.Vars ??= new ComponentData();
            part.State.Vars.SetInt(EggsNumKey, eggs);
            part.State.Flags |= BodyPartStateFlags.Infected;
        }

        private bool BurstFrom(BodyPartSlot part, out int count) {
            int eggs;
            count = 0;
            if (part?.State?.Vars?.TryGetInt(EggsNumKey, out eggs) != true) {
                eggs = 0;
            }
            if (eggs <= 0) {
                return false;
            }
            var b = part?.Body?.Owner?.Being;
            if (b != null) {
                if (eggs > 1 && b.S.Difficulty.Level < 0.2f) {
                    eggs = 1;
                }
                count = eggs;
                var pos = b.PosIdx;
                var species = Species.Get(SpeciesIdH.Crawler);
                for (int i = 0; i < eggs; i++) {
                    b.S.CmdQ.Enqueue(new CmdSpawnBeing(new GenBeingVars(species, BeingType.Neutral), pos));
                }
                //b.S.Situation.AddTension(eggs, "Crawlers bursted");
            }
            part.TryAddWound(NamedEntity.For(T.BeingsCrawler01),
                DamageType.Piercing, Severity.Fatal, G.Ticks, 1f, out _, conditionId: IdH);
            return true;
        }

        private bool GetAffectedBodyParts(Body body, out BodyPartSlot p1, out BodyPartSlot p2) {
            p1 = null;
            p2 = null;
            foreach (var slot in body.Slots) {
                if (!slot.IsInternal || slot.IsPartRemoved) { continue; }
                // skip non-organic organs
                if ((slot.InstalledDef.Flags & BodyPartFlags.Biological) == BodyPartFlags.None) {
                    continue;
                }
                switch (slot.Type.IdH) {
                    case BodyPartIdH.Stomach: { p1 = slot; break; }
                    case BodyPartIdH.Intestines: { p2 = slot; break; }
                }
                if (p1 != null && p2 != null) {
                    return true;
                }
            }
            return p1 != null || p2 != null;
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (ticks % Consts.TicksPer5Minutes != 1) { return; }

            if (cond.Owner.HasStateFlag(BeingState.Hidden)) {
                D.Err("Ticking crawler infected while being is hidden!");
                return;
            }

            // Check if we are still infected
            var body = cond.Owner.Body;
            if (!GetAffectedBodyParts(body, out var p1, out var p2)) {
                cond.Owner.RemoveCondition(s, cond.Condition.IdH);
                return;
            }
            int eggs = EggsIn(p1) + EggsIn(p2);
            if (eggs == 0) {
                // healed
                cond.Owner.RemoveCondition(s, cond.Condition.IdH);
                return;
            }

            var dur = ticks - cond.StartTick;
            if (dur > hatchDurationTicks) {
                if (cond.Owner.Being == null) {
                    D.ErrSoft("Won't burst crawlers from being == null! {0}", cond);
                    return;
                }
                var chance = (dur - hatchDurationTicks) / hatchDurationTicks;
                #if !PRODUCTION_BUILD
                // D.Warn("Chance to burst crawler: {0} -> {1}",  chance, cond.Owner);
                #endif
                if (Rng.UChance(chance)) {
                    var b1 = BurstFrom(p1, out var cnt1);
                    var b2 = BurstFrom(p2, out var cnt2);
                    if (b1 || b2) {
                        if (body.TryGetSlot(BodyPartSlotIdH.Body, out var bodySlot)) {
                            Severity sev;
                            if (s.Rng.Chance(0.15f)) {
                                sev = Severity.Critical;
                            } else {
                                sev = Severity.Fatal;
                            }
                            bodySlot.TryAddWound(NamedEntity.For(T.BeingsCrawler01),
                                DamageType.Piercing, sev, ticks, 1f, out _, conditionId: IdH);
                        }
                        AfterBurst(cond.Owner.Being, cnt1 + cnt2);
                    } else {
                        D.Err("Did not burst any crawlers from {0}", cond.Owner);
                    }
                    cond.Owner.RemoveCondition(s, cond.Condition.IdH);
                }
            }
        }

        private void AfterBurst(Being prey, int cnt) {
            var S = prey.S;
            var data = StoryEventData.CreateCustom(IconId.CPreviewCrawler01,
                T.EvtHumanHatchedCrawlersTitle,
                T.EvtHumanHatchedCrawlersDesc_V1.F(Texts.LinkToBody(prey.Data)));
            data.TargetBeing = prey.Data;
            S.Sig.AddCustomStoryEvent.Send(data, true);
            EntityUtils.CameraFocusOn(prey, isStoryEvent: true);
            The.AchievementTracker.Unlock(AchievementHatchCrawlers.AchievementId);
            S.Sys.Log.AddLine(prey, Texts.Red(T.EvtHumanHatchedCrawlersTitle));
            S.Sig.ShowFanfare.Send(Texts.LinkToBody(prey.Data,
                Texts.Red(T.EvtHumanHatchedCrawlersTitle)));
            if (prey.IsDead) {
                prey.Data.Mind.OnDeath(T.DeathCrawlersBurst_V1.F(cnt), G.Ticks);
            }
        }

        private ConditionCrawlerInfected() : base(ConditionId.CrawlerInfected,
            GraphicsId.BeingsHatsHumanCrawlerFacehugCrawlerFacehug) {}
    }
}
