using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.Rendering;

namespace Game.Systems.Creatures {
    // Drives colonist eye blinking. Actual sprite-swap + timing state lives on
    // BeingLooksComp (see CheckBlink) - this just gates the per-tick scan behind a single
    // shared camera-zoom check and iterates active humanoid beings.
    public sealed class BeingBlinkSys : GameSystem {
        public const string SysId = "BeingBlinkSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new BeingBlinkSys());
        }

        private BeingsSys beingsSys;

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
        }

        private void OnLoadState(GameState state) {
            beingsSys = state.Sys.Get<BeingsSys>(BeingsSys.SysId);
            state.Clock.OnTick.AddListener(OnTick);
        }

        private void OnTick(long ticks) {
            if (RenderUtils.CameraOrtho > Tunables.BeingBlinkZoomCutoff) { return; }
            var beings = beingsSys.ActiveBeings;
            for (int i = beings.Count - 1; i >= 0; i--) {
                var bd = beings[i];
                if (!bd.Species.IsHumanoid) { continue; }
                bd.Being?.Looks?.CheckBlink();
            }
        }

        public override void Unload() {
        }
    }
}
