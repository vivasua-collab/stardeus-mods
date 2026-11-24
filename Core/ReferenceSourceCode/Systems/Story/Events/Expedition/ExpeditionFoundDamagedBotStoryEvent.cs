using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class ExpeditionFoundDamagedBotStoryEvent : ExpeditionStoryEvent {
        public const string EventId = "ExpeditionFoundDamagedBot";
        private const string LandingEventDefId = "OnDemand/SpawnDamagedBotOnShuttleLand";
        private const string DataLandingMode = "LandingMode";
        private const string DataSpeciesId = "SpeciesId";
        private const int MaxGenerationAttempts = 20;

        public override string Id => EventId;
        public override string Title => null;

        [SelfInit]
        private static void Register() {
            Register(new ExpeditionFoundDamagedBotStoryEvent());
        }

        public override bool IsAvoidable(StoryEventData data, Expedition exp) {
            return false;
        }

        public override bool IsCancellable(StoryEventData data, Expedition exp) {
            return false;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (!IsLandingMode(data)) { return; }
            if (string.IsNullOrEmpty(data.Data.GetString(DataSpeciesId))) {
                data.FailEvent(ctx.S, "Damaged bot species not defined for shuttle landing");
            }
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            if (!IsLandingMode(data)) {
                base.OnTick(ctx, data);
                return;
            }
            SpawnBotAfterLanding(ctx, data);
        }

        public override void TickExpedition(StoryEventData data, Expedition exp) {
            if (!exp.Context.HadActionRequired) {
                exp.Context.SetRequiredAction(e => ChooseRecoveryAction(data, e));
            }
        }

        private void ChooseRecoveryAction(StoryEventData data, Expedition exp) {
            if (IsShowingChoice(data)) { return; }
            SetShowingChoice(data, true);

            var speciesId = data.Def.Vars.Get(PropertyIdH.SpeciesId, string.Empty);
            var species = Species.Get(speciesId);
            if (species == null) {
                data.FailEvent(exp.S, "Damaged bot species not found: " + speciesId);
                return;
            }

            var failChance = data.Def.Vars.Get(PropertyIdH.FailureChance, 0.1f);
            var rng = new Rng(Hashes.Quick(exp.Seed, Primes.Prime045));
            failChance *= rng.Range(0.9f, 1.1f);
            var chance = 1f - failChance;
            var msg = T.ExpFoundObjStart_V3.F(exp.Shuttle.Name,
                Texts.LinkToCodex(species.Def.IdH, species.Name), Units.Percentage(failChance));

            WPopup.Create(false, IconId.CQuestion, T.ExpeditionFoundObject, msg)
                .WithAction(T.Recover, () => {
                    if (AttemptToRecover(exp, rng, chance)) {
                        if (!StartLandingEvent(data, exp, speciesId)) {
                            exp.Context.ClearRequiredAction();
                            return;
                        }
                    }
                    exp.Context.ClearRequiredAction();
                    data.CreateLogEntry(IconId.COpenPackage, T.ExpeditionFoundObject, msg);
                    data.SetState(exp.S, StoryEventState.Ending);
                }).WithAction2(T.Ignore, () => {
                    exp.Context.ClearRequiredAction();
                    data.HideLogEntry();
                    data.SetState(exp.S, StoryEventState.Ending);
                }).Spawn(true);
        }

        private static bool StartLandingEvent(StoryEventData data, Expedition exp,
                string speciesId) {
            var def = StoryEventDef.Get(LandingEventDefId);
            if (def == null) {
                data.FailEvent(exp.S, "Damaged bot shuttle landing event not found");
                return false;
            }
            var landingData = StoryEventData.CreateFor(def);
            landingData.Seed = Hashes.Quick(data.Seed, Primes.Prime022);
            landingData.TargetTile = exp.Shuttle.Tile;
            var persistentData = landingData.GetOrCreateData();
            persistentData.SetBool(DataLandingMode, true);
            persistentData.SetString(DataSpeciesId, speciesId);
            if (!exp.S.Query.StartStoryEvent.Ask(landingData)) {
                data.FailEvent(exp.S, "Failed starting damaged bot shuttle landing event");
                return false;
            }
            return true;
        }

        private static bool IsLandingMode(StoryEventData data) {
            return data.Data?.GetBool(DataLandingMode, false) == true;
        }

        private static void SpawnBotAfterLanding(StoryContext ctx, StoryEventData data) {
            if (data.TargetTile == null || !data.TargetTile.IsActive) {
                data.FailEvent(ctx.S, "Must have active shuttle set on TargetTile");
                return;
            }
            if (!data.TargetTile.TryGetComponent<ShuttleComp>(out var shuttle)) {
                data.FailEvent(ctx.S, "TargetTile is not a shuttle");
                return;
            }
            if (shuttle.IsOut) { return; }

            var speciesId = data.Data.GetString(DataSpeciesId);
            var species = Species.Get(speciesId);
            if (species == null) {
                data.FailEvent(ctx.S, "Damaged bot species not found: " + speciesId);
                return;
            }
            var beingData = GenerateDamagedBot(ctx.S, species, data.SeededRng);
            if (beingData == null) {
                data.FailEvent(ctx.S, "Failed generating functional damaged bot: " + speciesId);
                return;
            }

            var command = new CmdSpawnBeing(beingData, shuttle.Tile.Transform.WorkSpotOrPos);
            command.Execute(ctx.S);
            var being = command.SpawnedBeing;
            if (being == null) {
                data.FailEvent(ctx.S,
                    $"Failed spawning damaged bot at shuttle position: {shuttle.Entity.Position}");
                return;
            }

            data.TargetBeing = being.Data;
            data.CreateLogEntry(being.Definition.Preview, T.ExpeditionFoundObject,
                Texts.LinkToBeing(being.Data, being.Species.Name));
            BeingUtils.ShowColonyGreetingFor(ctx.S, being);
            BeingUtils.AddInitialMentalBoostFor(being.Data, 3);
            var title = Texts.Green(T.ExpeditionFoundObject);
            ctx.S.Sys.Log.AddLine(shuttle.Tile, title);
            ctx.S.Sig.ShowFanfare.Send(Texts.LinkToTile(shuttle.Tile, title));
            data.SetState(ctx.S, StoryEventState.Ending);
        }

        private static BeingData GenerateDamagedBot(GameState s, Species species, Rng rng) {
            for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++) {
                var vars = new GenBeingVars(species, BeingType.Friendly);
                vars.Body = FactionDef.RandomBodyVars(rng);
                var beingData = s.Query.GenBeingFull.Ask(vars, rng);
                if (beingData == null || !HasIntactCore(beingData)) {
                    continue;
                }
                if (ApplyDamage(beingData, rng)
                        && beingData.Body.IsAlive
                        && !beingData.Body.IsIncapacitated
                        && HasIntactCore(beingData)) {
                    return beingData;
                }
            }
            return null;
        }

        private static bool ApplyDamage(BeingData beingData, Rng rng) {
            var candidates = new List<BodyPartSlot>();
            foreach (var slot in beingData.Body.Slots) {
                if (slot.HoldsMind || slot.IsPartRemoved || slot.InstalledDef == null) {
                    continue;
                }
                candidates.Add(slot);
            }
            var mutationCount = rng.Range(2, 5);
            if (candidates.Count < mutationCount) {
                return false;
            }

            var applied = 0;
            var woundSource = NamedEntity.For(T.Expedition);
            for (int i = 0; i < mutationCount; i++) {
                var slot = rng.From(candidates);
                candidates.Remove(slot);
                if (rng.Chance(0.35f)
                        && !slot.HasAttachedParts
                        && slot.CanRemovePart(false)
                        && slot.TryRemovePart(out _, out _)) {
                    applied++;
                    continue;
                }
                var severity = Severities.Random(Severity.Mild, Severity.Critical, rng);
                var damageType = DamageUtils.RandomCombat(rng);
                if (slot.TryAddWound(woundSource, damageType, severity,
                        G.Ticks - Consts.TicksPerWeek, 1f, out _)) {
                    applied++;
                }
            }
            beingData.Body.CheckCondition(false);
            return applied == mutationCount;
        }

        private static bool HasIntactCore(BeingData beingData) {
            if (!beingData.Body.TryGetSlot(BodyPartSlotIdH.Core, out var core)) {
                return false;
            }
            return !core.IsPartRemoved
                && core.InstalledDef != null
                && core.State != null
                && core.State.WoundSeverity == Severity.Irrelevant;
        }
    }
}
