#if !PRODUCTION_BUILD
//#define DEBUG_WINCH
#endif
using KL.Utils;
using System;
using Game.Data;
using UnityEngine;
using KL.Grid;
using System.Collections.Generic;
namespace Game.Commands {
    public class CmdPushSection : CmdMoveSectionBase {
        public const string CmdId = "push_section";
        private int pusher;
        private readonly Action<CmdPushSection> onDone;
        protected override bool isInitiatedByPlayer { get; set; }
        protected override bool FocusCamera => false;
        public override bool TrySameFrame => false; //!Ready.Gen || G.ClockSpeed >= ClockSpeed.Unlimited;
        private bool instant;
        protected override bool Instant => instant;

        public CmdPushSection InstantPush() {
            instant = true;
            return this;
        }

        // allowOutsideMargin is set when the section being pushed already sits outside
        // the map margin - the margin rule can't protect it any more, and clamping the
        // target back to the anchor would leave the section permanently immovable.
        public CmdPushSection(int pusher, int anchor, Vector2 target,
                Action<CmdPushSection> onDone, bool isByPlayer,
                bool allowOutsideMargin = false) : base(CmdId) {
            this.isInitiatedByPlayer = isByPlayer;
            this.onDone = onDone;
            this.pusher = pusher;
            this.anchor = anchor;
            targetPos = EntityUtils.ToPos(target);
            if (isByPlayer) {
                D.Warn("Ordered push section. Pusher: {0}, Anchor: {1}, Target: {2}",
                    Pos.String(pusher), Pos.String(anchor), Pos.String(targetPos));
                if (!allowOutsideMargin
                        && !EntityUtils.IsWithinMargin(targetPos, 1, 1, false, true)) {
                    D.Err("CmdMoveSectionBase target is outside margin: {0} ({1})",
                        targetPos, Pos.String(targetPos));
                    targetPos = anchor;
                }
            } else {
                if (!EntityUtils.IsWithinBounds(targetPos)) {
                    D.Err("CmdMoveSectionBase target is OOB: {0} ({1})",
                        targetPos, Pos.String(targetPos));
                    targetPos = anchor;
                }
            }
        }
        protected override void AfterMove() {
            onDone?.Invoke(this);
        }

        protected override void MoveAnchorAndTarget(int x, int y) {
            anchor = Pos.NeighborAt(anchor, x, y);
            pusher = Pos.NeighborAt(pusher, x, y);
        }

        protected override HashSet<int> GetIslandAndMoveDir(out int islandId, out int x, out int y) {
            islandId = S.Sys.Areas.IslandAt(anchor);
            if (S.Sys.Areas.Islands.TryGetValue(islandId, out var island)) {
                int from = pusher;
                int to = targetPos;

                Pos.Direction(from, to, out x, out y);
                return island;
            }
            x = 0;
            y = 0;
            return null;
        }

        protected override bool CanMove(int x, int y) {
            if (targetPos == pusher) {
                #if DEBUG_WINCH
                D.Log("Can't move, reached target");
                #endif
                return false;
            }
            var ar = S.Sys.Areas;
            if (ar.IslandCount != startIslandCount) {
                #if DEBUG_WINCH
                D.Warn("Can't move: island count changed");
                #endif
                if (CaptureCollision) {
                    var expanded = PosRect.Expand(LastIsland);
                    var outline = PosRect.Expand(expanded);
                    PosRect.Remove(outline, expanded);
                    LastCollision ??= new();
                    LastCollision.Clear();
                    foreach (var o in outline) {
                        if (EntityUtils.IsFloorAt(o)) {
                            LastCollision.Add(o);
                        }
                    }
                }
                return false;
            }
            return true;
        }

    }
}
