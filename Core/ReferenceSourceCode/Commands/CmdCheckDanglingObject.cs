using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;

namespace Game.Commands {
    public class CmdCheckDanglingObject : Command {
        private readonly int pos;
        private readonly bool autoRebuild;

        public CmdCheckDanglingObject(int pos, bool autoRebuild) : base("check_dangling_obj") {
            this.pos = pos;
            this.autoRebuild = autoRebuild;
        }

        protected override void Execute() {
            if (Pos.IsOOB(pos)) { return; }
            CheckDanglingSpaceObject(Pos.S(pos), pos);
            CheckDanglingSpaceObject(Pos.E(pos), pos);
            CheckDanglingSpaceObject(Pos.W(pos), pos);
            CheckDanglingSpaceObject(Pos.N(pos), pos);
            var wall = EntityUtils.WallAt(pos);
            if (wall?.IsActive == true) {
                // wall.ClearGoal(T.AdRejTargetUnavailable);
                if (wall.IsConstructed) {
                    D.Warn("Breaking down a danging wall tile: {0}", wall);
                    wall.Constructable.BreakDown(2f, autoRebuild: autoRebuild);
                } else {
                    // check if floor underneath is pending rebuild
                    var floor = EntityUtils.FloorAt(pos);
                    if (floor == null || !floor.IsActive) {
                        D.Warn("Breaking down an unbuilt danging wall tile: {0}", wall);
                        wall.ClearGoal(T.CannotBuildFloorNotConstructedInfo);
                    }
                }
            }
            var obj = EntityUtils.ParentObjAt(pos);
            if (obj?.IsActive == true) {
                // obj.ClearGoal(T.AdRejTargetUnavailable);
                if (obj.IsConstructed) {
                    D.Warn("Breaking down a danging obj tile: {0}", obj);
                    obj.Constructable.BreakDown(2f, autoRebuild: autoRebuild);
                } else {
                    // check if floor underneath is pending rebuild
                    var floor = EntityUtils.FloorAt(pos);
                    if (floor == null || !floor.IsActive) {
                        D.Warn("Breaking down an unbuilt danging obj tile: {0}", obj);
                        obj.ClearGoal(T.CannotBuildFloorNotConstructedInfo);
                    }
                }
            }
            UpdateInSpaceAt(S, pos);
        }

        public static void UpdateInSpaceAt(GameState S, int pos) {
            if (S.Objs.ByPos.TryGetValue(pos, out var objs)) {
                for (int i = objs.Count - 1; i >= 0; i--) {
                    Obj o = objs[i];
                    o.Transform.UpdateInSpace(true);
                }
            }
            if (S.Beings.ByPos.TryGetValue(pos, out var beings)) {
                for (int i = beings.Count - 1; i >= 0; i--) {
                    Being b = beings[i];
                    b.Motion.UpdateInSpace(false);
                }
            }
        }

        private void CheckDanglingSpaceObject(int pos, int exclude) {
            var o = EntityUtils.ParentObjAt(pos);
            if (EntityUtils.IsPresent(o) && o.Constructable.Placement
                    == PlacementConstraint.InSpaceAttached) {
                var outline = o.Transform.OutlineNonDiag;
                bool found = false;
                for (int i = outline.Length - 1; i >= 0; i--) {
                    int op = outline[i];
                    if (op == exclude) { continue; }
                    if (Pos.IsOOB(op)) { continue; }
                    var floor = EntityUtils.FloorAt(op);
                    if (EntityUtils.IsPresent(floor)) {
                        D.Warn("Dangling space object saved by floor at! {0}",
                            Pos.String(op));
                        found = true;
                        break;
                    }
                }
                if (!found) {
                    D.Warn("Destroying dangling space object due to floor collapse! {0}", o);
                    o.ClearGoal(T.AdRejTargetUnavailable);
                    o.Constructable.BreakDown(2f, autoRebuild: autoRebuild);
                }
            }
        }
    }
}
