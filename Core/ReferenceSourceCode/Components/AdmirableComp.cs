using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using Game.Systems.Reg;
using KL.Grid;
using KL.Utils;

namespace Game.Components {
    public sealed class AdmirableComp : BaseComponent<AdmirableComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new AdmirableComp());
        }

        public int AdmirePosIdx(AIAgentComp agent) {
            if (Tile != null) {
                if (Tile.Transform.HasWorkSpot) {
                    return Tile.Transform.WorkSpot;
                }
                if (Tile.Transform.IsMulti) {
                    return Pos.Invalid;
                }
                if (Tile.Definition.LayerId == WorldLayer.Floor) {
                    return Tile.PosIdx;
                }
            }
            var pos = Entity.PosIdx;
            var p = Pos.S(pos);
            if (CanAdmireAt(p, agent)) { return p; }
            // don't always start admiring the object from the same direction
            if (EntityId % 2 == 0) {
                p = Pos.E(pos);
                if (CanAdmireAt(p, agent)) { return p; }
                p = Pos.W(pos);
                if (CanAdmireAt(p, agent)) { return p; }
            } else {
                p = Pos.W(pos);
                if (CanAdmireAt(p, agent)) { return p; }
                p = Pos.E(pos);
                if (CanAdmireAt(p, agent)) { return p; }
            }
            p = Pos.N(pos);
            if (CanAdmireAt(p, agent)) { return p; }
            return Pos.Invalid;
        }

        private bool CanAdmireAt(int pos, AIAgentComp agent) {
            if (!EntityUtils.IsEmptyFloorAt(pos, agent.Motion.IsFlying)) {
                return false;
            }
            if (S.Sys.Path.IsClaimed(pos, agent.Being.Id)) {
                return false;
            }
            return agent.IsFeelingSafeAt(pos);
        }

        public string AdmiringName(bool presentTense) {
            if (Entity.Definition.LayerId < WorldLayer.Objects) {
                if (presentTense) {
                    return T.VerbAdmiringView;
                } else {
                    return T.VerbAdmiredView;
                }
            } else {
                if (presentTense) {
                    return T.ActAdmiringObject_V1.F(EntityUtils.KnownName(Entity));
                } else {
                    return T.ActAdmiredObject_V1.F(EntityUtils.KnownName(Entity));
                }
            }
        }

        public string AdmireIcon {
            get {
                if (Entity.Definition.LayerId < WorldLayer.Objects) {
                    return IconId.CBinoculars;
                } else {
                    return IconId.CMagnifier;
                }
            }
        }

        public bool IsAdmirableNow {
            get {
                if (Tile?.IsConstructed == false) {
                    return false;
                } else if (Being?.IsDead == true) {
                    return false;
                }
                return true;
            }
        }

        public bool CanSafelyAdmire(AIAgentComp agent) {
            return CanSafelyAdmire(agent, S.Sys.Path.Reg.Reach.For(agent.Motion));
        }

        // reach: the admirer's BeingReach, taken once per search
        public bool CanSafelyAdmire(AIAgentComp agent, ReachQuery reach) {
            if (!IsAdmirableNow) { return false; }
            if (Tile != null) {
                if (Tile.Definition.LayerId == WorldLayer.Walls) {
                    var pos = Entity.PosIdx;
                    return CanSafelyAdmireWallAt(Pos.N(pos), agent, reach)
                            || CanSafelyAdmireWallAt(Pos.S(pos), agent, reach)
                            || CanSafelyAdmireWallAt(Pos.W(pos), agent, reach)
                            || CanSafelyAdmireWallAt(Pos.E(pos), agent, reach);
                }
            }
            if (!reach.CanReach(Entity.PosIdx)) {
                return false;
            }
            return agent.IsFeelingSafeAt(Entity.PosIdx);
        }

        private bool CanSafelyAdmireWallAt(int pos, AIAgentComp agent, ReachQuery reach) {
            if (!EntityUtils.IsEmptyFloorAt(pos, agent.Motion.IsFlying)) {
                return false;
            }
            if (!reach.CanReach(pos)) {
                return false;
            }
            return agent.IsFeelingSafeAt(pos);
        }

        public int Admirability;
        public float FunMultiplier;
        public float Chance;
        public bool IsWindow;
        // Groups variants that are the same thing to admire (e.g. white and black knight)
        public int FunSubjectIdH;
        private PlanterComp planter;
        private bool isPlanterResolved;
        // A vase is admired for the plant growing in it
        public PlanterComp Planter {
            get {
                if (!isPlanterResolved) {
                    isPlanterResolved = true;
                    Entity.TryGetComponent(out planter);
                }
                return planter;
            }
        }

        protected override void OnConfig() {
            Admirability = Config.GetInt(PropertyIdH.Amount, 1);
            FunMultiplier = Config.GetFloat(PropertyIdH.Multiplier, 1f);
            // how likely a mood boost will happen
            Chance = Config.GetFloat(PropertyIdH.Chance, 0.5f);
            IsWindow = Config.GetBool(PropertyIdH.IsWindow, false);
            var funSubject = Config.GetString(PropertyIdH.FunSubject, null);
            FunSubjectIdH = string.IsNullOrWhiteSpace(funSubject) ? 0 : Hashes.S(funSubject);
        }

        public override string ToString() {
            return $" * Admirable [{Admirability}]";
        }

    }
}
