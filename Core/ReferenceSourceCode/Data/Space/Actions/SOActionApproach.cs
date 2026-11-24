using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionApproach : SOAction {
        public const string ActionId = "Approach";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionApproach());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so.Ship?.IsDerelict != true || so.Ship?.IsPlayer == true) {
                return false;
            }
            if (ctx.S.Sys.Ship.IsApproaching(so)) {
                // already on the way, "Cancel Flight" takes over from here
                return false;
            }
            so.Ship.NearbyPosFrom(ctx.Player.Location, out var isNear);
            if (isNear) {
                return true;
            }
            // from afar we fly there first, so the same rules as travelling apply
            return so.Parent == ctx.Player.Parent
                && so.Parent.Layer == UniverseLayer.Sector;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.SpaceObjectApproach, BeginEnter);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return UDB.Create(this, UDBT.DBtn, IconId.CPropulsion,
        //         T.SpaceObjectApproach)
        //             .WithClickFunction(BeginEnter);
        // }

        public void BeginEnter() {
            if (!IsAvailable(ctx)) { return; }
            if (ctx.S.Sys.Ship.IsMoving || ctx.S.Sys.Ship.HasDestination(out _)) {
                ctx.S.Sig.ShowErrorMessage.Send(T.ShipMustBeStoppedFirst);
                return;
            }
            if (!A.InstantBuild) {
                if (!ctx.S.Sys.Ship.ValidateFlightReadiness(UniverseLayer.Sector)) {
                    UISounds.PlayActionDenied();

                    return;
                }
            }
            if (ctx.S.Query.GetQuestBool.Ask(QuestBoolIds.DisallowApproachDerelict)) {
                var msg = T.DerelictShipApproachTooEarlyWarning;
                msg = $"{msg}\n{Texts.GreySmaller(Texts.Italic(T.CompleteMoreQuestsToUnlock))}";

                The.Dialog.ShowSimple(IconId.CComputing, msg);
                return;
            }
            var so = ctx.SO;
            if (ctx.S.Sys.Expedition.FindForSpaceObject(so) != null) {
                ctx.S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.SpaceObjectApproachFailExpeditionRunning}");

                return;
            }

            if (A.InstantBuild) {
                DerelictApproach.Execute(ctx.S, so);
                return;
            }

            // flying abandons ship sections that hang out in the map margin, so this must
            // go through the shared warning gate like every other flight order
            SOUtils.CheckAbandonOnTravel(ctx.S, () => DerelictApproach.Begin(ctx.S, so));
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
