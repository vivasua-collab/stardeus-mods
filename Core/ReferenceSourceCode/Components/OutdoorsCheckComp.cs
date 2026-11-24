using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using System.Text;
using Game.Systems.Notify;

namespace Game.Components {
    public sealed class OutdoorsCheckComp : BaseComponent<OutdoorsCheckComp>,
            IRareTickListener, IRelocatable, IDefDescriptionProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new OutdoorsCheckComp());
        }

        private NotificationHandle warningNot;

        public override string ToString() {
            return " * Outdoors Check";
        }

        private bool isOutdoors;
        public bool IsOutdoors {
            get => isOutdoors;
            set => isOutdoors = value;
        }
        private bool isShowingWarning;
        private bool mustBeOutdoors;
        public bool MustBeOutdoors => mustBeOutdoors;

        protected override void OnConfig() {
            mustBeOutdoors = !Config.GetBool(PropertyIdH.MustBeIndoors, false);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded) {
                UpdateOutdoorsState(G.Ticks);
            }
        }

        public void RareTick(long currentTick) {
            // If there is active construction somewhere, we may want to use
            // stale data instead of just returning
            // We will only check for this for the first time
            UpdateOutdoorsState(currentTick);
        }

        public void UpdateOutdoorsState(long tick) {
            if (!Ready.AreasInitial) {
                // fallback temporarily
                isOutdoors = mustBeOutdoors;
                //D.Err("Initial areas not ready for safety rare tick!");
                return;
            }
            var wasInWantedZone = IsOutdoors == mustBeOutdoors;
            var pos = Entity.PosIdx;
            if (pos < 1) {
                D.Err("Uninitialized POS during RareTick: {0}", Entity);
            }
            IsOutdoors = !S.Sys.Areas.IsEnclosed(pos);
            // A blueprint or a relocation ghost does not run yet, and the build tool
            // already warns while placing it. Its own notification doubled the one of
            // the device being relocated
            if (!Tile.IsConstructed) {
                RemoveWarning();
                return;
            }
            if (IsOutdoors == mustBeOutdoors) {
                if (!wasInWantedZone) {
                    RemoveWarning();
                }
                return;
            }
            if (isShowingWarning) {  return; }
            ShowInfoIcon(IconId.CWarning, Texts.LinkToOverlay(mustBeOutdoors ?
                T.MustBeOutdoors : T.MustBeIndoors, OverlayId.Areas));
            isShowingWarning = true;
            if (!warningNot.IsValid) {
                if (mustBeOutdoors) {
                    warningNot = Notification.BuildFor(Tile, null, T.MustBeOutdoors,
                        0, NotificationType.Warning)
                        .WithTooltip(T.MustBeOutdoorsTip)
                        .Group("out_chk", icon: IconId.CWarning)
                        .Show();
                } else {
                    warningNot = Notification.BuildFor(Tile, null, T.MustBeIndoors,
                        0, NotificationType.Warning)
                        .WithTooltip(T.MustBeIndoorsTip)
                        .Group("in_chk", icon: IconId.CWarning)
                        .Show();
                }
            }
        }

        private void RemoveWarning() {
            HideInfoIcon();
            if (!isShowingWarning) { return; }
            Notification.Remove(ref warningNot);
            isShowingWarning = false;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<OutdoorsCheckComp>(out var sc)) {
                RareTick(G.Ticks);
            }
        }
        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            var outdoors = def.ComponentConfigFor(ComponentIdH.OutdoorsCheck, false);
            if (outdoors != null) {
                Def.PrependAdditionalInfo(sb, addedAnything);
                addedAnything = true;
                if (outdoors.GetBool(PropertyIdH.MustBeIndoors, false)) {
                    sb.Append(T.MustBeIndoors);
                } else {
                    sb.Append(T.MustBeOutdoors);
                }
            }
            return addedAnything;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }
    }
}
