using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Ships;
using Game.UI;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Input {
    /// <summary>
    /// Picture-in-picture camera handler. Mostly for ship to ship combat.
    /// </summary>
    public sealed class PIPCamera : MonoBehaviour, ICustomPoolable {
        [SerializeField] private Camera mainCam;
        [SerializeField] private Camera gameCam;
        [SerializeField] private Camera starfieldCam;
        public RenderTexture Texture;

        private Vector3 gameCamPos;
        private Vector3 gameCamOffset;
        private float gameCamOrtho;
        public static float Ortho;
        public static bool IsActive;
        public static PIPCamera Current;
        /// <summary>
        /// The ship the inset is showing. Only its view belongs on the PIP layer -
        /// every other ship has to stay visible on the game camera.
        /// </summary>
        public static NPCShip Subject;

        public void SetActive(GameState s, bool active) {
            if (active == IsActive) { return; }
            IsActive = active;
            s?.Sig?.PIPCameraActivated.Send(this, active);
        }

        public void SetSubject(GameState s, NPCShip subject) {
            if (subject == Subject) { return; }
            Subject = subject;
            s?.Sig?.PIPSubjectChanged.Send(subject);
        }

        private void Awake() {
            if (Current != null && Current != this) {
                D.Err("Two PIP cameras are not allowed!");
                DestroyImmediate(gameObject);
            }
        }

        public void MoveGameCam(Vector3 pos, float ortho) {
            gameCamPos = pos;
            gameCamOrtho = ortho;
            Ortho = ortho;
            UpdateGameCamPos();
        }
        public void MoveStarfieldCam(Vector3 position) {
            var tr = RenderUtils.StarfieldCamera.transform;
            starfieldCam.transform.position = tr.position + position;
            starfieldCam.transform.rotation = tr.rotation;
        }

        private void SetupRT() {
            if (Texture == null) {
                Texture = RenderTexture.GetTemporary(1024, 1024, 16,
                    RenderTextureFormat.Default);
                mainCam.targetTexture = Texture;
                IsActive = true;
                Current = this;
            }
        }

        private void UpdateGameCamPos() {
            gameCam.transform.localPosition = gameCamPos + gameCamOffset;
            gameCam.orthographicSize = gameCamOrtho;
        }

        public void Cleanup()  {
            mainCam.targetTexture = null;
            if (Texture != null) {
                if (IsActive) {
                    SetActive(The.R?.State, false);
                }
                RenderTexture.ReleaseTemporary(Texture);
                Texture = null;
                Current = null;
            }
            Subject = null;
        }

        public void SetAspectRatio(float v) {
            SetupRT();
            mainCam.aspect = v;
            gameCam.aspect = v;
            starfieldCam.aspect = v;
        }

        public void PoolDeactivate() {
            gameObject.SetActive(false);
            Cleanup();
        }

        public void PoolActivate() {
            gameObject.SetActive(true);
        }

    }
}
