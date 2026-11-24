using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Data;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using Game.Procgen.Factions;
using KL.Utils;
using Game.Constants;
using UnityEngine;
using System;
using Game.Mods;
using Game.Modifiers;
using Game.Platform.Presence;
using Game.Systems.Creatures;
using KL.Randomness;
using Game.Systems.UIUX;

namespace Game.Scenarios {
    public abstract class Scenario {
        public abstract string Id { get;}
        public GameState S;
        public bool IsGenFailure;
        public virtual bool IsSandbox => false;
        public virtual bool IsInternal => false;
        // The calm rich presence line friends see while nothing dramatic happens
        // (PresenceActivity). An id it does not list shows the generic line.
        public virtual string PresenceId => PresenceActivity.Ship;
        public static Scenario DefaultScenario;
        private static readonly Dictionary<string, Scenario> Scenarios = new();
        public static Scenario Random(Rng rng) {
            var scenario = rng.From(Scenarios.Values);
            if (scenario is ExistingShipScenario || scenario.IsSandbox) {
                return DefaultScenario;
            }
            return scenario;
        }
        public abstract IEnumerator GeneratePreview(Texture2D tex, GameSetupData data);
        public static List<Scenario> PublicScenarios =>
            Scenarios.Values.Where(s => !s.IsInternal).OrderBy(s => s.Priority).ToList();
        public abstract string NameT { get; }
        public abstract string DescT { get;}
        public abstract int Priority { get; }
        public virtual int FiresToIgnore => 0;
        public virtual double StartingStarcreds => 1000;
        public virtual bool IsUnfinishedFeature => false;
        public virtual string[] InitialFavMats => null;
        /// <summary>Whether the destruction level slider applies to this scenario.</summary>
        public virtual bool UsesDestructionLevel => false;
        // The slider's starting point, before the player moves it
        public virtual float DefaultDestructionLevel(Difficulty difficulty) {
            return difficulty?.DefaultDestructionLevel ?? 0.75f;
        }
        /// <summary>Hand-picked ship seeds Quick Start rolls from. Null = random seed.</summary>
        public virtual string[] QuickStartSeeds => null;
        // An instant-start scenario (Quick Start, Sandbox) skips the setup screen where
        // difficulty is normally chosen, so the scenario itself has to name one.
        public virtual string QuickStartDifficultyId => Difficulty.QuickStartId;
        /// <summary>StoryGenDef id preselected in game setup. Null = StoryGenDef.Default.</summary>
        public virtual string DefaultStoryGenId => null;
        // The intro sends the player to the one derelict next to them. Anything else in the
        // starting sector - another derelict, a station, a port - reads as the quest target.
        public virtual bool StartsAlone => false;
        // A crew that cannot fly still needs someone to clean solar panels: one who can clean
        // starts in a Jet Suit (StartingJetSuit), and the crew setup counts them as that flyer
        public virtual bool GivesCleanerJetSuit => false;
        // Weapon defs the starting crew holds from the first tick (StartingCrewWeapons), each
        // in its own hands. The Weapon slot policy defaults to Manual, so a weapon left on the
        // floor stays there until the player equips it, and a new player met the first raid
        // unarmed. Whole-crew rolls keep someone able to hold each (StartingCrewJobs).
        public virtual string[] StartingWeapons => null;
        // Basic Tech preselected in game setup and used by Quick Start. A scenario that
        // locks it opens the basics it still wants through its own *_UnlockedResearch list.
        public virtual ModifierBasicTech.Strategy DefaultBasicTech =>
            ModifierBasicTech.Strategy.Unlocked;
        public ComponentData Data;
        public IEnumerator Setup(GameState state, GameSetupData config) {
            S = state;
            IsGenFailure = false;
            Data = new ComponentData(0, Id);
            state.Scenario = this;
            state.Wallet.Add(StartingStarcreds);
            yield return DoSetup(state, config);
            var favMats = InitialFavMats;
            if (!Arrays.IsEmpty(favMats)) {
                foreach (var m in favMats) {
                    var matType = MatType.Get(m);
                    if (matType != null) {
                        S.Sig.SetFavMat.Send(matType, true, 0);
                    } else {
                        var err = $"Invalid mat type id in InitialFavMats for scenario: {Id}";
                        D.Err(err);
                        ModRegistry.WarnAboutBrokenMod(this, err);
                    }
                }
            }
        }

        public static void Unload() {
            foreach (var s in Scenarios) {
                s.Value.UnloadOne();
            }
        }

        private void UnloadOne() {
            Data = null;
            IsGenFailure = false;
            S = null;
        }

        public virtual void AfterLoad(GameState s) {}
        public abstract IEnumerator DoSetup(GameState state, GameSetupData config);
        public abstract (Species species, int count)[] GetStartingBeingCounts();
        public abstract int StartingPetCount { get; }
        // One entry per stasis array the scenario builds at the start, in build order. The
        // Custom Start screen rolls these people up front; each must be the same instance the
        // scenario passes to its ScenarioStasisAreaGenStage. Null = no stasis array.
        public virtual StasisManifest[] StasisManifests => null;

        protected static void Register(Scenario scenario) {
            Scenarios[scenario.Id] = scenario;
        }

        public static void Flush() {
            if (DefaultScenario != null) {
                DefaultScenario.S = null;
            }
            foreach (var s in Scenarios) {
                s.Value.S = null;
            }
        }

        public static Scenario Find(string id) {
            if (id == null) {
                return DefaultScenario;
            }
            // for legacy saves
            id = id.Replace("scenario.", "");
            if (Scenarios.TryGetValue(id, out var sc)) {
                return sc;
            }
            D.Warn("Couldn't find scenario by id: {0}. Returning default", id);
            return DefaultScenario;
        }
    }
}
