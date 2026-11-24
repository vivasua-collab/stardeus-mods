using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Rendering;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public sealed class CopterFlyingComp : BaseComponent<CopterFlyingComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CopterFlyingComp());
        }
        private float speed;
        private const float hoverHeight = 0.5f;

        protected override void OnConfig() {
            speed = Config.GetFloat(PropertyIdH.Speed, 1.0f);
            carryOffset = Config.GetVector2(PropertyIdH.CarryOffset, new Vector2(0, -1f));
            if (speed < 0.0001f) { speed = 1f; }
        }

        private bool hasSavedStartPos;
        private Vector2 startPos;
        private Vector2 curPos;
        private Vector2 carryOffset;
        private AnimatedSpriteSet fauxObj;
        private string fauxSpriteId;
        private Vector2 fauxSpriteScale;
        private Vector2 exitPoint = Consts.V2Zero;

        public override string ToString() {
            return $" * CopterFlying[{Obj.Id}/{Obj.Position}/{Obj.DefinitionId}]";
        }

        public bool IsNear(Vector2 pos, float minDist = 2f) {
            var closeEnough = hoverHeight * hoverHeight * minDist * minDist;
            return Vectors.SqrDistance(Obj.Transform.Position, pos) < closeEnough;
        }

        public override void OnLateReady(bool wasLoaded) {
            var rng = new Rng(EntityId);
            exitPoint = EntityUtils.RandomMapEdgePoint(rng, 1f);
            Obj.Transform.SkipMoveLoose = true;
            // Obj.IsFlying = true;
            if (wasLoaded && fauxSpriteId != null) {
                S.Clock.OnNextUpdate(() => CreateFauxSprite());
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("HasSavedStartPos", hasSavedStartPos);
            if (hasSavedStartPos) {
                data.SetFloat("StartX", startPos.x);
                data.SetFloat("StartY", startPos.y);
                data.SetFloat("CurX", curPos.x);
                data.SetFloat("CurY", curPos.y);
            }
            if (fauxObj != null) {
                data.SetString("CarrySprite", fauxSpriteId);
                data.SetFloat("CarrySpriteScaleX", fauxSpriteScale.x);
                data.SetFloat("CarrySpriteScaleY", fauxSpriteScale.y);
            }
        }

        protected override void OnLoad(ComponentData data) {
            hasSavedStartPos = data.GetBool("HasSavedStartPos", hasSavedStartPos);
            if (hasSavedStartPos) {
                var fallbackPos = S.Grid.Center;
                var sx = data.GetFloat("StartX", fallbackPos.x);
                var sy = data.GetFloat("StartY", fallbackPos.y);
                var cx = data.GetFloat("CurX", fallbackPos.x);
                var cy = data.GetFloat("CurY", fallbackPos.y);
                startPos = new Vector2(sx, sy);
                curPos = new Vector2(cx, cy);
            }
            fauxSpriteId = data.GetString("CarrySprite", null);
            if (fauxSpriteId != null) {
                var fsx = data.GetFloat("CarrySpriteScaleX", 1f);
                var fsy = data.GetFloat("CarrySpriteScaleY", 1f);
                fauxSpriteScale = new Vector2(fsx, fsy);
            }
        }

        public bool TryLand(int landPos, Facing.Type landRot, Def landedDef,
                out CopterComp spawned) {
            if (CopterComp.TrySpawnLandedCopter(S, landPos, landRot, landedDef,
                    out spawned)) {
                D.Warn("Copter landed! {0} -> {1}", Obj, spawned);
                S.Objs.Destroy(Obj);
                return true;
            }
            return false;
        }

        public static bool TrySpawnFlyingCopterOnMapEdge(GameState S,
                out CopterFlyingComp spawned) {
            var obj = S.Objs.Create(Consts.V2One,
                The.Defs.FastGet(DefIdsH.ObjCoptersCargoCopterFlying), false);
            if (obj != null && obj.TryGetComponent(out spawned)) {
                obj.Transform.TeleportTo(spawned.exitPoint);
                obj.Graphics.TeleportTo(spawned.exitPoint);
                return true;
            }
            spawned = null;
            return false;
        }

        public static bool TrySpawnFlyingCopter(GameState S, Vector2 startPos,
                out CopterFlyingComp spawned) {
            var obj = S.Objs.Create(startPos,
                The.Defs.FastGet(DefIdsH.ObjCoptersCargoCopterFlying), false);
            if (obj != null && obj.TryGetComponent(out spawned)) {
                return true;
            }
            spawned = null;
            return false;
        }
        public bool PickupDef(Def def) {
            if (def == null) {
                return false;
            }
            fauxSpriteId = def.Preview;
            EntityUtils.TileSizeFor(def, true, out var width, out var height);
            fauxSpriteScale = new Vector2(width, height);
            if (!CreateFauxSprite()) {
                return false;
            }
            return true;
        }

        public bool TryPutDown(Def def, int pos, Facing.Type rot) {
            var tile = EntityUtils.CreatePrototype<Tile>(def);
            tile.Transform.Rotation = rot;
            if (tile == null) { return false; }
            if (!CmdPlaceTile.CanPlace(pos, tile, def.LayerId, warn: false)) {
                return false;
            }
            var cmd = new CmdPlaceTile(pos, tile, warn: false, true, true);
            cmd.Execute(S);
            if (cmd.PlacedTile != null) {
                RemoveFauxObj();
                return true;
            }
            return false;
        }

        public bool PickupTile(Tile tile) {
            if (tile == null || !tile.IsActive || tile.Constructable.HasAnyGoal
                    || tile.IsPendingMove) {
                return false;
            }
            var g = tile.Graphics;
            fauxSpriteId = g.IconSD.Id;
            fauxSpriteScale = g.OriginalGraphicSize;
            if (!CreateFauxSprite()) {
                return false;
            }
            new CmdRemoveTile(tile).Execute(S);
            return true;
        }

        private bool CreateFauxSprite() {
            if (fauxObj != null) {
                D.Err("Already have a faux obj attached! {0}. Trying to attach new one: {1}",
                    fauxObj, fauxSpriteId);
                return false;
            }
            if (Obj.Graphics.SpriteSet == null) {
                D.Err("Trying to attach faux obj on a non-initialized copter sprite set!");
                return false;
            }
            if (!RenderingService.Sprites.TryFind(fauxSpriteId, out var sd)) {
                D.Err("Could not find sprite by id: {0}", fauxSpriteId);
                return false;
            }
            sd = sd.Copy();
            sd.Scale = fauxSpriteScale;
            fauxObj = new AnimatedSpriteSet(this, 1, WorldLayer.FreeEntities,
                Obj.Graphics.Position + carryOffset);

            fauxObj.SetSprite(0, sd, S);
            fauxObj.SetSortOrder(S, 0, SpriteOrder.Obj);
            fauxObj.AttachTo(Obj.Graphics.SpriteSet);
            fauxObj.AddToRenderer(S);
            return true;
        }

        public void ClearMotionTarget() {
            hasSavedStartPos = false;
        }

        public void MoveTowards(Vector2 position) {
            if (!hasSavedStartPos) {
                curPos = startPos = Obj.Position;
                hasSavedStartPos = true;
            }
            // S.Clock.PreventMultiTick();
            var posNow = curPos;
            var distLeft = Vectors.Distance(posNow, position);
            float fullDistance = Vectors.Distance(startPos, position);
            var pctToMid = Maths.PercentageTowardsMidPoint(startPos, position,
                fullDistance, distLeft);
            var acc = Maths.Clamp(pctToMid, 0.2f, 1f);
            var tpf = S.Clock.TicksPerUpdate;
            var copterShipSpeed = Tunables.CopterSpeedBaseOnShip;
            var newPos = Vector2.MoveTowards(posNow, position,
                copterShipSpeed * speed * acc * tpf);
            curPos = newPos;
            var pctDistLeft = distLeft / fullDistance;
            var height = hoverHeight + Maths.ArcHeightAt(pctDistLeft) * 3f;
            float gridH = Pos.GridH - 2f;

            newPos = new Vector2(
                Maths.Clamp(newPos.x + 0.1f * height, 0f, gridH),
                Maths.Clamp(newPos.y + height, 0f, gridH));

            Obj.Transform.TeleportTo(newPos);
            Obj.Graphics.TeleportTo(newPos);
            Obj.Graphics.SetShadowHeight(height);
        }

        public void RemoveFauxObj() {
            if (fauxObj != null) {
                fauxObj.DetachAll();
                fauxObj.RemoveFromRenderer(S);
                fauxObj = null;
            }
        }

        public SpaceObject PutOnStarmap(SpaceObject parent, Vector2 loc) {
            // If we're carrying something, we want it gone with the copter, as it
            // was flown away
            RemoveFauxObj();
            var so = S.Universe.CreateObject();
            so.Type = SpaceObjectType.Find(SpaceObjectTypeId.CargoCopter);
            so.Location = loc;
            so.Name = Entity.Definition.NameT;
            parent.AddChild(so);
            S.Sig.SpaceObjectAdded.Send(so);
            S.Objs.Destroy(Obj, true);
            return so;
        }

        public bool IsNearMapEdge() {
            return IsNear(exitPoint, 5f);
        }

        public void MoveTowardsMapEdge() {
            MoveTowards(exitPoint);
        }
    }
}
