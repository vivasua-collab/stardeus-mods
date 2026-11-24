using System;
using Game.Constants;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Game.Utils;
using KL.Utils;
using System.Globalization;
using Game.Data;
using Game.Systems.UIUX;

namespace Game.UI {
    /// <summary>
    /// Handles in-game clock display in top right
    /// </summary>
    public class GameTimeText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
            IPointerClickHandler {
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text dayText;
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private RectTransform clockHourHandRT;
        [SerializeField] private RectTransform clockMinuteHandRT;
        public static string HoursText;
        public static int Day;

        private bool wasReady;
        private int lastHour;
        private int lastMin;
        private int lastDay = -1;
        private GameState S;

        public void OnPointerClick(PointerEventData eventData) {
            // S is null between games - Update() and Start() both treat it as optional
            if (S?.Clock == null) { return; }
            if (The.Bindings.IsPressed(Input.ActionType.CtrlModifier)) {
                WPopup.Create(true,
                    IconId.CInfo,
                    T.GameData,
                    null,
                    S.DebugInfo(true))
                    .WithAction2(T.CopyGameSeed,
                        () => GUIUtility.systemCopyBuffer = S.SeedStr)
                    .Spawn(false);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (S?.Clock == null) { return; }
            var status = T.GameplayShortStat_V4.F(
                    Texts.Accented(DateTime.Now.ToString("HH:mm dddd, MMM d",
                        CultureInfo.CurrentUICulture)),
                    Texts.Accented(Units.SecondsToHours(S.SessionTime)),
                    Texts.Accented(Units.SecondsToHours(S.Playtime)),
                    Texts.Accented(Units.TicksToTime(S.Clock.Ticks)));

            var batLevel = SystemInfo.batteryLevel;
            if (batLevel > 0f) {
                switch (SystemInfo.batteryStatus) {
                    case BatteryStatus.Unknown: {
                        var bl = SystemInfo.batteryLevel;
                        if (bl > 0 && bl < 1) {
                            var batStatus = $"Battery: {Units.Percentage(bl)}";
                            status = $"{batStatus}\n{status}";
                        }
                        break;
                    }
                    case BatteryStatus.Charging:
                    case BatteryStatus.Discharging:
                    case BatteryStatus.Full:
                    case BatteryStatus.NotCharging: {
                        var batStatus = T.DeviceBatteryLevel_V2.F(
                            Units.Percentage(SystemInfo.batteryLevel),
                            SystemInfo.batteryStatus);
                        status = $"{batStatus}\n{status}";
                        break;
                    }
                }
            }

            #if !PRODUCTION_BUILD
            if (S.Clock.IsForceStopped) {
                status = $"{status}\nGame Clock Force Stopped: {S.Clock.StopReasons}";
            }
            #endif

            //status = $"{status}\n{VersionInfo.MemUsage()}";

            The.SysSig.ShowTooltip.Send(status, true);
        }

        public void OnPointerExit(PointerEventData eventData) {
            The.SysSig.HideTooltip.Send();
        }

        private void Start() {
            timeText.text = null;
            dayText.text = null;
            S = The.R?.State;
            // cg.alpha = 0;
            wasReady = false;
        }

        private void Update() {
            if (wasReady) {
                if (Ready.Game) {
                    UpdateClock();
                } else {
                    wasReady = false;
                    // cg.alpha = 0;
                }
            } else if (Ready.Game) {
                if (S == null || !GameState.IsCurrent(S)) {
                    S = The.R?.State;
                }
                if (S?.Clock != null) {
                    wasReady = true;
                    // TweenCGA.Add(gameObject, 1, 1);
                }
            }
        }

        public void UpdateClock() {
            var date = S.Clock.ToDateTime(Consts.TickDurationInGameTime);
            var h = date.Hour;
            var m = date.Minute;
            if (lastMin == m && lastHour == h) {
                return;
            }
            clockMinuteHandRT.rotation =
                Quaternion.Euler(0, 0, -360f / 60f * m);
            clockHourHandRT.rotation =
                Quaternion.Euler(0, 0, -360f / 12f * ((h % 12) + (m / 60f)));
            lastHour = h;
            lastMin = m;
            var hoursText = Texts.ToTime(date, out var day);
            HoursText = hoursText;
            timeText.SetText(hoursText);
            if (day != lastDay) {
                #if !PRODUCTION_BUILD
                if (!The.TestsAreRunning) {
                    D.Log("Day: {0}. Session: {1:0.00}h. Clock speed: {2}. Game ID: {3}",
                        day, GameTimer.realtimeSinceStartupAsDouble / (60f * 60f),
                            G.ClockSpeed, S.GameId);
                }
                #else
                D.Warn("Day: {0}. Session: {1:0.00}h. Clock speed: {2}. Game ID: {3}",
                    day, GameTimer.realtimeSinceStartupAsDouble / (60f * 60f),
                        G.ClockSpeed, S.GameId);
                #endif
                // S.Clock.LogRates();
                lastDay = day;
                Day = day;
                var dayTxt = $"{T.Day} {day}";
                dayText.SetText(dayTxt);
            }
        }
    }
}
