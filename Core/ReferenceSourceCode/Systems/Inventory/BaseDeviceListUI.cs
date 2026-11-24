using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Signals;
using KL.Utils;
using UnityEngine;
using Game.Systems.Notify;
using Game.Systems.UIUX;

namespace Game.Systems.Inventory {
    public abstract class  BaseDeviceListUI<T> : INotificationUIProvider where T : IWithEntity {
        public Entity Entity => null;
        protected readonly GameState S;
        protected NotificationHandle ev;
        private bool isShowingEvent;
        protected readonly string eventName;
        protected readonly string eventIcon;
        protected readonly string headerIcon;
        protected virtual NotificationType NotificationType => NotificationType.Warning;
        protected readonly B<string> count = new(null);
        protected virtual bool AllowUnconstructed => false;
        protected readonly HashSet<T> devices = new();

        private bool needsUpdate;
        private WContentListButton listWidget;
        private Lifetime childLife;

        public BaseDeviceListUI(GameState S, Signal2<T, bool> signal,
                string eventName, string eventIcon, string headerIcon) {
            this.S = S;
            this.eventName = eventName;
            this.eventIcon = eventIcon;
            this.headerIcon = headerIcon;
            signal.AddListener(OnChange);
        }

        public void OnTick() {
            foreach (var dev in devices) {
                if (!dev.Entity.IsActive) {
                    devices.Remove(dev);
                    needsUpdate = true;
                    listWidget?.RebuildContainer();
                    break;
                }
            }
        }

        protected abstract string Tooltip();

        private void OnChange(T device, bool isListed) {
            if (isListed) {
                devices.Add(device);
            } else {
                devices.Remove(device);
            }
            needsUpdate = true;
            listWidget?.RebuildContainer();
        }

        public string GetName() {
            return "DeviceList";
        }


        protected abstract IOrderedEnumerable<T> OrderedDevices();

        private void CreateEvent() {
            if (isShowingEvent) {
                D.Err("Trying to show event more than once!");
                return;
            }
            isShowingEvent = true;
            ev = Notification.Build(eventIcon, eventName, 0, NotificationType)
                .Sticky()
                .WithText2B(count)
                .WithUIProvider(this)
                .Show();
        }

        public void Update() {
            if (!needsUpdate) { return; }
            needsUpdate = false;
            int cnt = 0;
            foreach (var dev in devices) {
                if (dev.Entity is Tile t) {
                    if (!t.IsActive) {
                        continue;
                    }
                    if (!t.IsConstructed && !AllowUnconstructed) {
                        continue;
                    }
                    cnt++;
                }
            }
            // var cnt = devices.Count;
            if (cnt > 0) {
                if (!isShowingEvent) {
                    CreateEvent();
                }
                count.Set(Units.Num(cnt));
            } else if (isShowingEvent) {
                RemoveEvent();
            }
        }

        private void RemoveEvent() {
            isShowingEvent = false;
            listWidget = null;
            childLife = null;
            Notification.Remove(ref ev);
        }

        public WBase SpawnNotificationUI(Lifetime life, NotificationData data,
                RectTransform container, int idx) {
            listWidget = UISpawn.Widget<WContentListButton>(life,
                UIPrefabId.WContent2ListButton, container, idx);
            listWidget.Load(eventIcon, eventName, count.Value);
            listWidget.LoadOnToggle(life, OnToggleDeviceList);
            count.Bind(life, text => listWidget?.SetText2(text));
            UIUtils.AddTipFunc(life, listWidget.PopupArea, BuildTooltip);
            life.OnDispose(() => {
                listWidget = null;
                childLife = null;
            });
            return listWidget;
        }

        private bool OnToggleDeviceList(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded) {
            if (childLife != null) {
                childLife.Dispose();
                childLife = null;
            }
            if (!willShow) { return wasLoaded; }
            childLife = life.CreateChild("DeviceListItems");
            PopulateDeviceList(childLife, container);
            return true;
        }

        protected virtual void PopulateDeviceList(Lifetime life,
                RectTransform container) {
            string lastCategory = null;
            int childIdx = 0;
            foreach (var device in OrderedDevices()) {
                if (device.Entity is Tile t) {
                    if (!t.IsActive) { continue; }
                    if (!t.IsConstructed && !AllowUnconstructed) { continue; }
                }
                var category = GetCategory(device);
                if (lastCategory != category) {
                    var header = UISpawn.Widget<WText>(life,
                        UIPrefabId.WContent2Header, container, childIdx++);
                    header.Load(category);
                    lastCategory = category;
                }
                SpawnDeviceWidget(life, device, container, ref childIdx);
            }
        }

        protected List<T> FilterActiveDevices() {
            var result = new List<T>();
            foreach (var device in OrderedDevices()) {
                if (device.Entity is Tile t) {
                    if (!t.IsActive) { continue; }
                    if (!t.IsConstructed && !AllowUnconstructed) { continue; }
                }
                result.Add(device);
            }
            return result;
        }

        protected abstract string GetCategory(T device);
        protected abstract void SpawnDeviceWidget(Lifetime life, T device,
            RectTransform container, ref int idx);
        protected abstract string BuildTooltip();
    }
}
