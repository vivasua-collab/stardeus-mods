using System;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Input;
using Game.Systems.Diplomacy;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WGiftChoiceOption : WBase {
        public WEntityHeader Header;
        public WGroup Content;
        public WButtonText Button;
        private Lifetime lifetime;

        public void Load(Lifetime parentLife, GameState s, GiftPackage package,
                string selectText, Action<GiftPackage> onChoose) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = parentLife.CreateChild($"GiftChoiceOption_{package.Number}");

            Header.Load(package.Icon, package.Name, HeaderText(package), () => {});
            Header.SetFactionIcon(null);
            Header.AutoResize();
            Button.Load(selectText, () => onChoose?.Invoke(package));
            LoadConfirmTooltip(package);

            int idx = 0;
            idx = LoadContents(s, package, idx);
        }

        private static string HeaderText(GiftPackage package) {
            string value = package.Value > 0f || package.Mode != GiftMode.Quest
                ? $"{T.NounValue}: {Texts.Green(Wallet.ToValueString(package.Value))}"
                : null;
            string chance = (package.OnAccept != null || package.OnReject != null)
                && package.Direction != GiftDirection.NPCToPlayer
                ? $"{T.ChanceAccept}: {Units.PercentageColored(package.AcceptChance, 0.85f, 0.5f)}"
                : null;

            if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(chance)) {
                return $"{value}\n{chance}";
            }
            if (!string.IsNullOrEmpty(value)) {
                return value;
            }
            return chance;
        }

        private int LoadContents(GameState s, GiftPackage package, int siblingIdx) {
            var block = UISpawn.Block(lifetime, Content, siblingIdx++);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.Contents);

            if (package.Items.Count == 0) {
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
                return siblingIdx;
            }

            foreach (var item in package.Items) {
                var row = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, block.Container, idx++);
                LoadItemRow(lifetime, s, row, item);
            }
            return siblingIdx;
        }

        private void LoadConfirmTooltip(GiftPackage package) {
            UIUtils.AddTip(lifetime, Button, GiftConsequenceTip.ForPackage(package));
        }

        private static void LoadItemRow(Lifetime life, GameState s, WIconText2 row, GiftItem item) {
            if (item.Tradeable != null) {
                LoadTradeableRow(row, item);
                return;
            }

            switch (item.Type) {
                case GiftItemType.Tile: {
                    if (item.Tile?.Definition == null) {
                        D.Err("No item tile!");
                        row.Load(IconId.CWarning, T.Error);
                        return;
                    }
                    row.Load(item.Tile.Definition.Preview, item.Tile.Definition.NameT);
                    UIUtils.AddTipFunc(life, row, () => item.Tile.Definition.ExtendedDesc(false));
                    return;
                }
                case GiftItemType.Being: {
                    if (item.Being == null) {
                        D.Err("No being!");
                        row.Load(IconId.CWarning, T.Error);
                        return;
                    }
                    string name = item.Being.Mind?.Name?.FullNameOrNameless ?? T.Unknown;
                    row.Load((string) null, name, null);
                    item.Being.Avatar.Load(row.Image);
                    UIUtils.AddTipFunc(life, row, () => item.Being.Species.Def.ExtendedDesc(false));
                    return;
                }
                case GiftItemType.Obj: {
                    if (item.Obj == null) {
                        D.Err("No obj!");
                        row.Load(IconId.CWarning, T.Error);
                        return;
                    }
                    row.Load(item.Obj.Def.PreviewSprite, item.Obj.ListName);
                    row.SetTint(item.Obj.Color);
                    UIUtils.AddTipFunc(life, row, () => {
                        if (item.Obj.Mind != null) {
                            return item.Obj.Tooltip(A.S);
                        } else {
                            return item.Obj.Def.ExtendedDesc(false);
                        }
                    });
                    return;
                }
                case GiftItemType.Fuel:
                case GiftItemType.Mat: {
                    if (item.Mat.Type == null) {
                        D.Err("No mat type!");
                        row.Load(IconId.CWarning, T.Error);
                        return;
                    }
                    string icon = item.Mat.Type.IconId;
                    string name = item.Mat.Type.NameT;
                    string count = Units.XNum(item.Mat.StackSize);
                    row.Load(icon, name, count);
                    row.SetTint(item.Mat.Type.IconTint);
                    UIUtils.AddTipFunc(life, row, () => item.Mat.Type.Def.ExtendedDesc(false));
                    return;
                }
                case GiftItemType.Money: {
                    string value = Wallet.ToValueString(item.Money);
                    if (item.Direction == GiftDirection.PlayerToNPC) {
                        value = Texts.Red($"-{value}");
                    } else {
                        value = Texts.Green($"+{value}");
                    }
                    row.Load(IconId.CStarcreds, T.CurrencyStarcreds, value);
                    return;
                }
                default: {
                    row.Load(IconId.CWarning, item.Type.ToString(), null);
                    return;
                }
            }
        }

        private static void LoadTradeableRow(WIconText2 row, GiftItem item) {
            var tradeable = item.Tradeable;
            string qty = Units.XNum(item.TradeableQuantity);
            if (tradeable.MatType != null) {
                row.Load(tradeable.MatType.IconId, tradeable.MatType.NameT, qty);
                row.SetTint(tradeable.MatType.IconTint);
                return;
            }

            row.Load(tradeable.Def.Preview, tradeable.Def.NameT, qty);
            if (tradeable.Color.a > 0f) {
                row.SetTint(tradeable.Color);
            }
        }
    }
}
