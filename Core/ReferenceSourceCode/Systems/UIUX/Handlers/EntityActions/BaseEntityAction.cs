using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public abstract class BaseEntityAction : IResettable {
        protected static void RegisterAction(BaseEntityAction action) {
            Registries.EntityActions.Add(action.Id, action);
        }
        public abstract string Id { get; }
        // Row label in input settings. Null = the action cannot be bound to a key.
        // The hotkey presses the action's bottom bar button (EntityActionHotkeys),
        // so only actions with one button per selection should opt in.
        public virtual string HotkeyLabel => null;
        // Actions sharing a HotkeyId share one binding, and the first of their
        // buttons in the bar gets pressed (draft / undraft, lock / unlock)
        public virtual string HotkeyId => Id;
        public virtual string HotkeyDefault => null;
        // Bound after HotkeyDefault: saved overrides are stored by binding index
        public virtual string HotkeyDefaultGamepad => null;
        protected virtual bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }
        protected virtual bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }
        protected virtual bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }
        protected virtual bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }
        protected virtual bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }
        protected virtual bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }
        // For beings shown in the entity panel without a Being entity (expedition, stored).
        // Separate from ApplyFor(Being) so entity actions never have to null check the entity.
        public virtual bool ApplyForDetached(GameState s, BeingData being, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }
        public bool ApplyFor(GameState s, List<Being> beings, out EntityActionData data) {
            if (beings.Count != 1) {
                return ApplyForMany(s, beings, out data);
            }
            return ApplyFor(s, beings[0], out data);
        }
        public bool ApplyFor(GameState s, List<Obj> objs, out EntityActionData data) {
            if (objs.Count != 1) {
                return ApplyForMany(s, objs, out data);
            }
            return ApplyFor(s, objs[0], out data);
        }
        public bool ApplyFor(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (tiles.Count != 1) {
                return ApplyForMany(s, tiles, out data);
            }
            return ApplyFor(s, EntityUtils.ParentObj(tiles[0]), out data);
        }
        private WBottomBarButton _barButtonPrefab;
        protected WBottomBarButton barButtonPrefab => _barButtonPrefab
            ??= ResourceLibrary.FindPrefab<WBottomBarButton>(UIPrefabId.WBottomBarButton);
        private WBottomBarButton _barButtonDisabledPrefab;
        protected WBottomBarButton barButtonDisabledPrefab => _barButtonDisabledPrefab
            ??= ResourceLibrary.FindPrefab<WBottomBarButton>(UIPrefabId.WBottomBarButton_Disabled);

        private WBottomBarButton _barButtonMultiPrefab;
        protected WBottomBarButton barButtonMultiPrefab => _barButtonMultiPrefab
            ??= ResourceLibrary.FindPrefab<WBottomBarButton>(UIPrefabId.WBottomBarButton_Multi);

        protected WBottomBarButton SpawnButton(Lifetime lifetime,
                bool applicable, RectTransform parent, int siblingIdx) {
            var widget = UISpawn.Widget(lifetime, applicable ?
                barButtonPrefab : barButtonDisabledPrefab, parent, siblingIdx);
            return widget;
        }
        protected WBottomBarButton SpawnButtonMulti(Lifetime lifetime,
                bool applicable, RectTransform parent, int siblingIdx) {
            var widget = UISpawn.Widget(lifetime, applicable ?
                barButtonMultiPrefab : barButtonDisabledPrefab, parent, siblingIdx);
            return widget;
        }
        public void OnReset() {
            Unload();
        }

        protected virtual void Unload() {}
        public static bool ValidateLimit(int count, string actionName, bool dryRun) {
            if (count > Tunables.MassActionMaxTiles) {
                if (!dryRun) {
                    A.S.Sig.ShowErrorMessage.Send(
                        $"{T.OperationForbidden}: {actionName} ({T.Selected}: {count}, {T.Max}: {Tunables.MassActionMaxTiles})");
                }
                return false;
            }
            return true;
        }

    }
}
