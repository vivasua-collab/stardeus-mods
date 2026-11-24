using System;

namespace Game.Systems.Path {
    [Flags] public enum NavPermission : ushort {
        None = 0,
        Walking = 1,
        Flying = 2,
        Robot = 4,
        Biological = 8,
        Humanoid = 16,
        Animal = 32,
        Friendly = 64,
        Enemy = 128,
        // Nested under Friendly - every guest also carries Friendly.
        // See NavPermissions.ParentOf.
        Guest = 256,
        // future: Prisoner = 512 (top-level, denied by default)
    }

    public static class NavPermissions {
        // Deny everyone regardless of which flags exist, so adding a flag never
        // silently leaves someone allowed (RegSys impassable door regions).
        public const NavPermission All = (NavPermission) ushort.MaxValue;

        // UI / text order. Children follow their parent. Not Enum.GetValues:
        // that orders by bit value and would put Guest after Enemy.
        public static readonly NavPermission[] DisplayOrder = {
            NavPermission.Walking,
            NavPermission.Flying,
            NavPermission.Robot,
            NavPermission.Biological,
            NavPermission.Humanoid,
            NavPermission.Animal,
            NavPermission.Friendly,
            NavPermission.Guest,
            NavPermission.Enemy,
        };

        // A being carrying the child bit always carries the parent bit too
        // (MotionControl.UpdateNavPermissions / UpdatePermissions), so denying
        // the parent already denies the child.
        public static NavPermission ParentOf(NavPermission perm) {
            switch (perm) {
                case NavPermission.Humanoid:
                case NavPermission.Animal:
                    return NavPermission.Biological;
                case NavPermission.Guest:
                    return NavPermission.Friendly;
                default:
                    return NavPermission.None;
            }
        }
    }
}
