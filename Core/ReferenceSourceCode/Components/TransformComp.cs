using Game.Constants;
using Game.Data;
using KL.Grid;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System;

namespace Game.Components {
    /// <summary>
    /// Obj transform only. Used to be used for Beings too before, now Beings
    /// have MotionSys and MotionComp.
    /// Can't rename to not break old saves, since component name has to
    /// match the class name.
    /// </summary>
    public sealed class TransformComp : BaseComponent<TransformComp> {
        public event Action<Obj, int, int> OnPositionChange;
        public Vector2 PosV2 => position;
        public Vector2 Position {
            get => position;
            set {
                Obj.Position = position = value;
                Obj.SelectPosition = position;
                var lastPos = posIdx;
                posIdx = Pos.FromXY(value.x, value.y);
                if (posIdx != lastPos) {
                    Obj.PosIdx = posIdx;
                    Obj.CenterIdx = posIdx;
                    OnPositionChange?.Invoke(Obj, lastPos, posIdx);
                }
            }
        }

        public bool IsInSpace;
        private Vector2 position;
        public bool SkipMoveLoose;
        public int PosIdx => posIdx;
        private int posIdx = -1;
        public string Info => T.TransformAt_V1.F(Pos.String(PosIdx));

        public int CenterIdx => posIdx;


        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TransformComp());
        }

        public TransformComp() {
            SkipTypeLookupCache = true;
        }

        public override void OnRemove() {
            OnPositionChange = null;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("X", Position.x);
            data.SetFloat("Y", Position.y);
        }

        protected override void OnLoad(ComponentData data) {
            var x = data.GetFloat("X");
            var y = data.GetFloat("Y");
            Position = new(x, y);
        }

        public void UnstuckIfNeeded() {
            if (!Entity.IsActive) { return; }
            var p = PosIdx;
            if (p == 0) {
                D.Err("Unstucking while posidx == 0! Will move to posidx = 1: {0}", entity);
            }
            if (Pos.IsOOB(p)) {
                D.Err("Unstucking while pos is OOB: {0} -> {1}", Pos.String(p), entity);
                return;
            }
            var adjPosIdx = EntityUtils.AdjustObjSpawnablePos(p);
            if (adjPosIdx != p) {
                // D.Warn("Unstucking: {0}: {1}", Pos.StringFromTo(p, adjPosIdx), Entity);
                var pos = EntityUtils.CenterOf(adjPosIdx);
                TeleportTo(pos);
                Obj.Graphics.TeleportTo(pos);
            }
            UpdateInSpace(false);
        }

        public override string ToString() {
            return string.Format(" * Transform [{0}]", Position);
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            var posBefore = PosIdx;
            var positionBefore = position;
            var newPosition = positionBefore + new Vector2(dx, dy);
            TeleportTo(newPosition);
            Obj?.Graphics.TeleportTo(newPosition);
            // Being?.Graphics.TeleportTo(newPosition);
            if (posBefore != posIdx) {
                UpdateInSpace(false);
            }
            // old implementation, would not work well with plants
            //var p = EntityUtils.CenterOf(pos);
            //TeleportTo(p);
        }

        public void TeleportTo(Vector2 pos) {
            var posBefore = PosIdx;
            Position = pos;
            var posIdx = PosIdx;
            if (posBefore != posIdx) {
                UpdateInSpace(false);
            }
        }

        public void UpdateInSpace(bool force) {
            if (Pos.IsOOB(posIdx)) {
                IsInSpace = true;
            } else {
                var isInSpaceNow = EntityUtils.IsEmptySpaceAt(posIdx);
                if (force || isInSpaceNow != IsInSpace) {
                    IsInSpace = isInSpaceNow;
                    Transmit(this, MsgIdH.InSpaceChanged);
                }
            }
        }
    }
}
