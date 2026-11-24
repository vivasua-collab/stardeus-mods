using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    /// <summary>
    /// Defines how specific materials get spawned as resource deposits in space
    /// </summary>
    public sealed class DepositConfig : SpaceAssignable {
        [SelfInit]
        private static void Init() {
            All.Clear();
        }
        public static readonly Dictionary<string, DepositConfig> All = new();
        public static void LoadKnown() {
            var all = The.ModLoader.LoadObjects<DepositConfig>("Config/Space/Deposits");
            foreach (var cfg in all) {
                cfg.MaxOnePerSector = true;
                var matType = MatType.Get(cfg.Id);
                if (matType == null) {
                    D.Err("DepositConfig does not have a matching MatType: {0}", cfg.Id);
                    continue;
                }
                if (!All.TryAdd(cfg.Id, cfg))  {
                    D.Err("DepositConfig with id {0} already exists", cfg.Id);
                    continue;
                }
                cfg.IdH = Hashes.S(cfg.Id);
                cfg.Mat = matType;
                matType.Deposit = cfg;
            }
            if (Arrays.IsEmpty(All)) {
                D.Err("Failed to load any Space/Deposits!");
            }
        }
        public MatType Mat;
        /// <summary>
        /// key = region difficulty, value = initial depth
        /// We can use this to make super rare material deposits very difficult to mine at
        /// easier regions but easier to mine at hardcore regions
        /// </summary>
        public float[] InitialDepthChance;
        private Curve initialDepthCurve;
        public Curve InitialDepthCurve => initialDepthCurve ??= InitialDepthChance;
        /// <summary>
        /// key = region difficulty, value = initial difficulty
        /// Add some fluctuation on top or the numbers will be very samey
        /// </summary>
        public float[] InitialDifficultyChance;
        private Curve initialDifficultyCurve;
        public Curve InitialDifficultyCurve => initialDifficultyCurve ??= InitialDifficultyChance;
        /// <summary>
        /// If ScanDifficulty is negative, this deposit is impossible to find via system scanner.
        /// It won't be listed as an option.
        /// </summary>
        public float ScanDifficulty;
        /// <summary>
        /// Extra chance that scanning for this material injects a new deposit into
        /// one of a candidate region's unexplored planet or asteroid sectors, rolled
        /// per region as the scan's outward ring search reaches it (see ScanSearch).
        /// Multiplied by the material's organic chance to appear in that sector.
        /// 0 disables the feature for this deposit.
        /// </summary>
        public float NearbySpawnChance;
        public bool IsWithDepth;
        /// <summary>
        /// Common deposits can be spawned by doing an additional generation pass
        /// in order to enrich the resource quantity
        /// </summary>
        public bool IsCommon;
        public string CollectKey;
        private string collectT;
        public string CollectT => collectT ??= CollectKey.T();
        private string mineTextTemplate;
        private string mineShuttleTextTemplate;
        private string expTemplate;
        public float MiningDifficulty;
        public string MineText(float rate, string depth) {
            mineTextTemplate ??= $"deposit.{CollectKey}".T();
            return mineTextTemplate.F(Mat.NameT, rate, depth);
        }
        public string MineShuttleText(float rate, string storage) {
            mineShuttleTextTemplate ??= $"deposit.shuttle.{CollectKey}".T();
            return mineShuttleTextTemplate.F(Mat.NameT, rate, storage);
        }

        public string ExpeditionStateText(SpaceObject so) {
            expTemplate ??= $"expedition.verb.{CollectKey}".T();
            return expTemplate.F(Texts.LinkToStarmap(so, Mat.NameT));
        }

        public override string ToString() {
            return $"DepositConfig[{Mat.Id}]";
        }

        public static DepositConfig Get(string id) {
            if (All.TryGetValue(id, out var cfg)) {
                return cfg;
            }
            D.Err("DepositConfig not available for id: {0}", id);
            return null;
        }

    }
}
