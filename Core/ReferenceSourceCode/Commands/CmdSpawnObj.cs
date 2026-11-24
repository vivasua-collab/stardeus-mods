using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.Items;
using System;
using KL.Randomness;

namespace Game.Commands {
    public class CmdSpawnObj : Command {
        private readonly Vector2 position;
        private readonly Def def;
        public Obj Obj;
        private bool adjustSpawnPos;
        private bool force;

        public CmdSpawnObj(Vector2 pos, Def def, bool force,
                bool adjustSpawnPos = true) : base("spawn_obj") {
            position = pos;
            this.adjustSpawnPos = adjustSpawnPos;
            this.def = def;
            this.force = force;
        }

        protected override void Execute() {
            if (force || EntityUtils.CanPlaceObjAt(position, true)) {
                Obj = S.Objs.Create(position, def, adjustSpawnPos);
                if (Obj == null) {
                    D.Err("Failed to create an object: {0} at {1}",
                        def.Id, position);
                }
            } else {
                D.Err("Refusing to spawn {0} at {1}", def.Id, position);
            }
        }
    }
}
