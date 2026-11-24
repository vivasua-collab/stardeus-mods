using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class MsgId {
    public const string BeingDataWasSet = nameof(BeingDataWasSet);
    public const string BeingActivated = nameof(BeingActivated);
    public const string BodyConditionChanged = nameof(BodyConditionChanged);
    public const string BeingFinishedLoading = nameof(BeingFinishedLoading);
    public const string SwitchedOn = nameof(SwitchedOn);
    public const string SwitchedOff = nameof(SwitchedOff);
    public const string DirectControlOn = nameof(DirectControlOn);
    public const string DirectControlOff = nameof(DirectControlOff);
    public const string FactionChanged = nameof(FactionChanged);
    public const string ConstructionFinished = nameof(ConstructionFinished);
    public const string DeconstructionOngoing = nameof(DeconstructionOngoing);
    public const string DeconstructionFinished = nameof(DeconstructionFinished);
    public const string TilePosChanged = nameof(TilePosChanged);
    public const string EntitySelected = nameof(EntitySelected);
    public const string EntityDeselected = nameof(EntityDeselected);
    public const string GraphicHidden = nameof(GraphicHidden);
    public const string GraphicUnhidden = nameof(GraphicUnhidden);
    public const string HealthWentToZero = nameof(HealthWentToZero);
    public const string HullIntegrityChanged = nameof(HullIntegrityChanged);
    public const string TookDamage = nameof(TookDamage);
    public const string TookExcessiveDamage = nameof(TookExcessiveDamage);
    public const string RepairedSomeDamage = nameof(RepairedSomeDamage);
    public const string RepairedAllDamage = nameof(RepairedAllDamage);
    public const string ElectricNodeAdded = nameof(ElectricNodeAdded);
    public const string ElectricNodeConnectionChanged = nameof(ElectricNodeConnectionChanged);
    public const string ElectricNodeSwitchedOff = nameof(ElectricNodeSwitchedOff);
    public const string ElectricNodeSwitchedOn = nameof(ElectricNodeSwitchedOn);
    public const string ElectricNodeReachabilityChanged = nameof(ElectricNodeReachabilityChanged);
    public const string FlammableAdded = nameof(FlammableAdded);
    public const string EquipmentRemoved = nameof(EquipmentRemoved);
    public const string EquipmentAdded = nameof(EquipmentAdded);
    public const string ProcessingEnqueued = nameof(ProcessingEnqueued);
    public const string InSpaceChanged = nameof(InSpaceChanged);
    public const string CarryStarted = nameof(CarryStarted);
    public const string CarryEnded = nameof(CarryEnded);
    public const string ImmobilizeStarted = nameof(ImmobilizeStarted);
    public const string ImmobilizeEnded = nameof(ImmobilizeEnded);
    public const string GenDefReplaced = nameof(GenDefReplaced);
    public const string RelocationStateChanged = nameof(RelocationStateChanged);
    public const string ObjStorageChanged = nameof(ObjStorageChanged);
    }
}
