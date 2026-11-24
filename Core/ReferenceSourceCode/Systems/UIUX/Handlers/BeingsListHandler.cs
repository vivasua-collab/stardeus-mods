using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class BeingsListHandler : BaseWidgetHandler<BeingsListWidget> {
        public BeingsListHandler(GameState s, BeingsListWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.TrySelectBeingAvatars.SetResponder(TrySelectAvatars);
            s.UIQuery.GetBeingAvatar.SetResponder(GetAvatar);
            s.UISig.PreviewBeingAvatarSelection.AddListener(OnPreviewSelection);
            s.Sig.EveryFrame.AddListener(EveryFrame);
        }

        private bool isPreviewing;

        // Mirrors FreeSelectBeingAvatarHandler.OnClickUp: any hit replaces the whole
        // selection with the hit avatars, no hit leaves the selection alone
        private void OnPreviewSelection(Rect rect) {
            bool anyHit = false;
            foreach (var item in widget.SpawnedAvatars) {
                if (UIUtils.HasOverlap(rect, item.Value.Rect)) {
                    anyHit = true;
                    break;
                }
            }
            if (!anyHit) {
                ClearPreview();
                return;
            }
            isPreviewing = true;
            foreach (var item in widget.SpawnedAvatars) {
                var avatar = item.Value;
                avatar.SetDragPreview(avatar.Being != null
                    && UIUtils.HasOverlap(rect, avatar.Rect));
            }
        }

        // Drags can end without the avatar handler's OnClickUp (Esc, RMB, tool switch),
        // but every ending clears G.IsClickDrag
        private void EveryFrame() {
            if (isPreviewing && !G.IsClickDrag) {
                ClearPreview();
            }
        }

        private void ClearPreview() {
            if (!isPreviewing) { return; }
            isPreviewing = false;
            foreach (var item in widget.SpawnedAvatars) {
                item.Value.ClearDragPreview();
            }
        }

        private bool TrySelectAvatars(Rect rect, List<BeingData> result) {
            bool selected = false;
            foreach (var item in widget.SpawnedAvatars) {
                if (UIUtils.HasOverlap(rect, item.Value.Rect)) {
                    result.Add(item.Key);
                    selected = true;
                }
            }
            return selected;
        }

        // Hidden avatars, compact groups and ship combat mode have none showing, and a
        // deferred rebuild can leave despawned (inactive) ones in the map for a frame
        private RectTransform GetAvatar(BeingData data) {
            if (widget.SpawnedAvatars.TryGetValue(data, out var avatar)
                    && avatar.gameObject.activeInHierarchy) {
                return avatar.Rect;
            }
            return null;
        }
    }
}
