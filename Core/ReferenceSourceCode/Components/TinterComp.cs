using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.UIUX;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public sealed class TinterComp : BaseComponent<TinterComp>,
            IRelocatable, IMatRequester,
            IAIGoalProvider, IAIGoalPriorityListener, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TinterComp());
        }

        private const float DefaultEnergyCost = 10f;
        private float energyCost;
        private MatDeficitEventHolder deficitEv;

        public float ColorCapacity;
        public float ColorLevelR;
        public float ColorLevelG;
        public float ColorLevelB;
        private float refillThreshold;
        private int refillPriority;

        protected override void OnConfig() {
            deficitEv = new MatDeficitEventHolder(Tile, this, T.ResearchPaint);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            ColorCapacity = Config.GetFloat(PropertyIdH.MaxCapacity, 1f);
            refillThreshold = 0.2f;
            refillPriority = 3;
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        private bool isAdded;
        public bool TryConsumePigmentFor(ref float pr, ref float pg, ref float pb) {
            if (!IsTinterActive) { return false; }
            var consumed = TryConsume(ref pr, ref ColorLevelR);
            consumed    |= TryConsume(ref pg, ref ColorLevelG);
            consumed    |= TryConsume(ref pb, ref ColorLevelB);
            return consumed;
        }

        private bool TryConsume(ref float pigment, ref float color) {
            if (color > 0f && pigment > 0f) {
                if (color <= pigment) {
                    pigment -= color;
                    color = 0;
                } else {
                    color -= pigment;
                    pigment = 0;
                }
                return true;
            }
            return false;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("LevelR", ColorLevelR);
            data.SetFloat("LevelG", ColorLevelG);
            data.SetFloat("LevelB", ColorLevelB);
            data.SetInt("RefillPriority", refillPriority);
            data.SetFloat("RefillThreshold", refillThreshold);
            deficitEv.Save(data);
        }

        protected override void OnLoad(ComponentData data) {
            ColorLevelR = data.GetFloat("LevelR", 0f);
            ColorLevelG = data.GetFloat("LevelG", 0f);
            ColorLevelB = data.GetFloat("LevelB", 0f);
            refillPriority = data.GetInt("RefillPriority", refillPriority);
            refillThreshold = data.GetFloat("RefillThreshold", refillThreshold);
            deficitEv.Load(data);
        }

        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(false, true, false, this);
            if (!isAdded) {
                isAdded = true;
                S.Sys.Tint.AddTinter(this);
            }
            if (wasLoaded) {
                deficitEv.OnLateReady();
            }
        }

        public override void OnRemove() {
            if (isAdded) {
                isAdded = false;
                S.Sys.Tint.RemoveTinter(this);
            }
            deficitEv.Clear();
        }

        public override string ToString() {
            return " * Tinter";
        }

        public float MaxConsumption => energyCost;

        public int InPort => Tile.Transform.InPort;

        public List<MatRequest> MissingMats { get; } = new();

        public MatRequest NextMissingMat {
            get {
                if (Arrays.IsEmpty(MissingMats)) { return null; }
                for (int i = 0; i < MissingMats.Count; i++) {
                    var r = MissingMats[i];
                    if (r.Amount > 0) {
                        return r;
                    }
                }
                return null;
            }
        }

        public bool IsTinterActive;
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    elnode.SetConsumerWantedInput(energyCost);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                ColorLevelR = ColorLevelB = ColorLevelG = ColorCapacity;
            }
        }

        private void SetTinterActive(bool active) {
            if (IsTinterActive != active) {
                IsTinterActive = active;
                S.Sys.Tint.UpdateUIBlock(true);
            }
        }

        private void AfterTickGrid() {
            if (elNode.IsConsuming) {
                SetTinterActive(true);
                CheckPigmentLevels();
            } else {
                SetTinterActive(false);
            }
        }

        private int wantR;
        private int wantG;
        private int wantB;

        // returns true if levels are ok
        private bool CheckPigmentLevels() {
            float wR = ColorCapacity - ColorLevelR;
            float wG = ColorCapacity - ColorLevelG;
            float wB = ColorCapacity - ColorLevelB;
            float thresh = ColorCapacity * (1f - refillThreshold);
            wantR = 0;
            wantG = 0;
            wantB = 0;
            if (wR > thresh) {
                wantR = Mathf.CeilToInt(wR);
            }
            if (wG > thresh) {
                wantG = Mathf.CeilToInt(wG);
            }
            if (wB > thresh) {
                wantB = Mathf.CeilToInt(wB);
            }
            if (wantR != 0 || wantG != 0 || wantB != 0) {
                if (!HasAnyGoal) {
                    var g = CreateGoal(JobTypeIdH.Hauling, IconId.CHaul,
                        T.HaulIngredients, this)
                            .WithWorldToDesired(AIVarsH.IsResourcesDelivered, false, true)
                            .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                            .NanobotCompatible()
                            .WriteInitialState();
                    entity.TrySetGoal(g);
                }
                UpdateMatReq();
                return false;
            }
            HideInfoIcon();
            return true;
        }

        private void UpdateMatReq() {
            if (MissingMats.Count == 0) {
                MissingMats.Add(new MatRequest() {
                    Type = MatType.Get(MatId.PigmentRed),
                    Amount = wantR,
                    IsAmountOptional = true,
                    Requester = this,
                });
                MissingMats.Add(new MatRequest() {
                    Type = MatType.Get(MatId.PigmentGreen),
                    Amount = wantG,
                    IsAmountOptional = true,
                    Requester = this,
                });
                MissingMats.Add(new MatRequest() {
                    Type = MatType.Get(MatId.PigmentBlue),
                    Amount = wantB,
                    IsAmountOptional = true,
                    Requester = this,
                });
            } else {
                MissingMats[0].Amount = wantR;
                MissingMats[1].Amount = wantG;
                MissingMats[2].Amount = wantB;
            }
            if (wantR == 0) {
                deficitEv.ClearIf(MissingMats[0].Type);
            }
            if (wantG == 0) {
                deficitEv.ClearIf(MissingMats[1].Type);
            }
            if (wantB == 0) {
                deficitEv.ClearIf(MissingMats[2].Type);
            }
        }

        private BCallback uiChanged;
        public BCallback UIChanged => uiChanged ??= new BCallback();

        private void NotifyUIChanged() {
            uiChanged?.Invoke();
        }

        public int RefillPriority => refillPriority;
        public float RefillThreshold => refillThreshold;

        public void AdjustRefillPriority(int direction) {
            refillPriority = UIUtils.AdjustPriority0To5(refillPriority, direction);
            if (TryGetGoal(out var g)) {
                g.WithPriority(refillPriority);
            }
        }

        public void SetRefillThreshold(float value) {
            refillThreshold = value;
            CheckPigmentLevels();
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<TinterComp>(out var other)) {
                other.ColorLevelR = ColorLevelR;
                other.ColorLevelG = ColorLevelG;
                other.ColorLevelB = ColorLevelB;
            }
        }

        public bool ProvideRequestedMat(UnstoredMatComp mat, Being worker,
                AIGoal goal = null) {
            var took = mat.Take(mat.Type, null, mat.StackSize,
                goal ?? worker?.AIAgent?.Goal ?? entity.Goal);
            if (mat.Type.IdH == MatIdH.PigmentRed) {
                if (took > 0) {
                    ColorLevelR += took;
                    if (ColorLevelR > ColorCapacity) {
                        ColorLevelR = ColorCapacity;
                    }
                }
            } else if (mat.Type.IdH == MatIdH.PigmentGreen) {
                if (took > 0) {
                    ColorLevelG += took;
                    if (ColorLevelG > ColorCapacity) {
                        ColorLevelG = ColorCapacity;
                    }
                }
            } else if (mat.Type.IdH == MatIdH.PigmentBlue) {
                if (took > 0) {
                    ColorLevelB += took;
                    if (ColorLevelB > ColorCapacity) {
                        ColorLevelB = ColorCapacity;
                    }
                }
            } else {
                D.Err("Took an unknown mat. Wanted a pigment! {0}", mat.Type);
            }
            if (took > 0) {
                S.Sig.MatConsumed.Send(mat.Type, took);
                S.Sys.Tint.UpdateUIBlock(true);
                NotifyUIChanged();
            }
            if (CheckPigmentLevels()) {
                UpdateMatReq();
                return true;
            }
            return false;
        }

        public void NotifyMatDeficit(MatType type) {
            deficitEv.Notify(type);
        }

        public void ClearLackNotification(MatType type) {
            deficitEv.ClearIf(type);
        }

        public void OnAIGoalPriorityChange(AIGoal goal) {
            refillPriority = goal.GoalPriority;
            NotifyUIChanged();
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
            if (AIGoal.IsActive(goal)) {
                UpdateMatReq();
            }
        }
    }
}
