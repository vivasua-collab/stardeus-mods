using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.Path;
using Game.Systems.Stats;

using Game.Utils;
using Game.Systems.UIUX;
namespace Game.Components {
    public sealed class PassabilityComp : BaseComponent<PassabilityComp>, IStatProvider {
        private const float UnbuiltDifficultyFactor = 0.5f;
        public bool IsDoorDefault;
        public bool IsDoor;
        public bool IsPassable;
        public bool IsPassableDefault;
        public bool IsWalkable;
        public bool IsWalkableDefault;
        public NavPermission Deny;
        public bool IsUnbuiltOverrideActive;

        public float Difficulty;
        private float DifficultyDefault;
        // How fast a being walks across this tile once it is built (a sandbag: slow, 1 = no
        // effect). Difficulty only makes the pathfinder avoid a tile, crossing it was full speed.
        public float MoveSpeedMul = 1f;
        private float moveSpeedMulDefault = 1f;
        // Pass-through only: beings walk across but never stop on it (a sandbag, and
        // every door). The pathfinder ends paths beside it and a being left on it steps off
        // (PathSys.TryClaimPos refuses it), so nobody sits in a doorway.
        public bool IsPassThroughOnly => IsDoor || passThroughOnly;
        private bool passThroughOnly;
        private bool passThroughOnlyDefault;
        private bool overriddenPassThroughOnly;
        private bool overriddenIsDoor;
        private bool overriddenIsPassable;
        private bool overriddenIsWalkable;
        private NavPermission overriddenDeny;
        private float overriddenDifficulty;
        private BFloat difficultyB;
        public BFloat DifficultyB => difficultyB ??= new BFloat(Difficulty);

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new PassabilityComp());
        }

        protected override void OnConfig() {
            IsDoorDefault = Config.GetBool(PropertyIdH.IsDoor, false);
            IsPassableDefault = IsPassable = Config.GetBool(PropertyIdH.IsPassable, true);
            IsWalkableDefault = IsWalkable = Config.GetBool(PropertyIdH.IsWalkable, false);
            DifficultyDefault = Difficulty = Config.GetFloat(PropertyIdH.Difficulty, 0.5f);
            moveSpeedMulDefault = MoveSpeedMul = Config.GetFloat(PropertyIdH.MoveSpeedMul, 1f);
            passThroughOnlyDefault = passThroughOnly
                = Config.GetBool(PropertyIdH.PassThroughOnly, false);
        }

        public void UpdateBuildable(bool isBuilt, float progress) {
            MoveSpeedMul = isBuilt ? moveSpeedMulDefault : 1f;
            var isPassThroughOnly = isBuilt && passThroughOnlyDefault;
            bool isDoor;
            bool isWalkable;
            bool isPassable;
            float difficulty;
            if (isBuilt) {
                isDoor = IsDoorDefault;
                isWalkable = IsWalkableDefault;
                isPassable = IsPassableDefault;
                difficulty = DifficultyDefault;
            } else {
                isDoor = false;
                isWalkable = false;
                isPassable = true;
                difficulty = UnbuiltDifficultyFactor * progress;
            }
            if (IsUnbuiltOverrideActive) {
                overriddenIsDoor = isDoor;
                overriddenIsWalkable = isWalkable;
                overriddenIsPassable = isPassable;
                overriddenDifficulty = difficulty;
                overriddenPassThroughOnly = isPassThroughOnly;
                return;
            }
            IsDoor = isDoor;
            passThroughOnly = isPassThroughOnly;
            IsWalkable = isWalkable;
            IsPassable = isPassable;
            Difficulty = difficulty;
            difficultyB?.Set(Difficulty);
        }

        public void Update(bool isPassable, bool isWalkable, float difficulty) {
            if (IsUnbuiltOverrideActive) {
                overriddenIsPassable = isPassable;
                overriddenIsWalkable = isWalkable;
                overriddenDifficulty = difficulty;
                return;
            }
            IsPassable = isPassable;
            IsWalkable = isWalkable;
            Difficulty = difficulty;
            difficultyB?.Set(Difficulty);
        }

        public bool UpdateDeny(NavPermission deny) {
            if (IsUnbuiltOverrideActive) {
                if (overriddenDeny == deny) {
                    return false;
                }
                overriddenDeny = deny;
                return true;
            }
            if (Deny != deny) {
                Deny = deny;
                S.Sig.RecalculatePassability.Send(entity.PosIdx);
                return true;
            }
            return false;
        }

        public void SetUnbuiltOverride(bool active) {
            if (IsUnbuiltOverrideActive == active) {
                return;
            }
            if (active) {
                overriddenIsDoor = IsDoor;
                overriddenIsPassable = IsPassable;
                overriddenIsWalkable = IsWalkable;
                overriddenDeny = Deny;
                overriddenDifficulty = Difficulty;
                overriddenPassThroughOnly = passThroughOnly;
                IsUnbuiltOverrideActive = true;
                IsDoor = false;
                passThroughOnly = false;
                IsPassable = true;
                IsWalkable = false;
                Deny = NavPermission.None;
                Difficulty = 0f;
            } else {
                IsUnbuiltOverrideActive = false;
                IsDoor = overriddenIsDoor;
                passThroughOnly = overriddenPassThroughOnly;
                IsPassable = overriddenIsPassable;
                IsWalkable = overriddenIsWalkable;
                Deny = overriddenDeny;
                Difficulty = overriddenDifficulty;
            }
            difficultyB?.Set(Difficulty);
        }

        public override string ToString() {
            return string.Format(" * Passability: {0}/{1}/{2}",
                IsPassable ? "p" : "x", IsWalkable ? "w" : "x", Difficulty);
        }

        public string StatsCategoryName => T.WalkingDifficulty;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (IsWalkable) {
                var walk = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, idx++);
                walk.Load(IconId.CFoot, T.PathWalkable);
            }
            if (IsPassable) {
                var fly = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, idx++);
                fly.Load(IconId.CPropulsion, T.PathFlyable);
            } else {
                var notPassable = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, idx++);
                notPassable.Load(IconId.CStop, T.PathNotPassable);
            }

            var difficulty = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
            difficulty.Load(IconId.CFoot, T.WalkingDifficulty, Difficulty);
            DifficultyB.Bind(life, (value) => {
                difficulty.SetPercent(value, false);
                difficulty.Item.SetText2(Units.Percentage(value));
            });
        }
    }
}
