using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;

using Game.Utils;
namespace Game.Systems.AI {
    /// <summary>
    /// Marks a being as capable of having mental health at all. Granted by the MIND species
    /// (Species.AddsNeeds), so a mood travels with the mind rather than with the brain or core
    /// it happens to sit in: a colonist moved into a robot core keeps their sanity, a crawler
    /// mind grafted into a human body has none, and a native robot mind never gains one.
    /// It is still carried by the mind holding slot, so pulling that part takes it away.
    /// Fun depends on it - see AINeed.RequiresMentalHealth and Species.ValidateMentalNeeds.
    /// </summary>
    public sealed class AINeedMentalHealth : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedMentalHealth());
        }

        public override int[] RelatedStats => null;
        // Sanity has its own tab, so this must never show up in the needs list.
        public override bool IsInternal => true;

        public override void Tick(GameState s, BeingData data, long ticks) { }
        public override void InitializeForNew(BeingData data) { }

        public override void InitializeFor(BeingData data) {
            Grant(data);
        }

        public override void OnRemoveNeed(BeingData data) {
            Revoke(data);
        }

        /// <summary>
        /// Gives the being a MentalHealth object. Also called from
        /// BeingData.SyncMentalHealthWithNeeds on load, where the need is never "added" because
        /// it was already in the save.
        /// </summary>
        public static void Grant(BeingData data) {
            var mind = data.Mind;
            if (mind == null) { return; }
            mind.Health ??= MentalHealth.TryNewFor(mind);
            // Initialize also clears IsUnused on the sanity stats, so a being that lost and
            // regained mental health gets its sanity UI back.
            mind.Health?.Initialize();
            ResetComp(data);
        }

        /// <summary>
        /// Drops the being's mental health, keeping the persistent stats so the same personality
        /// comes back if a mind that grants the need moves in later.
        /// </summary>
        public static void Revoke(BeingData data) {
            var mind = data.Mind;
            if (mind == null) { return; }
            if (mind.Health != null) {
                mind.SaveMentalStats();
                mind.Health = null;
            }
            var stats = data.StatsBeing;
            stats.Sanity.IsUnused = true;
            stats.SanityBaseline.IsUnused = true;
            stats.SanityTarget.IsUnused = true;
            ResetComp(data);
        }

        /// <summary>
        /// The component caches the MentalHealth it renders. Reaching it through Mind.Health
        /// would skip the case that matters most - a mind swapped for one that has no health at
        /// all - so ask the being for the component instead.
        /// </summary>
        private static void ResetComp(BeingData data) {
            if (data.Being == null) { return; }
            if (data.Being.TryGetComponent<MentalHealthComp>(out var comp)) {
                comp.AfterReplaceMind();
            }
        }

        public AINeedMentalHealth() : base(NeedId.MentalHealth) {}
    }
}
