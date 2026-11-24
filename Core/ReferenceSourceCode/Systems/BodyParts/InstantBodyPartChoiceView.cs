using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.BodyParts {
    // Sandbox-mode (A.InstantBuild) counterpart to SurgeryChoiceView. Lists ALL compatible body
    // part defs (not just physical items in the world) and installs/removes them instantly,
    // bypassing the surgery order/reservation/failure-roll pipeline entirely. Kept separate from
    // SurgeryChoiceView so the regular surgery flow is never touched by this dev/test path.
    public sealed class InstantBodyPartChoiceView : IContextPopupWithWidth {

        private readonly GameState S;
        private readonly BeingData being;
        private readonly BodyPartSlot slot;
        private readonly Action onClose;

        public InstantBodyPartChoiceView(GameState s, BodyPartSlot slot, Action onClose) {
            S = s;
            this.slot = slot;
            being = slot.Body.Owner;
            this.onClose = onClose;
        }

        public void Show() {
            S.UIQuery.ShowContextPopup.Ask(this, true);
        }
        public void Hide() {
            S.UIQuery.ShowContextPopup.Ask(this, false);
        }

        public int UniquePopupHash => JobTypeIdH.Medical;
        public bool ShowInScreenCenter => false;
        public int PopupWidth => 600;

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            wFooter.SetActiveFast(false);

            int bIdx = 0;
            var block = UISpawn.Block(lifetime, wContent, bIdx++);
            var cont = block.Container;
            int idx = 0;

            if (!slot.IsPartRemoved && slot.CanRemovePart(false)) {
                var removeBtn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WFooterItem_DeleteButton, wFooter.Container, 0);
                removeBtn.Load(T.BodyOpRemove, RemoveInstantly);
                wFooter.SetActiveFast(true);
            }

            var nativeDefs = new List<BodyPartDef>();
            var compatDefs = new List<BodyPartDef>();
            foreach (var def in BodyPartDef.All.Values) {
                if (!def.IsCompatibleWith(slot, out var isVanilla)) { continue; }
                (isVanilla ? nativeDefs : compatDefs).Add(def);
            }
            nativeDefs.Sort((a, b) => string.CompareOrdinal(a.NameT, b.NameT));
            compatDefs.Sort((a, b) => string.CompareOrdinal(a.NameT, b.NameT));

            bool addedParts = false;
            if (nativeDefs.Count > 0) {
                var label = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartNativeParts);
                AddInstallOptions(lifetime, cont, nativeDefs, ref idx);
                addedParts = true;
            }
            if (compatDefs.Count > 0) {
                var label = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartCompatibleParts);
                AddInstallOptions(lifetime, cont, compatDefs, ref idx);
                addedParts = true;
            }
            if (!addedParts) {
                var label = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, cont, idx++);
                label.Load(T.PartCompatibleParts);
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, cont, idx++);
                empty.Load(IconId.CInfo, T.NotAvailable);
            }

            return $"{being.Mind.Name.ShortName} {GlyphMap.ArrowRight} {slot.NameT}";
        }

        private void AddInstallOptions(Lifetime life, RectTransform cont,
                List<BodyPartDef> defs, ref int idx) {
            foreach (var def in defs) {
                var d = def;
                var w = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, cont, idx++);
                w.Load(d.IconId, d.NameT, () => InstallInstantly(d));
            }
        }

        private void InstallInstantly(BodyPartDef def) {
            if (slot.InstalledDef != null) {
                slot.TryRemovePart(out _, out _, isForSwap: true);
            }
            slot.IsDestroyed = false;
            if (!slot.InstallPart(A.S, def, BodyPartState.BrandNew())) {
                UISounds.PlayActionDenied();
                return;
            }
            slot.Body.CheckCondition(false);
            UISounds.PlayDigitalConfirm();
            Hide();
        }

        private void RemoveInstantly() {
            if (!slot.TryRemovePart(out _, out _, isForSwap: false)) {
                UISounds.PlayActionDenied();
                return;
            }
            slot.Body.CheckCondition(false);
            UISounds.PlayDigitalConfirm();
            Hide();
        }

        public void OnPopupClosed() {
            onClose?.Invoke();
        }

        public void OnPopupOpened() {
        }
    }
}
