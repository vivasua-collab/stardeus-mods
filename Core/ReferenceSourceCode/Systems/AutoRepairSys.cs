using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;

namespace Game.Systems {
    public sealed class AutoRepairSys : GameSystem {
        public const string SysId = "AutoRepairSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new AutoRepairSys());
        }

        private static readonly Func<DamageableComp, bool> IsDamaged = (d) => d.IsDamaged;
        private readonly List<DamageableComp> damaged = new();

        protected override void OnInitialize() {
            S.Sig.CombatChanged.AddListener(OnCombatChanged);
        }

        private void OnCombatChanged(bool active, CombatFlags flags) {
            if (active) { return; }
            if ((flags & CombatFlags.Hostile) != CombatFlags.Hostile) { return; }
            // Combat is over - retry the auto-repair tasks that were suppressed
            // while beings would have had to walk into the fight to reach them.
            S.Components.FindAllMatching(damaged, IsDamaged);
            foreach (var d in damaged) {
                d.TryAutoRepair();
            }
            damaged.Clear();
        }

        public override void Unload() {
        }
    }
}
