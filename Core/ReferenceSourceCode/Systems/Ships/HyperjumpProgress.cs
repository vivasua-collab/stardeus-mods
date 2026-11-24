#if !PRODUCTION_BUILD
// #define FAST_HYPERTRAVEL
#endif
using System.Globalization;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Systems.Space;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class HyperjumpProgress : ISaveableChild {
        public ISpaceEntity From;
        public ISpaceEntity To;
        public ISpaceEntity ActualTo;
        public long StartTick;
        public long EndTick;
        public bool IsMoving;
        public string MovingInLayer;
        public float Progress;
        public bool IsMovingInUniverseLayer => IsMoving && MovingInLayer == UniverseLayer.Universe;
        public void Start(string layer, ISpaceEntity from, ISpaceEntity to, long ticks) {
            IsMoving = true;
            StartTick = ticks;
            ActualTo = to;
            MovingInLayer = layer;
            if (from.SO.Parent != to.SO.Parent) {
                // from is player sector, we'll set it to player region instead
                from = from.SO.Parent.SpaceEntity;
                if (to.SO.Parent != from.SO.Parent) {
                    // most likely a warp to a beacon in a different region
                    to = to.SO.Parent.SpaceEntity;
                }
            }
            From = from;
            To = to;
            ReconcileMovingLayerWithEndpoints();
            var dist = Vectors.Distance(from.Position, to.Position);
            // D.Warn("Distance: {0}", dist);
            var speed = Tunables.HyperjumpSpeed;
            #if FAST_HYPERTRAVEL
            speed = 20f;
            #endif
            if (speed > 0f) {
                dist /= speed;
            }
            EndTick = ticks + (int) (dist * Consts.TicksPer3Hours);
        }
        public void Clear() {
            From = null;
            To = null;
            ActualTo = null;
            StartTick = 0;
            EndTick = 0;
            Progress = 0f;
            IsMoving = false;
            MovingInLayer = null;
        }
        public void UpdateProgress(long ticks) {
            Progress = Mathf.InverseLerp(StartTick, EndTick, ticks);
        }
        public float PreciseProgress(long ticks, float tickProgress) {
            return Mathf.InverseLerp(StartTick, EndTick, ticks + tickProgress);
        }
        public Vector2 PositionAt(long ticks, float tickProgress, out float prog) {
            prog = PreciseProgress(ticks, tickProgress);
            return Vector2.Lerp(From.UIPosition, To.UIPosition, prog);
        }

        public void OnSave(ComponentData data) {
            data.SetString("Prog.Layer", MovingInLayer);
            data.SetInt("Prog.From", From?.SO.Id ?? 0);
            data.SetInt("Prog.To", To?.SO.Id ?? 0);
            data.SetInt("Prog.ActualTo", ActualTo?.SO.Id ?? 0);
            data.SetLong("Prog.StartTick", StartTick);
            data.SetLong("Prog.EndTick", EndTick);
            data.SetBool("Prog.IsMoving", IsMoving);
            data.SetFloat("Prog.Progress", Progress);
        }

        public void OnLoad(ComponentData data) {
            MovingInLayer = data.GetString("Prog.Layer", null);
            var fromId = data.GetInt("Prog.From", 0);
            if (fromId != 0) {
                if (A.S.Universe.TryFind(fromId, out var from)) {
                    From = from.SpaceEntity;
                } else {
                    D.Err("Failed to load HyperjumpProgress.From by id: {0}", fromId);
                }
            }
            var toId = data.GetInt("Prog.To", 0);
            if (toId != 0) {
                if (A.S.Universe.TryFind(toId, out var to)) {
                    To = to.SpaceEntity;
                } else {
                    D.Err("Failed to load HyperjumpProgress.To by id: {0}", toId);
                }
            }
            var actualToId = data.GetInt("Prog.ActualTo", 0);
            if (actualToId != 0) {
                if (A.S.Universe.TryFind(actualToId, out var actualTo)) {
                    ActualTo = actualTo.SpaceEntity;
                } else {
                    D.Err("Failed to load HyperjumpProgress.ActualTo by id: {0}", actualToId);
                }
            }
            StartTick = data.GetLong("Prog.StartTick", 0);
            EndTick = data.GetLong("Prog.EndTick", 0);
            IsMoving = data.GetBool("Prog.IsMoving", false);
            Progress = data.GetFloat("Prog.Progress", 0f);
            ReconcileMovingLayerWithEndpoints();
        }

        private void ReconcileMovingLayerWithEndpoints() {
            if (!IsMoving) { return; }
            if (From is SpaceRegion && To is SpaceRegion) {
                MovingInLayer = UniverseLayer.Universe;
            } else if (From is SpaceSector && To is SpaceSector) {
                MovingInLayer = UniverseLayer.Region;
            }
        }
    }
}
