using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.UIUX;
using Game.Input;

namespace Game.Data.Space {
    public sealed class SOActionHyperjumpRegionHowTo : SOAction {
        public const string ActionId = "HyperjumpRegionHowTo";

        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionHyperjumpRegionHowTo());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so?.Region == null) { return false; }
            // Already in this region — no travel guidance needed.
            if (so.Region == A.PlayerSector.Parent?.Region) { return false; }
            // Only show when no real region-travel action is available.
            if (SOAction.Get(SOActionHyperjumpRegionFTL.ActionId).IsAvailable(ctx)) { return false; }
            if (SOAction.Get(SOActionHyperjumpRegionRelay.ActionId).IsAvailable(ctx)) { return false; }
            if (SOAction.Get(SOActionHyperjumpWarp.ActionId).IsAvailable(ctx)) { return false; }
            return true;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.HowDoITravelHere, new QuickChoice.Button() {
                AutoClose = true,
                OnClick = () => ShowHowTo(ctx.S),
                Tip = Texts.Red(T.RegionUnreachableWarning)
            });
            return true;
        }

        public static void ShowHowTo(GameState s) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            // Asking while still flying towards a relay: the relay only works once we are
            // close to it, so point at it rather than only listing it as an option
            var relay = OutOfReachRelayInSector(s);
            if (relay != null) {
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                sb.AppendLine($"{T.SpaceObjectApproach}: {relay.StarmapLink}");
            }
            sb.Append(GlyphMap.ArrowRight);
            sb.Append(' ');
            sb.AppendLine(T.RegionTravelMethodRelay_V1.F(Texts.Accented(T.SpaceObjectHyperjumprelay)));
            sb.Append(GlyphMap.ArrowRight);
            sb.Append(' ');
            sb.AppendLine(T.RegionTravelMethodBlackhole_V2.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesVoidRipper), Texts.Accented(T.SpaceObjectBlackhole)));
            sb.Append(GlyphMap.ArrowRight);
            sb.Append(' ');
            sb.AppendLine(T.RegionTravelMethodFtl_V1.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesFTLDrive)));
            sb.Append(GlyphMap.ArrowRight);
            sb.Append(' ');
            sb.AppendLine(T.RegionTravelMethodWarp_V1.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesWarpDrive)));
            WPopup.Create(false, IconId.CHyperspaceEnter,
                T.RegionTravelMethods,
                Texts.Red(T.RegionUnreachableWarning),
                sb.ToString()).Spawn();
        }

        private static SpaceObject OutOfReachRelayInSector(GameState s) {
            var sector = A.PlayerSector;
            if (sector == null || !sector.HasChildren) { return null; }
            foreach (var child in sector.Children) {
                if (child.Type.IdH == SpaceObjectTypeIdH.HyperjumpRelay) {
                    return s.Sys.Ship.IsNearby(child) ? null : child;
                }
            }
            return null;
        }

        public override string Id => ActionId;

        public override int UIPriority => -100;
    }
}
