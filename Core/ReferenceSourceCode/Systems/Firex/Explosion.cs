
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.Visuals;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Firex {
    [MessagePackObject]
    public sealed class Explosion {
        /// <summary>
        /// When it should explode
        /// </summary>
        [Key("Tick")] public long Tick;
        [Key("Flags")] public ExplosionFlags Flags;
        [Key("Position")] public Vector2 Position;
        [Key("PosIdx")] public int PosIdx;
        [Key("Str")] public float Strength;
        [Key("Rad")] public float Radius;
        [Key("Level")] public int Level;
        [Key("ChemFuelAmt")] public float ChemicalFireFuelAmt;
        [Key("FireChance")] public float FireChance;
        [Key("Seed")] public int Seed;
        [Key("IgnorePositions")] public HashSet<int> IgnorePositions;
        [IgnoreMember] public bool IgnoreObstacles => (Flags & ExplosionFlags.IgnoreObstacles) == ExplosionFlags.IgnoreObstacles;
        [IgnoreMember] public bool BlowHole => (Flags & ExplosionFlags.BlowHoleInFloor) == ExplosionFlags.BlowHoleInFloor;
        [IgnoreMember] public bool SkipFX => (Flags & ExplosionFlags.SkipFX) == ExplosionFlags.SkipFX;
        [IgnoreMember] public bool SkipFloors => (Flags & ExplosionFlags.SkipFloors) == ExplosionFlags.SkipFloors;
        [IgnoreMember] public bool IsImmediate => (Flags & ExplosionFlags.Immediate) == ExplosionFlags.Immediate;
        [IgnoreMember] public bool SkipEvent => (Flags & ExplosionFlags.SkipEvent) == ExplosionFlags.SkipEvent;
        [IgnoreMember] public bool StructureOnly => (Flags & ExplosionFlags.StructureOnly) == ExplosionFlags.StructureOnly;
        [IgnoreMember] public bool IsExecuted => (Flags & ExplosionFlags.Executed) == ExplosionFlags.Executed;
        [IgnoreMember] public bool IsFireOnly => (Flags & ExplosionFlags.FireOnly) == ExplosionFlags.FireOnly;
        [IgnoreMember] public bool OnlyWallsBlock => (Flags & ExplosionFlags.OnlyWallsBlock) == ExplosionFlags.OnlyWallsBlock;
        /// <summary>
        /// If this handler is set, instead of applying damage to each cell, this handler will be invoked.
        /// Useful for custom explosions, like toilet, or artillery fungus
        /// </summary>
        [IgnoreMember] public Action<int> CustomCellHandler;
        [IgnoreMember] public Action BeforeExplode;
        [IgnoreMember] public INamedEntity WoundSource => NamedEntity.For(Texts.LinkTo(T.Explosion,
            PosIdx > 0 ? PosIdx : Pos.FromVector(Position)));
        public static Explosion CreateCustom(GameState S,
                Action<int> cellHandler,
                Vector2 position = default,
                int posIdx = 0,
                float radius = 0f,
                float strength = 10f, // how well does it go through walls - 10 hp = very not well
                ExplosionFlags flags = ExplosionFlags.Custom | ExplosionFlags.SkipFX,
                int seed = 0,
                HashSet<int> ignorePositions = null) {
            if (posIdx == 0 && position == default) {
                D.Err("Trying to create a custom explosion without position!");
                return null;
            }
            flags |= ExplosionFlags.Custom | ExplosionFlags.Immediate | ExplosionFlags.SkipEvent;
            var tick = S.Ticks;
            var expl = new Explosion() {
                Position = position,
                PosIdx = posIdx,
                Strength = strength,
                Radius = radius,
                IgnorePositions = ignorePositions,
                Flags = flags,
                Seed = seed,
                Tick = tick,
                FireChance = 0f,
                ChemicalFireFuelAmt = 0f,
                CustomCellHandler = cellHandler,
            };
            S.Sig.CreateExplosion.Send(expl);
            return expl;
        }

        public static Explosion Create(GameState S,
                int level,
                Vector2 position = default,
                int posIdx = 0,
                ExplosionFlags flags = ExplosionFlags.None,
                int delay = 0,
                int seed = 0,
                float fireChance = 0.5f,
                float chemFuelAmt = 0.15f,
                float customRadius = 0f,
                float customStr = 0f,
                HashSet<int> ignorePositions = null) {
            if (posIdx == 0 && position == default) {
                D.Err("Trying to create an explosion without position!");
                return null;
            }
            Explosions.ForLevel(level, out var radius, out var strength);
            if (customRadius > 0f) { radius = customRadius; }
            if (customStr > 0f) { strength = customStr; }
            if ((flags & (ExplosionFlags.SkipFloors | ExplosionFlags.BlowHoleInFloor))
                == (ExplosionFlags.SkipFloors | ExplosionFlags.BlowHoleInFloor)) {
                    D.Err("Creating explosion with incompatible flags! {0}", flags);
                flags &= ~ExplosionFlags.SkipFloors;
            }
            if (delay > 0 && ((flags & ExplosionFlags.Immediate) == ExplosionFlags.Immediate)) {
                D.Err("Creating immediate explosion with delay! Ignoring delay");
                delay = 0;
            }
            var tick = delay > 0 ? S.Ticks + delay : 0;
            var expl = new Explosion() {
                Level = Mathf.CeilToInt(radius),
                Position = position,
                PosIdx = posIdx,
                Strength = strength,
                Radius = radius,
                IgnorePositions = ignorePositions,
                Flags = flags,
                Seed = seed,
                Tick = tick,
                FireChance = fireChance,
                ChemicalFireFuelAmt = chemFuelAmt,
            };
            S.Sig.CreateExplosion.Send(expl);
            return expl;
        }
        public override string ToString() {
            return $"Explosion[{Pos.String(PosIdx)}|{Position}|L:{Level}|S:{Strength}|R:{Radius}|Chem:{ChemicalFireFuelAmt}|F%{FireChance}|T:{Tick}|Seed:{Seed}|{Flags}]";
        }
    }
}
