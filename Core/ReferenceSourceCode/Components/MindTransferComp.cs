using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using Game.Systems.Items;
using KL.Utils;
using Game.Systems.Mental;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class MindTransferComp : BaseComponent<MindTransferComp>,
            IAIGoalProvider, ILoadsObj, IRelocatable, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MindTransferComp());
        }

        private const int DefaultEnergyCost = 50;
        private int energyCost;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetInt(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }
        public int DoneCount;
        public uint ReservedObjId {
            get {
                if (!IsInputLoaded) {
                    return inputId;
                }
                if (!IsOutputLoaded) {
                    return outputId;
                }
                return 0;
            }
        }
        private uint inputId;
        private ObjData input;
        public ObjData Input {
            get {
                if (input == null) {
                    TryGetObjData(ref input, ref inputId);
                }
                return input;
            }
            set {
                TrySetObjData(ref input, ref inputId, value);
            }
        }

        private void TrySetObjData(ref ObjData input, ref uint inputId, ObjData value) {
            if (value == null) {
                inputId = 0;
            } else {
                if (value.IsAvailable) {
                    inputId = value.Id;
                } else {
                    D.Err("Trying to set objdata when it's not available! {0}", input);
                    inputId = 0;
                    input = null;
                    return;
                }
            }
            input = value;
        }

        private bool TryGetObjData(ref ObjData input, ref uint inputId) {
            if (input == null) {
                if (inputId != 0) {
                    var inp = S.Query.FindObjData.Ask(inputId);
                    if (inp == null) {
                        D.Err("Failed getting Mind Transfer obj data by id: {0}", inputId);
                        inputId = 0;
                    } else {
                        input = inp;
                    }
                }
            }
            return input != null;
        }

        private uint outputId;
        private ObjData output;
        public ObjData Output {
            get {
                if (output == null) {
                    TryGetObjData(ref output, ref outputId);
                }
                return output;
            }
            set {
                TrySetObjData(ref output, ref outputId, value);
            }
        }

        public uint InputId => inputId;
        public uint OutputId => outputId;

        public int LoadObjSpotIdx => Tile.Transform.InPort;

        public bool IsInputLoaded;
        public bool IsOutputLoaded;
        public RefillableStorageComp Fuel;
        public float Progress;
        private float progressPerGridTick;

        private BFloat progressB;
        public BFloat ProgressB => progressB ??= new BFloat(0f);

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        protected override void OnConfig() {
            energyCost = Config.GetInt(PropertyIdH.EnergyCost, DefaultEnergyCost);
            // per hour
            progressPerGridTick = Config.GetFloat(PropertyIdH.DurationHours, 8f);
            progressPerGridTick *= Consts.TicksPerHour;
            progressPerGridTick = 1f / progressPerGridTick;
            // we tick grid every 10 ticks
            progressPerGridTick *= 10;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("InputId", inputId);
            data.SetUInt("OutputId", outputId);
            data.SetBool("InputLoaded", IsInputLoaded);
            data.SetBool("OutputLoaded", IsOutputLoaded);
            data.SetFloat("Progress", Progress);
            data.SetInt("DoneCount", DoneCount);
        }

        protected override void OnLoad(ComponentData data) {
            inputId = data.GetUInt("InputId", 0);
            outputId = data.GetUInt("OutputId", 0);
            Progress = data.GetFloat("Progress", 0f);
            IsInputLoaded = data.GetBool("InputLoaded", false);
            IsOutputLoaded = data.GetBool("OutputLoaded", false);
            DoneCount = data.GetInt("DoneCount", 0);
        }

        public override string ToString() {
            return " * SoulExchange";
        }

        public override void OnLateReady(bool wasLoaded) {
            Tile.Transform.EnsurePortPresence(false, true, true, this);
            Entity.TryGetComponent(out Fuel);
        }

        public override void OnReady(bool wasLoaded) {
        }
        private ElectricNodeComp elNode;
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    if (EntityUtils.IsWithinMargin(Tile.PosIdx, false, false)) {
                        S.UIQuery.ShowTutorialPopup.Ask(MindTransferPage.PageId, 3f);
                    }
                }
            }
        }

        private void AfterTickGrid() {
            if (IsInputLoaded && IsOutputLoaded) {
                if (elNode.HasDeficit || !elNode.IsSwitchedOn) { return; }
                if (!Fuel.TryConsume(progressPerGridTick)) {
                    elNode.SetConsumerWantedInput(0f);
                    return;
                }
                elNode.SetConsumerWantedInput(energyCost);
                if (!elNode.IsConsuming) { return; }

                Progress += progressPerGridTick;
                progressB?.Set(Progress);

                if (Progress >= 1f) {
                    var m = Input.Mind;
                    Mind.UnlinkFromObj(m, Input);
                    if (Output.Mind != null) {
                        // we're overwriting the output
                        Mind.UnlinkFromObj(Output.Mind, Output);
                    }
                    Mind.LinkToObj(m, Output);
                    S.Query.DeactivateObjData.Ask(Input, ObjState.Destroyed);
                    Input = null;
                    DoneCount++;
                    AbortExchange();
                }

                if (IsInputLoaded && IsOutputLoaded) {
                    ShowInfoBar(Consts.ColorGreenBar, Progress,
                        $"{T.MindTransfer}: {Units.Percentage(Progress)}");
                }
            } else {
                elNode.SetConsumerWantedInput(0f);
                HideInfoBar();
                if (Input != null || Output != null) {
                    var wantedPartId = IsInputLoaded ? outputId : inputId;
                    if (wantedPartId != 0 && !HasAnyGoal) {
                        var g = CreateGoal(JobTypeIdH.Hauling, IconId.CHaul,
                            $"{T.HaulItem}: {T.NounBodyPart}", this)
                                .WithVar(AIGoalVars.ObjId_Uint, wantedPartId)
                                .ByPlayer(true)
                                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                                .WithWorldToDesired(AIVarsH.IsObjLoaded, false, true);
                        entity.TrySetGoal(g);
                    }
                }
            }
        }

        public override void OnRemove() {
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }

        public bool LoadObj(ObjData objData, AIGoal goal) {
            if (objData.Id == inputId) {
                IsInputLoaded = true;
            } else if (objData.Id == outputId) {
                IsOutputLoaded = true;
            } else {
                D.Err("WTF u loading bro? in: {0} out: {1}, loading: {2} | {3}", inputId, outputId, objData, goal);
                return false;
            }
            objData.State = ObjState.InDevice;
            objData.DeviceId = Tile.Id;
            if (objData.Obj != null) {
                S.Objs.Destroy(objData.Obj);
            }
            stateChangedB?.Invoke();
            return true;
        }

        public void AbortExchange() {
            if (inputId != 0) {
                if (IsInputLoaded) {
                    if (Input == null) {
                        D.Err("Input is null while inputId was not in AbortExchange! {0}", Tile);
                    } else {
                        Input.State = ObjState.Active;
                        Input.TryUnreserve(null);
                        S.Objs.Create(EntityUtils.CenterOf(LoadObjSpotIdx), Input);
                    }
                }
            }
            IsInputLoaded = false;
            Input = null;
            if (outputId != 0) {
                if (IsOutputLoaded) {
                    if (Output == null) {
                        D.Err("Output is null while outputId was not in AbortExchange! {0}", Tile);
                    } else {
                        Output.State = ObjState.Active;
                        Output.TryUnreserve(null);
                        S.Objs.Create(EntityUtils.CenterOf(LoadObjSpotIdx), Output);
                    }
                }
            }
            IsOutputLoaded = false;
            Output = null;
            Progress = 0f;
            progressB?.Set(0f);
            HideInfoBar();
            stateChangedB?.Invoke();
            if (TryGetGoal(out var g)) {
                AIGoal.Cancel(g, G.Ticks);
            }
        }

        public void AddMindInputOptions(System.Collections.Generic.List<ObjData> items) {
            var objs = S.Query.GetAllObjs.Ask();
            foreach (var obj in ObjData.SortedBy(S, objs.Values, ObjData.SortMode.QualityDesc)) {
                if (obj.Id == inputId) { continue; }
                if (obj.Id == outputId) { continue; }
                if (!obj.IsAvailable) { continue; }
                if (obj.Mind == null) { continue; }
                if (obj.BodyPart?.SupportsMindTransfer != true) { continue; }
                items.Add(obj);
            }
        }

        public void AddMindOutputOptions(System.Collections.Generic.List<ObjData> items) {
            var objs = S.Query.GetAllObjs.Ask();
            foreach (var obj in ObjData.SortedBy(S, objs.Values, ObjData.SortMode.QualityDesc)) {
                if (obj.Id == inputId) { continue; }
                if (obj.Id == outputId) { continue; }
                if (!obj.IsAvailable) { continue; }
                if (obj.BodyPart?.HoldsMind != true) { continue; }
                if (obj.BodyPart?.SupportsMindTransfer != true) { continue; }
                items.Add(obj);
            }
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<MindTransferComp>(out var other)) {
                other.Progress = Progress;
                other.input = input;
                other.inputId = inputId;
                other.output = output;
                other.outputId = outputId;
                other.IsInputLoaded = IsInputLoaded;
                other.IsOutputLoaded = IsOutputLoaded;
            }
        }
    }
}
