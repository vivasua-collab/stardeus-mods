#if !PRODUCTION_BUILD
// #define ALWAYS_SHOW
#endif
using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Conditions;
using Game.Systems.Firex;
using Game.Systems.Story;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using Game.UI;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Single sandbox/dev bottom-bar button that opens a QuickChoice menu of
    /// debug actions appropriate for the selected entity. Replaces MakeHostileAction
    /// and CommonActionDevDebug. Visible in sandbox mode (A.InstantBuild) or in
    /// non-production builds.
    /// </summary>
    public sealed class SandboxAction : BaseEntityAction {
        public const string ActionId = "Sandbox";

        [SelfInit]
        private static void Register() {
#if !STARDEUS_DEMO
            RegisterAction(new SandboxAction());
#endif
        }

        public override string Id => ActionId;

        private static bool IsSandboxActive() {
#if ALWAYS_SHOW
            return true;
#else
            return A.InstantBuild;
#endif
        }

        // ── Being ──────────────────────────────────────────────────────────

        protected override bool ApplyFor(GameState s, Being being,
                out EntityActionData data) {
            if (!IsSandboxActive() || !being.IsActive) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateForBeing(l, p, i, s, being), Priority.None);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings,
                out EntityActionData data) {
            if (!IsSandboxActive()) {
                data = EntityActionData.Unavailable;
                return false;
            }
            int cnt = CountActive(beings);
            if (cnt == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMultiForBeings(l, p, i, s, new List<Being>(beings), cnt),
                Priority.None);
            return true;
        }

        // ── Tile ───────────────────────────────────────────────────────────

        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            if (!IsSandboxActive()) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateForTile(l, p, i, s, tile), Priority.None);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles,
                out EntityActionData data) {
            if (!IsSandboxActive() || tiles.Count == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMultiForTiles(l, p, i, s, new List<Tile>(tiles)),
                Priority.None);
            return true;
        }

        // ── Obj ────────────────────────────────────────────────────────────

        protected override bool ApplyFor(GameState s, Obj obj,
                out EntityActionData data) {
            if (!IsSandboxActive() || !obj.IsActive) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateForObj(l, p, i, s, obj), Priority.None);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs,
                out EntityActionData data) {
            if (!IsSandboxActive()) {
                data = EntityActionData.Unavailable;
                return false;
            }
            int cnt = CountActive(objs);
            if (cnt == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMultiForObjs(l, p, i, s, new List<Obj>(objs), cnt),
                Priority.None);
            return true;
        }

        // ── Button factories ───────────────────────────────────────────────

        private WBase CreateForBeing(Lifetime l, RectTransform p, int i,
                GameState s, Being being) {
            var w = SpawnButton(l, true, p, i);
            w.Load(IconId.WDev, T.DevDebug, () => ShowBeingChoice(s, being));
            return w;
        }

        private WBase CreateMultiForBeings(Lifetime l, RectTransform p, int i,
                GameState s, List<Being> beings, int cnt) {
            var w = SpawnButtonMulti(l, true, p, i);
            w.LoadMulti(cnt, IconId.WDev, T.DevDebug,
                () => ShowMultiBeingChoice(s, beings));
            return w;
        }

        private WBase CreateForTile(Lifetime l, RectTransform p, int i,
                GameState s, Tile tile) {
            var w = SpawnButton(l, true, p, i);
            w.Load(IconId.WDev, T.DevDebug, () => ShowTileChoice(s, tile));
            return w;
        }

        private WBase CreateMultiForTiles(Lifetime l, RectTransform p, int i,
                GameState s, List<Tile> tiles) {
            var w = SpawnButtonMulti(l, true, p, i);
            w.LoadMulti(tiles.Count, IconId.WDev, T.DevDebug,
                () => ShowMultiTileChoice(s, tiles));
            return w;
        }

        private WBase CreateForObj(Lifetime l, RectTransform p, int i,
                GameState s, Obj obj) {
            var w = SpawnButton(l, true, p, i);
            w.Load(IconId.WDev, T.DevDebug, () => ShowObjChoice(s, obj));
            return w;
        }

        private WBase CreateMultiForObjs(Lifetime l, RectTransform p, int i,
                GameState s, List<Obj> objs, int cnt) {
            var w = SpawnButtonMulti(l, true, p, i);
            w.LoadMulti(cnt, IconId.WDev, T.DevDebug,
                () => ShowMultiObjChoice(s, objs));
            return w;
        }

        // ── Being QuickChoice ──────────────────────────────────────────────

        private static void ShowBeingChoice(GameState s, Being being) {
            var qc = QuickChoice.Create(T.DevDebug);
            if (being.IsActive && being.HasData) {
                qc.AddButton("Make Hostile", () => {
                    BeingUtils.MakeHostile(being);
                    UIUtils.RefreshSelectedEntityUI();
                });
                qc.AddButton("Set Faction...", () => ShowSetFactionChoice(being), autoClose: false);
                qc.AddButton("Fake Birthday", () => {
                    being.Data.OnBirthday(s, (int)being.Data.Body.Age + 1, G.Ticks);
                });
                qc.AddButton("Add Condition...", () => ShowAddConditionChoice(s, being), autoClose: false);
                if (being.Data.Mind?.Health != null) {
                    qc.AddButton("Start Breakdown...", () => ShowStartBreakdownChoice(s, being), autoClose: false);
                }
                qc.AddButton("Log Link", () => {
                    var msg = Texts.LinkToBeing(being.Data);
                    s.Sig.ShowFanfare.Send(msg);
                    s.Sys.Log.AddLine(msg);
                });
            }
            qc.AddButton("Set on Fire", () => {
                s.Sys.Firex.TrySetOnFire(being, Fire.Create());
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton(T.Destroy, () => {
                s.Beings.Destroy(being, true);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton("Destroy Others", () => DestroyOtherBeings(s, being));
            qc.Show();
        }

        private static void ShowMultiBeingChoice(GameState s, List<Being> beings) {
            var active = new List<Being>();
            foreach (var b in beings) {
                if (b.IsActive && b.HasData) active.Add(b);
            }
            var qc = QuickChoice.Create(T.DevDebug);
            if (active.Count > 0) {
                qc.AddButton($"Make Hostile ({Units.XNum(active.Count)})", () => {
                    MakeHostile(active);
                });
                qc.AddButton("Set Faction...", () => {
                    var qcFac = QuickChoice.Create("Set Faction");
                    foreach (var kv in FactionDef.Known) {
                        if (kv.Value.IsInternal) { continue; }
                        var fac = kv.Value;
                        qcFac.AddIconButton(fac.FlagIcon, fac.NameT, () => {
                            foreach (var b in active) {
                                b.Data.Mind.SetFaction(fac);
                            }
                            UIUtils.RefreshSelectedEntityUI();
                        });
                    }
                    qcFac.Show();
                }, autoClose: false);
            }
            qc.AddButton($"Set on Fire ({Units.XNum(beings.Count)})", () => {
                foreach (var b in beings) {
                    s.Sys.Firex.TrySetOnFire(b, Fire.Create());
                }
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton($"{T.Destroy} ({Units.XNum(beings.Count)})", () => {
                foreach (var b in beings) {
                    if (b.IsActive) s.Beings.Destroy(b, true);
                }
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.Show();
        }

        private static void ShowSetFactionChoice(Being being) {
            var qc = QuickChoice.Create("Set Faction");
            foreach (var kv in FactionDef.Known) {
                if (kv.Value.IsInternal) { continue; }
                var fac = kv.Value;
                qc.AddIconButton(fac.FlagIcon, fac.NameT,
                    () => {
                        being.Data.Mind.SetFaction(fac);
                        UIUtils.RefreshSelectedEntityUI();
                    });
            }
            qc.Show();
        }

        private static void ShowAddConditionChoice(GameState s, Being being) {
            var qc = QuickChoice.Create("Add Condition");
            foreach (var kv in Registries.Conditions.All) {
                var condKey = kv.Key;
                var cond = kv.Value;
                qc.AddIconButton(cond.Icon, cond.NameT, () => {
                    ConditionState.CreateFor(s, condKey, being.Data, Severity.Moderate, true);
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            qc.Show();
        }

        private static void ShowStartBreakdownChoice(GameState s, Being being) {
            var qc = QuickChoice.Create("Start Breakdown");
            foreach (var def in StoryEventDef.Ordered) {
                if ((def.Flags & StoryEventFlags.MentalBreakdown) == StoryEventFlags.None) {
                    continue;
                }
                var evId = def.Id;
                qc.AddButton(evId, () => StartBreakdown(s, being, evId));
            }
            qc.Show();
        }

        private static void StartBreakdown(GameState s, Being being, string evId) {
            if (!being.IsActive || !being.HasData) { return; }
            // The hook picker consumes DevForceHook and skips all event filters. Remove
            // it here too, since TryStartBreakdown can bail out before any hook runs.
            s.BB.Set(BBKeyH.DevForceHook, evId);
            if (!being.Data.Mind.Health.TryStartBreakdown(being, true)) {
                s.Sig.ShowFanfare.Send($"Could not start {evId} for {being.Data.Name}");
            }
            s.BB.Remove(BBKeyH.DevForceHook, evId);
            UIUtils.RefreshSelectedEntityUI();
        }

        // ── Tile QuickChoice ───────────────────────────────────────────────

        private static void ShowTileChoice(GameState s, Tile tile) {
            var qc = QuickChoice.Create(T.DevDebug);
            if (!tile.IsConstructed && tile.Constructable != null) {
                qc.AddButton(T.FinishConstruction, () => {
                    tile.Constructable.InstantBuild();
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (tile.TryGetComponent<CleanableComp>(out var cleanable)) {
                qc.AddButton(T.CleanNow, () => {
                    cleanable.RemoveDirt(10000f, null);
                    if (cleanable.TryGetGoal(out var g)) {
                        AIGoal.Cancel(g, G.Ticks);
                    }
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (tile.TryGetComponent<RefillableStorageComp>(out var refillable)
                    && refillable.IsConfigured) {
                qc.AddButton(T.Refill, () => {
                    refillable.Fill(1000);
                    UIUtils.RefreshSelectedEntityUI();
                });
                qc.AddButton(T.EjectFuel, () => {
                    refillable.DumpContents();
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (tile.TryGetComponent<BatteryComp>(out var battery) && battery.ENode != null) {
                qc.AddButton(T.Recharge, () => {
                    battery.SetFullCharge();
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (tile.Health != null) {
                qc.AddButton("Deal Damage...", () => ShowDealDamageChoice(tile), autoClose: false);
            }
            qc.AddButton("Set on Fire", () => {
                s.Sys.Firex.TrySetOnFire(tile, Fire.Create());
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton(T.Destroy, () => {
                DestroyTile(s, tile);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.Show();
        }

        private static void ShowMultiTileChoice(GameState s, List<Tile> tiles) {
            int unconstructed = 0;
            int cleanableCount = 0;
            int refillableCount = 0;
            int batteryCount = 0;
            foreach (var t in tiles) {
                if (!t.IsConstructed && t.Constructable != null) unconstructed++;
                if (t.HasComponent<CleanableComp>()) cleanableCount++;
                if (t.TryGetComponent<RefillableStorageComp>(out var r) && r.IsConfigured) refillableCount++;
                if (t.TryGetComponent<BatteryComp>(out var b) && b.ENode != null) batteryCount++;
            }
            var qc = QuickChoice.Create(T.DevDebug);
            if (unconstructed > 0) {
                qc.AddButton($"{T.FinishConstruction} ({Units.XNum(unconstructed)})", () => {
                    foreach (var t in tiles) {
                        if (!t.IsConstructed && t.Constructable != null) {
                            t.Constructable.InstantBuild();
                        }
                    }
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (cleanableCount > 0) {
                qc.AddButton($"{T.CleanNow} ({Units.XNum(cleanableCount)})", () => {
                    foreach (var t in tiles) {
                        if (t.TryGetComponent<CleanableComp>(out var c)) {
                            c.RemoveDirt(10000f, null);
                            if (c.TryGetGoal(out var g)) {
                                AIGoal.Cancel(g, G.Ticks);
                            }
                        }
                    }
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (refillableCount > 0) {
                qc.AddButton($"{T.Refill} ({Units.XNum(refillableCount)})", () => {
                    foreach (var t in tiles) {
                        if (t.TryGetComponent<RefillableStorageComp>(out var r) && r.IsConfigured) {
                            r.Fill(1000);
                        }
                    }
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            if (batteryCount > 0) {
                qc.AddButton($"{T.Recharge} ({Units.XNum(batteryCount)})", () => {
                    foreach (var t in tiles) {
                        if (t.TryGetComponent<BatteryComp>(out var b) && b.ENode != null) {
                            b.SetFullCharge();
                        }
                    }
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            qc.AddButton($"Set on Fire ({Units.XNum(tiles.Count)})", () => {
                foreach (var t in tiles) s.Sys.Firex.TrySetOnFire(t, Fire.Create());
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton($"{T.Destroy} ({Units.XNum(tiles.Count)})", () => {
                foreach (var t in tiles) DestroyTile(s, t);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.Show();
        }

        private static void ShowDealDamageChoice(Tile tile) {
            var qc = QuickChoice.Create("Deal Damage");
            qc.AddButton("50%", () => {
                tile.Health.TakeDamage(tile.Health.MaxHealth * 0.5f, DamageType.Piercing);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton("25%", () => {
                tile.Health.TakeDamage(tile.Health.MaxHealth * 0.25f, DamageType.Piercing);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton("15%", () => {
                tile.Health.TakeDamage(tile.Health.MaxHealth * 0.15f, DamageType.Piercing);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton("1%", () => {
                tile.Health.TakeDamage(tile.Health.MaxHealth * 0.01f, DamageType.Piercing);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.Show();
        }

        // ── Obj QuickChoice ────────────────────────────────────────────────

        private static void ShowObjChoice(GameState s, Obj obj) {
            var qc = QuickChoice.Create(T.DevDebug);
            qc.AddButton("Set on Fire", () => s.Sys.Firex.TrySetOnFire(obj, Fire.Create()));
            qc.AddButton(T.Destroy, () => {
                s.Objs.Destroy(obj, true);
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.Show();
        }

        private static void ShowMultiObjChoice(GameState s, List<Obj> objs) {
            var qc = QuickChoice.Create(T.DevDebug);
            qc.AddButton($"Set on Fire ({Units.XNum(objs.Count)})", () => {
                foreach (var o in objs) {
                    s.Sys.Firex.TrySetOnFire(o, Fire.Create());
                }
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.AddButton($"{T.Destroy} ({Units.XNum(objs.Count)})", () => {
                foreach (var o in objs) {
                    if (o.IsActive) s.Objs.Destroy(o, true);
                }
                UIUtils.RefreshSelectedEntityUI();
            });
            qc.Show();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static void MakeHostile(List<Being> beings) {
            var fac = FactionDef.Get(FactionId.Pirate);
            var army = Army.Create(fac).WithObjective(CombatObjectiveIdH.Raid);
            foreach (var b in beings) {
                if (!b.IsActive || !b.HasData) { continue; }
                b.Data.Mind.SetFaction(fac);
                var cu = b.AIAgent.CombatUnit;
                if (cu.Army != null) {
                    cu.Army.RemoveUnit(cu, CombatUnitState.Unaware);
                }
                army.WithUnit(cu);
            }
            The.R.Query.StartRaid.Ask(army);
            UIUtils.RefreshSelectedEntityUI();
        }

        private static void DestroyOtherBeings(GameState s, Being selected) {
            if (!selected.IsActive || !s.Beings.All.Contains(selected)) {
                return;
            }
            var targets = new List<Being>();
            foreach (var b in s.Beings.All) {
                if (b != selected && b.IsActive) {
                    targets.Add(b);
                }
            }
            foreach (var b in targets) {
                if (b.IsActive) {
                    s.Beings.Destroy(b, true);
                }
            }
            UIUtils.RefreshSelectedEntityUI();

        }

        private static void DestroyTile(GameState s, Tile tile) {
            if (tile.Constructable != null) {
                tile.Constructable.BreakDown(0);
            } else {
                s.CmdQ.Enqueue(new CmdRemoveTile(tile));
            }
        }

        private static int CountActive(List<Being> beings) {
            int cnt = 0;
            foreach (var b in beings) { if (b.IsActive) cnt++; }
            return cnt;
        }

        private static int CountActive(List<Obj> objs) {
            int cnt = 0;
            foreach (var o in objs) { if (o.IsActive) cnt++; }
            return cnt;
        }
    }
}
