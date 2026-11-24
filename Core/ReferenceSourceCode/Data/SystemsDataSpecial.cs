using MessagePack;
using Game.Systems.AI;
using Game.Systems.Stats;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.AI.Combat;
using Game.Systems.Trade;
using Game.Systems.Naming;
using Game.Systems.Twitch;
using Game.Systems.Space;
using Game.Systems.Crafting;
using Game.Systems.Ships;
using Game.Systems.Diplomacy;
using Game.Systems.Minigames;
using System.Collections.Generic;
using Game.Systems.Statistics;
using Game.Systems.Story;
using Game.Systems.Drama;
using Game.Systems.Quests;
using Game.Systems.UIUX;

namespace Game.Data {
    [MessagePackObject]
    public class SystemsDataSpecial {
        /// <summary>
        /// For mod creators:
        /// See ISaveableSpecial interface for systems.
        /// When saving, check if this field is null, if it is, create a new dictionary.
        /// When loading, check if this field is null. Then check if your key exists.
        /// </summary>
        [Key("ModData")] public Dictionary<string, byte[]> ModData;
        [Key("Expeditions")] public ExpeditionsData ExpeditionsData;
        [Key("Dirt")] public DirtSysData Dirt;
        [Key("Projectiles")] public ProjectilesSysData Projectiles;
        [Key("Energy")] public EnergySysData Energy;
        [Key("Codex")] public CodexSysData Codex;
        [Key("Tutorial")] public TutorialSysData Tutorial;
        /// <summary>
        /// Quests key was used in legacy system, don't use it!
        /// </summary>
        [Key("Quest")] public QuestSysData Quests;
        [Key("Log")] public LogSysData Log;
        [Key("Scan")] public ScanSysData Scan;
        [Key("Tint")] public TintSysData Tint;
        [Key("Planets")] public PlanetsSysData Planets;
        [Key("Copters")] public CoptersSysData Copters;
        [Key("Firex")] public FirexSysData Firex;
        [Key("AIPlanner")] public AIPlannerSysData AIPlanner;
        [Key("Beings")] public BeingsSysData Beings;
        [Key("Objs")] public ObjsSysData Objs;
        [Key("BotDock")] public BotDockSysData BotDock;
        [Key("Sleep")] public SleepSysData Sleep;
        [Key("Combat")] public CombatSysData Combat;
        [Key("Trade")] public TradeSysData Trade;
        [Key("Naming")] public NamingSysData Naming;
        [Key("Space")] public SpaceSysData Space;
        [Key("Crafting")] public CraftingSysData Crafting;
        [Key("NPCShip")] public NPCShipSysData NPCShip;
        [Key("ShipCombat")] public ShipCombatSysData ShipCombat;
        [Key("ShipView")] public ShipViewSysData ShipView;
        [Key("Diplomacy")] public DiplomacySysData Diplomacy;
        [Key("Story")] public StorySysData Story;
        [Key("Drama")] public DramaSysData Drama;
        [Key("UIUX")] public UIUXSysData UIUX;
        [Key("Statistics")] public StatisticsSysData Statistics;
        [Key("Policies")] public PoliciesSysData Policies;
        [Key("PendingBodies")] public PendingSpaceBodySysData PendingBodies;
        [Key("Minigames")] public MinigameSysData Minigames;
    }
}
