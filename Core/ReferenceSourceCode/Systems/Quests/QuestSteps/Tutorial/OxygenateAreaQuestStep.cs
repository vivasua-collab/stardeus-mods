using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class OxygenateAreaQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new OxygenateAreaQuestStep(s, c, l));
        }
        public const string StepId = "OxygenateArea";
        private readonly Def def;
        private readonly bool isOneEnough;
        private readonly bool failIfNoDevice;
        private readonly float wantedO2;
        public OxygenateAreaQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var buildDef = config.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                    step, buildDef);
                return;
            }
            wantedO2 = config.GetFloat(PropertyIdH.MinOxygen, 0.19f);
            failIfNoDevice = config.GetBool(PropertyIdH.FailIfNoDevice, true);
            isOneEnough = config.GetBool(PropertyIdH.IsOneEnough, true);
            RefreshTitle();
            step.Desc = T.QuestStepOxygenateRoomTip_V2.F(def.CodexLink,
                Texts.LinkToOverlay(OverlayId.AtmoO2));
        }

        // Steps are built on load before the map is, so the first tick links the device
        private bool isTitleStale = true;
        // The room average the title shows, in whole percent. The player has to see it move:
        // "Oxygenate area" alone read as the surroundings, and a room too big for its pumps
        // looked like a quest that never progresses.
        private int shownPct = -1;
        private float currentO2;

        private void RefreshTitle() {
            shownPct = Mathf.FloorToInt(currentO2 * 100f);
            step.Title = Texts.WithProgress(
                T.QuestStepOxygenateRoom_V1.F(QuestStepLinks.DeviceLink(S, def)),
                Units.Percentage(currentO2), Units.Percentage(wantedO2));
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                RefreshTitle();
                A.S.UIQuery.ShowTutorialPopup.Ask(CreatingAtmospherePage.PageId, 10f);
            }
        }

        public void Tick(long currentTick) {
            if (isTitleStale) {
                isTitleStale = false;
                RefreshTitle();
            }
            if (!step.IsActive) { return; }
            var done = CheckCompleted();
            if (Mathf.FloorToInt(currentO2 * 100f) != shownPct) {
                RefreshTitle();
            }
            if (done) {
                CompleteStep();
            }
        }
        public bool CheckCompleted() {
            int enclosedCount = 0;
            int totalCount = 0;
            // The best room when one is enough, otherwise the worst one holding everybody back
            float shown = isOneEnough ? 0f : float.MaxValue;
            if (S.Map.TryAllByDef(def, out var found)) {
                totalCount = found.Count;
                foreach (var dev in found) {
                    if (!dev.Value.IsActive) { continue; }
                    var o2 = RoomO2(dev.Value, out var isEnclosed);
                    shown = isOneEnough ? Mathf.Max(shown, o2) : Mathf.Min(shown, o2);
                    if (isEnclosed && o2 >= wantedO2) {
                        enclosedCount++;
                    }
                }
            }
            currentO2 = shown == float.MaxValue ? 0f : shown;

            if (failIfNoDevice && totalCount == 0) {
                FailStep();
                return false;
            }
            if (isOneEnough) {
                return enclosedCount > 0;
            }
            var res = totalCount > 0 && totalCount == enclosedCount;
            return res;
        }
        // Average oxygen of the room the entity is in, or at the entity itself outside one
        private float RoomO2(Entity entity, out bool isEnclosed) {
            isEnclosed = false;
            var a = S.Sys.Areas;
            var areaId = a.At(entity.PosIdx);
            if (areaId <= 0) {
                //outside
                return S.GetO2(entity.PosIdx);
            }
            if (!a.Areas.TryGetValue(areaId, out var area) || area.Count == 0) {
                // recalculation / race condition?
                return S.GetO2(entity.PosIdx);
            }
            isEnclosed = true;
            float o2Total = 0;
            foreach (var pos in area) {
                o2Total += S.GetO2(pos);
            }
            return o2Total / area.Count;
        }
    }
}
