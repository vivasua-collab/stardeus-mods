using System;
using System.Collections;
using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Ships {
    // A ship that stays in a sector it nuked is destroyed for real: without a ship
    // computer the save itself is a lost game, so a No Reload colony cannot load its way
    // past the game over (ElectricitySys ends the game again on load).
    public static class NukedShipDestruction {
        // Runs inside the game over sequence with user input off, so a failure here must
        // not end that sequence
        public static IEnumerator Destroy(GameState s) {
            try {
                RemoveShipComputers(s);
                KillEveryone(s);
            } catch (Exception e) {
                D.Err("Failed destroying the nuked ship: {0}", e);
            }
            yield return WreckHull(s);
        }

        private static void RemoveShipComputers(GameState s) {
            var cpus = s.Components.AllByType<ShipComputerComp>();
            if (Arrays.IsEmpty(cpus)) { return; }
            var tiles = new List<Tile>(cpus.Count);
            foreach (ShipComputerComp cpu in cpus) {
                tiles.Add(cpu.Tile);
            }
            foreach (var tile in tiles) {
                if (!tile.IsActive) { continue; }
                if (tile.Constructable != null && !tile.IsImmutable) {
                    tile.Constructable.BreakDown(0f, immediate: true, silent: true);
                } else {
                    new CmdRemoveTile(tile).Execute(s);
                }
            }
            if (s.Components.CountMatching<ShipComputerComp>(c => c.IsConstructed) > 0) {
                D.Err("A ship computer survived the sector nuke");
            }
        }

        private static void KillEveryone(GameState s) {
            var alive = new List<Being>(s.Beings.Alive);
            var source = NamedEntity.For(T.Explosion);
            foreach (var being in alive) {
                if (!being.IsAlive) { continue; }
                BeingUtils.Kill(s, being.Data, DamageType.Explosive, source, T.Explosion,
                    null, 0);
            }
        }

        private static IEnumerator WreckHull(GameState s) {
            Ready.SetGen(false, "Nuked ship");
            var factory = new ShipFactory(s.Sys.Areas.ShipBounds, s.Rng.Int, false);
            factory.Initialize(s, false);
            GenShip.AddDestructionStages(factory, new Rng(s.Rng.Int), 300);
            yield return factory.RunStages(s);
            Ready.SetGen(true, "Nuked ship");
            s.Sig.ForceAreas.Send();
        }
    }
}
