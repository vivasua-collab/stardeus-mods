namespace Game.Systems.UIUX {
    // Tells when the focus ring should travel from a selected entity onto its actions in the
    // bottom bar. New players kept missing that the bar had turned into the selection's
    // actions, even with the swap slide: they searched the entity panel and the screen,
    // deselected and selected again.
    // A new selection rings at most once per SelectCooldown, so clicking through many things
    // rings for the first one. A player who looks stuck gets the ring regardless of that
    // cooldown: one who keeps a selection KeptFor, or deselects and selects the same entity
    // again within ReselectWithin. Stuck rings still need StuckGap since the last ring, so
    // toggling a selection cannot replay it back to back.
    // Only in the first SessionLimit of a session, and never once the player has used entity
    // actions LearnedAfterUses times: they know where the actions are, and the stuck signals
    // are ordinary for them (half a minute on a colonist's panel is often reading).
    // Real time, like PauseNeglect: attention is spent in real time. A ring that cannot play
    // right now is not asked for and spends nothing.
    public sealed class EntityActionsHint {
        public const double SessionLimit = 3600;
        public const int LearnedAfterUses = 3;
        public const double SelectCooldown = 300;
        public const double KeptFor = 30;
        public const double ReselectWithin = 60;
        public const double StuckGap = 30;
        private const double NotSet = double.MinValue;
        private const int NoEntity = int.MinValue;

        private double lastRing = NotSet;
        private int selectedId = NoEntity;
        private double selectedAt;
        // The kept rule already rang for this selection, or an action press made it moot
        private bool isServed;
        private int deselectedId = NoEntity;
        private double deselectedAt = NotSet;

        // One entity's actions took over the bar. `canRing` is false when no ring could play
        // right now (hints off, a covering screen): the selection is still recorded, and
        // nothing is spent. The selection already recorded keeps its clock.
        public bool Select(int id, double now, double session, int uses, bool canRing) {
            if (id == selectedId) { return false; }
            bool isReselect = id == deselectedId && now - deselectedAt <= ReselectWithin;
            deselectedId = NoEntity;
            selectedId = id;
            selectedAt = now;
            isServed = false;
            if (!canRing || !IsOpen(session, uses)) { return false; }
            return TrySpend(now, isReselect ? StuckGap : SelectCooldown);
        }

        // The bar stopped showing that entity's actions. Only a deselect (nothing selected
        // anymore) can be followed by a reselect: switching to another entity and back, or the
        // bar coming back to it after a crew panel closed, is not looking for something.
        public void Clear(double now, bool byDeselect) {
            if (selectedId == NoEntity) { return; }
            if (byDeselect) {
                deselectedId = selectedId;
                deselectedAt = now;
            }
            selectedId = NoEntity;
        }

        // Cheap enough for every frame; the caller then checks whether a ring could play
        public bool IsKeptDue(double now) {
            return selectedId != NoEntity && !isServed && now - selectedAt >= KeptFor;
        }

        // The selection was kept KeptFor and a ring could play right now
        public bool Keep(double now, double session, int uses) {
            if (!IsOpen(session, uses)) {
                isServed = true;
                return false;
            }
            // Too soon after the last ring: asked again on a later frame
            if (!TrySpend(now, StuckGap)) { return false; }
            isServed = true;
            return true;
        }

        // The player pressed one of the actions: they found them
        public void ActionUsed() {
            isServed = true;
        }

        private static bool IsOpen(double session, int uses) {
            return session < SessionLimit && uses < LearnedAfterUses;
        }

        // Every ring counts for both gaps, so a stuck ring also holds off the next selection's
        private bool TrySpend(double now, double gap) {
            if (lastRing != NotSet && now - lastRing < gap) { return false; }
            lastRing = now;
            return true;
        }
    }
}
