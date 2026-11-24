using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Systems.Notify;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.UIUX;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class EmergencySirenComp : BaseComponent<EmergencySirenComp>,
            IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new EmergencySirenComp());
        }
        private const float DefaultPassiveEnergyCost = 1f;
        private const float DefaultActiveEnergyCost = 10f;

        private float passiveEnergyCost;
        private float activeEnergyCost;
        private bool isAlertWanted;
        public readonly BBool IsAlertActive = new BBool();
        public readonly BBool IsAlertAutomatic = new BBool();
        public bool IsAlertSilent;
        private bool isRegistered;
        private float energyCost => IsAlertActive ? activeEnergyCost : passiveEnergyCost;

        public void SetAlertWanted(bool wanted) {
            isAlertWanted = wanted;
        }

        private NotificationHandle sirenEvent;
        private ParticlesSys.Data particles;

        protected override void OnConfig() {
            IsAlertAutomatic.SetSilent(true);
            passiveEnergyCost = Config.GetFloat(PropertyIdH.PassiveEnergyCost,
                DefaultPassiveEnergyCost);
            activeEnergyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultActiveEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(
                config.GetFloat(PropertyIdH.PassiveEnergyCost, DefaultPassiveEnergyCost),
                config.GetFloat(PropertyIdH.EnergyCost, DefaultActiveEnergyCost));
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsAlertWanted", isAlertWanted);
            data.SetBool("IsAlertAutomatic", IsAlertAutomatic);
            data.SetBool("IsAlertSilent", IsAlertSilent);
        }

        protected override void OnLoad(ComponentData data) {
            isAlertWanted = data.GetBool("IsAlertWanted", false);
            IsAlertAutomatic.SetSilent(data.GetBool("IsAlertAutomatic", true));
            IsAlertSilent = data.GetBool("IsAlertSilent", false);
        }
        public override string ToString() {
            return $" * EmergencySiren [Auto {IsAlertAutomatic}, Active: {IsAlertActive}]";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed) {
                RegisterSiren(true);
            }
            if (Tile.IsPendingMove || Tile.Constructable.HasGoal) {
                isAlertWanted = false;
            }
        }

        private ElectricNodeComp elNode;

        private void RegisterSiren(bool add) {
            if (isRegistered != add) {
                S.Sys.Sec.RegisterSiren(this, add);
                isRegistered = add;
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elNode.AddAfterTick(this, AfterTickGrid);
                    elnode.SetConsumerWantedInput(energyCost);
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                DeactivateAlarm();
                RegisterSiren(false);
            } else if (message == MsgIdH.ConstructionFinished) {
                RegisterSiren(true);
            } else if (message == MsgIdH.ElectricNodeConnectionChanged) {
                if (Tile.ENode.IsConnected && EntityUtils.IsWithinMargin(Tile.PosIdx, false, false) && Ready.Gen) {
                    S.UIQuery.ShowTutorialPopup.Ask(EmergencySirenPage.PageId, 1f);
                }
            }
        }

        public override void OnRemove() {
            DeactivateAlarm();
            RemoveParticles();
            RegisterSiren(false);
        }

        private void RemoveParticles() {
            if (isParticlesAdded) {
                isParticlesAdded = false;
                S.Sys.Particles.RemoveParticles(particles);
                particles = null;
            }
        }

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();

        public void RequestActivate() {
            isAlertWanted = true;
            if (Tile.ENode.IsConsuming) {
                ActivateAlarm();
            }
        }

        public void RequestDeactivate() {
            isAlertWanted = false;
            DeactivateAlarm();
        }

        public void SetSilent(bool silent) {
            IsAlertSilent = silent;
            if (silent) {
                if (entity.AudioSource.IsPlaying) {
                    entity.AudioSource.Stop();
                }
            } else if (IsAlertActive && !entity.AudioSource.IsPlaying) {
                entity.AudioSource.Play();
            }
        }

        private bool isParticlesAdded;

        private void EnableParticles() {
            if (!isParticlesAdded) {
                isParticlesAdded = true;
                if (particles == null) {
                    particles = new ParticlesSys.Data() {
                        PrefabName = "EmergencySirenFX",
                        IsStatic = true,
                        IsEmitOnly = true,
                        EmitChance = 1,
                        Owner = Entity,
                    };
                }
                S.Sys.Particles.AddParticles(particles);
            }
            particles.Play = true;
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (particles == null) { return; }
            D.Log("Moving siren particles: {0}:{1}", dx, dy);
            particles.Position += new Vector2(dx, dy);
            particles.EmitParams.position += new Vector3(dx, dy, 0);
        }

        private void DisableParticles() {
            if (!isParticlesAdded) { return; }
            particles.Play = false;
        }

        public void ActivateAlarm() {
            if (IsAlertActive) { return; }
            IsAlertActive.Set(true);
            if (!IsAlertSilent) {
                if (!entity.AudioSource.IsPlaying) {
                    entity.AudioSource.Play();
                }
            }
            EnableParticles();
            stateChangedB?.Invoke();
            S.Sig.ToggleSiren.Send(this, true);
            if (!sirenEvent.IsValid) {
                sirenEvent = Notification.BuildFor(Entity, null,
                        T.EmergencySirenActive, 0, NotificationType.Alert)
                    .Group("siren_act")
                    .WithTooltip(T.ObjectsDevicesEmergencysirenDesc)
                    .Sticky()
                    .Show();
            }
            ShowInfoIcon(IconId.CSiren, T.EmergencySirenActive);
        }

        public void DeactivateAlarm() {
            if (!IsAlertActive) { return; }
            IsAlertActive.Set(false);
            if (entity.AudioSource.IsPlaying) {
                entity.AudioSource.Stop();
            }
            DisableParticles();
            stateChangedB?.Invoke();
            S.Sig.ToggleSiren.Send(this, false);
            if (sirenEvent.IsValid) {
                Notification.Remove(ref sirenEvent);
            }
            HideInfoIcon();
        }

        private void AfterTickGrid() {
            if (elNode.IsConsuming) {
                if (isAlertWanted && IsAlertActive &&
                        (Tile.IsPendingMove || Tile.Constructable.HasGoal)) {
                    isAlertWanted = false;
                    DeactivateAlarm();
                }
                if (isAlertWanted && !IsAlertActive) {
                    ActivateAlarm();
                } else if (IsAlertActive && !isAlertWanted) {
                    DeactivateAlarm();
                }
            } else {
                if (IsAlertActive) {
                    DeactivateAlarm();
                }
            }
        }

    }
}
