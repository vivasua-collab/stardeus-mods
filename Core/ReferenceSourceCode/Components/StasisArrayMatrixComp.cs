using System.Collections;
using System.Collections.Generic;
using Game.Achievements;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public sealed class StasisArrayMatrixComp : BaseComponent<StasisArrayMatrixComp>,
            IRelocatable{
        public bool IsElysium;
        private string graphicId;
        private StasisArrayComp stasisArray;
        private Color c1;
        private Color c2;

        private float energyOutput;
        private float biomassProgress;
        private long lastBiomassTick;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new StasisArrayMatrixComp());
        }

        protected override void OnConfig() {
            graphicId = Config.GetString(PropertyIdH.Graphic);
            c1 = Colors.FromData(Config.GetIntSet(PropertyIdH.ElysiumColor));
            c2 = Colors.FromData(Config.GetIntSet(PropertyIdH.ElysiumPulsateColor));
        }

        public void EnableElysiumInstant(bool restore) {
            if (restore) {
                stasisArray.EnableElysium();
                IsElysium = true;
                ReconfigureElectricNode();
                // the look is not saved, a loaded or moved array has to get it back
                ApplyElysiumLook(Tile);
                return;
            }

            #if !STARDEUS_DEMO
            var arrays = S.Components.AllByType<StasisArrayComp>();
            if (arrays != null) {
                foreach (StasisArrayComp arr in arrays) {
                    arr.EnableElysium();
                    ApplyElysiumLook(arr.Tile);
                    var mtrx = arr.Tile.GetComponent<StasisArrayMatrixComp>();
                    mtrx.IsElysium = true;
                    mtrx.ReconfigureElectricNode();
                }
            }

            The.AchievementTracker.Unlock(AchievementElysium.AchievementId);
            S.Sig.ElysiumEnabled.Send();
            #endif
        }

        private void ApplyElysiumLook(Tile tile) {
            tile.Graphics.SetGraphicId(graphicId);
            tile.Light?.SetPulsating(c1, c2);
        }

        private void ReconfigureElectricNode() {
            if (elNode != null) {
                elNode.Node.IsConsumerHighPriority = false;
                elNode.Node.IsProducer = true;
                elNode.Node.Consumed = 0f;
                elNode.Node.Deficit = 0f;
                elNode.SetConsumerWantedInput(0f);
            }
        }

        public IEnumerator EnableElysium() {
            The.ScreenBlock.SetCenterText(T.EnablingElysium, 1);
            The.ScreenBlock.SetSideText(null);
            The.ScreenBlock.Show(3, () => EnableElysiumInstant(false));
            S.Query.DeselectAny.Ask();
            yield return Consts.Wait5Sec;
            The.ScreenBlock.Hide(3);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsElysium", IsElysium);
            data.SetFloat("BiomassProgress", biomassProgress);
        }

        protected override void OnLoad(ComponentData data) {
            IsElysium = Data.GetBool("IsElysium", IsElysium);
            biomassProgress = Data.GetFloat("BiomassProgress", 0f);
            // FIXME this is too simple? Use faction relationships
            if (IsElysium) {
                S.BB.Set(BBKeyH.ElysiumMode, true);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            stasisArray ??= Tile.GetComponent<StasisArrayComp>();
        }

        public override void PostLateReady(bool wasLoaded) {
            if (IsElysium && wasLoaded) {
                EnableElysiumInstant(true);
            }
        }

        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    if (IsElysium) {
                        ReconfigureElectricNode();
                    }
                }
            }
        }

        private void AfterTickGrid() {
            if (!IsElysium || !elNode.IsPowerable) { return; }
            var beingCount = stasisArray.BeingCount;
            energyOutput = beingCount * Tunables.ElysiumEnergyPerPerson
                    * S.BB.Get(BBKeyH.ElysiumOutputMul,
                        UnlockEffectElysiumOutput.DefaultMul);
            elNode.SetProducerMaxOutput(energyOutput);
            TickBiomass(beingCount);
        }

        /// <summary>
        /// Renders sleepers down into Protein once Biomass Harvesting is unlocked.
        /// The sleepers themselves are never consumed - Elysium keeps them alive
        /// because a dead sleeper stops producing energy.
        /// Driven off elapsed game ticks rather than call count: the grid only
        /// ticks every 10th game tick and spreads its delegates across frames,
        /// so this method is not called on a fixed cadence.
        /// </summary>
        private void TickBiomass(int beingCount) {
            if (beingCount < 1) { return; }
            if (!S.BB.Get(BBKeyH.ElysiumBiomass, false)) { return; }
            var ticks = G.Ticks;
            if (lastBiomassTick == 0L) {
                lastBiomassTick = ticks;
                return;
            }
            var elapsed = ticks - lastBiomassTick;
            lastBiomassTick = ticks;
            if (elapsed < 1L) { return; }
            if (elapsed > Consts.TicksPerHour) {
                // Loading a save or a long pause should not dump a pile of
                // Protein out in one go.
                elapsed = Consts.TicksPerHour;
            }
            biomassProgress += beingCount * elapsed
                * Tunables.ElysiumBiomassPerPersonPerDay / Consts.TicksPerDay;
            if (biomassProgress < 1f) { return; }
            biomassProgress -= 1f;
            var mat = EntityUtils.SpawnRawMaterial(MatIdH.Protein, 1,
                Tile.Transform.OutPort, 0.1f, true, true);
            if (mat != null) {
                S.Sig.MatProduced.Send(mat.Type, 1);
            }
        }

        public void RelocateTo(Entity target) {
            if (IsElysium && target.TryGetComponent<StasisArrayMatrixComp>(out var elysium)) {
                elysium.EnableElysiumInstant(true);
            }
        }
    }
}
