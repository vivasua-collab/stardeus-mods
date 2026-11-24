using Game.Constants;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Systems.Electricity {
    // One ship-wide notification for discharging batteries. Each battery used to post its own,
    // which counted a single condition once per battery in the hub
    internal sealed class BatteryDischargeAlert {
        private readonly B<string> countText = new(null);
        private NotificationHandle handle;
        // Batteries flip between charging and discharging as the load cycles, so once hidden the
        // alert stays away for an hour
        private long lastHiddenTick = -Consts.TicksPerHour;

        public void Update(ElectricitySys sys) {
            var count = CountDischarging(sys);
            if (count > 0) {
                countText.Set(Units.XNum(count));
                if (!handle.IsValid && G.Ticks - lastHiddenTick >= Consts.TicksPerHour) {
                    handle = Notification.Build(IconId.CBatteryFull,
                        T.BatteryInfo_V1.F(T.BatteryStateDischarging), 0, NotificationType.Info)
                        // Sticky: only sticky rows show the count, and it goes away by itself
                        .Sticky()
                        .WithText2B(countText)
                        .WithOnClick(() => {
                            A.S.UIQuery.ToggleElectricity.Ask(true);
                        })
                        .Show();
                }
            } else if (handle.IsValid) {
                lastHiddenTick = G.Ticks;
                Notification.Remove(ref handle);
            }
        }

        // Same rule as BatteryComp.AfterTickGrid uses for Status.Discharging
        private static int CountDischarging(ElectricitySys sys) {
            var nodes = sys.ArrNodes;
            int count = 0;
            for (int i = nodes.Count - 1; i >= 0; i--) {
                if (!nodes.TryGet(i, out var node)) { continue; }
                if (!node.IsInitialized || !node.IsBattery) { continue; }
                if (!node.IsPowerable || !node.IsProducing || node.IsConsuming) { continue; }
                if (sys.TryGetGrid(node.GridId, out var grid) && grid.HasShipComputer) {
                    count++;
                }
            }
            return count;
        }
    }
}
