#if UNITY_EDITOR
// #define DEBUG_LIGHTS
#endif
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Light;
using Unity.Mathematics;
using Game.Systems.UIUX;
using Game.Visuals;

namespace Game.Components {
    /*
    * Render Slot hacks:
    * UV = pulsate color
    * Transform.x = pulsate rate (0 = no pulsation)
    */
    public sealed class LightSourceComp : BaseComponent<LightSourceComp>,
            ICopyableComp, IDefPowerProvider {
        private const float DefaultEnergyCost = 0f;
        public ElectricNodeComp ENode => elNode;
        private float customRange;
        private float defaultRadius;
        private float radius;
        // How strongly the near white pixels of the light's own sprite glow (LightGlowBuffer);
        // 0 for none
        private float glow;
        // Root pos of the tiles marked as glowing, -1 for none
        private int glowPos = -1;
        private int glowW;
        private int glowH;
        private float halfRad;
        // (radius/2) squared
        //private float sqrRange;
        private Color color;
        private B<Color> colorB;
        public B<Color> ColorB => colorB ??= new B<Color>(color);
        public Color MainColor => color;
        private bool isLightWantedAfterInit;
        private bool isLightWanted;
        private bool isEnoughEnergy;
        private bool isLightOn;
        private bool needsEnergy;
        public bool IsEmitting => isLightOn;
        //private RenderSlot renderSlot;
        //private RenderSlot renderSlotUnobstructed;
        private bool isLightSourceCreated;
        private ushort lightSourceIdx;
        private LightSource lightSource;
        private bool isPulsating;
        private float pulsateSpeed;
        private bool canMoveRealtime;
        private float defaultEnergyCost;
        private float energyCost;
        private Color pulsateColor;
        private ElectricNodeComp elNode;
        private bool isCustomizable;
        public string CopyConfigText => T.CopyConfigLight;
        public bool IsCopyable => isCustomizable;
        // Main lights count towards lighting up the map and auto ambient color value
        public bool IsMainLight;
        private bool isListeningToLateUpdate;
        public int LightType;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new LightSourceComp());
            isLightReservationWarned = false;
        }

        // At the default range; a customized light scales it with the range squared
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            if (!config.GetBool(PropertyIdH.NeedsEnergy, true)) { return; }
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        protected override void OnConfig() {
            defaultRadius = radius = Config.GetFloat(PropertyIdH.Radius, 5);
            glow = Config.GetFloat(PropertyIdH.Glow, 0f);
            if (radius < 0.1f) {
                D.Err("Radius of light is less than 0.1f, fixing to 1f! {0}",
                    Entity);
                radius = 1f;
            }
            //sqrRange = (radius / 2f) * (radius / 2f);
            halfRad = radius / 2f;
            IsMainLight = Config.GetBool(PropertyIdH.IsMainLight, false);
            defaultEnergyCost = energyCost = Config.GetFloat(PropertyIdH.EnergyCost,
                DefaultEnergyCost);
            customRange = 1f;
            needsEnergy = Config.GetBool(PropertyIdH.NeedsEnergy, true);

            isCustomizable = Config.GetBool(PropertyIdH.IsCustomizable, false);
            #if DEBUG_LIGHTS
            isCustomizable = true;
            #endif
            var colorData = Config.GetIntSet(PropertyIdH.Color, null);
            var pulsateColorData = Config.GetIntSet(PropertyIdH.PulsateColor, null);
            var freqRange = Config.GetVector2(PropertyIdH.FrequencyRange, Vector2.one);
            if (Tile != null) {
                if (IsMainLight) {
                    LightType = 0;
                } else {
                    LightType = 1;
                }
            } else {
                LightType = 2;
            }

            // Not using statefull randomness here as it makes no sense
            pulsateSpeed = Rng.URange(freqRange);

            if (colorData == null) {
                color = Consts.DefaultLightColor;
            } else {
                color = Colors.FromData(colorData);
            }
            if (pulsateColorData != null) {
                isPulsating = true;
                pulsateColor = Colors.FromData(pulsateColorData);
            }

            isLightWantedAfterInit = Config.GetBool(PropertyIdH.IsOn, true);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            if (!needsEnergy) {
                data.SetBool("IsOn", isLightWanted);
            }
            if (isCustomizable) {
                data.SetFloat("Range", customRange);
                data.SetFloat("r", color.r);
                data.SetFloat("g", color.g);
                data.SetFloat("b", color.b);
                data.SetFloat("a", color.a);
            }
        }

        protected override void OnLoad(ComponentData data) {
            if (!needsEnergy) {
                isLightWantedAfterInit = data.GetBool("IsOn", true);
            }
            if (isCustomizable) {
                var range = data.GetFloat("Range", customRange);
                SetCustomRange(range);
                color.r = data.GetFloat("r", color.r);
                color.g = data.GetFloat("g", color.g);
                color.b = data.GetFloat("b", color.b);
                color.a = data.GetFloat("a", color.a);
                MigrateAlphaToLuminosity();
            }
        }

        private void MigrateAlphaToLuminosity() {
            var a = color.a;
            if (a < 1f) {
                Color.RGBToHSV(color, out var h, out var s, out var v);
                color = Color.HSVToRGB(h, s, v * a);
                color.a = 1f;
            }
        }

        private bool isInitialized;
        private void InitializeLight() {
            canMoveRealtime = Tile == null;
            isInitialized = true;
            OnRendererReady();
        }
        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                Ready.When(WhenH.BeingsLoaded, InitializeLight);
            } else {
                entity.AfterInitialize += InitializeLight;
            }
        }

        public override void OnRemove() {
            if (isListeningToLateUpdate) {
                isListeningToLateUpdate = false;
                S.Clock.OnUpdate60.RemoveListener(OnLateUpdate);
            }
            //SwitchLight(false);
            if (isLightSourceCreated) {
                UnmarkGlow();
                S.Sys.Light.RemoveLight(lightSourceIdx);
                isLightSourceCreated = false;
            }
        }

        public void SwitchLight(bool on) {
            isLightWanted = on;
            isEnoughEnergy = true;
            // We are not yet initialized
            if (isInitialized) {
                CheckLight(false);
            } else {
                isLightWantedAfterInit = on;
            }
        }

        public void SwitchUnchecked(bool on) {
            isLightOn = isLightWanted = on;
            if (isLightSourceCreated) {
                if (on) {
                    if (lightSource.Range != halfRad) {
                        lightSource.Range = halfRad;
                        S.Sys.Light.SetLight(lightSourceIdx, lightSource);
                    }
                } else {
                    if (lightSource.Range != 0f) {
                        lightSource.Range = 0f;
                        S.Sys.Light.SetLight(lightSourceIdx, lightSource);
                    }
                }
            }
        }

        // Optimistic options is for grid changes. New grid may not be powered
        // before first tick happens, so we will keep the light active
        private void CheckLight(bool optimistic) {
            if (!isLightSourceCreated) {
                // D.Warn("Light source not created for {0}", entity);
                return;
            }
            var shouldBeOn = isLightWanted && isEnoughEnergy;
            if (elNode != null) {
                if (optimistic) {
                    if (!elNode.IsPowerable) {
                        shouldBeOn = false;
                    }
                } else if (!elNode.IsConnected || (!elNode.IsConsuming && !elNode.IsProducing && !elNode.IsConnector)) {
                    shouldBeOn = false;
                }
            }
            if (Entity.Definition.LayerId == WorldLayer.Floor) {
                var pos = entity.PosIdx;
                if (EntityUtils.IsWallAt(pos)) {
                    // Floor light blocked by the wall above it
                    shouldBeOn = false;
                }
            }
            // D.Warn("Light should be on: {0} {1}", shouldBeOn, entity);
            //lightSource.SqrRange = 0;
            if (!isLightOn && shouldBeOn) {
                isLightOn = true;
                lightSource.Range = halfRad;
                UpdateRenderSlotPosition(false);
            } else if (isLightOn && !shouldBeOn) {
                isLightOn = false;
                lightSource.Range = 0;
                S.Sys.Light.SetLight(lightSourceIdx, lightSource);
            }
        }

        public bool IsLightAvailable {
            get {
                if (entity is Tile t) {
                    return t.IsConstructed && t.Health.IsOperational;
                }
                return true;
            }
        }

        private double nextLightCheck;

        private void OnLateUpdate() {
            if (!UIShowing.FullScreenWidget) {
                UpdateRenderSlotPosition(false);
            }
        }

        private static bool isLightReservationWarned;

        private void OnRendererReady() {
            // Why is this here?
            if (isLightSourceCreated) { return; }
            if (Tile?.IsConstructed == false) { return; }
            if (!Entity.IsActive) { return; }
            if (canMoveRealtime) {
                if (!isListeningToLateUpdate) {
                    isListeningToLateUpdate = true;
                    S.Clock.OnUpdate60.AddListener(OnLateUpdate);
                }
            }
            if (isLightWantedAfterInit && Being != null) {
                if (Being.IsDead) {
                    isLightWantedAfterInit = false;
                }
            }

            lightSource = new LightSource() {
                Type = LightType,
                Color = Colors.ToF3(color),
                Position = Entity.Position,
                Glow = glow,
            };

            if (isPulsating) {
                lightSource.Color2 = Colors.ToF3(pulsateColor);
                lightSource.PulseFreq = pulsateSpeed;
            }

            if (!S.Sys.Light.TryReserveSlot(out lightSourceIdx)) {
                if (!isLightReservationWarned) {
                    isLightReservationWarned = true;
                    D.Err("Could not reserve a light slot: {0}", Entity);
                }
                isLightSourceCreated = false;
            } else {
                isLightSourceCreated = true;
            }
            isLightOn = false;
            SwitchLight(isLightWantedAfterInit);
            // to add the turned off lights to the light map after load
            UpdateRenderSlotPosition(false);
        }

        private void UpdateRenderSlotPosition(bool tileMoved) {
            if (!isLightSourceCreated) { return; }
            if (tileMoved) {
                S.Sys.Light.RemoveLightFromOverlay(lightSourceIdx);
            }
            var p = Entity.Position;
            if (EntityUtils.IsWithinBounds(p)) {
                lightSource.Position = new float2(p.x, p.y);
                S.Sys.Light.SetLight(lightSourceIdx, lightSource);
                UpdateGlow();
            } else {
                D.Err("Trying to update render slot position for light source at OOB: {0}", p);
            }
        }

        // Only flat sprites glow: a tall object's upper part is drawn on another layer
        private void UpdateGlow() {
            if (glow <= 0f || Tile == null || Tile.Graphics?.HasHeight == true) { return; }
            var layer = Entity.Definition.LayerId;
            if (!LightGlowBuffer.IsGlowLayer(layer)) { return; }
            var tr = Tile.Transform;
            var pos = tr.PosIdx;
            var w = tr.RotatedWidth;
            var h = tr.RotatedHeight;
            if (pos == glowPos && w == glowW && h == glowH) { return; }
            UnmarkGlow();
            glowPos = pos;
            glowW = w;
            glowH = h;
            S.Sys.Light.Glow?.Mark(lightSourceIdx, layer, pos, w, h);
        }

        private void UnmarkGlow() {
            if (glowPos < 0) { return; }
            S.Sys.Light.Glow?.Unmark(lightSourceIdx, Entity.Definition.LayerId,
                glowPos, glowW, glowH);
            glowPos = -1;
        }

        private void AfterGridTick() {
            if (!isLightWanted && !isLightOn) { return; }
            if (G.ClockSpeed > ClockSpeed.Fast2 ||
                    GameTimer.realtimeSinceStartupAsDouble > nextLightCheck)  {
                if (Ready.Gen) {
                    nextLightCheck = GameTimer.realtimeSinceStartupAsDouble
                        + Rng.URange(0.5f, 1.5f);
                }
                CheckLight(false);
                UpdateFlicker();
            }
        }

        // How long after a power deficit the lamp keeps flickering, in ticks
        private const long DeficitFlickerTicks = 30;

        // A lamp flickers while its grid is short of power or while it is damaged. Drawing only
        // (Light.compute); the light map the AI reads is unaffected.
        private void UpdateFlicker() {
            if (!isLightSourceCreated) { return; }
            var flicker = 0f;
            if (isLightOn && AmbientMotion.IsOn) {
                if (elNode != null && elNode.Node.GridId != -1) {
                    var grid = elNode.Grid;
                    if (elNode.HasDeficit || grid.LastDeficitTick > 0
                            && G.Ticks - grid.LastDeficitTick < DeficitFlickerTicks) {
                        flicker = 1f;
                    }
                }
                // A heavily damaged lamp is off anyway, so a quarter gone already flickers hard
                if (Tile?.Health != null && Tile.Health.IsDamaged) {
                    flicker = Mathf.Max(flicker, Tile.Health.IsSignificantlyDamaged ? 0.8f : 0.35f);
                }
            }
            if (lightSource.Flicker != flicker) {
                lightSource.Flicker = flicker;
                S.Sys.Light.SetLight(lightSourceIdx, lightSource);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (!entity.IsActive) { return; }
            if (needsEnergy && message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    elNode = enode;
                    if (energyCost > 0f) {
                        elNode.SetConsumerWantedInput(energyCost);
                    }
                    elNode.AddAfterTick(this, AfterGridTick);
                    CheckLight(false);
                }
            } else if (needsEnergy && (message == MsgIdH.ElectricNodeConnectionChanged
                    || message == MsgIdH.ElectricNodeSwitchedOff
                    || message == MsgIdH.ElectricNodeSwitchedOn)) {
                // Right away, not on the next grid tick: Turn Off on a paused game left the
                // lamp lit while cutting its wire put it out (STA-1926)
                isEnoughEnergy = true;
                CheckLight(true);
            } else if (message == MsgIdH.TilePosChanged) {
                //SpriteSet?.Teleport(entity.Position);
                //SpriteSetUnob.Teleport(entity.Position);
                //UpdateRoomId();
                UpdateRenderSlotPosition(true);
            } else if (message == MsgIdH.ConstructionFinished /*&& SpriteSet != null*/) {
                // It is possible that constructable comp will send this before
                // we manage to actually initialize the light in OnLateReady.
                // So if this gets called first, it should initialize the light
                // without assumptions that late ready was called;
                if (!isInitialized) {
                    InitializeLight();
                } else {
                    OnRendererReady();
                }
                CheckLight(false);
            } else if (message == MsgIdH.HealthWentToZero) {
                SwitchLight(false);
            }
        }

        public override string ToString() {
            return " * LightSource";
        }

        private void UpdateColor(Color col) {
            if (!isLightSourceCreated) {
                return;
            }
            lightSource.Color = Colors.ToF3(col);
            if (isPulsating) {
                lightSource.Color2 = Colors.ToF3(pulsateColor);
            }
            S.Sys.Light.SetLight(lightSourceIdx, lightSource);
        }

        public float CustomRange => customRange;

        public void SetCustomRange(float v) {
            if (customRange == v) { return; }
            customRange = v;
            if (needsEnergy) {
                energyCost = defaultEnergyCost * customRange * customRange;
                elNode?.SetConsumerWantedInput(energyCost);
            }
            SetRadius(defaultRadius * customRange);
        }

        public void SetColor(Color c) {
            // D.Warn("Setting color on light at {0}: {1}", Pos.String(entity.PosIdx), c);
            if (isCustomizable) {
                color = c;
                colorB?.Set(color);
                UpdateColor(color);
            } else {
                D.Err("Skipping setcolor call on uncustomizable light! {0}",
                    Entity);
            }
        }
        public void SetPulsating(Color c1, Color c2) {
            isPulsating = true;
            color = c1;
            pulsateColor = c2;
            UpdateColor(c1);
            /*
            TODO
            if (renderSlot != null) {
                renderSlot.Rot = pulsateSpeed;
                renderSlotUnobstructed.Rot = pulsateSpeed;
                renderSlot.Color = renderSlotUnobstructed.Color = Colors.ToF4(c1);
                renderSlot.UV = renderSlotUnobstructed.UV = Colors.ToF4(c2);
            }
            */
        }

        public void SetRadius(float r) {
            if (Maths.Approximately(r, radius)) { return; }
            radius = r;
            halfRad = r / 2f;
            if (!isLightSourceCreated) { return; }
            if (lightSource.Range > 0) {
                lightSource.Range = halfRad;
            }
            S.Sys.Light.SetLight(lightSourceIdx, lightSource);
            //CheckLight(true);
        }

        public bool CanCopyTo(ICopyableComp target) {
            return target is LightSourceComp lc && lc.isCustomizable;
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is LightSourceComp lc && lc.isCustomizable) {
                lc.SetColor(color);
                //lc.customRange = customRange;
                lc.SetCustomRange(customRange);
            }
            return true;
        }
    }
}
