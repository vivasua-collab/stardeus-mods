using System;
using Game.Components;
using Game.Constants;
using Game.Systems.Items;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    public class Objs : EntityContainer<Obj> {
        public Obj Create(Vector2 pos, ObjData data, bool adjustSpawnPos = true) {
            if (!EntityUtils.IsWithinBounds(pos)) {
                D.Err("Refusing to spawn {0} at pos {1} - OOB", data, pos);
                return null;
            }
            var obj = Create(data.Def, S);
            data.State = ObjState.Active;
            data.StorageId = 0;
            data.DeviceId = 0;
            return LinkObjData(ref pos, adjustSpawnPos, obj, data);
        }

        public Obj Create(Vector2 pos, Def def, bool adjustSpawnPos = true) {
            if (!EntityUtils.IsWithinBounds(pos)) {
                D.Err("Refusing to create {0} at pos {1} - OOB", def, pos);
                return null;
            }
            var obj = Create(def, S);
            var data = S.Query.GenObjFull.Ask(def, S.Rng.Fork());
            return LinkObjData(ref pos, adjustSpawnPos, obj, data);
        }

        private Obj LinkObjData(ref Vector2 pos, bool adjustSpawnPos,
                Obj obj, ObjData data) {
            if (obj == null || data == null) {
                D.Err("Trying to link obj to data when either obj is null or data is null! obj: {0} | data: {1}",
                    obj, data);
                return null;
            }
            obj.Data = data;
            obj.Data.Obj = obj;
            obj.InvokeAfterInitialize();

            var posIdx = EntityUtils.ToPos(pos);
            if (adjustSpawnPos) {
                var newPos = EntityUtils.AdjustObjSpawnablePos(posIdx);
                if (newPos != posIdx) {
                    pos = EntityUtils.CenterOf(newPos) + Rng.UInsideUnitCircle() * 0.15f;
                }
            }

            obj.Transform.TeleportTo(pos);
            obj.Graphics.TeleportTo(pos);

            S.Query.ActivateObjData.Ask(data);

            if (!AddToQuadTree(obj)) {
                D.Err("Failed to add obj to quad tree: {0}", obj);
                Destroy(obj, false);
                return null;
            }

            return obj;
        }

        protected override void OnDestroy(Obj obj, bool immediately) {
            if (obj.Data == null) {
                D.Err("Obj data is null OnDestroy! {0}", obj);
                return;
            }
            if (obj.Data.State == ObjState.Active) {
                S.Query.DeactivateObjData.Ask(obj.Data, ObjState.Destroyed);
            } else {
                // we're changing the state into something like equipped or stored
                S.Query.DeactivateObjData.Ask(obj.Data, obj.Data.State);
            }
        }

        protected override bool AddToQuadTree(Obj entity) {
            if (quadTree.Insert(entity)) {
                entity.Transform.OnPositionChange += OnPositionChange;
                UpdateByPos(entity, Pos.Invalid, entity.PosIdx);
                return true;
            }
            return false;
        }

        private void UpdateByPos(Obj obj, int pPos, int cPos) {
            if (pPos != cPos) {
                if (ByPos.TryGetValue(pPos, out var objsPrev)) {
                    objsPrev.Remove(obj);
                }
                if (!ByPos.TryGetValue(cPos, out var objsNow)) {
                    objsNow = new();
                    ByPos.Add(cPos, objsNow);
                }
                objsNow.Add(obj);
            }
        }

        protected void OnPositionChange(Obj obj, int prev, int now) {
            // ByPos and quad tree have to stay truthful when an obj leaves or re-enters
            // the map. A stale entry makes section moves relocate objs that are not
            // there anymore, and Destroy skips unregistering objs that are OOB.
            if (Pos.IsOOB(now)) {
                if (!Pos.IsOOB(prev)) {
                    if (ByPos.TryGetValue(prev, out var objsPrev)) {
                        objsPrev.Remove(obj);
                    }
                    quadTree.Remove(obj, Pos.ToVector(prev));
                }
                return;
            }
            UpdateByPos(obj, prev, now);
            if (Pos.IsOOB(prev)) {
                quadTree.Insert(obj, Pos.ToVector(now));
            } else {
                quadTree.Update(obj, Pos.ToVector(prev), Pos.ToVector(now));
            }
        }
    }
}
