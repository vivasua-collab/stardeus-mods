#if !PRODUCTION_BUILD
// #define DEEP_SCAN_DEBUG
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Suggestions;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Space;
using System.Linq;
using Game.Procgen.Space;
using Game.Input;
using Game.Systems.UIUX;
using Game.Systems.Notify;

namespace Game.Systems {
    public enum ScanMode {
        None,
        Mat,
        Anomaly,
        DerelictShip,
        SpaceStation,
        /// <summary>
        /// Never use this, it is now automatic and passive
        /// </summary>
        Deep,
        HyperjumpRelay,
    }
    public sealed class ScanSys : GameSystem, ISaveableSpecial {
        public const string SysId = "ScanSys";

        public const string DeepScanCountKey = "DeepScans";
        public const string DeepFocusFindsKey = "DeepFocusFinds";

        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ScanSys());
        }

        private ScanSysData data;
        public readonly B<ScanMode> Mode = new(ScanMode.None);
        public readonly B<MatType> MatTarget = new(null);
        public readonly B<MatType> DeepFocus = new(null);
        // Focused deep scan misses since the last hit, colony-wide so bad luck carries over to
        // the next sector instead of resetting when the ship moves
        public int DeepFocusMisses;
        private MatType lastFocusMiss;
        public readonly BFloat ScanProgress = new();
        public readonly BFloat ScanDeepProgress = new();
        public long ScanStartTick;
        public long ScanDeepStartTick;
        public float ScanDifficulty;
        public float ScanDeepDifficulty;
        public int ScanStrength;
        private float scanProgressRate;
        public SpaceSector FoundSector;
        public readonly B<SpaceObject> FoundSO = new(null);
        public SpaceObject FoundDeepSO;
        private ScanSearch search;

        public string ScanTime => Units.TicksToTime(
            S.Clock.Ticks - ScanStartTick, true);
        public string ScanDeepTime => Units.TicksToTime(
            S.Clock.Ticks - ScanDeepStartTick, true);


        private readonly List<RadarComp> radars = new();

        public void AddRadar(RadarComp radar) {
            radars.Add(radar);
        }

        public void RemoveRadar(RadarComp radar) {
            radars.Remove(radar);
        }

        public override void Unload() {
            isRadarStateKnown = false;
            wasRadarActive = false;
        }

        public string GetName() {
            return Id;
        }

        public void SetMode(ScanMode mode, MatType scanTarget) {
            FoundSector = null;
            FoundSO.Set(null);
            Mode.Set(mode);
            ScanProgress.Set(0);
            MatTarget.Set(scanTarget);
            ScanStartTick = S.Clock.Ticks;
            ScanDifficulty = mode switch {
                ScanMode.Mat => scanTarget.Deposit.ScanDifficulty * Rng.URange(0.8f, 1.2f),
                ScanMode.Anomaly => Rng.URange(3f, 7f),
                _ => 1f,
            };
            if (ScanDifficulty < 0.001f) {
                ScanDifficulty = 1f;
            }
            ScanDifficulty *= S.Difficulty.Level;
        }

        // Exotic materials (rarity 4 - Dark Matter and the terraforming ones) stay out of reach
        // of a focused deep scan: their scarcity is the point
        public const int MaxDeepFocusRarity = 3;

        // Same rules as the long-range material scan (scanning research unlocks rarer
        // materials), plus the rarity 4 exclusion. Null means no focus and is always allowed.
        public bool CanDeepFocus(MatType mat) {
            if (mat == null) { return true; }
            if (mat.Deposit == null || mat.Deposit.ScanDifficulty <= 0f) { return false; }
            return mat.MatRarity <= MaxDeepFocusRarity && mat.MatRarity <= MaxScannableRarity();
        }

        // Switching the focus restarts a running deep scan, so it can't be swapped in just
        // before the scan completes
        public void SetDeepFocus(MatType mat) {
            if (!CanDeepFocus(mat)) { return; }
            if (DeepFocus.Value == mat) { return; }
            DeepFocus.Set(mat);
            if (ScanDeepProgress > 0f) {
                ScanDeepProgress.Set(0f);
            }
        }

        private Curve deepScanDiffCurve;

        /// <summary>
        /// Rampup Curve: https://www.desmos.com/calculator/e5892kvx6d
        /// </summary>
        private void UpdateDeepScanDifficulty() {
            var so = S.Universe.Player.Parent;
            deepScanDiffCurve ??= Tunables.DeepScanSectorDifficultyCurve;
            ScanDeepDifficulty = deepScanDiffCurve.Evaluate(SOUtils.SectorDifficultyOf(so, null));
            if (so.HasData && so.Data.TryGetInt(DeepScanCountKey, out int scans)) {
                ScanDeepDifficulty += Mathf.Sqrt(ScanDeepDifficulty * scans);
            }
            if (DeepFocus.Value != null && !IsFocusUsedUp(so)) {
                ScanDeepDifficulty *= Tunables.DeepScanFocusDifficultyMul;
            }
        }

        public bool IsDeepScanAvailable(out int maxDeposits) {
            var so = S.Universe.Player.Parent;
            maxDeposits = 0;
            if (!so.HasChildren || SOUtils.IsNuked(so)) { return false; }
            var sec = so.Sector;
            if (sec?.Biome.Template is not SectorTemplate secTpl) {
                return false;
            }
            if (Arrays.IsEmpty(secTpl.NumDepositsChance)) {
                // this could be a space event that spawned asteroids
                bool hasSpawnTarget = false;
                foreach (var ch in so.Children) {
                    if (CanHostResource(ch)) {
                        hasSpawnTarget = true;
                        break;
                    }
                }
                if (!hasSpawnTarget) {
                    maxDeposits = 0;
                } else {
                    maxDeposits = 5;
                }
            } else {
                maxDeposits = Mathf.CeilToInt(secTpl.NumDepositsCurve.Evaluate(1f));
            }
            if (maxDeposits < 1) { return false; }
            return true;
        }

        private bool CheckCanScanDeep() {
            if (!IsDeepScanAvailable(out var maxDeposits)) {
                return false;
            }
            if (S.Vars.IsShowingCutscene) { return false; }
            var so = S.Universe.Player.Parent;
            var sec = so.Sector;
            int curDeposits = 0;
            if (sec.Biome.Deposits != null) {
                foreach (var dep in sec.Biome.Deposits) {
                    curDeposits += dep.Value;
                }
            }
            if (curDeposits >= maxDeposits) {
                return false;
            }
            if (so.Data.TryGetInt(DeepScanCountKey, out int scans)) {
                var rng = new Rng(Hashes.Quick(so.Seed, Primes.Prime004));
                var from = S.Difficulty.MinDeepScansPerLocation;
                var to = S.Difficulty.MaxDeepScansPerLocation;
                var maxScans = rng.Range(from, to + 1);
                if (scans >= maxScans) {
                    return false;
                }
            }
            return true;
        }

        private void OnLoadSave(GameState s) {
            RestoreData();
            var scanDays = Tunables.BaseScanDurationDays;
            scanProgressRate = 1f / Consts.TicksPerDay / scanDays;
            search = new ScanSearch(s);
            s.Clock.OnTick.AddListener(OnTick);
            s.Sig.EntityAdded.AddListener(OnEntityAdded);
        }

        private void OnEntityAdded(Entity entity) {
            if (The.Toolbox.ActiveTool != The.Toolbox.BuildTool) {
                return;
            }
            if (!entity.HasComponent<RadarComp>()) { return; }
            // Relocating moves existing strength, it never adds any
            if (IsRelocationTarget(entity)) { return; }
            var allRadars = S.Components.AllByType<RadarComp>();
            var str = 0;
            var isNewEntityNotBuilt = !entity.GetComponent<ConstructableComp>().IsConstructed;
            foreach (RadarComp radar in allRadars) {
                if (!CountsTowardsScanLimit(radar, entity, isNewEntityNotBuilt)) { continue; }
                str += radar.ScanStrength;
            }
            if (str < Tunables.MaxScanStrength) {
                return;
            }
            // The new unbuilt device only stays out of the check above; the warning
            // itself reports the strength with it, or it reads one device short
            if (isNewEntityNotBuilt) {
                str += entity.GetComponent<RadarComp>().ScanStrength;
            }

            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(
                T.ScanStrengthLimitTip_V3.F(
                    Texts.Accented(T.NounScanStrength),
                    Texts.Accented(str),
                    Texts.Accented(Tunables.MaxScanStrength)));
            sb.Append(GlyphMap.BRHR);

            foreach (RadarComp radar in allRadars) {
                if (!CountsTowardsScanLimit(radar, entity, false)) { continue; }
                sb.AppendFormat("\n {0} +{1} {2} ",
                    radar.Tile.NameLink, radar.ScanStrength,
                        GlyphMap.ArrowRight);
                if (radar.IsConstructed) {
                    if (!radar.Tile.ENode.IsConsuming) {
                        sb.Append(Texts.Yellow(T.Unpowered));
                    } else {
                        sb.Append(Texts.Green(T.Active));
                    }
                } else {
                    sb.Append(Texts.Grey(T.ConstructionPending));
                }
            }
            WPopup.Warning(T.Warning, sb.ToString());
            sb.Clear();
        }

        private static bool IsRelocationTarget(Entity entity) {
            return entity.TryGetComponent<ReinstallableComp>(out var re) && re.SourceId != 0;
        }

        // Shared by the sum and the listing so the warning never lists more devices than it
        // adds up. Relocation sources still scan until moved, their targets are the same
        // device counted twice.
        private static bool CountsTowardsScanLimit(RadarComp radar, Entity added,
                bool isAddedNotBuilt) {
            if (!radar.Entity.IsActive) { return false; }
            if (IsRelocationTarget(radar.Entity)) { return false; }
            // prevent unnecessary nagging about exceeding the limit after
            // ordering a system scanner with current strength of 9
            if (isAddedNotBuilt && radar.Tile == added) { return false; }
            return true;
        }

        private void RestoreData() {
            if (data == null) { return; }
            ScanProgress.SetSilent(data.ScanProgress);
            ScanDeepProgress.SetSilent(data.ScanDeepProgress);
            ScanStartTick = data.StartTick;
            ScanDeepStartTick = data.DeepStartTick;
            ScanDifficulty = data.ScanDifficulty;
            ScanDeepDifficulty = data.ScanDeepDifficulty;
            if (data.FoundSOId != 0) {
                if (S.Universe.TryFind(data.FoundSOId, out var so)) {
                    FoundSO.SetSilent(so);
                    FoundSector = so.Sector;
                } else {
                    D.Warn("Couldn't find SO by id when restoring scan sys data: {0}",
                        data.FoundSOId);
                }
            }
            if (data.FoundDeepSOId != 0) {
                if (!S.Universe.TryFind(data.FoundDeepSOId, out FoundDeepSO)) {
                    D.Warn("Couldn't find Deep SO by id when restoring scan sys data: {0}",
                        data.FoundDeepSOId);
                }
            }
            if (ScanDifficulty < 0.00001f) {
                ScanDifficulty = 1f;
            }
            if (ScanDeepDifficulty < 0.00001f) {
                ScanDeepDifficulty = 1f;
            }
            if (data.TargetMat != null) {
                MatTarget.SetSilent(MatType.Get(data.TargetMat));
            }
            if (data.DeepFocusMat != null) {
                DeepFocus.SetSilent(MatType.Get(data.DeepFocusMat));
            }
            DeepFocusMisses = data.DeepFocusMisses;
            Mode.SetSilent(data.Mode);
            // legacy fix
            if (Mode.Value == ScanMode.Deep) {
                Mode.SetSilent(ScanMode.None);
                ScanProgress.SetSilent(0f);
                ScanDeepProgress.SetSilent(1f);
                MatTarget.SetSilent(null);
                FoundDeepSO = FoundSO.Value;
                FoundSector = null;
                FoundSO.Set(null);
            }
            data = null;
        }

        private bool isAdvScanGoalSent;

        private void CheckScanGoals() {
            // don't suggest anything while still scanning
            if (Mode.Value != ScanMode.None && ScanProgress < 1f) {
                return;
            }
            if (!isAdvScanGoalSent) {
                if (G.Ticks % Consts.TicksPer15Minutes == 13) {
                    if (S.BB.Get(BBKeyH.SystemScanAdvanced, false) && S.Sys.Sec.IsRadarActive()) {
                        if (S.Query.CanAddSuggestionNow.Ask()) {
                            isAdvScanGoalSent = true;
                            if (G.Ticks > Consts.TicksPer3Days) {
                                S.Sig.AddSuggestion.Send(SuggestionScanAdvanced.SuggId);
                            }
                        }
                    }
                }
            }
        }

        private void OnTick(long ticks) {
            if (A.IsInHyperspace) { return; }
            CheckScanGoals();
            UpdateScanStrength();
            ScanDeep();
            ScanLongRange();
        }

        private void ScanLongRange() {
            if (ScanProgress >= 1f) { return; }
            if (Mode.Value == ScanMode.None) { return; }
            AddScanProgress();
        }

        private void UpdateScanStrength() {
            ScanStrength = 0;
            for (int i = radars.Count - 1; i >= 0; i--) {
                RadarComp radar = radars[i];
                if (radar.IsRadarActive) {
                    ScanStrength += radar.ScanStrength;
                }
            }
            if (ScanStrength > Tunables.MaxScanStrength) {
                ScanStrength = Tunables.MaxScanStrength;
            }
            if (ScanStrength == 0 && A.InstantBuild) {
                ScanStrength = 1;
            }
            CheckRadarActivated();
        }

        // Arriving in a sector with a working radar scans it (SOUtils.ScanWithRadar), but a
        // radar built after arrival - the first one of every game - never did, so the deposits
        // right next to the ship stayed Unknown. The first tick after a load only records the
        // state, so loading never counts as an activation.
        private bool isRadarStateKnown;
        private bool wasRadarActive;

        private void CheckRadarActivated() {
            var isActive = ScanStrength > 0;
            var activated = isRadarStateKnown && isActive && !wasRadarActive;
            isRadarStateKnown = true;
            wasRadarActive = isActive;
            if (!activated) { return; }
            var so = S.Universe.Player.Parent;
            if (so == null || so.Sector == null || !NeedsLocalScan(so)) { return; }
            int unknownBefore = CountUnknownDeposits(so);
            SOUtils.ScanWithRadar(S, so, true, rebuildStarmap: true);
            int found = unknownBefore - CountUnknownDeposits(so);
            if (found > 0) {
                var message = T.ScanRadarLocalDone_V1.F(Texts.Accented(found));
                S.Sys.Log.AddLine(message);
                S.Sig.ShowFanfare.Send(message);
            }
        }

        private static bool NeedsLocalScan(SpaceObject so) {
            if (!so.Knowledge.IsFullyScanned) { return true; }
            if (!so.HasChildren) { return false; }
            foreach (var child in so.Children) {
                if (!child.Knowledge.IsFullyScanned) { return true; }
            }
            return false;
        }

        private static int CountUnknownDeposits(SpaceObject so) {
            if (!so.HasChildren) { return 0; }
            int count = 0;
            foreach (var child in so.Children) {
                if (child.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                    count++;
                }
            }
            return count;
        }

        private void ScanDeep() {
            if (ScanDeepProgress == 0f) {
                if (CheckCanScanDeep()) {
                    ScanDeepStartTick = G.Ticks;
                    UpdateDeepScanDifficulty();
                } else {
                    // no longer deep scanning this location
                    ScanDeepProgress.Set(-1f);
                    ScanDeepStartTick = 0;
                }
            }
            if (ScanDeepProgress >= 0f) {
                AddScanDeepProgress();
            }
        }

        private void AddScanProgress() {
            ScanProgress.Add(ScanStrength * scanProgressRate / ScanDifficulty);
            if (ScanProgress > 1f) {
                OnScanDone();
            }
        }
        private void AddScanDeepProgress() {
            ScanDeepProgress.Add(
                ScanStrength * scanProgressRate / ScanDeepDifficulty);
            #if DEEP_SCAN_DEBUG
            ScanDeepProgress += 0.1f;
            #endif
            if (ScanDeepProgress > 1f) {
                OnScanDeep();
            }
        }

        private void OnScanDone() {
            //Mode = ScanMode.None;
            //ScanProgress = 0;
            if (A.IsInHyperspace) {
                D.Err("Trying to OnScanMaterial while in hyperspace");
                ScanProgress.Set(0.99f);
                return;
            }
            ScanProgress.Set(1f);
            switch (Mode.Value) {
                case ScanMode.Mat: OnScanMaterial(); break;
                case ScanMode.Anomaly: OnScanAnomaly(); break;
                case ScanMode.SpaceStation: OnScanSpaceStation(); break;
                case ScanMode.DerelictShip: OnScanDerelictShip(); break;
                case ScanMode.HyperjumpRelay: OnScanRelay(); break;
            }
        }

        private void OnScanMaterial() {
            if (MatTarget.Value == null) {
                D.Err("Null MatTarget when ScanMode is Mat");
                return;
            }

            var found = search.FindMat(MatTarget.Value);

            if (found == null) {
                NotifyScannerCouldNotFind();
            } else {
                found.SO.Knowledge.DiscoverMat(MatTarget.Value);
                OnMatScanSuccess(found);
            }
        }

        private void OnScanDeep() {
            var so = S.Universe.Player.Parent;
            var data = so.Data;
            if (!data.TryGetInt(DeepScanCountKey, out int scans)) {
                scans = 0;
            }
            data.SetInt(DeepScanCountKey, scans + 1);
            lastFocusMiss = null;
            var found = SpawnDeepScanResult(so, scans + 1);
            ScanDeepStartTick = 0;
            if (!CheckCanScanDeep()) {
                ScanDeepProgress.Set(-1f);
            } else {
                ScanDeepProgress.Set(0f);
            }
            if (found.Item1 != null) {
                greyDeep ??= Texts.Grey($"({T.ScanTargetDeep})");
                var message = T.ScanFound_V1.F(Texts.LinkToStarmap(found.Item1, found.Item2.NameT));
                message = $"{message} {greyDeep}";
                FoundDeepSO = found.Item1;
                FoundDeepSO.UI?.Reload();
                ShowFoundDeepEvent(message, FocusMissLine());
            } else {
                ScanDeepProgress.Set(-1f);
                var missLine = FocusMissLine();
                if (missLine != null) {
                    missLine = T.ScanReport_V1.F(Texts.Yellow(missLine));
                    S.Sys.Log.AddLine(missLine);
                    S.Sig.ShowFanfare.Send(missLine);
                }
            }
        }

        private string FocusMissLine() {
            return lastFocusMiss == null ? null : T.ScanDeepFocusMiss_V1.F(lastFocusMiss.NameT);
        }

        private string greyDeep;

        private Curve deepScanExtraDiffCurve;

        private (SpaceObject, MatType) SpawnDeepScanResult(SpaceObject so, int numScan) {
            if (!so.HasChildren || SOUtils.IsNuked(so)) {
                return default;
            }
            var rng = new Rng(Hashes.Quick(so.Seed, Primes.Prime001 * numScan));
            var common = new List<DepositConfig>();
            bool allowUncommon = rng.Chance(0.05f);
            foreach (var cfg in DepositConfig.All) {
                if (allowUncommon || cfg.Value.IsCommon) {
                    common.Add(cfg.Value);
                }
            }
            var sec = so.Sector;
            if (sec?.Biome.Template is not SectorTemplate secTpl) {
                D.Err("Not a sector! {0}", sec);
                return default;
            }
            int maxDeposits;
            if (Arrays.IsEmpty(secTpl.NumDepositsChance)) {
                // this could be a space event that spawned asteroids
                bool hasSpawnTarget = false;
                foreach (var ch in so.Children) {
                    if (CanHostResource(ch)) {
                        hasSpawnTarget = true;
                        break;
                    }
                }
                if (!hasSpawnTarget) {
                    maxDeposits = 0;
                } else {
                    maxDeposits = 5;
                }
            } else {
                maxDeposits = Mathf.CeilToInt(secTpl.NumDepositsCurve.Evaluate(1f));
            }
            if (maxDeposits < 1) { return default; }
            int curDeposits = 0;
            if (sec.Biome.Deposits != null) {
                foreach (var dep in sec.Biome.Deposits) {
                    curDeposits += dep.Value;
                }
            }
            if (curDeposits >= maxDeposits) {
                    D.Warn("Don't want more deposits here! {0}", curDeposits);
                return default;
            }
            int attempts = 10;
            MatType chosenMat = null;
            deepScanExtraDiffCurve ??= Tunables.DeepScanExtraDifficultyChance;
            var extraDiffChance = rng.Curve(deepScanExtraDiffCurve);
            chosenMat = TryFocusedDeepScanPick(so, sec, numScan);
            while (chosenMat == null && attempts-- > 0) {
                Arrays.Shuffle(rng, common);
                for (int i = common.Count - 1; i >= 0; i--) {
                    DepositConfig dep = common[i];
                    if (dep.Matches(sec, out float diffChance, extraDiffChance)) {
                        // higher chance of rare shit!
                        if (rng.Chance(diffChance + 0.2f)) {
                            chosenMat = dep.Mat;
                            so.SpaceEntity.Biome.AddAssignable(dep, checkDifficulty: false);
                            break;
                        }
                    }
                }
            }
            if (chosenMat == null) {
                return default;
            }
            // The random pick runs as if there was no focus, so it can land on the focus right
            // after the focus roll missed. The player got what they asked for: a hit for the
            // misses, but not a focused find for the sector cap, since an unfocused scan would
            // have found it too.
            if (chosenMat == lastFocusMiss) {
                lastFocusMiss = null;
                DeepFocusMisses = 0;
            }
            so.Mats ??= new();
            so.Mats.Add(chosenMat);

            var depositType = SpaceObjectType.Find(SpaceObjectTypeIdH.Resource);
            var gen = GenUniverse.FindGenerator(depositType.Id);
            var child = gen.Generate(S.Universe, UniverseLayer.SectorContents,
                depositType, so);
            so.AddChild(child);
            FindLocationForNewResource(child, so, rng, 10);
            //child is created and positioned at this point
            // D.Warn("Spawning deep scan result: {0}", child);
            if (so.Faction != null && rng.Chance(0.75f)) {
                child.SetFaction(so.Faction);
            }
            S.Sys.Planets.AssignMaterialToPOI(child, chosenMat);
            SOActionScan.ScanResource(child, out var matType);
            // S.Sig.RebuildStarmap.Send(false, false);
            S.Sig.SpaceObjectAdded.Send(child);
            if (matType != chosenMat) {
                D.Err("Mismatch of materials! {0} | {1} | {2}",
                    child, matType, chosenMat);
            }
            return (child, chosenMat);
        }

        // Rolled on its own RNG so that unfocused deep scans pick exactly what they did before
        // focus existed. Only fair attempts roll: when the focus cannot form here or the sector
        // is used up, nothing is rolled and no miss is counted, so misses can't be banked in
        // sectors where a hit was never possible.
        private MatType TryFocusedDeepScanPick(SpaceObject so, SpaceSector sec, int numScan) {
            if (!TryGetFocusChance(so, sec, out float chance)) { return null; }
            var focus = DeepFocus.Value;
            var rng = new Rng(Hashes.Quick(so.Seed, Primes.Prime017 * numScan));
            if (!rng.Chance(chance)) {
                DeepFocusMisses++;
                lastFocusMiss = focus;
                return null;
            }
            DeepFocusMisses = 0;
            so.SpaceEntity.Biome.AddAssignable(focus.Deposit, checkDifficulty: false);
            so.Data.TryGetInt(DeepFocusFindsKey, out int finds);
            so.Data.SetInt(DeepFocusFindsKey, finds + 1);
            return focus;
        }

        // Chance that the next focused deep scan in the ship's sector finds the focus. The roll
        // and the tooltip both read this, so what the player sees is what gets rolled.
        public bool TryGetFocusChance(out float chance) {
            chance = 0f;
            var so = S.Universe.Player.Parent;
            var sec = so?.Sector;
            return sec != null && TryGetFocusChance(so, sec, out chance);
        }

        // Rising chance: every miss adds the base chance again, so the worst streak is
        // ceil(1 / base) - 1 misses while the average stays close to the base
        private bool TryGetFocusChance(SpaceObject so, SpaceSector sec, out float chance) {
            chance = 0f;
            var focus = DeepFocus.Value;
            var dep = focus?.Deposit;
            if (dep == null || !CanDeepFocus(focus)) { return false; }
            if (IsFocusUsedUp(so)) { return false; }
            if (!CanFocusFormIn(dep, sec, out float diffChance)) { return false; }
            var baseChance = CurveDef.FastGet(CurveIdH.Scanning_DeepScanFocusChance)
                .Evaluate(diffChance);
            chance = Mathf.Min(1f, baseChance * (DeepFocusMisses + 1));
            return true;
        }

        // Caps focused finds per sector so the player can't camp in one sector and target
        // deposit after deposit - targeting more means travelling
        public bool IsFocusUsedUp(SpaceObject so) {
            if (so == null || !so.HasData) { return false; }
            return so.Data.TryGetInt(DeepFocusFindsKey, out int finds)
                && finds >= Tunables.DeepScanFocusMaxPerSector;
        }

        // Whether the focus material can form in the player's sector - the answer the deep
        // scan tooltip gives the player
        public bool CanFocusFormHere(MatType mat) {
            var dep = mat?.Deposit;
            if (dep == null) { return false; }
            var sec = S.Universe.Player.Parent.Sector;
            if (sec == null) { return false; }
            return CanFocusFormIn(dep, sec, out _);
        }

        // Unlike the random pick, a focus gets no extra difficulty roll: it must be able to form
        // at the sector's own difficulty, so Uranium can never be pulled out of an easy sector
        private static bool CanFocusFormIn(DepositConfig dep, SpaceSector sec,
                out float diffChance) {
            if (sec.Biome.Difficulty < dep.MinDifficulty) {
                diffChance = 0f;
                return false;
            }
            return dep.Matches(sec, out diffChance);
        }

        // Same research gate as the long-range material scan list
        public int MaxScannableRarity() {
            if (S.BB.Get(BBKeyH.SystemScanExtended, false)) {
                return 4;
            }
            if (S.BB.Get(BBKeyH.SystemScanAdvanced, false)) {
                return 3;
            }
            return 1;
        }

        public bool CanHostResource(SpaceObject so) {
            if (so.Type.IdH == SpaceObjectTypeIdH.PlanetCloseup) {
                return true;
            }
            if (so.Type.IdH == SpaceObjectTypeIdH.Moon) {
                return true;
            }
            if (so.Type.IdH == SpaceObjectTypeIdH.Asteroid) {
                return true;
            }
            return false;
        }

        private bool CanSpawnResourceAt(Vector2 loc, SpaceObject me, SpaceObject so) {
            foreach (var c in so.Children) {
                if (c == me) { continue; }
                if (c.Type.IsDynamic && Vectors.SqrDistance(loc, c.Location)
                        < GenSpaceObject.MinSurfacePOIDist * GenSpaceObject.MinSurfacePOIDist) {
                    if (c.Type.IdH == SpaceObjectTypeIdH.Player) { continue; }
                    if (c.Type.IdH == SpaceObjectTypeIdH.Shuttle) { continue; }
                    if (c.Type.IdH == SpaceObjectTypeIdH.CargoCopter) { continue; }
                    return false;
                }
            }
            return true;
        }

        public void FindLocationForNewResource(SpaceObject child, SpaceObject so, Rng rng, int majorAttemptsLeft) {
            if (majorAttemptsLeft < 1) {
                D.Err("Out of attempts while trying to find good position for a resource {0}", child);
                return;
            }
            SpaceObject target = null;
            var candidates = new List<SpaceObject>();

            var sortedChildren = so.Children.Where(c => CanHostResource(c))
                .OrderBy(c => rng.Value);
            if (sortedChildren.Count() == 0) {
                // will keep 0:0
                return;
            }
            // 1. try to find a moon with small chance(high probability), to skip closeup
            if (rng.Chance(0.2f)) {
                foreach (var ch in sortedChildren) {
                    if (ch.Type.IdH == SpaceObjectTypeIdH.Moon) {
                        target = ch;
                        break;
                    }
                }
            }
            if (target == null) {
                foreach (var ch in so.Children) {
                    if (ch.Type.IdH == SpaceObjectTypeIdH.PlanetCloseup) {
                        target = ch;
                        break;
                    }
                }
            }
            if (target == null) {
                target = sortedChildren.First();
            }
            if (target == null) {
                D.Err("target could not be selected, will spawn resource in the center");
            } else {
                var size = SpaceObjectLooks.ObjSize(target, target.Looks(false));
                size *= 0.2f;
                int attempts = 10;
                do {
                    child.Location = target.Location + rng.InsideUnitCircle() * size;
                } while (!CanSpawnResourceAt(child.Location, child, so) && attempts-- > 0);
                if (attempts < 1) {
                    FindLocationForNewResource(child, so, rng, majorAttemptsLeft - 1);
                }
            }
        }

        private void OnAnomalyScanSuccess(SpaceSector obj) {
            FoundSO.Set(obj.SO);
            FoundSector = obj;
            S.Sys.Universe.GenerateSector(obj.SO, isEntering: false, reveal: true);
            obj.SO.Knowledge.POIs ??= new();
            obj.SO.Knowledge.POIs[SpaceObjectTypeIdH.Anomaly] = 1;
            var message = T.ScanFoundOnPlanet_V2.F(T.ScanTargetAnomaly, obj.SO.ParentPath());
            ShowFoundEvent(message);
        }
        private void OnSpaceStationScanSuccess(SpaceSector obj) {
            FoundSO.Set(obj.SO);
            FoundSector = obj;
            var message = T.ScanFoundOnPlanet_V2.F(T.SpaceObjectSpacestation, obj.SO.ParentPath());
            S.Sys.Universe.GenerateSector(obj.SO, isEntering: false, reveal: true);
            ShowFoundEvent(message);
        }
        private void OnRelayScanSuccess(SpaceSector obj) {
            FoundSO.Set(obj.SO);
            FoundSector = obj;
            var message = T.ScanFoundOnPlanet_V2.F(T.SpaceObjectHyperjumprelay, obj.SO.ParentPath());
            S.Sys.Universe.GenerateSector(obj.SO, isEntering: false, reveal: true);
            ShowFoundEvent(message);
        }
        private void OnDerelictScanSuccess(SpaceSector obj) {
            FoundSO.Set(obj.SO);
            FoundSector = obj;
            var message = T.ScanFoundOnPlanet_V2.F(T.ScanTargetDerelictShip, obj.SO.ParentPath());
            S.Sys.Universe.GenerateSector(obj.SO, isEntering: false, reveal: true);
            ShowFoundEvent(message);
        }
        private void OnMatScanSuccess(SpaceSector obj) {
            FoundSO.Set(obj.SO);
            FoundSector = obj;
            S.Sys.Universe.GenerateSector(obj.SO, isEntering: false, reveal: true);
            if (obj.SO.HasChildren) {
                var mv = MatTarget.Value;
                foreach (var child in obj.SO.Children) {
                    if (S.Sys.Planets.IsMinableResource(child, out _, out var childMat)) {
                        if (child.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown
                                && childMat == mv) {
                            SOActionScan.ScanResource(child, out _);
                            break;
                        }
                    }
                }
            } else {
                D.Err("Scanned a planet for material, but it doesn't have children! {0}", obj.SO);
            }
            // already doing this in ScanResource
            // obj.SO.Knowledge.DiscoverMat(MatTarget);
            var message = T.ScanFoundOnPlanet_V2.F(
                MatTarget.Value.NameT, obj.SO.ParentPath());
            string hint = null;
            if (obj.Region != SOUtils.GetPlayerSector(S)?.Region) {
                hint = RegionSupplyInfo.AbundantHintFor(S, MatTarget.Value);
            }
            ShowFoundEvent(message, hint);
        }
        private readonly List<NotificationHandle> deepScanEvents = new();

        // The hint goes to the log and fanfare only, the notification title stays short
        private void ShowFoundDeepEvent(string message, string hint = null) {
            var obj = FoundDeepSO;
            obj.UI?.Refresh();
            var evt = Notification.Build(obj.Icon, message, Consts.TicksPer3Days, NotificationType.Info)
                .Group("deep_scan", T.ScanTargetDeep, IconId.CMagnifier)
                .WithOnClick(() => {
                    S.Sys.Universe.ShowInStarmap(FoundDeepSO);
                }).Show();
            message = T.ScanReport_V1.F(Texts.Green(message));
            if (hint != null) {
                message = $"{message}. {hint}";
            }
            S.Sys.Log.AddLine(message);
            S.Sig.ShowFanfare.Send(message);
            deepScanEvents.Add(evt);
        }

        // The hint goes to the log and fanfare only, the notification title stays short
        private void ShowFoundEvent(string message, string hint = null) {
            var obj = FoundSO.Value;
            obj?.UI?.Refresh();
            var evt = Notification.Build(IconId.CCommunications, message,
                    Consts.TicksPer3Days, NotificationType.Info)
                .Group("scan", T.ResearchScanning, IconId.CCommunications)
                .WithOnClick(() => {
                    S.Sys.Universe.ShowInStarmap(obj);
                }).Show();
            message = T.ScanReport_V1.F(Texts.Green(message));
            if (hint != null) {
                message = $"{message}. {hint}";
            }
            S.Sys.Log.AddLine(message);
            S.Sig.ShowFanfare.Send(message);
        }

        public string ScanSubject => Mode.Value switch {
            ScanMode.None => T.ScanTargetNone,
            ScanMode.Mat => MatTarget.Value?.NameT,
            ScanMode.Anomaly => T.ScanTargetAnomaly,
            ScanMode.DerelictShip => T.ScanTargetDerelictShip,
            ScanMode.SpaceStation => T.SpaceObjectSpacestation,
            ScanMode.HyperjumpRelay => T.SpaceObjectHyperjumprelay,
            _ => $"{T.Unknown}: {Mode}",
        };

        private void NotifyScannerCouldNotFind() {
            string message;
            if (FoundSO.Value != null) {
                message = T.ScanNotFound_V1.F(
                    Texts.LinkToStarmap(FoundSO.Value, ScanSubject));
            } else {
                // nothing was ever found for this scan, so there is nothing to link to
                message = T.ScanNotFound_V1.F(ScanSubject);
            }
            message = T.ScanReport_V1.F(Texts.Yellow(message));
            if (Mode.Value == ScanMode.Mat && MatTarget.Value != null) {
                var hint = RegionSupplyInfo.AbundantHintFor(S, MatTarget.Value);
                if (hint != null) {
                    message = $"{message}. {hint}";
                }
            }
            S.Sys.Log.AddLine(message);
            S.Sig.ShowFanfare.Send(message);
        }

        private void OnScanAnomaly() {
            SpaceSector found = search.FindAnomaly();

            if (found == null) {
                NotifyScannerCouldNotFind();
            } else {
                SOUtils.EnsureKnowledgeOfPOI(found.SO, SpaceObjectTypeIdH.Anomaly);
                OnAnomalyScanSuccess(found);
            }
        }

        private void OnScanSpaceStation() {
            SpaceSector found = search.FindSpaceStation();

            if (found == null) {
                NotifyScannerCouldNotFind();
            } else {
                SOUtils.EnsureKnowledgeOfPOI(found.SO, SpaceObjectTypeIdH.SpaceStation);
                OnSpaceStationScanSuccess(found);
            }
        }
        private void OnScanRelay() {
            SpaceSector found = search.FindRelay();

            if (found == null) {
                NotifyScannerCouldNotFind();
            } else {
                OnRelayScanSuccess(found);
            }
        }

        private void OnScanDerelictShip() {
            SpaceSector found = search.FindDerelictShip();

            if (found == null) {
                NotifyScannerCouldNotFind();
            } else {
                OnDerelictScanSuccess(found);
            }
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Scan ??= new ScanSysData();
            sd.Scan.ScanProgress = ScanProgress;
            sd.Scan.ScanDeepProgress = ScanDeepProgress;
            sd.Scan.ScanDifficulty = ScanDifficulty;
            sd.Scan.ScanDeepDifficulty = ScanDeepDifficulty;
            sd.Scan.Mode = Mode.Value;
            sd.Scan.TargetMat = MatTarget.Value?.Id;
            sd.Scan.DeepFocusMat = DeepFocus.Value?.Id;
            sd.Scan.DeepFocusMisses = DeepFocusMisses;
            sd.Scan.StartTick = ScanStartTick;
            sd.Scan.DeepStartTick = ScanDeepStartTick;
            sd.Scan.FoundSOId = FoundSO.Value?.Id ?? 0;
            sd.Scan.FoundDeepSOId = FoundDeepSO?.Id ?? 0;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd.Scan != null) {
                data = sd.Scan;
            }
        }

        public void OnHyperspaceAction(float penalty) {
            if (!Arrays.IsEmpty(deepScanEvents)) {
                for (int i = deepScanEvents.Count - 1; i >= 0; i--) {
                    var evt = deepScanEvents[i];
                    Notification.Remove(ref evt);
                }
                deepScanEvents.Clear();
            }
            ScanDeepProgress.Set(0f);
            ScanDeepStartTick = 0;
            if (Mode.Value == ScanMode.None || ScanProgress >= 0.99f) {
                return;
            }
            ScanProgress.Multiply(1f - penalty);
        }

        public void Reset() {
            FoundSector = null;
            FoundSO.Set(null);
            FoundDeepSO = null;
        }
    }
}
