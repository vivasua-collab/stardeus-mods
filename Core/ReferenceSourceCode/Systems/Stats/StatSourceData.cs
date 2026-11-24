using Game.Components;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.Stats {
    public struct StatSourceData {
        public int StatId;
        public float CompoundLimit;
        public float StateMul;
        public float QualityMul;
        public float Amount;

        public static StatSourceData[] Deserialize(ComponentConfig[] data, object ctx) {
            if (data == null) { return null; }
            var res = new StatSourceData[data.Length];
            for (int i = data.Length - 1; i >= 0; i--) {
                ComponentConfig ps = data[i];
                float amt = ps.GetFloat(PropertyIdH.Amount, 0f);
                float limit = ps.GetFloat(PropertyIdH.CompoundLimit, 0f);
                if (amt == 0f) {
                    var amtPerH = ps.GetFloat(PropertyIdH.AmountPerHour, 0f);
                    if (amtPerH != 0f) {
                        amt = amtPerH / Consts.TicksPerHour;
                        limit /= Consts.TicksPerHour;
                    }
                }
                if (amt == 0f) {
                    var amtPerD = ps.GetFloat(PropertyIdH.AmountPerDay, 0f);
                    if (amtPerD != 0f) {
                        amt = amtPerD / Consts.TicksPerDay;
                        limit /= Consts.TicksPerDay;
                    }
                }
                if (amt == 0f) {
                    var amtPerM = ps.GetFloat(PropertyIdH.AmountPerMinute, 0f);
                    if (amtPerM != 0f) {
                        amt = amtPerM / Consts.TicksPerMinute;
                        limit /= Consts.TicksPerMinute;
                    }
                }
                if (amt == 0f) {
                    var tempCels = ps.GetFloat(PropertyIdH.TemperatureCelsius, 0f);
                    if (tempCels != 0f) {
                        amt = Units.CelsiusToKelvin(tempCels);
                        if (limit != 0f) {
                            limit = Units.CelsiusToKelvin(limit);
                        }
                    }
                }
                if (amt == 0f) {
                    D.Err("StatSource amount is zero for {0}. Context: {1}",
                        ps.Component, ctx);
                }
                var ss = new StatSourceData {
                    StatId = ps.ComponentH,
                    Amount = amt,
                    StateMul = ps.GetFloat(PropertyIdH.StateMul, 0f),
                    QualityMul = ps.GetFloat(PropertyIdH.QualityMul, 0f),
                    CompoundLimit = limit,
                };
                res[i] = ss;
            }
            return res;
        }
    }
}
