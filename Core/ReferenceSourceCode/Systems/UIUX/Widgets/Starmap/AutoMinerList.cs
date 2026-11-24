using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public struct AutoMinerInfo {
        public SpaceObject SO;
        public MatType Mat;
    }

    // Deployed auto miners on the starmap. The Mining Automation menu block and the rig icon
    // under a sector or region node list them from here, so both agree on what counts.
    public static class AutoMinerList {
        public static void CollectInSector(GameState s, SpaceObject sector,
                List<AutoMinerInfo> into) {
            if (Arrays.IsEmpty(sector.Children)) { return; }
            foreach (var child in sector.Children) {
                if (s.Sys.Planets.TryGetResourceFor(child, out var resource, out var mt)
                        && resource.AutoMineSpeed > 0f) {
                    into.Add(new AutoMinerInfo { SO = child, Mat = mt });
                }
            }
        }

        public static void CollectInRegion(GameState s, SpaceObject region,
                List<AutoMinerInfo> into) {
            if (Arrays.IsEmpty(region.Children)) { return; }
            foreach (var sector in region.Children) {
                CollectInSector(s, sector, into);
            }
        }

        // Rigs on a starmap node: a sector's own, or every sector's of a region
        public static void CollectFor(GameState s, SpaceObject node, List<AutoMinerInfo> into) {
            into.Clear();
            if (node.Type.IdH == SpaceObjectTypeIdH.Region) {
                CollectInRegion(s, node, into);
            } else if (node.Parent?.Type.IdH == SpaceObjectTypeIdH.Region) {
                CollectInSector(s, node, into);
            }
            if (into.Count > 1) {
                into.Sort(BySectorThenMat);
            }
        }

        private static int BySectorThenMat(AutoMinerInfo a, AutoMinerInfo b) {
            var bySector = string.CompareOrdinal(a.SO.Parent?.DisplayName, b.SO.Parent?.DisplayName);
            return bySector != 0 ? bySector : string.CompareOrdinal(a.Mat.NameT, b.Mat.NameT);
        }

        public static bool IsMaxDepthReached(in PlanetResource resource) {
            return resource.MiningDepth >= resource.AutoMineMaxDepth;
        }

        public static string StorageText(in PlanetResource resource) {
            return Units.XOutOfY((int) resource.Unretrieved, resource.UnretrievedMax);
        }

        // Difficulty and depth of one rig, for its row in the Mining Automation menu
        public static string RigTooltip(GameState s, SpaceObject so) {
            if (!s.Sys.Planets.TryGetResourceFor(so, out var resource, out var mt)) {
                return null;
            }
            var sb = new StringBuilder();
            sb.Append(T.MiningDifficultyValue_V1.F(
                Texts.Accented(resource.MiningDifficulty.ToString("0.##"), false)));
            if (!mt.Deposit.IsWithDepth) {
                return sb.ToString();
            }
            AppendDepth(sb, resource);
            return sb.ToString();
        }

        // Every rig on a starmap node, one line each so a region with many rigs stays
        // readable: material, sector (region nodes only), storage, depth of max depth.
        // Labels are left out on purpose: the sector tooltip shows depth the same way.
        public static string NodeTooltip(GameState s, SpaceObject node,
                List<AutoMinerInfo> rigs) {
            var sb = new StringBuilder();
            sb.Append(Texts.Title(T.ResearchMiningautomation));
            bool showSector = node.Type.IdH == SpaceObjectTypeIdH.Region;
            bool first = true;
            for (int i = 0; i < rigs.Count; i++) {
                var rig = rigs[i];
                if (!s.Sys.Planets.TryGetResourceFor(rig.SO, out var resource, out var mt)) {
                    continue;
                }
                if (!first) {
                    sb.Append('\n');
                }
                first = false;
                bool withDepth = mt.Deposit.IsWithDepth;
                // A full storage stops the rig as surely as the max depth does
                if (IsStorageFull(resource) || (withDepth && IsMaxDepthReached(resource))) {
                    sb.Append(GlyphMap.SpriteWarning);
                    sb.Append(' ');
                }
                sb.Append(mt.Def.CodexLink);
                if (showSector && rig.SO.Parent != null) {
                    sb.Append(' ');
                    sb.Append(Texts.Grey(rig.SO.Parent.DisplayName));
                }
                sb.Append("  ");
                sb.Append(ByFill(StorageText(resource), resource.Unretrieved,
                    resource.UnretrievedMax));
                if (withDepth) {
                    var depth = $"{Units.Depth(resource.MiningDepth)}/{Units.Depth(resource.AutoMineMaxDepth)}";
                    sb.Append("  ");
                    sb.Append(GlyphMap.ArrowDown);
                    sb.Append(ByFill(depth, resource.MiningDepth, resource.AutoMineMaxDepth));
                }
            }
            return sb.ToString();
        }

        private static void AppendDepth(StringBuilder sb, in PlanetResource resource) {
            var depthText = ByFill(Units.Depth(resource.MiningDepth),
                resource.MiningDepth, resource.AutoMineMaxDepth);
            var maxDepthText = ByFill(Units.Depth(resource.AutoMineMaxDepth),
                resource.MiningDepth, resource.AutoMineMaxDepth);
            sb.AppendFormat("\n{0}: {1}", T.NounDepth, depthText);
            sb.AppendFormat("\n{0}: {1}", T.MiningDepthMax, maxDepthText);
        }

        public static bool IsStorageFull(in PlanetResource resource) {
            return resource.UnretrievedMax > 0 && resource.Unretrieved >= resource.UnretrievedMax;
        }

        // Share of the room used up: plenty left is green, the last 20% yellow, none red
        private const float LowRoomShare = 0.8f;
        private static string ByFill(string text, float value, float max) {
            if (max <= 0f || value >= max) {
                return Texts.Red(text);
            }
            return value >= max * LowRoomShare ? Texts.Yellow(text) : Texts.Green(text);
        }
    }
}
