using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Utils;

namespace Game.Systems.Inventory {
    public sealed class FullStorageUI {
        // Hysteresis: routine take/store churn of a few units must not make the
        // notification disappear and reappear right after the space is consumed again
        private const int HideAtFreeSpace = 100;
        private readonly GameState S;
        private NotificationHandle fullNotif;

        public FullStorageUI(GameState s) {
            S = s;
            s.Sig.FailedFindingStorageFor.AddListener(OnFailStoring);
        }

        private void OnFailStoring(Entity ntt) {
            var msg2 = $"{Texts.LinkToEntity(ntt)}: {Texts.Red(T.StorageUnavailable)}";
            S.Sys.Log.AddLine(msg2);
        }

        public void Check(List<IMatStorage> commonStorages) {
            int capacity = 0;
            int free = 0;
            for (int i = commonStorages.Count - 1; i >= 0; i--) {
                if (commonStorages[i] is not MatStorageComp ms) { continue; }
                if (ms.StorageType == StorageType.TakeOnly) { continue; }
                capacity += ms.MaxCapacity;
                free += Math.Max(0, ms.AvailableCapacity);
            }
            if (capacity == 0) {
                Notification.Remove(ref fullNotif);
                return;
            }
            if (!fullNotif.IsValid) {
                if (free <= 0) {
                    fullNotif = Notification.Build(
                        GraphicsId.ObjectsDevicesStorageMini_D, T.MatStoragesFull, 0,
                        NotificationType.Warning).Show();
                    S.Sig.ShowFanfare.Send(T.MatStoragesFull);
                }
            } else if (free >= HideAtFreeSpace) {
                Notification.Remove(ref fullNotif);
            }
        }

        public void Reset() {
            fullNotif = default;
        }
    }
}
