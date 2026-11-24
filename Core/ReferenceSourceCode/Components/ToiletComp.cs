using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using UnityEngine;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class ToiletComp : BaseSlotsComp<ToiletComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ToiletComp());
        }
        public override int SlotDesignation => SlotDesignationH.Toilet;
        private CleanableComp cleanable;
        public override bool IsAvailable {
            get {
                if (!base.IsAvailable) { return false; }
                if (cleanable != null && cleanable.IsFull) {
                    if (!cleanable.HasAnyGoal) {
                        cleanable.CreateCleaningGoal(false);
                    }
                    return false;
                }
                return true;
            }
        }
        private bool isForHumans;
        private bool isForPets;
        public override bool IsCompatibleWith(BeingData data) {
            if (isForHumans && !data.Mind.Species.IsHumanoid) {
                // robots will be able to use it
                return !data.Mind.Species.IsBiological;
            }
            if (isForPets && !data.Mind.Species.IsAnimal) {
                return false;
            }
            return true;
        }

        protected override void OnConfig() {
            OnConfigSlots();
            isForHumans = Config.GetBool(PropertyIdH.IsForHumans, false);
            isForPets = Config.GetBool(PropertyIdH.IsForPets, false);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CToilet, T.Toilet);
            }
            cleanable = Entity.GetComponent<CleanableComp>();
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        public override string ToString() {
            return " * Toilet";
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                DumpShit();
            } else if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CToilet, T.Toilet);
            }
        }

        private void DumpShit() {
            if (cleanable == null) { return; }
            var pos = Tile.Transform.PosIdx;
            if (Tile.Transform.HasWorkSpot) {
                pos = Tile.Transform.OutPort;
            }
            var dp = cleanable.DirtAmount;
            if (dp == 0) { dp = 1; }
            int left = Mathf.CeilToInt(dp * 3);
            Pos.ForEachAroundRandom(Rng.Unseeded, pos, 3, true, true, (p) => {
                S.Sys.Dirt.Add(DirtData.Create(p, Images.RandomMatching(
                    GraphicsId.DirtHumanWaste01, 2), Rng.URange(0.75f, 1.25f)));
                return left-- > 0;
            });
        }

        // one use is 30 minutes, it will be fully overflowing after X uses
        // it was 10 dirt capacity, now it's 3, thus 0.3f
        private const float dirtAccumulationPerTick = 0.3f / Consts.TicksPer2Hours;

        public override bool Use(BeingData data, Slot slot) {
            if (cleanable == null) {
                return true;
            }
            if (hasOverflow && !cleanable.IsFull) {
                hasOverflow = false;
            }
            var excess = cleanable.AddDirt(dirtAccumulationPerTick);
            if (excess > 0f) {
                TriggerOverflow();
                return false;
            }
            return true;
            //UpdateUIBlock(true); ?
        }

        private bool hasOverflow;

        private void TriggerOverflow() {
            if (!hasOverflow) {
                hasOverflow = true;
                S.Sys.Log.AddLine(Tile, T.ToiletOverflowing);
                DumpShit();
            }
        }
    }
}
