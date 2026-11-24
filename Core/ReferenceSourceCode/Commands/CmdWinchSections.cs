using Game.Constants;
using Game.Commands;
using KL.Signals;
using KL.Utils;
using System.Collections;
using System.Threading.Tasks;
using System;
using Game.Data;
using UnityEngine;
using MessagePack;
using System.Text;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using KL.Grid;
using System.Collections.Generic;
using Game.Achievements;
namespace Game.Commands {
    public class CmdWinchSections : CmdMoveSectionBase {
        protected override bool isInitiatedByPlayer { get; set; }
        public const string CmdId = "winch_sections";
        private int hook;
        private Tile hookObj;
        private Tile anchorObj;
        private int from;
        private int to;
        #if PRODUCTION_BUILD
        public override bool TrySameFrame => false;
        #else
        public override bool TrySameFrame => The.TestsAreRunning && G.ClockSpeed >= ClockSpeed.Unlimited;
        #endif

        public CmdWinchSections(int hook, int anchor, bool isByPlayer) : base(CmdId) {
            this.isInitiatedByPlayer = isByPlayer;
            this.hook = hook;
            this.anchor = anchor;
            if (isByPlayer) {
                D.Warn("Ordered winch sections. Hook: {0}, Anchor: {1}",
                    Pos.String(hook), Pos.String(anchor));
            }
        }
        protected override int GetTarget() {
            return hook;
        }
        protected override IEnumerator PrepareForMove() {
            hookObj = EntityUtils.ObjAt(hook);
            anchorObj = EntityUtils.ObjAt(anchor);
            /*
            if (hookObj != null && anchorObj != null) {
                S.Sig.CameraFocusRooted.Send(hookObj, anchorObj);
            }
            */
            yield break;
        }

        protected override void AfterMove() {
            if (startIslandCount > endIslandCount) {
                D.Warn("Achievement. Winch");
                The.AchievementTracker.Unlock(
                    AchievementWinch.AchievementId);
            }
        }

        protected override HashSet<int> GetIslandAndMoveDir(out int islandId, out int x, out int y) {
            var anchorIsleId = S.Sys.Areas.IslandAt(anchor);
            var anchorIsle = S.Sys.Areas.Islands[anchorIsleId];

            var hookIsleId = S.Sys.Areas.IslandAt(hook);
            var hookIsle = S.Sys.Areas.Islands[hookIsleId];

            HashSet<int> island;

            if (hookIsle.Count >= anchorIsle.Count) {
                from = anchor;
                to = hook;
                islandId = anchorIsleId;
                island = anchorIsle;
            } else {
                from = hook;
                to = anchor;
                islandId = hookIsleId;
                island = hookIsle;
            }

            Pos.Direction(from, to, out x, out y);
            return island;
        }

        protected override void MoveAnchorAndTarget(int x, int y) {
            if (from == anchor) {
                anchor = Pos.NeighborAt(anchor, x, y);
            } else {
                hook = Pos.NeighborAt(hook, x, y);
            }
        }

        protected override bool CanMove(int x, int y) {
            if (Pos.AreAdjacent(hook, anchor)) {
                D.Log("Can't move, reached target");
                return false;
            }
            var ar = S.Sys.Areas;
            var islandA = ar.IslandAt(hook);
            var islandB = ar.IslandAt(anchor);
            if (islandA < 1 || islandB < 1) {
                D.Warn("Can't move, either hook or anchor is not an island: H: {0}, A: {1}", islandA, islandB);
                return false;
            }
            if (islandA == islandB) {
                D.Warn("Can't move, Hook and Anchor on same island: {0}", islandA);
                return false;
            }
            return true;
        }
    }
}
