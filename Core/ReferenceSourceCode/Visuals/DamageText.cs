using UnityEngine;
using TMPro;
using Game.Rendering;
using KL.Utils;
using KL.Pool;
using Game.Constants;
using Game.Utils;
using Game.Systems.Ships;

namespace Game.Visuals {
    public class DamageText : MonoBehaviour {
        [SelfInit]
        private static void Cleanup() {
            active = 0;
            activeShip = 0;
        }
        [SerializeField] private TextMeshPro textMeshPro;
        private float elapsedTime = 0f;
        private static int active;
        private static int activeShip;
        private const int limit = 100;
        private const int limitShip = 100;
        private float fontSize;
        private float floatSpeed;
        private bool forOurShip;
        private static int pipCameraLayer;
        private static int gameCameraLayer;

        public static void Show(Vector2 position, int damage, float fontSize,
                float floatSpeed, bool forEnemyShip) {
            if (!Ready.Gen) { return; }
            if (forEnemyShip) {
                if (activeShip >= limitShip) { return; }
            } else {
                if (active >= limit) { return; }
            }
            if (pipCameraLayer == 0) {
                pipCameraLayer = LayerMask.NameToLayer("PIP");
                gameCameraLayer = LayerMask.NameToLayer("ToolsAbove");
            }
            var prefab = ResourceLibrary.FindPrefab<DamageText>();
            var dmg = PrefabPool.Spawn(prefab, Vectors.SetZ(position, -10),
                Consts.QId, ResourceLibrary.Root);
            dmg.textMeshPro.SetText(Units.Num(damage));
            dmg.floatSpeed = floatSpeed;
            dmg.transform.localScale = Consts.V3One;
            dmg.fontSize = fontSize;
            if (forEnemyShip) {
                dmg.gameObject.layer = pipCameraLayer;
                dmg.forOurShip = false;
                activeShip++;
            } else {
                dmg.gameObject.layer = gameCameraLayer;
                dmg.forOurShip = true;
                active++;
            }
            #if UNITY_EDITOR
            dmg.gameObject.name = $"DamageText_{forEnemyShip}_{damage}";
            #endif
            dmg.textMeshPro.fontSize = fontSize;
            dmg.elapsedTime = 0f;
            // D.Warn("Showing damage: {0} at {1}", damage, position);
        }

        private void Update() {
            // Float the text upwards
            transform.position += new Vector3(0f, floatSpeed * GameTimer.deltaTime, 0f);

            // Fade out the text
            elapsedTime += GameTimer.deltaTime;
            var fadeDuration = 2f;
            float alpha = Mathf.Lerp(1f, 0.5f, elapsedTime / fadeDuration);
            textMeshPro.color = Colors.WithA(Consts.ColorChartRed, alpha);

            // Destroy the object after fadeDuration
            if (elapsedTime >= fadeDuration) {
                PrefabPool.Despawn(gameObject);
            }
        }

        private void OnDisable() {
            if (forOurShip) {
                active--;
                if (active < 0) {
                    active = 0;
                    D.Err("Active went below 0!");
                }
            } else {
                activeShip--;
                if (activeShip < 0) {
                    activeShip = 0;
                    D.Err("ActiveShip went below 0!");
                }
            }
        }

        public static void ShowShipHit(NPCShip ship, Vector2 pos, int dmg) {
            // for pip view
            var sw = ship.Width;
            Show(pos, dmg, sw * 1.5f, sw * 0.05f, true);
            // for game camera
            if (sw > 80) {
                sw = 80;
            }
            Show(pos, dmg, sw * 1.5f, sw * 0.05f, false);
        }
    }
}
