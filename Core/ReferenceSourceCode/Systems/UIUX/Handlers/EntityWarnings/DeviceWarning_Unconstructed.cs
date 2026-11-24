using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Signals;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_Unconstructed : BaseEntityWarning {
        public const string WarningId = "Device_Unconstructed";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_Unconstructed());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private Tile tile;
        private GameState S;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = true;
            if (tile.Reinstallable != null) {
                if (tile.Reinstallable.IsPending || tile.Reinstallable.IsMidRelocation) {
                    return false;
                }
            }
            if (tile.Constructable.TryGetGoal(out var goal)) {
                if (goal.JobIdH == JobTypeIdH.Demolition) { return false; }
            } else {
                return false;
            }
            this.tile = tile;
            S = s;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CHammer, T.InfoConstructionInProgress, ShowQuickChoice);
            ShowTimeStopped(life, warning, S.Clock);
        }

        // "Construction in progress" is not true while time is paused, and the player who has
        // not noticed the pause is exactly the one looking at this line, wondering why nobody
        // comes (see PauseNeglect)
        private static void ShowTimeStopped(Lifetime life, WButtonIconText2 warning,
                Clock clock) {
            Signal1<ClockSpeed>.SignalListener onSpeed = (speed) => {
                warning.SetText2(speed == ClockSpeed.Stopped
                    ? Texts.Accented(T.TimeStopped) : null);
            };
            onSpeed(clock.Speed);
            // The widget is pooled, so the listener goes with the lifetime, not its GameObject
            clock.OnSpeedChange.AddListener(onSpeed);
            life.OnDispose(() => clock.OnSpeedChange.RemoveListener(onSpeed));
            UIUtils.AddTipFunc(life, warning,
                () => clock.IsStopped ? T.NoTasksRasonClockStopped : null);
        }
        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(T.CancelTask, () => AIGoal.Cancel(tile.Goal, G.Ticks, T.AdRejCancelled));
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }


        public override void Unload() {
            tile = null;
            S = null;
        }
    }
}
