using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class DerelictShipFirstStoryStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipFirstStory";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipFirstStoryStoryEvent());
        }
        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ObjGenStage(new List<Def> {
                The.Defs.FastGet(DefIdsH.ObjContainersQuestPackage),
                The.Defs.FastGet(DefIdsH.ObjMiscResonanceCharge)
            }));
            factory.AddStage(new SlaughterSpotGenStage());
        }

        public override void AfterGenerate(GameState s, ShipFactory factory, Rng rng) {
            var diff = s.Difficulty.Level;
            int spawnedCrawlers = 0;
            int wantedCrawlers = Mathf.CeilToInt(s.Difficulty.Level * 3f);
            if (wantedCrawlers < 2) {
                wantedCrawlers = 2;
            }

            var crawler = Species.Get(SpeciesIdH.Crawler);

            factory.ConfinementArea.ForEach((t) => {
                if (spawnedCrawlers < wantedCrawlers) {
                    if (EntityUtils.IsEmptyFloorWithIntegrityAt(t)) {
                        if (factory.Spec.IsWithinGenArea(t)) {
                            if (rng.Chance(0.05f * diff)) {
                                s.CmdQ.Enqueue(new CmdSpawnBeing(
                                    new GenBeingVars(crawler, BeingType.Neutral), t));
                                spawnedCrawlers++;
                            }
                        }
                    }
                }
            });

            SpawnDamagedBotFightingCrawlers(s, factory, rng, crawler);
        }

        private static void SpawnDamagedBotFightingCrawlers(GameState s,
                ShipFactory factory, Rng rng, Species crawler) {
            // Pick a valid interior tile as the anchor
            var positions = new List<int>();
            factory.Spec.Bounds.ForEachExpanded((p) => {
                if (EntityUtils.IsEmptyFloorWithIntegrityAt(p)) { positions.Add(p); }
            }, 1);
            if (positions.Count == 0) {
                D.Warn("DerelictShipFirstStory: no valid floor tile for carrier spawn");
                return;
            }
            var anchor = rng.From(positions);

            // Spawn the friendly Drone or Carrier (it will join the player colony on contact)
            var botCmd = new CmdSpawnBeing(
                new GenBeingVars(Species.Get(
                    rng.OneOf(SpeciesIdH.Drone, SpeciesIdH.Carrier, SpeciesIdH.Robot)),
                    BeingType.Friendly),
                EntityUtils.SpawnableAround(anchor, 3f, false));
            botCmd.Execute(s);
            var bot = botCmd.SpawnedBeing;
            if (bot == null) {
                D.Warn("DerelictShipFirstStory: failed to spawn bot");
                return;
            }

            BeingUtils.ShowColonyGreetingFor(s, bot);

            // The carrier has been in a fight — apply a couple of light wounds
            ApplyLightCombatDamage(bot.Data, rng);

            if (!A.IsCombatOn) { return; }

            // Spawn 2-4 crawlers right around the carrier for the ongoing fight
            Being combatTarget = null;
            int wantedFightingCrawlers = rng.Range(2, 5);
            for (int i = 0; i < wantedFightingCrawlers; i++) {
                var crawlerCmd = new CmdSpawnBeing(
                    new GenBeingVars(crawler, BeingType.Neutral),
                    EntityUtils.SpawnableAround(bot.PosIdx, 3f, false));
                crawlerCmd.Execute(s);
                combatTarget ??= crawlerCmd.SpawnedBeing;
            }

            // Start the fight — StartCombatBetween pulls the whole Crawler army in
            if (combatTarget == null) { return; }
            if (s.Query.StartCombatBetween.Ask(
                    bot.AIAgent.CombatUnit, combatTarget.AIAgent.CombatUnit)) {
                bot.AIAgent.CombatUnit.SetAttackTarget(
                    combatTarget.AIAgent.CombatUnit, true);
            }
        }

        private static void ApplyLightCombatDamage(BeingData data, Rng rng) {
            // 1-2 mild/moderate wounds on non-core slots; carrier stays alive and functional
            if (!data.Body.TryGetSlot(BodyPartSlotIdH.Core, out var coreSlot)) { return; }
            var candidates = new List<BodyPartSlot>();
            foreach (var slot in data.Body.Slots) {
                if (slot.HoldsMind || slot.IsPartRemoved || slot.InstalledDef == null) {
                    continue;
                }
                if (slot == coreSlot) { continue; }
                candidates.Add(slot);
            }
            if (candidates.Count == 0) { return; }
            var woundSource = NamedEntity.For(T.BeingsCrawler01);
            var woundCount = rng.Range(1, 3);
            for (int i = 0; i < woundCount && candidates.Count > 0; i++) {
                var slot = rng.From(candidates);
                candidates.Remove(slot);
                var severity = Severities.Random(Severity.Mild, Severity.Moderate, rng);
                slot.TryAddWound(woundSource, DamageUtils.RandomCombat(rng), severity,
                    G.Ticks - Consts.TicksPer4Hours, 1f, out _);
            }
            data.Body.CheckCondition(false);
        }
    }
}
