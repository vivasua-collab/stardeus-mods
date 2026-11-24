#if !PRODUCTION_BUILD
// #define DEBUG_SHIP_DEVICES
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Turrets;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.UI.InGame;
using Game.Rendering;
using Game.Input;

namespace Game.Systems.Ships {
    /// <summary>
    /// For displaying ship sprite in game view mid combat
    /// </summary>
    public sealed class ShipView : MonoBehaviour, ICustomPoolable, ITurretTarget {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Transform viewTransform;
        [SerializeField] private Transform zoneContainer;
        [SerializeField] private Transform barContainer;
        private ShipOffScreenMarker marker;
        public bool IsVisible => marker.IsShipVisible;
        public Vector2 Position => (Vector2) viewTransform.position + aimOffset;
        public Vector2 CenterPosition => viewTransform.position;
        public NPCShip Ship;
        public GameState S;
        public ForceFieldView ShieldView;
        private float tiltAngle;
        /// <summary>
        /// The tilt that lines the ship up with the map center. Only applies while the
        /// ship faces the map, an overridden facing points somewhere else entirely.
        /// </summary>
        private float defaultTiltAngle;
        private float shipScale;
        public ShipViewSlot Slot { get; private set; }
        public float ShipWidthUnits;
        private readonly List<ShipZoneView> spawnedZones = new();
        private InGameProgressBar healthBar;
        private InGameProgressBar fluxBar;
        // Read-only, for tests that assert the pooled bars came back at prefab scale
        public InGameProgressBar HealthBar => healthBar;
        public InGameProgressBar FluxBar => fluxBar;
        private static int pipCameraLayer;
        private static int gameCameraLayer;
        private bool isSubscribed;
        private int wantedLayer;
        private int currentLayer;
        private Vector2 aimOffset {
            get {
                var t = Time.time;
                return new Vector2(Mathf.Sin(t), Mathf.Cos(t)) * Ship.Width * 0.15f;
            }
        }

        public Vector2 AdjustedHitPos(Vector2 turretPos, IShipWeaponTarget subTarget,
                bool isHit, bool hitShield, bool hitPointDefense) {
            Vector2 pos;
            if (!isHit) {
                if (subTarget != null) {
                    // add a little bit
                    pos = subTarget.Position + Rng.UInsideUnitCircle().normalized * 2f;
                } else {
                    pos = (Vector2) viewTransform.position + aimOffset;
                }
                var dist = Vectors.Distance(turretPos, pos);
                var dir = (pos - turretPos).normalized;
                // hit behind the ship
                return turretPos + dir * (dist + Ship.Width * 2f);
            } else {
                pos = subTarget?.Position ?? (Vector2) viewTransform.position + aimOffset;
            }
            if (hitShield || hitPointDefense) {
                var dir = (pos - turretPos).normalized;
                float offset;
                float dist;
                if (hitPointDefense) {
                    offset = 0.55f;
                    dist = Vectors.Distance(turretPos, pos);
                } else {
                    if (Maths.FindIntersectionPoint(turretPos, dir,
                            ShieldView.Center, ShieldView.Radius * 0.95f, out var point)) {
                        return point;
                    }
                    offset = 0.45f;
                }
                dist = Vectors.Distance(turretPos, pos);
                return turretPos + dir * (dist - Ship.Width * offset);
            }
            return pos;
        }

        public void SetViewSlot(ShipViewSlot slot) {
            if (slot != null) {
                slot.View = this;
                transform.localPosition = slot.Position;
            }
            Slot = slot;
        }

        public void EnsureNearPlayer() {
            // TODO other rotations
            var loc = Ship.NearbyPosFrom(A.Player.Location, out var isNear, 3f);
            if (!isNear) {
                D.ErrSoft("Trying to spawn ship view when NPC ship is too far! {0} | player: {1}",
                    Ship.SO.Location, A.Player.Location);
                Ship.TeleportToSectorLocation(loc);
            }
        }
        private void OnPIPActivated(PIPCamera pip, bool active) {
            UpdatePIPLayer();
        }

        private void OnPIPSubjectChanged(NPCShip subject) {
            UpdatePIPLayer();
        }

        /// <summary>
        /// Only the ship the inset is actually showing moves to the PIP layer, every
        /// other ship stays where the game camera can see it.
        /// </summary>
        private bool IsOnPIPLayer => PIPCamera.IsActive && PIPCamera.Subject == Ship;

        private void UpdatePIPLayer() {
            wantedLayer = IsOnPIPLayer ? pipCameraLayer : gameCameraLayer;
        }

        public void LoadShip(GameState s) {
            if (pipCameraLayer == 0) {
                pipCameraLayer = LayerMask.NameToLayer("PIP");
                gameCameraLayer = LayerMask.NameToLayer("PIPAndGame");
            }
            UpdatePIPLayer();
            if (!isSubscribed) {
                isSubscribed = true;
                The.R?.Sig?.PIPCameraActivated.AddListener(OnPIPActivated, gameObject);
                The.R?.Sig?.PIPSubjectChanged.AddListener(OnPIPSubjectChanged, gameObject);
            }
            #if UNITY_EDITOR
            name = $"ShipView_{Ship}";
            #endif
            ShieldView ??= new ForceFieldView(s);
            var offScreenMarkerPrefab = ResourceLibrary.FindPrefab<ShipOffScreenMarker>();
            marker = PrefabPool.Spawn(offScreenMarkerPrefab, Consts.V3Zero, Consts.QId, ResourceLibrary.Root);
            marker.shipView = this;

            var sprite = Images.Sprite(Ship.Class.Graphic(Slot.ShipFacing, false));
            spriteRenderer.sprite = sprite;
            #if UNITY_EDITOR
            if (Slot.IsColliding) {
                spriteRenderer.color = Color.red;
            }
            #endif

            float spriteWidthUnits = sprite.rect.width / sprite.pixelsPerUnit;
            shipScale = Ship.Width / spriteWidthUnits;
            ShipWidthUnits = shipScale;

            viewTransform.localScale = Consts.V3One * shipScale;
            viewTransform.localRotation = Consts.QId;
            zoneContainer.localRotation = Consts.QId;

            if (healthBar == null) {
                var barPrefab = ResourceLibrary.FindPrefab<InGameProgressBar>();
                healthBar = PrefabPool.Spawn(barPrefab, Consts.V3Zero, Consts.QId, barContainer);
                healthBar.transform.localRotation = Consts.QId;
                // A reused bar is reparented keeping its world scale, which under the scaled
                // barContainer turns into a fraction of the prefab scale
                healthBar.transform.localScale = Consts.V3One;
                var ship = Ship;
                healthBar.SetTooltip(() => {
                    return $"{T.NounHullPoints}: {ship.CombatState.HP}/{ship.CombatState.MaxHP}";
                });
                healthBar.SetFillColor(Consts.ColorHealthBar);
                healthBar.SetBackColor(new Color(0f, 0f, 0f, 0.5f));
            }

            if (fluxBar == null) {
                var dualBarPrefab = ResourceLibrary.FindPrefab<InGameProgressBar>("InGameProgressBarDual");
                fluxBar = PrefabPool.Spawn(dualBarPrefab, Consts.V3Zero, Consts.QId, barContainer);
                fluxBar.transform.localRotation = Consts.QId;
                fluxBar.transform.localScale = Consts.V3One;
                var ship = Ship;
                fluxBar.SetTooltip(() => {
                    if (GameState.IsCurrent(S)) {
                        return ShipCombatWidgetUtils.GetFluxTip(ship, includeSettings: false);
                    }
                    return null;
                });
                fluxBar.SetFillColor(ShipCombatWidgetUtils.FluxSoftColor);
                fluxBar.SetFillColor2(ShipCombatWidgetUtils.FluxHardColor);
                fluxBar.SetBackColor(new Color(0f, 0f, 0f, 0.5f));
            }

            // 1. find angle between visual and map center
            var mapCenter = S.Grid.Center;

            var angle = Vectors.SignedAnglesBetween(mapCenter + Facing.ToOffset(Slot.Edge, 10f), mapCenter, Slot.Position);
            defaultTiltAngle = angle * 0.75f;
            tiltAngle = Slot.FacingOverride == null ? defaultTiltAngle : 0f;

            SpawnDeviceZones(Slot, shipScale, Slot.RotAngle);
            EnsureNearPlayer();
            zoneContainer.localRotation = Quaternion.Euler(0f, 0f, Slot.RotAngle);
        }

        /// <summary>
        /// Turns the ship to face what it is currently attacking. An NPC target means
        /// facing that ship's view; the player, or no target at all, goes back to the
        /// default edge based facing that looks at the player's map.
        /// Driven entirely by ShipCombatAI.ResolveFocus - facing is derived state.
        /// </summary>
        public void FaceTarget(NPCShip target) {
            Facing.Type? wanted = null;
            if (target != null && !target.IsPlayer
                    && target.View != null && target.View != this) {
                wanted = Facing.FromDirection(
                    target.View.CenterPosition - CenterPosition, Slot.ShipFacing);
            }
            // Nullable comparison covers both halves of the rule: switching from an NPC
            // back to the player can land on the same cardinal facing and still has to
            // drop the override and put the map center tilt back.
            if (wanted == Slot.FacingOverride) { return; }
            Slot.FacingOverride = wanted;
            ApplyFacing();
        }

        private void ApplyFacing() {
            var facing = Slot.ShipFacing;
            var rotAngle = Slot.RotAngle;
            // shipScale deliberately stays as LoadShip computed it - a ship that turns
            // has to keep its size, only its orientation changes
            spriteRenderer.sprite = Images.Sprite(Ship.Class.Graphic(facing, false));
            zoneContainer.localRotation = Quaternion.Euler(0f, 0f, rotAngle);
            tiltAngle = Slot.FacingOverride == null ? defaultTiltAngle : 0f;
            // re-align the weapon / engine zones with the new ship orientation
            foreach (var zone in spawnedZones) {
                zone.ShipDev.Device.SetView(zone, facing, rotAngle);
            }
        }

        public void ToggleShieldVisual(bool on, bool instant = false) {
            if (on == ShieldView.IsUp) { return; }
            var side = Ship.CombatSideLabel;
            if (on) {
                A.S.Sys.Log.AddLine($"{T.NounShields} ({side}) {GlyphMap.ArrowRight} {Texts.Accented(T.Active)}");
                ShieldView.Enable(viewTransform.position, Ship.Width * 1.25f, instant);
            } else {
                A.S.Sys.Log.AddLine($"{T.NounShields} ({side}) {GlyphMap.ArrowRight} {Texts.Accented(T.StateDisabled)}");
                ShieldView.Disable(instant);
            }
        }

        public void Tick() {
            foreach (var zone in spawnedZones) {
                zone.Tick();
            }
        }

        private void SpawnDeviceZones(ShipViewSlot slot, float shipSpriteScale, float rotAngle) {
            var zonePrefab = ResourceLibrary.FindPrefab<ShipZoneView>();
            var rng = Ship.SeededRng(Primes.Prime037);
            // var angle = Slot.RotAngle;
            var center = viewTransform.position;
            var dir = slot.ShipFacing;

            int numDevice = 0;
            int total = Ship.CombatState.Weapons.Count;
            var pattern = (rng.Range(0, 2) + 0) % 3;
            // D.Warn("Weapon Pattern: {0}: {1}", pattern, Ship);
            foreach (var device in Ship.CombatState.Weapons) {
                numDevice = SpawnDeviceZone(
                    shipSpriteScale, rotAngle, zonePrefab, rng,
                    dir, numDevice, total, pattern, device);
            }

            numDevice = 0;
            total = Ship.CombatState.Shields.Count;
            pattern = (rng.Range(0, 2) + 0) % 3;
            // D.Warn("Shield Pattern: {0}: {1}", pattern, Ship);
            foreach (var device in Ship.CombatState.Shields) {
                numDevice = SpawnDeviceZone(
                    shipSpriteScale, rotAngle, zonePrefab, rng,
                    dir, numDevice, total, pattern, device);
            }

            numDevice = 0;
            total = Ship.CombatState.Capacitors.Count;
            pattern = (rng.Range(0, 2) + 0) % 3;
            foreach (var device in Ship.CombatState.Capacitors) {
                numDevice = SpawnDeviceZone(
                    shipSpriteScale, rotAngle, zonePrefab, rng,
                    dir, numDevice, total, pattern, device);
            }

            numDevice = 0;
            total = Ship.CombatState.Vents.Count;
            pattern = (rng.Range(0, 2) + 0) % 3;
            foreach (var device in Ship.CombatState.Vents) {
                numDevice = SpawnDeviceZone(
                    shipSpriteScale, rotAngle, zonePrefab, rng,
                    dir, numDevice, total, pattern, device);
            }

            numDevice = 0;
            total = Ship.CombatState.Engines.Count;
            pattern = (rng.Range(0, 2) + 0) % 3;
            // D.Warn("Engine Pattern: {0}: {1}", pattern, Ship);
            foreach (var device in Ship.CombatState.Engines) {
                numDevice = SpawnDeviceZone(
                    shipSpriteScale, rotAngle, zonePrefab, rng,
                    dir, numDevice, total, pattern, device);
            }
        }

        private int SpawnDeviceZone(float shipSpriteScale, float rotAngle,
                ShipZoneView zonePrefab, Rng rng, Facing.Type dir,
                int numDevice, int total, int pattern, IShipDevice device) {
            var zonePos = NextDeviceZonePos(device, rng, pattern, ref numDevice, total);
            var zone = PrefabPool.Spawn(zonePrefab, zonePos, Consts.QId, zoneContainer);
            zone.LocalPos = zonePos;
            zone.Ship = Ship;
            zone.ShipDev = device;
            numDevice++;
            zone.SetShipSize(shipSpriteScale);
            device.Device.SetView(zone, dir, rotAngle);
            spawnedZones.Add(zone);
            #if DEBUG_SHIP_DEVICES
            zone.DebugInfo = $"Device: {device}. Zone: #{numDevice}: {zonePos}. Pat: {pattern}";
            #endif
            return numDevice;
        }

        public readonly Vector2[][] shieldPositions = new Vector2[][] {
            new Vector2[] { new(0f, 0f) }, // 1 shield
            new Vector2[] { new(0f, -0.1f), new(0f, 0.1f) }, // 2 shields
            new Vector2[] { new(0f, -0.1f), new(0f, 0.1f), new(-0.1f, 0f) }, // 3 shields
            new Vector2[] { new(0f, -0.1f), new(0f, 0.1f), new(-0.1f, 0f), new(0.1f, 0f) }, // 4 shields
            new Vector2[] { new(0f, 0f), new(0f, -0.15f), new(0f, 0.15f), new(-0.15f, 0f), new(0.15f, 0f) }, // 5 shields
            new Vector2[] { new(0f, -0.2f), new(0f, 0.2f), new(-0.2f, 0f), new(0.2f, 0f), new(-0.2f, -0.2f), new(-0.2f, 0.2f) }, // 6 shields
            new Vector2[] { new(0f, -0.2f), new(0f, 0.2f), new(-0.2f, 0f), new(0.2f, 0f), new(-0.2f, -0.2f), new(-0.2f, 0.2f), new(0.3f, 0f) }, // 7 shields
            new Vector2[] { new(0f, -0.2f), new(0f, 0.2f), new(-0.2f, 0f), new(0.2f, 0f), new(-0.2f, -0.2f), new(-0.2f, 0.2f), new(0.3f, 0f), new(-0.3f, 0f) }, // 8 shields
        };
        private Vector2 NextEngineZonePos(Rng rng, int pattern,
                ref int num, int total, int depth = 0) {
            var startOffX = -0.5f;
            float density = Ship.Class.Visual_EngineDensity;
            var startOffY = density;
            var shiftX = 0.1f;
            var shiftY = density;
            // if (num == 0) {
                // D.Warn("Density: {0}. Total: {1}, startOff: {2}. shift: {3}",
                    // density, total, startOffY, shiftY);
            // }

            int perRowMax = Ship.Class.Visual_EnginesPerRow;
            int numRow = num / perRowMax;

            var x = startOffX + (num / (perRowMax - numRow)) * shiftX;
            // x = 1f - x;
            var yMod = (perRowMax - numRow) / 2;

            var y = startOffY + ((num / 2) % yMod) * shiftY;

            y *= Ship.Class.ViewZoneWidthCurve.Evaluate(x);

            if (y < 0.2f && x < 0.25f && x > -0.25f) {
                num++;
                if (depth < 10) {
                    return NextEngineZonePos(rng, pattern,
                        ref num, total, depth + 1);
                } else {
                    D.Err("NextWeaponZone went too deep!");
                }
           }

            var sign = num % 2 == 0 ? 1f : -1f;
            y *= sign;
            var curveSign = Mathf.Sign(Ship.Class.Visual_EngineCurvatureSign);
            var curveAmt = Mathf.Cos(y) * curveSign;
            x += curveAmt;
            x -= curveSign;

            // D.Warn("W: #{0}. X: {1}. Y: {2}. sig: {3}", num, x, y, sign);
            var vec = new Vector2(x, y);
            return  vec;
        }

        private Vector2 NextShieldZonePos(Rng rng, int pattern, int num, int total) {
            if (total == 1) {
                return Consts.V2Zero;
            }
            // Calculate the angle for the n-th position on the unit circle
            float angle = (360f / total) * num;
            float x = Maths.FastCos(angle);
            float y = Maths.FastSin(angle);
            var vec = new Vector2(x, y).normalized * 0.05f;
            return vec;
        }
        private Vector2 NextCapacitorZonePos(Rng rng, int pattern, int num, int total) {
            // Calculate the angle for the n-th position on the unit circle
            float angle = 90f + (180f / total) * num;
            float x = Maths.FastCos(angle);
            float y = Maths.FastSin(angle);
            var vec = new Vector2(x, y).normalized * 0.1f;
            return vec;
        }
        private Vector2 NextVentZonePos(Rng rng, int pattern, int num, int total) {
            // Mirror of NextCapacitorZonePos, swept across the opposite semicircle so
            // vents don't render on top of capacitors.
            float angle = -90f + (180f / total) * num;
            float x = Maths.FastCos(angle);
            float y = Maths.FastSin(angle);
            var vec = new Vector2(x, y).normalized * 0.075f;
            return vec;
        }

        private Vector2 NextSpaceStationWeaponZonePos(Rng rng, int pattern,
                int num, int total) {
            Vector2 vec;
            // Calculate the angle for the n-th position on the unit circle
            float angle = (360f / total) * num;
            float x = Maths.FastCos(angle);
            float y = Maths.FastSin(angle);

            // Set the position on the unit circle edge
            vec = new Vector2(x, y).normalized * 0.5f;
            return vec;
        }

        private Vector2 NextDeviceZonePos(IShipDevice device,
                Rng rng, int pattern, ref int num, int total, int depth = 0) {
            if (device is ShipWeapon) {
                if (Ship.Class.IsSpaceStation) {
                    return NextSpaceStationWeaponZonePos(rng, pattern, num, total);
                } else {
                    return NextWeaponZonePos(rng, pattern, ref num, total, depth);
                }
            } else if (device is ShipShield) {
                return NextShieldZonePos(rng, pattern, num, total);
            } else if (device is ShipCapacitor) {
                return NextCapacitorZonePos(rng, pattern, num, total);
            } else if (device is ShipVent) {
                return NextVentZonePos(rng, pattern, num, total);
            } else if (device is ShipEngine) {
                return NextEngineZonePos(rng, pattern, ref num, total, depth);
            } else {
                D.Err("Unhandled device class: {0}", device);
                return NextWeaponZonePos(rng, pattern, ref num, total, depth);
            }
        }
        private Vector2 NextWeaponZonePos(Rng rng, int pattern,
                ref int numWeapon, int totalWeapons, int depth = 0) {
            var startOffX = 0.5f;
            var startOffY = 0.1f;
            var compress = (totalWeapons / 6) + 1;
            var shiftX = 0.2f / compress;
            var shiftY = 0.35f / compress;

            // 0 => 0
            // 1 => 0
            // 2 => 1
            // 3 => 1
            // 4 => 2
            // 5 => 2

            var x = startOffX + (numWeapon / 2) * shiftX;
            x = 1f - x;

            // 0 => 1
            // 1 => -1
            // 2 => 2
            // 3 => -2
            // 4 => 3
            // 5 => -3
            // 6 => 1
            // 7 => -1

            var yMod = 3;
            var patY = numWeapon / 10;
            if (patY > 0) {
                switch (pattern) {
                    case 0: {
                        yMod -= patY;
                        break;
                    }
                    case 1: {
                        yMod += patY;
                        break;
                    }
                    case 2: {
                        yMod += patY - 1;
                        x -= patY * shiftX;
                        break;
                    }
                    case 3: {
                        yMod += (patY - (numWeapon / 2));
                        x -= patY * shiftX;
                        break;
                    }
                }
            }
            if (yMod < 1) {
                yMod = 10;
            }
            var y = startOffY + ((numWeapon / 2) % yMod) * shiftY;

            y *= Ship.Class.ViewZoneWidthCurve.Evaluate(x);
            if (y < 0.1f && x < 0.1f && x > -0.1f) {
                numWeapon++;
                if (depth < 10) {
                    return NextWeaponZonePos(rng, pattern,
                        ref numWeapon, totalWeapons, depth + 1);
                } else {
                    D.Err("NextWeaponZone went too deep!");
                }
           }

            var sign = numWeapon % 2 == 0 ? 1f : -1f;
            y *= sign;

            if (totalWeapons == 1) {
                y = 0f;
            }

            // D.Warn("W: #{0}. X: {1}. Y: {2}. sig: {3}", numWeapon, x, y, sign);
            var vec = new Vector2(x, y);

            return vec;
        }

        private void Update() {
            float hover = Ship.Class.CombatMotionHover;
            var t = Time.time;
            var hoverSpeed = 1f * Ship.Class.CombatMotionSpeed;
            var hoverAmt = Mathf.Sin(t * hoverSpeed) * hover;
            viewTransform.localPosition = new Vector3(0, hoverAmt, 0);

            var rotateSpeed = 0.7f * Ship.Class.CombatMotionSpeed;
            var rotateFrom = -Ship.Class.CombatMotionRotation;
            var rotateTo = Ship.Class.CombatMotionRotation;
            var rotateAmt = Mathf.Sin(t * rotateSpeed);

            viewTransform.localRotation = Quaternion.Euler(new Vector3(0, 0,
                tiltAngle + Mathf.Lerp(rotateFrom, rotateTo, 0.5f + (rotateAmt * 0.5f))));

            barContainer.transform.rotation = Consts.QId;
            var barWidth = ShipViewBarScale.BarWidth(Ship.CombatState.MaxHP);
            var orthoOffset = ShipViewBarScale.OrthoOffset(IsOnPIPLayer, PIPCamera.Ortho,
                RenderUtils.CameraOrtho, Ship.Width, barWidth);
            barContainer.transform.localScale = Consts.V3One / shipScale * orthoOffset;
            barContainer.transform.position = transform.position + new Vector3(0f, -Ship.Width * 0.5f + hoverAmt, 0f);//new Vector3(0f, -Ship.Width * 0.6f, 0f);
            if (healthBar != null) {
                healthBar.transform.localPosition = Consts.V3Zero; //new Vector3(0f, -10f / shipScale, 0f);
                healthBar.SetWidth(barWidth);
                healthBar.SetProgress(Ship.CombatState.HP / Ship.CombatState.MaxHP);
            }

            if (fluxBar != null && Ship.CombatState.Flux.Capacity > 0) {
                fluxBar.transform.localPosition = new Vector3(0f, -0.7f * orthoOffset / barContainer.transform.lossyScale.x, 0f);
                fluxBar.SetWidth(barWidth);

                // Set progress for hard flux and total flux
                float hardFluxPercent = Ship.CombatState.Flux.Hard / Ship.CombatState.Flux.Capacity;
                float totalFluxPercent = (Ship.CombatState.Flux.Hard + Ship.CombatState.Flux.Soft) / Ship.CombatState.Flux.Capacity;

                fluxBar.SetProgress(totalFluxPercent);
                fluxBar.SetProgress2(hardFluxPercent);
                fluxBar.gameObject.SetActive(true);
            } else if (fluxBar != null) {
                fluxBar.gameObject.SetActive(false);
            }
            if (wantedLayer != currentLayer) {
                currentLayer = wantedLayer;
                RenderUtils.SetLayerRecursively(barContainer, currentLayer);
            }

            ShieldView.CopyTransform(viewTransform);
            ShieldView.Update();
        }

        public void PoolDeactivate() {
            gameObject.SetActive(false);
            ShieldView.Disable(true);
            foreach (var zone in spawnedZones) {
                PrefabPool.Despawn(zone.gameObject);
            }
            spawnedZones.Clear();
            if (marker != null) {
                PrefabPool.Despawn(marker.gameObject);
                marker = null;
            }
            if (healthBar != null) {
                PrefabPool.Despawn(healthBar.gameObject);
                healthBar = null;
            }
            if (fluxBar != null) {
                PrefabPool.Despawn(fluxBar.gameObject);
                fluxBar = null;
            }
            if (isSubscribed) {
                isSubscribed = false;
                The.R?.Sig?.PIPCameraActivated.RemoveListener(OnPIPActivated);
                The.R?.Sig?.PIPSubjectChanged.RemoveListener(OnPIPSubjectChanged);
            }
            Ship = null;
        }

        public void PoolActivate() {
            gameObject.SetActive(true);
        }

        public void JumpCamera() {
            // var ortho = RenderUtils.CameraOrtho;
            D.Warn("Jumping to ship view: {0}", Ship);
            S.Sig.CameraFocus.Send(
                transform.position,
                Consts.V2One,
                Ship.Width * 2f);
        }

        public void ShakeShield(float dur) {
            ShieldView.Shake(dur);
        }
    }
}
