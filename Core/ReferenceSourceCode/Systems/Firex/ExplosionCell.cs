using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Firex {
    public struct ExplosionCell {
        public int PosIdx;
        public Vector2 Position;
        // how much force we have after the transfer
        public float RemainingForce;
        // how much force do we transfer into current cell
        public float AppliedForce;
        // theoretical max force at current distance
        public float MaxForce;
        public float ForceTransferPercent => MaxForce < Maths.Epsilon ? 0f : RemainingForce / MaxForce;
        public float Distance;
        public Tile TopmostTile;
        public List<Being> Beings;
        public List<Obj> Objs;

        public static ExplosionCell Create(int posIdx, float dist, float expectedForce) {
            return new ExplosionCell() {
                PosIdx = posIdx,
                Position = EntityUtils.CenterOf(posIdx),
                RemainingForce = expectedForce,
                AppliedForce = expectedForce,
                MaxForce = expectedForce,
                Distance = dist,
            };
        }
        public override string ToString() {
            return $"ExplosionCell[{Pos.String(PosIdx)}|RF:{RemainingForce}|AF:{AppliedForce}|MF:{MaxForce}|%:{ForceTransferPercent}|D:{Distance}]";
        }

        /// <param name="processedTiles">tiles that already were affected</param>
        /// <returns>damage dealt</returns>
        public float Explode(HashSet<int> processedTiles, Rng rng, Explosion expl, FirexSys firex) {
            if (AppliedForce < Maths.Epsilon) { return 0f; }
            if (expl.IgnorePositions != null && expl.IgnorePositions.Contains(PosIdx)) {
                return 0f;
            }
            if (expl.CustomCellHandler != null) {
                expl.CustomCellHandler.Invoke(PosIdx);
                return 0f;
            }
            float distPct = Distance / expl.Radius;
            float initialForce = AppliedForce;
            float dmg = ExplodeTiles(processedTiles, rng, expl, distPct);
            if (expl.CustomCellHandler == null) {
                Being lastBeing = ExplodeBeings(expl, rng, initialForce);
                Obj lastObj = ExplodeObjs(expl, initialForce);
                StartFire(rng, expl, firex, distPct, lastBeing, lastObj);
            }
            return dmg;
        }

        private readonly void StartFire(Rng rng, Explosion expl, FirexSys firex,
                float distPct, Being lastBeing, Obj lastObj) {
            float fireChance = expl.FireChance;
            // won't spread fire if wall was in the way
            if (fireChance <= 0f || AppliedForce != MaxForce) {
                return;
            }
            fireChance *= Mathf.LerpUnclamped(0.9f, 0.1f, distPct);
            if (!rng.Chance(fireChance)) {
                return;
            }
            bool startedFire = false;
            var tile = EntityUtils.GetTopmostParentTile(PosIdx);
            if (tile != null && tile.IsConstructed && !tile.Flammable.IsOnFire) {
                startedFire = firex.TrySetOnFire(tile.Flammable,
                    Fire.Create(0.75f + fireChance, expl.ChemicalFireFuelAmt));
            }
            if (!startedFire && lastObj != null && lastObj.IsActive) {
                if (!lastObj.Flammable.IsOnFire) {
                    startedFire = firex.TrySetOnFire(lastObj.Flammable,
                        Fire.Create(0.75f + fireChance, expl.ChemicalFireFuelAmt));
                }
            }
            if (!startedFire && lastBeing != null && lastBeing.IsActive) {
                if (!lastBeing.Flammable.IsOnFire) {
                    firex.TrySetOnFire(lastBeing.Flammable,
                        Fire.Create(0.75f + fireChance, expl.ChemicalFireFuelAmt));
                }
            }
        }

        private readonly Obj ExplodeObjs(Explosion expl, float initialForce) {
            Obj lastObj = null;
            if (Arrays.IsEmpty(Objs)) {
                return lastObj;
            }
            for (int i = Objs.Count - 1; i >= 0; i--) {
                Obj obj = Arrays.SafeGetFast(Objs, i);
                if (obj != null && obj.IsActive) {
                    if (!expl.IsFireOnly) {
                        obj.Health.TakeDamage(initialForce, DamageType.Explosive);
                    }
                    if (!obj.Health.IsDestroyed) {
                        lastObj = obj;
                    }
                }
            }

            return lastObj;
        }

        private readonly Being ExplodeBeings(Explosion expl, Rng rng, float initialForce) {
            Being lastBeing = null;
            if (Arrays.IsEmpty(Beings)) {
                return lastBeing;
            }
            INamedEntity source = null;
            for (int i = Beings.Count - 1; i >= 0; i--) {
                Being b = Arrays.SafeGetFast(Beings, i);
                if (b != null && b.IsActive) {
                    if (!expl.IsFireOnly) {
                        var numHits = initialForce / 100f;
                        if (numHits < 1f) {
                            numHits = 1f;
                        } else if (numHits > 6f) {
                            numHits = 6f;
                        }
                        source ??= expl.WoundSource;
                        BodyPartSlot worstPart = null;
                        var worstSev = Severity.Irrelevant;
                        for (int h = 0; h < numHits; h++) {
                            Explosions.DealExplosionDamageToBeing(
                                source, rng, initialForce, b, ref worstPart, ref worstSev);
                        }
                        Explosions.AfterExplosionDamage(b.S, source, b, worstPart, worstSev);
                    }
                    if (b.IsActive) {
                        lastBeing = b;
                    }
                }
            }

            return lastBeing;
        }

        private readonly float ExplodeTiles(HashSet<int> processedTiles, Rng rng, Explosion expl, float distPct) {
            float dmg = 0f;
            float force = AppliedForce;
            if (TopmostTile == null || !TopmostTile.IsActive) { return 0f; }
            bool skipFloors = expl.SkipFloors;
            if (skipFloors && TopmostTile.Definition.LayerId == WorldLayer.Floor) {
                return 0f;
            }
            bool blowHole = expl.BlowHole;
            if (blowHole && rng.Chance(distPct - 0.5f)) {
                blowHole = false;
            }
            if (blowHole) {
                force = 100000f;
            }
            if (processedTiles.Add(TopmostTile.Id)) {
                // D.Warn("Exploding {0} | {1}", TopmostTile, this);
                if (!expl.IsFireOnly) {
                    TopmostTile.Health.TakeDamage(force, DamageType.Explosive);
                }
                dmg += force;
            } else {
                // this happens when we blow up a multitile
                // D.Err("Trying to explode twice! {0} | {1}", TopmostTile, this);
            }

            if (!skipFloors && TopmostTile.Definition.LayerId > WorldLayer.Floor) {
                var floor = EntityUtils.FloorAt(PosIdx);
                if (floor != null && floor.IsConstructed) {
                    if (processedTiles.Add(floor.Id)) {
                        force = RemainingForce;
                        // if (blowHole) {
                        //     force = floor.Health.Health;
                        // }
                        if (!expl.IsFireOnly) {
                            floor.Health.TakeDamage(force, DamageType.Explosive);
                        }
                        dmg += force;
                    } else {
                        D.Err("Trying to explode floor twice! {0} | {1}", floor,
                            this);
                    }
                }
            }
            return dmg;
        }
    }
}
