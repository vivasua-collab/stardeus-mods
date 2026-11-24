using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class HeatAreaQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new HeatAreaQuestStep(s, c, l));
        }
        public const string StepId = "HeatArea";
        private readonly Def def;
        private readonly bool isOneEnough;
        private readonly bool failIfNoDevice;
        private readonly float wantedTempKelvin;
        public HeatAreaQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var buildDef = config.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                    step, buildDef);
                return;
            }
            var temp = config.GetInt(PropertyIdH.MinTempCelsius, 5);
            wantedTempKelvin = Units.CelsiusToKelvin(temp);
            failIfNoDevice = config.GetBool(PropertyIdH.FailIfNoDevice, true);
            isOneEnough = config.GetBool(PropertyIdH.IsOneEnough, true);
            RefreshTitle();
            step.Desc = T.QuestStepHeatRoomTip_V2.F(def.CodexLink,
                Texts.LinkToOverlay(OverlayId.AtmoTemp));
        }

        // Steps are built on load before the map is, so the first tick links the device
        private bool isTitleStale = true;
        // The room average the title shows, in whole degrees Kelvin. Same reason as the
        // oxygen step: a room too big for its heaters must look slow, not stuck.
        private int shownKelvin = -1;
        private float currentKelvin;

        private void RefreshTitle() {
            shownKelvin = Mathf.FloorToInt(currentKelvin);
            step.Title = Texts.WithProgress(
                T.QuestStepHeatRoom_V1.F(QuestStepLinks.DeviceLink(S, def)),
                Texts.Temperature(currentKelvin), Texts.Temperature(wantedTempKelvin));
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
            if (Mathf.FloorToInt(currentKelvin) != shownKelvin) {
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
                    var temp = RoomTemp(dev.Value, out var isEnclosed);
                    shown = isOneEnough ? Mathf.Max(shown, temp) : Mathf.Min(shown, temp);
                    if (isEnclosed && temp >= wantedTempKelvin) {
                        enclosedCount++;
                    }
                }
            }
            currentKelvin = shown == float.MaxValue ? 0f : shown;

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
        // Average temperature of the room the entity is in. Outside an enclosed room it is the
        // temperature at the entity itself, so the title never shows absolute zero.
        private float RoomTemp(Entity entity, out bool isEnclosed) {
            isEnclosed = false;
            var a = S.Sys.Areas;
            var areaId = a.At(entity.PosIdx);
            if (areaId <= 0) {
                //outside
                return S.GetTemp(entity.PosIdx);
            }
            if (!a.Areas.TryGetValue(areaId, out var area) || area.Count == 0) {
                return S.GetTemp(entity.PosIdx);
            }
            isEnclosed = true;
            float tempTotal = 0;
            foreach (var pos in area) {
                tempTotal += S.GetTemp(pos);
            }
            return tempTotal / area.Count;
        }
    }
}
