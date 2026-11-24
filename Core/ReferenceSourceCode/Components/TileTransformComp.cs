using System;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Data.Factions;
using System.Linq;

namespace Game.Components {
    public sealed class TileTransformComp : BaseComponent<TileTransformComp> {

        public const bool DefaultIsRotatable = true;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TileTransformComp());
        }
        public TileTransformComp() {
            SkipTypeLookupCache = true;
        }

        public bool IsChild;
        private int posIdx = Pos.Invalid;
        public Vector2 Size => new Vector2(RotatedWidth, RotatedHeight);
        public int PosIdx {
            get { return posIdx; }
            set {
                if (posIdx == value) { return; }
                posIdx = value;
                Tile.PosIdx = posIdx;
                Position = Tile.Position = HasSize ? EntityUtils.ToVector2(posIdx)
                    + new Vector2(RotatedWidth / 2f, RotatedHeight / 2f)
                        : EntityUtils.CenterOf(posIdx);
                Tile.SelectPosition = Position;
                if (IsMulti) {
                    Tile.CenterIdx = EntityUtils.ToPos(Position);
                } else {
                    Tile.CenterIdx = value;
                }
                if (entity != null) {
                    Transmit(this, MsgIdH.TilePosChanged);
                }
            }
        }
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            PosIdx = pos;
            outline = null;
            outlineNonDiag = null;
        }
        public int SpacingCategoryMinGap;
        public string SpacingCategory;
        public bool HasWorkSpot;
        public bool HasOutPort;
        public bool HasInPort;
        public bool HasAnyPort => HasInPort || HasOutPort || HasWorkSpot;
        public bool IsWorkSpotCompatible;
        private int workSpotOffsetX;
        private int workSpotOffsetY;
        private int outPortOffsetX;
        private int outPortOffsetY;
        private int inPortOffsetX;
        private int inPortOffsetY;
        ///<summary>Don't use it for unbuilt prototypes!!!
        ///Do RotatedWorkSpot(rootYouProvide)</summary>
        public int WorkSpot => IsChild ?
            RotatedWorkSpot(ParentPos) : RotatedWorkSpot(posIdx);
        public int OutPort => IsChild ?
            RotatedOutPort(ParentPos) : RotatedOutPort(posIdx);
        public int InPort => IsChild ?
            RotatedInPort(ParentPos) : RotatedInPort(posIdx);
        public int AnyPortInFirst => HasInPort ? InPort : (HasOutPort ? OutPort : WorkSpotOrPos);
        public int AnyPortOutFirst => HasOutPort ? OutPort : (HasInPort ? InPort : WorkSpotOrPos);
        public int WorkSpotOrPos => HasWorkSpot ? WorkSpot : PosIdx;
        public byte CoverPercent;
        public int ParentPos;
        public Tile Parent;
        public Tile Root => IsChild ? Parent : Tile;
        private Facing.Type rotation;
        public Facing.Type Rotation {
            get => rotation;
            set {
                rotation = value;
                RotatedWidth = IsRotated ? Height : Width;
                RotatedHeight = IsRotated ? Width : Height;
            }
        }
        public bool IsMulti => HasSize || IsChild;
        public bool IsRotatable;
        public bool IsRotated => IsRotatable && Facing.IsRotated(Rotation);
        public int Height;
        public int Width;
        public int RotatedWidth;
        public int RotatedHeight;
        public int TileCount;
        public PlacementType Placement = PlacementType.Single;
        public bool HasSize;
        public Facing.Type[] ValidRotations;
        // Used to be EntityUtils.CenterOf(Pos)
        public Vector2 Position { get; private set;}
        public Vector2 SelectPosition { get; private set; }
        public Vector2 SelectableSize => Size;
        public string EntityName { get; set; }

        public bool TrySetRotation(Facing.Type rot) {
            if (!IsRotatable) { return false; }
            if (ValidRotations?.Length > 0) {
                if (!ValidRotations.Contains(rot)) {
                    return false;
                }
            }
            Rotation = rot;
            return true;
        }

        public Facing.Type RandomValidRotation(Rng rng) {
            if (!IsRotatable) { return Facing.Type.Down; }
            if (ValidRotations?.Length > 0) {
                return rng.From(ValidRotations);
            } else {
                return rng.Enum<Facing.Type>();
            }
        }

        public int AdjustMultiPosBeforeRotation(int pos, sbyte dir) {
            if (!IsMulti) { return pos; }
            if (!IsChild) {
                // this is the root, we will need to know where it goes
                if (dir == 1) {
                    // this will be rotated to right, so bottom left will be top left
                    // which means that we need to return pos plus width - 1, because
                    // the bottom right cell of the tile will become the new root after rotation
                    var adjPos = Pos.NeighborAt(pos, RotatedWidth - 1, 0);
                    //D.Warn("Moving root to adjusted spot: {0} -> {1} for {2}", pos, adjPos, Tile);
                    return adjPos;
                } else if (dir == -1) {
                    // this rotated left so the adj position should be to left
                    var adjPos = Pos.NeighborAt(pos, 0, RotatedHeight - 1);
                    //D.Warn("Moving root to adjusted spot: {0} -> {1} for {2}", pos, adjPos, Tile);
                    return adjPos;
                }
            } else {
                // this is a child, is it in the way of the parent?
                if (ParentPos == Pos.Invalid) {
                    D.Err("Parent pos invalid when rotating tile.. FFUFUUUUUUUCK!!!!");
                    return pos;
                }
                if (dir == 1) {
                    var adjRootPos = Pos.NeighborAt(Parent.PosIdx, Parent.Transform.RotatedWidth - 1, 0);
                    if (pos == adjRootPos) {
                        // We are in the way! let's return parent pos;
                        //D.Warn("Moving subtile to root spot: {0} -> {1} for {2}", pos, adjRootPos, Tile);
                        return Parent.PosIdx;
                    }
                } else if (dir == -1) {
                    var adjRootPos = Pos.NeighborAt(Parent.PosIdx, 0, Parent.Transform.RotatedHeight - 1);
                    if (pos == adjRootPos) {
                        // We are in the way! let's return parent pos;
                        //D.Warn("Moving subtile to root spot: {0} -> {1} for {2}", pos, adjRootPos, Tile);
                        return Parent.PosIdx;
                    }
                }
            }
            return pos;
        }

        public bool Contains(int pos) {
            var rw = RotatedWidth;
            var rh = RotatedHeight;
            for (int x = 0; x < rw; x++) {
                for (int y = 0; y < rh; y++) {
                    if (Pos.NeighborAt(posIdx, x, y) == pos) {
                        return true;
                    }
                }
            }
            return false;
        }

        public float CellCount;

        public static TileTransformComp Decal(int pos) {
            var t = new TileTransformComp();
            t.posIdx = pos;
            t.IsRotatable = false;
            return t;
        }

        public int GetSubtilePos(int[] cache) {
            if (posIdx < 1) {
                D.Err("Invalid pos idx in GetSubtilePos! You probably wanted to call GetSubtilePos(int, int[])! {0}", Tile);
                return 0;
            }
            return GetSubtilePos(posIdx, cache);
        }

        // returns the number of positions
        public int GetSubtilePos(int pos, int[] cache) {
            if (pos < 1) {
                D.Err("Invalid pos idx in GetSubtilePos! {0}", Tile);
                return 0;
            }
            if (!IsMulti) {
                cache[0] = pos;
                return 1;
            }
            var rw = RotatedWidth;
            var rh = RotatedHeight;
            var idx = 0;
            for (int x = 0; x < rw; x++) {
                for (int y = 0; y < rh; y++) {
                    var p = Pos.NeighborAt(pos, x, y);
                    cache[idx++] = p;
                }
            }
            return idx;
        }

        // If bool returns false, we won't continue
        [Obsolete("Replace with GetSubtilePos")]
        public void ForEachMulti(Func<int, bool> callback) {
            if (IsMulti) {
                var rw = RotatedWidth;
                var rh = RotatedHeight;
                for (int x = 0; x < rw; x++) {
                    for (int y = 0; y < rh; y++) {
                        if (!callback(Pos.NeighborAt(posIdx, x, y))) {
                            return;
                        }
                    }
                }
            } else {
                callback(posIdx);
            }
        }

        private int[] outline;

        //[Obsolete("Refactor this away")]
        // TODO refactor this away
        public int[] Outline => outline ??= OutlineAtWithGarbage(posIdx);
        private int[] outlineNonDiag;

        public int[] OutlineNonDiag => outlineNonDiag ??= OutlineAtWithGarbage(posIdx, false);

        public int[] OutlineAtWithGarbage(int p, bool diag = true) {
            var count = OutlineAt(p, Caches.IntArray1024, diag);
            var res = new int[count];
            Array.Copy(Caches.IntArray1024, res, count);
            return res;
        }

        // For use in tool place tile check
        public int OutlineAt(int p, int[] result, bool diag = true) {
            var rw = RotatedWidth;
            var rh = RotatedHeight;
            int count = 0;
            for (int x = -1; x <= rw; x++) {
                for (int y = -1; y <= rh; y++) {
                    if (x == -1 || y == -1 || x == rw || y == rh) {
                        if (!diag) {
                            if ((x == -1 || x == rw) && (y == -1 || y == rh)) {
                                continue;
                            }
                        }
                        result[count++] = Pos.NeighborAt(p, x, y);
                    }
                }
            }
            return count;
        }

        public override void OnReady(bool wasLoaded) {
            // WARNING: if anything extra is added here, it will be reinvoked in Receive()!
            EntityName = entity.Definition.NameT;
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.GenDefReplaced) {
                // to reload the EntityName
                OnReady(false);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (HasAnyPort) {
                Ready.WhenPathfinding(() => {
                    S?.Sig?.PortsChanged.Send(this, true);
                });
            }
            if (LayerId == WorldLayer.Walls && Placement == PlacementType.Single) {
                if (wasLoaded) {
                    S.Clock.OnNextUpdate(AutoUpdateRotation);
                } else {
                    AutoUpdateRotation();
                }
            }
        }

        public override void OnRemove() {
            if (HasAnyPort) {
                S.Sig.PortsChanged.Send(this, false);
            }
        }

        public void AutoUpdateRotation() {
            if (LayerId != WorldLayer.Walls || Placement != PlacementType.Single) {
                return;
            }
            if (!EntityUtils.IsWithinMargin(entity.PosIdx, 1, 1)) {
                return;
            }
            // only single tiles in wall layer will auto rotate
            // by finding the neighbors
            var oldRot = Rotation;
            var wallN = EntityUtils.IsWallAt(Pos.N(entity.PosIdx));
            var wallS = EntityUtils.IsWallAt(Pos.S(entity.PosIdx));
            var wallE = EntityUtils.IsWallAt(Pos.E(entity.PosIdx));
            var wallW = EntityUtils.IsWallAt(Pos.W(entity.PosIdx));

            // Good case
            if (wallE && wallW) {
                if (oldRot != Facing.Type.Down) {
                    Rotation = Facing.Type.Down;
                    EntityUtils.TriggerChange(Tile, false);
                }
            } else if (wallN && wallS) {
                if (oldRot != Facing.Type.Right) {
                    Rotation = Facing.Type.Right;
                    EntityUtils.TriggerChange(Tile, false);
                }
            } else if (wallE || wallW) {
                if (oldRot != Facing.Type.Down) {
                    Rotation = Facing.Type.Down;
                    EntityUtils.TriggerChange(Tile, false);
                }
            } else if (wallN || wallS) {
                if (oldRot != Facing.Type.Right) {
                    Rotation = Facing.Type.Right;
                    EntityUtils.TriggerChange(Tile, false);
                }
            }
        }

        ///<summary>Use with prototypes, provide root yourself</summary>
        public Vector2 RotatedCenter(int root) {
            return EntityUtils.ToVector2(root) + Size * 0.5f;
        }
        ///<summary>Use with prototypes, provide root yourself</summary>
        public int RotatedWorkSpot(int root) {
            if (!HasWorkSpot) {
                D.Err("Trying to get work spot while we don't have one! {0}", Entity);
                return Pos.NeighborAt(root, 0, -1);
            }
            var offX = workSpotOffsetX;
            var offY = workSpotOffsetY;
            return RotatedPortPos(root, offX, offY);
        }
        public int RotatedOutPort(int root) {
            if (!HasOutPort) {
                D.Err("Trying to get out port while we don't have one! {0}", Entity);
                return Pos.NeighborAt(root, 0, -1);
            }
            var offX = outPortOffsetX;
            var offY = outPortOffsetY;
            return RotatedPortPos(root, offX, offY);
        }
        public int RotatedInPort(int root) {
            if (!HasInPort) {
                D.Err("Trying to get in port while we don't have one! {0}", Entity);
                return Pos.NeighborAt(root, 0, -1);
            }
            var offX = inPortOffsetX;
            var offY = inPortOffsetY;
            return RotatedPortPos(root, offX, offY);
        }

        private int RotatedPortPos(int root, int offX, int offY) {
            if (!IsRotatable) {
                return Pos.NeighborAt(root, offX, offY);
            }
            // If tile has height, work spot will be 1 step lower in up rot
            switch (Rotation) {
                case Facing.Type.Down:
                    return Pos.NeighborAt(root, offX, offY);
                case Facing.Type.Up:
                    return Pos.NeighborAt(root,
                    Width - 1 - offX, Height - offY - 1);
                case Facing.Type.Left:
                    return Pos.NeighborAt(root,
                    offY, Width - offX - 1);
                case Facing.Type.Right:
                    return Pos.NeighborAt(root,
                    Height - offY - 1, offX);
                default: {
                    D.Err("Unhandled facing in workspot/port: {0}", Rotation);
                    return root;
                }
            }
        }

        private int defaultCover {
            get {
                switch (entity.Definition.LayerId) {
                    case WorldLayer.Walls:
                    case WorldLayer.Objects: { return 100; }
                }
                return 0;
            }
        }

        protected override void OnConfig() {
            SpacingCategoryMinGap = Config.GetInt(PropertyIdH.SpacingCategoryMinGap, 0);
            SpacingCategory = Config.GetString(PropertyIdH.SpacingCategory, null);
            IsRotatable = Config.GetBool(PropertyIdH.IsRotatable, DefaultIsRotatable);
            CoverPercent = (byte) Config.GetInt(PropertyIdH.CoverPercent, defaultCover);
            if (Config.HasKey(PropertyIdH.WorkSpot)) {
                var wsOff = Config.GetVector2(PropertyIdH.WorkSpot);
                HasWorkSpot = true;
                workSpotOffsetX = Mathf.RoundToInt(wsOff.x);
                workSpotOffsetY = Mathf.RoundToInt(wsOff.y);
            }
            if (Config.HasKey(PropertyIdH.OutPort)) {
                var portOff = Config.GetVector2(PropertyIdH.OutPort);
                HasOutPort = true;
                outPortOffsetX = Mathf.RoundToInt(portOff.x);
                outPortOffsetY = Mathf.RoundToInt(portOff.y);
            }
            if (Config.HasKey(PropertyIdH.InPort)) {
                var portOff = Config.GetVector2(PropertyIdH.InPort);
                HasInPort = true;
                inPortOffsetX = Mathf.RoundToInt(portOff.x);
                inPortOffsetY = Mathf.RoundToInt(portOff.y);
            }

            Height = RotatedHeight = Config.GetInt(PropertyIdH.Height, 1);
            Width = RotatedWidth = Config.GetInt(PropertyIdH.Width, 1);
            if (!IsRotatable && (HasWorkSpot || Height != Width)) {
                D.Err("Invalid TileTransform: {0} is not rotatable, but either has a workspot ({1}) or Height != Width! {2}!={3}. Setting as rotatable.",
                    Tile, HasWorkSpot, Height, Width);
                IsRotatable = true;
            }
            HasSize = Height > 1 || Width > 1;
            CellCount = Width * Height;
            IsWorkSpotCompatible = Config.GetBool(PropertyIdH.IsWorkSpotCompatible, false);
            Placement = Enums.FromString<PlacementType>(
                Config.GetString(PropertyIdH.PlacementType, "Single"));

            // ValidRotations
            var validRots = Config.GetString(PropertyIdH.ValidRotations, null);
            if (!string.IsNullOrWhiteSpace(validRots)) {
                ValidRotations = new Facing.Type[validRots.Length];
                for (int i = 0; i < validRots.Length; i++) {
                    ValidRotations[i] = Facing.ConvertRotation(validRots[i]);
                }
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            if (IsRotatable) {
                data.SetInt("Rotation", (int) Rotation);
            }
            data.SetInt("PosIdx", PosIdx);
            if (IsChild) {
                data.SetInt("ParentPos", ParentPos);
            }
        }

        protected override void OnLoad(ComponentData data) {
            if (IsRotatable) {
                Rotation = (Facing.Type) data.GetInt("Rotation", 0);
            }
            PosIdx = data.GetInt("PosIdx", -1);
            ParentPos = data.GetInt("ParentPos", -1);
            if (ParentPos != -1) {
                IsChild = true;
            }
        }

        public override string ToString() {
            if (IsChild) {
                return string.Format(" * TileTransform: [{0}] (child of {1})",
                    Pos.String(PosIdx), ParentPos);
            } else {
                if (IsMulti) {
                    return string.Format(" * TileTransform: [{0}] ({1}x{2})",
                        Pos.String(PosIdx), Width, Height);
                }
                return string.Format(" * TileTransform [{0}]", Pos.String(posIdx));
            }
        }

        private FactionDef TileFaction {
            get {
                FactionDef fac = null;
                if (Tile.TryGetComponent<IWithFaction>(out var withFac)) {
                    fac = withFac.Faction;
                }
                return fac;
            }
        }

        public void RelocateToInstant(int pos, Facing.Type selectedRotation,
                Action<Tile> afterRelocate = null) {
            D.Warn("Executing instant relocation of {0} (on top of itself or sandbox) from {1} to {2}",
                Tile.Definition.Id,
                Pos.String(PosIdx), Pos.String(pos));
            var layer = entity.Definition.LayerId;
            var oldNode = Tile.ENode;
            int[] oldCons = null;
            if (oldNode != null) {
                var oldcnt = A.Grid.ConnectionsFor(EntityId, Caches.IntArray128);
                oldCons = Arrays.Copy(Caches.IntArray128, oldcnt);
            }
            Tile.Reinstallable.IsMidRelocation = true;
            new CmdRemoveTile(PosIdx, layer).Execute(S);
            A.Grid.RebuildGrid(true);
            var copy = EntityUtils.CreatePrototype<Tile>(entity.Definition.Id);
            copy.Transform.Rotation = selectedRotation;
            copy.Reinstallable.IsMidRelocation = true;
            copy.Graphics.OnReady(false);
            Action<Tile> after;
            if (oldCons != null) {
                after = (t) => {
                    afterRelocate?.Invoke(t);
                    var newNode = t.ENode;
                    if (newNode != null) {
                        foreach (var c in oldCons) {
                            var old = S.Components.Find<ElectricNodeComp>(c);
                            if (old != null) {
                                if (!A.Grid.TryConnect(newNode, old, out var failR)) {
                                    D.Warn("Failed updating conn after replacement instant: {0}", failR);
                                }
                            }
                        }
                    }
                };
            } else {
                after = afterRelocate;
            }
            S.ActQ.Enqueue(() => PlaceInstant(S, pos, copy, after));

        }

        public void EnsurePortPresence(bool requireWorkSpot, bool requireInPort,
                bool requireOutPort, IComponent forComp) {
            if (requireWorkSpot && !HasWorkSpot) {
                D.Err("{0} has no WorkSpot defined, but one is required by {1}! Fix it in TileTransform component configuration!", Tile.Definition.Id, forComp);
                HasWorkSpot = true;
                workSpotOffsetX = 0;
                workSpotOffsetY = -1;
            }
            if (requireInPort && !HasInPort) {
                D.Err("{0} has no InPort defined, but one is required by {1}! Fix it in TileTransform component configuration!", Tile.Definition.Id, forComp);
                HasInPort = true;
                if (HasOutPort) {
                    inPortOffsetX = outPortOffsetX;
                    inPortOffsetY = outPortOffsetY;
                } else if (HasWorkSpot) {
                    inPortOffsetX = workSpotOffsetX;
                    inPortOffsetY = workSpotOffsetY;
                } else {
                    inPortOffsetX = 0;
                    inPortOffsetY = -1;
                }
            }
            if (requireOutPort && !HasOutPort) {
                D.Err("{0} has no OutPort defined, but one is required by {1}! Fix it in TileTransform component configuration!", Tile.Definition.Id, forComp);
                HasOutPort = true;
                if (HasInPort) {
                    outPortOffsetX = inPortOffsetX;
                    outPortOffsetY = inPortOffsetY;
                } else if (HasWorkSpot) {
                    outPortOffsetX = workSpotOffsetX;
                    outPortOffsetY = workSpotOffsetY;
                } else {
                    outPortOffsetX = 0;
                    outPortOffsetY = -1;
                }
            }
        }

        private void PlaceInstant(GameState s, int pos, Tile copy, Action<Tile> afterRelocate) {
            // reinstall does it
            new CmdPlaceTile(pos, copy, warn: true, true, true, (t) => {
                int mpCount = t.Transform.GetSubtilePos(pos, Caches.PosIdx);
                for (int mpi = 0; mpi < mpCount; mpi++) {
                    int p = Caches.PosIdx[mpi];
                    Caches.ObjList.Clear();
                    if (s.Objs.ByPos.TryGetValue(p, out var list)) {
                        for (int i = list.Count - 1; i >= 0; i--) {
                            Obj ob = list[i];
                            ob.Transform.UnstuckIfNeeded();
                        }
                    }
                }
                ReinstallableComp.CopyRelocatable(Tile, copy);
                afterRelocate?.Invoke(t);
                if (copy.Transform.IsMulti) {
                    var posCache = Caches.IntArray128;
                    var cnt = copy.Transform.GetSubtilePos(posCache);
                    for (int i = 0; i < cnt; i++) {
                        var multiPos = posCache[i];
                        S.UpdateSection(multiPos);
                    }
                    // refresh the multi tile walkability
                    S.Sig.RecalculatePassability.Send(copy.PosIdx);
                }
            }).Execute(S);
        }
    }
}
