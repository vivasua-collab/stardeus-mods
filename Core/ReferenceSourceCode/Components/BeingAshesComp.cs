using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Components {
    public sealed class BeingAshesComp : BaseComponent<BeingAshesComp> {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BeingAshesComp());
        }

        public bool IsNamed => Obj.Data.Mind != null;

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                Ready.When(WhenH.AllSystemsLoaded, UpdateInfo);
            } else {
                Obj.AfterInitialize += UpdateInfo;
            }
        }

        public void UpdateInfo() {
            var mind = Obj.Data?.Mind;
            if (mind == null) { return; }
            ShowInfoIcon(IconId.CFuneralRibbon, mind.DeceasedInfo(
                Obj.Data.CustomString,
                Obj.Data.CustomLong));
            Obj.Pile.TryTreatAsObj(true);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.CarryEnded) {
                UpdateInfo();
            }
        }

        public static void DoForget(GameState s, ObjData objData, bool sendDebuff) {
            var obj = objData.Obj;
            if (sendDebuff) {
                s.Sig.BeingAshesForgotten.Send(objData, objData.Mind);
            }
            Mind.UnlinkFromObj(objData.Mind, objData);
            if (obj != null) {
                obj.Pile.IsForbidden = false;
                obj.Pile.TryTreatAsObj(false);
                if (obj.TryGetComponent<BeingAshesComp>(out var comp)) {
                    comp.HideInfoIcon();
                }
            }
        }

        public override string ToString() {
            return " * BeingAshes";
        }
    }
}
