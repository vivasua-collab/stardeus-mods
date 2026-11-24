using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System.Text;
using Game.Systems.Stats;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.UI;

namespace Game.Components {
    public sealed class HeatProducerComp : BaseComponent<HeatProducerComp>,
            IRareTickListener,
            IDefDescriptionProvider, IStatProvider {
        private readonly List<int> surroundings = new();

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new HeatProducerComp());
        }

        private float range;
        private float rate;
        /// <summary>
        /// Percentage of max output (rate * Output = actual rate)
        /// </summary>
        public float Output;
        private float multiplier;
        private bool grabbedSurroundings;
        private bool checkSameArea;
        private int maxTemp;
        /// <summary>
        /// Produces regardless of electricity use
        /// </summary>
        private bool isPassive;
        private static float warnTemp;

        protected override void OnConfig() {
            if (warnTemp == 0) {
                warnTemp = Units.CelsiusToKelvin(100f);
            }
            rate = Config.GetFloat(PropertyIdH.Rate);
            range = Config.GetInt(PropertyIdH.Range, 2);
            maxTemp = Mathf.RoundToInt(Units.CelsiusToKelvin(
                Config.GetInt(PropertyIdH.MaxTempCelsius, 1000)));
            checkSameArea = Config.GetBool(PropertyIdH.CheckSameArea, false);
            Output = Config.GetFloat(PropertyIdH.InitialOutput, 1f);
            isPassive = Config.GetBool(PropertyIdH.IsPassive, false);
        }

        private float lastRate;
        public bool IsProducingHeat => lastRate > 0f;
        public readonly B<float> OutputB = new(0f);

        public override void OnLateReady(bool wasLoaded) {
            multiplier = Tunables.HeatProductionMultiplier;
        }

        public override string ToString() {
            return string.Format(
                " * HeatProducer [Rate: {0}, Range: {1}, Output: {2}]",
                    rate, range, Output);
        }
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public string StatsCategoryName => T.HeatProducer;
        public int StatsOrder => 50;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }

        private void AfterTickGrid() {
            if (!isPassive && !elNode.IsConsuming && !elNode.IsProducing) {
                lastRate = 0;
                OutputB.Set(0f);
                return;
            }
            // The first tick should probably happen while game is not ready yet
            if (!Ready.Game) { return; }
            if (!Ready.Gen) { return; }
            if (!Ready.Areas || !Ready.AtmoTemp) { return; }
            var devicePos = entity.CenterIdx;
            if (devicePos == 0) {
                D.Err("We are ticking energy while device was not loaded!");
                return;
            }
            // if (curArea == 0) { return 0f; }
            var outrate = rate * Output * multiplier;
            // The pin below holds a tile through the atmosphere step, so a producer that makes
            // no heat (a ship computer under full heat sinks, an idle engine) would still keep
            // its tiles from cooling and feed their warmth to the room
            if (outrate <= 0f) {
                lastRate = 0;
                OutputB.Set(0f);
                return;
            }
            var area = S.Sys.Areas;//.AreaAt(devicePos, out var curArea);
            var curArea = area.At(devicePos);

            // Heating
            if (!grabbedSurroundings) {
                GrabSurroundings();
            }

            var sc = surroundings.Count;
            var sumTemp = 0f;
            for (int i = 0; i < sc; i++) {
                int pos = surroundings[i];
                // not heat something outside the area
                if (checkSameArea && area.At(pos) != curArea) {
                    continue;
                }
                var level = S.IncrTemp(pos, outrate, maxTemp);
                sumTemp += level;
                // Hold the tile at what it reached while the step's iterations spread
                // it, capped at maxTemp so a burning tile is not propped up by us
                S.PinTempAtLeast(pos, level < maxTemp ? level : maxTemp);
            }
            lastRate = outrate;
            OutputB.Set(rate > 0 ? lastRate / rate : 0f);
        }

        private void GrabSurroundings() {
            grabbedSurroundings = S.Grid.GetSurroundings(
                entity.CenterIdx, range, surroundings);
        }

        private NotificationHandle hotEvent;
        private bool isShowingHotNotification;
        private void ShowHotNotification() {
            isShowingHotNotification = true;
            if (!hotEvent.IsValid) {
                ShowInfoIcon(IconId.CTemperature, T.TempTooHot);
                hotEvent = Notification.BuildFor(Tile, IconId.CTemperature, T.TempTooHot, 0, NotificationType.Alert)
                    .WithTooltip(T.TooHotTip)
                    .Group("too_hot")
                    .Show();
            }
        }

        private void HideHotNotification() {
            isShowingHotNotification = false;
            if (hotEvent.IsValid) {
                HideInfoIcon();
                Notification.Remove(ref hotEvent);
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            grabbedSurroundings = false;
            surroundings.Clear();
        }

        public void RareTick(long currentTick) {
            var temp = S.GetTemp(Tile.CenterIdx);
            var isTooHot = temp > warnTemp;
            if (isShowingHotNotification && !isTooHot) {
                HideHotNotification();
            } else if (isTooHot && !isShowingHotNotification) {
                ShowHotNotification();
            }
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            addedAnything = true;
            sb.Append(T.HeatProducer);
            return addedAnything;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (!grabbedSurroundings) {
                GrabSurroundings();
            }
            var w = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress,
                block.Container, idx++);
            w.Load(IconId.CTemperature, T.HeatProducer, OutputB.Value);
            OutputB.Bind(life, (p) => {
                w.SetPercent(p, false);
                w.Item.SetText2(Units.PercentageColored(p));
            });
            UIUtils.AddTip(life, w, T.HeatProducerTip);

            // var rangeRow = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2,
            //     block.Container, idx++);
            // rangeRow.Load(IconId.CTemperature, T.Range,
            //     Units.Num(surroundings.Count));
        }
    }
}
