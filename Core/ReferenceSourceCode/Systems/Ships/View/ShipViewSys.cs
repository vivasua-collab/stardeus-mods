using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems.Ships;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    /// <summary>
    /// Handles interactive display of other space ships when they are in
    /// local map. Mostly for ship to ship combat.
    /// </summary>
    public sealed class ShipViewSys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipViewSys());
        }
        private const string SysId = "ShipView";
        public override string Id => SysId;
        private ShipView viewPrefab;
        private Facing.Type[] edges;
        public readonly List<ShipViewSlot> Slots = new();

        private ShipViewSysData data;

        public override void Unload() {
        }

        protected override void OnInitialize() {
            edges = Facing.Types.ToArray();
            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState state) {
            state.Query.SpawnShipView.SetResponder(OnSpawnView);
            state.Query.SpawnShipViewAtPos.SetResponder(OnSpawnViewAtPos);
            state.Query.DespawnShipView.SetResponder(OnDespawnView);
            // waits for combat rosters, not just ships: spawning a view has observable
            // side effects (it can teleport the ship next to the player), so a slot
            // belonging to a ship that is no longer fighting must never spawn at all
            Ready.When(WhenH.ShipCombatDataRestored, RestoreData);
        }

        private void RestoreData() {
            data?.Restore(S);
            SpawnMissingCombatantViews();
            Ready.Set(WhenH.ShipViewDataRestored, true);
            data = null;
        }

        /// <summary>
        /// Combat needs every participant to have a view to shoot and be shot at, so
        /// anyone the save had no usable slot for gets a fresh one.
        /// </summary>
        private void SpawnMissingCombatantViews() {
            SpawnMissingViews(S.Query.GetEnemyShips.Ask());
            SpawnMissingViews(S.Query.GetAllyShips.Ask());
            for (int i = Slots.Count - 1; i >= 0; i--) {
                var ship = Slots[i].View?.Ship;
                if (ship == null || !ShipViewSysData.IsCombatant(S, ship)) {
                    D.Err("Ship view restored for a ship that is not in combat: {0}", ship);
                }
            }
        }

        private void SpawnMissingViews(List<NPCShip> ships) {
            for (int i = ships.Count - 1; i >= 0; i--) {
                var ship = ships[i];
                if (ship.IsPlayer || ship.View != null) { continue; }
                D.Warn("Spawning a missing combat view for {0}", ship);
                OnSpawnView(ship);
            }
        }

        private bool OnDespawnView(NPCShip ship) {
            ship.State &= ~ShipStateFlags.InGameView;
            if (ship.View == null) { return false; }
            Slots.Remove(ship.View.Slot);
            PrefabPool.Despawn(ship.View.gameObject);
            ship.View = null;
            S.Sig.NPCShipSpawnedInGameView.Send(ship, false);
            return true;
        }
        private ShipView OnSpawnViewAtPos(NPCShip ship, Facing.Type edge, Vector2 pos) {
            ShipView view = CreateShipViewFor(ship);
            var slot = new ShipViewSlot();
            slot.Load(edge, pos, view);
            Slots.Add(slot);
            LoadShipView(view, slot);
            return view;
        }
        private ShipView OnSpawnView(NPCShip ship) {
            ShipView view = CreateShipViewFor(ship);
            var slot = FindSlotFor(view);
            LoadShipView(view, slot);
            return view;
        }

        private void LoadShipView(ShipView view, ShipViewSlot slot) {
            view.SetViewSlot(slot);
            view.LoadShip(S);
            var ship = view.Ship;

            ship.State |= ShipStateFlags.InGameView;

            ship.View = view;
            S.Sig.NPCShipSpawnedInGameView.Send(ship, true);
        }

        private ShipView CreateShipViewFor(NPCShip ship) {
            viewPrefab ??= ResourceLibrary.FindPrefab<ShipView>();
            var view = PrefabPool.Spawn(viewPrefab,
                Consts.V3Zero, Consts.QId, ResourceLibrary.Root);
            view.S = S;
            // ship must be assigned before anything else is done
            view.Ship = ship;
            return view;
        }

        private Vector2 RelativePosition(ShipView view) {
            var dir = (view.Ship.SO.Location - A.Player.Location).normalized;
            return Vectors.Rotate(dir, S.Sys.ShipNav.ShipDefaultRot - S.Sys.Ship.Rotation);
        }

        private ShipViewSlot FindSlotFor(ShipView view) {
            var edgeLen = S.GridWidth;

            var slot = new ShipViewSlot();

            var adjPos = RelativePosition(view);
            var wantedEdge = Facing.FromDirection(adjPos, S.Sys.ShipNav.ShipDirection);

            // try finding slot on the wanted edge first
            var posOnWantedEdge = ToEdgePos(wantedEdge, edgeLen, Rng.URange(0.25f, 0.75f));
            slot.Load(wantedEdge, posOnWantedEdge, view);
            if (CanFit(slot)) {
                Slots.Add(slot);
                return slot;
            }

            for (int i = 0; i < 50; i++) {
                Arrays.Shuffle(Rng.Unseeded, edges);
                foreach (var edge in edges) {
                    // try finding slot
                    var posOnEdge = ToEdgePos(edge, edgeLen, Rng.URange(0.05f, 0.95f));
                    slot.Load(edge, posOnEdge, view);
                    // new Vector2(S.Map.Width + ship.Width, S.Map.Height / 2);
                    if (CanFit(slot)) {
                        Slots.Add(slot);
                        return slot;
                    }
                }
            }

            for (int i = 0; i < 50; i++) {
                Arrays.Shuffle(Rng.Unseeded, edges);
                foreach (var edge in edges) {
                    // try finding slot
                    var posOnEdge = ToEdgePos(edge, edgeLen, Rng.URange(-0.05f, -0.45f));
                    slot.Load(edge, posOnEdge, view);
                    // new Vector2(S.Map.Width + ship.Width, S.Map.Height / 2);
                    if (CanFit(slot)) {
                        Slots.Add(slot);
                        return slot;
                    }
                }
            }

            for (int i = 0; i < 50; i++) {
                Arrays.Shuffle(Rng.Unseeded, edges);
                foreach (var edge in edges) {
                    // try finding slot
                    var posOnEdge = ToEdgePos(edge, edgeLen, Rng.URange(1.05f, 1.45f));
                    slot.Load(edge, posOnEdge, view);
                    // new Vector2(S.Map.Width + ship.Width, S.Map.Height / 2);
                    if (CanFit(slot)) {
                        Slots.Add(slot);
                        return slot;
                    }
                }
            }

            D.Err("Ran out of attempts, but will still use the slot: {0}", slot);
            slot.IsColliding = true;

            Slots.Add(slot);
            return slot;
        }

        private bool CanFit(ShipViewSlot slot) {
            for (int i = Slots.Count - 1; i >= 0; i--)  {
                ShipViewSlot sl = Slots[i];
                if (sl.CollidesWith(slot)) {
                    return false;
                }
            }
            return true;
        }

        private Vector2 ToEdgePos(Facing.Type edge, float edgeLen, float posOnEdgeLen) {
            var h = edgeLen / 2f;
            var center = new Vector2(h, h);
            var pos = center + Facing.ToOffset(edge, h);
            var edgeShift = edgeLen * posOnEdgeLen - h;
            switch (edge) {
                case Facing.Type.Down:
                case Facing.Type.Up: {
                    pos += new Vector2(edgeShift, 0f);
                    break;
                }
                case Facing.Type.Left:
                case Facing.Type.Right: {
                    pos += new Vector2(0f, edgeShift);
                    break;
                }
            }
            // D.Warn("Edge pos: {0} {1}% -> {2}", edge, posOnEdgeLen, pos);
            return pos;
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.ShipView = ShipViewSysData.Serialize(this);
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd.ShipView;
        }
    }
}
