using System.Collections.Generic;
using System.Linq;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Backstories {
    [MessagePackObject]
    public struct ExperienceData {
        [Key("Roles")] public (AgePhase, string)[] Roles;
        public static ExperienceData Serialize(Experience exp) {
            if (exp == null || exp.Roles == null) {
                return default;
            }
            var d = new ExperienceData();
            d.Roles = new (AgePhase, string)[exp.Roles.Count];
            int i = 0;
            foreach (var r in exp.Roles) {
                d.Roles[i++] = (r.Key, r.Value.Id);
            }
            return d;
        }

        public static Experience Deserialize(Mind mind, ExperienceData data) {
            var exp = new Experience();
            exp.Owner = mind;
            // Load what was saved: wild creatures, clones and babies have an empty backstory,
            // a mind that never ran the backstory step has none at all
            if (data.Roles == null) {
                return exp;
            }
            exp.Roles = new Dictionary<AgePhase, BeingRole>();
            for (int i = 0; i < data.Roles.Length; i++) {
                (AgePhase, string) r = data.Roles[i];
                var role = BeingRole.Get(r.Item2);
                if (role == null) {
                    D.Err("Could not find saved being role by id {0} for {1}",
                        r.Item2, mind);
                    continue;
                }
                exp.Roles.Add(r.Item1, role);
            }
            return exp;
        }
    }
}
