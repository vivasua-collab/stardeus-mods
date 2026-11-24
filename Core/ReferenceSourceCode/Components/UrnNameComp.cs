using Game.Utils;
using Game.CodeGen;
using Game.Systems.Mental;
using System.Buffers;
using Game.Systems.Items;
using Game.Constants;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class UrnNameComp : BaseComponent<UrnNameComp> {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new UrnNameComp());
        }

        private Mind mind;
        private string causeOfDeath;
        private long timeOfDeath;
        public Mind Mind => mind;
        public string CauseOfDeath => causeOfDeath;
        public long TimeOfDeath => timeOfDeath;
        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                Ready.When(WhenH.AllSystemsLoaded, CheckState);
            }
        }

        public void CheckState() {
            var pool = ArrayPool<ObjData>.Shared;
            var objs = pool.Rent(1);

            var found = S.Query.FindStoredObjData.Ask(
                EntityId, objs, null, default);

            if (found == 0) {
                mind = null;
                causeOfDeath = null;
                timeOfDeath = 0;
                pool.Return(objs, true);
                stateChangedB?.Invoke();
                Hide();
                return;
            }

            mind = objs[0].Mind;
            causeOfDeath = objs[0].CustomString;
            timeOfDeath = objs[0].CustomLong;
            pool.Return(objs, true);

            if (mind == null) {
                stateChangedB?.Invoke();
                Hide();
                return;
            }

            stateChangedB?.Invoke();
            SetName(mind, causeOfDeath, timeOfDeath);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ObjStorageChanged) {
                CheckState();
            } else if (message == MsgIdH.ConstructionFinished) {
                CheckState();
            }
        }

        public void SetName(Mind mind, string cause, long time) {
            ShowInfoIcon(IconId.CFuneralRibbon,
                mind.DeceasedInfo(cause, time));
        }
        public void Hide() {
            HideInfoIcon();
        }

        public override string ToString() {
            return " * UrnName";
        }
    }
}
