using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Mental {
    [MessagePackObject]
    public sealed class MentalEffect {
        [Key("PosIdx")] public int PosIdx;
        [Key("StartTick")] public long StartTick;
        [Key("EndTick")] public long EndTick;
        [Key("Effect")] public sbyte Effect;
        [Key("Icon")] public string Icon;
        [Key("Reason")] public string Reason;
        [Key("Tooltip")] public string Tooltip;
        /// <summary>
        /// MoodSourceIdH of a permanent effect, 0 for a timed one. A permanent effect has no
        /// end time: the source that set it removes it when its cause is gone.
        /// </summary>
        [Key("SourceId")] public int SourceId;
        /// <summary>
        /// BeingData.Id of the body a permanent effect was set for. Every permanent source
        /// (gear, implants, conditions, sleep) belongs to the body, so a mind that moves to
        /// another body must not carry the old body's effects along.
        /// </summary>
        [Key("BodyId")] public uint BodyId;
        /// <summary>
        /// Last tick the source confirmed a permanent effect. Only a leak backstop reads it.
        /// </summary>
        [Key("ConfirmTick")] public long ConfirmTick;
        [SkipDumpInstance] [IgnoreMember] public bool IsAdded;
        [SkipDumpInstance] [IgnoreMember] private bool isUDBHistory;
        [IgnoreMember] public readonly BFloat BProgress = new();
        [IgnoreMember] public bool IsPermanent => SourceId != 0;

        /// <summary>
        /// Use MentalHealth.SetPermanentEffect instead of creating these directly.
        /// </summary>
        public static MentalEffect CreatePermanent(int sourceIdH, uint bodyId, int pos,
                string icon, long ticks, string reason, int effect) {
            return new MentalEffect() {
                PosIdx = pos,
                StartTick = ticks,
                EndTick = long.MaxValue,
                ConfirmTick = ticks,
                SourceId = sourceIdH,
                BodyId = bodyId,
                Reason = reason,
                Icon = icon,
                Effect = (sbyte) Mathf.Clamp(effect, -100, 100),
            };
        }

        public static MentalEffect CreateWithIcon(int pos, string icon, long start,
                long duration, string reason, int effect) {
            var me = Create(pos, start, duration, reason, effect);
            me.Icon = icon;
            return me;
        }

        public static MentalEffect Create(int pos, long start, long duration, string reason,
                int effect) {
            if (duration < Consts.TicksPerHour) {
                D.ErrSoft("Mood effect duration is too short! {0} {1}: {2} ticks",
                    reason, effect, duration);
            }
            if (effect > 100) {
                effect = 100;
                D.ErrSoft("Creating a mood effect with > 100! {0} {1}: {2} ticks",
                    reason, effect, duration);
            } else if (effect < -100) {
                effect = -100;
                D.ErrSoft("Creating a mood effect with < -100! {0} {1}: {2} ticks",
                    reason, effect, duration);
            }
            if (start + duration < G.Ticks) {
                D.Err("Creating a mood effect that will end right away! {0}", reason);
                start = G.Ticks;
            }
            return new MentalEffect() {
                PosIdx = pos,
                StartTick = start,
                EndTick = start + duration,
                Reason = reason,
                Icon = null,
                Effect = (sbyte) effect,
            };
        }

        public MentalEffect WithTooltip(string tip) {
            Tooltip = tip;
            return this;
        }
        public override string ToString() {
            return $"MentalEffect[{Reason}|{Effect}]";
        }


        public string OngoingTip() {
            var tip = IsPermanent
                ? T.StatusPermanent
                : T.EndsIn_V1.F(Units.TicksToTime(EndTick - G.Ticks));
            if (Tooltip != null) {
                tip = $"{Tooltip}{GlyphMap.BRHR}{tip}";
            }
            return tip;
        }

        public string HistoryTip() {
            var tip = T.EndedAgo_V1.F(Units.TicksAgoFromNow(EndTick));
            if (Tooltip != null) {
                tip = $"{Tooltip}{GlyphMap.BRHR}{tip}";
            }
            return tip;
        }

        public void OnClickUI() {
            if (Pos.IsOOBNonZero(PosIdx)) {
                The.SysSig.ShowTooltip.Send($"{T.Location}: {T.Unknown}", true);
            } else {
                EntityUtils.CameraFocusOn(PosIdx);
            }
        }

        public void OnHoverUI(bool h) {
            D.Warn("Hovering mental effect UI! {0} {1}", Reason, h);
            if (h) {
                EntityUtils.ShowPosLink(PosIdx);
            } else {
                EntityUtils.HideEntityLink();
            }
        }

        public void UpdateProgress(long ticks) {
            if (IsPermanent) { return; }
            float prog = (float)(ticks - StartTick) / (EndTick - StartTick);
            BProgress.Set(prog);
        }
    }
}
