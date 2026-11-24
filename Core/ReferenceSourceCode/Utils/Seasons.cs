#if !PRODUCTION_BUILD
// #define FORCE_SEASONAL_CONTENT
#endif
using System;
using System.Globalization;

namespace Game.Utils {
    // Cheap, non-caching real-world month/day (no year) range checks used to gate
    // seasonal content (e.g. Halloween decorations). See Def.Seasonal / SeasonalLock.
    public static class Seasons {
        public static bool IsInRange(int fromMonth, int fromDay, int toMonth, int toDay) {
            var now = DateTime.Now;
            var cur = now.Month * 100 + now.Day;
            var from = fromMonth * 100 + fromDay;
            var to = toMonth * 100 + toDay;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                return true;
            }
            #endif
            #if FORCE_SEASONAL_CONTENT
            return true;
            #else
            return from <= to ? cur >= from && cur <= to : cur >= from || cur <= to;
            #endif
        }

        public static string FormatRange(int fromMonth, int fromDay, int toMonth, int toDay) {
            return $"{FormatDate(fromMonth, fromDay)} - {FormatDate(toMonth, toDay)}";
        }

        private static string FormatDate(int month, int day) {
            return new DateTime(2000, month, day).ToString("MMMM d", CultureInfo.CurrentUICulture);
        }
    }
}
