using System;
using Game.Constants;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// For positioning enemy ship in game view, each ship takes a slot
    /// around the outer map edge.
    /// </summary>
    public class ShipViewSlot {
        public ShipView View;
        public Vector2 Position;
        public Facing.Type Edge;
        /// <summary>
        /// Set while the ship faces something other than the player's map, cleared to
        /// go back to facing the map from its edge. Not serialized - it is derived from
        /// what the ship is attacking (see ShipView.FaceTarget).
        /// </summary>
        public Facing.Type? FacingOverride;
        public Facing.Type ShipFacing => FacingOverride ?? Facing.Opposite(Edge);
        /// <summary>
        /// When we look for weapon zone positions, we will use XY coordinate
        /// to position the weapons from nose to tail on left and right.
        /// When the ship faces Right, angle is 0 because standard XY coordinate will match;
        /// For other facings we will want to rotate the coordinate system by some degrees
        /// to realign with the ship visual.
        /// </summary>
        public float RotAngle {
            get {
                switch (ShipFacing) {
                    case Facing.Type.Right: return 0f;
                    case Facing.Type.Left: return 180f;
                    case Facing.Type.Up: return 90f;
                    case Facing.Type.Down: return -90f;
                    default: {
                        D.Err("Invalid ship facing: {0}", ShipFacing);
                        return 0f;
                    }
                }
            }
        }
        public float Radius => View.Ship.Width * 0.75f;
        public bool IsColliding;

        public void Load(Facing.Type edge, Vector2 posOnEdge, ShipView view) {
            var shipOffset = Facing.ToOffset(edge, view.Ship.Width / 2f + 25f);
            View = view;
            Edge = edge;
            FacingOverride = null;
            Position = posOnEdge + shipOffset;
            // D.Warn("Setting ship position on edge: {0}, pos on edge: {1}, offset: {2}, final: {3}",
            //     edge, posOnEdge, shipOffset, Position);
        }

        public bool CollidesWith(ShipViewSlot other) {
            // if (Edge != other.Edge) { return false; }
            var dist = Vectors.Distance(Position, other.Position);
            var collides = dist < Radius + other.Radius;
            // D.Warn("Collision: {0} and {1}: {2}", this, other, collides);
            return collides;
        }

        public override string ToString() {
            return $"ShipViewSlot[{Edge},{Position},R:{Radius}]";
        }
    }
}
