using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Data;
using Game.Systems.Creatures;

namespace Game.Systems.UIUX {
    [Flags]
    public enum EntityActionScope {
        None = 0,
        Being = 1,
        Obj = 2,
        Tile = 4,
        // Being shown in the entity panel without an entity. Its bar is built
        // apart from the selection, so it counts as a type of its own.
        Detached = 8,
    }

    // The bottom bar is built for one entity type at a time, so actions whose
    // scopes do not overlap never have buttons together and may share a hotkey.
    // The scope is derived from the overloads an action overrides instead of
    // being declared: a declared scope that drifts from the overloads would let
    // one key press two buttons, and nothing would report a conflict.
    public static class EntityActionScopes {
        private const BindingFlags Flags = BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic;
        // Protected, nameof cannot reach it from here. A rename makes every scope
        // None, which reports conflicts instead of hiding them.
        private const string ApplyForMany = "ApplyForMany";
        private static readonly Type dataRef = typeof(EntityActionData).MakeByRefType();

        public static EntityActionScope Of(BaseEntityAction action) {
            var type = action.GetType();
            var scope = EntityActionScope.None;
            if (Overrides(type, nameof(BaseEntityAction.ApplyFor), typeof(Being))
                    || Overrides(type, ApplyForMany, typeof(List<Being>))) {
                scope |= EntityActionScope.Being;
            }
            if (Overrides(type, nameof(BaseEntityAction.ApplyFor), typeof(Obj))
                    || Overrides(type, ApplyForMany, typeof(List<Obj>))) {
                scope |= EntityActionScope.Obj;
            }
            if (Overrides(type, nameof(BaseEntityAction.ApplyFor), typeof(Tile))
                    || Overrides(type, ApplyForMany, typeof(List<Tile>))) {
                scope |= EntityActionScope.Tile;
            }
            if (Overrides(type, nameof(BaseEntityAction.ApplyForDetached), typeof(BeingData))) {
                scope |= EntityActionScope.Detached;
            }
            return scope;
        }

        private static bool Overrides(Type type, string name, Type entityArg) {
            var method = type.GetMethod(name, Flags, null,
                new[] { typeof(GameState), entityArg, dataRef }, null);
            return method != null && method.DeclaringType != typeof(BaseEntityAction);
        }
    }
}
