using KL.Grid;
using Game.Data;
using Game.Components;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using Game.Commands;
using System.Linq;
using Game.Tools;
using Game.Modifiers;
using KL.Randomness;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.Systems.Items;
using System;
using Game.Systems.Naming;
using Game.Systems.UIUX;

namespace Game.Procgen {
    public class ColonistGenStage : BaseShipGenStage, IWithScenarioConfig {
        private GameSetupData config;
        private readonly GenItemFlags equipment;
        public ColonistGenStage(GenItemFlags equipment) {
            this.equipment = equipment;
        }
        public override void Apply() {
            var spawned = SpawnColonists(S, config, equipment, FindColonistPos);
            var bd = Caches.BeingDataList;
            bd.Clear();
            foreach (var b in spawned) {
                D.Warn("Spawned colonist: {0} at {1}", b, b.Position);
                if (b.Data.Species.IsBiological && !b.Data.Species.IsAnimal) {
                    b.Data.StatsBeing.AIAlignment.UpdateBase(rng.Range(0.4f, 0.8f));
                    BeingUtils.UpdateFlagsFor(S, b.Data);
                }
                bd.Add(b.Data);
            }
            S.Query.GenerateRelationshipsFor.Ask(bd);
        }

        public static List<Being> SpawnColonists(GameState S, GameSetupData config,
                GenItemFlags equipment, Func<BeingData, int> posFunc, Rng rng = null) {
            var output = new List<Being>();
            foreach (var b in config.StartingBeings) {
                var species = b.Key;
                // human or pet survivors only!
                if (!species.IsBiological) { continue; }
                var list = b.Value;
                while (list.Count > 0) {
                    var data = GetBeingData(S, species, BeingType.StartingCrew, config);
                    if (equipment != GenItemFlags.None) {
                        BeingUtils.GenInventoryFor(data, rng ?? Rng.Unseeded, equipment);
                    }
                    var pos = posFunc(data);
                    if (pos == Pos.Invalid) {
                        D.Err("Failed resolving colonist pos for {0}. Fallback to center of the ship", data);
                        pos = S.Grid.CenterPos;
                    }
                    var cmd = new CmdSpawnBeing(data, pos);
                    cmd.Execute(S);
                    var being = cmd.SpawnedBeing;
                    if (being != null) {
                        Ready.WhenGame(being, "Make colonist damageable again", () => {
                            being.SetImmutable(being.Persona, false);
                        });
                    }
                    output.Add(being);
                }
            }
            return output;
        }

        private int FindColonistPos(BeingData data) {
            var humanPos = 0;
            if (spec.HasGenArea) {
                var subRng = rng.Fork();
                var area = spec.GenArea.Contents();
                area.Sort();
                foreach (var p in area.OrderBy(r => subRng.Value)) {
                    if (EntityUtils.IsEmptyFloorAt(p)) {
                        humanPos = p;
                        break;
                    }
                }
            } else {
                var shipCpuPos = spec.CPUPos;
                D.Ass(shipCpuPos > 0, "No cpu pos!");
                Pos.ForEachAroundRandom(rng, shipCpuPos, 5, false, true, (p) => {
                    if (EntityUtils.IsEmptyFloorAt(p)) {
                        humanPos = p;
                        return false;
                    }
                    return true;
                });
            }

            return humanPos;
        }

        public void SetConfig(GameSetupData config) {
            this.config = config;
        }
    }
}
