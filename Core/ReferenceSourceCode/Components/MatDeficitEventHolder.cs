using Game.Constants;
using Game.Data;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Hints;

namespace Game.Components {
    public class MatDeficitEventHolder : IWithEntity {
        public MatDeficitEventHolder(Entity entity, IWithEntity owner,
                string category) {
            if (entity is Tile tile) {
                this.Tile = tile;
                S = tile.S;
            } else if (entity is Being b) {
                this.Being = b;
                S = b.S;
            }
            Category = category;
            Entity = entity;
            this.Owner = owner;
        }
        public MatDeficitEventHolder(Tile tile, IWithEntity owner,
                string category) {
            this.Tile = tile;
            Category = category;
            S = tile.S;
            Entity = tile;
            this.Owner = owner;
        }
        public MatDeficitEventHolder(Being being, IWithEntity owner,
                string category) {
            this.Being = being;
            Category = category;
            S = being.S;
            Entity = being;
            this.Owner = owner;
        }
        private readonly GameState S;
        public readonly IWithEntity Owner;
        private bool isDeficitNotified;
        public bool HasDeficit => deficitType != null;
        private MatType deficitType;
        public MatType DeficitType => deficitType;
        public readonly string Category;
        public bool IsMuted { get; private set; }

        public void SetMuted(bool muted) {
            if (IsMuted == muted) { return; }
            IsMuted = muted;
            if (muted) {
                if (isDeficitNotified) {
                    isDeficitNotified = false;
                    S.Sig.MatDeficitChanged.Send(this, false);
                }
            } else if (deficitType != null) {
                NotifyDeficit();
            }
        }

        public Entity Entity { get; private set; }
        public readonly Tile Tile;
        public readonly Being Being;

        public void Save(ComponentData data) {
            data.SetString("mat.deficit.type", deficitType?.Id);
        }
        public void Load(ComponentData data) {
            var evId = data.GetString("mat.deficit.type", null);
            if (evId != null) {
                // FastGet returns a null, which allows to change
                // refillable storage target material without
                // causing load failures, the deficit will simply be gone
                // and will be recreated when needed
                deficitType = MatType.FastGet(Hashes.S(evId));
            }
        }

        // Only call after was loaded!
        public void OnLateReady() {
            if (deficitType != null) {
                NotifyDeficit();
            }
        }

        public void Notify(MatType type, bool allowNewType = false) {
            if (isDeficitNotified) {
                if (deficitType == type) { return; }
                if (!allowNewType) {
                    D.Err("Trying to add deficit event of new type. Has {0}. New {1}. {2}",
                        deficitType, type, Tile);
                }
                Clear();
            }
            deficitType = type;
            NotifyDeficit();
        }

        public void NotifyDeficit() {
            if (IsMuted) { return; }
            if (!isDeficitNotified) {
                isDeficitNotified = true;
                if (deficitType.IdH == MatIdH.DarkMatter) {
                    S.Sys.Hint.Show(HintDarkMatter.Id, false);
                }
                S.Sig.MatDeficitChanged.Send(this, true);
            }
        }
        public void ClearIf(MatType type) {
            if (deficitType != type) { return; }
            Clear();
        }
        public void Clear() {
            if (isDeficitNotified) {
                isDeficitNotified = false;
                S.Sig.MatDeficitChanged.Send(this, false);
            }
            // D.Warn("Clearing deficit");
            deficitType = null;
        }

        public void AssertClear() {
            if (!isDeficitNotified) { return; }
            D.Err("Assertion failed - deficit event was not clear: {0} -> {1}",
                deficitType, Tile);
            Clear();
        }
    }
}
