using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Story;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Input;

namespace Game.Modifiers {
    public sealed class ModifierDisableStoryEvents : Modifier {
        public static readonly HashSet<StoryEventDef> DisabledEvents = new();
        public const string ModifierId = "DisableStoryEvents";
        public override int SortOrder => 67;
        public override string GroupName => T.SituationStoryevents;
        public override string Id => ModifierId;
        public override bool CanChangeInGame => true;
        public static ModifierDisableStoryEvents Get =>
            (ModifierDisableStoryEvents) ById[ModifierId];

        [SelfInit]
        private static void Register() {
            DisabledEvents.Clear();
            Register(new ModifierDisableStoryEvents());
        }
        public override bool IsActiveByDefault => false;
        public override bool ForceShowInCloneGame => true;
        public override string NameT {
            get {
                var cnt = StoryEventDef.All.Count;
                int disCnt;
                if (UsePrefsForDisplay)  {
                    disCnt = The.R.State.Prefs.GetInt(
                        Pref.DisabledStoryEvents,
                        PrefH.DisabledStoryEvents, 0);
                } else {
                    disCnt = DisabledEvents.Count;
                }
                return T.DisableStoryEventsBtn_V1.F(
                    Units.XOutOfY(cnt - disCnt, cnt));
            }
        }
        public override string DescT {
            get {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendLine();
                var count = 0;
                if (UsePrefsForDisplay) {
                    var s = The.R.State;
                    foreach (var def in StoryEventDef.Ordered) {
                        if (!ReadDisabledPref(s, def)) { continue; }
                        sb.AppendFormat(" {0} {1}\n", Texts.Accented(GlyphMap.ArrowRight), def.Id);
                        count++;
                    }
                } else {
                    foreach (var evt in DisabledEvents) {
                        sb.AppendFormat(" {0} {1}\n", Texts.Accented(GlyphMap.ArrowRight), evt.Id);
                        count++;
                    }
                }
                if (count == 0) {
                    return T.DisableStoryEventsTip;
                }
                return T.DisableStoryEventsTipDisabled_V1.F(sb.ToString());
            }
        }

        // While a game is running, the save's own Prefs are the source of truth. Before that
        // (new game setup, clone game) there is no GameState to write to, so the choice is
        // staged in DisabledEvents until Apply commits it. The staging set is only ever
        // non-empty on a setup screen, which is what keeps the labels pointing at the right
        // one: a setup screen can be opened while an abandoned game is still loaded.
        private static bool UsePrefsForDisplay =>
            DisabledEvents.Count == 0
                && The.R?.State?.IsLoaded == true && !A.IsSaveTemplate;

        public override void Reset(GameSetupData data) {
            IsActive = false;
            if (GameState.IsCurrent(The.R?.State)) {
                var s = The.R.State;
                if (s.Prefs.Data.Bools != null) {
                    foreach (var boolKey in s.Prefs.Data.Bools.ToArray()) {
                        if (boolKey.Key.StartsWith("story.off.")) {
                            s.Prefs.UnsetBool(boolKey.Key);
                        }
                    }
                }
                s.Prefs.SetInt(Pref.DisabledStoryEvents, 0);
            }
            foreach (var def in StoryEventDef.All.Values) {
                def.IsDisabled = false;
            }
            DisabledEvents.Clear();
        }

        // inGame: asked from the in-game settings, where the running save's prefs decide.
        // Otherwise it is a setup screen, where the staging set does.
        public bool IsDisabled(StoryEventDef def, bool inGame) {
            if (inGame && GameState.IsCurrent(The.R?.State)) {
                return ReadDisabledPref(The.R.State, def);
            }
            return DisabledEvents.Contains(def);
        }

        public void SetDisabled(StoryEventDef def, bool disabled, bool inGame) {
            if (def == null) {
                D.Err("Can't toggle null story event.");
                return;
            }
            if (inGame && GameState.IsCurrent(The.R?.State)) {
                var s = The.R.State;
                ToggleStoryEvent(s, def, !disabled);
                // StoryEventDef.IsDisabled is a cache that StorySys only refreshes on load
                def.IsDisabled = disabled;
                RecountDisabled(s);
                return;
            }
            if (disabled) {
                AddDisabledEvent(def);
            } else {
                RemoveDisabledEvent(def);
            }
        }

        public void AddDisabledEvent(StoryEventDef def) {
            DisabledEvents.Add(def);
            IsActive = true;
        }
        public void RemoveDisabledEvent(StoryEventDef def) {
            DisabledEvents.Remove(def);
            IsActive = DisabledEvents.Count > 0;
        }

        // Keeps the Pref.DisabledStoryEvents counter (what the button label shows) and
        // IsActive in sync with the prefs that actually gate the events.
        public int RecountDisabled(GameState s) {
            var numDisabled = 0;
            if (s.Prefs.Data.Bools != null) {
                foreach (var boolKey in s.Prefs.Data.Bools) {
                    if (boolKey.Value && boolKey.Key.StartsWith("story.off.")) {
                        numDisabled++;
                    }
                }
            }
            s.Prefs.SetInt(Pref.DisabledStoryEvents, numDisabled);
            IsActive = numDisabled > 0;
            return numDisabled;
        }

        // Raw pref lookup, without the test guard that IsDisabledFor applies to gameplay.
        public static bool ReadDisabledPref(GameState s, StoryEventDef def) {
            return s.Prefs.GetBoolAutoHash($"story.off.{def.Id}", false);
        }

        public static bool IsDisabledFor(GameState s, StoryEventDef def) {
            // if (def.Category == StoryEventCategory.Internal) {
            //     return false;
            // }
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                return false;
            }
            #endif
            return ReadDisabledPref(s, def);
        }

        public static void ToggleStoryEvent(GameState s, string id, bool on) {
            if (id == null) {
                D.Err("Can't toggle null story event.");
                return;
            }
            if (on) {
                s.Prefs.UnsetBool($"story.off.{id}");
            } else {
                var evt = StoryEventDef.Find(id, true);
                s.Prefs.SetBool($"story.off.{id}", true);
            }
        }

        public static void ToggleStoryEvent(GameState s, StoryEventDef def, bool on) {
            ToggleStoryEvent(s, def?.Id, on);
        }

        public override void Apply(GameState s, bool isActive, bool isLoading) {
            // this can't be done here!
            if (!isLoading) {
                // Only a new game commits the staging set. A save already carries its own
                // choice in prefs, and a set left behind by an abandoned setup screen would
                // otherwise leak into whatever gets loaded next.
                foreach (var evt in DisabledEvents) {
                    D.Warn("Disabling story event: {0}", evt.Id);
                    ToggleStoryEvent(s, evt, false);
                }
            }
            // can't call reset here, it will kill the disabled events
            // Reset();
            DisabledEvents.Clear();
            if (RecountDisabled(s) > 0 && s.Prefs.Data.Bools != null) {
                foreach (var boolKey in s.Prefs.Data.Bools) {
                    if (!boolKey.Value || !boolKey.Key.StartsWith("story.off.")) { continue; }
                    #if PRODUCTION_BUILD
                    D.Warn("Story event disabled: {0}", boolKey.Key);
                    #else
                    D.Log("Story event disabled: {0}", boolKey.Key);
                    #endif
                }
            }
        }

        public override void CreateNewGameControls(Lifetime life, int idx, RectTransform parent) {
            var btn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WTabPanelItem_Button, parent, idx);
            btn.Load(DecoratedLabelT, () => OnStoryEvents(false, null));
            UIUtils.AddTipFunc(life, btn, () => DescT);
        }

        public override void CreateInGameControls(Lifetime lifetime, int idx, RectTransform parent) {
            var btn = UISpawn.Widget<WButtonText>(lifetime, UIPrefabId.WTabPanelItem_Button, parent, idx);
            // The count in the label is the only confirmation the player gets that a toggle
            // landed in the save, so refresh it while the popup is open
            btn.Load(DecoratedLabelT, () => OnStoryEvents(true, () => btn.SetText(DecoratedLabelT)));
            UIUtils.AddTipFunc(lifetime, btn, () => DescT);
        }
        private void OnStoryEvents(bool inGame, Action onChanged) {
            A.S.UIQuery.ShowSystemContextPopup.Ask(
                new DisableStoryEventsPopup(inGame, onChanged), true);
        }
    }
}
