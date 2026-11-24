using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Motion;
using Game.Systems.Naming;
using Game.Systems.Stats;
using Game.Systems.Traits;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Creatures {
    [MessagePackObject]
    public class BeingDataData {
        [Key("Id")] public uint Id;
        [Key("State")] public byte StateLegacy;
        [Key("StateFlags")] public BeingState StateFlags;
        [Key("Species")] public string SpeciesType;
        [Key("Body")] public BodyData Body;
        [Key("InventoryData")] public InventoryData Inventory;
        /// <summary>
        /// LEGACY! Only for migrating old saves, do not write
        /// </summary>
        [Key("Mind")] public MindData LegacyMind;
        [Key("MindId")] public uint MindId;
        [Key("FactionId")] public string FactionId;
        [Key("EnvFlags")] public BeingEnvFlags EnvFlags;
        [Key("BeingId")] public int BeingId;
        [Key("GroupId")] public int GroupId;
        [Key("HideAvatarButton")] public bool HideAvatarButton;
        [Key("LogGoalChanges")] public bool LogGoalChanges;
        [Key("LogIdleGoals")] public bool LogIdleGoals;
        [Key("GoalChanges")] public List<string> GoalChanges;
        [Key("InStasis")] public long InStasisFromTick;
        [Key("StatsBeing")] public StatsContainerData StatsBeing;
        [Key("Conditions")] public ConditionStateData[] Conditions;
        [Key("ConditionProgress")] public Dictionary<int, float> ConditionProgress;
        [Key("Looks")] public BeingLooks Looks;
        [Key("Counters")] public Counters Counters;
        [Key("Position")] public Position Position;
        [Key("StorageId")] public int StorageId;
        [Key("Dockable")] public DockableConfig Dockable;


        public static BeingData DeepCopy(GameState S, BeingData data) {
            // bool copyInitialized = data.StateFlags != BeingState.Uninitialized;
            // Detach inventory
            var inventory = data.Inventory;
            data.Inventory = null;
            MindData mindData = MindData.Serialize(data.Mind);
            if (mindData.Id != 0) {
                D.Warn("Resetting mind data id for deep copy of {0}", data);
                mindData.Id = 0;
            }
            var datadata = Serialize(S, data,
                expectRegisteredMind: false);
            var serialized = MessagePackSerializer.Serialize(datadata,
                MessagePackSerializerOptions.Standard);
            var serializedMind = MessagePackSerializer.Serialize(mindData,
                MessagePackSerializerOptions.Standard);
            // Reattach inventory
            data.Inventory = inventory;
            var datadata2 = MessagePackSerializer.Deserialize<BeingDataData>(
                serialized, MessagePackSerializerOptions.Standard);
            var mindData2 = MessagePackSerializer.Deserialize<MindData>(
                serializedMind, MessagePackSerializerOptions.Standard);
            if (TryDeserialize(S, datadata2, null, out var copy, data.Id != 0)) {
                // always have to copy the inventory, because if we don't some stats will remain forever
                copy.Inventory = InventoryData.DeepCopy(S, inventory, copy);
                if (copy.Inventory != null) {
                    foreach (var item in copy.Inventory.Equipment) {
                        if (item.Value.Item != null) {
                            item.Value.ApplyItemEffects();
                        }
                    }
                    copy.Inventory.Update();
                }

                // D.Warn("Copying inventory: {0}", copy.Inventory);
                copy.Id = 0;
                copy.StateFlags = BeingState.Uninitialized;
                var mind2 = MindData.Deserialize(mindData2);
                mind2.SetOwner(copy);

                return copy;
            }
            D.Err("Failed making a deep copy of being! If there is more than one gen attempt, saving would be broken! {0}", data);
            return null;
        }

        public static BeingDataData[] Serialize(GameState s,
                Dictionary<uint, BeingData> all) {
            // D.Warn("Serializing {0} being data", all.Count);
            var d = new BeingDataData[all.Count];
            int i = 0;
            foreach (var data in all) {
                d[i++] = Serialize(s, data.Value, expectRegisteredMind: true);
            }
            return d;
        }

        public static BeingDataData Serialize(GameState s, BeingData data,
                bool expectRegisteredMind) {
            if (data == null) {
                D.Err("Trying to serialize null data!");
                return default;
            }
            try {
                var d = new BeingDataData();
                d.Id = data.Id;
                d.SpeciesType = data.Species.Id;
                d.StateFlags = data.StateFlags;
                d.EnvFlags = data.EnvFlags;
                d.InStasisFromTick = data.InStasisFromTick;
                d.StorageId = data.StorageId;
                d.Dockable = data.Dockable;
                d.Counters = data.Counters;
                d.GroupId = data.GroupId;
                d.HideAvatarButton = data.HideAvatarButton;
                d.LogGoalChanges = data.LogGoalChanges;
                d.LogIdleGoals = data.LogIdleGoals;
                d.GoalChanges = data.GoalChanges;
                if (data.Being != null) {
                    d.BeingId = data.Being.Id;
                    data.LastPosition = data.Being.Motion.Position;
                    d.Position = data.LastPosition;
                    // D.Err("saving pos: {0}", d.Position);
                }
                if (data.Mind != null && data.Mind.Id == 0) {
                    if (expectRegisteredMind && !data.Mind.IsBlank) {
                        D.Err("Non-blank mind was not registered! {0} | {1}",
                            data, data.Mind);
                    }
                    s.Query.RegisterMind.Ask(data.Mind);
                }
                d.MindId = data.Mind?.Id ?? 0;
                d.Body = BodyData.Serialize(data.Body);
                d.ConditionProgress = data.ConditionProgress;
                d.Inventory = InventoryData.Serialize(data.Inventory);
                d.Looks = data.Looks;
                d.StatsBeing = StatsContainerData.Serialize(data.StatsBeing);
                d.Conditions = ConditionStateData.Serialize(data.Conditions);
                return d;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed serializing being data: {0}", data);
                return default;
            }
        }

        public static bool TryDeserialize(GameState S, BeingDataData data,
                Being b, out BeingData d, bool expectInitialized = true) {
            if (expectInitialized) {
                if (data.Id == 0) {
                    D.Err("Trying to deserialize initialized being with id 0! {0} -> {1}", data, b);
                    d = null;
                    return false;
                }
            } else {
                if (data.Id != 0) {
                    D.Err("Trying to deserialize uninitialized being with id != 0! {0} -> {1}", data, b);
                    d = null;
                    return false;
                }
            }
            // D.Warn("Deserializing: {0}", data.Mind);
            d = new BeingData();
            d.EnvFlags = data.EnvFlags;
            d.Species = Species.Get(data.SpeciesType);
            d.Id = data.Id;
            d.GroupId = data.GroupId;
            d.HideAvatarButton = data.HideAvatarButton;
            d.LogGoalChanges = data.LogGoalChanges;
            d.LogIdleGoals = data.LogIdleGoals;
            d.GoalChanges = data.GoalChanges;
            if (d.LogGoalChanges) { Game.Systems.UIUX.GoalDebugTab.Enabled = true; }
            d.StorageId = data.StorageId;
            d.StateFlags = data.StateFlags;
            d.Counters = data.Counters ?? new Counters();
            d.Counters.Initialize(d);
            #if !PRODUCTION_BUILD
            MigrateLegacyStateFlags(data, d);
            #endif
            if (d.Species == null) {
                D.Err("Failed to load species for being data: {0}", D.DumpInstance(data));
                return false;
            }

            d.Species.LooksDef.InitializeFor(d);
            var savedLooks = data.Looks;
            // Pre-LookPartTypeDef saves have no TypeId on any part, so they can't be matched
            // against current part defs at all. Queue a full regeneration once the body (which
            // Generate needs) is available below, instead of trying to recover per-part data.
            var needsLegacyLooksRegen = savedLooks.IsLegacy();
            // Types present in the save, so the generate-missing pass below only fills in parts
            // that genuinely weren't in the save (e.g. a part type added by a mod since), not
            // ones that were saved with a legitimately null sprite (e.g. bald hair).
            HashSet<int> loadedLookTypeHashes = null;
            if (!needsLegacyLooksRegen) {
                savedLooks.ResolvePostLoad();
                loadedLookTypeHashes = new HashSet<int>();
                if (savedLooks.Parts != null) {
                    foreach (var lookPart in savedLooks.Parts) {
                        d.Looks.LoadPart(lookPart, b);
                        if (lookPart.Type != null) {
                            loadedLookTypeHashes.Add(lookPart.Type.IdH);
                        }
                    }
                }
                d.Looks.BodyType = savedLooks.BodyType;
            }
            d.Dockable = data.Dockable;
            d.InStasisFromTick = data.InStasisFromTick;
            if (b != null) {
                d.LastPosition = data.Position;
                d.SetBeing(b);
            }
            d.MindId = data.MindId;

            d.StatsBeing = new StatsBeing();
            StatsContainerData.Deserialize(d.StatsBeing, data.StatsBeing);
            d.StatsBeing.ActivateFor(d);
            d.StatsBeing.FinalizeStats();

            var isBodyOK = BodyData.TryDeserialize(d, data.Body, out var body);
            d.Body = body;
            if (!isBodyOK) {
                D.Err("Failed to deserialize body for being: {0}. Regenerating.", b);
                var bodyGen = Registries.GenBeingDataSteps.Get(GenBeingBodyDataStep.StepId);
                if (!bodyGen.Generate(The.R.State, d, Rng.Unseeded, new GenBeingVars(d.Species, BeingType.Neutral))) {
                    D.Err("Failed to do fallback generation of body for being: {0}", b);
                    return false;
                }
            }

            if (needsLegacyLooksRegen) {
                D.Log("Being {0} has pre-LookPartTypeDef looks data, regenerating.", b);
                The.Looks.RegenerateAll(Rng.Unseeded, d);
            }

            RepairMissingLookBodyType(d);

            InventoryData.TryDeserialize(S, d, data.Inventory);

            if (data.Conditions != null) {
                for (int i = 0; i < data.Conditions.Length; i++) {
                    ConditionStateData c = data.Conditions[i];
                    var cond = ConditionStateData.Deserialize(c, d);
                    if (cond != null) {
                        d.Conditions[cond.Condition.IdH] = cond;
                    }
                }
            }
            foreach (var dp in data.ConditionProgress) {
                d.ConditionProgress.Add(dp.Key, dp.Value);
            }

            if (d.HasStateFlag(BeingState.Alive | BeingState.Dead)) {
                D.Err("A being is both dead and alive! Making dead. {0}", d);
                d.StateFlags &= ~BeingState.Alive;
            }

            if (!d.Body.IsAlive && d.StateFlags.HasFlag(BeingState.Alive)) {
                // fixing body state
                d.Body.AddToBodyState(BodyState.Alive, true);
            }

            // D.Warn("Deserialized being data: {0}", d);

            if (loadedLookTypeHashes != null) {
                foreach (var lookPart in d.Looks.Parts) {
                    if (!loadedLookTypeHashes.Contains(lookPart.Type.IdH)) {
                        The.Looks.GenerateMissing(Rng.Unseeded, d, lookPart);
                    }
                }
            }

            return true;
        }

        private static void RepairMissingLookBodyType(BeingData data) {
            if (data.Looks.BodyType != '\0') { return; }
            var bodyPart = data.Looks.Get(LookPartTypeDef.Body);
            var spriteId = bodyPart?.SpriteId;
            if (spriteId == null) { return; }
            var slashIdx = spriteId.LastIndexOf('/');
            var tagIdx = slashIdx + 1;
            if (tagIdx + 2 < spriteId.Length
                    && spriteId[tagIdx] == '['
                    && spriteId[tagIdx + 2] == ']') {
                data.Looks.BodyType = spriteId[tagIdx + 1];
                return;
            }
            D.ErrSoft("Could not repair missing BodyType from body sprite: {0} ({1})",
                spriteId, data);
        }

        #if !PRODUCTION_BUILD
        private static void MigrateLegacyStateFlags(BeingDataData data, BeingData d) {
            if (d.StateFlags != BeingState.Uninitialized) { return; }
            // D.Err("Being state flags uninitialized: Legacy State: {0}", data.StateLegacy);
            switch (data.StateLegacy) {
                case 1: {
                        // active -> alive
                        d.StateFlags = BeingState.Alive;
                        break;
                    }
                case 2: {
                        // expedition
                        d.StateFlags = BeingState.Alive | BeingState.Hidden | BeingState.Expedition;
                        break;
                    }
                case 3: {
                        // stasis
                        D.Err("Stasis!");
                        break;
                    }
                case 4: {
                        // hidden
                        d.StateFlags = BeingState.Alive | BeingState.Hidden;
                        break;
                    }
                case 5: {
                        // dead
                        d.StateFlags = BeingState.Dead;
                        break;
                    }
            }
        }
        #endif
    }
}
