using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class TreadmillComp : BaseSlotsComp<TreadmillComp>,
        IWorkoutProvider, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TreadmillComp());
        }
        private const float DefaultEnergyOutput = 10f;

        // While someone runs on it
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var output = config.GetFloat(PropertyIdH.EnergyOutput, DefaultEnergyOutput);
            power.AddProduction(output, output);
        }
        public override int SlotDesignation => SlotDesignationH.Workout;
        public int WorkSpot => Tile.Transform.WorkSpot;
        private Facing.Type downRotation;
        private Vector2[] offsets;
        public override Facing.Type SpotRotation => Facing.Rotate(
            Tile.Transform.Rotation, downRotation);

        private float energyOutput;

        // A flight suit (Jet Suit, Combat Suit) makes its wearer fly, but they
        // still walk on legs: once the late game handed those suits to the whole colony,
        // nobody could use a treadmill (STA-1639)
        public override bool IsCompatibleWith(BeingData being) {
            return being.Being.Motion.IsGrounded || being.HasAbility(AbilityIdH.Move);
        }

        protected override void OnConfig() {
            energyOutput = Config.GetFloat(PropertyIdH.EnergyOutput, DefaultEnergyOutput);
            downRotation = Enums.FromString<Facing.Type>(
                Config.GetString(PropertyIdH.DownRotation));
            offsets = Config.GetVector2Set(PropertyIdH.WalkPositionOffsets);
            OnConfigSlots();
        }

        public override string ToString() {
            return " * Treadmill";
        }
        public override void OnLateReady(bool wasLoaded) {
            Tile.Light.SwitchLight(false);
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CTreadmill, T.WorkoutSlots);
            }
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CTreadmill, T.WorkoutSlots);
            }
        }

        private void AfterTickGrid() {
            if (!elNode.IsPowerable) { return; }
            if (isWorkingOut) {
                elNode.SetProducerMaxOutput(energyOutput);
            } else {
                elNode.SetProducerMaxOutput(0f);
            }
        }

        private bool isWorkingOut;

        public void BeginWorkout(Being worker) {
            worker.Graphics.SetFacing(SpotRotation);
            worker.Graphics.SetOffset(Consts.V2Zero);
            // Spot rotation is visual, transform rotation is our reference
            worker.Motion.TeleportTo(Tile.Position + offsets[(int) Tile.Transform.Rotation]);
            Tile.Light.SwitchLight(true);
            isWorkingOut = true;
        }

        public bool DoWorkout(Being worker) {
            if (!Entity.IsActive || !Tile.ENode.IsPowerable) {
                return false;
            }
            worker.Graphics.Punch(Facing.ToOffset(SpotRotation, 1f),
                0.15f, Rng.URange(0.8f, 1.2f), Rng.URange(0.8f, 1.2f));
            return true;
        }

        public void EndWorkout(Being worker) {
            Tile.Light.SwitchLight(false);
            isWorkingOut = false;
        }
    }
}
