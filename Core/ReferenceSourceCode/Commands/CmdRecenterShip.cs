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
using Game.Components;
using KL.Collections;
using System.Linq;
namespace Game.Commands {
    public class CmdRecenterShip : CmdMoveSectionBase {
        protected override bool isInitiatedByPlayer { get; set; }
        public const string CmdId = "recenter_ship";
        #if PRODUCTION_BUILD
        public override bool TrySameFrame => false;
        #else
        public override bool TrySameFrame => The.TestsAreRunning && G.ClockSpeed >= ClockSpeed.Unlimited;
        #endif

        protected override bool FocusCamera => false;
        protected override bool Instant => true;
        // A silent jump, not a ramming: if anything is in the way, stay put
        protected override bool BreaksFloorsInPath => false;

        public CmdRecenterShip(int shipCenter) : base(CmdId) {
            isInitiatedByPlayer = false;
            anchor = shipCenter;
        }

        public static bool TryFindShipCenter(GameState s, out int center) {
            var areas = s.Sys.Areas;
            if (!areas.TryGetShipIsland(out var islandId)) {
                center = Pos.Invalid;
                return false;
            }
            center = Pos.Encapsulate(areas.Islands[islandId]).CenterPos;
            return true;
        }
        protected override int GetTarget() {
            return S.Grid.CenterPos;
        }
        protected override IEnumerator PrepareForMove() {
            yield break;
        }

        protected override void AfterMove() {
            EntityUtils.CameraFocusOnShip();
        }

        protected override HashSet<int> GetIslandAndMoveDir(out int islandId, out int x, out int y) {
            var areas = S.Sys.Areas;
            if (!areas.TryGetShipIsland(out islandId)) {
                x = 0;
                y = 0;
                return null;
            }
            var ship = areas.Islands[islandId];
            var isleCenter = Pos.Encapsulate(ship).CenterPos;
            // The instant jump is measured from the anchor, so it has to be the center of the
            // island that actually moves, whatever changed since the command was queued
            anchor = isleCenter;
            Pos.Delta(isleCenter, S.Grid.CenterPos, out x, out y);
            return ship;
        }

        protected override void MoveAnchorAndTarget(int x, int y) {
            anchor = Pos.NeighborAt(anchor, x, y);
        }

        protected override bool CanMove(int x, int y) {
            if (S.Grid.CenterPos == anchor) {
                D.Log("Can't move, reached target");
                return false;
            }
            return true;
        }
    }
}
