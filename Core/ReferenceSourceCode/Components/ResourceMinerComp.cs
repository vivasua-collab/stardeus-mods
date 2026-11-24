using Game.Data;
using Game.Systems.Copters;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Utils;

namespace Game.Components {
    public sealed class ResourceMinerComp : BaseComponent<ResourceMinerComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ResourceMinerComp());
        }

        public float AutoMineSpeed;
        public float AutoMineMaxDepth;
        public float AutoMineEfficiency;
        public int StorageCapacity;
        public bool IsReserved;

        public ResourceMinerData MissionData => new() {
            EntityId = entity.Id,
            Efficiency = AutoMineEfficiency,
            Speed = AutoMineSpeed,
            MaxDepth = AutoMineMaxDepth,
            Storage = StorageCapacity,
            MinerDefId = entity.DefinitionId,
            PosOnShip = Tile.PosIdx,
            RotOnShip = Tile.Transform.Rotation,
            Size = Maths.Max(Tile.Transform.Width, Tile.Transform.Height),
            Sprite = MinerSprite,
        };
        private string MinerSprite {
            get {
                var sprite = MapRenderer.Resolver.ResolveRotatableTile(
                    Tile, Tile.Transform.Rotation);
                return sprite?.Id ?? Tile.Graphics.IconSD.Id;
            }
        }

        /// <summary>
        /// Make sure to set miner.IsReserved = true if the miner gets
        /// selected for a mission
        /// </summary>
        public static ResourceMinerComp FirstAvailable(GameState s) {
            var minerId = s.BB.Get(BBKeyH.MinerId, 0);
            if (minerId != 0) {
                var p = s.Components.Find<ResourceMinerComp>(minerId);
                if (p?.IsConstructed == true && !p.IsReserved
                        && !p.Tile.Health.IsDamaged
                        // && !p.Entity.HasCancellableAd
                        ) {
                    return p;
                }
            }
            var miner = s.Components.FindFirstMatching<ResourceMinerComp>(
                p => p.IsConstructed && !p.IsReserved
                    && !p.Tile.Health.IsDamaged
                    // && !p.Entity.HasCancellableAd
                    );
            return miner;
        }

        public override void OnSave() {
            GetOrCreateData().SetBool("IsReserved", IsReserved);
        }

        protected override void OnLoad(ComponentData data) {
            IsReserved = data.GetBool("IsReserved", false);
        }

        protected override void OnConfig() {
            AutoMineSpeed = Config.GetFloat(PropertyIdH.Speed, 1.0f);
            AutoMineMaxDepth = Config.GetFloat(PropertyIdH.MiningDepth, 10f);
            AutoMineEfficiency = Config.GetFloat(PropertyIdH.Efficiency, 1.0f);
            StorageCapacity = Config.GetInt(PropertyIdH.MaxCapacity, 2000);
        }
        public override string ToString() {
            return " * Resource Miner";
        }
    }
}
