namespace Game.Components {
    /// <summary>
    /// What kind of dispose check <see cref="DisposableComp.CanDisposeNow"/> is being asked to
    /// perform. Carries two independent things the old single `bool mass` parameter conflated:
    /// whether this is one explicitly picked entity or one member of a multi selection, and
    /// whether this is a button-availability check (may be a dry run) or the actual order being
    /// issued. Both matter, since a single explicitly picked, damaged, living, player owned bot
    /// is allowed to bend some guards - the friendly-alive check and the busy-with-a-goal check -
    /// that a bulk selection must keep at every stage.
    /// </summary>
    public enum DisposeCheck {
        /// <summary>Availability check for one explicitly picked entity.</summary>
        Single,
        /// <summary>Availability check for one member of a multi selection.</summary>
        Bulk,
        /// <summary>The order is being issued for one explicitly picked entity.</summary>
        SingleOrder,
        /// <summary>The order is being issued for one member of a multi selection.</summary>
        BulkOrder,
    }
}
