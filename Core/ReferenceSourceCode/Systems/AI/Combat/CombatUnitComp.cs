#if UNITY_EDITOR
#define DEBUG_COMBAT
#endif
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using System;

namespace Game.Systems.AI.Combat {
    public sealed class CombatUnitComp : BaseComponent<CombatUnitComp>, IThreat {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CombatUnitComp());
        }
        private bool isThreatAdded;
        private bool isSuppressionThoughtBubbleShowing;
        private CombatUnit Unit => Being.AIAgent.CombatUnit;
        public int ThreatType => ThreatIdH.Being;
        public bool IsThreatActive => Being.IsAlive && !Being.Data.IsFriendly
            && !Being.Data.Body.IsIncapacitated;
        public bool AffectsSoundtrack => false;
        public string SubmenuTitle => T.CombatUnit;
        private void CheckThreat() {
            if (!isThreatAdded) {
                if (Being.Data.Mind == null || !IsThreatActive) {
                    // D.Err("Checking threat when mind is null! {0}", Being);
                    return;
                }
                if (Being.IsAlive && Being.Data.EffectiveFaction?.IsPlayer != true) {
                    AddThreat();
                }
            } else {
                if (!IsThreatActive) {
                    RemoveThreat();
                }
            }
        }

        private void AddThreat() {
            if (!isThreatAdded) {
                isThreatAdded = true;
                S.Sig.RegisterThreat.Send(this, true);
            }
        }

        public void RemoveThreat() {
            if (isThreatAdded) {
                isThreatAdded = false;
                S.Sig.RegisterThreat.Send(this, false);
            }
        }

        public override void PostLateReady(bool wasLoaded) {
            var unit = Unit;
            if (unit == null) { return; }
            unit.Comp = this;
            SyncSuppressionThoughtBubble(unit);
        }

        public void ShowSuppressionThoughtBubble() {
            if (isSuppressionThoughtBubbleShowing) { return; }
            isSuppressionThoughtBubbleShowing = true;
            ShowThoughtBubble(IconId.CDazed, T.AdjSuppressed);
        }

        public void HideSuppressionThoughtBubble() {
            if (!isSuppressionThoughtBubbleShowing) { return; }
            isSuppressionThoughtBubbleShowing = false;
            HideThoughtBubble();
        }

        public void SyncSuppressionThoughtBubble(CombatUnit unit) {
            if (unit?.IsSuppressed == true) {
                ShowSuppressionThoughtBubble();
            } else {
                HideSuppressionThoughtBubble();
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                HideSuppressionThoughtBubble();
                RemoveThreat();
            } else if (message == MsgIdH.FactionChanged) {
                CheckThreat();
            } else if (message == MsgIdH.BeingFinishedLoading) {
                CheckThreat();
            } else if (message == MsgIdH.BodyConditionChanged) {
                CheckThreat();
            } else if (message == MsgIdH.ImmobilizeEnded) {
                CheckThreat();
            } else if (message == MsgIdH.ImmobilizeStarted) {
                CheckThreat();
            }
        }

        public override void OnRemove() {
            HideSuppressionThoughtBubble();
            var unit = Unit;
            S.Sys.Combat.RemoveUnit(unit);
            if (unit?.Comp == this) {
                unit.Comp = null;
            }
            RemoveThreat();
        }

        public static string IconFor(CombatUnitConfig cfg) {
            switch (cfg) {
                case CombatUnitConfig.Fight: return IconId.CCombat;
                case CombatUnitConfig.FreeWill: return IconId.CTraits;
                case CombatUnitConfig.Flee: return IconId.CTraffic;
                case CombatUnitConfig.Ignore: return IconId.CStop;
            }
            D.Err("Unhandled combat config name: {0}", cfg);
            return cfg.ToString();
        }
        public static string NameFor(CombatUnitConfig cfg) {
            switch (cfg) {
                case CombatUnitConfig.Fight: return T.VerbFight;
                case CombatUnitConfig.FreeWill: return T.NounFreeWill;
                case CombatUnitConfig.Flee: return T.VerbFlee;
                case CombatUnitConfig.Ignore: return T.VerbIgnore;
            }
            D.Err("Unhandled combat config name: {0}", cfg);
            return cfg.ToString();
        }

        private static readonly List<string> cfgOpts = new();
        public static List<string> CfgOpts {
            get {
                if (cfgOpts.Count == 0) {
                    foreach (CombatUnitConfig opt in Enum.GetValues(typeof(CombatUnitConfig))) {
                        cfgOpts.Add(NameFor(opt));
                    }
                }
                return cfgOpts;
            }
        }
        public override string ToString() {
            return $" * CombatUnit[{Unit}]";
        }
    }
}
