using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.UI;

namespace Game.Components {
    public sealed class ResonanceChargeComp : BaseComponent<ResonanceChargeComp>,
            ITickListener, IDetonatable {
        private long explodeTick;
        private int explodeInTicks;
        private bool isDetonating;
        private bool isDetonated;
        public bool IsDetonating => isDetonating;
        public bool IsDetonated => isDetonated;
        private string preDetonateSound;
        private float detonateDelay;
        private int explosionLevel;

        private NotificationHandle timerEvent;
        private NotificationData timerEventData;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ResonanceChargeComp());
        }

        protected override void OnConfig() {
            preDetonateSound = Config.GetString(PropertyIdH.PreDetonateSound, null);
            detonateDelay = Config.GetFloat(PropertyIdH.DetonateDelay, 5f);
            explosionLevel = Config.GetInt(PropertyIdH.ExplosionLevel, 5);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsDetonating", isDetonating);
            data.SetLong("ExplodeTick", explodeTick);
            data.SetInt("ExplodeInTicks", explodeInTicks);
        }

        protected override void OnLoad(ComponentData data) {
            isDetonating = data.GetBool("IsDetonating", false);
            explodeTick = data.GetLong("ExplodeTick", 0);
            explodeInTicks = data.GetInt("ExplodeInTicks", 0);
        }

        public override void OnLateReady(bool wasLoaded) {
            UpdateGraphic();
        }

        public override void OnRemove() {
            RemoveEvent();
        }

        public override string ToString() {
            return " * Resonance Charge";
        }

        public bool CanTriggerDetonation => !isDetonating && !isDetonated;

        public void TriggerDetonation() {
            if (isDetonating || isDetonated) { return; }
            if (Obj?.IsActive != true) { return; }
            var failure = SectionDemolition.CanDemolish(S, Obj.PosIdx,
                out var offendingPos);
            if (failure != SectionDemolitionFailure.None) {
                ShowFailure(failure, offendingPos);
                return;
            }
            isDetonating = true;
            if (!string.IsNullOrEmpty(preDetonateSound)) {
                Sounds.Play(Obj.Position, preDetonateSound, true);
            }
            explodeInTicks = Mathf.Max(1,
                Mathf.CeilToInt(detonateDelay * Consts.TicksPerMinute));
            explodeTick = G.Ticks + explodeInTicks;
            ShowCountdown(explodeInTicks);
            UpdateGraphic();
        }

        private void Detonate() {
            if (isDetonated) { return; }
            if (Obj?.IsActive != true) { return; }
            isDetonated = true;
            RemoveEvent();
            HideAllInfo();
            var pos = Obj.Position;
            var posIdx = Obj.PosIdx;
            // Recomputed here rather than remembered from TriggerDetonation:
            // the section can dissolve during the countdown, and there is no
            // turning back at this point, so the charge is spent either way.
            var islandId = S.Sys.Areas.IslandAt(posIdx);
            Explosions.ForLevel(explosionLevel, out var radius, out var strength);
            Explosions.CreateExplosionFX(S, pos, radius, strength, true);
            S.Objs.Destroy(Obj, true);
            if (islandId > 0) {
                SectionDemolition.Execute(S, islandId, posIdx);
            }
        }

        public void Tick(long currentTick) {
            if (!isDetonating || isDetonated) { return; }
            if (explodeTick == 0) { return; }
            if (explodeTick <= currentTick) {
                HideAllInfo();
                Detonate();
                return;
            }
            ShowCountdown(explodeTick - currentTick);
        }

        // Also called the moment detonation is triggered: Tick only runs a tick
        // later, so leaving the announcement to it reports one tick less than the
        // delay the player was promised.
        private void ShowCountdown(float ticksLeft) {
            var minLeft = ticksLeft / Consts.TicksPerMinute;
            EnsureEvent(minLeft);
            var bar = ShowInfoBar(Consts.RedRectColor, ticksLeft / explodeInTicks,
                ticksLeft, explodeInTicks, T.CountdownMinutes_V1.F(minLeft));
            bar.IsPretranslated = true;
        }

        private void ShowFailure(SectionDemolitionFailure failure, int offendingPos) {
            switch (failure) {
                case SectionDemolitionFailure.AsimovThirdLaw:
                    ShowCpuDialog(offendingPos, T.AsimovViolation3);
                    UISounds.PlayActionDenied();
                    break;
                case SectionDemolitionFailure.AsimovFirstLaw:
                    S.Sig.ShowErrorMessage.Send(T.AsimovViolation1);
                    break;
                default:
                    S.Sig.ShowErrorMessage.Send(T.ResonanceChargeErrorNoSection);
                    break;
            }
        }

        private void ShowCpuDialog(int pos, string text) {
            var cpu = EntityUtils.ParentObjAt(pos);
            if (cpu != null) {
                The.Dialog.ShowSimple(cpu.Definition.Preview, text);
            }
        }

        private void RemoveEvent() {
            Notification.Remove(ref timerEvent);
            timerEventData = null;
        }

        private void EnsureEvent(float minutes) {
            var text = T.ExplosionImminentMinutes_V1.F(minutes);
            if (timerEvent.IsValid) {
                timerEventData.Title = text;
                return;
            }
            timerEventData = Notification.BuildFor(Obj, armedGraphicId, text,
                0, NotificationType.Critical)
                .Group("resonancecharge")
                .Sticky()
                .Emergency(false);
            timerEvent = timerEventData.Show();
            var title = Texts.LinkToObj(Obj, Texts.Red(text));
            S.Sig.ShowFanfare.Send(title);
            S.Sys.Log.AddLine(Obj, text);
        }

        private string _armedGraphicId;
        private string armedGraphicId => _armedGraphicId ??= $"{Obj.Graphics.GraphicId}_Armed";

        private void UpdateGraphic() {
            if (isDetonating) {
                Obj.Graphics.OverrideGraphic(armedGraphicId);
                Obj.Light.SwitchLight(true);
            } else {
                Obj.Graphics.OverrideGraphic(Obj.Graphics.GraphicId);
                Obj.Light.SwitchLight(false);
            }
        }
    }
}
