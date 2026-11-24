using System;
using System.Reflection;
using Game.Research;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using Game.Systems.Quests;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Tools;
using KL.Utils;
namespace Game.Data {
    public static class Registries {
        [ExecuteOnReload]
        private static void CleanupStatics() {
            UnlockEffects.Clear();
            // get the fields of this class, so we'll call .Clear on ALL registries
            var fields = typeof(Registries).GetFields(BindingFlags.Static | BindingFlags.Public);
            if (Arrays.IsEmpty(fields)) {
                D.Err("Cleaning up registries on exit EMPTY: {0}", D.Dump(fields));
                return;
            }
            foreach (var field in fields) {
                object registryInstance = field.GetValue(null);
                var t = field.FieldType;
                t.GetMethod("Clear").Invoke(registryInstance, null);
            }
        }

        /// <summary>
        /// IMPORTANT!!
        /// Implement <see cref="IResettable"/> on your classes to reset them!
        /// </summary>
        public static void CleanupAfterGame() {
            // get the fields of this class, so we'll call .CleanupAfterGame on ALL registries
            var fields = typeof(Registries).GetFields(BindingFlags.Static | BindingFlags.Public);
            if (Arrays.IsEmpty(fields)) {
                D.Err("Cleaning up registries after game: {0}", D.Dump(fields));
            }
            foreach (var field in fields) {
                object registryInstance = field.GetValue(null);
                var t = field.FieldType;

                /// IMPORTANT!!
                /// Implement <see cref="IResettable"/> on your classes to reset them,
                /// CleanupAfterGame is called only on Registry static class!!!!
                var meth = t.GetMethod("CleanupAfterGame");

                if (meth == null) {
                    D.Err("No CleanupAfterGame in {0}", t.Name);
                } else {
                    meth.Invoke(registryInstance, null);
                }
            }
        }

        // All registries:

        public static readonly Registry<IResearchUnlockEffect> UnlockEffects = new();
        public static readonly Registry<BaseQuestStep.Factory> QuestSteps = new();
        public static readonly Registry<QuestReward.Factory> QuestRewards = new();
        public static readonly Registry<QuestVar.Factory> QuestVars = new();
        public static readonly Registry<IQuestValidator> QuestValidators = new();
        public static readonly Registry<AIAction> AIActions = new();
        public static readonly Registry<AttackAction> AttackActions = new();
        public static readonly Registry<AINeed> AINeeds = new();
        public static readonly Registry<Condition> Conditions = new();
        public static readonly Registry<GenBeingDataStep> GenBeingDataSteps = new();
        public static readonly Registry<CombatObjective> CombatObjectives = new();
        public static readonly Registry<GoalPriorityResolver> GoalPriorityResolvers = new();
        public static readonly Registry<FreeSelectHandler> FreeSelectHandlers = new();
        public static readonly Registry<NameSource> NameSources = new();
        public static readonly Registry<SpaceEffect> BiomeModifiers = new();
        public static readonly Registry<NPCShipBehavior> NPCShipBehaviors = new();
        public static readonly Registry<NPCShipReaction> NPCShipReactions = new();
        public static readonly Registry<ShipHailAction> ShipHailActions = new();
        public static readonly Registry<StoryAspect.StoryAspectFactory> StoryAspects = new();
        public static readonly Registry<StoryEvent> StoryEvents = new();
        public static readonly Registry<BeingModifier> BeingModifiers = new();
        public static readonly Registry<BaseWidget> UIWidgets = new();
        public static readonly Registry<BaseEntityAction> EntityActions = new();
        public static readonly Registry<BaseEntityTab> EntityTabs = new();
        public static readonly Registry<BaseEntityWarning> EntityWarnings = new();
        public static readonly Registry<BaseSettingsPage> SettingsPages = new();
        public static readonly Registry<BaseSearchAdapter> SearchAdapters = new();
        public static readonly Registry<BaseSearchCommand> SearchCommands = new();
        public static readonly Registry<ShipComputerApp> ShipComputerApps = new();
        public static readonly Registry<BasePolicyTab> PolicyTabs = new();
        public static readonly Registry<EntityOverviewBlock> EntityOverviewBlocks = new();
        public static readonly Registry<SandboxBlock> SandboxBlocks = new();
    }
}
