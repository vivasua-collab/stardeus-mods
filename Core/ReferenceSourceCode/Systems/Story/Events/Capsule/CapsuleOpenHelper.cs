using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    public static class CapsuleOpenHelper {
        private const string SourceKindKey = "CapsuleSourceKind";
        private const string SourceKindQuestPackage = "QuestPackage";
        private const string SourceNameKey = "CapsuleSourceName";
        private const string SourceIconKey = "CapsuleSourceIcon";
        private const string RaidPointsKey = "RaidPoints";
        private const string RaidGearTierKey = "RaidGearTier";
        private const string RaiderIdsKey = "RaiderIds";

        public static bool Start(GameState s, object source, Obj obj, Being opener,
                int seed, string riggedEventId = null, string factionId = null,
                float raidPoints = 0f, bool isQuestPackage = false, int raidGearTier = -1,
                int[] raiderIds = null) {
            if (obj == null) {
                D.Err("Can't start capsule contents without a target object: {0}", source);
                return false;
            }
            if (seed == 0) {
                seed = Hashes.Quick(Primes.Prime020, obj.Id);
            }

            if (riggedEventId != null) {
                var riggedEvent = StoryEventDef.Get(riggedEventId);
                if (riggedEvent == null) {
                    D.Err("Capsule RiggedEventId not available: {0} | {1}",
                        riggedEventId, obj);
                } else {
                    var data = StoryEventData.CreateFor(riggedEvent);
                    ApplyContext(data, obj, opener, seed, factionId, raidPoints,
                        isQuestPackage, raidGearTier, raiderIds);
                    if (s.Query.StartStoryEvent.Ask(data)) {
                        return true;
                    }
                    D.Err("Failed starting rigged capsule event: {0} | {1}",
                        data, obj);
                }
            }

            var hook = StoryEventHook.CreateFor(source ?? obj, StoryEventFlags.Capsule)
                .WithSeed(seed)
                .Instant();
            hook.WithBeforeStart((d) => ApplyContext(d, obj, opener, seed, factionId,
                raidPoints, isQuestPackage, raidGearTier, raiderIds));

            var hookData = s.Query.AddStoryEventHook.Ask(hook);
            if (StoryEventData.IsOK(hookData)) {
                return true;
            }
            D.Err("Couldn't pick an event for capsule hook: {0} | {1}",
                hookData, hook);
            return false;
        }

        public static string ContainerName(StoryEventData data) {
            return data.Data?.GetString(SourceNameKey, null) ?? T.StorageCapsule;
        }

        public static string ContainerIcon(StoryEventData data) {
            return data.Data?.GetString(SourceIconKey, null)
                ?? GraphicsId.ObjContainersStorageCapsule;
        }

        public static string ContainedText(StoryEventData data, string contents) {
            if (data.Data?.GetString(SourceKindKey, null) == SourceKindQuestPackage) {
                return $"{T.QuestPackageContained}: {contents}";
            }
            return T.StorageCapsuleContained_V1.F(contents);
        }

        private static void ApplyContext(StoryEventData data, Obj obj, Being opener,
                int seed, string factionId, float raidPoints, bool isQuestPackage,
                int raidGearTier = -1, int[] raiderIds = null) {
            data.TargetObj = obj.Data;
            data.TargetBeing = opener?.Data;
            data.Seed = seed;
            if (factionId != null) {
                data.Faction = FactionDef.Get(factionId);
            }
            var d = data.GetOrCreateData();
            d.SetString(SourceNameKey, obj.Definition?.NameT);
            d.SetString(SourceIconKey, obj.Definition?.Preview);
            if (isQuestPackage) {
                d.SetString(SourceKindKey, SourceKindQuestPackage);
            }
            if (raidPoints > 0f) {
                d.SetFloat(RaidPointsKey, raidPoints);
                d.SetInt(RaidGearTierKey, raidGearTier);
            }
            if (!Arrays.IsEmpty(raiderIds)) {
                d.SetIntSet(RaiderIdsKey, raiderIds);
            }
        }
    }
}
