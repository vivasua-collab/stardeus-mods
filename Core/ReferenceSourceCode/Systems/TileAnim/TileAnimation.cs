using System;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.TileAnim {
    public class TileAnimation {
        public Vector2 PosOffset;
        public bool IsAnimating;
        public string BaseSpriteId;
        public string AnimSpriteId;
        public Tile AnimatedTile;
        public bool IsRegistered;
        public int SpriteOrder;
        public bool PingPong;
        public int PingPongStillFrames;
        public bool CopyAnimSprite;
        public float CurrentFrame;
        public float Speed;
        public string FramePrefix;
        public Vector2 Size;
        public int NumFrames;
        public int NumFramesActual;
        public bool NeedsEnergy;
        public int Hash;
        public TileAnimation() {
        }

        public override string ToString() {
            return $"TileAnim[On:{IsAnimating},{AnimatedTile}]";
        }
    }
}
