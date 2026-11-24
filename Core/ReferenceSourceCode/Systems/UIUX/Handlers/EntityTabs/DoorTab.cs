using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Path;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DoorTab : BaseEntityTab {
        public const string TabId = "Door";

        [SelfInit]
        private static void Register() {
            RegisterTab(new DoorTab());
        }

        private Tile tile;
        private DoorComp door;

        public override string TabName => T.Door;

        public override int Order => -5;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) {
                return false;
            }
            if (tile.ENode?.IsReachable == false) {
                return false;
            }
            if (!tile.TryGetComponent(out door)) {
                return false;
            }
            this.tile = tile;
            return true;
        }


        public override bool LoadContent(WGroup group) {
            door.LockedB.Bind(lifetime, (l) => ReloadTab(), false);

            var block = UISpawn.Block(lifetime, group, 0);
            var idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounPermissions);

            if (door.ArePermissionsUnlocked) {
                var isLocked = door.IsLocked;

                // Child rows (see NavPermissions.ParentOf) are indented under their
                // parent; DisplayOrder guarantees children directly follow it.
                WIndentWrapper indent = null;
                var indentParent = NavPermission.None;
                var indentIdx = 0;
                var order = NavPermissions.DisplayOrder;
                for (int i = 0; i < order.Length; i++) {
                    var perm = order[i];
                    var parent = NavPermissions.ParentOf(perm);
                    WToggleText permBtn;
                    if (parent == NavPermission.None) {
                        permBtn = UISpawn.Widget<WToggleText>(lifetime,
                            UIPrefabId.WContent2Toggle, block.Container, idx++);
                    } else {
                        if (indent == null || indentParent != parent) {
                            indent = UISpawn.Widget<WIndentWrapper>(lifetime,
                                UIPrefabId.WIndentWrapper, block.Container, idx++);
                            indent.SetIndent(40);
                            indentParent = parent;
                            indentIdx = 0;
                        }
                        permBtn = UISpawn.Widget<WToggleText>(lifetime,
                            UIPrefabId.WContent2Toggle, indent.Container, indentIdx++);
                    }
                    SpawnPermToggle(permBtn, perm, isLocked);
                }
            } else {
                var locked = UISpawn.Widget<WButtonIconText2>(lifetime, UIPrefabId.WContent2Button, block.Container, idx++);
                locked.Load(IconId.CLocked, T.ResearchNodeLocked, null, () => {
                    var def = The.Defs.FastGet(DefIdsH.ResearchSecurityDoorPermissions);
                    A.S.UIQuery.ShowInResearchTree.Ask(def);
                });
            }

            return true;
        }

        private void SpawnPermToggle(WToggleText permBtn, NavPermission perm, bool isLocked) {
            var editable = IsEditable(perm);
            var permName = PathNode.NameForPerm(perm);
            if (isLocked) {
                permName = Texts.Grey(permName);
            } else if (!editable) {
                permName = Texts.Grey(Texts.Strike(permName));
            }
            permBtn.Load(permName,
                !isLocked && IsAllowed(perm),
                (v) => {
                    SetAllowed(perm, v);
                    if (v) {
                        // Self-heal: allowing a parent re-enables its children so
                        // they show ticked and interactable again.
                        SetChildrenAllowed(perm);
                    }
                    if (NavPermissions.ParentOf(perm) != NavPermission.None || HasChildren(perm)) {
                        ReloadTab();
                    }
                });
            if (isLocked) {
                permBtn.SetInteractable(false);
                UIUtils.AddTip(lifetime, permBtn,
                    $"{tile.Definition.NameT}: {T.InfoDoorLocked}");
            } else if (!editable) {
                permBtn.SetInteractable(false);
            }
        }

        private void SetAllowed(NavPermission perm, bool allow) {
            var isDenied = (door.Deny & perm) == perm;
            if (isDenied == allow) {
                door.TogglePermission(perm);
            }
        }

        private void SetChildrenAllowed(NavPermission parent) {
            var order = NavPermissions.DisplayOrder;
            for (int i = 0; i < order.Length; i++) {
                if (NavPermissions.ParentOf(order[i]) == parent) {
                    SetAllowed(order[i], true);
                }
            }
        }

        private static bool HasChildren(NavPermission parent) {
            var order = NavPermissions.DisplayOrder;
            for (int i = 0; i < order.Length; i++) {
                if (NavPermissions.ParentOf(order[i]) == parent) {
                    return true;
                }
            }
            return false;
        }

        // Child rows are display-derived: a being carrying a child bit always
        // carries the parent bit too, so denying the parent already blocks the
        // child regardless of its own bit.
        // See Docs/Architecture/Systems/DoorPermissions.md.
        private bool IsAllowed(NavPermission perm) {
            var covered = NavPermissions.ParentOf(perm) | perm;
            return (door.Deny & covered) == NavPermission.None;
        }

        private bool IsEditable(NavPermission perm) {
            return (door.Deny & NavPermissions.ParentOf(perm)) == NavPermission.None;
        }

        protected override void Cleanup() {
            tile = null;
            door = null;
        }

        public override string Id => TabId;
    }
}
