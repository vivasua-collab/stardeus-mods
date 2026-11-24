#if !PRODUCTION_BUILD
// #define DEBUG_PLANTS
#endif
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Rendering;
using KL.Utils;
using Game.Systems.AI;
using Game.Systems.Rendering;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class PlantComp : BaseComponent<PlantComp>,
            IAIGoalProvider {
        public string Id => entity.Definition.Id;
        public string PlantName => entity.Definition.NameT;
        public string PlantIcon => entity.Definition.Preview;
        public Def Def => entity.Definition;
        private AnimatedSpriteSet graphic;
        public int PlanterId { get; set; }
        private float growthRate;
        private float growth;
        public float Growth => growth;
        public bool IsGrown => growth > 0.999f;
        private float growthAfterHarvest;
        private float minTemp;
        private float maxTemp;
        public float MinTemp => minTemp;
        public float MaxTemp => maxTemp;
        public int WorkAmount;
        private byte lastStage = 0;
        private string growthState;
        public string GrowthState => growthState;
        public float WaterUsePerTick;
        private Mat[] harvestOutput;
        public Mat[] HarvestOutput => harvestOutput;
        public bool IsAutoHarvest { get; set; }
        private BFloat growthB;
        public BFloat GrowthB => growthB ??= new BFloat(growth);
        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();
        public string Size { get; private set; }
        private const float minHarvestGrowth = 0.95f;
        public bool CanBeHarvestedNow => growth >= minHarvestGrowth;
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new PlantComp());
        }

        protected override void OnConfig() {
            Size = Config.GetString(PropertyIdH.Size, "Small");
            WorkAmount = Config.GetInt(PropertyIdH.WorkAmount, 10);
            harvestOutput = Config.GetRawMaterialsRef(PropertyIdH.HarvestOutput, false, null);
            if (harvestOutput == null) {
                D.Err("Plant has no harvest output! {0}", Entity.Definition.Id);
            }
            foreach (var mat in harvestOutput) {
                if (mat.StackSize < 1) {
                    D.Err("Mat with Stack Size 0 in config of plant harvest output on {0}: {1}",
                        Entity.DefinitionId, mat.Type);
                }
            }
            WaterUsePerTick = Config.GetFloat(PropertyIdH.WaterUsePerDay, 0.1f);
            WaterUsePerTick /= Consts.TicksPerDay;

            growthRate = Config.GetFloat(PropertyIdH.GrowthRate, 1f);
            growthAfterHarvest = Config.GetFloat(PropertyIdH.GrowthAfterHarvest, -1f);
            var tempMin = Config.GetFloat(PropertyIdH.MinTempCelsius, -10f);
            var tempMax = Config.GetFloat(PropertyIdH.MaxTempCelsius, +50f);
            minTemp = Units.CelsiusToKelvin(tempMin);
            maxTemp = Units.CelsiusToKelvin(tempMax);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Growth", growth);
            data.SetInt("PlanterId", PlanterId);
        }

        protected override void OnLoad(ComponentData data) {
            growth = data.GetFloat("Growth", 0);
            PlanterId = data.GetInt("PlanterId", 0);
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
                    return;
                }
                graphic = sset;
            }
            if (growth < 0.2f) {
                if (lastStage == 1) {
                    return;
                }
                lastStage = 1;
                // seed stage
                //graphic.Sprites[0] = FindSprite("Seed");
                graphic.SetSprite(0, FindSprite("Seed"), S);
                // Seeds lie in the soil, they do not sway
                graphic.SetRenderer(S, 0, Obj.Graphics.Renderer, SpriteOrder.PlantSmall);
            } else if (growth < 0.5f) {
                if (lastStage == 2) {
                    return;
                }
                lastStage = 2;
                //graphic.Sprites[0] = FindSprite("Growing1");
                graphic.SetSprite(0, FindSprite("Growing1"), S);
                graphic.SetRenderer(S, 0, SwayRenderer, SpriteOrder.PlantSmall);
                // growth1 sprout
            } else if (growth < minHarvestGrowth) {
                if (lastStage == 3) {
                    return;
                }
                lastStage = 3;
                //graphic.Sprites[0] = FindSprite("Growing2");
                graphic.SetSprite(0, FindSprite("Growing2"), S);
                graphic.SetRenderer(S, 0, SwayRenderer, SpriteOrder.PlantBig);
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
                graphic.SetRenderer(S, 0, SwayRenderer, SpriteOrder.PlantBig);
            }
        }

        // A def's own renderer wins over the sway
        private string SwayRenderer => Obj.Graphics.Renderer ?? Renderers.Plants;

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
            SetGraphicWhenReady();
            if (growth >= 1f) {
                growthState = T.FullyGrown;
            } else {
                growthState = T.NotGrowing;
            }
            if (wasLoaded) {
                PlanterComp planter = null;
                if (PlanterId == 0) {
                    // TODO remove this later
                    planter = S.Components.FindFirstMatching<PlanterComp>(
                            (p) => p.HasPlantWithId(EntityId));
                    if (planter != null) {
                        //D.Warn("Injecting plant into closest planter! {0}->{1}",
                            //this, planter);
                        PlanterId = planter.EntityId;
                    }
                }
                if (planter == null && PlanterId > 0) {
                    planter = S.Components.Find<PlanterComp>(PlanterId);
                }
                if (planter == null) {
                    // This plant needs to be destroyed
                    D.ErrSoft("Removing orphan plant: {0}", this);
                    S.Objs.Destroy(Obj, false);
                }
            }
            Obj.Transform.SkipMoveLoose = true;
        }

        public override string ToString() {
            return $" * Plant [{PlantName} {growth * 100}%, {growthState}]";
        }

        private string lastNotGrowingWarning;

        public bool IsGrowing { get; private set; }

        public void Grow(float growthAmt, bool hasWater) {
            IsGrowing = false;
            if (!entity.IsActive) {
                D.Err("Trying to grow inactive plant! {0}", entity);
                return;
            }

            // Check temperature and oxygen
            var pos = Obj.PosIdx;
            var temp = S.GetTemp(pos);
            var light = S.Query.LightAtPos.Ask(pos);

            var hasTemp = Maths.InRange(minTemp, maxTemp, temp, out var tempDiff);

            if (hasTemp && hasWater) {
                if (light < 0.5f) {
                    growthState = T.EnvTooDark;
                    IsGrowing = false;
                } else {
                    if (growth < 1f) {
                        growth += growthAmt * growthRate * light;
                        IsGrowing = true;
                    }

                    if (growth >= 1) {
                        growth = 1;
                        growthState = T.FullyGrown;
                    } else {
                        growthState = T.Growing;
                    }
                    SetGraphicWhenReady();
                }
            } else {
                if (!hasTemp) {
                    if (tempDiff > 0) {
                        growthState = T.TempTooHot;
                    } else {
                        growthState = T.TempTooCold;
                    }
                } else if (!hasWater) {
                    growthState = T.MaterialLack_V1.F(T.MaterialWater);
                } else if (light < 0.5f) {
                    growthState = T.EnvTooDark;
                }
            }

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

            if (IsAutoHarvest && IsGrown) {
                TryOrderHarvest();
            }
            growthB?.Set(growth);
            stateChangedB?.Invoke();
        }

        public void DestroyPlant() {
            S.Objs.Destroy(Obj);
        }

        public void DevSetGrowth(float pct) {
            growth = pct;
            Grow(0f, true);
        }

        public override void OnRemove() {
            //D.Warn("Removing plant: {0}", entity);
        }

        public Mat[] Harvest() {
            HideInfoIcon();
            // The plant itself is harvested
            if (growthAfterHarvest < 0) {
                S.Objs.Destroy(Obj, false);
            } else {
                if (Obj.Health.IsDamaged) {
                    Obj.Health.RepairDamage(1000);
                }
                growthState = T.Growing;
                growth = growthAfterHarvest;
                SetGraphicWhenReady();
                growthB?.Set(growth);
                stateChangedB?.Invoke();
            }
            return Arrays.Copy(harvestOutput);
        }

        public bool TryOrderHarvest(bool byPlayer = false) {
            if (entity.Goal != null) {
                return false;
            }
            if (!CanBeHarvestedNow) {
                return false;
            }
            var goal = CreateGoal(JobTypeIdH.Plants, IconId.CLeaf, T.Harvest, this)
                .WithWorldToDesired(AIVarsH.IsHarvested, false, true)
                .ByPlayer(byPlayer)
                .WithRequiredAbilities(AbilityIdH.Manipulate, AbilityIdH.DumbLabor)
                .WriteInitialState();
            return entity.TrySetGoal(goal);
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }

        public void OnGoalLoad(AIGoal goal) {
        }
    }
}
