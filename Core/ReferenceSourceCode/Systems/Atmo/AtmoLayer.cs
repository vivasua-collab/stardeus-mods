using Game.Constants;
using KL.Utils;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Game.Systems.Atmo {
    // One atmosphere quantity (O2 or Temp) over the grid, plus the bookkeeping that
    // lets the simulation visit only the 8x8 chunks that matter. Invariant: a chunk
    // outside ActiveChunks has no hull and holds no gas in Data, DataIn or DataOut,
    // so AtmoChunkJob can skip it and read zeros from it through halos. A chunk
    // joins the set when it has hull, was written by the main thread since the last
    // step, wrote gas in the last step, or neighbours a chunk that did; it leaves
    // only after a step in which it wrote nothing, and its DataIn is zeroed on the
    // way out. Every main-thread writer of Data or Res must go through this class
    // so the marks stay complete. See AtmosphereSystem.md.
    public sealed class AtmoLayer {
        public readonly int Id;
        public readonly string Name;
        public readonly float SpaceLoss;
        public readonly int Width;
        public readonly int Height;
        public readonly int ChunksX;
        public readonly int ChunksY;
        public readonly int NumChunks;
        public NativeArray<float> Data;
        public NativeArray<float> DataIn;
        public NativeArray<float> DataOut;
        public NativeArray<float> Res;
        public NativeArray<float> ResIn;
        // Chunks the next job visits. Rebuilt in PrepareForJob
        public NativeList<int> ActiveChunks;
        // Written by the job: 1 if the chunk wrote any gas this step
        public NativeArray<byte> ChunkHasGas;
        // Cells held at a value through all but the last iteration of the step, so a
        // producer's tiles keep feeding while the gas spreads instead of being diluted
        // by the first iteration, yet still cool once per step. Positive holds at
        // least that value, negative holds at most its magnitude (both quantities are
        // never negative). Sparse: pinCells lists the set entries so clearing touches
        // only them
        public NativeArray<float> Pin;
        private NativeList<int> pinCells;
        private NativeArray<byte> chunkHasHull;
        private NativeArray<byte> chunkHullDirty;
        private NativeArray<byte> chunkDirty;
        private NativeArray<byte> chunkActive;
        private bool resDirty = true;

        public AtmoLayer(string Name, float lossInOpenSpaceMul, int width, int height) {
            Id = Hashes.S(Name);
            this.Name = Name;
            SpaceLoss = lossInOpenSpaceMul;
            Width = width;
            Height = height;
            ChunksX = width / Consts.MapChunkSize;
            ChunksY = height / Consts.MapChunkSize;
            NumChunks = ChunksX * ChunksY;
            int size = width * height;
            Data = new NativeArray<float>(size, Allocator.Persistent);
            DataIn = new NativeArray<float>(size, Allocator.Persistent);
            DataOut = new NativeArray<float>(size, Allocator.Persistent);
            Res = new NativeArray<float>(size, Allocator.Persistent);
            ResIn = new NativeArray<float>(size, Allocator.Persistent);
            ActiveChunks = new NativeList<int>(NumChunks, Allocator.Persistent);
            ChunkHasGas = new NativeArray<byte>(NumChunks, Allocator.Persistent);
            Pin = new NativeArray<float>(size, Allocator.Persistent);
            pinCells = new NativeList<int>(256, Allocator.Persistent);
            chunkHasHull = new NativeArray<byte>(NumChunks, Allocator.Persistent);
            chunkHullDirty = new NativeArray<byte>(NumChunks, Allocator.Persistent);
            chunkDirty = new NativeArray<byte>(NumChunks, Allocator.Persistent);
            chunkActive = new NativeArray<byte>(NumChunks, Allocator.Persistent);
        }

        public void Load(float[] data, float[] res) {
            Data.CopyFrom(data);
            Res.CopyFrom(res);
            // Nothing is known about where the gas and the hull are yet, so the
            // first step visits everything and the set settles from there
            resDirty = true;
            for (int c = 0; c < NumChunks; c++) {
                chunkDirty[c] = 1;
                chunkHullDirty[c] = 1;
            }
        }

        public void Cleanup() {
            if (Data.IsCreated) {
                Data.Dispose();
            }
            if (Res.IsCreated) {
                Res.Dispose();
            }
            if (DataIn.IsCreated) {
                DataIn.Dispose();
            }
            if (DataOut.IsCreated) {
                DataOut.Dispose();
            }
            if (ResIn.IsCreated) {
                ResIn.Dispose();
            }
            if (ActiveChunks.IsCreated) {
                ActiveChunks.Dispose();
            }
            if (ChunkHasGas.IsCreated) {
                ChunkHasGas.Dispose();
            }
            if (Pin.IsCreated) {
                Pin.Dispose();
            }
            if (pinCells.IsCreated) {
                pinCells.Dispose();
            }
            if (chunkHasHull.IsCreated) {
                chunkHasHull.Dispose();
            }
            if (chunkHullDirty.IsCreated) {
                chunkHullDirty.Dispose();
            }
            if (chunkDirty.IsCreated) {
                chunkDirty.Dispose();
            }
            if (chunkActive.IsCreated) {
                chunkActive.Dispose();
            }
        }

        public int ChunkOf(int pos) {
            int x = pos / Height;
            int y = pos - x * Height;
            return (x / Consts.MapChunkSize) * ChunksY + y / Consts.MapChunkSize;
        }

        public void ChunkOrigin(int chunk, out int x0, out int y0) {
            x0 = (chunk / ChunksY) * Consts.MapChunkSize;
            y0 = (chunk % ChunksY) * Consts.MapChunkSize;
        }

        public bool IsChunkActive(int chunk) {
            return chunkActive[chunk] != 0;
        }

        private void MarkDirty(int pos) {
            chunkDirty[ChunkOf(pos)] = 1;
        }

        // Release builds check no NativeArray index, so a write outside the grid lands in the
        // allocator's own bookkeeping and the game only crashes much later, when the layer is
        // freed on quit (STA-1927). Every writer goes through this: the write is dropped and
        // the first one is logged with its caller, so the next report names it.
        private bool loggedBadPos;
        private bool IsInside(int pos) {
            if ((uint) pos < (uint) Data.Length) { return true; }
            if (!loggedBadPos) {
                loggedBadPos = true;
                D.Err("Atmo layer {0} write outside the grid at {1} (size {2}). Dropped.\n{3}",
                    Name, pos, Data.Length, System.Environment.StackTrace);
            }
            return false;
        }

        public float At(int i) {
            return Data[i];
        }
        public float ResAt(int i) {
            return Res[i];
        }

        public void Set(int pos, float val) {
            if (!IsInside(pos)) { return; }
            Data[pos] = val;
            MarkDirty(pos);
        }

        public void SetRes(int pos, float val) {
            if (!IsInside(pos)) { return; }
            Res[pos] = val;
            resDirty = true;
            chunkHullDirty[ChunkOf(pos)] = 1;
        }

        public void PinAtLeast(int pos, float value) {
            if (value <= 0f) { return; }
            if (!IsInside(pos)) { return; }
            ref var p = ref Pin.GetUnsafeRef(pos);
            if (p == 0f) {
                pinCells.Add(pos);
            }
            if (p < value) {
                p = value;
            }
            MarkDirty(pos);
        }

        // A ceiling at or below 0 is never wanted (the quantities are never negative)
        // and could not be told apart from no pin, so it is ignored
        public void PinAtMost(int pos, float value) {
            if (value <= 0f) { return; }
            if (!IsInside(pos)) { return; }
            ref var p = ref Pin.GetUnsafeRef(pos);
            if (p == 0f) {
                pinCells.Add(pos);
            }
            if (p >= 0f || -p > value) {
                p = -value;
            }
            MarkDirty(pos);
        }

        public void ClearPins() {
            for (int i = 0; i < pinCells.Length; i++) {
                Pin[pinCells[i]] = 0f;
            }
            pinCells.Clear();
        }

        public void PrepareForJob() {
            if (resDirty) {
                resDirty = false;
                ResIn.CopyFrom(Res);
            }
            RefreshHullFlags();
            RebuildActiveChunks();
            for (int i = 0; i < ActiveChunks.Length; i++) {
                CopyChunk(Data, DataIn, ActiveChunks[i]);
            }
        }

        public void FlushData() {
            if (!DataOut.IsCreated) {
                D.Err("DataOut is not created for atmo layer {0}. Can't flush!",
                    Name);
                return;
            }
            for (int i = 0; i < ActiveChunks.Length; i++) {
                CopyChunk(DataOut, Data, ActiveChunks[i]);
            }
        }

        private void RefreshHullFlags() {
            for (int c = 0; c < NumChunks; c++) {
                if (chunkHullDirty[c] == 0) { continue; }
                chunkHullDirty[c] = 0;
                chunkHasHull[c] = ChunkHasHull(c);
            }
        }

        private byte ChunkHasHull(int chunk) {
            ChunkOrigin(chunk, out int x0, out int y0);
            for (int dx = 0; dx < Consts.MapChunkSize; dx++) {
                int start = (x0 + dx) * Height + y0;
                for (int dy = 0; dy < Consts.MapChunkSize; dy++) {
                    if (Res[start + dy] >= AtmoRes.Space) {
                        return 1;
                    }
                }
            }
            return 0;
        }

        private void RebuildActiveChunks() {
            ActiveChunks.Clear();
            for (int c = 0; c < NumChunks; c++) {
                bool active = chunkHasHull[c] != 0 || chunkDirty[c] != 0
                    || ChunkHasGas[c] != 0 || NeighborHasGas(c);
                chunkDirty[c] = 0;
                if (active) {
                    chunkActive[c] = 1;
                    ActiveChunks.Add(c);
                } else if (chunkActive[c] != 0) {
                    // Leaving the set. Data and DataOut are already zero here because
                    // the last step wrote no gas; DataIn still holds that step's input
                    chunkActive[c] = 0;
                    ClearChunk(DataIn, c);
                }
            }
        }

        private bool NeighborHasGas(int chunk) {
            int cx = chunk / ChunksY;
            int cy = chunk % ChunksY;
            for (int dx = -1; dx <= 1; dx++) {
                int nx = cx + dx;
                if (nx < 0 || nx >= ChunksX) { continue; }
                for (int dy = -1; dy <= 1; dy++) {
                    if (dx == 0 && dy == 0) { continue; }
                    int ny = cy + dy;
                    if (ny < 0 || ny >= ChunksY) { continue; }
                    if (ChunkHasGas[nx * ChunksY + ny] != 0) {
                        return true;
                    }
                }
            }
            return false;
        }

        // Column-major layout puts a chunk's 8 cells of one column next to each
        // other, so a chunk is 8 short contiguous runs
        private unsafe void CopyChunk(NativeArray<float> src, NativeArray<float> dst, int chunk) {
            ChunkOrigin(chunk, out int x0, out int y0);
            var s = (float*) src.GetUnsafeReadOnlyPtr();
            var d = (float*) dst.GetUnsafePtr();
            for (int dx = 0; dx < Consts.MapChunkSize; dx++) {
                int start = (x0 + dx) * Height + y0;
                UnsafeUtility.MemCpy(d + start, s + start, Consts.MapChunkSize * sizeof(float));
            }
        }

        private unsafe void ClearChunk(NativeArray<float> dst, int chunk) {
            ChunkOrigin(chunk, out int x0, out int y0);
            var d = (float*) dst.GetUnsafePtr();
            for (int dx = 0; dx < Consts.MapChunkSize; dx++) {
                int start = (x0 + dx) * Height + y0;
                UnsafeUtility.MemClear(d + start, Consts.MapChunkSize * sizeof(float));
            }
        }

        public float Increase(int pos, float amount, float max) {
            if (!IsInside(pos)) { return 0f; }
            ref var value = ref Data.GetUnsafeRef(pos);
            if (value < max) {
                value += amount;
                MarkDirty(pos);
            }
            return value;
        }

        // Returns the value after the increase, or 0 when the cell has too little hull
        public float Increase(int pos, float amount, float max, float minRes) {
            if (!IsInside(pos)) { return 0f; }
            if (Res[pos] < minRes) { return 0f; }
            ref var value = ref Data.GetUnsafeRef(pos);
            if (value < max) {
                value += amount;
                MarkDirty(pos);
            }
            return value;
        }

        public void SetAtLeast(int pos, float amount) {
            if (!IsInside(pos)) { return; }
            ref var value = ref Data.GetUnsafeRef(pos);
            if (value < amount) {
                value = amount;
                MarkDirty(pos);
            }
        }

        // Takes up to amount away, never below zero. Returns the level after: a drained tile
        // reads 0, which is what tells a breathing being it is out of air
        public float Reduce(int pos, float amount) {
            if (!IsInside(pos)) { return 0f; }
            ref var level = ref Data.GetUnsafeRef(pos);
            var newLvl = level - amount;
            level = newLvl <= 0f ? 0f : newLvl;
            return level;
        }

        // No dirty mark: lowering a value cannot put gas into a chunk that holds none,
        // and this runs for every breathing being every tick
        public void Decrease(int pos, float amount) {
            if (!IsInside(pos)) { return; }
            ref var value = ref Data.GetUnsafeRef(pos);
            var newVal = value - amount;
            if (newVal < 0) {
                value = 0f;
            } else {
                value = newVal;
            }
        }
    }
}
