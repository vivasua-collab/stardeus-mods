using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexShipClassesTab : BaseCodexTab {
        public override string Id => "ShipClasses";
        public override int Order => 60;
        public override string TabName => T.CodexShipClasses;
        public override string TabIcon => IconId.CUpgrade;

        private ShipClassDef selected;
        private WButtonIconText2 selectedButton;

        public bool ShowEntry(string id) {
            var shipClass = ShipClassDef.Get(id);
            if (shipClass == null) {
                return false;
            }
            selected = shipClass;
            return true;
        }

        public override void LoadIndex(WGroup index) {
            selectedButton = null;
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var visibleShipClasses = ShipClassDef.AllOrdered
                .Where(item => MatchesSearch(item.NameT, item.RequiredClass?.NameT))
                .ToList();

            if (selected != null && !visibleShipClasses.Contains(selected)) {
                selected = null;
            }
            selected ??= visibleShipClasses.FirstOrDefault();

            foreach (var item in visibleShipClasses) {
                var capturedItem = item;
                var w = UISpawn.Widget<WButtonIconText2>(Lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                bool isSelected = item == selected;
                var title = isSelected
                    ? Texts.Accented(item.NameT, bold: false)
                    : item.NameT;
                if (isSelected) {
                    selectedButton = w;
                }
                w.Load(item.Preview, title, null, () => {
                    if (selected == capturedItem) {
                        return;
                    }
                    var previous = selected;
                    var previousButton = selectedButton;
                    selected = capturedItem;
                    selectedButton = w;
                    previousButton?.SetText(previous.NameT);
                    w.SetText(Texts.Accented(capturedItem.NameT, bold: false));
                    RebuildContent();
                });
            }
        }

        public override void LoadContent(WGroup content) {
            if (selected == null) {
                return;
            }

            var block = UISpawn.Block(Lifetime, content, 0);
            int idx = 0;

            var titleW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
            titleW.Load(selected.NameT);

            var sb = Caches.StringBuilder2;
            sb.Clear();

            sb.AppendFormat("{0}:\n", Texts.Accented2(T.NounRequirements, false));
            sb.AppendFormat("{0}: {1}\n",
                T.NounLicenseFee, Texts.Accented(Wallet.ToValueString(selected.UnlockPrice)));
            if (selected.RequiredClass != null) {
                sb.AppendFormat("{0}: {1}\n",
                    T.MustOwn, Texts.Accented(selected.RequiredClass.NameT));
            }
            if (selected.IsSpaceStation) {
                sb.AppendFormat("{0}: {1}\n",
                    T.NounEngines, Texts.Accented("0"));
            }
            if (selected.MinSize > 0) {
                sb.AppendFormat("{0}: {1}+\n",
                    T.Size, Texts.Accented(selected.MinSize));
            }
            if (selected.MinEnergy > 0) {
                sb.AppendFormat("{0}: {1}+\n",
                    T.ResearchEnergy, Texts.Accented(Units.Electricity(selected.MinEnergy)));
            }

            sb.AppendFormat("\n{0}:\n", Texts.Accented2(T.NounStats, false));
            sb.AppendFormat("{0}: {1}\n",
                T.NounClassPoints, Texts.Accented(selected.MaxClassPoints));
            sb.AppendFormat("{0} ({1}): {2}\n",
                T.NounHullPoints, T.NounClassBonus, Texts.Accented($"+{selected.HPBonus}"));
            sb.AppendFormat("{0}: {1}\n",
                T.ShipClassBaseEvade, Texts.Accented(Units.Percentage(selected.BaseEvasion)));
            sb.AppendFormat("{0}: {1}\n",
                T.NounFluxBonus, Texts.Accented(selected.FluxBonus));
            sb.AppendFormat("{0}: {1}\n",
                T.MaxFluxCapacity, Texts.Accented(selected.MaxFluxCapacity));
            sb.AppendFormat("{0}: {1}\n",
                T.NounBaseVentRate,
                Texts.Accented($"+{selected.FluxSoftVentPerHour:0.#}/{T.UnitShortHour}"));
            sb.AppendFormat("{0} ({1}): {2}\n",
                T.NounFluxVents, T.Max, Texts.Accented(selected.MaxFluxVents));

            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(sb.ToString());
            descW.RebuildTextLayout();

            if (selected.Preview != null) {
                var sprite = Images.SpriteWithResourceFallback(selected.Preview);
                if (sprite != null) {
                    var capturedSprite = sprite;
                    var imgW = UISpawn.Widget<WButtonImageAutoSize>(Lifetime,
                        UIPrefabId.WTabPanelItem_ButtonImageBlock, block.Container, idx++);
                    imgW.Load(null, () => {
                        The.SysSig.ShowImageBlowout.Send(capturedSprite);
                    }, heightLimit: 256);
                    UIUtils.AddTip(Lifetime, imgW, T.ClickToEnlarge);
                    imgW.SetSprite(capturedSprite);
                }
            }
        }
    }
}
