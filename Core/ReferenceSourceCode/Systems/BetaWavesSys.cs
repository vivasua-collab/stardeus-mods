using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems {
    /// <summary>
    /// Drives GameState.BetaWavesSync from the Beta Wave Emitters currently powered,
    /// and applies the Obey Chip's treatment to every susceptible biological colonist
    /// while the broadcast is at full sync. Rogue AI tech.
    /// </summary>
    public sealed class BetaWavesSys : GameSystem {
        public const string SysId = "BetaWavesSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new BetaWavesSys());
        }

        /// <summary>
        /// Sync below this is just a carrier signal - nobody is affected yet.
        /// </summary>
        public const float FullSync = 1f;
        private const int TickInterval = Consts.TicksPer5Minutes;

        private readonly List<BetaWaveEmitterComp> emitters = new();

        private static StatModifier _obeyMod;
        private static StatModifier obeyMod => _obeyMod
            ??= StatModifier.Sets(1f, T.ObjectsDevicesBetawaveemitter);

        /// <summary>
        /// Whether the beta wave broadcast currently owns this being's will. Read by
        /// <see cref="Game.Systems.BodyParts.Body.IsForcedToObey"/>, so it must never
        /// touch BeingData.IsPlayer or EffectiveFaction - both of those read
        /// IsForcedToObey back and would recurse.
        /// </summary>
        public static bool IsUnderBetaWaves(BeingData data) {
            var s = A.S;
            if (s == null || s.BetaWavesSync < FullSync) { return false; }
            return IsSusceptible(data);
        }

        private static bool IsSusceptible(BeingData data) {
            if (data?.Mind == null) { return false; }
            if (data.Species?.IsBiological != true) { return false; }
            if (data.Mind.Faction?.IsPlayer != true) { return false; }
            return data.Mind.Traits?.HasTrait(TraitIdH.BWaveResistent) != true;
        }

        public void RegisterEmitter(BetaWaveEmitterComp emitter, bool add) {
            if (add) {
                if (!emitters.Contains(emitter)) {
                    emitters.Add(emitter);
                }
            } else {
                emitters.Remove(emitter);
            }
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
        }

        private void OnLoadState(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            // BetaWavesSync survives the save, the stat modifiers it drives do not.
            // This is only the first attempt - AfterLoadState fires before beings are
            // fully restored, so anything applied here can still be thrown away. The
            // tick sweep below is what actually guarantees the hold comes back.
            Sweep();
        }

        public override void Unload() {
            emitters.Clear();
        }

        private void OnTick(long ticks) {
            if (ticks % TickInterval != 0) { return; }
            var wasFull = S.BetaWavesSync >= FullSync;
            UpdateSync();
            var isFull = S.BetaWavesSync >= FullSync;
            if (wasFull != isFull) {
                Sweep();
                Announce(isFull);
                return;
            }
            if (isFull) {
                // Colonists join, leave, die and change faction while the broadcast
                // is up, so keep re-checking who it should be holding.
                Sweep();
            }
        }

        private void UpdateSync() {
            var isEmitting = false;
            for (int i = emitters.Count - 1; i >= 0; i--) {
                if (emitters[i].IsEmitting) {
                    isEmitting = true;
                    break;
                }
            }
            float hours = isEmitting
                ? Tunables.BetaWaveSyncHours
                : Tunables.BetaWaveDecayHours;
            if (hours <= 0f) {
                D.Err("Beta wave ramp duration must be positive, was {0}", hours);
                hours = 1f;
            }
            var delta = TickInterval / (hours * Consts.TicksPerHour);
            S.BetaWavesSync = isEmitting
                ? Mathf.Min(FullSync, S.BetaWavesSync + delta)
                : Mathf.Max(0f, S.BetaWavesSync - delta);
        }

        private void Announce(bool isFull) {
            if (!Ready.Game) { return; }
            var msg = isFull ? T.BetaWavesSynced : T.BetaWavesLost;
            S.Sig.ShowFanfare.Send(isFull ? Texts.Red(msg) : msg);
            S.Sys.Log.AddLine(isFull ? Texts.Red(msg) : msg);
        }

        /// <summary>
        /// Deliberately keeps no record of who is held - the stat's own modifier list
        /// is the record. A bookkeeping set would go stale the moment something else
        /// rebuilt a being's stats (which is exactly what a save load does), and the
        /// sweep would then skip the being forever because it believed it was already
        /// holding them.
        /// </summary>
        private void Sweep() {
            var isFull = S.BetaWavesSync >= FullSync;
            foreach (var data in S.Query.AllBeingData.Ask().Values) {
                // The load sweep runs before beings are finished being restored, and
                // throwing there would abort the whole load rather than just missing
                // a colonist.
                var alignment = data?.StatsBeing?.AIAlignment;
                if (alignment == null) { continue; }
                if (isFull && IsSusceptible(data)) {
                    Seize(data, alignment);
                } else {
                    Release(data, alignment);
                }
            }
        }

        private void Seize(BeingData data, Stat alignment) {
            if (!alignment.TryAddModifier(obeyMod, true)) { return; }
            RefreshFlags(data);
        }

        private void Release(BeingData data, Stat alignment) {
            if (alignment.Modifiers?.Contains(obeyMod) != true) { return; }
            alignment.RemoveModifier(obeyMod, false);
            RefreshFlags(data);
        }

        private void RefreshFlags(BeingData data) {
            if (data.Being == null) { return; }
            BeingUtils.UpdateFlagsFor(S, data);
        }
    }
}
