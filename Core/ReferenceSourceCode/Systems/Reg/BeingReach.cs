using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Areas;
using Game.Systems.Motion;
using Game.Systems.Path;
using KL.Grid;
using Unity.Collections;
using Unity.Jobs;

namespace Game.Systems.Reg {
    // Which regions a being can get to by a route it may walk. Floor islands only know floor
    // connectivity, while the pathfinder also refuses doors the being may not open and tiles
    // outside its air and temperature limits. Target searches that trusted islands kept
    // picking targets the walk could never reach (CQGKQ: a cat behind airless rooms), so
    // this judges reach by the pathfinder's own rules, per region, once per search.
    // Why it works the way it does: Docs/Architecture/Systems/BeingReach.md
    public sealed class BeingReach {
        private const int MaxProfiles = 64;
        // limits that matter are rounded toward the lenient side, so beings whose limits
        // differ by wear or a clothing modifier share one labelling
        private const float O2Step = 0.01f;
        private const float TempStep = 5f;
        private readonly GameState S;
        private readonly RegSys reg;
        private readonly Dictionary<ReachProfile, ReachLabels> byProfile = new();
        private readonly Stack<ReachLabels> spareLabels = new();
        // managed copies of the region graph: the label work reads them many times, and
        // native container indexers cost several times more in Mono
        private AreaType[] types = Array.Empty<AreaType>();
        private NavPermission[] deny = Array.Empty<NavPermission>();
        private int[] edgeStarts = Array.Empty<int>();
        private int[] edges = Array.Empty<int>();
        private int[] floorRegions = Array.Empty<int>();
        private int numRegions;
        private int numFloorRegions;
        private bool isGraphDirty = true;
        // per floor region: the most air and the temperature range over its tiles
        private NativeArray<float> jobMaxO2;
        private NativeArray<float> jobMinTemp;
        private NativeArray<float> jobMaxTemp;
        private NativeArray<float> jobExtremes;
        private float[] maxO2 = Array.Empty<float>();
        private float[] minTemp = Array.Empty<float>();
        private float[] maxTemp = Array.Empty<float>();
        // a limit at or inside these fails no floor region, so it can't change reach
        private float leastBestO2 = float.MaxValue;
        private float coldestTop = float.MaxValue;
        private float hottestBottom = float.MinValue;
        private int[] unionParents = Array.Empty<int>();
        // regions or door masks changed: every profile rebuilds its labels
        private int graphVersion = 1;
        // the atmosphere summary was refreshed: profiles check whether a region flipped
        private int atmoVersion;
        private bool isAtmoDirty = true;
        public int LabelBuilds { get; private set; }
        public int SummaryBuilds { get; private set; }
        public int ProfileCount => byProfile.Count;

        public BeingReach(GameState s, RegSys reg) {
            S = s;
            this.reg = reg;
        }

        public void OnRegionsChanged() {
            isGraphDirty = true;
            isAtmoDirty = true;
            graphVersion++;
        }

        public void OnDoorsChanged() {
            isGraphDirty = true;
            graphVersion++;
        }

        public void OnAtmoChanged() {
            isAtmoDirty = true;
        }

        public void Dispose() {
            DisposeSummary();
            byProfile.Clear();
            spareLabels.Clear();
        }

        private void DisposeSummary() {
            if (jobMaxO2.IsCreated) { jobMaxO2.Dispose(); }
            if (jobMinTemp.IsCreated) { jobMinTemp.Dispose(); }
            if (jobMaxTemp.IsCreated) { jobMaxTemp.Dispose(); }
            if (jobExtremes.IsCreated) { jobExtremes.Dispose(); }
        }

        public ReachQuery For(MotionControl motion) {
            return For(motion, false);
        }

        // ignoreAtmosphere: for walks that may cross unsafe tiles (PathReqFlags.UnsafeOK)
        public ReachQuery For(MotionControl motion, bool ignoreAtmosphere) {
            var areas = S.Sys.Areas;
            var pos = motion.Position.Idx;
            var isGrounded = motion.IsGrounded;
            if (reg.NumRegions == 0 || Pos.IsOOB(pos)) {
                return ReachQuery.ByIslands(areas, pos, isGrounded);
            }
            EnsureGraph();
            if (!ignoreAtmosphere) {
                EnsureAtmoSummary();
            }
            var labels = LabelsFor(ProfileOf(motion, ignoreAtmosphere));
            var start = StartLabel(labels.Labels, pos);
            if (start < 0) {
                // standing where it may not walk (unsafe air, a sealed spot): the pathfinder
                // lets a being walk out of danger, so judge by islands as before
                return ReachQuery.ByIslands(areas, pos, isGrounded);
            }
            return new ReachQuery(labels.Labels, reg.PosRegIds, start);
        }

        // The pathfinder's limits (MotionControl.CreatePathReq), with every limit that no
        // floor region fails dropped: robots and suited crew then share the labels that
        // ignore the air instead of one labelling per worn-down part
        private ReachProfile ProfileOf(MotionControl m, bool ignoreAtmosphere) {
            var g = m.IsGrounded;
            var perms = m.Permissions;
            if (!ignoreAtmosphere) {
                var o2 = m.MinO2;
                var tMin = m.MinTemp;
                var tMax = m.MaxTemp;
                var o2Counts = o2 > 0f && o2 > leastBestO2;
                var coldCounts = tMin > coldestTop;
                var heatCounts = tMax < hottestBottom;
                if (o2Counts || coldCounts || heatCounts) {
                    return new ReachProfile(g, false, perms,
                        o2Counts ? MathF.Floor(o2 / O2Step) * O2Step : 0f,
                        coldCounts ? MathF.Floor(tMin / TempStep) * TempStep : 0f,
                        heatCounts ? MathF.Ceiling(tMax / TempStep) * TempStep : float.MaxValue);
                }
            }
            return new ReachProfile(g, true, perms, 0f, 0f, 0f);
        }

        private ReachLabels LabelsFor(in ReachProfile profile) {
            if (!byProfile.TryGetValue(profile, out var labels)) {
                if (byProfile.Count >= MaxProfiles) {
                    foreach (var l in byProfile.Values) {
                        spareLabels.Push(l);
                    }
                    byProfile.Clear();
                }
                labels = spareLabels.Count > 0 ? spareLabels.Pop() : new ReachLabels();
                labels.GraphVersion = 0;
                byProfile.Add(profile, labels);
            }
            if (labels.GraphVersion != graphVersion) {
                Build(labels, profile);
            } else if (!profile.IgnoreAtmosphere && labels.AtmoVersion != atmoVersion) {
                if (HasFlipped(labels, profile)) {
                    Build(labels, profile);
                } else {
                    labels.AtmoVersion = atmoVersion;
                }
            }
            return labels;
        }

        private int StartLabel(int[] labels, int pos) {
            var posRegIds = reg.PosRegIds;
            var r = posRegIds[pos];
            if (r < 0 || r >= numRegions) { return -1; }
            var type = types[r];
            if (type == AreaType.Wall || type == AreaType.BlockedByObject) {
                // on a device or in a bed: it leaves through a tile next to it
                for (int i = 0; i < 8; i++) {
                    var n = Pos.NeighborAt(pos, i);
                    if (Pos.IsOOB(n)) { continue; }
                    var nr = posRegIds[n];
                    if (nr >= 0 && nr < numRegions && labels[nr] >= 0) {
                        return labels[nr];
                    }
                }
                return -1;
            }
            return labels[r];
        }

        private void EnsureGraph() {
            var n = reg.NumRegions;
            if (!isGraphDirty && numRegions == n) { return; }
            if (types.Length < n) {
                var cap = n + n / 4;
                types = new AreaType[cap];
                deny = new NavPermission[cap];
                floorRegions = new int[cap];
                edgeStarts = new int[cap + 1];
            }
            var numEdges = reg.RegEdges.Length;
            if (edges.Length < numEdges) {
                edges = new int[numEdges + numEdges / 4];
            }
            NativeArray<AreaType>.Copy(reg.RegTypes.AsArray(), 0, types, 0, n);
            NativeArray<NavPermission>.Copy(reg.RegDeny.AsArray(), 0, deny, 0, n);
            NativeArray<int>.Copy(reg.RegEdgeStarts.AsArray(), 0, edgeStarts, 0, n + 1);
            NativeArray<int>.Copy(reg.RegEdges.AsArray(), 0, edges, 0, numEdges);
            numFloorRegions = 0;
            for (int r = 0; r < n; r++) {
                if (types[r] == AreaType.Floor) {
                    floorRegions[numFloorRegions++] = r;
                }
            }
            if (numRegions != n) {
                // the summary is per region id
                isAtmoDirty = true;
            }
            numRegions = n;
            isGraphDirty = false;
        }

        private void EnsureAtmoSummary() {
            var n = numRegions;
            if (!isAtmoDirty) { return; }
            if (!jobMaxO2.IsCreated || jobMaxO2.Length < n) {
                DisposeSummary();
                var cap = n + n / 4;
                jobMaxO2 = new NativeArray<float>(cap, Allocator.Persistent);
                jobMinTemp = new NativeArray<float>(cap, Allocator.Persistent);
                jobMaxTemp = new NativeArray<float>(cap, Allocator.Persistent);
                jobExtremes = new NativeArray<float>(3, Allocator.Persistent);
                maxO2 = new float[cap];
                minTemp = new float[cap];
                maxTemp = new float[cap];
                graphVersion++;
            }
            new RegAtmoSummaryJob {
                RegTypes = reg.RegTypes,
                RegPosStarts = reg.RegPosStarts,
                RegPos = reg.RegPos,
                O2 = S.Map.O2.Data,
                Temp = S.Map.Temp.Data,
                NumRegions = n,
                MaxO2 = jobMaxO2,
                MinTemp = jobMinTemp,
                MaxTemp = jobMaxTemp,
                Extremes = jobExtremes,
            }.Run();
            NativeArray<float>.Copy(jobMaxO2, maxO2, n);
            NativeArray<float>.Copy(jobMinTemp, minTemp, n);
            NativeArray<float>.Copy(jobMaxTemp, maxTemp, n);
            leastBestO2 = jobExtremes[0];
            coldestTop = jobExtremes[1];
            hottestBottom = jobExtremes[2];
            isAtmoDirty = false;
            atmoVersion++;
            SummaryBuilds++;
        }

        // Mirrors RegSys.IsRegPassable and FastPathfindJob.DifficultyOf: doors are always safe
        // to cross, and a floor region counts if any of its tiles may be safe (optimistic, so
        // a reachable target is never ruled out; the pathfinder stays the judge of the route)
        private bool IsPassable(int r, in ReachProfile p) {
            switch (types[r]) {
                case AreaType.Space:
                    return !p.IsGrounded;
                case AreaType.Door:
                    return (deny[r] & p.Permissions) == NavPermission.None;
                case AreaType.Floor:
                    return p.IgnoreAtmosphere || IsSafe(r, p);
                default:
                    return false;
            }
        }

        private bool IsSafe(int r, in ReachProfile p) {
            if (p.MinO2 > 0f && maxO2[r] < p.MinO2) { return false; }
            if (maxTemp[r] < p.MinTemp) { return false; }
            if (minTemp[r] > p.MaxTemp) { return false; }
            return true;
        }

        private bool HasFlipped(ReachLabels labels, in ReachProfile p) {
            var l = labels.Labels;
            for (int i = 0; i < numFloorRegions; i++) {
                var r = floorRegions[i];
                if (IsSafe(r, p) != l[r] >= 0) { return true; }
            }
            return false;
        }

        private void Build(ReachLabels labels, in ReachProfile p) {
            var n = numRegions;
            if (labels.Labels.Length < n) {
                labels.Labels = new int[n + n / 4];
            }
            if (unionParents.Length < n) {
                unionParents = new int[n + n / 4];
            }
            var parents = unionParents;
            var l = labels.Labels;
            for (int r = 0; r < n; r++) {
                parents[r] = IsPassable(r, p) ? r : -1;
            }
            for (int r = 0; r < n; r++) {
                if (parents[r] < 0) { continue; }
                var end = edgeStarts[r + 1];
                for (int i = edgeStarts[r]; i < end; i++) {
                    var e = edges[i];
                    if (e < 0 || e >= n || parents[e] < 0) { continue; }
                    var a = Find(parents, r);
                    var b = Find(parents, e);
                    if (a != b) {
                        parents[a] = b;
                    }
                }
            }
            for (int r = 0; r < n; r++) {
                l[r] = parents[r] < 0 ? -1 : Find(parents, r);
            }
            labels.GraphVersion = graphVersion;
            labels.AtmoVersion = atmoVersion;
            LabelBuilds++;
        }

        private static int Find(int[] parents, int r) {
            while (parents[r] != r) {
                parents[r] = parents[parents[r]];
                r = parents[r];
            }
            return r;
        }

        private sealed class ReachLabels {
            // idx = region id, val = the component it belongs to, -1 = may not walk there
            public int[] Labels = Array.Empty<int>();
            public int GraphVersion;
            public int AtmoVersion;
        }
    }

    // What the pathfinder compares when it judges a route (MotionControl.CreatePathReq)
    public readonly struct ReachProfile : IEquatable<ReachProfile> {
        public readonly bool IsGrounded;
        public readonly bool IgnoreAtmosphere;
        public readonly NavPermission Permissions;
        public readonly float MinO2;
        public readonly float MinTemp;
        public readonly float MaxTemp;

        public ReachProfile(bool isGrounded, bool ignoreAtmosphere, NavPermission permissions,
                float minO2, float minTemp, float maxTemp) {
            IsGrounded = isGrounded;
            IgnoreAtmosphere = ignoreAtmosphere;
            Permissions = permissions;
            MinO2 = ignoreAtmosphere ? 0f : minO2;
            MinTemp = ignoreAtmosphere ? 0f : minTemp;
            MaxTemp = ignoreAtmosphere ? 0f : maxTemp;
        }

        public bool Equals(ReachProfile o) {
            return IsGrounded == o.IsGrounded && IgnoreAtmosphere == o.IgnoreAtmosphere
                && Permissions == o.Permissions && MinO2 == o.MinO2
                && MinTemp == o.MinTemp && MaxTemp == o.MaxTemp;
        }

        public override bool Equals(object obj) {
            return obj is ReachProfile o && Equals(o);
        }

        public override int GetHashCode() {
            return HashCode.Combine(IsGrounded, IgnoreAtmosphere, (int) Permissions,
                MinO2, MinTemp, MaxTemp);
        }
    }
}
