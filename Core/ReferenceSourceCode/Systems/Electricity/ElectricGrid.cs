using System;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using MessagePack;
using Game.Input;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Systems.Electricity {
    [MessagePackObject]
    public struct ElectricGrid {
        [IgnoreMember] public bool IsInitialized => Size != 0;
        [Key("Id")] public int Id;
        [Key("HasCPU")] public bool HasShipComputer;
        [Key("Size")] public int Size;
        // Theoretical max output the grid could reach if everything was burning
        [Key("MaxOut")] public float MaxOutput; // max output possible
        [Key("MaxOutWOBat")] public float MaxOutputWithoutBat; // max output possible w/o batteries
        [Key("MaxOutBat")] public float MaxOutputBat; // max outputs possible by batteries
        [Key("Out")] public float Output; // sum of all outputs
        [Key("OutRenew")] public float OutputRenewable; // non-smart (not consuming any materials)
        [Key("OutWOBat")] public float OutputWithoutBat; // sum of all active non-bat outputs
        [Key("OutBat")] public float OutputBat; // sum of all active battery outputs
        [Key("Consumption")] public float Consumption; // actual consumption of all devices without batteries
        [Key("BatChargeAdd")] public float BatChargeAdded; // how much did battery charging consumed this tick
        [Key("BatCharge")] public float BatCharge; // sum of all battery charge levels
        [Key("BatCap")] public float BatCapacity; // sum of all battery capacities
        [Key("LastDeficit")] public float LastDeficit; // deficit during last grid tick
        [Key("LastDeficitTick")] public long LastDeficitTick; // deficit during last grid tick
        [Key("Deficit")] public float Deficit; // deficit this grid tick
        [Key("Disk")] public int Disk;
        [Key("Mem")] public int Memory;
        [Key("CPU")] public int CPU;
        [Key("GPU")] public int GPU;
        [Key("NumShipCPUs")] public int NumShipCPUs;

        [IgnoreMember] public string CPUT => T.CpuModuleAmount_V1.F(CPU);

        public string BonusTooltipFor(float pct, string statName, string finalValue) {
            var pctBonus = pct - 1f;
            string pctBonusTxt;
            if (Maths.Approximately(pctBonus, 0f)) {
                pctBonusTxt = Texts.Grey("0%");
            } else {
                pctBonusTxt = Units.PercentageColoredSigned(pctBonus, green: 0.001f, yellow: 0f);
            }
            var linkToCodex = Texts.LinkToCodexManual(T.CpuBonus, ComputerResourcesBonusPage.PageId);
            var bonusText = Texts.GreyParentheses($"{Texts.Accented(CPUT)} {linkToCodex}");
            if (finalValue != null) {
                return $"{statName} {pctBonusTxt} {bonusText} {GlyphMap.ArrowRight} {finalValue}";
            }
            return $"{statName} {pctBonusTxt} {bonusText}";
        }

        public override string ToString() {
            #if !PRODUCTION_BUILD
            return D.DumpInstance(this);
            #else
            return $"ElectricGrid[{Id}]";
            #endif
        }
    }
}
