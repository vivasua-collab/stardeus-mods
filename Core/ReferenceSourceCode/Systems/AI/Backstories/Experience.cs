using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Backstories {
    public sealed class Experience {
        public Mind Owner;
        /// <summary>
        /// Each age phase will have one role. Young beings will have less roles,
        /// older ones will be more experienced and more skilled.
        /// </summary>
        public Dictionary<AgePhase, BeingRole> Roles;

        public static Experience BlankFor(Mind mind) {
            var exp = new Experience();
            exp.SetOwner(mind);
            return exp;
        }

        public void SetOwner(Mind mind) {
            Owner = mind;
        }

        public string BackstoryFor(BeingData data) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            foreach (var role in Roles) {
                var phText = Texts.AgePhase(role.Key, data.Species);
                if (phText != null) {
                    sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, phText, Texts.Accented(role.Value.NameT));
                } else {
                    sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, Texts.Accented(role.Value.NameT));
                }
            }
            return sb.ToString();
        }

        public void ApplyModifiersAfterLoad(GameState s, bool isLoading) {
            if (Roles == null) { return; }
            foreach (var role in Roles.Values) {
                role.ApplyModifiersAfterLoadBeing(s, Owner.Owner, isLoading);
            }
        }
    }
}
