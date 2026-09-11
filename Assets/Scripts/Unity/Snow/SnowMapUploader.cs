using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Snow;
using UnityEngine;

namespace AlpineSim.Unity.Snow
{
    /// <summary>
    /// Converts dirty snow-grid chunks into texels of the snow map (RGBAHalf) and the surface map
    /// (RGBA32) and uploads them, either through the SnowDeform compute kernel (one dispatch per
    /// frame) or, without compute support, through Texture2D.SetPixelData + Apply. Both paths write
    /// identical values: the conversion is shared and runs on the CPU.
    /// Texel layout: R depth m, G density fraction, B roughness, A groom age; surface: R type/255, G groom dir/2pi, B lateral, A 0.
    /// </summary>
    public sealed class SnowMapUploader : IDisposable
    {
        public Texture SnowMap => _useCompute ? (Texture)_snowRt : _snowTex;
        public Texture SurfaceMap => _useCompute ? (Texture)_surfRt : _surfTex;
        public int Size { get; }
        public bool UsesCompute => _useCompute;
        public int TexelsUploadedLastFrame { get; private set; }
        public int PendingChunks => _pending.Count;

        private readonly SnowGrid _grid;
        private readonly RenderData _render;
        private readonly float _groomAgeHours;
        private readonly int _cellsPerTexel;     // cells per texel edge
        private readonly int _texelsPerChunk;    // texels per chunk edge
        private readonly bool _useCompute;
        private readonly int _maxTexelsPerFrame;

        // GPU path
        private RenderTexture _snowRt, _surfRt;
        private ComputeShader _compute;
        private int _kernel;
        private ComputeBuffer _coordBuf, _snowBuf, _surfBuf;
        private uint[] _coords;
        private Vector4[] _snowVals, _surfVals;
        // CPU path
        private Texture2D _snowTex, _surfTex;
        private ushort[] _snowHalf;    // 4 halves per texel
        private Color32[] _surfBytes;
        private bool _cpuDirty;

        private readonly List<int> _pending = new List<int>();
        private readonly List<int> _tmp = new List<int>();
        private long _tick;

        private const float DensityLo = 100f, DensityHi = 600f;

        public SnowMapUploader(SnowGrid grid, RenderData render, float mapSizeM, float groomAgeHours, ComputeShader compute)
        {
            _grid = grid;
            _render = render;
            _groomAgeHours = Mathf.Max(1f, groomAgeHours);
            float texelsPerMeter = Mathf.Max(0.25f, render.SnowMapTexelsPerMeter);
            _cellsPerTexel = Mathf.Max(1, Mathf.RoundToInt(1f / (texelsPerMeter * grid.CellSize)));
            Size = Mathf.Clamp(Mathf.CeilToInt(grid.CellsPerAxis / (float)_cellsPerTexel), 64, 8192);
            _texelsPerChunk = Mathf.Max(1, SnowGrid.ChunkSize / _cellsPerTexel);
            _maxTexelsPerFrame = Mathf.Max(_texelsPerChunk * _texelsPerChunk, render.SnowMapMaxUpdatesPerFrame / (_cellsPerTexel * _cellsPerTexel));
            _useCompute = compute != null && SystemInfo.supportsComputeShaders;
            if (_useCompute)
            {
                _compute = compute;
                _kernel = compute.FindKernel("CSMain");
                _snowRt = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "SnowMap" };
                _snowRt.Create();
                _surfRt = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SurfaceMap" };
                _surfRt.Create();
                int cap = _maxTexelsPerFrame;
                _coords = new uint[cap]; _snowVals = new Vector4[cap]; _surfVals = new Vector4[cap];
                _coordBuf = new ComputeBuffer(cap, sizeof(uint));
                _snowBuf = new ComputeBuffer(cap, sizeof(float) * 4);
                _surfBuf = new ComputeBuffer(cap, sizeof(float) * 4);
            }
            else
            {
                _snowTex = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "SnowMap" };
                _surfTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SurfaceMap" };
                _snowHalf = new ushort[Size * Size * 4];
                _surfBytes = new Color32[Size * Size];
            }
        }

        /// <summary>Full rebuild (new game / load): every texel, including the background pack outside the envelope.</summary>
        public void Rebuild(long tick)
        {
            _tick = tick;
            if (_useCompute)
            {
                // background everywhere first, then every allocated chunk through the normal path
                int total = Size * Size;
                int written = 0;
                while (written < total)
                {
                    int n = Mathf.Min(_coords.Length, total - written);
                    for (int i = 0; i < n; i++)
                    {
                        int t = written + i;
                        int x = t % Size, y = t / Size;
                        _coords[i] = (uint)x | ((uint)y << 16);
                        ConvertTexel(x, y, out _snowVals[i], out _surfVals[i]);
                    }
                    Dispatch(n);
                    written += n;
                }
                _pending.Clear();
                _grid.ConsumeDirtyChunks(_tmp); _tmp.Clear();
                return;
            }
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    WriteCpuTexel(x, y);
            _cpuDirty = true;
            _pending.Clear();
            _grid.ConsumeDirtyChunks(_tmp); _tmp.Clear();
            FlushCpu();
        }

        /// <summary>Per frame: drain dirty chunks (bounded per frame) and upload.</summary>
        public void Update(long tick)
        {
            _tick = tick;
            _tmp.Clear();
            _grid.ConsumeDirtyChunks(_tmp);
            _pending.AddRange(_tmp);
            TexelsUploadedLastFrame = 0;
            if (_pending.Count == 0) { if (_cpuDirty) FlushCpu(); return; }
            int texelsPerChunk = _texelsPerChunk * _texelsPerChunk;
            int chunksThisFrame = Mathf.Max(1, _maxTexelsPerFrame / texelsPerChunk);
            int take = Mathf.Min(chunksThisFrame, _pending.Count);
            int n = 0;
            for (int c = 0; c < take; c++)
            {
                int chunk = _pending[c];
                int cx0 = _grid.ChunkX[chunk] * SnowGrid.ChunkSize / _cellsPerTexel;
                int cy0 = _grid.ChunkY[chunk] * SnowGrid.ChunkSize / _cellsPerTexel;
                for (int ty = 0; ty < _texelsPerChunk; ty++)
                {
                    for (int tx = 0; tx < _texelsPerChunk; tx++)
                    {
                        int x = cx0 + tx, y = cy0 + ty;
                        if (x >= Size || y >= Size) continue;
                        if (_useCompute)
                        {
                            _coords[n] = (uint)x | ((uint)y << 16);
                            ConvertTexel(x, y, out _snowVals[n], out _surfVals[n]);
                        }
                        else WriteCpuTexel(x, y);
                        n++;
                    }
                }
            }
            _pending.RemoveRange(0, take);
            TexelsUploadedLastFrame = n;
            if (_useCompute) { if (n > 0) Dispatch(n); }
            else { _cpuDirty = true; FlushCpu(); }
        }

        private void Dispatch(int count)
        {
            _coordBuf.SetData(_coords, 0, 0, count);
            _snowBuf.SetData(_snowVals, 0, 0, count);
            _surfBuf.SetData(_surfVals, 0, 0, count);
            _compute.SetInt("_Count", count);
            _compute.SetBuffer(_kernel, "_Coords", _coordBuf);
            _compute.SetBuffer(_kernel, "_Snow", _snowBuf);
            _compute.SetBuffer(_kernel, "_Surf", _surfBuf);
            _compute.SetTexture(_kernel, "_SnowMap", _snowRt);
            _compute.SetTexture(_kernel, "_SurfaceMap", _surfRt);
            _compute.Dispatch(_kernel, (count + 63) / 64, 1, 1);
        }

        private void FlushCpu()
        {
            if (!_cpuDirty) return;
            _snowTex.SetPixelData(_snowHalf, 0);
            _snowTex.Apply(false, false);
            _surfTex.SetPixelData(_surfBytes, 0);
            _surfTex.Apply(false, false);
            _cpuDirty = false;
        }

        private void WriteCpuTexel(int x, int y)
        {
            ConvertTexel(x, y, out var s, out var f);
            int i = (y * Size + x) * 4;
            _snowHalf[i] = FloatToHalf(s.x); _snowHalf[i + 1] = FloatToHalf(s.y); _snowHalf[i + 2] = FloatToHalf(s.z); _snowHalf[i + 3] = FloatToHalf(s.w);
            _surfBytes[y * Size + x] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(f.x * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(f.y * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(f.z * 255f), 0, 255), 0);
        }

        /// <summary>Average the cells under a texel. Unallocated cells carry the background pack.</summary>
        private void ConvertTexel(int x, int y, out Vector4 snow, out Vector4 surf)
        {
            float depth = 0f, dens = 0f, rough = 0f, age = 0f, dir = 0f, lateral = 0f;
            int type = 0, n = 0, allocated = 0;
            float ageTicks = _groomAgeHours * Core.Sim.SimTime.TicksPerHour;
            for (int cy = 0; cy < _cellsPerTexel; cy++)
            {
                for (int cx = 0; cx < _cellsPerTexel; cx++)
                {
                    int id = _grid.CellId(x * _cellsPerTexel + cx, y * _cellsPerTexel + cy);
                    n++;
                    if (id < 0)
                    {
                        depth += (_grid.BackgroundLooseMm + _grid.BackgroundPackedMm) * 0.001f;
                        dens += _grid.BackgroundDensity;
                        age += 1f;
                        continue;
                    }
                    allocated++;
                    depth += _grid.TotalDepthMm(id) * 0.001f;
                    dens += _grid.ColumnDensity(id);
                    rough += _grid.Roughness[id];
                    long last = _grid.LastGroomTick[id];
                    age += last <= 0 ? 1f : Mathf.Clamp01((_tick - last) / ageTicks);
                    dir += _grid.GroomDir[id] / 255f;
                    lateral += _grid.Lateral != null ? _grid.Lateral[id] * 0.5f + 0.5f : 0.5f;
                    type = _grid.Surface[id];
                }
            }
            float inv = 1f / Mathf.Max(1, n);
            float invA = 1f / Mathf.Max(1, allocated);
            snow = new Vector4(depth * inv, Mathf.Clamp01((dens * inv - DensityLo) / (DensityHi - DensityLo)), allocated > 0 ? rough * invA : 0f, age * inv);
            surf = new Vector4(type / 255f, allocated > 0 ? dir * invA : 0f, allocated > 0 ? lateral * invA : 0.5f, 0f);
        }

        /// <summary>IEEE 754 binary16 conversion (round-to-nearest-even); no Unity dependency so the CPU path is bit-exact.</summary>
        public static ushort FloatToHalf(float value)
        {
            uint f = (uint)BitConverter.SingleToInt32Bits(value);
            uint sign = (f >> 16) & 0x8000u;
            int exp = (int)((f >> 23) & 0xFF) - 127 + 15;
            uint mant = f & 0x7FFFFFu;
            if (exp <= 0)
            {
                if (exp < -10) return (ushort)sign;
                mant |= 0x800000u;
                int shift = 14 - exp;
                uint half = mant >> shift;
                uint rem = mant & ((1u << shift) - 1);
                uint halfway = 1u << (shift - 1);
                if (rem > halfway || (rem == halfway && (half & 1u) != 0)) half++;
                return (ushort)(sign | half);
            }
            if (exp >= 31) return (ushort)(sign | 0x7C00u);
            uint h = sign | ((uint)exp << 10) | (mant >> 13);
            uint r = mant & 0x1FFFu;
            if (r > 0x1000u || (r == 0x1000u && (h & 1u) != 0)) h++;
            return (ushort)h;
        }

        public void Dispose()
        {
            _coordBuf?.Release(); _snowBuf?.Release(); _surfBuf?.Release();
            if (_snowRt != null) { _snowRt.Release(); UnityEngine.Object.Destroy(_snowRt); }
            if (_surfRt != null) { _surfRt.Release(); UnityEngine.Object.Destroy(_surfRt); }
            if (_snowTex != null) UnityEngine.Object.Destroy(_snowTex);
            if (_surfTex != null) UnityEngine.Object.Destroy(_surfTex);
        }
    }
}
