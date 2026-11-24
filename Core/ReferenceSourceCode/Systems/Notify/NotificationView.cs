using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEditor;
using UnityEngine;

namespace Game.Systems.Notify {
    public class NotificationView : INotificationUIProvider {
        public NotificationData First;
        public List<NotificationData> List;
        public string Title {
            get {
                if (List == null) {
                    return First.Title ?? First.TitleB?.Value ?? "???";
                }
                return First.GroupTitle ?? First.Title ?? First.TitleB?.Value ?? "???";
            }
        }
        private Lifetime childLife;
        private BInt groupCountB;
        public void AddData(NotificationData data) {
            if (First == null) {
                First = data;
                return;
            }
            if (List == null) {
                List = new List<NotificationData>(4);
                List.Add(First);
            }
            List.Add(data);
        }

        public void SpawnNotificationUI(Lifetime life, WContentBlock block, int idx) {
            if (List != null) {
                // need to spawn a group widget first
                var groupWidget = UISpawn.Widget<WContentListButton>(life,
                    UIPrefabId.WContent2ListButton, block.Container, idx);
                var icon = First.GroupIcon ?? First.Icon;
                var title = First.GroupTitle ?? First.Title;
                groupCountB = new BInt(List.Count);
                groupWidget.Load(icon, title, null);
                groupCountB.Bind(life, (cnt) => {
                    groupWidget.SetText2(Units.XNum(cnt));
                });
                if (icon == null) {
                    groupWidget.Button.SetSprite(First.Sprite);
                }
                groupWidget.LoadOnToggle(life, OnToggleGroup);
            } else {
                // single notification
                var uiProvider = First.UIProvider ?? this;
                First.SetWidget(life,
                    uiProvider.SpawnNotificationUI(life, First,
                        block.Container, idx));
            }
        }

        private bool OnToggleGroup(Lifetime life, RectTransform c,
                bool willShow, bool wasLoaded) {
            if (childLife != null) {
                childLife.Dispose();
                childLife = null;
            }
            if (!willShow) { return wasLoaded; }
            childLife = life.CreateChild("NotificationGroup");
            var uiProvider = First.UIProvider ?? this;
            var idx = 0;
            foreach (var item in List) {
                if (item.Flags.HasFlag(NotificationFlags.Removed)) { continue; }
                item.SetWidget(childLife,
                    uiProvider.SpawnNotificationUI(childLife, item, c, idx++));
            }
            var closeAllBtn = UISpawn.Widget<WButtonText>(childLife, UIPrefabId.WContent2ButtonSimple, c, idx++);
            closeAllBtn.Load(Texts.Italic(T.ClearAll), () => {
                for (int i = List.Count - 1; i >= 0; i--) {
                    NotificationData item = List[i];
                    item.Remove(i == 0);
                }
            });
            return true;
        }

        public WBase SpawnNotificationUI(Lifetime life, NotificationData data,
                RectTransform container, int idx) {

            if ((data.Flags & NotificationFlags.Sticky) == NotificationFlags.Sticky) {
                var w = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx);
                w.Load(data.Icon, data.Title, data.Text2, () => OnClick(data));
                if (data.Sprite != null) {
                    w.SetSprite(data.Sprite);
                }
                if (data.TitleB != null) {
                    data.TitleB.Bind(life, t => w.SetText(t));
                }
                if (data.Text2B != null) {
                    data.Text2B.Bind(life, t => w.SetText2(t));
                }
                AddTooltip(life, data, w);
                return w;
            } else {
                // closeable notification
                var w = UISpawn.Widget<WContentButtonWithX>(life, UIPrefabId.WContent2ButtonX, container, idx);
                w.Button.Load(data.Icon, data.Title, () => OnClick(data));
                if (data.Sprite != null) {
                    w.Button.SetSprite(data.Sprite);
                }
                if (data.TitleB != null) {
                    data.TitleB.Bind(life, t => w.Button.SetText(t));
                }
                // closeable notification doesn't support text2
                w.XButton.Load(() => {
                    if (List?.Count > 1) {
                        data.Remove(false);
                        w.Dispose();
                        List.Remove(data);
                        groupCountB.Decrement();
                    } else {
                        // will rebuild
                        data.Remove(true);
                    }
                });
                AddTooltip(life, data, w);
                return w;
            }
        }

        public static void AddTooltip(Lifetime life, NotificationData data, WBase w) {
            Tooltip tip = null;
            if (data.TooltipFunc != null) {
                tip = UIUtils.AddTipFunc(life, w, data.TooltipFunc);
            } else if (data.Tooltip != null) {
                tip = UIUtils.AddTip(life, w, data.Tooltip);
            }
            if (data.OnHover != null) {
                if (tip == null) {
                    UIUtils.AddHover(life, w, data.OnHover);
                } else {
                    tip.HoverFun = data.OnHover;
                }
            }
        }

        public static void OnClick(NotificationData data) {
            if (data.OnClick != null) {
                data.OnClick();
                return;
            }
            if (data.Being != null) {
                if (data.Being.Being != null) {
                    EntityUtils.SelectOrFocus(data.Being.Being);
                } else {
                    A.S.UIQuery.ShowBeingPanel.Ask(data.Being);
                }
            } else if (data.Obj != null) {
                if (data.Obj.Obj != null && data.Obj.Obj.IsActive) {
                    EntityUtils.SelectOrFocus(data.Obj.Obj);
                } else {
                    if (data.Obj.IsStorable(out _)) {
                        A.S.UIQuery.ShowObjPanel.Ask(data.Obj);
                    } else {
                        data.Remove(true);
                    }
                }
            } else if (data.Tile != null && data.Tile.IsActive) {
                EntityUtils.SelectOrFocus(data.Tile);
            } else {
                if (data.Flags.HasFlag(NotificationFlags.Sticky)) {
                    // do nothing, let user close it manually
                    D.Err("Click function not available for notification: {0}", data);
                } else {
                    data.Remove(true);
                }
            }
        }

        public override string ToString() {
            return $"NotificationView: First={First}, ListCount={List?.Count}";
        }
    }
}
