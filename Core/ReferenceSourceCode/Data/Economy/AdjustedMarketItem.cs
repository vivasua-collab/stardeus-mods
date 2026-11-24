namespace Game.Data.Economy {
    public class AdjustedMarketItem {
        public string Id;
        public int IdH;
        public float Value;

        public static AdjustedMarketItem Create(MarketItem mi) {
            return new AdjustedMarketItem() {
                Id = mi.Id,
                IdH = mi.IdH,
                Value = mi.Value,
            };
        }
    }
}
