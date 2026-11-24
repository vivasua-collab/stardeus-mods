using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using System;
using KL.Grid;
using KL.Randomness;
using Game.Commands;
using Game.Systems.Firex;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class ArtilleryFungusComp : BaseComponent<ArtilleryFungusComp>,
            IWildPlant, IXenoDetectable {
        private AnimatedSpriteSet graphic;
        private float growthRate;
        private float growth;
        public float Growth => growth;
        private byte lastStage = 0;
        private string growthState;
        public string GrowthState => growthState;
        private float radius;
        private float perishTempKelvin;
        private BFloat growthB;
        public BFloat GrowthB => growthB ??= new BFloat(growth);
        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ArtilleryFungusComp());
        }

        protected override void OnConfig() {
            growthRate = Config.GetFloat(PropertyIdH.GrowthRate, 1f);
            radius = Config.GetFloat(PropertyIdH.Radius, 3f);
            perishTempKelvin = (float) Config.GetInt(PropertyIdH.MaxTempCelsius, 50);
            perishTempKelvin += Rng.URange(-5f, 5f);
            perishTempKelvin = Units.CelsiusToKelvin(perishTempKelvin);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Growth", growth);
        }

        protected override void OnLoad(ComponentData data) {
            growth = data.GetFloat("Growth", 0);
        }

        private bool isWaitingForGameReady;

        private void SetGraphicWhenReady() {
            if (Ready.Game) {
                SetGraphic();
            } else {
                if (isWaitingForGameReady) { return; }
                isWaitingForGameReady = true;
                Ready.WhenGameLate(this, "SetPlantGraphic", SetGraphic);
            }
        }

        private void SetGraphic() {
            if (!Entity.IsActive) { return; }
            if (graphic == null) {
                isWaitingForGameReady = false;
                var sset = Obj.Graphics.SpriteSet;
                if (sset == null || sset.Id == 0) {
                    Obj.Graphics.Show(true);
                    sset = Obj.Graphics.SpriteSet;
                    if (sset == null || sset.Id == 0) {
                        // sprite set not initialized!
                        return;
                    }
                }
                graphic = sset;
            }
            if (growth < 0.2f) {
                if (lastStage == 1) {
                    return;
                }
                lastStage = 1;
                // seed stage
                graphic.SetSprite(0, FindSprite("Seed"), S);
            } else if (growth < 0.5f) {
                if (lastStage == 2) {
                    return;
                }
                lastStage = 2;
                graphic.SetSprite(0, FindSprite("Growing1"), S);
                // growth1 sprout
            } else if (growth < 1f) {
                if (lastStage == 3) {
                    return;
                }
                lastStage = 3;
                graphic.SetSprite(0, FindSprite("Growing2"), S);
                // growth2 sprout
            } else {
                // final version
                if (lastStage == 4) {
                    return;
                }
                lastStage = 4;
                // null stage is the full grown sprite id
                //graphic.Sprites[0] = FindSprite(null);
                graphic.SetSprite(0, FindSprite(null), S);
            }
        }

        private SpriteData FindSprite(string stage) {
            if (stage == null) {
                return RenderingService.Sprites.Find(Obj.Graphics.GraphicId);
            }
            var graphicId = string.Format("{0}_{1}",
                Obj.Graphics.GraphicId, stage);
            var gfx = RenderingService.Sprites.Find(graphicId);
            var scale = Obj.Graphics.Size;
            if (!gfx.IsScale(scale)) {
                if (!gfx.IsTempCopy) {
                    gfx = gfx.Copy();
                    gfx.IsTempCopy = true;
                }
                gfx.Scale = Obj.Graphics.Size;
            }
            return gfx;
        }


        public override void OnLateReady(bool wasLoaded) {
            S.Sig.RegisterWildPlant.Send(this, true);
            Achievements.BaseAchievements.OnFungusGrown(S);
            SetGraphicWhenReady();
            if (growth >= 1f) {
                growthState = T.FullyGrown;
            } else {
                growthState = T.NotGrowing;
            }
        }

        public override void OnRemove() {
            S.Sig.RegisterWildPlant.Send(this, false);
        }

        public override string ToString() {
            return $" * Plant [ArtilleryFungus {growth * 100}%, {growthState}]";
        }

        private string lastNotGrowingWarning;

        public bool IsGrowing { get; private set; }
        public void Grow(float growthAmt, int ticksPassed) {
            if (Obj.Carrier != null) {
                // don't grow / spread while being carried
                return;
            }
            if (!entity.IsActive) {
                D.Err("Trying to grow inactive plant! {0}", entity);
                return;
            }
            var pos = Obj.PosIdx;
            var temp = S.GetTemp(pos);
            if (temp >= perishTempKelvin) {
                S.Objs.Destroy(Obj);
                EntityUtils.SpawnRawMaterial(
                    MatIdH.PlantFiber, 1, pos, 0.25f, skipCheck: true);
                return;
            }

            bool wasGrown = growth == 1f;
            if (wasGrown) {
                return;
            }
            // To spread the growth out a bit
            if (Rng.UChance(0.33f)) {
                return;
            }
            IsGrowing = false;

            // Check temperature and oxygen
            var light = S.Query.LightAtPos.Ask(pos);

            if (growth < 1f) {
                growth += growthAmt * growthRate * (1f - light * 0.5f);
                IsGrowing = true;
            }

            if (growth >= 1f) {
                growth = 1f;
                growthState = T.FullyGrown;
                Spread();
            } else {
                growthState = T.Growing;
            }
            #if DEBUG_PLANTS
            D.Log("Growing plant {1}, ticks passed: {0}",
                ticksPassed, growth);
            #endif
            SetGraphicWhenReady();

            if (IsGrowing) {
                if (lastNotGrowingWarning != null) {
                    lastNotGrowingWarning = null;
                    HideInfoIcon();
                }
            } else {
                if (growth < 0.99f) {
                    if (lastNotGrowingWarning != growthState) {
                        lastNotGrowingWarning = growthState;
                        ShowInfoIcon(IconId.CCross, $"{T.InfoPlantNotGrowing}: {Texts.Red(growthState)}");
                        SetGraphicWhenReady();
                    }
                }
            }
            growthB?.Set(growth);
            stateChangedB?.Invoke();
        }

        private void Spread() {
            if (!EntityUtils.IsFloorAt(Obj.PosIdx)) {
                return;
            }
            int crowd = S.Objs.FindInRadiusMatching(Obj.Position,
                0.5f, Caches.Obj16, (o => o.Definition == Obj.Definition), 3);
            if (crowd > 2) { return; }
            var areaSys = S.Sys.Areas;
            var objPos = Obj.PosIdx;
            var areaId = areaSys.At(objPos);
            bool isOutdoors = areaId < 1;
            Caches.WeightedInts.Clear();
            Explosion.CreateCustom(S,
                cellHandler: (p) => {
                    var wall = EntityUtils.WallAt(p);
                    bool addOnOtherEnd = false;
                    if (wall != null) {
                        if (wall.Door != null) {
                            if (wall.Door.IsClosed) {
                                return;
                            }
                            addOnOtherEnd = true;
                        } else if (wall.TryGetComponent<VentComp>(out var vent)) {
                            if (vent.IsClosed) {
                                return;
                            } else {
                                // D.Warn("Found vent at {0}", Pos.String(p));
                                addOnOtherEnd = true;
                            }
                        }
                    }
                    // adjust P
                    if (addOnOtherEnd) {
                        p = EntityUtils.OtherSideOfWall(p, areaId);
                    } else if (Pos.SqrDistance(p, objPos) < 2) {
                        // congested
                        return;
                    }
                    int found = S.Objs.FindInRadiusMatching(Pos.ToVector(p),
                        0.5f, Caches.Obj16, (o => o.Definition == Obj.Definition), 4);
                    // don't overdo
                    if (found > 3) { return; }
                    var light = S.Query.LightAtPos.Ask(p);
                    var weight = (1f - light * 0.5f) * (1f / (found + 1));
                    // if (addOnOtherEnd) {
                        // weight += 1f; // more chance to spread - NO, it's BAD, spores get stuck near a vent
                        // D.Warn("Will try to do shit outside a vent at {0}. W: {1}", Pos.String(p), weight);
                    // }
                    Caches.WeightedInts.TryAdd(p, weight);
                },
                position: Obj.Position,
                radius: radius,
                flags: ExplosionFlags.SkipFX);

            var toAdd = Rng.URange(2f, 3f) * S.Difficulty.SoftLevel;
            int found = S.Objs.FindInRadiusMatching(Obj.Position,
                0.5f, Caches.Obj16, (o => o.Definition == Obj.Definition), 4);
            if (crowd > 1) {
                toAdd = 1;
            }
            // D.Warn("Spreading fungus to {0} out of {1} potential positions",
            //     toAdd, Caches.WeightedInts.Count);
            var toAddInt = Mathf.RoundToInt(toAdd);
            if (toAddInt < 1) {
                toAddInt = 1;
            }

            int attempts = Caches.WeightedInts.Count;
            while (toAddInt > 0 && attempts-- > 0 && Caches.WeightedInts.Count > 0) {
                var pos = Rng.UWeightedFrom(Caches.WeightedInts);
                Caches.WeightedInts.Remove(pos);
                if (EntityUtils.IsEmptyFloorAt(pos)) {
                    var cmd = new CmdSpawnObj(EntityUtils.CenterOf(pos) +
                        Rng.UInsideUnitCircle() * 0.5f, Obj.Definition, true, false);
                    S.CmdQ.Enqueue(cmd);
                    if (--toAddInt == 0) {
                        break;
                    }
                }
            }

            var floor = EntityUtils.FloorAt(objPos);
            if (floor != null) {
                floor.Health.TakeDamage(30f, DamageType.Structural);
            }
            Sounds.PlaySoftPop(Obj.Position);
        }
    }
}
