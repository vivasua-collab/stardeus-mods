using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
namespace Game.Tools {
    public sealed class ToolSpawnObj : Tool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolSpawnObj());
        }
        public override bool IsDevTool => true;
        public override bool IsBuild => true;
        public const string ToolId = "spawn_obj";
        public override string Id => ToolId;
        public override string MenuIcon => IconId.WPackage;
        public override bool IsConfigurable => true;
        private List<string> availableParams;
        private Def currentDef;
        public override List<string> AvailableParams =>
            availableParams ??= The.Defs.Defs.Values.Where(
                v => !v.SkipToolListing &&
                !v.IsAbstract &&
                v.LayerId == WorldLayer.FreeEntities &&
                v.Id.StartsWith("Obj"))
                    .Select(v => v.Id).ToList();

        public override string CurrentParam => currentDef?.Id;
        public override bool Configure(string param) {
            currentDef = The.Defs.Get(param);
            return currentDef != null;
        }

        public override void OnClickDown(Vector2 coords) {
            if (EntityUtils.IsWithinBounds(coords)) {
                if (currentDef.MatType != null) {
                    var amt = 1;
                    if (The.Bindings.IsPressed(Input.ActionType.CtrlModifier)) {
                        amt = 10;
                    } else if (The.Bindings.IsPressed(Input.ActionType.ShiftModifier)) {
                        amt = 100;
                    } else if (The.Bindings.IsPressed(Input.ActionType.AltModifier)) {
                        amt = 1000;
                    }
                    EntityUtils.SpawnRawMaterial(currentDef.MatType, amt, EntityUtils.ToPos(coords),
                        0.1f, true, false);
                } else {
                    bool force = false;
                    // #if UNITY_EDITOR
                    // force = true;
                    // #endif
                    S.CmdQ.Enqueue(new CmdSpawnObj(coords, currentDef, force: force));
                }
            }
        }

        public override Def DefFor(string param) {
            return The.Defs.Get(param);
        }

        public override string MenuNameFor(string param) {
            return The.Defs.Get(param).NameKey;
        }
        public override string MenuIconFor(string param) {
            return The.Defs.Get(param).Preview;
        }

        public override string ToString() {
            if (currentDef != null) {
                return string.Format("{0} ({1})", Id, currentDef.Id);
            } else {
                return string.Format("{0} (unconfigured)", Id);
            }
        }

        //STRESS TEST
        private Vector2 lastSpawnPos;
        public override void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance) {
            if (Vectors.SqrDistance(lastSpawnPos, pos) > 1) {
                if (Rng.UChance(0.6f)) {
                    OnClickDown(pos);
                }
                lastSpawnPos = pos;
            }
        }
    }
}
