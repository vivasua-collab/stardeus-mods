using Game.Utils;

namespace Game.Data {
    // Real-world month/day (no year) window a Def is available in, e.g. Halloween
    // decorations. Attach to Def.Seasonal - null means "not seasonal, always available".
    public class SeasonalLock {
        public int FromMonth;
        public int FromDay;
        public int ToMonth;
        public int ToDay;

        public bool IsInSeason => Seasons.IsInRange(FromMonth, FromDay, ToMonth, ToDay);

        private string rangeText;
        public string RangeText => rangeText ??=
            Seasons.FormatRange(FromMonth, FromDay, ToMonth, ToDay);

        public SeasonalLock Copy() {
            return new SeasonalLock {
                FromMonth = FromMonth, FromDay = FromDay, ToMonth = ToMonth, ToDay = ToDay,
            };
        }
    }
}
