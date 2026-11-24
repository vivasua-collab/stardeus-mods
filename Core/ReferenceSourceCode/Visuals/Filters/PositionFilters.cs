using System;
using System.Runtime.CompilerServices;
using Game.CodeGen;
using Game.Constants;
using Game.Rendering;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Visuals.Filters {
    public static class PositionFilters {
        private static float CopterHoverAmp(Rng rng) => rng.Range(0.07f, 0.12f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InSpaceHoverAmp(Rng rng) => rng.Range(0.06f, 0.12f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InSpaceHoverSpeed(Rng rng) => rng.Range(0.3f, 0.5f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DroneHoverAmp(Rng rng) => rng.Range(0.04f, 0.05f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DroneHoverSpeed(Rng rng) => rng.Range(1.5f, 2f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float JitterSpeed(Rng rng) => rng.Range(1.5f, 2f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float JitterAmp(Rng rng) => rng.Range(0.015f, 0.02f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Offset(Rng rng) => rng.Value;
        public static AnimatedSpriteSet.SpritePosFilter ByNameHash(int nameHash, int seed) {
            switch (nameHash) {
                case PositionFilterTypesH.CopterHover: return CopterHover(seed);
                case PositionFilterTypesH.DroneHover: return DroneHover(seed);
                case PositionFilterTypesH.RobotHeadJitter: return RobotJitter(seed);
                case PositionFilterTypesH.InSpaceHover: return InSpaceHover(seed);
                default: {
                    D.Err("No PositionFilter for nameHash: {0}", nameHash);
                    return null;
                }
            }
        }


        private static AnimatedSpriteSet.SpritePosFilter RobotJitter(int seed) {
            var rng = new Rng(seed);
            var jsX = JitterSpeed(rng);
            var jsY = JitterSpeed(rng);
            var offX = Offset(rng);
            var offY = Offset(rng);
            var amp = JitterAmp(rng);
            return (Vector2 input) => {
                var tf = GameTimer.time;
                var shift = new Vector2(
                    Maths.FastSin((offX + tf) * jsX),
                    Maths.FastCos((offY + tf) * jsY)
                ) * amp;
                return (Vector2) input + shift;
            };
        }
        private static AnimatedSpriteSet.SpritePosFilter InSpaceHover(int seed) {
            var rng = new Rng(seed);
            var off = Offset(rng);
            var amp = InSpaceHoverAmp(rng);
            var speed = InSpaceHoverSpeed(rng);
            return (Vector2 input) =>
                input + new Vector2(0,
                    Maths.FastSin((off + GameTimer.time) * speed) * amp);
        }
        private static AnimatedSpriteSet.SpritePosFilter DroneHover(int seed) {
            var rng = new Rng(seed);
            var off = Offset(rng);
            var amp = DroneHoverAmp(rng);
            var speed = DroneHoverSpeed(rng);
            return (Vector2 input) =>
                input + new Vector2(0,
                    Maths.FastSin((off + GameTimer.time) * speed) * amp);
        }

        private static AnimatedSpriteSet.SpritePosFilter CopterHover(int seed) {
            var rng = new Rng(seed);
            var off = Offset(rng);
            var amp = CopterHoverAmp(rng);
            var speed = DroneHoverSpeed(rng);
            return (Vector2 input) =>
                input + new Vector2(0,
                    Maths.FastSin((off + GameTimer.time) * speed) * amp);
        }
    }
}
