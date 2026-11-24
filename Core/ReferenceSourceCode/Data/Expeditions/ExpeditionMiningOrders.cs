using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class ExpeditionMiningOrders {
        // A trip back to the ship to dump storage when it's full
        [Key("AutoUnload")] public bool IsAutoUnload;
        // Minimum mining rate (per day)
        [Key("MinRate")] public int MinRate;
        // Should we abort the expedition if someone failed to reach the shuttle?
        [Key("AbortOnMissingCrew")] public bool AbortOnMissingCrew;

        public void SavePrefs(GameState s) {
            s.Prefs.SetInt(Pref.ExpeditionMinRate, MinRate);
            s.Prefs.SetBool(Pref.ExpeditionAutoUnload, IsAutoUnload);
            s.Prefs.SetBool(Pref.ExpeditionAbortOnCrewMissing, AbortOnMissingCrew);
        }

        public static ExpeditionMiningOrders Default(GameState s) {
            return new ExpeditionMiningOrders() {
                MinRate = s.Prefs.GetInt(Pref.ExpeditionMinRate,
                    PrefH.ExpeditionMinRate, 0),
                IsAutoUnload = s.Prefs.GetBool(Pref.ExpeditionAutoUnload,
                    PrefH.ExpeditionAutoUnload, false),
                AbortOnMissingCrew = s.Prefs.GetBool(Pref.ExpeditionAbortOnCrewMissing,
                    PrefH.ExpeditionAbortOnCrewMissing, false),
            };
        }
    }
}
