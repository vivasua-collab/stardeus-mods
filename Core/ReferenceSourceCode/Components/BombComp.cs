using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Data.Factions;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.Systems.UIUX;
using Game.Systems.Voice;
using Game.UI;

namespace Game.Components {
    public sealed class BombComp : BaseComponent<BombComp>,
            ITickListener, IWithFaction, IDetonatable {
        private long explodeTick;
        private int explodeInTicks;
        private bool isArmed;
        private bool isDetonated;
        private bool isDetonating;
        public bool IsDetonated => isDetonated;
        public bool IsDetonating => isDetonating;
        public bool IsArmed => isArmed;
        public bool HasTimer => explodeInTicks != 0;
        public bool IsPlayerFaction => Faction?.IsPlayer != false;
        private int level;
        private string preDetonateSound;
        private float detonateDelay;
        public FactionDef Faction { get; set; }

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BombComp());
        }

        private NotificationHandle timerEvent;
        private NotificationData timerEventData;

        public void PlayPreDetonateSound(bool ui) {
            if (ui) {
                UISounds.Play(preDetonateSound, vol: 0.5f);
            } else {
                Sounds.Play(Entity.Position, preDetonateSound, true);
            }
        }

        protected override void OnConfig() {
            level = Config.GetInt(PropertyIdH.ExplosionLevel, 5);
            preDetonateSound = Config.GetString(PropertyIdH.PreDetonateSound, null);
            detonateDelay = Config.GetFloat(PropertyIdH.DetonateDelay, 0);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsArmed", isArmed);
            data.SetBool("IsDetonating", isDetonating);
            data.SetLong("ExplodeTick", explodeTick);
            data.SetInt("ExplodeInTicks", explodeInTicks);
            data.SetString("FactionId", Faction?.Id);
        }

        protected override void OnLoad(ComponentData data) {
            isArmed = data.GetBool("IsArmed", false);
            isDetonating = data.GetBool("IsDetonating", false);
            explodeTick = data.GetLong("ExplodeTick", 0);
            explodeInTicks = data.GetInt("ExplodeInTicks", 0);
            var facId = data.GetString("FactionId", null);
            if (facId != null) {
                Faction = FactionDef.Get(facId);
            }
        }

        public override string ToString() {
            return " * Bomb";
        }

        public override void OnLateReady(bool wasLoaded) {
            UpdateGraphic();
        }

        public void ToggleArm(bool on) {
            // Once the player has triggered detonation there is no turning back
            if (isDetonating) { return; }
            isArmed = on;
            if (!on) {
                explodeInTicks = 0;
                explodeTick = 0;
            }
            UpdateGraphic();
            stateChangedB?.Invoke();
        }

        public bool CanTriggerDetonation => IsPlayerFaction
            && !isDetonating && !isDetonated;

        public void TriggerDetonation() {
            if (isDetonating || isDetonated) { return; }
            isDetonating = true;
            if (!string.IsNullOrEmpty(preDetonateSound)) {
                PlayPreDetonateSound(false);
            }
            SetTimer(Mathf.Max(1, Mathf.CeilToInt(detonateDelay * Consts.TicksPerMinute)));
            ShowCountdown(explodeInTicks);
            stateChangedB?.Invoke();
        }

        public void Detonate() {
            if (isDetonated) { return; }
            isDetonated = true;
            // S.Query.DeselectIfSelected.Ask(Obj);
            D.Warn("Detonating explosive at {0}", Entity.Position);
            S.Objs.Destroy(Obj, true);
            DetonateWithSFX();
        }

        private void DetonateWithSFX() {
            int delay;
            if (!string.IsNullOrEmpty(preDetonateSound)) {
                // On the player-triggered path the sound already played when the
                // countdown started, so don't stack a second siren on the blast
                if (!isDetonating) {
                    PlayPreDetonateSound(false);
                }
                delay = 0;
            } else {
                delay = Mathf.CeilToInt(detonateDelay * Consts.TicksPerMinute);
            }
            Explosion.Create(S, level,
                position: Obj.Position,
                delay: delay);
            S.Clock.ExecuteAfterTicks(delay, RemoveEvent);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.TookDamage) {
                Detonate();
            }
        }

        public void Tick(long currentTick) {
            if (!isArmed) { return; }
            if (explodeTick != 0) {
                if (explodeTick <= currentTick) {
                    HideAllInfo();
                    Detonate();
                } else {
                    ShowCountdown(explodeTick - currentTick);
                }
            } else {
                if (Faction == null) {
                    Faction = FactionDef.Player;
                }
                var enemy = S.Beings.FindFirstInPos(Obj.PosIdx, (b) => {
                    return b.IsAlive && b.HasData && Faction != b.Data.EffectiveFaction
                        && b.Carrier == null && !b.Data.Body.IsIncapacitated;
                });
                if (enemy != null) {
                    Detonate();
                }
            }
        }

        // Also called the moment the player triggers detonation: Tick only runs a
        // tick later, so leaving the announcement to it reports one tick less than
        // the delay the player was promised.
        private void ShowCountdown(float ticksLeft) {
            var minLeft = ticksLeft / Consts.TicksPerMinute;
            EnsureEvent(minLeft);
            var bar = ShowInfoBar(Consts.RedRectColor, ticksLeft / explodeInTicks,
                ticksLeft, explodeInTicks, T.CountdownMinutes_V1.F(minLeft));
            bar.IsPretranslated = true;
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
                .Group("bomb")
                .Sticky()
                .Emergency(false)
                .WithVoice(VoiceLine.Bomb);
            timerEvent = timerEventData.Show();
            var title = Texts.LinkToObj(Obj, Texts.Red(text));
            S.Sig.ShowFanfare.Send(title);
            S.Sys.Log.AddLine(Obj, text);
        }

        public static BombComp Spawn(GameState S, Vector2 pos) {
            var cmd = new CmdSpawnObj(pos, The.Defs.FastGet(DefIdsH.ObjMiscC42), true);
            cmd.Execute(S);
            var obj = cmd.Obj;
            var bomb = obj.GetComponent<BombComp>();
            return bomb;
        }

        public void SetTimer(int afterTicks) {
            isArmed = afterTicks > 0;
            explodeTick = G.Ticks + afterTicks;
            explodeInTicks = afterTicks;
            UpdateGraphic();
        }

        private string _armedGraphicId;
        private string armedGraphicId => _armedGraphicId ??= $"{Obj.Graphics.GraphicId}_Armed";


        private void UpdateGraphic() {
            if (isArmed) {
                Obj.Graphics.OverrideGraphic(armedGraphicId);
                Obj.Light.SwitchLight(true);
            } else {
                Obj.Graphics.OverrideGraphic(Obj.Graphics.GraphicId);
                Obj.Light.SwitchLight(false);
            }
        }

    }
}
