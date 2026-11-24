using Game.CodeGen;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    /// <summary>
    /// Wraps a humanoid player colonist as a quest provider.
    /// QuestProviderType.Colonist
    /// </summary>
    public sealed class ColonistQuestProvider : IQuestProvider {
        public readonly BeingData BeingData;
        public Mind Mind => BeingData.Mind;
        /// <summary>
        /// Optional extra being mind ids to require on the same expedition.
        /// Populated by quests that send multiple specific colonists (e.g. crush expedition).
        /// </summary>
        public uint[] ExtraBeingIds;
        // Providers are QuestSys.ProviderQuests keys, and every story event makes a new one:
        // equal per colonist, so a colonist holds one open quest at a time. Captured, because
        // a key's hash must not change when the brain moves to another body.
        public readonly uint MindId;

        public ColonistQuestProvider(BeingData beingData) {
            BeingData = beingData;
            MindId = beingData.Mind?.Id ?? 0;
        }

        public override bool Equals(object obj) {
            return obj is ColonistQuestProvider other && other.MindId == MindId;
        }

        public override int GetHashCode() => (int) MindId;

        // A colonist asks for one favour at a time, and an OncePerGiver favour only once
        public static bool CanOffer(BeingData beingData, QuestDef def) {
            if (!CanGiveQuest(beingData, def) || beingData.Mind == null) { return false; }
            return A.S.Query.CanOfferColonistQuest.Ask(beingData.Mind.Id, def);
        }

        public static bool CanGiveQuest(BeingData beingData) {
            return beingData?.HasAbility(AbilityIdH.Intelligence) == true;
        }

        public static bool CanGiveQuest(BeingData beingData, QuestDef def) {
            if (!CanGiveQuest(beingData)) { return false; }
            // Beings without a past can't ask for revenge or remember a hidden stash.
            // The past lives in the mind: clones, baby minds and humans hatched before
            // eggs set BodyFlags.Clone all have no backstory roles. The body flag also
            // covers teleporter clones, which copy the original's backstory.
            if (def.Flags.HasFlag(QuestFlags.RequiresMemories)
                    && (beingData.Body?.HasFlag(BodyFlags.Clone) == true
                        || Arrays.IsEmpty(beingData.Mind?.Experience?.Roles))) {
                return false;
            }
            return true;
        }

        public QuestProviderType ProviderType => QuestProviderType.Colonist;
        public uint ProviderId => Mind.Id;
        public FactionDef Faction => FactionDef.Player;
        public int Seed => (int) Mind.Id;

        private uint questId;
        public uint QuestId {
            get => questId;
            set => questId = value;
        }

        public string StarmapLink => Mind.LinkToShortName;

        public Vector2 SectorLocation {
            get {
                var sector = SOUtils.GetPlayerSector(A.S);
                return sector?.SO?.Location ?? Vector2.zero;
            }
        }

        public SpaceObject ParentSector => SOUtils.GetPlayerSector(A.S)?.SO;
        public SpaceObject ProviderSO => SOUtils.GetPlayerSector(A.S)?.SO;

        public override string ToString() =>
            $"ColonistQuestProvider[{Mind.Name.ShortName}|{Mind.Id}]";
    }
}
