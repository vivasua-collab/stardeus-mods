using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.Voice {
    /// <summary>
    /// Plays spoken callouts for critical events (fire, raid, incoming asteroid, etc.)
    /// one at a time, without repeating the same line too often, and without piling up
    /// a backlog of stale announcements. See Docs/Architecture/Systems/VoiceAlerts.md.
    /// </summary>
    public sealed class VoiceSys : GameSystem {
        public const string SysId = "VoiceSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new VoiceSys());
        }

        // Minimum real seconds before the same line can be said again.
        private const float LineCooldownSeconds = 30f;
        // Real seconds of silence kept between two consecutive lines.
        private const float GapSeconds = 0.75f;
        // Real seconds between requesting a line and it starting to speak, so the alert
        // claxon or notification sound that fired with the same event is done first.
        private const float LeadInSeconds = 1f;
        // A queued line older than this no longer describes the current situation.
        private const float MaxQueueAgeSeconds = 20f;
        // Upper bound on queued lines, in case many events land in the same frame.
        private const int MaxQueueLength = 4;
        // Suppresses lines right after a save loads, so re-created notifications for
        // an already-ongoing situation don't trigger a burst of callouts.
        private const float PostLoadMuteSeconds = 5f;
        private const float VoiceVolume = 0.5f;

        private struct PendingLine {
            public string Id;
            public double EnqueuedAt;
        }

        private readonly Dictionary<int, double> lastSaidAt = new();
        private readonly List<PendingLine> queue = new();
        private double busyUntil;
        private double muteUntil;
        private bool isEnabled = true;

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            OnSettingChanged(Pref.GVoiceAlerts);
        }

        private void OnLoadState(GameState state) {
            muteUntil = GameTimer.realtimeSinceStartupAsDouble + PostLoadMuteSeconds;
            busyUntil = 0;
            queue.Clear();
            lastSaidAt.Clear();
            state.Sig.SayVoiceLine.AddListener(Enqueue);
            state.Sig.EveryFrame.AddListener(EveryFrame);
        }

        private void OnSettingChanged(string setting) {
            if (setting == Pref.GVoiceAlerts) {
                isEnabled = The.Prefs.GetBool(Pref.GVoiceAlerts, PrefH.GVoiceAlerts, true);
            }
        }

        private void Enqueue(string lineId) {
            if (string.IsNullOrEmpty(lineId)) { return; }
            if (!isEnabled || G.IsDemoMode) { return; }
            if (!Ready.Game || !Ready.Gen || UIShowing.LoadingScreen) { return; }
            var now = GameTimer.realtimeSinceStartupAsDouble;
            if (now < muteUntil) { return; }
            var hash = lineId.GetHashCode();
            if (lastSaidAt.TryGetValue(hash, out var last) && now - last < LineCooldownSeconds) {
                return;
            }
            for (int i = 0; i < queue.Count; i++) {
                if (queue[i].Id == lineId) { return; }
            }
            if (queue.Count >= MaxQueueLength) { return; }
            queue.Add(new PendingLine { Id = lineId, EnqueuedAt = now });
            // Reserve the cooldown now, not at playback time, so a burst of the same
            // event within the cooldown window can't queue up more than once.
            lastSaidAt[hash] = now;
        }

        private void EveryFrame() {
            if (queue.Count == 0) { return; }
            var now = GameTimer.realtimeSinceStartupAsDouble;
            if (now < busyUntil) { return; }
            while (queue.Count > 0 && now - queue[0].EnqueuedAt > MaxQueueAgeSeconds) {
                queue.RemoveAt(0);
            }
            if (queue.Count == 0) { return; }
            // Entries are enqueued in time order, so the head is always the first one to
            // clear its lead-in, and waiting here keeps playback in request order.
            if (now - queue[0].EnqueuedAt < LeadInSeconds) { return; }
            var line = queue[0];
            queue.RemoveAt(0);
            Play(line.Id, now);
        }

        private void Play(string lineId, double now) {
            var clip = The.Sounds.Find(lineId);
            if (clip == null) { return; }
            The.SysSig.PlayUISound.Send(lineId, VoiceVolume);
            busyUntil = now + clip.length + GapSeconds;
        }

        public override void Unload() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
            queue.Clear();
            lastSaidAt.Clear();
        }
    }
}
