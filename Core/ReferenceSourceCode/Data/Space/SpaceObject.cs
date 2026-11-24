#if UNITY_EDITOR
// #define DEBUG_VISIBILITY
#endif
using System;
using System.Collections.Generic;
using System.Text;
using Game.Components;
using Game.Constants;
using Game.Data.Factions;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Trade;
using Game.Systems.Naming;
using Game.Systems.Space;
using Game.Input;
using Game.Systems.Ships;
using System.Diagnostics;
using Game.Systems.Quests;

namespace Game.Data.Space {
    public class SpaceObject : IQuestProvider {
        public GameState S;
        public int Id;
        public string Name;
        public Dictionary<int, int> POIs;
        public uint TraderId;
        public Action<string> OnNameChange;
        public SOFlags Flags;
        public Dictionary<TerraformType, float> TerraformState;
        /// <summary>
        /// If Sector != null, this space object is the root of that sector
        /// </summary>
        public SpaceSector Sector;
        public SpaceRegion Region;
        public ISpaceEntity SpaceEntity => (ISpaceEntity) Sector ?? Region;
        public StarfieldSO Visual;
        public NPCShip Ship;
        public uint QuestId { get; set; }
        private SOInfo info;
        public SOInfo Info => info ??= new SOInfo(this);
        public UISpaceObject UI { get; private set; }
        public string TypeName => Type.NameT;
        // Originally only for objects with children. Resource deposits are also marked visited
        // via MarkLocalVisited() once an expedition actually mines them (see
        // ExpeditionStageMineResource), so quests stop re-offering deposits already mined.
        public bool IsVisited => (Flags & SOFlags.Visited) == SOFlags.Visited;
        /// <summary>
        /// For marking objects as flown over in hyperspace
        /// </summary>
        public bool IsVisitedHS => (Flags & SOFlags.VisitedHS) == SOFlags.VisitedHS;
        public bool HasTerraformable => (Flags & SOFlags.Terraformable) == SOFlags.Terraformable;
        // Owner
        public string FactionId;
        public FactionDef Faction { get; private set; }
        // Presence of others
        public List<FactionDef> Factions;
        private SOLooks looks;
        private SOLooks sectorLooks;
        private int looksSeed;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public bool CanHaveLife {
            get {
                var ntt = SpaceEntity;
                ntt ??= Parent?.SpaceEntity;
                if (ntt == null) {
                    D.Err("No SpaceEntity on SpaceObject and its parent! {0} / {1}", this, Parent);
                    return false;
                }
                if (ntt.Biome.Template == null) {
                    D.Err("No template on space entity biome: {0}", ntt);
                    return false;
                }
                return !ntt.Biome.Template.NoLife;
            }
        }

        public int LooksSeed {
            get {
                if (looksSeed != 0) { return looksSeed; }
                if (Type.InheritParentLooks) {
                    looksSeed = Parent.LooksSeed;
                } else {
                    looksSeed = Hashes.Quick(Seed, Primes.Prime024);
                }
                return looksSeed;
            }
        }
        private SOKnowledge knowledge;
        public SOKnowledge Knowledge => knowledge ??= new(this);
        public bool HasKnowledge => knowledge != null;
        public string Layer;
        public SpaceObjectType Type;
        private ComponentData data;
        public ComponentData Data => data ??= new ComponentData(Id, "SpaceObject");
        public bool HasData => data != null;
        public SpaceObject Parent { get; private set; }
        public bool HasChildren => !Arrays.IsEmpty(Children);
        [SkipDumpInstance]
        public List<SpaceObject> Children;
        [SkipDumpInstance] public SpaceObjectData SaveData;
        public QuestProviderType ProviderType => QuestProviderType.SpaceObject;
        public uint ProviderId => (uint) Id;
        public SpaceObject ProviderSO => this;
        private Vector2 location;
        public Vector2 Location {
            set {
                // if (location != value) {
                //     D.Warn("Location of {0} set to: {1}->{2} | {3}",
                //         Type.Id, location, value, this);
                // }
                if (float.IsNaN(value.x)) {
                    D.Err("Trying to set invalid location! {0} | {1}", value, this);
                    return;
                }
                location = value;
            }
            get => location;
        }
        public Vector2 SectorLocation => Location;
        public SpaceObject ParentSector => SOUtils.SectorFor(this)?.SO;
        public float Size;
        public Map Map;
        private int seed;
        public int Seed {
            get {
                if (seed == 0) {
                    var s = S.Seed;
                    if (s == 0) {
                        D.Err("Trying to get space object seed when S seed is 0!");
                    }
                    if (Id == 0) {
                        D.Err("Trying to get space object seed when Id is 0!");
                    }
                    seed = Hashes.Quick(s, Hashes.Quick(Id, Primes.Prime036));
                }
                return seed;
            }
        }

        private Trader trader;
        public bool IsTraderCreated => trader != null;
        public Trader Trader {
            get {
                if (trader != null) { return trader; }
                if (!Type.HasTrader) { return null; }
                if (TraderId != 0) {
                    trader = S.Query.FindTrader.Ask(TraderId);
                    if (trader == null) {
                        D.Err("Trader is null for SO while TraderId != 0! {0}  -> {1} ",
                            TraderId, this);
                        TraderId = 0;
                    }
                }
                if (trader == null) {
                    var req = new TraderRequest();
                    req.SO = this;
                    req.Faction = Faction;
                    req.Rng = new Rng(Hashes.Quick(Seed, Primes.Prime038));
                    float wealth;
                    if (Type.IdH == SpaceObjectTypeIdH.Port) {
                        wealth = req.Rng.Curve(TraderRequest.WealthCurveRich);
                        req.MinItems = Tunables.TraderMinItems * 2;
                        req.MinGroups = Tunables.TraderMinGroups * 2;
                        req.MinCash = Tunables.TraderMinCash * 10;
                    } else if (Ship?.Class.IsSpaceStation == true) {
                        wealth = req.Rng.Curve(TraderRequest.WealthCurveMid);
                    } else {
                        // random shit, likely an expedition POI
                        wealth = req.Rng.Curve(TraderRequest.WealthCurvePoor);
                    }
                    req.WealthFactor = wealth;
                    trader = S.Query.CreateTrader.Ask(req);
                    if (trader != null) {
                        TraderId = trader.Id;
                    } else {
                        D.Err("Failed creating a trader from req! {0} for SO: {1}",
                            D.DumpInstance(req), this);
                    }
                }
                return trader;
            }
        }

        private readonly Dictionary<string, float> serializedState =
                new(TerraformType.TypeCount);

        public float MiningDifficulty;
        public bool HasAnyMats {
            get {
                return !Arrays.IsEmpty(Mats) || !Arrays.IsEmpty(knowledge?.Mats);
            }
        }
        public List<MatType> Mats;
        public string StarmapLink => Texts.LinkToStarmap(this);
        private string displayName;
        public string DisplayName {
            get {
                if (displayName == null) {
                    if (string.IsNullOrWhiteSpace(Name)) {
                        if (Type.UseTypeName) {
                            return Type.NameT;
                        }
                        if (Type.IdH == SpaceObjectTypeIdH.ShipHyperTrail) {
                            Name = T.SpaceObjectShiphypertrail;
                        } else if (Type.IdH == SpaceObjectTypeIdH.Player) {
                            Name = S.GameName;
                        } else if (Type.IdH == SpaceObjectTypeIdH.SectorUnknown) {
                            Name = T.SpaceObjectSector;
                        } else {
                            var name = S.Query.GenerateName.Ask(NameRequest.ForSO(
                                Type.IdH, new Rng(Seed)));
                            name.Consume();
                            UpdateName(name.Name);
                        }
                    }
                    displayName = DisplayNameWithSuffix(Name);
                }
                return displayName;
            }
        }
        private static readonly Dictionary<SOLooks, float> weightedTemplates = new();
        private readonly static Curve color1Sat = new float[] {
            0f, 0.0f,
            1f, 0.2f
        };
        private readonly static Curve color2Sat = new float[] {
            0f, 0.0f,
            1f, 1.0f
        };
        private readonly static Curve color1Val = new float[] {
            0f, -0.3f,
            1f, 0.0f
        };
        private readonly static Curve color2Val = new float[] {
            0f, -1f,
            0.1f, -0.5f,
            0.2f, -0.45f,
            0.3f, -0.4f,
            0.4f, -0.3f,
            0.5f, -0.2f,
            0.6f, -0.12f,
            0.7f, -0.1f,
            0.8f, -0.08f,
            0.9f, -0.05f,
            1f, 0f
        };
        private const float hueRange = 0.01f;

        private string customIcon;
        public string Icon {
            get {
                if (Ship != null) {
                    if (Ship.IsPlayer) {
                        return IconId.CComputing;
                    }
                    return Ship.Class.Preview;
                }
                if (this.Type.CanHaveCustomIcon) {
                    return customIcon ??= Data.GetString(SpaceObjectLooks.VarIcon, null)
                        ?? this.Type.Icon;
                }
                return this.Type.Icon;
            }
            set {
                if (this.Type.CanHaveCustomIcon) {
                    Data.SetString(SpaceObjectLooks.VarIcon, value);
                    customIcon = value;
                } else {
                    D.Err("Cannot set the icon on a type that has CanHaveCustomIcon false! {0} => {1}",
                        Type.Id, value);
                }
            }
        }
        private float exitDist = -1f;
        public float ExitDist {
            get {
                if (exitDist < 0f) {
                    if (HasChildren) {
                        foreach (var child in Children) {
                            if (!child.Type.CanMoveInStarmap) {
                                exitDist = Maths.Max(exitDist,
                                    child.Location.magnitude + child.Size);
                            }
                        }
                        exitDist = Maths.Max(exitDist, Type.MinExitDist);
                        exitDist *= 1.2f;
                    }
                }
                return exitDist;
            }
        }
        public void MarkVisited() {
            if (!IsVisited) {
                Flags |= SOFlags.Visited;
                Flags |= SOFlags.VisitedHS;
                S.Situation.SpaceObjectsDiscovered++;
            }
        }
        // Marks a mined resource deposit as visited so quests stop re-offering it. Unlike
        // MarkVisited(), does not touch SpaceObjectsDiscovered (reserved for objects with children).
        public void MarkLocalVisited() {
            Flags |= SOFlags.Visited;
        }

        public string ParentPath(bool fromPlayer = true, bool link = true) {
            if (Parent == null) { return null; }
            SpaceObject pp = null;
            if (fromPlayer) {
                pp = S.Universe.Player.Parent;
            }
            Caches.StringList.Clear();
            var p = this;
            bool foundParent = false;
            while (p != null && p.Parent != null) {
                Caches.StringList.Add(link ? Texts.LinkToStarmap(p) : Texts.Accented(p.DisplayName, false));
                p = p.Parent;
                if (foundParent) {
                    break;
                } else if (fromPlayer && p == pp) {
                    foundParent = true;
                }
            }
            Caches.StringList.Reverse();
            return string.Join($" {GlyphMap.ArrowRight} ", Caches.StringList);
        }
        public void SetType(SpaceObjectType type) {
            if (Type != type) {
                Type = type;
                looks = null;
                // MinExitDist comes from the type: a consumed event or a nuked sector must not
                // keep the old footprint until the next load (STA-1933)
                exitDist = -1f;
            }
            displayName = null;
        }

        public float AddTerraformProgress(TerraformType t, float p,
                out bool hasJustFinished) {
            TerraformState ??= new Dictionary<TerraformType, float>();
            if (p > 1f) { p = 1f; }
            if (!TerraformState.TryGetValue(t, out var val)) {
                val = p;
                TerraformState.Add(t, val);
                hasJustFinished = val >= 1f;
            } else {
                var oldVal = val;
                val += p;
                if (val >= 1f) {
                    hasJustFinished = oldVal < 1f;
                    val = 1f;
                } else {
                    hasJustFinished = false;
                }
                TerraformState[t] = val;
            }
            UI?.UpdateLooks();
            Visual?.UpdateLooks();
            return val;
        }

        public float TerraformingProgress {
            get {
                if ((Flags & SOFlags.Habitable) == SOFlags.Habitable) {
                    return 1f;
                }
                var total = 0f;
                var prog = 0f;
                var tt = TerraformType.All;
                for (int i = 0; i < tt.Count; i++) {
                    total++;
                    prog += TerraformingProgressFor(tt[i]);
                }
                var p = prog / total;
                if (p >= 0.99999f) {
                    Flags |= SOFlags.Habitable;
                }
                return p;
            }
        }

        public bool IsShip => Ship != null ||
            Type.IdH == SpaceObjectTypeIdH.Ship ||
            Type.IdH == SpaceObjectTypeIdH.ShipDerelict ||
            Type.IdH == SpaceObjectTypeIdH.SpaceStation;

        public int GetPOIs(int type) {
            if (POIs?.TryGetValue(type, out var res) == true) {
                return res;
            }
            return 0;
        }

        public void SetPOIs(int type, int cnt, bool syncKnowledge = false) {
            POIs ??= new();
            POIs[type] = cnt;
            if (syncKnowledge) {
                SyncKnowledgeFor(type);
            }
        }

        public void ChangePOIs(int type, sbyte change, bool syncKnowledge = false) {
            var cur = GetPOIs(type);
            var newVal = cur + change;
            if (newVal < 1) {
                // ship POIs are dropped after generation, so a derelict leaving would go below 0
                POIs?.Remove(type);
            } else {
                SetPOIs(type, newVal);
            }
            if (syncKnowledge) {
                SyncKnowledgeFor(type);
            }
        }

        public float TerraformingProgressFor(TerraformType t) {
            if (TerraformState == null) { return 0f; }
            if (TerraformState.TryGetValue(t, out float p)) {
                return p;
            }
            return 0f;
        }
        public void ResetLooks() {
            looks = null;
        }
        public void SetFaction(FactionDef fac) {
            FactionId = fac?.Id;
            Faction = fac;
            if (Ship != null) {
                Ship.Faction = fac;
            }
        }


        public SpaceLooks SpaceLooks() {
            var rng = new Rng(LooksSeed);
            var tweak1 = rng.Chance(0.5f);
            return new SpaceLooks{
                ColorShift1 = new Unity.Mathematics.float3() {
                    x = rng.Range(-hueRange, hueRange),
                    y = color1Sat.Evaluate(rng.Value),
                    z = tweak1 ? color1Val.Evaluate(rng.Value) : 0f,
                },
                ColorShift2 = new Unity.Mathematics.float3() {
                    x = rng.Range(-hueRange, hueRange),
                    y = color2Sat.Evaluate(rng.Value),
                    z = tweak1 ? 0 : color2Val.Evaluate(rng.Value),
                },
            };
        }

        public bool HasTerraformableLooks =>
            (Flags & (SOFlags.Terraformable | SOFlags.Settled | SOFlags.Habitable))
                != SOFlags.None;

        public string ListName => Ship?.NameFull ?? DisplayName;

        public SOLooks Looks(bool isSectorView) {
            bool isTerraformLooks = false;
            if (HasTerraformableLooks) {
                if (Type.IdH == SpaceObjectTypeIdH.Planet) {
                    isTerraformLooks = true;
                }
            } else if (Type.IdH == SpaceObjectTypeIdH.PlanetCloseup) {
                isTerraformLooks = Parent.HasTerraformableLooks;
            }
            if (looks != null) {
                if (looks.IsForTerraformable == isTerraformLooks) {
                    return ForView(isSectorView);
                }
                // A quest or finished terraforming made the planet habitable after its looks
                // were picked. Returning null here made the starmap treat the closeup as
                // constant size and scale it by the inverse of the map zoom (STA-1810)
                looks = null;
            }
            string looksTypeId;

            // if (isSectorView) {
            //     looksTypeId = "Sector";
            // }
            if (IsShip) {
                if (Ship == null) {
                    // too early - loading
                    D.Warn("Trying to get ship looks too early, ship not linked! {0}",
                        this);
                    return null;
                }
                looksTypeId = Ship.LooksTypeId;
            } else if (Type.InheritParentLooks) {
                looksTypeId = Parent.Type.Id;
            } else {
                looksTypeId = Type.Id;
            }

            if (looksTypeId == null) {
                D.Err("No looks type id resolved for space object: {0}", this);
                return null;
            }

            var rng = new Rng(LooksSeed);

            var lookTemplates = SOUtils.LoadLookTemplates(this, looksTypeId);
            weightedTemplates.Clear();
            if (lookTemplates == null) {
                return null;
            }
            foreach (var t in lookTemplates) {
                if (isTerraformLooks != t.IsForTerraformable) {
                    continue;
                }
                if (t.Probability < 0.0001f) {
                    D.Err("Probability is non-positive on space object looks: {0}", t.Id);
                    t.Probability = 1.0f;
                }
                weightedTemplates.Add(t, t.Probability);
            }

            rng.TryWeightedFrom(weightedTemplates, out var l);

            if (l == null) {
                D.Err("No looks for {0}", this);
                return null;
            //} else {
                //D.Log("Loading looks: {0} for {1}. Seed: {2}", l, this, LooksSeed);
            }

            looks = l;
            sectorLooks = null;
            return ForView(isSectorView);
        }

        // The region view draws its sectors at one fixed size, so it gets its own clone. One
        // shared cache gave whichever view asked first to the other one too: a ship asked
        // for in the region view first was drawn at that fixed size in its own sector
        private SOLooks ForView(bool isSectorView) {
            if (!isSectorView || Type.IdH == SpaceObjectTypeIdH.Sector) {
                return looks;
            }
            if (sectorLooks == null) {
                sectorLooks = SOLooks.Clone(looks);
                sectorLooks.FixedStarmapSize = 0.5f;
                sectorLooks.IsConstantSizeInStarmap = true;
            }
            return sectorLooks;
        }
        public bool HasKnowledgeOf(MatType mat) {
            if (knowledge?.Mats == null) {
                return false;
            }
            return knowledge.Mats.Contains(mat.IdH);
        }
        public bool HasKnowledgeOfPOI(int type) {
            if (knowledge?.POIs == null) {
                return false;
            }
            return knowledge.POIs.ContainsKey(type);
        }

        private string DisplayNameWithSuffix(string name) {
            return Type.NameWithType(name);
        }

        public void SetParent(SpaceObject spaceObject) {
            // D.Warn("Setting parent of {0} to {1}", this, spaceObject);
            Parent = spaceObject;
        }

        public Vector2 LocationApprox(Vector2 from) {
            if (Ship != null) {
                return Ship.NearbyPosFrom(from, out _);
            }
            float approxMagnitude = Tunables.StarmapApproxMagnitude * Mathf.Max(Type.LocationApproxMagnitude, 1f);
            var backwardsDirVector = (from - location).normalized;
            var size = Size;
            if (size < 1f) {
                size = 1f;
            }
            backwardsDirVector *= Rng.URange(0.4f, 0.6f)
                * size * approxMagnitude;
            var angle = Rng.URange(-15f, 15f);
            var target = location + Vectors.Rotate(backwardsDirVector, angle);
            return target;
        }

        public bool IsReachableFrom(Vector2 from, float closeInPct = 1f) {
            var size = Size;
            if (size < 1f) {
                size = 1f;
            }
            size *= Mathf.Max(Type.LocationApproxMagnitude, 1f);
            var reachDist = size * Tunables.StarmapApproxMagnitude * closeInPct;
            return Vectors.SqrDistance(from, location) < reachDist * reachDist;
        }
        public bool IsVisibleFrom(Vector2 from) {
            var size = Size;
            if (size < 0.01f) {
                size = 1f;
            }
            // just to add some extra
            // size *= 1.5f;
            size *= Tunables.StarmapVisualCondensation;
            var visDist = size;
            #if DEBUG_VISIBILITY
            var dist = Vectors.Distance(from, location);
            D.Warn("{0} is visible from {1}. Size: {2}. Vis Scale M: {3}, VisCond: {4}. FinalSize: {5}, Dist: {6}",
                this, from, Size, Looks().StarfieldSizeMul, Parent.Type.VisualCondensation, size, dist);
            #endif
            return Vectors.SqrDistance(from, location) < visDist * visDist;
        }

        private Dictionary<string, float> SerializeTerraformState() {
            if (TerraformState == null) { return null; }
            serializedState.Clear();
            foreach (var state in TerraformState) {
                serializedState.Add(state.Key.Id, state.Value);
            }
            return serializedState;
        }

        private void DeserializeTerraformState(Dictionary<string, float> state) {
            if (state == null || state.Count == 0) {
                TerraformState = null;
            } else {
                TerraformType.LoadKnown();
                if (TerraformType.ById == null) {
                    D.Err("No known terraform types!");
                }
                TerraformState = new();
                foreach (var s in state) {
                    if (TerraformType.ById.TryGetValue(s.Key, out var type)) {
                        TerraformState.Add(type, s.Value);
                    } else {
                        D.Err("Trying to load a terraform type that does not exist: {0}",
                            s.Key);
                    }
                }
            }
        }

        public SpaceObjectData GetData() {
            List<int> children = null;
            if (!Arrays.IsEmpty(Children)) {
                children = new(Children.Count);
                for (int i = 0; i < Children.Count; i++) {
                    SpaceObject c = Children[i];
                    children.Add(c.Id);
                }
            }
            string[] serMat = null;
            if (Mats != null) {
                serMat = new string[Mats.Count];
                for (int i = 0; i < Mats.Count; i++) {
                    MatType mat = Mats[i];
                    serMat[i] = mat.Id;
                }
            }
            string[] factionIds = null;
            if (Factions != null) {
                factionIds = new string[Factions.Count];
                for (int i = 0; i < Factions.Count; i++) {
                    FactionDef f = Factions[i];
                    factionIds[i] = f.Id;
                }
            }
            var sd = new SpaceObjectData() {
                Id = Id,
                TypeId = Type.Id,
                FactionId = FactionId,
                FactionIds = factionIds,
                Name = Name,
                Flags = Flags,
                TerraformState = SerializeTerraformState(),
                TraderId = TraderId,
                IsVisited = IsVisited,
                IsVisitedHS = IsVisitedHS,
                Layer = Layer,
                QuestId = QuestId,
                POIs = POIs,
                Knowledge = knowledge,
                ChildrenIds = children,
                Location = Location,
                MiningDifficulty = MiningDifficulty,
                MatTypes = serMat,
                Size = Size,
                MapData = MapData.Serialize(Map),
            };
            if (data?.IsEmpty == false) {
                sd.Data = data;
            }
            if (Parent != null) {
                sd.ParentId = Parent.Id;
            }
            return sd;
        }

        public bool HasMat(MatType oreType) {
            if (Arrays.IsEmpty(Mats)) { return false; }
            return Mats.Contains(oreType);
        }

        public void ResetNameCache() {
            displayName = null;
            UI?.Reload();
            OnNameChange?.Invoke(Name);
        }

        public void UpdateName(string newName) {
            // D.Warn("Renaming SO: {0} => {1}", Name, newName);
            Name = newName;
            displayName = null;
            if (Type.IdH == SpaceObjectTypeIdH.Player) {
                S.GameName = newName;
                FactionDef.Player.OverrideName(newName);
                Ship.Name = newName;
                Ship.ResetNameCache();
            }
            UI?.Reload();
            OnNameChange?.Invoke(newName);
        }

        public void SyncKnowledgeFor(int poi) {
            int cnt = GetPOIs(poi);
            if (cnt > 0) {
                Knowledge.POIs ??= new();
                Knowledge.POIs[poi] = cnt;
            } else {
                if (knowledge?.POIs?.ContainsKey(poi) == true) {
                    knowledge.POIs.Remove(poi);
                }
            }
        }

        public bool SetData(SpaceObjectData data) {
            // Children and parent data will be extracted from this
            SaveData = data;
            Id = data.Id;
            Type = SpaceObjectType.Find(data.TypeId);
            if (Type == null) {
                D.Err("Failed deserializing SO, no type: {0}, {1}",
                    data.TypeId, data.Id);
                return false;
            }
            TraderId = data.TraderId;
            var factionId = data.FactionId;
            if (factionId != null) {
                SetFaction(FactionDef.Get(factionId));
            }
            if (!Arrays.IsEmpty(data.FactionIds)) {
                Factions = new();
                foreach (var fac in data.FactionIds) {
                    var faction = FactionDef.Get(fac);
                    if (faction != null) {
                        Factions.Add(faction);
                    }
                }
            }
            Layer = data.Layer;
            Name = data.Name;
            if (Type.IdH == SpaceObjectTypeIdH.Player && Name == "Default") {
                // D.Warn("Resetting ship name from Default to {0}", S.GameName);
                Name = S.GameName;
            }
            MiningDifficulty = data.MiningDifficulty;
            if (data.MatTypes != null) {
                Mats = new List<MatType>(data.MatTypes.Length);
                foreach (var mt in data.MatTypes) {
                    var type = MatType.Get(mt);
                    if (type != null) {
                        Mats.Add(type);
                    }
                }
            }
            POIs = data.POIs;
            DropEmptyPOIs();
            knowledge = data.Knowledge;
            if (knowledge != null) {
                knowledge.SO = this;
            }
            Flags = data.Flags;
            QuestId = data.QuestId;
            this.data = data.Data;
            Location = data.Location;
            Size = data.Size;
            if (Size < 0.00001f) {
                Size = 1f;
            }
            DeserializeTerraformState(data.TerraformState);
            Map = MapData.Deserialize(data.MapData);
            return true;
        }

        // Saves from before ChangePOIs stopped at zero hold ShipDerelict -1, which kept the
        // sector from ever counting as fully scanned
        private void DropEmptyPOIs() {
            if (Arrays.IsEmpty(POIs)) { return; }
            List<int> empty = null;
            foreach (var kv in POIs) {
                if (kv.Value < 1) {
                    empty ??= new List<int>();
                    empty.Add(kv.Key);
                }
            }
            if (empty == null) { return; }
            foreach (var key in empty) {
                POIs.Remove(key);
            }
        }

        public string TreeString(string prefix) {
            var sb = new StringBuilder();
            sb.AppendFormat("\n{0}- {1}", prefix, Id);
            if (!Arrays.IsEmpty(Children)) {
                for (int i = 0; i < Children.Count; i++) {
                    SpaceObject child = Children[i];
                    sb.AppendFormat(child.TreeString($"{prefix}--"));
                }
            }
            return sb.ToString();
        }
        /// <returns>true if child didn't exist before</returns>
        public bool TryAddChild(SpaceObject c) {
            if (Children == null || !Children.Contains(c)) {
                AddChild(c);
                return true;
            }
            return false;
        }

        public bool AddChild(SpaceObject c) {
            if (c.Parent != null) {
                if (c.Parent == this) {
                    D.Err("Trying to add child to the same parent more than once! {0} -> {1}",
                        c.Parent, c);
                    return false;
                } else if (c.Parent == null) {
                    D.Err("Trying to add child that has no parent! {0} -> {1}. Parent is null: {2}",
                        c.Parent, c, c.Parent);
                    return false;
                }
                D.Err("Trying to add child that already has a parent! {0} -> {1}. Removing old parent: {2}",
                    c.Parent, c, c.Parent);
                c.Parent.RemoveChild(c);
            }
            // D.Warn("Adding child to {0} -> {1}", this, c);
            if (c == this) {
                D.Err("Trying to add self as child space object, this would lead to recursion! {0}", c);
                return false;
            }
            c.Parent = this;
            if (Children == null) {
                Children = new();
            }
            // D.Warn("Adding child: {0} to {1}", c, this);
            if (!Children.AddUniqueInefficient(c)) {
                D.Err("Trying to add child {0} to {1} more than once!", c, this);
                return false;
            }
            return true;
        }
        public bool RemoveChild(SpaceObject c) {
            // D.Warn("Removing child from {0} -> {1}", this, c);
            if (Children == null) {
                D.Err("Trying to remove child when there are no children in SO. {0} -> {1}",
                    this, c);
            }
            c.Parent = null;
            // TODO figure out why layer was being removed
            // c.Layer = UniverseLayer.Undefined;
            return Children?.Remove(c) == true;
        }
        public bool CalculateExplorationState(out int visited, out int total) {
            visited = 0;
            total = 0;
            if (Region != null) {
                if (HasChildren) {
                    foreach (var sector in Children) {
                        if (sector.IsVisited) {
                            visited++;
                        }
                        total++;
                    }
                }
                return IsVisitedHS || visited > 0;
            }
            if (Sector != null) {
                if (IsVisited && HasChildren) {
                    foreach (var child in Children) {
                        // signals, anomalies, unknown resources - never visited unless consumed
                        if (child.Type.IsConsumable) {
                            total++;
                        // space stations, ports, known resources - always visited
                        } else if (child.Type.IsAutoVisited) {
                            visited++;
                            total++;
                        }
                    }
                } else {
                    // not spawned, but we have data, nothing visited though
                    if (!Arrays.IsEmpty(SpaceEntity.Biome.Deposits)) {
                        foreach (var dep in SpaceEntity.Biome.Deposits) {
                            total += dep.Value;
                        }
                    }
                    if (!Arrays.IsEmpty(SpaceEntity.Biome.POIs)) {
                        foreach (var poi in SpaceEntity.Biome.POIs) {
                            total += poi.Value;
                        }
                    }
                    // We haven't visited, let's show a grey bar
                    if (!IsVisited && total == 0) {
                        total = 1;
                    }
                }
                return IsVisitedHS || IsVisited;
            }
            return false;
        }

        public override string ToString() {
            if (HasChildren) {
                return $"SpaceObject {Type?.Id}@{Id} [{displayName ?? Name}][{Location}][{Children.Count}]";
            } else {
                return $"SpaceObject {Type?.Id}@{Id} [{displayName ?? Name}][{Location}]";
            }
        }

        public void SetUI(UISpaceObject ui) {
            if (UI == ui) { return; }
            // D.Warn("Setting UI: {0}->{1}", UI, ui);
            UI = ui;
        }

        public void Unload() {
            S = null;
            data = null;
            customIcon = null;
            Visual = null;
            SetUI(null);
            OnNameChange = null;
            TerraformState = null;
            Children = null;
            Faction = null;
            looks = null;
            sectorLooks = null;
            knowledge = null;
            Map = null;
            SaveData = null;
            Parent = null;
            Region = null;
            Sector = null;
        }
    }
}
