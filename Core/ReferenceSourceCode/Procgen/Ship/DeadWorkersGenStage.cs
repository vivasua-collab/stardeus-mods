using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Game.Systems.Naming;

namespace Game.Procgen {
    public class DeadWorkersGenStage : BaseShipGenStage {
        public override void Apply() {
            var q = new Queue<int>();
            // q.Enqueue(SpeciesIdH.Robot);
            // q.Enqueue(SpeciesIdH.Drone);
            // q.Enqueue(SpeciesIdH.Carrier);
            q.Enqueue(SpeciesIdH.CleaningBot);
            var globalAttempts = 1000;
            var r = rng.Fork();
            var islands = S.Sys.Areas.Islands.OrderBy(v => r.Value);
            while (q.Count > 0 && globalAttempts-- > 0) {
                foreach (var island in islands) {
                    var attempts = 10;
                    var isleList = new List<int>(island.Value);
                    isleList.Sort();
                    do {
                        var pos = r.From(isleList);
                        isleList.Remove(pos);
                        if (!IsWithinGenArea(pos)) { continue; }
                        if (EntityUtils.IsEmptyFloorAt(pos)) {
                            var workerSpecies = Species.Get(q.Dequeue());
                            var vars = new GenBeingVars(workerSpecies, BeingType.Neutral);
                            vars.Body = FactionDef.RandomBodyVars(rng);
                            var data = S.Query.GenBeingFull.Ask(vars , r);

                            var b = S.Beings.Create(pos, data);
                            var woundTimeAgo = r.Range(Consts.TicksPer3Days, Consts.TicksPerWeek);
                            BeingUtils.KillHistorical(S, data, woundTimeAgo, r, new DamageType[] {
                                DamageType.Energy,
                                DamageType.Sharp,
                                DamageType.Piercing,
                                DamageType.Explosive,
                            });
                        }
                        if (q.Count == 0) { return; }
                    } while (attempts-- > 0);
                }
            }
        }
    }
}
