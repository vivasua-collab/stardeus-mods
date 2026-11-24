using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class ShipDeviceId {
        public const string Shield_ShieldEmitter = nameof(Shield_ShieldEmitter);
        public const string Engine_Thruster = nameof(Engine_Thruster);
        public const string Capacitor_FluxCapacitor = nameof(Capacitor_FluxCapacitor);
        public const string Vent_FluxVent = nameof(Vent_FluxVent);
    }
}
