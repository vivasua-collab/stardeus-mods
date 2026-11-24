using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Visuals;
using UnityEngine;
using KL.Randomness;
using Game.Components.Upgrades;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class ShuttleComp : BaseComponent<ShuttleComp>,
            IRelocatable, IStatProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShuttleComp());
        }
        public readonly B<string> NameB = new(null);
        public string Name => NameB.Value;
        // Crew
        public int Capacity;
        public bool IsOut;
        public bool IsInUse { get; private set; }
        public bool IsAvailableForExpedition =>
            !IsInUse && !IsOut && !IsLost && Tile.IsActive;
        // A shuttle the colony can still fly some day. A deserter's keeps its tile with
        // IsLost set, and a destroyed one auto-rebuilds into a blueprint the player may never
        // fund. Cached so callers counting it don't allocate a closure.
        public static readonly Func<ShuttleComp, bool> IsUsable =
            sh => sh.IsConstructed && !sh.IsLost;

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        public void SetInUse(bool inUse) {
            Tile.SetImmutable(this, inUse && !IsLost);
            IsInUse = inUse;
            stateChangedB?.Invoke();
        }

        public bool IsLost;
        public SpaceObject SpaceObject { get; private set; }
        public void SetSO(SpaceObject so) {
            SpaceObject = so;
        }

        public int MiningPower;
        public float MiningDepth;
        private BInt miningPowerB;
        public BInt MiningPowerB => miningPowerB ??= new BInt(MiningPower);
        private BFloat miningDepthB;
        public BFloat MiningDepthB => miningDepthB ??= new BFloat(MiningDepth);

        private string graphicIn;
        private string graphicOut;
        public long LastSelectTick;

        private ShuttleFlyingComp flyingShuttle;
        private int flyingShuttleId;
        public ShuttleFlyingComp FlyingShuttle {
            get {
                if (flyingShuttle == null) {
                    if (flyingShuttleId != 0) {
                        var sh = S.Objs.Find(flyingShuttleId);
                        if (sh != null) {
                            flyingShuttle = sh.GetComponent<ShuttleFlyingComp>();
                        }
                        if (flyingShuttle == null) {
                            D.Err("Failed to restore a flying shuttle by id: {0} for {1}",
                                flyingShuttleId, Entity);
                            flyingShuttleId = 0;
                        }
                    }
                }
                return flyingShuttle;
            }
            set {
                flyingShuttle = value;
                if (value == null) {
                    flyingShuttleId = 0;
                } else {
                    flyingShuttleId = value.EntityId;
                }
            }
        }

        private Particles dustFx;

        private Vector2 GetShuttleDirection(out string sprite) {
            switch (Tile.Transform.Rotation) {
                case Facing.Type.Down: {
                    sprite = GraphicsId.ObjectsDevicesShuttleFlying_D;
                    return new(0, -1);
                }
                case Facing.Type.Up: {
                    sprite = GraphicsId.ObjectsDevicesShuttleFlying_U;
                    return new(0, 1);
                }
                case Facing.Type.Left: {
                    sprite = GraphicsId.ObjectsDevicesShuttleFlying_L;
                    return new(-1, 0);
                }
                case Facing.Type.Right: {
                    sprite = GraphicsId.ObjectsDevicesShuttleFlying_R;
                    return new(1, 0);
                }
            }
            sprite = GraphicsId.IconsColorMissing;
            return Consts.V2Zero;
        }

        public void ShowDustFX() {
            if (dustFx != null) { return; }
            dustFx = Particles.Create("ShuttleDustFX", Rng.UUint,
                entity.Position);
        }

        public void KillDustFX() {
            if (dustFx == null) { return; }
            dustFx.Play(false);
            dustFx.DestroyAfter(30);
            dustFx = null;
        }

        public Obj LaunchShuttle(bool takeoff, bool permanent = false) {
            /*
            if (Tile.Constructable.HasCancellableAd) {
                Tile.Constructable.CancelAdvert("Shuttle launching");
            }
            */
            Vector2 startPos;
            Vector2 dest;
            var w = S.GridWidth;
            var h = S.GridHeight;
            var pos = Tile.Transform.Position;
            var dir = GetShuttleDirection(out var sprite);
            var r = Tile.Transform.Rotation;
            float heightOffset;
            switch (r)  {
                case Facing.Type.Down: heightOffset = 0.33f; break;
                case Facing.Type.Up: heightOffset = 0.5f; break;
                case Facing.Type.Left: heightOffset = 0.55f; break;
                case Facing.Type.Right: heightOffset = 0.55f; break;
                default: heightOffset = 0.5f; break;
            }
            if (takeoff) {
                ShowDustFX();
                Hide(permanent);
                startPos = pos;
                dest = startPos + dir * new Vector2(w, h + 100);
            } else {
                startPos = pos + dir * new Vector2(-w, -h + 100);
                dest = pos;
            }
            startPos = new Vector2(
                Mathf.Clamp(startPos.x, 0, w - 1),
                Mathf.Clamp(startPos.y + heightOffset, 0, h - 1));
            dest = new Vector2(
                Mathf.Clamp(dest.x, 0, w),
                Mathf.Clamp(dest.y + heightOffset, 0, h));
            var obj = S.Objs.Create(startPos,
                The.Defs.Get(DefIds.ObjShuttlesShuttleFlying), false);
            obj.Graphics.OverrideGraphic(sprite);
            obj.Graphics.SetSize(new Vector2(
                Tile.Transform.RotatedWidth, Tile.Transform.RotatedHeight + 1));
            // TODO store a back reference, shuttle can be destroyed, etc
            obj.GetComponent<ShuttleFlyingComp>().Launch(
                this, dest, !takeoff, permanent, heightOffset);
            return obj;
        }

        protected override void OnConfig() {
            MiningPower = Config.GetInt(PropertyIdH.MiningPower, 1);
            MiningDepth = Config.GetFloat(PropertyIdH.MiningDepth, 2f);
            Capacity = Config.GetInt(PropertyIdH.MaxCapacity, 4);
            graphicIn = Config.GetString(PropertyIdH.GraphicIn);
            graphicOut = Config.GetString(PropertyIdH.GraphicOut);
        }

        private int[] lastCrewIds;

        public void SaveLastCrew(List<BeingData> crew) {
            if (crew == null || crew.Count == 0) {
                lastCrewIds = null;
                return;
            }
            lastCrewIds = new int[crew.Count];
            for (int i = 0; i < crew.Count; i++) {
                lastCrewIds[i] = (int) crew[i].Id;
            }
        }

        public int[] GetLastCrewIds() => lastCrewIds;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetString("Name", Name);
            data.SetLong("LastSelectTick", LastSelectTick);
            data.SetBool("IsOut", IsOut);
            data.SetBool("IsLost", IsLost);
            data.SetInt("FlyingId", flyingShuttleId);
            data.SetBool("IsInUse", IsInUse);
            var soId = (SpaceObject?.Id) ?? 0;
            data.SetInt("SpaceObjectId", soId);
            data.SetIntSet("LastCrewIds", lastCrewIds);
        }

        protected override void OnLoad(ComponentData data) {
            NameB.SetSilent(data.GetString("Name", null));
            IsOut = data.GetBool("IsOut", false);
            IsLost = data.GetBool("IsLost", false);
            LastSelectTick = data.GetLong("LastSelectTick", 0);
            flyingShuttleId = data.GetInt("FlyingId", 0);
            IsInUse = data.GetBool("IsInUse", false);
            lastCrewIds = data.GetIntSet("LastCrewIds", null);
        }
        public void Hide(bool permanent = false) {
            IsOut = true;
            if (!IsInUse) {
                Tile.SetImmutable(this, !permanent);
            }
            if (permanent) {
                IsLost = true;
            }
            // FIXME does not work
            //Tile.Light.SwitchLight(false);
            Tile.Graphics.SetGraphicId(graphicOut);
            //EntityUtils.TriggerChange(Tile);
            Tile.MaterialStorage.Hide();
            if (IsLost) {
                ShowInfoIcon(IconId.CWarning, T.Lost);
                if (SpaceObject != null) {
                    SOUtils.RemoveSO(SpaceObject);
                    SpaceObject = null;
                }
            }
        }

        public void Show() {
            if (IsLost) {
                D.Err("Trying to show a lost shuttle!");
                return;
            }
            IsOut = false;
            if (!IsInUse) {
                Tile.SetImmutable(this, false);
            }
            //Tile.Light.SwitchLight(true);
            Tile.Graphics.SetGraphicId(graphicIn);
            //EntityUtils.TriggerChange(Tile);
            Tile.MaterialStorage.Show();
        }

        public override string ToString() {
            return $" * Shuttle [{Name}]";
        }

        private void CheckInUse() {
            SetInUse(IsInUse);
        }

        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(false, true, true, this);
            if (string.IsNullOrWhiteSpace(Name)) {
                var shutCount = S.Components.CountByType<ShuttleComp>();
                NameB.SetSilent($"{T.Shuttle} #{shutCount}");
            }
            if (wasLoaded) {
                if (IsInUse) {
                    Ready.WhenGame(this, "Restore immutability", CheckInUse);
                }
                if (IsLost) {
                    Hide(true);
                }
                var soId = Data.GetInt("SpaceObjectId", 0);
                if (soId != 0) {
                    SpaceObject = S.Universe.Find(soId);
                    if (SpaceObject == null) {
                        D.Err("Shuttle failed to restore space object: {0}", soId);
                    } else {
                        SpaceObject.OnNameChange = (n) => NameB.Set(n);
                    }
                }
            }
        }

        public int Store(MatType oreType, int amt) {
            return Tile.MaterialStorage.Store(oreType, null, amt, null);
        }

        private string miningPowerText;
        public string MiningPowerText => miningPowerText
            ??= $"{Texts.Accented(T.MiningPower)}: {Units.Num(MiningPower)}\n{Texts.Accented(T.MiningDepthMax)}: {Units.Depth(MiningDepth)}";
        public void AddMiningPower(int amt) {
            MiningPower += amt;
            miningPowerText = null;
            if (MiningPower < 0) {
                MiningPower = 0;
            }
            miningPowerB?.Set(MiningPower);
        }
        public void AddMiningDepth(float depth) {
            MiningDepth += depth;
            miningPowerText = null;
            if (MiningDepth < 0) {
                MiningDepth = 0;
            }
            miningDepthB?.Set(MiningDepth);
        }


        public void RelocateTo(Entity target) {
            if (!target.TryGetComponent<ShuttleComp>(out var other)) {
                D.Err("Other relocatable has no shuttle comp! {0}", target);
                return;
            }
            other.NameB.Set(Name);
            other.lastCrewIds = lastCrewIds;
        }

        public string StatsCategoryName => T.Shuttle;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var wPower = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            MiningPowerB.Bind(life, (value) => {
                wPower.Load(IconId.CDrill, T.MiningPower, Units.Num(value));
            });

            var wDepth = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            MiningDepthB.Bind(life, (value) => {
                wDepth.Load(IconId.CDrill, T.MiningDepthMax, Units.Num(value));
            });
        }

        // The pad this shuttle is parked on can itself be part of a winched/rotated
        // section. If a shuttle is currently in flight to/from it, its cached flight
        // target needs to move with the pad or it'll miss it.
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            FlyingShuttle?.OnPadMoved(dx, dy);
        }

        public int InPort => Tile.Transform.InPort;
        public int OutPort => Tile.Transform.OutPort;

        public bool HasAutoPilot {
            get {
                var ups = Tile.Upgrades.Upgrades;
                if (Arrays.IsEmpty(ups)) { return false; }
                foreach (var u in ups) {
                    if (u == AutoPilotUpgrade.Id) {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
