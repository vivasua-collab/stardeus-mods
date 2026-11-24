using System;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using MessagePack;
using Game.Achievements;
using Game.Systems.UIUX;
using KL.Signals;

namespace Game.Data.Economy {
    [MessagePackObject]
    public sealed class Wallet {
        [Key("Amount")] public double Amount;
        [IgnoreMember] public bool IsEmpty => Amount == 0d;
        [IgnoreMember] public bool IsPlayer;
        private B<double> amountB;
        [IgnoreMember] public B<double> AmountB => amountB ??= new B<double>(Amount);
        private static readonly HashSet<int> achieved = new();

        public void Add(double amount) {
            Amount += amount;
            amountB?.Set(Amount);
            if (IsPlayer && amount > 0d) {
                CheckAchievements(Amount);
            }
        }
        private void CheckAchievements(double amt) {
            var achievements = AchievementMoney.Achievements;
            if (achieved.Count == achievements.Count) {
                return;
            }
            foreach (var a in achievements) {
                if (a.Value <= amt) {
                    if (achieved.Add(a.Key)) {
                        var achId = AchievementMoney.AchievementsH[a.Key];
                        The.AchievementTracker.Unlock(achId);
                    }
                }
            }
        }


        public bool CanAfford(double value) {
            return value <= Amount;
        }

        public bool TryPay(double value, Wallet to) {
            if (Amount >= value) {
                Amount -= value;
                amountB?.Set(Amount);
                to?.Add(value);
                return true;
            }
            return false;
        }

        [IgnoreMember] public string ValueString => ToValueString(Amount);

        public static string ToValueString(double value, bool abbrev = true,
                bool withSymbol = true) {
            string suffix;
            var sign = Math.Sign(value);
            value = Math.Abs(value);
            if (abbrev) {
                if (value > 100 && value < int.MaxValue) {
                    value = Math.Round(value);
                }
                if (value >= 1_000_000_000) {
                    value /= 1_000_000_000;
                    suffix = "B";
                } else if (value >= 1_000_000) {
                    value /= 1_000_000;
                    suffix = "M";
                } else if (value >= 1_000) {
                    value /= 1_000;
                    suffix = "K";
                } else {
                    suffix = string.Empty;
                }
            } else {
                suffix = string.Empty;
            }
            string format;
            if (sign < 0) {
                format = withSymbol ? FormatNegative : FormatNegativeRaw;
            } else {
                format = withSymbol ? Format : FormatRaw;
            }
            return string.Format(format, value, suffix);
        }
        // Don't use it directly! use ToValue String
        private const string FormatRaw = "{0:0.##}{1}";
        private const string FormatNegativeRaw = "-{0:0.##}{1}";
        private const string Format = "${0:0.##}{1}";
        private const string FormatNegative = "-${0:0.##}{1}";
    }
}
