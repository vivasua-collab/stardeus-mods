using System;
using System.Collections.Generic;
using Game.Data;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.UI;
using Game.Components;
using Game.Achievements;

namespace Game.Constants {
    [Serializable]
    public class TerraformType {
        public static readonly Dictionary<string, TerraformType> ById = new();
        public static readonly List<TerraformType> All = new();
        private static bool hasLoaded;
        private static bool hasRegisteredAchievements;
        public string Id;
        public bool IsCoreGame;
        public string IconId;
        public float DurationDays;
        public float MaterialConsumptionPerDay;
        public string AchievementId => $"Terraform{Id}";
        private string nameT;
        public string NameT => nameT ??= $"terraform.type.{Id}".T();
        //private string descT;
        //public string DescT => descT ??= $"terraform.type.{Id}.desc".T();

        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            ById.Clear();
            All.Clear();
            hasLoaded = false;
            hasRegisteredAchievements = false;
        }

        public static int TypeCount {
            get {
                return All.Count;
            }
        }

        public static void LoadKnown() {
            if (hasLoaded) { return; }
            hasLoaded = true;

            var types = The.ModLoader.LoadObjects<TerraformType>("Config/Terraform");
            foreach (var s in types) {
                AddType(s);
            }
        }

        private static void AddType(TerraformType s) {
            ById[s.Id] = s;
            All.AddUniqueInefficient(s);
        }

        public static TerraformType Get(string type) {
            if (type == null) {
                D.Err("Getting terraform type by id null");
                return null;
            }
            var nType = ById.Get(type, null);
            if (nType == null) {
                D.Err("Terraform Type {0} is not defined! See Config/Terraform folder", type);
            }
            return nType;
        }

        public override string ToString() {
            return $"TerraformType [{Id}]";
        }

        internal static void RegisterAchievements() {
            if (hasRegisteredAchievements) { return; }
            LoadKnown();
            foreach (var tt in All) {
                AchievementTracker.Register(new AchievementTerraform(tt));
            }
            hasRegisteredAchievements = true;
        }
    }
}
