using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Systems.Trade;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Spawns a delivery quest passenger out of the colony's trade portal. The passenger is a
    /// non-hostile guest of the quest provider's faction (a random non-hostile faction if the
    /// provider has none), of a species chosen from the configurable SpeciesIds list (default
    /// Human). Fails instantly if there is no active trade portal (a safety net behind the
    /// HasActiveTradePortal validator). Stores the passenger's mind id on the quest so the
    /// watchdog and delivery steps can find them.
    /// </summary>
    public sealed class SpawnPassengerQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new SpawnPassengerQuestStep(s, c, l));
        }

        public const string CheckId = "SpawnPassenger";

        public SpawnPassengerQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after != QuestStepState.Active) { return; }
            SpawnPassenger();
        }

        private void SpawnPassenger() {
            var portal = TradeUI.FindTradePortal(S);
            if (portal == null) {
                // Safety net: the HasActiveTradePortal validator should prevent this, but the
                // portal may have been destroyed between offer and acceptance.
                D.Warn("No active trade portal to spawn quest passenger: {0}", step);
                S.Sig.ShowErrorMessage.Send($"{step.Data.Quest.Data.Title}: {T.TradePortalNotAvailable}");
                FailStep();
                return;
            }

            var rng = step.SeededRng;
            var faction = PickFaction(rng);
            var species = PickSpecies(rng);

            var spawnPoint = portal.Tile.Transform.OutPort;
            var bVars = new GenBeingVars(species, BeingType.Friendly);
            bVars.Faction = faction;
            bVars.Body = FactionDef.RandomBodyVars(rng);
            var bData = BeingUtils.GenBeingWithInventory(bVars, S.Rng,
                GenItemFlags.Casual | GenItemFlags.SpaceReady);

            var cmd = new CmdSpawnBeing(bData, spawnPoint);
            cmd.Execute(S);
            var being = cmd.SpawnedBeing;
            if (being == null) {
                D.Err("Failed to spawn quest passenger at trade portal: {0}", step);
                FailStep();
                return;
            }

            // Mark as guest BEFORE SetFaction: SetFaction -> AfterSetFaction -> Name.CheckState(),
            // which colors the name by IsGuest (Mind.Role == Passenger). Setting the role after
            // would leave the name painted red because Role has no change notification.
            being.Data.Mind.Role = ColonyRole.Passenger;
            being.Data.Mind.SetFaction(faction);
            BeingUtils.ShowColonyGreetingFor(S, being);

            // Seed initial relationships with nearby crew so the guest can socialise.
            // var bd = Caches.BeingDataList;
            // bd.Clear();
            // bd.Add(being.Data);
            // foreach (var other in S.Beings.Alive) {
            //     if (other.Data.Id == being.Data.Id) { continue; }
            //     if (other.Data.IsPlayer && other.Data.Species.IsHumanoid) {
            //         bd.Add(other.Data);
            //     }
            // }
            // S.Query.GenerateRelationshipsFor.Ask(bd);

            step.Data.GetOrCreateData().SetInt(
                PassengerUtils.PassengerMindIdKey, (int) being.Data.Mind.Id);
            CompleteStep();
        }

        private FactionDef PickFaction(Rng rng) {
            var provider = step.Data.Provider;
            var pf = provider?.Faction;
            if (pf != null && !pf.IsBlank && !pf.IsInternal && !pf.IsPlayer) {
                return pf;
            }
            // Provider has no usable faction: pick a random non-hostile, non-internal one.
            var pool = Caches.FactionList;
            pool.Clear();
            foreach (var f in FactionDef.AllOrdered) {
                if (f.IsPlayer || f.IsBlank || f.IsAntiPlayer) { continue; }
                if (f.Friendliness < 0f) { continue; }
                pool.Add(f);
            }
            if (pool.Count == 0) {
                // Fallback: any non-internal faction.
                foreach (var f in FactionDef.AllOrdered) {
                    if (f.IsPlayer || f.IsBlank) { continue; }
                    pool.Add(f);
                }
            }
            return rng.From(pool) ?? FactionDef.Blank;
        }

        private Species PickSpecies(Rng rng) {
            var ids = config.GetStringSet(PropertyIdH.SpeciesIds, null);
            var pool = new List<Species>();
            if (ids != null) {
                foreach (var id in ids) {
                    var sp = Species.Get(id);
                    if (sp != null) { pool.Add(sp); }
                }
            }
            if (pool.Count == 0) {
                return Species.Get(SpeciesIdH.Human);
            }
            return rng.From(pool);
        }
    }
}
