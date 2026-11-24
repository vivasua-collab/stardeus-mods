#if UNITY_EDITOR
// #define DEBUG_SYSTEMS
#endif
using System;
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Areas;
using Game.Systems.Atmo;
using Game.Systems.Copters;
using Game.Systems.Creatures;
using Game.Systems.Electricity;
using Game.Systems.Firex;
using Game.Systems.Light;
using Game.Systems.Path;
using Game.Systems.Planets;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using Game.Story;
using Game.Systems.Inventory;
using Game.Systems.Twitch;
using Game.Systems.Ships;
using Game.Systems.Diplomacy;
using Game.Systems.Story;
using System.Linq;

namespace Game.Systems {
    public sealed class GameSystems {
        private static readonly Dictionary<string, SystemFactory> systemPrototypes = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            systemPrototypes.Clear();
        }
        public static void Register(string key, SystemFactory prototype) {
            #pragma warning disable
            if (systemPrototypes.ContainsKey(key)) {
                #if DEBUG_SYSTEMS
                D.Log("Replacing system: {0}", key);
                D.Warn("Replacing system {0}: {1} -> {2}",
                    key, systemPrototypes[key], prototype);
                #endif
                systemPrototypes[key] = prototype;
            } else {
                #if DEBUG_SYSTEMS
                D.Log("Registering system: {0}", key);
                #endif
                systemPrototypes.Add(key, prototype);
            }
            #pragma warning restore
        }
        public AreaSys Areas;
        #if LEGACY_PATH
        public PathfinderSys Path;
        #endif
        public PathSys Path;
        public ElectricitySys Electricity;
        public InfoSys Info;
        public AtmoSys Atmo;
        public HintSys Hint;
        public UpgradesSys Upg;
        public RenderingSys Rend;
        public CoptersSys Copters;
        public DiplomacySys Diplomacy;
        public AudioSys Audio;
        public PlanetsSys Planets;
        public ResearchSys Research;
        public CombatSys Combat;
        public ParticlesSys Particles;
        public DirtSys Dirt;
        public LightSys Light;
        public LogSys Log;
        public FirexSys Firex;
        public EconomySys Economy;
        public SlotSys Slots;
        public ExpeditionSys Expedition;
        public TerraformSys Terraform;
        public UniverseSys Universe;
        public ShipSys Ship;
        public NPCShipSys NPCShip;
        public ScanSys Scan;
        public AISys AI;
        public TintSys Tint;
        public ShipNavSys ShipNav;
        public SecuritySys Sec;
        public InventorySys Inventory;
        public TwitchSys Twitch;
        public PendingSpaceBodySys PendingBodies;
        public T Get<T>() where T : GameSystem {
            foreach (var s in systems.Values) {
                if (s is T sysT) {
                    return sysT;
                }
            }
            D.Err("Cannot find system by type: {0}", typeof(T));
            return default;
        }
        public T Get<T>(string id) where T : GameSystem {
            var system = systems.Get(id, null);
            if (system == null) {
                D.Err("Cannot find system by id: {0}", id);
                return default;
            }
            if (system is T systemT) {
                return systemT;
            }
            D.Err("Found system by id {0}, but type did not match. Expected {1}, Actual {2}",
                id, typeof(T), system.GetType());
            return default;
        }
        private readonly Dictionary<string, GameSystem> systems = new();
        public Dictionary<string, GameSystem> Known => systems;
        public readonly List<GameSystem> Active = new();
        public delegate GameSystem SystemFactory();

        public GameSystems() {
            foreach (var proto in systemPrototypes) {
                var sys = proto.Value();
                if (proto.Key != sys.Id) {
                    D.Err("Skipping Misconfigured GameSystem! Registered with Id: {0}, while instance id: {1}",
                        proto.Key, sys.Id);
                    continue;
                }
                if (systems.ContainsKey(sys.Id)) {
                    D.Err("Trying to initialize same system twice: {0}", sys.Id);
                } else {
                    if (systems.ContainsValue(sys)) {
                        D.Err("Trying to add system twice: {0}: {1}", sys.Id, sys);

                    }
                    systems.Add(sys.Id, sys);
                }
            }
        }

        private readonly Dictionary<string, string> disabledSystems = new();

        public void Initialize(GameRuntime r, string firstVersion) {
            D.Ass(r.IsCurrent, "Runtime is not current during Initialize!");
            disabledSystems.Clear();
            Active.Clear();
            var isSandbox = r.State.Scenario?.IsSandbox != false;
            // originally systems were registered in alphabetic order,
            // and they were made to initialize in this order too.
            // after switch to [SelfInit], the order isn't guaranteed anymore
            // so we will still initialize it like before
            foreach (var sys in systems.OrderBy(kv => kv.Key)) {
                var val = sys.Value;
                if (isSandbox && val.SkipInSandbox) {
                    val.OnDeactivate();
                    val.IsDeactivated = true;
                    continue;
                }

                if (val.MinRequiredVersion != null) {
                    var isTooNew = VersionInfo.IsOlderThan(
                        val.MinRequiredVersion,
                        firstVersion);
                    if (isTooNew) {
                        #if PRODUCTION_BUILD
                        D.Warn("Skipping system: {0}. It was created with version {1}. Min required version for system is {2}.",
                            sys.Key, firstVersion, val.MinRequiredVersion);
                        #endif
                        val.IsDeactivated = true;
                        disabledSystems.Add(val.Id, val.MinRequiredVersion);
                        continue;
                    }
                }
                val.IsDeactivated = false;
                AssignConvenienceVariable(val);
                #if DEBUG_SYSTEMS
                D.Log("Initializing system: {0}", sys.Key);
                #endif
                val.Initialize(r);
                // could be deactivated during initialize
                if (!val.IsDeactivated) {
                    Active.Add(val);
                }
            }
            NotifyDisabledSystems(firstVersion);
        }

        [Obsolete("This is a bad pattern, signals / queries should be used instead of 'convenience' reference leaking")]
        private void AssignConvenienceVariable(GameSystem val) {
            if (val is AudioSys audio) { Audio = audio; }
            if (val is ParticlesSys p) { Particles = p; }
            if (val is AreaSys areas) { Areas = areas; }
            if (val is AtmoSys atm) { Atmo = atm; }
            if (val is CombatSys comb) { Combat = comb; }
            if (val is RenderingSys rend) { Rend = rend; }
            if (val is PathSys npath) { Path = npath; }
            if (val is ElectricitySys ele) { Electricity = ele; }
            if (val is InfoSys info) { Info = info; }
            if (val is UpgradesSys upg) { Upg = upg; }
            if (val is HintSys hint) { Hint = hint; }
            if (val is ResearchSys res) { Research = res; }
            if (val is FirexSys fi) { Firex = fi; }
            if (val is TerraformSys ter) { Terraform = ter; }
            if (val is EconomySys eco) { Economy = eco; }
            if (val is SlotSys slots) { Slots = slots; }
            if (val is DiplomacySys dip) { Diplomacy = dip; }
            // if (val is StorySys story) { Story = story; }
            if (val is LogSys log) { Log = log; }
            if (val is ScanSys scan) { Scan = scan; }
            if (val is PlanetsSys pla) { Planets = pla; }
            if (val is DirtSys dirt) { Dirt = dirt; }
            if (val is LightSys light) { Light = light; }
            if (val is CoptersSys copters) { Copters = copters; }
            if (val is UniverseSys univ) { Universe = univ; }
            if (val is ShipSys ship) { Ship = ship; }
            if (val is ShipNavSys nav) { ShipNav = nav; }
            if (val is NPCShipSys npcs) { NPCShip = npcs; }
            if (val is TintSys tint) { Tint = tint; }
            if (val is SecuritySys sec) { Sec = sec; }
            if (val is ExpeditionSys exp) { Expedition = exp; }
            if (val is InventorySys inv) { Inventory = inv; }
            if (val is AISys ai) { AI = ai; }
            if (val is TwitchSys tw) { Twitch = tw; }
            if (val is PendingSpaceBodySys psb) { PendingBodies = psb; }
        }

        private void NotifyDisabledSystems(string firstVersion) {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) { return; }
            #endif
            if (disabledSystems.Count <= 0) { return; }
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(T.GameSystemsDisabledFirstVersion_V1.F(firstVersion));
            sb.Append("\n");
            foreach (var sys in disabledSystems) {
                var version = sys.Value;
                if (!version.StartsWith("v")) {
                    version = $"v{version}";
                }
                sb.AppendFormat("\n * <b>{0}</b> -> <b>{1}</b>", sys.Key, version);
            }
            sb.AppendFormat("\n\n{0}", T.GameSystemsDisabledEndNote);
            WPopup.Create(true, IconId.CWarning, T.GameSystemsDisabled_V1.F(disabledSystems.Count), sb.ToString()).Spawn();
            sb.Clear();
            disabledSystems.Clear();
        }

        public void Unload() {
            //D.Warn("Unloading systems");
            foreach (var sys in systems) {
                try {
                    sys.Value.Unload();
                    sys.Value.RemoveState();
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed unloading system: {0}", sys.Value.Id);
                }
            }
            Active.Clear();
        }

        public void Restore(GameStateData s) {
            if (s == null) {
                D.Err("Systems trying to restore null state!");
            } else {
                SystemsData.Deserialize(this, s.Systems);
            }
        }

        public void SaveData(GameStateData s) {
            s.Systems = SystemsData.Serialize(this);
        }
    }
}
