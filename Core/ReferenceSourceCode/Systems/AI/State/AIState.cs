#if !PRODUCTION_BUILD
#define DEBUG_AI_STATE
#define VALIDATE_AI_STATE
// #define DEBUG_STATE_RESIZE
#endif
using System;
using System.Runtime.CompilerServices;
using Game.CodeGen;
using Game.Data;
using KL.Utils;

namespace Game.Systems.AI {
    public struct AIState {
        public AIVar[] Vars;
        public byte VarCount;
        private byte VarsSize;
        public const byte InitVarSize = 2;
        public static AIState Create() {
            return new AIState() {
                Vars = new AIVar[InitVarSize],
                VarsSize = InitVarSize,
            };
        }
        public bool Get(int key, bool def) {
            var vars = Vars;
            var count = VarCount;
            for (int i = 0; i < count; i++) {
                ref AIVar v = ref vars[i];
                if (v.Key == key) {
                    return v.Bool;
                }
            }
            return def;
        }
        public bool Try(int key, out bool val) {
            var vars = Vars;
            var count = VarCount;
            for (int i = 0; i < count; i++) {
                ref AIVar v = ref vars[i];
                if (v.Key == key) {
                    val = v.Bool;
                    return true;
                }
            }
            val = false;
            return false;
        }
        public static AIState With(int key, bool val) {
            var state = new AIState() {
                Vars = new AIVar[InitVarSize]
            };
            state.Set(key, val);
            return state;
        }
        public void Append(AIVar var) {
            #if DEBUG_AI_STATE
            var vars = Vars;
            var count = VarCount;
            for (int i = 0; i < count; i++) {
                ref AIVar v = ref vars[i];
                if (v.Key == var.Key) {
                    D.Err("Trying to append a var to AIState, while this var is already here! {0} -> {1}",
                        var, this);
                    return;
                }
            }
            #endif
            #if DEBUG_STATE_RESIZE
            EnsureCanAddNewVar(var.Key, var.Bool);
            #else
            EnsureCanAddNewVar();
            #endif
            Vars[VarCount++] = var;
            #if VALIDATE_AI_STATE
            Validate();
            #endif

        }
        public void Set(AIVar var) {
            var vars = Vars;
            var count = VarCount;
            var varKey = var.Key;
            for (int i = 0; i < count; i++) {
                ref AIVar v = ref vars[i];
                if (v.Key == varKey) {
                    v.Bool = var.Bool;
                    return;
                }
            }
            #if DEBUG_STATE_RESIZE
            EnsureCanAddNewVar(var.Key, var.Bool);
            #else
            EnsureCanAddNewVar();
            #endif
            Vars[VarCount++] = var;
        }

        public void Set(int key, bool val) {
            var vars = Vars;
            var count = VarCount;
            for (int i = 0; i < count; i++) {
                ref AIVar v = ref vars[i];
                if (v.Key == key) {
                    v.Bool = val;
                    return;
                }
            }
            #if DEBUG_STATE_RESIZE
            EnsureCanAddNewVar(key, val);
            #else
            EnsureCanAddNewVar();
            #endif
            Vars[VarCount++] = AIVar.Create(key, val);
            #if VALIDATE_AI_STATE
            Validate();
            #endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCanAddNewVar(int key, bool val) {
            EnsureSize((byte) (VarCount + 1), key, val);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCanAddNewVar() {
            EnsureSize((byte) (VarCount + 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureSize(byte size, int key, bool val) {
            if (VarsSize < size) {
                Array.Resize(ref Vars, size);
                VarsSize = size;
                #if DEBUG_STATE_RESIZE
                D.ErrSoft("Resizing AIState.Vars to size {0}! Will add: {1}->{2} to {3}",
                    size, AIVarsH.ResolveName(key), val, this);
                #endif
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureSize(byte size) {
            if (VarsSize < size) {
                Array.Resize(ref Vars, size);
                VarsSize = size;
                #if DEBUG_STATE_RESIZE
                D.ErrSoft("Resizing AIState.Vars to size {0}! {1}", size, this);
                #endif
                #if VALIDATE_AI_STATE
                Validate();
                #endif
            }
        }

        public AIState And(int key, bool val) {
            Set(key, val);
            return this;
        }

        // Tells is this state is fully mirrored by the other state.
        // Other state can have more variables, but we only care
        // about those that are in this state
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsFulfilledIn(AIState other) {
            var vc = VarCount;
            if (vc == 0) { return false; }
            var ovc = other.VarCount;
            if (ovc == 0) { return false; }
            // Early exit: if we need more vars than other has, can't be fulfilled
            if (vc > ovc) { return false; }

            // O(N^2) - unavoidable without sorted data or dictionary
            var vars = Vars;
            var ov = other.Vars;
            for (int i = 0; i < vc; i++) {
                ref AIVar v = ref vars[i];
                bool found = false;
                var vKey = v.Key;
                var vBool = v.Bool;
                for (int j = 0; j < ovc; j++) {
                    ref AIVar v2 = ref ov[j];
                    if (v2.Key == vKey) {
                        // Inline the equality check for speed
                        if (vBool != v2.Bool) {
                            return false;
                        }
                        found = true;
                        break;
                    }
                }
                if (!found) {
                    return false;
                }
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEqualTo(AIState other) {
            // Fast path: different counts means not equal
            if (VarCount != other.VarCount) { return false; }
            if (VarCount == 0) { return true; }

            // Cache array references and count
            var thisVars = Vars;
            var otherVars = other.Vars;
            if (thisVars == null || otherVars == null) { return false; }
            var count = VarCount;

            // O(N²) but with optimizations
            for (int j = 0; j < count; j++) {
                ref AIVar v2 = ref otherVars[j];
                bool found = false;
                var v2Key = v2.Key;
                var v2Bool = v2.Bool;
                for (int i = 0; i < count; i++) {
                    ref AIVar v = ref thisVars[i];
                    if (v.Key == v2Key) {
                        // Inline comparison instead of calling found = v.Bool == v2.Bool
                        if (v.Bool != v2Bool) {
                            return false;
                        }
                        found = true;
                        break;
                    }
                }
                if (!found) {
                    return false;
                }
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AIState Subtract(AIState state) {
            var vc = VarCount;
            var stateVC = state.VarCount;

            if (vc == 0 || stateVC == 0 || Vars == null || state.Vars == null) {
                // nothing to remove from, or nothing to remove
                return state;
            }

            var subState = new AIState();
            // Allocate only what we might need (worst case is vc)
            byte newVarsSize = vc;
            subState.Vars = new AIVar[newVarsSize];
            subState.VarsSize = newVarsSize;
            byte newVarCount = 0;

            var vars = Vars;
            var stateVars = state.Vars;

            // remove all vars that weren't found
            for (int j = 0; j < vc; j++) {
                ref AIVar v = ref vars[j];
                bool addKey = true;
                // Cache key for inner loop comparison
                var vKey = v.Key;
                for (int i = 0; i < stateVC; i++) {
                    if (stateVars[i].Key == vKey) {
                        addKey = false;
                        break;
                    }
                }
                if (addKey) {
                    subState.Vars[newVarCount++] = v;
                }
            }
            subState.VarCount = newVarCount;
            #if VALIDATE_AI_STATE
            subState.Validate();
            #endif
            return subState;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AIState MergeWith(AIState state) {
            var vc = VarCount;
            var stateVC = state.VarCount;

            if (vc == 0) {
                // nothing to add
                return state;
            }
            if (stateVC == 0) {
                return this;
            }
            var appendedState = Duplicate();
            var appendedVars = appendedState.Vars;
            var stateVars = state.Vars;
            for (int j = 0; j < stateVC; j++) {
                ref AIVar v2 = ref stateVars[j];
                bool found = false;
                var v2Key = v2.Key;
                for (int i = 0; i < vc; i++) {
                    ref AIVar v = ref appendedVars[i];
                    if (v.Key == v2Key) {
                        v.Bool = v2.Bool;
                        found = true;
                        break;
                    }
                }
                if (!found) {
                    appendedState.Append(v2);
                }
            }
            #if VALIDATE_AI_STATE
            appendedState.Validate();
            #endif
            return appendedState;
        }

        public static bool IsNotEmpty(AIState state) {
            return state.VarCount > 0 && state.Vars != null;
        }

        public static AIState Load(AIVar[] vars, byte cnt) {
            var s = new AIState();
            s.Vars = vars;
            s.VarCount = cnt;
            if (vars != null) {
                s.VarsSize = (byte) vars.Length;
            }
            s.Validate();
            return s;
        }

        public static (AIVar[], byte) Save(AIState state) {
            // state.Validate();
            if (state.Vars == null) {
                return (null, 0);
            }
            return (state.Vars, state.VarCount);
        }

        public AIState Duplicate() {
            var copy = new AIState();
            if (Vars != null) {
                copy.Vars = new AIVar[VarsSize];
                copy.VarsSize = VarsSize;
                if (VarCount > 0) {
                    Array.Copy(Vars, copy.Vars, VarCount);
                }
            } else {
                copy.Vars = new AIVar[InitVarSize];
                copy.VarsSize = InitVarSize;
            }
            copy.VarCount = VarCount;
            return copy;
        }

        public void CopyTo(ref AIState target) {
            var count = VarCount;
            target.EnsureSize(count);
            var vars = Vars;
            var targetVars = target.Vars;
            for (int i = 0; i < count; i++) {
                targetVars[i] = vars[i];
            }
            target.VarCount = count;
        }

        public void InvertCopyTo(ref AIState target) {
            var count = VarCount;
            target.EnsureSize(count);
            var vars = Vars;
            var targetVars = target.Vars;
            for (int i = 0; i < count; i++) {
                var v = vars[i];
                v.Bool = !v.Bool;
                targetVars[i] = v;
            }
            target.VarCount = count;
        }

        public void Validate() {
            if (Vars != null) {
                if (Vars.Length != VarsSize) {
                    D.Err("Vars.Length({0}) does not match VarsSize({1})",
                        Vars.Length, VarsSize);
                }
            }
            var count = VarCount;
            var size = VarsSize;
            if (count > size) {
                D.Err("VarCount({0}) is greater than VarsSize({1})! {2}",
                    count, size, this);
            }
            if (count > 0 && (Vars == null || Vars.Length == 0)) {
                D.Err("VarCount({0}) is not zero but Vars is empty: {1}",
                    count, this);
            }
            var vars = Vars;
            for (int i = 0; i < count; i++) {
                if (!vars[i].IsValid) {
                    D.Err("Invalid var in state! {0}", this);
                }
            }
        }

        public string ShortString => Vars == null ? "Blank" : string.Join(";", Vars);
        public override string ToString() {
            if (Vars == null) {
                return "AIState[Invalid]";
            }
            #if DEBUG_AI_STATE
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append("AIState[");
            sb.Append(VarCount);
            sb.Append('|');
            if (Vars != null) {
                for (int i = 0; i < VarCount; i++) {
                    sb.Append(Vars[i]);
                    if (i < VarCount - 1) {
                        sb.Append(',');
                    }
                }
            }
            sb.Append(']');
            return sb.ToString();
            #else
            return $"AIState[{VarCount}]";
            #endif
        }
    }
}
