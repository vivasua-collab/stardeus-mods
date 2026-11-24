using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public static class ShipCombatWidgetUtils {
        // saturation 70%
        public static string FluxHardColorTxt = "#EBB622";
        public static string FluxSoftColorTxt = "#AF840D";
        public static Color FluxSoftColor = Colors.FromHex(FluxSoftColorTxt);
        public static Color FluxHardColor = Colors.FromHex(FluxHardColorTxt);

        public struct FluxVentStats {
            public int Active;
            public int Total;
            public int EffectiveActive;
            public int MaxEffective;
            public float EffectiveBonus;
            public float InstalledBonus;
        }

        public struct FluxCapacitorStats {
            public int Active;
            public int Total;
            public int ActiveBonus;
            public int InstalledBonus;
        }

        public static string ShipClassOverLimitTip(NPCShip ship, int currentCP, int maxCP) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.AppendLine(Texts.Accented(T.Warning));
            if (ship.IsPlayer) {
                sb.AppendLine(T.ClassPointsExceededPlayer);
            } else {
                sb.AppendLine(T.ClassPointsExceededNpc);
            }
            AddClassPointsDesc(ship, currentCP, 0, maxCP, sb);
            return sb.ToString();
        }

        public static void AddClassPointsDesc(NPCShip ship, int currentCP,
                int nonConstructedCP, int maxCP, StringBuilder sb) {
            string cpText;
            if (nonConstructedCP > 0 && currentCP + nonConstructedCP > maxCP) {
                var futureCP = currentCP + nonConstructedCP;
                if (currentCP <= maxCP) {
                    cpText = $"{Texts.Green(currentCP)}+{Texts.Accented(nonConstructedCP)}={Texts.Red(futureCP)}";
                } else {
                    cpText = $"{Texts.Red(currentCP)}+{Texts.Accented(nonConstructedCP)}={Texts.Red(futureCP)}";
                }
            } else {
                cpText = currentCP > maxCP ? Texts.Red(Units.Num(currentCP))
                    : Texts.Green(Units.Num(currentCP));
            }
            sb.AppendLine(T.ClassPointsExceededInfo_V3.F(
                Texts.Accented(ship.Class.NameT),
                Texts.Green(Units.Num(maxCP)),
                cpText));
            sb.AppendLine();
            sb.Append(Texts.AutoLinkCodexConcept(T.ClassPointsDesc, T.NounClassPoints, ClassPointsPage.PageId));
        }

        public static string GetClassPointsTip(NPCShip ship) {
            if (ship == null) { return string.Empty; }
            var cp = ship.CombatState.CurrentClassPoints;
            var mp = ship.Class.MaxClassPoints;
            var sb = Caches.StringBuilder3;
            sb.Clear();
            if (cp > mp) {
                sb.Append(ShipClassOverLimitTip(ship, cp, mp));
            } else {
                sb.Append(T.ClassPointsDesc);
            }
            sb.AppendLine();
            if (!The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                sb.AppendFormat(T.HoldButtonForMoreInfo_V1.F(
                    The.Bindings.GetControlName(ActionType.CtrlModifier)));
                return sb.ToString();
            }
            var devices = ship.CombatState.Devices;
            var counts = Caches.NamedCounts;
            counts.Clear();
            for (int i = 0; i < devices.Count; i++) {
                var id = devices[i].Device.DeviceDef.Id;
                counts.TryGetValue(id, out var c);
                counts[id] = c + 1;
            }
            for (int i = 0; i < devices.Count; i++) {
                var dev = devices[i];
                var id = dev.Device.DeviceDef.Id;
                if (!counts.TryGetValue(id, out var count) || count <= 0) continue;
                counts[id] = 0;
                var points = dev.Device.DeviceDef.ClassPoints;
                sb.AppendFormat("\n{0}: {1} x {2} = {3}",
                    dev.Device.Def.CodexLink, count, points, count * points);
            }
            counts.Clear();
            sb.AppendFormat("\n{0}: {1} {2}", T.NounTotalSum, cp, T.NounClassPoints);
            return sb.ToString();
        }

        public static string GetFluxTip(NPCShip ship, bool includeSettings) {
            if (ship == null) { return string.Empty; }
            var flux = ship.CombatState?.Flux;
            if (flux == null) { return string.Empty; }
            var sb = Caches.StringBuilder2;
            sb.Clear();
            if (flux.State == ShipFluxState.Overload) {
                // sb.AppendLine(Texts.Red(T.FluxOverload));
                sb.AppendLine(Texts.Red(T.FluxOverloadDesc));
            } else if (flux.State == ShipFluxState.Venting) {
                sb.AppendLine(Texts.Accented(T.FluxVenting, false));
            }
            sb.AppendFormat("{0}: {1}\n", T.FluxAccumulated,
                Units.PercentageColoredInverse(flux.TotalPercent));
            sb.AppendLine(flux.VentRateText);
            if (includeSettings) {
                var baseVentRate = string.Format(" {0} {1}: +{2}/h\n",
                    GlyphMap.ArrowRight, T.NounBaseVentRate, flux.Ship.Class.FluxSoftVentPerHour);
                sb.Append(Texts.Grey(baseVentRate));
                var maxVentBonus = flux.Ship.CombatState.FluxVentBonus(false);
                string ventBonus;
                if (maxVentBonus > 0) {
                    ventBonus = string.Format(" {0} {1}: +{2}/h ({3}: +{4}/h)\n",
                        GlyphMap.ArrowRight, T.NounVentBonus,
                            flux.Ship.CombatState.FluxVentBonus(true),
                            T.Max, maxVentBonus);
                    sb.Append(Texts.Grey(ventBonus));
                }
                AppendFluxVentRateMultiplier(flux, sb);
            }
            sb.AppendFormat("{0}: {1}\n", T.FluxHard,
                Texts.WithColor(Units.Num(flux.Hard), FluxHardColorTxt));
            sb.AppendFormat("{0}: {1}\n", T.FluxSoft,
                Texts.WithColor(Units.Num(flux.Soft), FluxSoftColorTxt));
            sb.AppendFormat("{0}: {1}\n", T.FluxCapacity, Texts.Accented(flux.Capacity));
            if (!includeSettings) {
                return sb.ToString();
            }

            var baseCap = string.Format(" {0} {1}: +{2}\n",
                GlyphMap.ArrowRight, T.NounBaseCapacity, Tunables.ShipBaseFlux);
            sb.Append(Texts.Grey(baseCap));
            var classBonus = string.Format(" {0} {1}: +{2}\n",
                GlyphMap.ArrowRight, T.NounClassBonus, flux.Ship.Class.FluxBonus);
            sb.Append(Texts.Grey(classBonus));
            var maxCapBonus = flux.Ship.CombatState.FluxCapacitorBonus(false);
            string capacitorBonus;
            if (maxCapBonus > 0) {
                capacitorBonus = string.Format(" {0} {1}: +{2} ({3}: +{4})\n",
                    GlyphMap.ArrowRight, T.NounCapacitorBonus,
                        flux.Ship.CombatState.FluxCapacitorBonus(true),
                        T.Max, maxCapBonus);
            } else {
                capacitorBonus = string.Format(" {0} {1}: +0\n",
                    GlyphMap.ArrowRight, T.NounCapacitorBonus);
            }
            sb.Append(Texts.Grey(capacitorBonus));

            // var settings = flux.Ship.CombatState.FluxSettings;
            // if (settings != null) {
            //     sb.AppendFormat("{0}: {1}\n", T.AutoVentFlux,
            //         Texts.Accented(settings.AutoVentFlux ? T.On : T.Off));
            //     if (settings.AutoVentFlux) {
            //         sb.AppendFormat("{0}: {1}\n", T.ToleratedFlux,
            //             Texts.Accented(Units.Percentage(settings.ToleratedFluxTotal)));
            //         sb.AppendFormat("{0}: {1}\n", T.ToleratedFluxHard,
            //             Texts.Accented(Units.Percentage(settings.ToleratedFluxHard)));
            //         sb.AppendFormat("{0}: {1}\n", T.AutoVentFluxStopTotal,
            //             Texts.Accented(Units.Percentage(settings.VentFluxStopTotal)));
            //         sb.AppendFormat("{0}: {1}\n", T.AutoVentFluxStopHard,
            //             Texts.Accented(Units.Percentage(settings.VentFluxStopHard)));
            //     }
            // }
            // sb.AppendFormat("{0}: {1}\n", T.Codex,
            //     Texts.LinkToCodexManual(T.NounFlux, FluxPage.PageId));
            return sb.ToString();
        }

        public static FluxCapacitorStats GetFluxCapacitorStats(NPCShip ship) {
            var stats = new FluxCapacitorStats();
            var cs = ship?.CombatState;
            if (cs == null) { return stats; }
            var capacitors = cs.Capacitors;
            for (int i = capacitors.Count - 1; i >= 0; i--) {
                var cap = capacitors[i];
                var capacity = cap.CapacitorDef.Capacity;
                stats.Total++;
                stats.InstalledBonus += capacity;
                if (cap.Phase == ShipCapacitorPhase.Active && cap.IsReachable) {
                    stats.Active++;
                    stats.ActiveBonus += capacity;
                }
            }
            return stats;
        }

        public static string GetFluxCapacitorBonusText(FluxCapacitorStats stats) {
            return $"+{Units.XOutOfY(stats.ActiveBonus, stats.InstalledBonus)}";
        }

        public static string GetFluxCapacitorsTip(NPCShip ship) {
            if (ship == null) { return string.Empty; }
            var flux = ship.CombatState?.Flux;
            if (flux == null) { return string.Empty; }
            var stats = GetFluxCapacitorStats(ship);
            var sb = Caches.StringBuilder2;
            sb.Clear();
            // sb.Append(Texts.SubTitle(Texts.LinkToCodex(
            //     DefIdsH.ObjectsDevicesFluxCapacitor, T.NounFluxCapacitors)));
            if (stats.Total == 0) {
                sb.AppendLine(T.NotAvailable);
            } else {
                sb.AppendFormat("{0}: {1}\n", T.NounFluxCapacitors,
                    Texts.Accented(Units.XOutOfY(stats.Active, stats.Total)));
            }
            sb.AppendFormat("{0}: {1}\n", T.FluxCapacity, Texts.Accented(flux.Capacity));
            var baseCap = string.Format(" {0} {1}: +{2}\n",
                GlyphMap.ArrowRight, T.NounBaseCapacity, Tunables.ShipBaseFlux);
            sb.Append(Texts.Grey(baseCap));
            var classBonus = string.Format(" {0} {1}: +{2}\n",
                GlyphMap.ArrowRight, T.NounClassBonus, flux.Ship.Class.FluxBonus);
            sb.Append(Texts.Grey(classBonus));
            string capacitorBonus;
            if (stats.InstalledBonus > 0) {
                capacitorBonus = string.Format(" {0} {1}: +{2} ({3}: +{4})\n",
                    GlyphMap.ArrowRight, T.NounCapacitorBonus,
                    stats.ActiveBonus, T.Max, stats.InstalledBonus);
            } else {
                capacitorBonus = string.Format(" {0} {1}: +0\n",
                    GlyphMap.ArrowRight, T.NounCapacitorBonus);
            }
            sb.Append(Texts.Grey(capacitorBonus));
            if (flux.Ship.Class.MaxFluxCapacity > 0) {
                var maxCapacity = string.Format(" {0} {1}: {2}",
                    GlyphMap.ArrowRight, T.MaxFluxCapacity,
                    flux.Ship.Class.MaxFluxCapacity);
                sb.AppendLine(Texts.Grey(maxCapacity));
            }
            return sb.ToString();
        }

        public static FluxVentStats GetFluxVentStats(NPCShip ship) {
            var stats = new FluxVentStats();
            var cs = ship?.CombatState;
            if (cs == null) { return stats; }
            stats.MaxEffective = ship.Class.MaxFluxVents;
            var vents = cs.Vents;
            for (int i = vents.Count - 1; i >= 0; i--) {
                var vent = vents[i];
                var bonus = vent.VentDef.VentBonus;
                stats.Total++;
                stats.InstalledBonus += bonus;
                if (!vent.IsActive) { continue; }
                stats.Active++;
                if (stats.MaxEffective > 0 && stats.EffectiveActive >= stats.MaxEffective) {
                    continue;
                }
                stats.EffectiveActive++;
                stats.EffectiveBonus += bonus;
            }
            return stats;
        }

        public static bool SameFluxVentStats(FluxVentStats a, FluxVentStats b) {
            return a.Active == b.Active
                && a.Total == b.Total
                && a.EffectiveActive == b.EffectiveActive
                && a.MaxEffective == b.MaxEffective
                && Mathf.Approximately(a.EffectiveBonus, b.EffectiveBonus)
                && Mathf.Approximately(a.InstalledBonus, b.InstalledBonus);
        }

        public static string GetFluxVentBonusText(FluxVentStats stats) {
            var fb1 = FluxVentBonusValueText(stats.EffectiveBonus);
            var fb2 = FluxVentBonusValueText(stats.InstalledBonus);
            return $"+{fb1}/{fb2}/{T.UnitShortHour}";
        }

        public static string GetFluxVentsTip(NPCShip ship) {
            if (ship == null) { return string.Empty; }
            var flux = ship.CombatState?.Flux;
            if (flux == null) { return string.Empty; }
            var stats = GetFluxVentStats(ship);
            var sb = Caches.StringBuilder2;
            sb.Clear();
            // sb.Append(Texts.SubTitle(Texts.LinkToCodex(
            //     DefIdsH.ObjectsDevicesFluxVent, T.NounFluxVents)));
            if (stats.Total == 0) {
                sb.AppendLine(T.NotAvailable);
            } else {
                sb.AppendFormat("{0}: {1}\n", T.NounFluxVents,
                    Texts.Accented(Units.XOutOfY(stats.Active, stats.Total)));
                var className = Texts.Grey($"({ship.Class.CodexLink})");
                sb.AppendFormat("{0}: {1} {2}\n", T.Max,
                    Texts.Accented(stats.MaxEffective), className);
            }
            sb.AppendLine(flux.VentRateText);
            var baseVentRate = string.Format(" {0} {1}: +{2}/{3}\n",
                GlyphMap.ArrowRight, T.NounBaseVentRate,
                FluxVentBonusValueText(ship.Class.FluxSoftVentPerHour),
                T.UnitShortHour);
            sb.Append(Texts.Grey(baseVentRate));
            string ventBonus;
            if (stats.InstalledBonus > 0f) {
                ventBonus = string.Format(" {0} {1}: +{2}/{5} ({3}: +{4}/{5})\n",
                    GlyphMap.ArrowRight, T.NounVentBonus,
                    FluxVentBonusValueText(stats.EffectiveBonus),
                    T.Max, FluxVentBonusValueText(stats.InstalledBonus), T.UnitShortHour);
                sb.Append(Texts.Grey(ventBonus));
            }
            AppendFluxVentRateMultiplier(flux, sb);
            return sb.ToString();
        }

        private static string FluxVentBonusValueText(float value) {
            return string.Format("{0:0.#}", value);
        }

        private static void AppendFluxVentRateMultiplier(ShipFlux flux, StringBuilder sb) {
            float multiplier;
            string label;
            if (flux.State == ShipFluxState.Overload) {
                multiplier = Tunables.ShipFluxOverloadVentMul;
                label = T.FluxOverload;
            } else if (flux.State == ShipFluxState.Venting) {
                multiplier = Tunables.ShipFluxActiveVentMul;
                label = T.VentOpen;
            } else {
                return;
            }
            if (Mathf.Approximately(multiplier, 1f)) { return; }
            var text = string.Format(" {0} {1}: x{2}\n",
                GlyphMap.ArrowRight, label, FluxVentMultiplierText(multiplier));
            sb.Append(Texts.Grey(text));
        }

        private static string FluxVentMultiplierText(float value) {
            return string.Format("{0:0.##}", value);
        }

        public static string GetManualFireTip(BattleStationComp bs) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            if (bs.WeaponDef == null) {
                sb.AppendLine(T.InfoDeviceUnconfigured);
                return sb.ToString();
            }
            sb.Append(Texts.SubTitle($"{bs.WeaponDef.Def.CodexLink} x{bs.AssignedWeapons.Value}"));

            if (bs.PreferredTargetType != null) {
                sb.AppendFormat("{0} {1} {2}\n",
                    T.NounTarget, GlyphMap.ArrowRight, Texts.Accented(bs.PreferredTargetType.NameT));
            }

            var operatable = bs.Operatable;
            // An operator killed mid-fight can stay referenced with its data already gone,
            // and this tooltip refreshes every frame while hovered
            var opData = bs.IsOperatorPresent ? operatable.Operator.Data : null;
            if (opData?.Mind != null) {
                if (!bs.EffectiveManualFireMode) {
                    sb.AppendLine(Texts.Green(T.WeaponsAutofire));
                } else {
                    sb.AppendLine(Texts.Accented(T.NounManualFire));
                }
                var shootingLevel = opData.Mind.Jobs.LevelOf(JobTypeIdH.Shooting);
                sb.AppendFormat("{0}: {1} {2}\n", T.NounOperator,
                    Texts.LinkToBeing(opData, opData.Mind.Name.ShortName),
                    Texts.Grey($"({T.JobTypeShooting}: {shootingLevel})"));
                sb.AppendFormat("{0}: {1}\n", T.AccuracyBonus,
                    Units.PercentageSigned(bs.OperatorAccuracyBonus - 1f));
            } else if (operatable.HasGoal) {
                sb.AppendLine(Texts.Accented(T.NounManualFire));
                sb.AppendLine(Texts.Yellow(T.WaitingForOperator));
            } else {
                sb.AppendLine(Texts.Accented(T.NounManualFire));
            }

            var flux = bs.S?.Universe?.Player?.Ship.CombatState.Flux;
            if (flux == null) {
                return sb.ToString();
            }
            if (bs.GetManualFireFluxPreview(out var futurePct, out var willOverload)) {
                sb.AppendFormat("{0}: {1} {2} {3}\n", T.FluxAccumulated,
                    Units.PercentageColoredInverse(flux.TotalPercent),
                    GlyphMap.ArrowRight,
                    willOverload ? Texts.Red(Units.Percentage(futurePct))
                        : Units.PercentageColoredInverse(futurePct));
                if (willOverload) {
                    if (Mathf.RoundToInt((float) (GameTimer.realtimeSinceStartupAsDouble % 0.5d) * 2f) == 0) {
                        sb.AppendLine(Texts.Red(T.ManualFireWillOverload));
                    } else {
                        sb.AppendLine(Texts.Yellow(T.ManualFireWillOverload));
                    }
                }
            }

            if (!The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                sb.Append('\n');
                sb.AppendFormat(Texts.Align(Texts.Grey(T.HoldButtonForMoreInfo_V1.F(
                    The.Bindings.GetControlName(ActionType.CtrlModifier))), "Right"));
            } else {
                AppendWeaponStatuses(bs, sb);
            }
            return sb.ToString();
        }

        private static void AppendWeaponStatuses(BattleStationComp bs, StringBuilder sb) {
            var cs = bs.S.Universe.Player.Ship.CombatState;
            foreach (var w in cs.Weapons) {
                if (w.BackingStation != bs) { continue; }
                var name = w.EntityName;
                string status;
                var enode = w.Comp?.Tile.ENode;
                if (enode != null && !enode.IsSwitchedOn) {
                    status = Texts.Red(T.InfoEnergyNodeOff);
                } else if (enode != null && !enode.IsConnected) {
                    status = Texts.Red(T.ElDeviceDisconnected);
                } else if (enode != null && !enode.IsReachable) {
                    status = Texts.Red(T.AdjUnreachable);
                } else {
                    switch (w.Phase) {
                        case ShipWeaponPhase.Ready:
                        case ShipWeaponPhase.Firing:
                            status = Texts.Green(w.PhaseName);
                            break;
                        case ShipWeaponPhase.Charging:
                        case ShipWeaponPhase.Windup:
                            status = Texts.Grey(w.PhaseName);
                            break;
                        case ShipWeaponPhase.Disabled:
                        case ShipWeaponPhase.OutOfAmmo:
                        case ShipWeaponPhase.Inactive:
                            status = Texts.Red(w.PhaseName);
                            break;
                        default:
                            status = w.PhaseName;
                            break;
                    }
                }
                if (w.WeaponDef.AmmoCapacity > 0) {
                    status = $"[{T.Ammo}: {Units.XOutOfY(w.Ammo, w.WeaponDef.AmmoCapacity)}] {status}";
                }
                sb.AppendFormat("\n{0}: {1}", name, status);
            }
            sb.Append('\n');
        }

        public static string GetThrustersTip(NPCShip ship) {
            if (ship == null) { return string.Empty; }
            var sb = Caches.StringBuilder2;
            sb.Clear();
            var cs = ship.CombatState;
            cs.GetEvasionPercentBonus(out var curBonus, out var maxBonus);
            var totalMax = Texts.Grey($"({T.Max}: {Units.Percentage(Tunables.ShipMaxEvadePercent)})");
            sb.AppendFormat("{0}: {1} {2}\n", T.NounEvade,
                Texts.Accented(Units.Percentage(cs.EvasionPercent)), totalMax);
            var className = Texts.Grey($"({ship.Class.CodexLink})");
            sb.AppendFormat("{0}: {1} {2}\n", T.ShipClassBaseEvade,
                Texts.Accented(Units.Percentage(ship.Class.BaseEvasion)), className);
            if (maxBonus > 0f) {
                var bonusMax = Texts.Grey($"/{Units.Percentage(maxBonus)}");
                sb.AppendFormat("{0}: {1}{2}{3}\n",
                    T.ShipEvadeThrusterBonus, Texts.Accented("+"),
                    Texts.Accented(Units.Percentage(curBonus)), bonusMax);
            }
            if (cs.TryGetBestPilotingBonus(out var pilot, out var level, out var pilotBonus)) {
                sb.AppendFormat("{0} {1}: {2} {3} {4}{5}\n",
                    Texts.LinkToBeing(pilot.Data), T.JobTypePiloting,
                    Texts.Accented(level), GlyphMap.ArrowRight,
                    Texts.Accented("+"), Texts.Accented(Units.Percentage(pilotBonus)));
            }
            return sb.ToString();
        }
    }
}
