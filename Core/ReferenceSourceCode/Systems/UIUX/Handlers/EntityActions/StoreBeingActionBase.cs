using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public abstract class StoreBeingActionBase : BaseEntityAction {
        protected abstract bool AcceptsBiological { get; }
        // Same binding as StoreObjAction / StorePileAction: one verb, one key
        public override string HotkeyLabel => T.ActionStore;
        public override string HotkeyId => "Store";
        public override string HotkeyDefault => "<Keyboard>/h";

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!CanStore(s, entity, out var storable, out var storageDef)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, entity, storageDef),
                Priority.Normal);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings,
                out EntityActionData data) {
            if (!CountStorable(s, beings, out var cnt, out var storageDef)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Being>(beings), cnt, storageDef),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i,
                GameState s, Being being, Def storageDef) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(storageDef.Preview,
                T.VerbSendTo_V1.F(storageDef.NameT),
                () => ShowStoreChoice(s, new List<Being> { being }));
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                GameState s, List<Being> beings, int cnt, Def storageDef) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, storageDef.Preview, T.VerbSendTo_V1.F(storageDef.NameT),
                () => ShowStoreChoice(s, beings));
            return widget;
        }

        private static void ShowStoreChoice(GameState s, List<Being> beings) {
            var storages = s.Query.AllBeingStorages.Ask();
            if (Arrays.IsEmpty(storages)) {
                return;
            }

            var orderable = new Dictionary<Def, List<StorableBeingComp>>();
            foreach (var being in beings) {
                if (!being.IsActive || !being.TryGetComponent<StorableBeingComp>(out var sb)) {
                    continue;
                }
                if (sb.HasAnyGoal) {
                    continue;
                }
                sb.CheckCompatibleStorages(storages, (storage, storable) => {
                    var def = storage.Entity.Definition;
                    if (!orderable.TryGetValue(def, out var list)) {
                        list = new List<StorableBeingComp>();
                        orderable[def] = list;
                    }
                    if (!list.Contains(storable)) {
                        list.Add(storable);
                    }
                });
            }

            if (orderable.Count == 0) {
                return;
            }

            if (orderable.Count == 1) {
                foreach (var kv in orderable) {
                    var list = kv.Value;
                    CountByType(list, out var dead, out var incap, out var alive);
                    if (alive == 0) {
                        OrderAll(list, null);
                        A.S.UIQuery.RebuildEntityActions.Ask();
                    } else {
                        ShowTypeChoice(list, dead, incap, alive);
                    }
                    return;
                }
            }

            var qc = QuickChoice.Create(T.Select);
            foreach (var kv in orderable) {
                var def = kv.Key;
                var list = kv.Value;
                qc.AddButton($"{def.NameT} ({Units.XNum(list.Count)})", () => {
                    CountByType(list, out var dead, out var incap, out var alive);
                    if (alive == 0) {
                        OrderAll(list, null);
                        A.S.UIQuery.RebuildEntityActions.Ask();
                    } else {
                        ShowTypeChoice(list, dead, incap, alive);
                    }
                });
            }
            qc.Show();
        }

        private static void ShowTypeChoice(List<StorableBeingComp> list,
                int dead, int incap, int alive) {
            var qc = QuickChoice.Create(T.Select);
            if (alive > 0) {
                qc.AddButton($"{T.BeingStateAlive} ({Units.XNum(alive)})", () => {
                    OrderAll(list, sb =>
                        sb.Being.IsAlive && !sb.Being.Data.Body.IsIncapacitated);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            }
            if (incap > 0) {
                qc.AddButton($"{T.BeingStateIncapacitated} ({Units.XNum(incap)})", () => {
                    OrderAll(list, sb =>
                        sb.Being.IsAlive && sb.Being.Data.Body.IsIncapacitated);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            }
            if (dead > 0) {
                qc.AddButton($"{T.Dead} ({Units.XNum(dead)})", () => {
                    OrderAll(list, sb => sb.Being.IsDead);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            }
            if (incap > 0 && dead > 0) {
                qc.AddButton(
                    $"{T.Dead} + {T.BeingStateIncapacitated} ({Units.XNum(dead + incap)})", () => {
                    OrderAll(list, sb =>
                        sb.Being.IsDead || sb.Being.Data.Body.IsIncapacitated);
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            }
            qc.Show();
        }

        private static void OrderAll(List<StorableBeingComp> list,
                System.Func<StorableBeingComp, bool> filter) {
            foreach (var sb in list) {
                if (filter == null || filter(sb)) {
                    sb.TryOrderStoring(true, true);
                }
            }
        }

        private static void CountByType(List<StorableBeingComp> list,
                out int dead, out int incap, out int alive) {
            dead = 0;
            incap = 0;
            alive = 0;
            foreach (var sb in list) {
                if (sb.Being.IsAlive) {
                    if (sb.Being.Data.Body.IsIncapacitated) {
                        incap++;
                    } else {
                        alive++;
                    }
                } else {
                    dead++;
                }
            }
        }

        private bool CountStorable(GameState s, List<Being> beings,
                out int cnt, out Def storageDef) {
            var storages = s.Query.AllBeingStorages.Ask();
            storageDef = null;
            if (Arrays.IsEmpty(storages)) {
                cnt = 0;
                return false;
            }
            int c = 0;
            foreach (var being in beings) {
                if (CanStore(s, being, out _, out var def)) {
                    c++;
                    if (storageDef == null) {
                        storageDef = def;
                    }
                }
            }
            cnt = c;
            return cnt > 0;
        }

        private bool CanStore(GameState s, Being being,
                out StorableBeingComp storable, out Def storageDef) {
            storable = null;
            storageDef = null;
            if (!being.IsActive || !being.TryGetComponent(out storable)) {
                return false;
            }
            if (being.Data.Species.IsBiological != AcceptsBiological) {
                return false;
            }
            if (storable.HasAnyGoal) {
                return false;
            }
            if (!storable.CanBeStoredNow) {
                return false;
            }
            var storages = s.Query.AllBeingStorages.Ask();
            if (Arrays.IsEmpty(storages)) {
                return false;
            }
            bool found = false;
            Def foundDef = null;
            storable.CheckCompatibleStorages(storages, (storage, _) => {
                if (!found) {
                    foundDef = storage.Entity.Definition;
                    found = true;
                }
            });
            storageDef = foundDef;
            return found;
        }
    }
}
