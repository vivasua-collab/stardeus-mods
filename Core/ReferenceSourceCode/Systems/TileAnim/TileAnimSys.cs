using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.RangeCheck;
using Game.Systems.Ships;
using Game.Systems.TileAnim;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Turrets {
    public sealed class TileAnimSys : GameSystem {
        public const string SysId = "TileAnim";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new TileAnimSys());
        }

        private TileAnimGraphics graphics;
        private readonly List<TileAnimation> anims = new();
        private readonly Dictionary<int, TileAnimation> animsById = new();
        private bool isAnimDisabled;


        protected override void OnInitialize() {
            graphics = new TileAnimGraphics(S);
            S.Sig.AfterLoadState.AddListener(OnLoad);
            S.Query.RegisterTileAnim.SetResponder(RegisterTileAnim);
            S.Sig.WinchEnded.AddListener(OnWinchEnd);
            S.Sig.ShipRotated.AddListener(AfterShipRotate);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            CheckSystemEnabled();
        }

        private readonly List<TileAnimationComp> rotatedComps = new();
        private void AfterShipRotate() {
            // Every comp, not just the registered anims: one turned out of a skipped facing
            // has no anim registered. Ship rotation is rare, so the full walk is cheap
            rotatedComps.Clear();
            S.Components.FindAll(rotatedComps);
            foreach (var comp in rotatedComps) {
                comp.AfterShipRotate();
            }
            rotatedComps.Clear();
        }

        private void OnWinchEnd(bool moved) {
            if (!moved) { return; }
            graphics.ResetAnims();
        }

        private void OnSettingChanged(string setting) {
            if (setting == Pref.GNoDeviceAnim) {
                CheckSystemEnabled();
            }
        }

        private void CheckSystemEnabled() {
            var disabled = The.Prefs.GetBool(Pref.GNoDeviceAnim,
                PrefH.GNoDeviceAnim, false);
            isAnimDisabled = disabled;
        }

        private void OnLoad(GameState state) {
            S.Clock.OnTickProgress.AddListener(OnTickProgress);
            S.Sig.ShipMoved.AddListener(OnShipMoved);
        }

        private void OnShipMoved(int x, int y) {
            graphics.ResetAnims();
        }

        private bool RegisterTileAnim(TileAnimation anim, bool add) {
            if (isAnimDisabled) {
                return true;
            }
            if (add) {
                if (animsById.TryAdd(anim.AnimatedTile.Id, anim)) {
                    graphics.PrepareFor(anim);
                    anims.Add(anim);
                } else {
                    D.Err("Turret already added! {0}", anim);
                }
            } else {
                if (!animsById.Remove(anim.AnimatedTile.Id)) {
                    D.Err("Failed to remove turret by id: {0}: {1}",
                        anim.AnimatedTile.Id, anim);
                }
                anims.Remove(anim);
                graphics.UnloadFor(anim);
            }
            return true;
        }


        private void OnTickProgress(float prog) {
            if (isAnimDisabled) { return; }
            // animations are made with 60 FPS as a reference
            for (int i = anims.Count - 1; i >= 0; i--) {
                var anim = anims[i];
                if (!anim.IsAnimating) { continue; }
                if (anim.NeedsEnergy) {
                    var enode = anim.AnimatedTile.ENode;
                    if (!enode.IsConsuming && !enode.IsProducing) {
                        continue;
                    }
                }
                float before = anim.CurrentFrame;
                anim.CurrentFrame += anim.Speed * G.AnimSpeedMul * GameTimer.FPSRate;
                if (anim.CurrentFrame < 0f) {
                    anim.CurrentFrame += anim.NumFramesActual;
                }
                anim.CurrentFrame %= anim.NumFramesActual;
            }
            graphics.OnTickProgress(prog);
        }

        public override void Unload() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
        }

        public string GetName() {
            return "TurretsSys";
        }

        private readonly Dictionary<int, RangeCheckHit> closestHits = new();

        private readonly List<RotatingTurret> activeShipTurrets = new();

    }
}
