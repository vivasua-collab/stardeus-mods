using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class EntityOverview_Slots : EntityOverviewBlock {
        public const string BlockId = "Slots";
        public override int Order => 10;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() {
            Register(new EntityOverview_Slots());
        }

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent<ISlots>(out var sc)) { return false; }
            if (sc.HideUI) { return false; }
            if (sc.IsMakeshift) { return false; }
            return true;
        }

        public override void LoadContent(EntityOverviewContext ctx, WGroup group, ref int gIdx) {
            var tile = ctx.Tile;
            if (tile == null) { return; }
            if (!tile.TryGetComponent<ISlots>(out var slotsComp)) { return; }
            var slots = slotsComp.Slots;
            if (slots == null) { return; }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(slotsComp.UITitle);

            for (int i = 0; i < slots.Count; i++) {
                var slot = slots[i];
                Lifetime contentLife = null;

                var listBtn = UISpawn.Widget<WContentListButton>(lifetime,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);

                listBtn.Load(IconId.CInfo, $"{T.NounSlot} #{slot.Number + 1}", null);

                listBtn.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    contentLife = lifetime.CreateChild($"SlotContent_{slot.Number}");
                    SpawnSlotContent(contentLife, container, slot);
                    return true;
                }, expanded: true);

                slot.StateB.Bind(lifetime, () => {
                    contentLife?.Dispose();
                    contentLife = null;
                    listBtn.RebuildContainer();
                }, false);
            }
        }

        private static void SpawnSlotContent(Lifetime life, RectTransform container, Slot slot) {
            int idx = 0;

            var being = slot.ContainedBeing;
            bool hasSomething = false;
            if (being != null) {
                var occupationRow = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                var bd = being.Data;
                occupationRow.Load(null, bd.Mind.Name.ShortName, null, () => {
                    EntityUtils.SelectOrFocus(being);
                });
                bd.Avatar.Load(occupationRow.Image);
                UIUtils.AddHover(life, occupationRow,
                    on => EntityUtils.ShowEntityLink(null, being, on));
                hasSomething = true;
            }

            if (slot.ReservedBy != null) {
                var reservedBeing = slot.ReservedBy;
                var reservedRow = UISpawn.Widget<WIconText>(life,
                    UIPrefabId.WContent2Text, container, idx++);
                reservedRow.Load(IconId.CTime,
                    T.ReservedByDetailed_V1.F(reservedBeing.Data.Mind.Name.DisplayName));
                UIUtils.AddTip(life, reservedRow, Units.TicksAgoFromNow(slot.LastReserveTime));
                UIUtils.AddHover(life, reservedRow,
                    on => EntityUtils.ShowEntityLink(null, reservedBeing, on));
                hasSomething = true;
            }

            var sideReservations = slot.SideReservations;
            if (sideReservations != null) {
                foreach (var kv in sideReservations) {
                    var sideBeing = kv.Value;
                    var sideRow = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent2Button, container, idx++);
                    var bd = sideBeing.Data;
                    sideRow.Load(null,
                        T.ReservedByDetailed_V1.F(bd.Mind.Name.DisplayName), null, () => {
                        EntityUtils.SelectOrFocus(sideBeing);
                    });
                    bd.Avatar.Load(sideRow.Image);
                    hasSomething = true;
                }
            }

            if (!hasSomething) {
                var emptyRow = UISpawn.Widget<WIconText>(life,
                    UIPrefabId.WContent2Text, container, idx++);
                emptyRow.Load(IconId.CInfo, T.Empty);
            }
        }
    }
}
