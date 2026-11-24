namespace Game.Procgen {
    // ShipFactory hands every stage the next fork of spec.Rng in list order, so removing or
    // moving a stage gives every later stage a different rng and reshapes every seed of the
    // scenario, curated Quick Start seeds included. Put this where the stage used to be.
    // It builds nothing: taking its fork is the whole job.
    public sealed class RngSlotGenStage : BaseShipGenStage {
    }
}
