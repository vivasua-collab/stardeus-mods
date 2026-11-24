using System;
using Game.Data;
using KL.Grid;
using UnityEngine;
using KL.Utils;
using Game.Systems.Creatures;
using KL.Randomness;
using Game.Systems.Naming;

namespace Game.Commands {
    public class CmdSpawnBeing : Command {
        private BeingData data;
        public Being SpawnedBeing;
        private Vector2 position;
        private GenBeingVars vars;
        private int posIdx;
        private readonly Action<Being> afterSpawn;

        public CmdSpawnBeing(BeingData data, Vector2 position, Action<Being> afterSpawn = null) : base("spawn_being") {
            this.data = data;
            this.position = position;
            this.posIdx = EntityUtils.ToPos(position);
            this.afterSpawn = afterSpawn;
            if (Pos.IsOOB(posIdx)) {
                D.Err("Trying to spawn a being outside margin! Ignoring: {0}", data);
                return;
            }
        }
        public CmdSpawnBeing(BeingData data, int posIdx, Action<Being> afterSpawn = null) : base("spawn_being") {
            this.data = data;
            this.posIdx = posIdx;
            this.position = EntityUtils.CenterOf(posIdx);
            this.afterSpawn = afterSpawn;
            if (Pos.IsOOB(posIdx)) {
                D.Err("Trying to spawn a being outside margin! Ignoring: {0}", data);
                return;
            }
        }
        public CmdSpawnBeing(GenBeingVars vars, int posIdx, Action<Being> afterSpawn = null) : base("spawn_being") {
            this.vars = vars;
            this.posIdx = posIdx;
            this.position = EntityUtils.CenterOf(posIdx);
            this.afterSpawn = afterSpawn;
            if (Pos.IsOOB(posIdx)) {
                D.Err("Trying to spawn a being outside margin! Ignoring: {0}", data);
                return;
            }
        }
        public CmdSpawnBeing(GenBeingVars vars, Vector2 position, Action<Being> afterSpawn = null) : base("spawn_being") {
            this.vars = vars;
            this.position = position;
            this.posIdx = EntityUtils.ToPos(position);
            this.afterSpawn = afterSpawn;
            if (Pos.IsOOB(posIdx)) {
                D.Err("Trying to spawn a being outside margin! Ignoring: {0}", data);
                return;
            }
        }

        protected override void Execute() {
            if (Pos.IsOOB(posIdx)) {
                return;
            }
            if (!EntityUtils.IsEmptyFloorAt(posIdx)) {
                var obj = EntityUtils.GetTopmostParentTile(posIdx);
                if (obj != null) {
                    var radius = obj.Size.magnitude + 1;
                    var newPosition = EntityUtils.SpawnableAround(posIdx, radius, false);
                    if (Ready.Gen) { // possible to get it in generation stages
                        D.Warn("Tried to spawn a being inside an object, auto adjusting spawn pos! {0} {1}->{2}",
                            obj, position, newPosition);
                    }
                    position = newPosition;
                }
            }

            if (data == null) {
                data = S.Query.GenBeingFull.Ask(vars, Rng.Unseeded);
            }
            SpawnedBeing = S.Beings.Create(position, data);
            if (SpawnedBeing == null) {
                D.Err("Failed creating being at pos {0}. Data: {1}", position, data);
                return;
            }
            S.Sig.BeingChanged.Send(SpawnedBeing);
            afterSpawn?.Invoke(SpawnedBeing);
        }

        public static bool CanSpawnAt(Vector2 coords) {
            return EntityUtils.IsWithinBounds(coords);
        }
    }
}
