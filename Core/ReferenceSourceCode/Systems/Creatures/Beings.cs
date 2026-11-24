using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Story.Events;
using Game.Systems.Story;

namespace Game.Systems.Creatures {
    public class Beings : EntityContainer<Being> {
        public readonly List<Being> Alive = new();

        public int AliveCount => Alive.Count;
        public bool HasAliveBySpecies(int spIdH) {
            for (int i = Alive.Count - 1; i >= 0; i--) {
                Being b = Alive[i];
                if (b.Species.IdH == spIdH) {
                    return true;
                }
            }
            return false;
        }
        public int CountAliveBySpecies(Species sp) {
            var c = 0;
            for (int i = Alive.Count - 1; i >= 0; i--) {
                Being b = Alive[i];
                if (b.Species == sp) {
                    c++;
                }
            }
            return c;
        }

        public bool CanPerformHarmfulActionAt(int pos) {
            if (A.HasAsimovOverride) { return true; }
            if (S.Beings.ByPos.TryGetValue(pos, out var beings)) {
                for (int i = beings.Count - 1; i >= 0; i--) {
                    Being b = beings[i];
                    if (b.Data.IsFriendly && b.Species.IsHumanoid
                            && b.IsAlive) {
                        The.Dialog.ShowSimple(IconId.CComputing,
                            T.AsimovViolation1);
                        return false;
                    }
                }
            }
            return true;
        }

        public int AllBySpecies(Species sp, Being[] res) {
            int c = 0;
            var rc = res.Length;
            for (int i = Alive.Count - 1; i >= 0; i--) {
                Being b = Alive[i];
                if (b.Species == sp) {
                    res[c++] = b;
                    if (c >= rc) { return c; }
                }
            }
            return c;
        }
        public Being Create(int posIdx, BeingData data) {
            if (posIdx < 1) {
                D.Err("Trying to create a being at pos < 1: {0}", Pos.StringD(posIdx));
            }
            return Create(Pos.ToVector(posIdx), data);
        }
        public Being Create(Vector2 pos, BeingData data) {
            // D.Warn("Creating being: {0}", data);
            if (data.Being != null) {
                if (data.Being.IsActive) {
                    D.Err("Attempting to create another active instance of Being at {0}: {1}. Existing being: {2}",
                        pos, data, data.Being);
                    return data.Being;
                } else {
                    if (All.Contains(data.Being)) {
                        D.Err("Attempting to crate a new Being while inactive copy is contained in All: {0}: {1}",
                            data.Being, data);
                        All.Remove(data.Being);
                    }
                }
            }
            // data.Faction ??= FactionDef.Player;
            var being = Create(data.Species.Def, S);
            var posIdx = Pos.FromVector(pos);
            data.LastPosition.Idx = posIdx;
            data.LastPosition.Exact = pos;
            data.SetBeing(being);
            if (!FinalizeBeing(being, posIdx, pos)) {
                return null;
            }
            S.Query.ActivateBeingData.Ask(data);
            // D.Warn("Creating being #{0}: {1}", All.Count, being);
            if (being.Data.Being != being) {
                D.Err("Being data being != being! {0} | {1} != {2}",
                    being.Data, being.Data.Being, being);
                being.Data.SetBeing(being);
            }
            being.Name.CheckState();
            return being;
        }

        private bool FinalizeBeing(Being being, int posIdx, Vector2 pos) {
            being.InvokeAfterInitialize();
            being.Graphics.RebuildLooks(true);

            being.Motion.TeleportTo(pos);
            being.Motion.TryClaimPos(posIdx);

            // if (!AddToQuadTree(being)) {
            //     D.Err("Failed to add being to quad tree! {0}", being);
            //     Destroy(being, false);
            //     return false;
            // }

            S.Sig.BeingChanged.Send(being);

            return true;
        }

        public override void Destroy() {
            Alive.Clear();
        }

        protected override void OnDestroy(Being being, bool immediately) {
            // D.Warn("removing being: {0} | {1}", being, being.Data);
            S.Query.DeselectIfSelected.Ask(being);
            Alive.Remove(being);
            being.HideAllInfo();
            if (being.HasData) {
                if (being.Data.HasStateFlag(BeingState.Alive)
                        && !being.Data.HasStateFlag(BeingState.Hidden)) {
                    being.Data.StateFlags &= ~BeingState.Alive;
                    being.Data.StateFlags |= BeingState.Dead;
                }
                S.Query.DeactivateBeingData.Ask(being.Data);
            } else {
                D.Err("Destroying a being without data! {0}", being);
            }
        }

        private readonly Dictionary<int, int> aliveCountCache = new();
        private long aliveDefCountTick;
        public int CountAliveByDef(Def def) {
            var tick = G.Ticks;
            if (tick != aliveDefCountTick) {
                aliveCountCache.Clear();
                aliveDefCountTick = tick;
                // build the cache for this tick
                for (int i = Alive.Count - 1; i >= 0; i--) {
                    var b = Alive[i];
                    if (b.IsActive && b.IsAlive) {
                        var idH = b.Definition.IdH;
                        if (aliveCountCache.TryGetValue(idH, out var c)) {
                            aliveCountCache[idH] = c + 1;
                        } else {
                            aliveCountCache.Add(idH, 1);
                        }
                    }
                }
            }
            if (aliveCountCache.TryGetValue(def.IdH, out var cnt)) {
                return cnt;
            }
            return 0;
        }

        public bool AfterMotionInitialized(Being entity) {
            return AddToQuadTree(entity);
        }
        protected override bool AddToQuadTree(Being entity) {
            // we won't add to quad tree like this
            if (!entity.HasData) {
                // D.Err("Trying to add being to quad tree before data was set! {0}", entity);
                return true;
            }
            if (entity.Motion.IsInQuadTree) {
                D.Err("Already added motion to quad tree!");
                return true;
            }
            entity.Motion.IsInQuadTree = true;
            //BeingsQuadTreeDebug.Tree = quadTree;
            if (quadTree.Insert(entity)) {
                entity.Motion.OnPositionChange += OnPositionChange;
                UpdateByPos(entity, Pos.Invalid, entity.PosIdx);
                return true;
            }
            return false;
        }

        private void UpdateByPos(Being being, int pPos, int cPos) {
            // D.Warn("Updating being by pos: {0} => {1}",
            //     Pos.StringFromTo(pPos, cPos), being);
            if (cPos == 0 && pPos < 1) {
                D.Err("Trying to update being pos to 0! {0} {1}",
                    Pos.StringFromTo(pPos, cPos), being);
            }
            if (pPos != cPos) {
                if (ByPos.TryGetValue(pPos, out var beingsPrev)) {
                    beingsPrev.Remove(being);
                }
                if (!ByPos.TryGetValue(cPos, out var beingsNow)) {
                    beingsNow = new();
                    ByPos.Add(cPos, beingsNow);
                }
                beingsNow.Add(being);
                S.Sig.BeingPosChanged.Send(being, pPos, cPos);
            }
        }

        protected void OnPositionChange(Being being, int prev, int now) {
            UpdateByPos(being, prev, now);
            quadTree.Update(being, Pos.ToVector(prev), Pos.ToVector(now));
        }

        public bool HasDeadBeingsInPos(int posIdx, Vector2? moveTo) {
            bool hasDead = false;
            for (int i = 0; i < All.Count; i++) {
                Being b = All[i];
                if (!b.IsActive) { continue; }
                if (b.Motion.Position.Idx != posIdx) { continue; }
                if (b.IsDead) {
                    hasDead = true;
                    if (moveTo.HasValue) {
                        b.Motion.TeleportTo(moveTo.Value);
                    } else {
                        break;
                    }
                }
            }
            return hasDead;
        }

        public int CountAt(int posIdx) {
            if (ByPos.TryGetValue(posIdx, out var beings)) {
                return beings.Count;
            }
            return 0;
        }
    }
}
