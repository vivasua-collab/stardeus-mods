using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using Game.Data.Factions;
namespace Game.Tools {
    public sealed class ToolSpawnBeing : Tool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolSpawnBeing());
        }
        public override bool IsDevTool => true;
        public override bool IsBuild => true;
        public const string ToolId = "spawn_being";
        public override string Id => ToolId;
        public override string MenuIcon => IconId.WPerson;
        public override bool IsConfigurable => true;
        private List<string> availableParams;
        private Def currentDef;
        public override List<string> AvailableParams =>
            availableParams ??= The.Defs.Defs.Values.Where(
                v => !v.IsAbstract && v.LayerId == WorldLayer.FreeEntities
                    && v.IsBeing)
                .Select(v => v.Id).ToList();

        public override string CurrentParam => currentDef?.Id;
        public override bool Configure(string param) {
            currentDef = The.Defs.Get(param);
            return currentDef != null;
        }

        public override void OnClickDown(Vector2 coords) {
            var species = Species.ForDef(currentDef.IdH);
            if (CmdSpawnBeing.CanSpawnAt(coords)) {
                var vars = new GenBeingVars(species, BeingType.Friendly);
                vars.Body = FactionDef.RandomBodyVars(Rng.Unseeded);
                // #if UNITY_EDITOR
                // vars.Body.ChanceCustomMind = 1f;
                // vars.Body.ChanceModded = 1f;
                // #endif
                BeingUtils.SpawnBeingWithEquipment(S, species.IdH, coords, vars: vars);
            }
        }

        public override string ToString() {
            if (currentDef != null) {
                return string.Format("{0} ({1})", Id, currentDef.Id);
            } else {
                return string.Format("{0} (unconfigured)", Id);
            }
        }

        public override string MenuNameFor(string param) {
            return The.Defs.Get(param).NameKey;
        }

        public override string MenuIconFor(string param) {
            return The.Defs.Get(param).Preview;
        }

        //STRESS TEST
        private Vector2 lastSpawnPos;
        public override void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance) {
            if (Vectors.SqrDistance(lastSpawnPos, pos) > 1) {
                if (Rng.UChance(0.8f)) {
                    OnClickDown(pos);
                }
                lastSpawnPos = pos;
            }
        }
    }
}
