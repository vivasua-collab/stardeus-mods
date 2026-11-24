using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class WShipClassOption : WBase {
        public WEntityHeader Header;
        public WGroup Content;
        public WButtonText Button;
        private Lifetime lifetime;

        public void Load(Lifetime parentLife, GameState s, ShipClassDef type,
                bool isUnlocked, Action<ShipClassDef> onChoose,
                Action onRefresh) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = parentLife.CreateChild($"ShipClassChoiceOption_{type.NameT}");

            Header.Load(type.Preview, type.NameT, null, null);
            Header.SetFactionIcon(null);
            if (A.Player.Ship.Class == type) {
                Header.SetText2(Texts.Grey($"({T.OurShip})"));
            }
            Header.AutoResize();

            bool isStrictCheck = !isUnlocked;
            bool isCompatible = type.IsCompatibleWithPlayer(s, isStrictCheck);

            if (isUnlocked && isCompatible) {
                Button.Load(T.Select, () => {
                    if (type.IsCompatibleWithPlayer(s, false)) {
                        onChoose?.Invoke(type);
                    } else {
                        s.Sig.ShowErrorMessage.Send(T.ShipDoesNotMeetRequirements);
                    }
                });
            } else if (!isUnlocked && isCompatible) {
                var price = Wallet.ToValueString(type.UnlockPrice);
                if (s.Wallet.CanAfford(type.UnlockPrice)) {
                    price = Texts.Green(price);
                } else {
                    price = Texts.Red(price);
                }
                Button.Load($"{T.Buy}: {price}", () => {
                    if (!s.Wallet.TryPay(type.UnlockPrice, null)) {
                        s.Sig.ShowErrorMessage.Send(T.CannotAfford);
                        return;
                    }
                    var unlocked = s.Query.GetUnlockedShipClasses.Ask();
                    unlocked.Add(type);
                    UISounds.PlayAchievement();
                    onRefresh?.Invoke();
                });
                UIUtils.AddTipFunc(lifetime, Button, () => {
                    if (s.Wallet.CanAfford(type.UnlockPrice)) {
                        return $"{T.OurWallet}: {Texts.Green(s.Wallet.ValueString)}";
                    }
                    return $"{T.OurWallet}: {Texts.Red(s.Wallet.ValueString)}";
                });
            } else {
                Button.Load(Texts.Red(T.AdjIncompatible), () => {
                    s.Sig.ShowErrorMessage.Send(T.ShipDoesNotMeetRequirements);
                });
            }

            int idx = 0;
            idx = LoadRequirements(s, type, isUnlocked, isCompatible, idx);
            idx = LoadStats(s, type, idx);
            // LoadPreview(type, idx);
        }

        private int LoadRequirements(GameState s, ShipClassDef type,
                bool isUnlocked, bool isCompatible, int siblingIdx) {
            var ship = A.Player.Ship;
            var block = UISpawn.Block(lifetime, Content, siblingIdx++);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounRequirements);

            var fee = Wallet.ToValueString(type.UnlockPrice);
            if (isUnlocked) {
                fee = Texts.Accented(fee);
            } else if (s.Wallet.CanAfford(type.UnlockPrice)) {
                fee = Texts.Green(fee);
            } else {
                fee = Texts.Red(fee);
            }
            var feeRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            feeRow.Load(IconId.CStarcreds, T.NounLicenseFee, fee);
            if (!isUnlocked) {
                UIUtils.AddTipFunc(lifetime, feeRow, () => {
                    if (s.Wallet.CanAfford(type.UnlockPrice)) {
                        return $"{T.OurWallet}: {Texts.Green(s.Wallet.ValueString)}";
                    }
                    return $"{T.OurWallet}: {Texts.Red(s.Wallet.ValueString)}";
                });
            }

            if (type.RequiredClass != null && (!isUnlocked || !isCompatible)) {
                List<ShipClassDef> unlocked = s.Query.GetUnlockedShipClasses.Ask();
                var mustOwnName = type.RequiredClass.NameT;
                if (unlocked.Contains(type.RequiredClass)) {
                    mustOwnName = Texts.Green(mustOwnName);
                } else {
                    mustOwnName = Texts.Red(mustOwnName);
                }
                var row = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                row.Load(type.RequiredClass.Preview, T.MustOwn, mustOwnName);
            }

            if (type.IsSpaceStation) {
                var row = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                string engineText;
                if (isUnlocked) {
                    engineText = Texts.Accented("0");
                } else if (ship.CombatState.Engines.Count > 0) {
                    engineText = Texts.Red("0");
                } else {
                    engineText = Texts.Green("0");
                }
                row.Load(IconId.CPropulsion, T.NounEngines, engineText);
                UIUtils.AddTipFunc(lifetime, row, () => {
                    var count = ship.CombatState.Engines.Count;
                    string ourText;
                    if (count > 0) {
                        ourText = Texts.Red(count);
                    } else {
                        ourText = Texts.Green("0");
                    }
                    return $"{T.OurShip}: {ourText}";
                });
            }

            if (type.MinSize > 0) {
                var row = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                string sizeText;
                if (isUnlocked) {
                    sizeText = Texts.Accented(type.MinSize);
                } else if (ship.Size >= type.MinSize) {
                    sizeText = Texts.Green(type.MinSize);
                } else {
                    sizeText = Texts.Red(type.MinSize);
                }
                row.Load(IconId.CSpaceShip, T.Size, $"{sizeText}+");
                UIUtils.AddTipFunc(lifetime, row, () => {
                    var size = ship.Size;
                    string ourText;
                    if (size < type.MinSize) {
                        ourText = Texts.Red(size);
                    } else {
                        ourText = Texts.Green(size);
                    }
                    return $"{T.OurShip}: {ourText}";
                });
            }

            if (type.MinEnergy > 0) {
                var row = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                string energyText;
                if (isUnlocked) {
                    energyText = Texts.Accented(Units.Electricity(type.MinEnergy));
                } else {
                    The.R.Query.UpdatePlayerShipEnergy.Ask();
                    if (ship.EnergyProduction >= type.MinEnergy) {
                        energyText = Texts.Green(Units.Electricity(type.MinEnergy));
                    } else {
                        energyText = Texts.Red(Units.Electricity(type.MinEnergy));
                    }
                }
                row.Load(IconId.CEnergy, T.ResearchEnergy, $"{energyText}+");
                UIUtils.AddTipFunc(lifetime, row, () => {
                    The.R.Query.UpdatePlayerShipEnergy.Ask();
                    var nrg = ship.EnergyProduction;
                    string ourText;
                    if (nrg < type.MinEnergy) {
                        ourText = Texts.Red(Units.Electricity(nrg));
                    } else {
                        ourText = Texts.Green(Units.Electricity(nrg));
                    }
                    return $"{T.OurShip}: {ourText}";
                });
            }

            return siblingIdx;
        }

        private int LoadStats(GameState s, ShipClassDef type, int siblingIdx) {
            var ship = A.Player.Ship;
            var block = UISpawn.Block(lifetime, Content, siblingIdx++);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounStats);

            var greyMax = Texts.Grey($"({T.Max})");

            // Class Points
            var cpRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            cpRow.Load(IconId.CUpgrade, $"{T.NounClassPoints} {greyMax}",
                Texts.Accented(type.MaxClassPoints));
            UIUtils.AddTipFunc(lifetime, cpRow, () => {
                var cp = ship.CombatState.CurrentClassPoints;
                var maxCP = ship.Class.MaxClassPoints;
                string curText;
                if (cp > type.MaxClassPoints) {
                    curText = Texts.Red(cp);
                } else {
                    curText = Texts.Green(cp);
                }
                string maxText;
                if (maxCP < type.MaxClassPoints) {
                    maxText = Texts.Red(maxCP);
                } else {
                    maxText = Texts.Green(maxCP);
                }
                return $"{T.OurShip}: {maxText} ({T.AmountCurrent}: {curText})";
            });

            // Base Evasion
            var evRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            evRow.Load(IconId.CPropulsion, T.ShipClassBaseEvade,
                Texts.Accented(Units.Percentage(type.BaseEvasion)));
            UIUtils.AddTipFunc(lifetime, evRow, () => {
                var evasion = ship.Class.BaseEvasion;
                string ourText;
                if (evasion < type.BaseEvasion) {
                    ourText = Texts.Red(Units.Percentage(evasion));
                } else {
                    ourText = Texts.Green(Units.Percentage(evasion));
                }
                return $"{T.OurShip}: {ourText}";
            });

            // Flux Bonus
            var fluxRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            fluxRow.Load(IconId.CFlux, T.NounFluxBonus,
                Texts.Accented(type.FluxBonus));
            UIUtils.AddTipFunc(lifetime, fluxRow, () => {
                var flux = ship.Class.FluxBonus;
                string ourText;
                if (flux < type.FluxBonus) {
                    ourText = Texts.Red(flux);
                } else {
                    ourText = Texts.Green(flux);
                }
                return $"{T.OurShip}: {ourText}";
            });

            // Max Flux Capacity
            var maxFluxRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            maxFluxRow.Load(IconId.CFlux, T.MaxFluxCapacity,
                Texts.Accented(type.MaxFluxCapacity));
            UIUtils.AddTipFunc(lifetime, maxFluxRow, () => {
                var maxFlux = ship.Class.MaxFluxCapacity;
                string ourText;
                if (maxFlux < type.MaxFluxCapacity) {
                    ourText = Texts.Red(maxFlux);
                } else {
                    ourText = Texts.Green(maxFlux);
                }
                return $"{T.OurShip}: {ourText}";
            });

            // Base Vent Rate
            var ventRateRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            ventRateRow.Load(IconId.CFlux, T.NounBaseVentRate,
                Texts.Accented($"+{type.FluxSoftVentPerHour:0.#}/{T.UnitShortHour}"));
            UIUtils.AddTipFunc(lifetime, ventRateRow, () => {
                var ventRate = ship.Class.FluxSoftVentPerHour;
                var ventRateText = $"+{ventRate:0.#}/{T.UnitShortHour}";
                string ourText;
                if (ventRate < type.FluxSoftVentPerHour) {
                    ourText = Texts.Red(ventRateText);
                } else {
                    ourText = Texts.Green(ventRateText);
                }
                return $"{T.OurShip}: {ourText}";
            });

            // Max Flux Vents
            var maxVentsRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            maxVentsRow.Load(IconId.CFlux, $"{T.NounFluxVents} {greyMax}",
                Texts.Accented(type.MaxFluxVents));
            UIUtils.AddTipFunc(lifetime, maxVentsRow, () => {
                var maxVents = ship.Class.MaxFluxVents;
                string ourText;
                if (maxVents < type.MaxFluxVents) {
                    ourText = Texts.Red(maxVents);
                } else {
                    ourText = Texts.Green(maxVents);
                }
                return $"{T.OurShip}: {ourText}";
            });

            return siblingIdx;
        }

        private void LoadPreview(ShipClassDef type, int siblingIdx) {
            if (type.Preview == null) {
                return;
            }
            var sprite = Images.SpriteWithResourceFallback(type.Preview);
            if (sprite == null) {
                return;
            }
            var block = UISpawn.Block(lifetime, Content, siblingIdx);
            var capturedSprite = sprite;
            var imgW = UISpawn.Widget<WButtonImageAutoSize>(lifetime,
                UIPrefabId.WTabPanelItem_ButtonImageBlock, block.Container, 0);
            imgW.Load(null, () => {
                The.SysSig.ShowImageBlowout.Send(capturedSprite);
            });
            imgW.SetSprite(capturedSprite);
            UIUtils.AddTip(lifetime, imgW, T.ClickToEnlarge);
        }
    }
}
