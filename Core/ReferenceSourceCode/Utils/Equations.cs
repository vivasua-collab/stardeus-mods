using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using KL.Utils;
using Unity;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Utils {
    public static class Equations {
        // https://www.desmos.com/calculator/va6lp1dm2z
        public static float BatchSizeCost(int bs) {
            if (bs < 2) { return 1f; }
            var a1 = -0.0105263f;
            var b1 = 1.01053f;
            return a1 * bs + b1;
        }
        // https://www.desmos.com/calculator/rt1hb4u3dwI
        public static float NPCShipPowerDiffHitChance(float powerDiff) {
            return 0.234f * powerDiff + 0.476f;
        }

        // https://www.desmos.com/calculator/csbp69kf5n
        public static float InfluenceToTargetTension(float influence) {
            var x = influence / 1000f;
            var a1 = 5.7f;
            var a2 = 3.5f;
            var s = 1.85f;
            var c = 7.4f; // how fast we reach the upper part of the easing curve
            var d = 177f; // how fast we grow linearly (bigger is slower)
            var i = 1f; // floor
            var m = 100; // max difficulty before it starts going linear
            var sp = a1 - (math.sqrt(x / c) - a2);
            return (m / (1 + math.pow(s, sp))) + (x / d) + i;
        }

        public static float TractorBeamEmitRate(float dist) {
            dist = Maths.Max(Maths.Min(dist, 160), 25);
            var rate = Maths.FastIntSqrt((int) dist) * 5;
            if (!float.IsFinite(rate)) {
                rate = 3f;
            }
            return Mathf.Clamp(rate, 3, 64);
        }

        // https://www.desmos.com/calculator/rxxrh7eapd
        public static float BeingNameCameraOrthoSize(float o) {
            const float a = -0.00264f;
            const float b = 0.3446f;
            const float c = 1.3709f;
            return a * o * o + b * o + c;
        }
        // https://www.desmos.com/calculator/rxxrh7eapd
        public static float BeingNameCameraOrthoStep(int i) {
            const float a = 0.2f;
            const float b = -0.3f;
            const float c = 1.1f;
            return a * i * i + b * i + c;
        }

        // https://www.desmos.com/calculator/apmpliz8tn
        public static float MaxTierAvgCostDiff(int tier) {
            int x = tier;
            if (x > 8) {
                x = 8;
            }
            var a = 0.0955598f;
            var b = -1.51332f;
            var c = 7.639f;
            var res = a * x * x + b * x + c;
            return res;
        }

        public static int TaskLimit(int memory, float mul) {
            return (int) (memory * 20 * mul);
        }
        // Easing function: cubic in
        public static float CubicIn(float t) {
            return t * t * t;
        }

        // Easing function: cubic out
        public static float CubicOut(float t) {
            float f = t - 1f;
            return f * f * f + 1f;
        }
        // Easing function: cubic in/out
        public static float CubicInOut(float t) {
            if (t < 0.5f) {
                return 4f * t * t * t;
            } else {
                float f = ((2f * t) - 2f);
                return 0.5f * f * f * f + 1f;
            }
        }

        public static float TierForDifficulty(int tier, float diff) {
            return tier * math.pow(diff, 0.1f);
        }

        public static int DiskCostFor(int tierExact, int depth, int depCount, float difficulty) {
            var cost = (tierExact - 1) * 2 + depth;
            if (cost == 0) {
                cost = 1;
            }
            return cost;
        }

        // https://www.desmos.com/calculator/rcrqhtflqu
        // y = a*tier^b+c*tier+d + depth * tier^2*depCount
        // a = 4.42f
        // b = 4.37
        // c = -40
        // d = 45
        public static int UnlockValueFor(int tierExact, int depth, int depCount, float difficulty) {
            var tier = TierForDifficulty(tierExact, difficulty);
            var exact = 4.42f * math.pow(tier, 4.37f) + tier * -40f + 45
                + depth * math.pow(tier, 2) * (1f + depCount / 3f);
                // * depCount; -- this sucks
            if (difficulty < 0.9f) {
                return (int) math.floor(exact);
            } else if (difficulty > 1.1f) {
                return (int) math.ceil(exact);
            } else {
                return (int) math.round(exact);
            }
        }

        // https://www.desmos.com/calculator/5fxlowi2az
        // cost = ax^2+bx+c
        // a = 2.4f
        // b = -1.8f
        // c = 6.4f
        public static int MemCostFor(int tierExact, int depth, int depCount, float difficulty) {
            var tier = TierForDifficulty(tierExact, difficulty);
            var exact = 2.4f * math.pow(tier, 2f) - 1.8f * tier
                + 4.7f + depth + depCount * math.sqrt(tier * 0.3f);
            if (difficulty < 0.9f) {
                return (int) math.floor(exact);
            } else if (difficulty > 1.1f) {
                return (int) math.ceil(exact);
            } else {
                return (int) math.round(exact);
            }
        }

        // https://www.desmos.com/calculator/l2fpelacgb
        public static float EnginePush(float engineForce, int shipMass,
                float minSpeed, float maxSpeed) {
            var t = math.sqrt(engineForce / shipMass);
            return math.lerp(minSpeed, maxSpeed, Maths.Clamp(t, 0f, 1f));
        }

        // Push the thrusters give a ship in local space. One rule for the player's ship and NPC
        // ships, so a change to one side's sizes or thrusters can't skew their relative speeds.
        public static float LocalSpacePush(float thrust, int shipSize) {
            var mass = CurveDef.FastGet(CurveIdH.SpaceTravel_EngineMass).Evaluate(shipSize);
            var t = math.sqrt(thrust / mass);
            return math.lerp(Tunables.ShipSpeedMin, Tunables.ShipSpeedMax, Maths.Clamp(t, 0f, 1f));
        }

        // https://www.desmos.com/calculator/fmqfwcgpnx
        public static float AttackMethodValue(float damage, float acc, float range) {
            return damage * acc * (1 + math.log(range + 2f));
        }

        // https://www.desmos.com/calculator/ywqnpihbbo
        public static float JobLevelTrainSpeedMul(byte lvl) {
            return 6f / (lvl + 1);
        }
    }
}
