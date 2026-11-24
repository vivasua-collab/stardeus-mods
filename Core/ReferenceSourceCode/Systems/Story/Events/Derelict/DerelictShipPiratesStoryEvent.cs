using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Systems.AI.Combat;
using Game.Systems.Firex;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipPiratesStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipPirates";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new DerelictShipPiratesStoryEvent());
        }
        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn;
        }
        protected override int maxDerelictWidth => 90;
        protected override int maxDerelictHeight => 60;
        protected override int minLootCount => 20;
        protected override int maxLootCount => 40;
        // This derelict is an ambush: a pirate crew waiting for whoever boards, so it is a real
        // raid at the full budget and counts toward NumRaids. Crew remnants on other derelicts
        // (BaseDerelictShipStoryEvent.AddCombatCrewRemnantsStage) are not raids and never
        // touch CombatRaid. The old 2.0x was dropped: the budget already tracks the colony.
        private const int MinPirates = 4;

        protected override void AddPostStructureStages(ShipFactory factory, Rng rng) {
        }

        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
        }

        // Already spawns its own ambush raid in AfterGenerate below - the base guaranteed
        // crew remnants stage would double up on hostiles for this event.
        protected override void AddCombatCrewRemnantsStage(ShipFactory factory, Rng rng) { }

        public override void AfterGenerate(GameState s, ShipFactory factory, Rng rng) {

            var positions = new List<int>();
            factory.Spec.Bounds.ForEachExpanded((p) => {
                if (EntityUtils.IsEmptyFloorWithIntegrityAt(p)) {
                    positions.Add(p);
                }
            }, 1);
            var pos = rng.From(positions);
            var S = A.S;

            var raid = new CombatRaid(S, rng.Int);
            raid.PosIdx = pos;
            raid.MinRaiders = MinPirates;
            if (!raid.TryStartRaid(out var army)) {
                return;
            }
            // I've set this because the pirates would otherwise start
            // attacking walls of their own ship
            army.HomeArea = factory.Spec.Bounds;

            var bounds = army.HomeArea.ToBounds();
            bounds.Encapsulate(army.HomeArea.ToBounds());
            var shipBounds = S.Sys.Areas.ShipBounds.ToBounds();
            bounds.Encapsulate(shipBounds);

            s.Sig.CameraFocus.Send(bounds.center, bounds.extents, 0f);

            var anchor = pos;
            var pusher = pos;
            var cpu = S.Components.FindClosestMatching<ShipComputerComp>(
                EntityUtils.CenterOf(pos), false, (c) =>
                    c.IsConstructed && !factory.Spec.Bounds.Contains(c.Entity.PosIdx));
            if (cpu == null) {
                cpu = S.Components.FindFirstByType<ShipComputerComp>();
            }
            if (cpu != null) {
                var delay = S.Rng.Range(Consts.TicksPer5Minutes, Consts.TicksPer10Minutes);
                S.Clock.ExecuteAfterTicks(delay, () => {
                    S.Sig.ForceAreas.Send();
                    var cmd = new CmdPushSection(pusher, anchor,
                        cpu.Tile.Position, (c) => FinishMove(s, army, c), false);
                    cmd.CaptureCollision = true;
                    S.CmdQ.Enqueue(cmd);
                });
            }
        }

        private void FinishMove(GameState s, Army army, CmdPushSection cmd) {
            if (army == null) {
                D.Err("Army is null after finishing derelict ship pirates move!");
                return;
            }
            var lastHome = army.HomeArea;
            if (lastHome.Width < 3 || lastHome.Height < 3) {
                D.Err("Home area too small for derelict ship pirates! {0}", lastHome);
                return;
            }
            var newHome = lastHome.MoveBySafe(cmd.ExecutedMotion, Consts.MapMargin,
                    out var moved);
            if (!moved) {
                D.Err("Did not move in FinishMove! {0} by {1}", lastHome,
                    cmd.ExecutedMotion);
            }
            army.HomeArea = newHome;
            if (cmd.LastCollision == null) { return; }
            if (Arrays.IsEmpty(cmd.LastCollision)) {
                D.ErrSoft("Collision with pirate ship is empty!");
                return;
            }
            var expanded = PosRect.Expand(cmd.LastCollision);
            if (A.FocusOnEvents) {
                var rect = Pos.Encapsulate(expanded);
                s.Sig.CameraFocus.Send(rect.Center(), rect.Extents, 0f);
            }
            foreach (var p in expanded) {
                Explosion.Create(s, 3,
                    posIdx: p,
                    customRadius: 1f,
                    customStr: 300f,
                    flags: ExplosionFlags.SkipFloors | ExplosionFlags.SkipEvent);
                // S.CmdQ.Enqueue(new CmdCreateExplosion(EntityUtils.CenterOf(p),
                //     1f, 300f, false, false)
                //         .SkipLayers(WorldLayer.Floor));
            }
            s.Clock.SetSpeed(ClockSpeed.Normal);
        }
    }
}
