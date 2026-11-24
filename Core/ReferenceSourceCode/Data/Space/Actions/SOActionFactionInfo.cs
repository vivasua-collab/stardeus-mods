using Game.Constants;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Data.Space {
    public sealed class SOActionFactionInfo : SOAction {
        public override int UIPriority => int.MaxValue;
        public const string ActionId = "FactionInfo";

        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionFactionInfo());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.Ship != null) {
                return false;
            }
            return ctx.SO.FactionId != null; // && ctx.SO.Knowledge?.FactionId != null;
        }

        public override bool AddOptions(QuickChoice qc) {
            var faction = ctx.SO.Faction;
            if (faction.IsPlayer) {
                qc.AddDynamicText(T.OwnedByYourFaction, new QuickChoice.DynamicText());
            } else {
                qc.AddDynamicText(T.OwnedByFaction, new QuickChoice.DynamicText() {
                    TipFunc = () => T.OwnedByFactionDetails,
                });
            }
            qc.AddIconButton(faction.NameT, new QuickChoice.IconButton() {
                AutoClose = false,
                Icon = Images.Sprite(faction.FlagIcon),
                OnClick = ShowInfo,
                TipFunc = ShowFactionRelationshipTip,
            });
            return true;
        }

        private string ShowFactionRelationshipTip() {
            var dip = ctx.S.Sys.Diplomacy;
            var f = ctx.SO.Faction;
            if (f == null || f.IsPlayer) { return null; }
            var rel = dip.RelationshipWith(ctx.SO.Faction);
            return T.FactionStandingTip_V2.F(f.NameT, Units.PercentageSigned(rel));
        }

        private void ShowInfo() {
            var faction = ctx.SO.Faction;
            WPopup.Create(false, faction.FlagIcon, faction.NameT, faction.AboutT).Spawn();
        }

        public override string Id => ActionId;
    }
}
