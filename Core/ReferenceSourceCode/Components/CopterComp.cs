using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems.Copters;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;

namespace Game.Components {
    public sealed class CopterComp : BaseComponent<CopterComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CopterComp());
        }
        public bool IsReserved;
        public CopterData MissionData => new() {
            EntityId = entity.Id,
            Speed = speed,
            Size = Maths.Max(Tile.Transform.Width, Tile.Transform.Height),
            Sprite = Tile.Graphics.GraphicId,
        };

        public static CopterComp FirstAvailable(GameState s) {
            var copterId = s.BB.Get(BBKeyH.CopterId, 0);
            if (copterId != 0) {
                var p = s.Components.Find<CopterComp>(copterId);
                if (p?.IsConstructed == true
                        && !p.IsReserved
                        && !p.Tile.Health.IsDamaged
                        // && !p.Entity.HasCancellableAd
                        ) {
                    return p;
                }
            }
            var copter = s.Components.FindFirstMatching<CopterComp>(
                p => p.IsConstructed
                    && !p.IsReserved
                    && !p.Tile.Health.IsDamaged
                    // && !p.Entity.HasCancellableAd
                    );
            return copter;
        }

        private float speed;

        protected override void OnConfig() {
            speed = Config.GetFloat(PropertyIdH.Speed, 1.0f);
        }

        public override string ToString() {
            return " * Copter";
        }

        public override void OnSave() {
            GetOrCreateData().SetBool("IsReserved", IsReserved);
        }

        protected override void OnLoad(ComponentData data) {
            IsReserved = data.GetBool("IsReserved", false);
        }

        public static bool TrySpawnLandedCopter(GameState S, int landPos,
                Facing.Type landRot, Def def, out CopterComp spawned) {
            var proto = EntityUtils.CreatePrototype<Tile>(def);
            proto.Transform.Rotation = landRot;
            proto.Graphics.OnReady(false);
            var placeCmd = new CmdPlaceTile(landPos, proto, warn: false, true);
            placeCmd.Execute(S);
            var placed = placeCmd.PlacedTile;
            if (placed != null && placed.TryGetComponent(out spawned)) {
                return true;
            }
            D.Warn("Failed to place a copter tile at {0}, {1}",
                Pos.String(landPos), proto);
            spawned = null;
            return false;
        }

        public bool TryLiftoff(out CopterFlyingComp spawned) {
            if (CopterFlyingComp.TrySpawnFlyingCopter(S, Tile.Position, out spawned)) {
                var tileToStringBefore = Tile.ToString();
                new CmdRemoveTile(Tile).Execute(S);
                D.Warn("Copter liftoff! {0} -> {1}", tileToStringBefore, spawned);
                return true;
            }
            return false;
        }

    }
}
