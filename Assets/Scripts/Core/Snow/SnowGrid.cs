using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using AlpineSim.Core.Math;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Snow
{
    /// <summary>
    /// The shared state machine: a sparse, chunked struct-of-arrays snow grid at 0.5 m cells
    /// covering the map's cell space. Chunks (32×32 cells = 16 m) are allocated only where the
    /// skiable envelope (pistes, lots, roads, ramps, gun cones) needs them.
    ///
    /// Per cell: loose depth (fresh, density FreshDensity), packed depth with its own density,
    /// temperature, moisture, roughness (mogul/rut energy 0..1), last groom tick, groom direction,
    /// surface type, piste segment id, and salt load. Derived arrays (elevation, sun exposure,
    /// lateral position) are rebuilt from terrain and pistes and never saved.
    ///
    /// Mass per m² = (LooseMm * FreshDensity + PackedMm * Density) / 1000. Every mutator that moves
    /// snow conserves this quantity; only Deposit (snowfall, guns, hauled snow) and Remove
    /// (blower throw-off, melt, hauling away) change it.
    ///
    /// Persistence: IJsonConvertible packs each allocated chunk's arrays as deflate+base64 blobs.
    /// </summary>
    public sealed class SnowGrid : IJsonConvertible
    {
        public const int ChunkSize = 32;
        public const int CellsPerChunk = ChunkSize * ChunkSize;
        public const int ChunkShift = 5;
        public const int ChunkMask = ChunkSize - 1;

        public float CellSize { get; private set; } = 0.5f;
        public int CellsPerAxis { get; private set; }
        public int ChunksPerAxis { get; private set; }
        public float FreshDensity { get; set; } = 100f;

        /// <summary>chunkY * ChunksPerAxis + chunkX → chunk id, or -1 when not allocated.</summary>
        public int[] ChunkIndex { get; private set; }
        public int ChunkCount { get; private set; }
        private int _capacity;
        public int[] ChunkX { get; private set; }
        public int[] ChunkY { get; private set; }

        // ---- struct-of-arrays cell state (length = capacity * CellsPerChunk) ----
        public float[] LooseMm;
        public float[] PackedMm;
        public float[] Density;
        public float[] TempC;
        public float[] Moisture;
        public float[] Roughness;
        public int[] LastGroomTick;
        public byte[] GroomDir;
        public byte[] Surface;
        public short[] Segment;
        public float[] Salt;

        // ---- derived, rebuilt by SnowSystem.Initialize (never saved) ----
        public float[] Elevation;
        public float[] SunExposure;   // 0 (north-facing, shaded) .. 1 (south-facing, full sun)
        public float[] Lateral;       // -1 .. 1 across the piste; 0 on the centreline
        public float[] SlopeDeg;

        /// <summary>Snowpack outside the allocated envelope (a single scalar model used for physics and rendering).</summary>
        public float BackgroundLooseMm;
        public float BackgroundPackedMm;
        public float BackgroundDensity = 250f;

        private bool[] _dirty;
        private readonly List<int> _dirtyList = new List<int>();

        public SnowGrid() { }

        public SnowGrid(float mapSizeM, float cellSize, float freshDensity)
        {
            Configure(mapSizeM, cellSize, freshDensity);
        }

        public void Configure(float mapSizeM, float cellSize, float freshDensity)
        {
            CellSize = cellSize;
            FreshDensity = freshDensity;
            CellsPerAxis = (int)System.Math.Ceiling(mapSizeM / cellSize);
            ChunksPerAxis = (CellsPerAxis + ChunkSize - 1) / ChunkSize;
            ChunkIndex = new int[ChunksPerAxis * ChunksPerAxis];
            for (int i = 0; i < ChunkIndex.Length; i++) ChunkIndex[i] = -1;
            ChunkCount = 0;
            _capacity = 0;
            ChunkX = new int[0];
            ChunkY = new int[0];
            AllocateArrays(0);
        }

        public float CellAreaM2 => CellSize * CellSize;
        public int CellCount => ChunkCount * CellsPerChunk;

        // ------------------------------------------------------------------ addressing
        public bool CellCoords(Vec2 pos, out int cx, out int cy)
        {
            cx = (int)MathF.Floor(pos.X / CellSize);
            cy = (int)MathF.Floor(pos.Y / CellSize);
            return cx >= 0 && cy >= 0 && cx < CellsPerAxis && cy < CellsPerAxis;
        }

        public Vec2 CellCenter(int cx, int cy) => new Vec2((cx + 0.5f) * CellSize, (cy + 0.5f) * CellSize);

        /// <summary>Cell id for cell coordinates, or -1 when the chunk is not allocated or out of range.</summary>
        public int CellId(int cx, int cy)
        {
            if ((uint)cx >= (uint)CellsPerAxis || (uint)cy >= (uint)CellsPerAxis) return -1;
            int chunk = ChunkIndex[(cy >> ChunkShift) * ChunksPerAxis + (cx >> ChunkShift)];
            if (chunk < 0) return -1;
            return chunk * CellsPerChunk + ((cy & ChunkMask) << ChunkShift) + (cx & ChunkMask);
        }

        public int CellIdAt(Vec2 pos) => CellCoords(pos, out int cx, out int cy) ? CellId(cx, cy) : -1;

        public void CellCoordsOf(int id, out int cx, out int cy)
        {
            int chunk = id / CellsPerChunk;
            int local = id - chunk * CellsPerChunk;
            cx = ChunkX[chunk] * ChunkSize + (local & ChunkMask);
            cy = ChunkY[chunk] * ChunkSize + (local >> ChunkShift);
        }

        public Vec2 CellCenterOf(int id)
        {
            CellCoordsOf(id, out int cx, out int cy);
            return CellCenter(cx, cy);
        }

        public int ChunkOf(int id) => id / CellsPerChunk;

        /// <summary>Allocates the chunk containing the cell if needed and returns the cell id.</summary>
        public int EnsureCell(int cx, int cy)
        {
            if ((uint)cx >= (uint)CellsPerAxis || (uint)cy >= (uint)CellsPerAxis) return -1;
            int chunk = EnsureChunk(cx >> ChunkShift, cy >> ChunkShift);
            return chunk * CellsPerChunk + ((cy & ChunkMask) << ChunkShift) + (cx & ChunkMask);
        }

        public int EnsureCellAt(Vec2 pos) => CellCoords(pos, out int cx, out int cy) ? EnsureCell(cx, cy) : -1;

        public int EnsureChunk(int chunkX, int chunkY)
        {
            int key = chunkY * ChunksPerAxis + chunkX;
            int id = ChunkIndex[key];
            if (id >= 0) return id;
            if (ChunkCount == _capacity) Grow(System.Math.Max(64, _capacity * 2));
            id = ChunkCount++;
            ChunkIndex[key] = id;
            ChunkX[id] = chunkX;
            ChunkY[id] = chunkY;
            int b = id * CellsPerChunk;
            for (int i = 0; i < CellsPerChunk; i++)
            {
                Density[b + i] = BackgroundDensity;
                Segment[b + i] = -1;
                LastGroomTick[b + i] = -1;
                LooseMm[b + i] = BackgroundLooseMm;
                PackedMm[b + i] = BackgroundPackedMm;
            }
            MarkChunkDirty(id);
            return id;
        }

        private void Grow(int newCapacity)
        {
            Array.Resize(ref ChunkXBacking, newCapacity);
            Array.Resize(ref ChunkYBacking, newCapacity);
            ChunkX = ChunkXBacking; ChunkY = ChunkYBacking;
            int n = newCapacity * CellsPerChunk;
            Array.Resize(ref LooseMm, n); Array.Resize(ref PackedMm, n); Array.Resize(ref Density, n);
            Array.Resize(ref TempC, n); Array.Resize(ref Moisture, n); Array.Resize(ref Roughness, n);
            Array.Resize(ref LastGroomTick, n); Array.Resize(ref GroomDir, n); Array.Resize(ref Surface, n);
            Array.Resize(ref Segment, n); Array.Resize(ref Salt, n);
            Array.Resize(ref Elevation, n); Array.Resize(ref SunExposure, n); Array.Resize(ref Lateral, n); Array.Resize(ref SlopeDeg, n);
            Array.Resize(ref _dirty, newCapacity);
            _capacity = newCapacity;
        }

        private int[] ChunkXBacking = new int[0];
        private int[] ChunkYBacking = new int[0];

        private void AllocateArrays(int capacity)
        {
            ChunkXBacking = new int[capacity]; ChunkYBacking = new int[capacity];
            ChunkX = ChunkXBacking; ChunkY = ChunkYBacking;
            int n = capacity * CellsPerChunk;
            LooseMm = new float[n]; PackedMm = new float[n]; Density = new float[n]; TempC = new float[n];
            Moisture = new float[n]; Roughness = new float[n]; LastGroomTick = new int[n]; GroomDir = new byte[n];
            Surface = new byte[n]; Segment = new short[n]; Salt = new float[n];
            Elevation = new float[n]; SunExposure = new float[n]; Lateral = new float[n]; SlopeDeg = new float[n];
            _dirty = new bool[capacity];
            _capacity = capacity;
        }

        // ------------------------------------------------------------------ queries
        public float TotalDepthMm(int id) => LooseMm[id] + PackedMm[id];

        /// <summary>kg per m² of snow on the cell.</summary>
        public float MassKgPerM2(int id) => (LooseMm[id] * FreshDensity + PackedMm[id] * Density[id]) * 0.001f;

        /// <summary>Mean density of the whole column (loose + packed), kg/m³.</summary>
        public float ColumnDensity(int id)
        {
            float d = TotalDepthMm(id);
            return d <= 1e-4f ? Density[id] : MassKgPerM2(id) * 1000f / d;
        }

        public SurfaceType SurfaceOf(int id) => (SurfaceType)Surface[id];

        /// <summary>Depth at a world position; background snowpack where no chunk is allocated.</summary>
        public float DepthAtMm(Vec2 pos)
        {
            int id = CellIdAt(pos);
            return id < 0 ? BackgroundLooseMm + BackgroundPackedMm : TotalDepthMm(id);
        }

        public float DensityAt(Vec2 pos)
        {
            int id = CellIdAt(pos);
            return id < 0 ? BackgroundDensity : ColumnDensity(id);
        }

        /// <summary>Total snow mass in kg over all allocated cells (mass-conservation tests use this).</summary>
        public double TotalMassKg()
        {
            double sum = 0;
            int n = CellCount;
            float area = CellAreaM2;
            for (int i = 0; i < n; i++) sum += MassKgPerM2(i) * area;
            return sum;
        }

        public double MassKgWhere(Func<int, bool> predicate)
        {
            double sum = 0;
            int n = CellCount;
            float area = CellAreaM2;
            for (int i = 0; i < n; i++) if (predicate(i)) sum += MassKgPerM2(i) * area;
            return sum;
        }

        // ------------------------------------------------------------------ dirty tracking (renderer)
        public void MarkDirty(int cellId) => MarkChunkDirty(cellId / CellsPerChunk);

        public void MarkChunkDirty(int chunk)
        {
            if (chunk < 0 || chunk >= ChunkCount) return;
            if (_dirty[chunk]) return;
            _dirty[chunk] = true;
            _dirtyList.Add(chunk);
        }

        public void MarkAllDirty()
        {
            for (int i = 0; i < ChunkCount; i++) MarkChunkDirty(i);
        }

        /// <summary>Moves the dirty chunk ids into <paramref name="into"/> and clears the flags.</summary>
        public void ConsumeDirtyChunks(List<int> into)
        {
            into.AddRange(_dirtyList);
            for (int i = 0; i < _dirtyList.Count; i++) _dirty[_dirtyList[i]] = false;
            _dirtyList.Clear();
        }

        public int DirtyChunkCount => _dirtyList.Count;

        // ------------------------------------------------------------------ persistence
        public JsonNode ToJson()
        {
            var o = JsonNode.NewObject();
            o.Set("cellSize", CellSize);
            o.Set("cellsPerAxis", CellsPerAxis);
            o.Set("freshDensity", FreshDensity);
            o.Set("backgroundLooseMm", BackgroundLooseMm);
            o.Set("backgroundPackedMm", BackgroundPackedMm);
            o.Set("backgroundDensity", BackgroundDensity);
            o.Set("chunkCount", ChunkCount);
            var chunks = JsonNode.NewArray();
            for (int c = 0; c < ChunkCount; c++)
            {
                var cj = JsonNode.NewObject();
                cj.Set("x", ChunkX[c]);
                cj.Set("y", ChunkY[c]);
                cj.Set("data", EncodeChunk(c));
                chunks.Add(cj);
            }
            o.Set("chunks", chunks);
            return o;
        }

        public void FromJson(JsonNode node)
        {
            float cellSize = node.GetFloat("cellSize", 0.5f);
            int cellsPerAxis = node.GetInt("cellsPerAxis", 4096);
            Configure(cellsPerAxis * cellSize, cellSize, node.GetFloat("freshDensity", 100f));
            BackgroundLooseMm = node.GetFloat("backgroundLooseMm");
            BackgroundPackedMm = node.GetFloat("backgroundPackedMm");
            BackgroundDensity = node.GetFloat("backgroundDensity", 250f);
            var chunks = node["chunks"];
            for (int i = 0; i < chunks.Count; i++)
            {
                var cj = chunks[i];
                int id = EnsureChunk(cj.GetInt("x"), cj.GetInt("y"));
                DecodeChunk(id, cj.GetString("data"));
            }
            MarkAllDirty();
        }

        private string EncodeChunk(int c)
        {
            int b = c * CellsPerChunk;
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
                using (var w = new BinaryWriter(ds))
                {
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(LooseMm[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(PackedMm[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(Density[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(TempC[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(Moisture[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(Roughness[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(LastGroomTick[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(GroomDir[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(Surface[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(Segment[b + i]);
                    for (int i = 0; i < CellsPerChunk; i++) w.Write(Salt[b + i]);
                }
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        private void DecodeChunk(int c, string base64)
        {
            if (string.IsNullOrEmpty(base64)) return;
            int b = c * CellsPerChunk;
            using (var ms = new MemoryStream(Convert.FromBase64String(base64)))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var r = new BinaryReader(ds))
            {
                for (int i = 0; i < CellsPerChunk; i++) LooseMm[b + i] = r.ReadSingle();
                for (int i = 0; i < CellsPerChunk; i++) PackedMm[b + i] = r.ReadSingle();
                for (int i = 0; i < CellsPerChunk; i++) Density[b + i] = r.ReadSingle();
                for (int i = 0; i < CellsPerChunk; i++) TempC[b + i] = r.ReadSingle();
                for (int i = 0; i < CellsPerChunk; i++) Moisture[b + i] = r.ReadSingle();
                for (int i = 0; i < CellsPerChunk; i++) Roughness[b + i] = r.ReadSingle();
                for (int i = 0; i < CellsPerChunk; i++) LastGroomTick[b + i] = r.ReadInt32();
                for (int i = 0; i < CellsPerChunk; i++) GroomDir[b + i] = r.ReadByte();
                for (int i = 0; i < CellsPerChunk; i++) Surface[b + i] = r.ReadByte();
                for (int i = 0; i < CellsPerChunk; i++) Segment[b + i] = r.ReadInt16();
                for (int i = 0; i < CellsPerChunk; i++) Salt[b + i] = r.ReadSingle();
            }
        }
    }
}
