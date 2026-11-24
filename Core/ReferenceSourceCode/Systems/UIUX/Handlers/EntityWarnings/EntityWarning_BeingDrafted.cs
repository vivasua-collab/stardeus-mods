using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_BeingDrafted : BaseEntityWarning {
        public const string WarningId = "Entity_BeingDrafted";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_BeingDrafted());
        }
        public override string Id => WarningId;
        public override int Order => 20;
        private Being being;

        public override bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            if (being?.Being == null || !being.Being.IsActive) { return false; }
            if (!being.Being.TryGetComponent(out DirectControlComp control)) { return false; }
            if (!control.IsUnderDirectControl) { return false; }
            this.being = being.Being;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CDraft, T.AdjDrafted, ShowQuickChoice);
            UIUtils.AddTip(life, warning, T.BeingDraftedTip);
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(T.VerbUndraft, Undraft);
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void Undraft() {
            UndraftAction.UndraftAll(new List<Being> { being }, false, out _);
        }

        public override void Unload() {
            being = null;
        }
    }
}
