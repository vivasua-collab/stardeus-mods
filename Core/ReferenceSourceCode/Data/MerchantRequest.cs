using Game.Components;

namespace Game.Data {
    public sealed class MerchantRequest {
        public string MerchantType;
        public long SinceTick;
        public int TicksLeft;
        public long ConfirmedAtTick;
        public int MerchantSeed;

        public static MerchantRequest Load(ComponentData data) {
            var isPresent = data.GetBool("MerchReq.Present", false);
            if (!isPresent) { return null; }
            var r = new MerchantRequest();
            r.MerchantType = data.GetString("MerchReq.Type", null);
            r.SinceTick = data.GetLong("MerchReq.SinceTick", 0L);
            r.TicksLeft = data.GetInt("MerchReq.TicksLeft", 0);
            r.ConfirmedAtTick = data.GetLong("MerchReq.ConfirmedAtTick", 0L);
            r.MerchantSeed = data.GetInt("MerchReq.Seed", 0);
            return r;
        }

        public static void Save(MerchantRequest r, ComponentData data) {
            var isPresent = r != null;
            data.SetBool("MerchReq.Present", isPresent);
            if (!isPresent) { return; }
            data.SetString("MerchReq.Type", r.MerchantType);
            data.SetLong("MerchReq.SinceTick", r.SinceTick);
            data.SetInt("MerchReq.TicksLeft", r.TicksLeft);
            data.SetLong("MerchReq.ConfirmedAtTick", r.ConfirmedAtTick);
            data.SetInt("MerchReq.Seed", r.MerchantSeed);
        }
    }
}
