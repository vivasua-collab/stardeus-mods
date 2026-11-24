using System;
using System.Collections.Generic;
using System.Text;
using Game.Data;
using Game.Procgen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using MessagePack;

namespace Game.Procgen {
    [Serializable]
    [MessagePackObject]
    public class ShipSpec {
        [Key("Seed")]
        public int Seed;
        [Key("CenterX")]
        public int CenterX;
        [Key("CenterY")]
        public int CenterY;
        [IgnoreMember]
        public int OriginX => CenterX - TemplateWidth / 2;
        [IgnoreMember]
        public int OriginY => CenterY - TemplateHeight;
        [Key("CellWidth")]
        public int CellWidth = 1;
        [Key("CellHeight")]
        public int CellHeight = 1;
        [Key("TemplateWidth")]
        public int TemplateWidth;
        [Key("TemplateHeight")]
        public int TemplateHeight;
        [Key("Width")]
        public int Width;
        [Key("Height")]
        public int Height;
        [IgnoreMember]
        public List<ShipIsland> Islands;
        private CellType[,] template;
        [Key("SerializedTemplate")]
        [SerializeField]
        private string[] serializedTemplate;
        [IgnoreMember] public Rng Rng;
        [IgnoreMember] public Rng CosmeticRng;
        /// <summary>
        /// THIS GETS STALE AFTER ISLAND FLOAT GEN STAGE
        /// </summary>
        [Key("CPUPos")] public int CPUPos { get; private set; }
        [IgnoreMember] public bool HasCPU;
        public void SetCPUPos(int pos) {
            HasCPU = true;
            // D.Warn("Setting cpu pos: {0}", Pos.StringFromTo(CPUPos, pos));
            CPUPos = pos;
        }
        [IgnoreMember] public List<int> StasisPos { get; private set; }
        [IgnoreMember] public int SurvivorPos { get; private set; }
        [IgnoreMember] public bool HasStasisArray;
        [IgnoreMember] public bool HasSurvivors;
        public void AddStasisPos(int pos) {
            if (Pos.IsOOB(pos)) {
                D.Err("Trying to add OOB pos for stasis pos! {0}", Pos.StringD(pos));
                return;
            }
            HasStasisArray = true;
            // D.Warn("Adding stasis pos: {0}", Pos.String(pos));
            StasisPos ??= new List<int>();
            StasisPos.Add(pos);
        }
        public void SetSurvivorPos(int pos) {
            if (Pos.IsOOB(pos)) {
                D.Err("Trying to add OOB pos for survivor pos! {0}", Pos.StringD(pos));
                return;
            }
            HasSurvivors = true;
            SurvivorPos = pos;
        }

        [IgnoreMember] public readonly HashSet<int> InvincibleParts = new();
        // The outer walls OuterWallGenStage built. After destruction, walls facing space are
        // also inner walls whose room was blown away; things mounted on the hull go on these
        // only. Tiles, not positions: moving a section moves the same tiles, and a wall
        // replaced later (airlock, window) or destroyed drops out by itself (HullRuns)
        [IgnoreMember] public readonly HashSet<Tile> HullWalls = new();
        // Tiles of corridors cut by a room stage, so door placement can treat them as hubs
        [IgnoreMember] public readonly HashSet<int> CorridorTiles = new();
        // Centers of the rooms RoomGrowthGenStage reserved for the stasis arrays and the
        // shuttle, which their stages try before any other room
        [IgnoreMember] public readonly List<int> StasisRoomHints = new();
        [IgnoreMember] public readonly List<int> ShuttleRoomHints = new();
        // Whether a stage's StartOver is honored. Only a starting ship's factory can start
        // over: restarting a derelict's would clear the player's own ship too
        [IgnoreMember] public bool CanStartOver;

        // Set once ShipRotateGenStage has turned the ship: the template stays as it was rolled,
        // so the bounds it implies would still show the ship before the turn
        [IgnoreMember] private PosRect rotatedBounds;
        [IgnoreMember] private bool hasRotatedBounds;
        public void SetRotatedBounds(PosRect bounds) {
            rotatedBounds = bounds;
            hasRotatedBounds = true;
        }

        [IgnoreMember]
        public PosRect Bounds {
            get {
                if (hasRotatedBounds) { return rotatedBounds; }
                var minX = CenterX - TemplateWidth * CellWidth / 2;
                var maxX = CenterX + TemplateWidth * CellWidth / 2;
                var minY = CenterY - TemplateHeight * CellHeight;
                var maxY = CenterY + TemplateHeight * CellHeight;
                return new(minX, minY, maxX, maxY);
            }
        }

        [IgnoreMember] public PosRect GenArea;
        [IgnoreMember] public bool HasGenArea;
        public void SetGenArea(PosRect area) {
            //D.Log("Gen area: {0}", area);
            HasGenArea = true;
            GenArea = area;
        }

        public bool IsWithinGenArea(int posIdx) {
            if (!HasGenArea) { return true; }
            return GenArea.Contains(posIdx);
        }

        public List<XY> Find(CellType searchedType) {
            var res = new List<XY>();
            ForEachMirrored((x, y, type) => {
                if (type == searchedType) {
                    res.Add(new(x, y));
                }
            });
            return res;
        }
        public List<int> FindNonEmpty() {
            var res = new List<int>();
            ForEach((x, y, type) => {
                if (!IsEmpty(x, y)) {
                    res.Add(Pos.FromXY(x, y));
                }
            });
            return res;
        }
        // Cell positions (x, full y) packed with Pos.FromXY
        public List<int> FindNonEmptyFull() {
            var res = new List<int>();
            for (int x = 0; x < TemplateWidth; x++) {
                for (int fy = 0; fy < TemplateHeight * 2; fy++) {
                    if (!IsEmptyFull(x, fy)) {
                        res.Add(Pos.FromXY(x, fy));
                    }
                }
            }
            return res;
        }
        public List<int> FindNonEmptyMirrored() {
            var res = new List<int>();
            ForEachMirrored((x, y, type) => {
                if (!IsEmpty(x, y)) {
                    res.Add(Pos.FromXY(x, y));
                }
            });
            return res;
        }

        // This is just half of actual height since template is mirrored

        public CellType GetCellMirrored(int x, int y) {
            return template[x, y];
        }
        public CellType GetCellMirroredSafe(int x, int y) {
            if (IsOutOfBounds(x, y, true)) { return CellType.Empty; }
            return template[x, y];
        }

        // Pos here is cell pos, not tile pos, do not refactor to int you idiot
        public int ToWorldPosMirrored(XY cellPos, bool top) {
            var y = top ? CellToWorldY(TemplateHeight + cellPos.Y)
                : CellToWorldY(TemplateHeight - cellPos.Y - 1);
            return Pos.FromXY(CellToWorldX(cellPos.X), y);
        }
        public bool IsYOnAxis(int y) {
            var y0 = CenterY;
            return y0 == y || y0 - 1 == y;
        }

        public bool IsOnAxis(int idx) {
            Pos.ToXY(idx, out var x, out var y);
            return IsYOnAxis(y);
        }

        public int TileToWorldX(int shipX) {
            return CenterX - Width / 2 + shipX;
        }
        public int TileToWorldY(int shipY) {
            return CenterY - Height / 2 + shipY;
        }

        public int CellToWorldX(int x) {
            return CenterX - Width / 2 + x * CellWidth;
        }

        public int CellToWorldY(int y) {
            return CenterY + (y - TemplateHeight) * CellHeight;
        }

        public CellType GetCellAtWorldXY(int x, int y) {
            // x = CenterX - Width / 2 + ? * CellWidth;
            // x - CenterX + Width / 2 = ? * CellWidth;
            // (x - CenterX + Width / 2) / CellWidth = ?

            // y = CenterY + (? - Templateheight) * CellHeight;
            // y - CenterY = (? - Templateheight) * Cellheight;
            // (y - centerY) / CellHeight = ? - TH;
            // ? = ((y - centerY) / cellheight) + templateheight

            var cx = (x - CenterX + Width / 2) / CellWidth;
            var cy = ((y - CenterY) / CellHeight) + TemplateHeight;
            var res = GetCellMirrored(cx, cy);
            D.Warn("WorldXY to cell: {0}:{1} -> {2}:{3} / {4}",
                x, y, cx, cy, res);
            return res;
        }

        public void CreateRng() {
            Rng = new Rng(Seed);
            CosmeticRng = new Rng(Hashes.Quick(Seed, 1237));
        }

        public void CreateTemplate() {
            template = new CellType[TemplateWidth, TemplateHeight * 2];
        }
        public bool IsCell(int x, int y, CellType type) {
            if (IsOutOfBounds(x, y)) { return type == CellType.Empty; }
            return template[x, TemplateHeight + y] == type;
        }

        ///<summary>Before mirror by default!!!</summary>
        public bool IsEmpty(int x, int y, bool mirrored = false,
                bool cornersAreEmpty = true, bool engineIsEmpty = false) {
            if (IsOutOfBounds(x, y, mirrored)) {
                // D.Warn("Is OOB: {0}:{1}", x, y);
                return true;
            }
            CellType type;
            if (mirrored) {
                type = template[x, y];
            } else {
                type = template[x, TemplateHeight + y];
            }
            var res = type == CellType.Empty
                || type == CellType.BodyEmpty
                || type == CellType.CockpitEmpty
                || (engineIsEmpty && type == CellType.Engine)
                || (cornersAreEmpty && type == CellType.Corner);
            // D.Warn("Cell type at {0}:{1}: {2}, empty: {3}", x, y, type, res);
            return res;
        }

        ///<summary>Before mirror by default!</summary>
        public bool IsOutOfBounds(int x, int y, bool mirrored = false) {
            if (mirrored) {
                return x < 0 || x >= TemplateWidth || y < 0 || y >= TemplateHeight * 2;
            }
            return x < 0 || x >= TemplateWidth || y < 0 || y >= TemplateHeight;
        }

        public void SetCellMirrored(int x, int y, CellType type) {
            template[x, y] = type;
        }

        // The template always stores the full ship height; the half API above mirrors every
        // write. A hull that sets this writes through the full API instead, and every stage
        // that reads the template has to treat each row on its own. Stored so that a missing
        // key in older saves reads as the mirrored ship it was.
        [Key("IsAsymmetric")]
        public bool IsAsymmetric;
        [IgnoreMember]
        public int FullHeight => TemplateHeight * 2;

        public bool IsOutOfBoundsFull(int x, int fy) {
            return x < 0 || x >= TemplateWidth || fy < 0 || fy >= TemplateHeight * 2;
        }
        public CellType GetFull(int x, int fy) {
            if (IsOutOfBoundsFull(x, fy)) { return CellType.Empty; }
            return template[x, fy];
        }
        public void SetFull(int x, int fy, CellType type) {
            if (IsOutOfBoundsFull(x, fy)) { return; }
            template[x, fy] = type;
        }
        public bool IsEmptyFull(int x, int fy, bool cornersAreEmpty = true,
                bool engineIsEmpty = false) {
            if (IsOutOfBoundsFull(x, fy)) { return true; }
            return IsEmptyType(template[x, fy], cornersAreEmpty, engineIsEmpty);
        }
        public static bool IsEmptyType(CellType type, bool cornersAreEmpty = true,
                bool engineIsEmpty = false) {
            return type == CellType.Empty
                || type == CellType.BodyEmpty
                || type == CellType.CockpitEmpty
                || (engineIsEmpty && type == CellType.Engine)
                || (cornersAreEmpty && type == CellType.Corner);
        }
        // Hull stages try several asymmetry amounts on the same base shape
        public CellType[,] CloneTemplate() {
            return (CellType[,]) template.Clone();
        }
        public void RestoreTemplate(CellType[,] copy) {
            template = (CellType[,]) copy.Clone();
        }
        public int MirrorFullY(int fy) {
            return TemplateHeight * 2 - 1 - fy;
        }
        // Bottom-left tile of a cell in full template coordinates
        public int FullCellToWorld(int x, int fy) {
            return Pos.FromXY(CellToWorldX(x), CellToWorldY(fy));
        }

        public void SetCell(int x, int y, CellType type) {
            if (IsOutOfBounds(x, y)) {
                D.ErrSoft("Trying to set out of bounds cell! {0}x{1} {2}",
                    x, y, type);
                return;
            }
            template[x, TemplateHeight + y] = type;
            template[x, TemplateHeight - y - 1] = type;
        }

        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendFormat("ShipSpec: [{0}x{1}] Cell[{2}x{3}] Seed[{4}] Fill[{5}]",
                TemplateWidth, TemplateHeight * 2, CellWidth, CellHeight, Seed,
                FillRatio);
            if (!Arrays.IsEmpty(Islands)) {
                sb.AppendFormat(" Islands(x{0})\n", Islands.Count);
                foreach (var island in Islands) {
                    sb.AppendLine(island.ToString());
                }
            }
            sb.AppendLine();
            // Add column numbers
            sb.Append("   ");
            for (int x = 0; x < TemplateWidth; x++) {
                sb.Append(" ");
                sb.Append(x.ToString("00"));
            }
            sb.AppendLine();
            for (int y = 0; y < TemplateHeight * 2; y++) {
                // add row number
                if ((TemplateHeight - y - 1>= 0)) {
                    sb.Append(" ");
                }
                sb.Append((TemplateHeight - y - 1).ToString("00"));
                for (int x = 0; x < TemplateWidth; x++) {
                    sb.Append(CellString(GetCellMirrored(x, y)));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        [IgnoreMember]
        public float FillRatio {
            get {
                float filled = 0;
                float empty = 0;
                for (int x = 0; x < TemplateWidth; x++) {
                    for (int y = 0; y < TemplateHeight * 2; y++) {
                        if (IsEmpty(x, y)) {
                            empty++;
                        } else {
                            filled++;
                        }
                    }
                }
                return filled / empty;
            }
        }

        [IgnoreMember]
        public string ShortInfo => string.Format("ShipSpec: {0}x{1} ({2})",
            Width, Height, Seed);
        [IgnoreMember]
        public string GenCommand => string.Format("gen_ship {0} {1} {2}",
           Seed, Width, Height);

        public bool ForbidForEachTile;
        ///<summary>ONLY USE BEFORE REKT PHASE!!!!!
        ///There is no more symmetry when it comes to tiles, so it's
        ///mirrored by default</summary>
        public void ForEachTile(Action<int> action, int extra = 0) {
            if (ForbidForEachTile) {
                D.Err("ShipSpec.ForEachTile is forbidden! Are you trying it after a destruction phase?");
            }
            if (extra == 0) {
                Bounds.ForEach(action);
            } else {
                Bounds.ForEachExpanded(action, extra);
            }
        }

        public void ForEachMirrored(Action<int, int, CellType> action) {
            for (int x = 0; x < TemplateWidth; x++) {
                for (int y = 0; y < TemplateHeight * 2; y++) {
                    action(x, y, GetCellMirrored(x, y));
                }
            }
        }
        public void ForEach(Action<int, int, CellType> action) {
            for (int x = 0; x < TemplateWidth; x++) {
                for (int y = 0; y < TemplateHeight; y++) {
                    action(x, y, GetCellMirrored(x, TemplateHeight + y));
                }
            }
        }

        private string CellString(CellType type) {
            switch (type) {
                case CellType.Empty: return " . ";
                case CellType.BodyEmpty: return " b ";
                case CellType.CockpitEmpty: return " c ";
                case CellType.BodySolid: return "[B]";
                case CellType.CockpitSolid: return "[C]";
                case CellType.Skin: return "[S]";
                case CellType.Solid: return "[H]";
                case CellType.Cockpit: return "?C?";
                case CellType.Body: return "?B?";
                case CellType.Connection: return "[+]";
                case CellType.Corner: return "[>]";
                case CellType.Engine: return "[E]";
                default: return "???";
            }
        }

        public void Serialize() {
            serializedTemplate = new string[TemplateHeight * 2];
            for (int y = 0; y < TemplateHeight * 2; y++) {
                var row = new List<int>();
                for (int x = 0; x < TemplateWidth; x++) {
                    row.Add((int) template[x, y]);
                }
                serializedTemplate[y] = string.Join(",", row);
            }
        }
        public void Deserialize() {
            CreateRng();
            CreateTemplate();
            for (int y = 0; y < TemplateHeight * 2; y++) {
                var row = serializedTemplate[y].Split(',');
                for (int x = 0; x < TemplateWidth; x++) {
                    var typeInt = Int32.Parse(row[x]);
                    template[x, y] = (CellType) typeInt;
                }
            }
            serializedTemplate = null;
        }
    }
}
