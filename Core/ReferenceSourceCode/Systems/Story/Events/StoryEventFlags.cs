using System;

namespace Game.Systems.Story {
    [Flags]
    public enum StoryEventFlags : ulong {
        None = 0,
        /// <summary>
        /// Any event with this tag will not be considered for auto execution
        /// </summary>
        OnDemand = 1 << 0,
        Being = 1 << 1,
        Device = 1 << 2,
        Raid = 1 << 3,
        ShipCombat = 1 << 4,
        ShipInteraction = 1 << 5,
        Expedition = 1 << 6,
        Capsule = 1 << 7,
        StasisPod = 1 << 8,
        ColonyGrowthRobo = 1 << 9,
        ColonyGrowthBio = 1 << 10,
        Death = 1 << 11,
        Disease = 1 << 12,
        Signal = 1 << 13,
        Anomaly = 1 << 14,
        Fire = 1 << 15,
        Explosion = 1 << 16,
        OncePerGame = 1 << 17,
        HeavyDisaster = 1 << 18,
        WaitsForCondition = 1 << 19,
        Funny = 1 << 20,
        Annoying = 1 << 21,
        CanHaveDuplicates = 1 << 22,
        SpaceThreat = 1 << 23,
        DerelictShip = 1 << 24,
        Space = 1 << 25,
        NoHyperspace = 1 << 26,
        MentalBreakdown = 1 << 27,
        NeverTimeout = 1 << 28,
        Egg = 1 << 29,
        /// <summary>
        /// For events that occur due to neglect to take care of things
        /// </summary>
        Neglect = 1 << 30,
        Punishing = 1L << 31,
        /// <summary>
        /// By default, all events cooldown after they are finished,
        /// but some long running events may want to cooldown on start
        /// </summary>
        CooldownOnStart = 1L << 32,
        /// <summary>
        /// Quests given to the player by your colonists
        /// </summary>
        ColonistRequest = 1L << 33,
        /// <summary>
        /// A bit more serious than social interactions - stabbings, fights, etc
        /// </summary>
        Drama = 1L << 34,
        /// <summary>
        /// Random social interactions
        /// </summary>
        Social = 1L << 35,
        /// <summary>
        /// This is only for OnDemand events that are running via a hook. By
        /// default repeat delay won't be ignored even in case of fallback,
        /// unless this flag is set
        /// </summary>
        CanIgnoreRepeatDelay = 1L << 36,

        /// <summary>
        /// For things like photon storms, dust storms, etc
        /// </summary>
        SpaceStorm = 1L << 37,

        /// <summary>
        /// Tracks organic (unprovoked) NPC ship attacks on the player.
        /// Used to enforce cooldown between attacks; retaliation and
        /// story/quest-driven combat are exempt.
        /// </summary>
        NPCShipAttack = 1L << 38,

        /// <summary>
        /// Colonist asks for pet of some type. It's annoying to get asked for
        /// dog + cat + pig one after another
        /// </summary>
        ColonistPetRequest = 1L << 39,

        /// <summary>
        /// Event may be picked by the hook fallback pass, which runs only when
        /// no other event was eligible for the hook. That pass ignores story
        /// points, schedule and repeat delay, but still checks flag cooldowns,
        /// requirements and CheckConditions.
        /// </summary>
        Fallback = 1L << 40,

        /// <summary>
        /// This event brings its own survivors, so don't run it on a derelict that already
        /// gets guaranteed crew remnants from the combat kill - see
        /// CmdSpawnDerelictShip's hook filter and ShipStateFlags.SpawnDevices.
        /// </summary>
        DerelictSpawnsSurvivors = 1L << 41,

        /// <summary>
        /// Flags that have a cooldown once the story event executes
        /// </summary>
        Coolable = Being | Device | Raid | ShipCombat | ShipInteraction
            | ColonyGrowthBio | ColonyGrowthRobo | Death | Disease | Fire
            | Explosion | HeavyDisaster | Funny | Annoying | SpaceThreat
            | Neglect | Punishing | Drama | Social | SpaceStorm
            | NPCShipAttack | ColonistRequest | ColonistPetRequest,

        RunASAPWhenPrioritized = Social | Raid | Drama | ColonistRequest,
    }

}
