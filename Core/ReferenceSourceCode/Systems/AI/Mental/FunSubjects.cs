using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Mental {
    // The one place that decides what counts as "the same" fun subject. A being admires a
    // plant, not the vase it grows in, and every window shows the same space.
    public static class FunSubjects {
        private sealed class SubjectGroup {
            public string Id;
            public Def RepresentativeDef;
            private string nameT;
            // the group id is the translation key of its name, so groups can reuse existing texts
            public string NameT => nameT ??= Id.T();
        }

        private static Dictionary<int, SubjectGroup> groups;

        [ExecuteOnReload]
        private static void ClearStatics() {
            groups = null;
        }

        public static FunSource ForDevice(Entity device) {
            if (device?.Definition == null) { return FunSource.None; }
            return new FunSource(FunActivity.UseDevice, device.Definition.IdH);
        }

        public static FunSource ForWorkout(Entity device) {
            if (device?.Definition == null) { return FunSource.None; }
            return new FunSource(FunActivity.Workout, device.Definition.IdH);
        }

        public static FunSource ForAdmirable(AdmirableComp admirable) {
            if (admirable == null) { return FunSource.None; }
            if (admirable.IsWindow) {
                return FunSource.View;
            }
            var planter = admirable.Planter;
            if (planter != null && planter.IsConfigured) {
                return new FunSource(FunActivity.Admire, planter.PlantDefIdH);
            }
            if (admirable.FunSubjectIdH != 0) {
                return new FunSource(FunActivity.Admire, admirable.FunSubjectIdH);
            }
            var def = admirable.Entity?.Definition;
            if (def == null) { return FunSource.None; }
            return new FunSource(FunActivity.Admire, def.IdH);
        }

        public static FunSource ForPet(Species species) {
            if (species == null) { return FunSource.None; }
            return new FunSource(FunActivity.Pet, species.IdH);
        }

        public static FunSource ForRide(Species species) {
            if (species == null) { return FunSource.None; }
            return new FunSource(FunActivity.RideBot, species.IdH);
        }

        // Saves only keep subject hashes, so a subject can stop resolving when the mod that
        // added it is removed
        public static bool IsKnown(FunSource src) {
            switch (src.Activity) {
                case FunActivity.UseDevice:
                case FunActivity.Workout:
                    return The.Defs.FastGet(src.SubjectIdH) != null;
                case FunActivity.Admire:
                    return TryGroup(src.SubjectIdH, out _)
                        || The.Defs.FastGet(src.SubjectIdH) != null;
                case FunActivity.AdmireView:
                    return true;
                case FunActivity.Pet:
                case FunActivity.RideBot:
                    return Species.Known.ContainsKey(src.SubjectIdH);
                default:
                    return false;
            }
        }

        public static string DisplayName(FunSource src) {
            switch (src.Activity) {
                case FunActivity.UseDevice:
                case FunActivity.Workout:
                    return The.Defs.FastGet(src.SubjectIdH)?.NameT ?? T.Unknown;
                case FunActivity.Admire:
                    return T.ActAdmiringObject_V1.F(SubjectName(src.SubjectIdH));
                case FunActivity.AdmireView:
                    return T.VerbAdmiringView;
                case FunActivity.Pet:
                    return T.FunSourcePetting_V1.F(SpeciesName(src.SubjectIdH));
                case FunActivity.RideBot:
                    return T.RidingABot;
                default:
                    return T.Unknown;
            }
        }

        public static string Icon(FunSource src) {
            switch (src.Activity) {
                case FunActivity.UseDevice:
                case FunActivity.Workout:
                case FunActivity.Admire:
                    return CodexDef(src)?.Preview ?? IconId.CMagnifier;
                case FunActivity.AdmireView:
                    return IconId.CBinoculars;
                case FunActivity.Pet:
                    return IconId.CPaw;
                case FunActivity.RideBot:
                    return IconId.CJoystick;
                default:
                    return IconId.CMoodGood;
            }
        }

        public static Def CodexDef(FunSource src) {
            switch (src.Activity) {
                case FunActivity.UseDevice:
                case FunActivity.Workout:
                    return The.Defs.FastGet(src.SubjectIdH);
                case FunActivity.Admire:
                    if (TryGroup(src.SubjectIdH, out var group)) {
                        return group.RepresentativeDef;
                    }
                    return The.Defs.FastGet(src.SubjectIdH);
                case FunActivity.Pet:
                case FunActivity.RideBot:
                    return Species.Known.TryGetValue(src.SubjectIdH, out var sp) ? sp.Def : null;
                default:
                    return null;
            }
        }

        private static string SubjectName(int subjectIdH) {
            if (TryGroup(subjectIdH, out var group)) {
                return group.NameT;
            }
            return The.Defs.FastGet(subjectIdH)?.NameT ?? T.Unknown;
        }

        private static string SpeciesName(int speciesIdH) {
            return Species.Known.TryGetValue(speciesIdH, out var sp) ? sp.Name : T.Unknown;
        }

        private static bool TryGroup(int subjectIdH, out SubjectGroup group) {
            EnsureGroups();
            return groups.TryGetValue(subjectIdH, out group);
        }

        // Groups are data driven (Admirable "FunSubject" property), so they are discovered
        // from defs once, the same way PlanterComp discovers available plants.
        private static void EnsureGroups() {
            if (groups != null) { return; }
            groups = new Dictionary<int, SubjectGroup>();
            foreach (var def in The.Defs.Defs.Values) {
                if (def.IsAbstract) { continue; }
                var conf = def.ComponentConfigFor(ComponentIdH.Admirable, false);
                if (conf == null) { continue; }
                var groupId = conf.GetString(PropertyIdH.FunSubject, null);
                if (string.IsNullOrWhiteSpace(groupId)) { continue; }
                var idH = Hashes.S(groupId);
                if (!groups.ContainsKey(idH)) {
                    groups.Add(idH, new SubjectGroup() {
                        Id = groupId,
                        RepresentativeDef = def,
                    });
                }
            }
        }
    }
}
