using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using UnityEngine;
using KL.Utils;
using Game.Rendering;

namespace Game.Visuals {
    public sealed class StarfieldVFX {
        private readonly GameState S;
        private readonly VFXHolder starfield;
        private VFXHolder nebulas;
        private readonly VFXHolder dust;
        private readonly uint menuSeed;
        private SpaceLooks looks;
        private const string nebulaType = "StarfieldNebulaOptimized";

        private void CreateNebulaVFX() {
            // D.FIXME_LATER("Check if OSX can show nebula now");
            var nebulaOn = The.Prefs.GetBool(
                Pref.GNebulaOn, PrefH.GNebulaOn, Consts.DefaultNebulaOn);
            if (!nebulaOn || RenderUtils.DisableAllVFX) {
                if (nebulas != null) {
                    nebulas.Destroy();
                    nebulas = null;
                }
                return;
            }
            if (nebulas == null) {
                int nebulaCount = Consts.IsLessAwesomeGPU ? 10 : 20;
                //D.Warn("Creating nebula: {0} / {1}", nebulaType, nebulaCount);
                nebulas = new VFXHolder(nebulaType);
                if (!nebulas.IsSpawned) {
                    nebulas.Destroy();
                    nebulas = null;
                    return;
                }
                nebulas.VFX.SetInt(ShaderVars.NebulaCount, nebulaCount);
            }
            if (S != null) {
                nebulas.VFX.startSeed = (uint) S.Seed;
            } else {
                nebulas.VFX.startSeed = menuSeed;
            }
            Texture2D nebulaMain;
            Texture2D nebulaDetail;
            if (Images.TrySprite(GraphicsId.NebulasNebulaMain01, out var spr)) {
                nebulaMain = spr.texture;
                // Looks bad in pixel mode
                nebulaMain.filterMode = FilterMode.Bilinear;
                nebulas.VFX.SetTexture(ShaderVars.NebulaMainTex, nebulaMain);
            } else {
                CrashWatchdog.UpdateBoot("Could not get sprite: {0}",
                    GraphicsId.NebulasNebulaMain01);
            }
            if (Images.TrySprite(GraphicsId.NebulasNebulaDetail01, out var spr2)) {
                nebulaDetail = spr2.texture;
                // Looks bad in pixel mode
                nebulaDetail.filterMode = FilterMode.Bilinear;
                nebulas.VFX.SetTexture(ShaderVars.NebulaDetailTex, nebulaDetail);
            } else {
                CrashWatchdog.UpdateBoot("Could not get sprite: {0}",
                    GraphicsId.NebulasNebulaDetail01);
            }
            nebulas.VFX.resetSeedOnPlay = false;
            nebulas.VFX.gameObject.layer = LayerMask.NameToLayer("Starfield");
        }

        public StarfieldVFX(GameState s) {
            S = s;
            menuSeed = Rng.UUint;
            starfield = new VFXHolder("StarfieldNoNebula");
            if (starfield.IsSpawned) {
                starfield.VFX.gameObject.layer = LayerMask.NameToLayer("Starfield");
            }
            CreateNebulaVFX();
            if (starfield.IsSpawned) {
                starfield.VFX.resetSeedOnPlay = false;
                starfield.VFX.startSeed = S != null ? (uint) S.Seed : menuSeed;
            }
            if (S != null) {
                S.Sig.CamOrthoSizeChanged.AddListener(SetCameraZoom);
                S.Sig.OnMoveCamera.AddListener(SetCameraPos);
            }
            dust = new VFXHolder("Dust");
            if (dust.IsSpawned) {
                dust.VFX.gameObject.layer = LayerMask.NameToLayer("Dust");
            }
            The.SysSig.SetSpaceLooks.AddListener(SetLooks);
            The.SysSig.ResolutionChanged.AddListener(RebuildNebulas);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            RebuildNebulas();
            if (S == null) {
                MoveTo(Rng.UInsideUnitCircle().normalized * 20f);
                // No game behind the menu, nothing would reset a sky left by the last one
                SpaceSky.Reset();
            }
        }

        public void Reboot(bool nebulaOn) {
            #if UNITY_STANDALONE_OSX
            // FIXME check if Unity has fixed this
            // return;
            #endif
            dust.Stop();
            dust.Show();
            starfield.Stop();
            starfield.Show();
            if (nebulaOn && nebulas != null) {
                nebulas.Stop();
                nebulas.Show();
            }
        }

        public void Destroy() {
            The.SysSig.ResolutionChanged.RemoveListener(RebuildNebulas);
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
            The.SysSig.SetSpaceLooks.RemoveListener(SetLooks);
            starfield.Destroy();
            nebulas?.Destroy();
            dust.Destroy();
        }

        public void SetLooks(SpaceLooks looks) {
            this.looks = looks;
            nebulas?.VFX?.SetVector3(ShaderVars.ColorShift1, looks.ColorShift1);
            nebulas?.VFX?.SetVector3(ShaderVars.ColorShift2, looks.ColorShift2);
            //D.Warn("Setting starfield looks with color shift: {0} and {1}",
                //looks.ColorShift1, looks.ColorShift2);
            /*
            // rebuild was required for older version of static cached nebula
            var nebulaOn = The.Prefs.GetBool(Pref.GNebulaOn,
                PrefH.GNebulaOn, Consts.DefaultNebulaOn);
            if (nebulaOn) {
                RebuildNebulas();
            }
            */
        }

        public void SetDustMultiplier(float v) {
            dust.VFX?.SetFloat(ShaderVars.DensityMultiplier, v);
        }
        public void SetCameraPos(Vector3 unadjustedPos, Vector3 pos) {
            // D.Warn("Setting camera pos: {0}", pos);
            starfield.VFX?.SetVector3(ShaderVars.CamPosition, pos);
            dust.VFX?.SetVector3(ShaderVars.CamPosition, pos);
            nebulas?.VFX?.SetVector3(ShaderVars.CamPosition, pos);
        }

        public void SetCameraZoom(float zoom) {
            starfield.VFX?.SetFloat(ShaderVars.CamZoom, zoom);
            dust.VFX?.SetFloat(ShaderVars.CamZoom, zoom);
            nebulas?.VFX?.SetFloat(ShaderVars.CamZoom, zoom);
        }

        public void MoveTo(Vector2 pos) {
            // If you ever see the starfield moving in a wrong direction diagonally
            // it is 99% due to float precision loss with high numbers
            //D.Warn("Moving starfield to pos: {0}", pos);
            starfield.VFX?.SetVector2(ShaderVars.WorldPos, pos);
            dust.VFX?.SetVector2(ShaderVars.WorldPos, pos);
            nebulas?.VFX?.SetVector2(ShaderVars.WorldPos, pos);
        }

        private const float shipFTLmul = 100f;

        public void SetShipVector(Vector2 shipForward, float speed) {
            if (speed < 0.001f) {
                ClearShipVector();
            } else {
                if (speed > 1f) {
                    speed *= speed;
                }
                starfield.VFX?.SetVector3(ShaderVars.ShipVector, new Vector3(
                    shipForward.x, shipForward.y, speed * shipFTLmul));
            }
        }

        private void OnSettingChanged(string setting) {
            if (setting == Pref.GNebulaOn) {
                CreateNebulaVFX();
                RebuildNebulas();
            }
            SpaceSky.OnSettingChanged(setting);
        }

        private void RebuildNebulas() {
            var nebulaOn = The.Prefs.GetBool(Pref.GNebulaOn, PrefH.GNebulaOn,
                Consts.DefaultNebulaOn);
            Reboot(nebulaOn);
        }

        public void ClearShipVector() {
            starfield.VFX?.SetVector3(ShaderVars.ShipVector, Consts.V3Zero);
        }
    }
}
