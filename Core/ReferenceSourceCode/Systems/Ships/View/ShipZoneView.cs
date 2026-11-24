using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems.Turrets;
using Game.UI.InGame;
using Game.Utils;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Systems.Ships {
    /// <summary>
    /// For displaying ship combat loadout, weapons, shields, etc
    /// </summary>
    public sealed class ShipZoneView : MonoBehaviour, ITurretTarget,
            IPointerEnterHandler,
            IPointerExitHandler,
            ICustomPoolable {
        private static int pipCameraLayer;
        private static int gameCameraLayer;
        [SerializeField] private SpriteRenderer spriteRend;
        [SerializeField] private SpriteRenderer statusSpriteRend;
        [SerializeField] private Transform progressBarTr;
        [SerializeField] private SphereCollider uiCollider;
        private Transform statusTr;
        private Transform tr;
        public Vector2 Size => new Vector2(9f, 9f);
        public static Vector2 JumpSize = new Vector2(120f, 120f);
        public NPCShip Ship;
        public ShipDevice Device;
        public IShipDevice ShipDev;
        private float spriteScale;
        public string DebugInfo;
        public Vector2 Position => tr.position;
        public Vector2 LocalPos;
        private float parentUnscale;
        private InGameProgressBar healthBar;
        private InGameProgressBar chargeBar;
        private bool isSubscribed;
        private int wantedLayer;
        private int currentLayer;

        private void Start() {
            LinkTransforms();
        }

        private void LinkTransforms() {
            tr = transform;
            statusTr = statusSpriteRend.transform;
        }

        public void SetShipSize(float parentScale) {
            if (parentScale != 0f) {
                parentUnscale = 1f / parentScale;
            } else {
                D.Err("Parent scale cannot be 0!");
                parentUnscale = 1f;
            }
        }

        public void SetDestroyed(bool destroyed) {
            if (destroyed) {
                statusSpriteRend.sprite = Images.Sprite(IconId.CCross);
                statusSpriteRend.enabled = true;
            } else {
                statusSpriteRend.enabled = false;
            }
        }

        public void Tick() {
            var phase = ShipDev.PhasePercent;
            if (float.IsNaN(phase)) {
                D.Err("Phase percent is NaN! Phase: {0}, Device: {1}",
                    ShipDev.PhaseName, ShipDev);
                phase = 0f;
            }
            chargeBar.SetProgress(phase);
        }

        public void Load(ShipDevice device, Facing.Type dir, float rotAngle) {
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
            Device = device;
            SetDestroyed(device.Health <= 0f);
            var sprite = device.DeviceDef.ZoneSprites[(int) dir];
            spriteRend.sprite = sprite;
            if (device.DeviceDef.ZoneSpriteScale > 0f) {
                spriteScale = device.DeviceDef.ZoneSpriteScale;
            } else {
                spriteScale = 1f;
            }
            if (healthBar == null) {
                var barPrefab = ResourceLibrary.FindPrefab<InGameProgressBar>();
                healthBar = PrefabPool.Spawn(barPrefab, Consts.V3Zero,
                    Consts.QId, progressBarTr);
                healthBar.transform.localPosition = Vector3.zero;
                healthBar.SetWidth(2.56f);
            }
            healthBar.transform.localRotation = Consts.QId;
            healthBar.SetFillColor(Consts.ColorHealthBar);


            if (chargeBar == null) {
                var barPrefab = ResourceLibrary.FindPrefab<InGameProgressBar>();
                chargeBar = PrefabPool.Spawn(barPrefab, Consts.V3Zero,
                    Consts.QId, progressBarTr);
                chargeBar.SetWidth(2.56f);
            }
            chargeBar.transform.localRotation = Consts.QId;
            chargeBar.SetFillColor(Consts.ColorBatteryBar);

            UpdateHealthBar();
            // need to unrotate the sprite by rotAngle of the zone container
            transform.localRotation = Quaternion.Euler(0f, 0f, -rotAngle);
            chargeBar.transform.localPosition = new Vector3(0f, -0.35f / spriteScale, 0f);
            uiCollider.radius = 1f / spriteScale;
            uiCollider.center = new Vector3(0, 0.5f / spriteScale, 0f);
            chargeBar.Transform.localScale = Consts.V3One * 0.5f / spriteScale;
            healthBar.Transform.localScale = Consts.V3One * 0.5f / spriteScale;
            chargeBar.SetFillColor(Colors.WithA(Consts.ColorBatteryBar, 0.9f));
            healthBar.SetFillColor(Colors.WithA(Consts.ColorHealthBar, 0.9f));
            chargeBar.SetBackColor(new Color(0f, 0f, 0f, 0.5f));
            healthBar.SetBackColor(new Color(0f, 0f, 0f, 0.5f));
            if (tr == null) {
                LinkTransforms();
            }
            statusTr.localScale = Consts.V3One * 0.5f / spriteScale;
            tr.localPosition = LocalPos * Ship.Width * 0.85f * parentUnscale;// * Mathf.Sin(Time.time);
            // to not have progress bars collide into each other
            tr.localPosition += new Vector3(0f, 0f, Rng.URange(-0.1f, -2f));
        }

        private void Update() {
            // to test cross sprite
            // SetDestroyed(true);
            statusTr.rotation = Consts.QId;
            // Sized for whichever camera actually draws this zone, not for whether the
            // ship happens to be on screen - the two are not the same thing.
            var onPip = IsOnPIPLayer;
            var scale = (onPip ? PIPCamera.Ortho : RenderUtils.CameraOrtho) * 0.1f;
            var max = Ship.Class.Visual_DeviceMaxScale;
            if (onPip) {
                max = 5f;
            } else {
                max = 2.5f;
            }
            var min = 0.2f;
            // D.Warn("Scale: {0}", scale);
            if (scale < min) {
                scale = min;
            }
            if (scale > max) {
                scale = max;
            }
            progressBarTr.rotation = Consts.QId;
            progressBarTr.position = tr.position + new Vector3(0f, -0.5f * scale, 0f);
            var sc = Consts.V3One * scale * parentUnscale;// * spriteUnits;
            tr.localScale = sc * spriteScale;
            if (wantedLayer != currentLayer) {
                currentLayer = wantedLayer;
                RenderUtils.SetLayerRecursively(tr, currentLayer);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            The.SysSig.ShowDynamicTooltip.Send(GetTooltip, true);
        }

        private string GetTooltip() {
            var sb = Caches.StringBuilder;
            sb.Clear();

            sb.Append(ShipDev.FullTooltip);
            sb.AppendLine(T.HealthBarInfo_V2.F((int)Device.Health, Device.HealthMax));
            if (ShipDev is ShipWeapon weapon) {
                if (weapon.WeaponDef.AmmoCapacity > 0) {
                    sb.AppendFormat("\n{0}: {1}", T.Ammo, Units.XOutOfY(weapon.Ammo,
                        weapon.WeaponDef.AmmoCapacity));
                }
            }
            if (DebugInfo != null) {
                sb.AppendFormat("Debug: {0}\n\n", DebugInfo);
            }
            Ship.AppendTooltip(sb, false);
            var tip = sb.ToString();
            return tip;
        }

        public void OnPointerExit(PointerEventData eventData) {
            The.SysSig.HideTooltip.Send();
        }

        public void PoolDeactivate() {
            if (isSubscribed) {
                isSubscribed = false;
                The.R?.Sig?.PIPCameraActivated.RemoveListener(OnPIPActivated);
                The.R?.Sig?.PIPSubjectChanged.RemoveListener(OnPIPSubjectChanged);
            }
            DespawnBars();
            gameObject.SetActive(false);
        }

        private void OnPIPActivated(PIPCamera pip, bool active) {
            UpdatePIPLayer();
        }

        private void OnPIPSubjectChanged(NPCShip subject) {
            UpdatePIPLayer();
        }

        private bool IsOnPIPLayer => PIPCamera.IsActive && PIPCamera.Subject == Ship;

        private void UpdatePIPLayer() {
            wantedLayer = IsOnPIPLayer ? pipCameraLayer : gameCameraLayer;
        }

        private void DespawnBars() {
            if (healthBar != null) {
                PrefabPool.Despawn(healthBar.gameObject);
                healthBar = null;
            }
            if (chargeBar != null) {
                PrefabPool.Despawn(chargeBar.gameObject);
                chargeBar = null;
            }
        }

        public void PoolActivate() {
            gameObject.SetActive(true);
        }

        public void UpdateHealthBar() {
            healthBar?.SetProgress(Device?.HealthPercent ?? 0f);
        }
    }
}
