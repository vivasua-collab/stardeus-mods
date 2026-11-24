using System;
using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class OverlayId {
        public const string Codex = nameof(Codex);
        [Obsolete("Will be removed")]
        public const string Combat = nameof(Combat);
        [Obsolete("Will be removed")]
        public const string Search = nameof(Search);
        public const string Electricity = nameof(Electricity);
        public const string Tasks = nameof(Tasks);
        public const string Areas = nameof(Areas);
        public const string Islands = nameof(Islands);
        public const string AtmoTempRes = nameof(AtmoTempRes);
        public const string AtmoO2Res = nameof(AtmoO2Res);
        public const string AtmoO2 = nameof(AtmoO2);
        public const string AtmoTemp = nameof(AtmoTemp);
        public const string EventLog = nameof(EventLog);
        public const string NoBuildZone = nameof(NoBuildZone);
        public const string ShipNav = nameof(ShipNav);
        public const string Expedition = nameof(Expedition);
        public const string Copters = nameof(Copters);
        public const string Plan = nameof(Plan);
        public const string Path = nameof(Path);
        public const string Tint = nameof(Tint);
        public const string Light = nameof(Light);
        public const string Wallet = nameof(Wallet);
        public const string Trade = nameof(Trade);
        public const string Reg = nameof(Reg);
        [Obsolete("Will be removed")]
        public const string Terraform = nameof(Terraform);
        public const string Security = nameof(Security);
        public const string Dev = nameof(Dev);
        public const string BeingGroup = nameof(BeingGroup);
    }
}
