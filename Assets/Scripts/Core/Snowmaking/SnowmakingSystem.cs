using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Fleet;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Snow;
using AlpineSim.Core.Vehicles;
using AlpineSim.Core.Weather;

namespace AlpineSim.Core.Snowmaking
{
    /// <summary>
    /// Snow guns, hydrants, pumps, compressors and the reservoir. A gun runs when it is connected to
    /// a hydrant, the wet-bulb at its elevation is below its output curve's threshold, and the pump
    /// (and compressor for lance guns) network has flow left. Output (m³ snow / h) comes from the
    /// gun def's gunOutputByWetBulb curve; snow lands as SnowOps.DepositPacked in a cone downwind
    /// of the aim (reach and half-angle from the def's specs) at the def's snow density; water =
    /// snow / snowmaking.snowPerWaterM3, energy = gun power + pump/compressor share. Water and power
    /// are posted hourly; the reservoir drains and refills. Ticks after Weather, before Snow.
    /// </summary>
    public sealed class SnowmakingSystem : ISimSystem
    {
        public string Name => "Snowmaking";

        private float _baseElevation;
        private readonly List<int> _cells = new List<int>(64);
        private float _hourKwh, _hourWater, _hourSnow;

        public void Initialize(SimContext ctx, bool newGame)
        {
            var s = ctx.World.Snowmaking;
            var scen = ctx.Sim.Scenario;
            var stations = ctx.Data.Stations;
            _baseElevation = ctx.Terrain.SampleHeight(scen.BaseArea.Pos);
            if (newGame)
            {
                foreach (var h in scen.Hydrants) s.Hydrants.Add(new Hydrant { Id = s.NextHydrantId++, ExternalId = h.Id, Pos = h.Pos, PisteId = h.PisteId, Built = true });
                s.ReservoirCapacityM3 = scen.ReservoirCapacityM3 > 0f ? scen.ReservoirCapacityM3 : stations.Reservoir.CapacityM3;
                s.ReservoirM3 = scen.ReservoirInitialM3 > 0f ? scen.ReservoirInitialM3 : stations.Reservoir.InitialM3;
                s.ReservoirInflowM3PerHour = stations.Reservoir.InflowM3PerHour;
                foreach (var id in scen.StartPumpStations) AddStation(ctx, "pump", id, 0);
                foreach (var id in scen.StartCompressorStations) AddStation(ctx, "compressor", id, 0);
            }
            RecomputeCapacity(ctx);
        }

        private BuiltStation AddStation(SimContext ctx, string kind, string defId, double cost)
        {
            var s = ctx.World.Snowmaking;
            var st = new BuiltStation { Id = s.NextStationId++, DefId = defId, Kind = kind, BuiltDay = ctx.Time.Day, Cost = cost, Pos = ctx.Sim.Scenario.Landmark("pumpHouse").Pos };
            s.Stations.Add(st);
            return st;
        }

        private void RecomputeCapacity(SimContext ctx)
        {
            var s = ctx.World.Snowmaking;
            var stations = ctx.Data.Stations;
            s.PumpCapacityLps = 0f; s.CompressorCapacityM3Min = 0f; s.PowerAvailableKw = 0f;
            foreach (var st in s.Stations)
            {
                if (st.Kind == "pump") { foreach (var d in stations.PumpStations) if (d.Id == st.DefId) { s.PumpCapacityLps += d.FlowLps; s.PowerAvailableKw += d.PowerKw; } }
                else { foreach (var d in stations.CompressorStations) if (d.Id == st.DefId) { s.CompressorCapacityM3Min += d.AirM3PerMin; s.PowerAvailableKw += d.PowerKw; } }
            }
            // pump/compressor machines owned as vehicles (category Snowmaking with Pump/Compress roles) add capacity too
            foreach (var v in ctx.World.Vehicles.List)
            {
                var d = ctx.Data.Vehicle(v.DefId);
                if (d == null) continue;
                if (d.HasRole(VehicleRole.Pump)) s.PumpCapacityLps += d.SpecOr("pumpLps", 0f);
                if (d.HasRole(VehicleRole.Compress)) s.CompressorCapacityM3Min += d.SpecOr("airM3PerMin", 0f);
            }
        }

        public IReadOnlyList<SnowGunState> Guns(SimContext ctx) => ctx.World.Snowmaking.Guns;
        public IReadOnlyList<Hydrant> Hydrants(SimContext ctx) => ctx.World.Snowmaking.Hydrants;

        // ------------------------------------------------------------------ placement
        public SnowGunState Place(SimContext ctx, int vehicleId, Vec2 pos, float headingDeg, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) { reason = "no such machine"; return null; }
            var def = ctx.Data.Vehicle(v.DefId);
            if (def == null || !def.HasRole(VehicleRole.MakeSnow)) { reason = v.Name + " is not a snow gun"; return null; }
            if (v.PlacedGunId >= 0) { reason = v.Name + " is already placed; pick it up first"; return null; }
            if (!def.HasCurve("gunOutputByWetBulb")) { reason = def.DisplayName + " has no output curve"; return null; }
            var s = ctx.World.Snowmaking;
            var gun = new SnowGunState { Id = s.NextGunId++, DefId = def.Id, VehicleId = v.Id, Pos = pos, HeadingDeg = headingDeg, Fixed = def.IsStationary, AutoRun = true, Status = "placed, not connected" };
            s.Guns.Add(gun);
            v.Pos = pos;
            v.Heading = (90f - headingDeg) * MathUtil.Deg2Rad;
            v.PlacedGunId = gun.Id;
            var h = NearestHydrant(ctx, pos, ctx.Tuning.F("snowmaking.hoseMaxM"));
            if (h != null && h.ConnectedGunId < 0) Connect(ctx, gun.Id, h.Id, out _);
            ctx.Events.Publish(new GunPlacedEvent { GunId = gun.Id });
            return gun;
        }

        public bool Remove(SimContext ctx, int gunId)
        {
            var s = ctx.World.Snowmaking;
            var gun = s.Gun(gunId);
            if (gun == null) return false;
            Disconnect(ctx, gunId);
            s.Guns.Remove(gun);
            var v = ctx.World.Vehicles.Get(gun.VehicleId);
            if (v != null) v.PlacedGunId = -1;
            ctx.Events.Publish(new GunRemovedEvent { GunId = gunId });
            return true;
        }

        public bool Connect(SimContext ctx, int gunId, int hydrantId, out string reason)
        {
            reason = "";
            var s = ctx.World.Snowmaking;
            var gun = s.Gun(gunId); var h = s.HydrantById(hydrantId);
            if (gun == null || h == null) { reason = "no such gun or hydrant"; return false; }
            if (!h.Built) { reason = "hydrant not built"; return false; }
            float max = ctx.Tuning.F("snowmaking.hoseMaxM");
            float d = Vec2.Distance(gun.Pos, h.Pos);
            if (d > max) { reason = "hydrant is " + MathF.Round(d) + " m away; hose reaches " + max + " m"; return false; }
            if (h.ConnectedGunId >= 0 && h.ConnectedGunId != gunId) { reason = "hydrant already in use"; return false; }
            Disconnect(ctx, gunId);
            gun.HydrantId = hydrantId;
            h.ConnectedGunId = gunId;
            gun.Status = "connected";
            return true;
        }

        public void Disconnect(SimContext ctx, int gunId)
        {
            var s = ctx.World.Snowmaking;
            var gun = s.Gun(gunId);
            if (gun == null) return;
            var h = s.HydrantById(gun.HydrantId);
            if (h != null && h.ConnectedGunId == gunId) h.ConnectedGunId = -1;
            gun.HydrantId = -1;
            gun.Running = false;
        }

        public void SetRunning(SimContext ctx, int gunId, bool running)
        {
            var gun = ctx.World.Snowmaking.Gun(gunId);
            if (gun == null) return;
            gun.Running = running;
            gun.AutoRun = false;
            ctx.Events.Publish(new GunStateEvent { GunId = gunId, Running = running, Status = gun.Status });
        }

        public void SetAuto(SimContext ctx, int gunId, bool auto) { var g = ctx.World.Snowmaking.Gun(gunId); if (g != null) g.AutoRun = auto; }
        public void SetAim(SimContext ctx, int gunId, float headingDeg) { var g = ctx.World.Snowmaking.Gun(gunId); if (g != null) g.HeadingDeg = headingDeg; }

        public float WetBulbAt(SimContext ctx, Vec2 pos)
        {
            var w = ctx.World.Weather.Current;
            float elev = ctx.Terrain.SampleHeight(pos);
            float t = WetBulb.TempAtElevation(w.TempC, _baseElevation, elev, ctx.World.Weather.LapseRateCPer100m);
            return WetBulb.FromTempAndHumidity(t, w.HumidityPct, WetBulb.PressureAtElevationHpa(elev));
        }

        public float PotentialOutputM3PerHour(SimContext ctx, SnowGunState gun)
        {
            var def = ctx.Data.Vehicle(gun.DefId);
            if (def == null || !def.HasCurve("gunOutputByWetBulb")) return 0f;
            float out1 = MathF.Max(0f, def.Curve("gunOutputByWetBulb", WetBulbAt(ctx, gun.Pos)));
            return out1 * (1f - gun.NozzleWear * 0.3f);
        }

        public bool CanRun(SimContext ctx, SnowGunState gun, out string reason)
        {
            reason = "";
            var s = ctx.World.Snowmaking;
            if (gun.HydrantId < 0) { reason = "not connected to a hydrant"; return false; }
            var def = ctx.Data.Vehicle(gun.DefId);
            if (def == null) { reason = "unknown gun"; return false; }
            if (s.ReservoirM3 <= 1f) { reason = "reservoir empty"; return false; }
            if (s.PumpCapacityLps <= 0f) { reason = "no pump station"; return false; }
            if (def.SpecOr("airM3PerMin", 0f) > 0f && s.CompressorCapacityM3Min <= 0f) { reason = "lance gun needs a compressor"; return false; }
            float wb = WetBulbAt(ctx, gun.Pos);
            if (PotentialOutputM3PerHour(ctx, gun) <= 0.01f) { reason = "wet-bulb " + wb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " C too warm"; return false; }
            return true;
        }

        public Hydrant NearestHydrant(SimContext ctx, Vec2 pos, float maxDistanceM)
        {
            Hydrant best = null; float bd = maxDistanceM * maxDistanceM;
            foreach (var h in ctx.World.Snowmaking.Hydrants)
            {
                if (!h.Built) continue;
                float d = Vec2.SqrDistance(h.Pos, pos);
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        public bool BuildStation(SimContext ctx, string kind, string defId, out string reason)
        {
            reason = "";
            var stations = ctx.Data.Stations;
            double cost = 0; bool found = false;
            if (kind == "pump") foreach (var d in stations.PumpStations) if (d.Id == defId) { cost = d.Cost; found = true; }
            if (kind == "compressor") foreach (var d in stations.CompressorStations) if (d.Id == defId) { cost = d.Cost; found = true; }
            if (!found) { reason = "unknown station " + defId; return false; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Construction, cost, kind + " station " + defId, out reason)) return false;
            AddStation(ctx, kind, defId, cost);
            RecomputeCapacity(ctx);
            ctx.Sim.Log("Built " + kind + " station " + defId + ".");
            return true;
        }

        public bool InstallHydrant(SimContext ctx, Vec2 pos, string pisteId, out string reason)
        {
            reason = "";
            var s = ctx.World.Snowmaking;
            double cost = ctx.Data.Stations.HydrantInstallCost;
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Construction, cost, "hydrant on " + pisteId, out reason)) return false;
            s.Hydrants.Add(new Hydrant { Id = s.NextHydrantId++, ExternalId = "h" + s.NextHydrantId, Pos = pos, PisteId = pisteId ?? "", Built = true });
            return true;
        }

        // ------------------------------------------------------------------ tick
        public void Tick(SimContext ctx, float dt)
        {
            var s = ctx.World.Snowmaking;
            var t = ctx.Tuning;
            if (ctx.Time.IsMinuteStart) RecomputeCapacity(ctx);
            // reservoir inflow
            s.ReservoirM3 = MathF.Min(s.ReservoirCapacityM3, s.ReservoirM3 + s.ReservoirInflowM3PerHour * dt / 3600f);

            // auto start/stop each minute
            if (ctx.Time.IsMinuteStart)
            {
                foreach (var g in s.Guns)
                {
                    bool can = CanRun(ctx, g, out string reason);
                    if (g.AutoRun && s.SystemAuto) g.Running = can;
                    else if (g.Running && !can) g.Running = false;
                    g.Status = g.Running ? "making snow" : (g.HydrantId < 0 ? "not connected" : reason);
                    if (!g.Running) g.OutputM3PerHour = 0f;
                }
            }

            // flow allocation
            float pumpLeft = s.PumpCapacityLps;
            float airLeft = s.CompressorCapacityM3Min;
            float snowPerWater = t.F("snowmaking.snowPerWaterM3");
            var w = ctx.World.Weather.Current;
            Vec2 windFrom = Vec2.FromAngle((90f - w.WindDirDeg) * MathUtil.Deg2Rad);
            Vec2 windTo = -windFrom;
            foreach (var g in s.Guns)
            {
                if (!g.Running) continue;
                var def = ctx.Data.Vehicle(g.DefId);
                if (def == null) continue;
                float potential = PotentialOutputM3PerHour(ctx, g);
                // blowback: aiming into the wind loses output
                Vec2 aim = Vec2.FromAngle((90f - g.HeadingDeg) * MathUtil.Deg2Rad);
                float into = Vec2.Dot(aim, windFrom);
                if (into > 0.5f && w.WindKmh > 8f) potential *= t.F("snowmaking.blowbackFactor");
                float waterM3h = potential / snowPerWater;
                float lps = waterM3h * 1000f / 3600f;
                float scale = 1f;
                if (lps > pumpLeft) { scale = pumpLeft / MathF.Max(0.001f, lps); }
                float air = def.SpecOr("airM3PerMin", 0f);
                if (air > 0f && air > airLeft) scale = MathF.Min(scale, airLeft / air);
                if (s.ReservoirM3 <= 1f) scale = 0f;
                float output = potential * scale;
                pumpLeft -= lps * scale;
                airLeft -= air * scale;
                g.OutputM3PerHour = output;
                if (output <= 0f) continue;
                float m3 = output * dt / 3600f;
                float density = def.SpecOr("snowDensityKgM3", t.F("snowmaking.machineSnowDensityKgM3"));
                float kg = m3 * density;
                DepositCone(ctx, g, def, aim, windTo, w.WindKmh, kg, density);
                float water = m3 / snowPerWater;
                s.ReservoirM3 = MathF.Max(0f, s.ReservoirM3 - water);
                g.WaterM3Today += water; s.WaterUsedTodayM3 += water; s.WaterUsedSeasonM3 += water;
                float kwh = (def.SpecOr("gunPowerKw", 0f) + t.F("snowmaking.pumpKwhPerM3Water") * water * 3600f / dt + t.F("snowmaking.compressorKwPerM3Min") * air * scale) * dt / 3600f;
                g.KwhToday += kwh; s.KwhToday += kwh; s.KwhSeason += kwh;
                g.SnowMadeM3Total += m3; s.SnowMadeSeasonM3 += m3;
                g.Hours += dt / 3600f;
                g.NozzleWear = MathUtil.Clamp01(g.NozzleWear + dt / 3600f / t.F("snowmaking.nozzleLifeHours"));
                var v = ctx.World.Vehicles.Get(g.VehicleId);
                if (v != null) { v.MadeSnowM3Today += m3; v.HoursMeter += dt / 3600f; }
                _hourKwh += kwh; _hourWater += water; _hourSnow += m3;
            }

            if (ctx.Time.IsHourStart)
            {
                if (ctx.TryGetSystem<EconomySystem>(out var eco))
                {
                    if (_hourKwh > 0f) eco.Post(ctx, LedgerCategory.Snowmaking, -_hourKwh * ctx.Data.Economy.ElectricityPricePerKwh, "Snowmaking power " + MathF.Round(_hourKwh) + " kWh");
                    if (_hourWater > 0f) eco.Post(ctx, LedgerCategory.Water, -_hourWater * ctx.Data.Economy.WaterPricePerM3, "Snowmaking water " + MathF.Round(_hourWater) + " m3");
                }
                ctx.Events.Publish(new SnowmakingHourEvent { WaterM3 = _hourWater, Kwh = _hourKwh, SnowM3 = _hourSnow, WetBulbC = w.WetBulbC });
                _hourKwh = 0f; _hourWater = 0f; _hourSnow = 0f;
            }
            if (ctx.Time.IsDayStart)
            {
                foreach (var g in s.Guns) { g.WaterM3Today = 0f; g.KwhToday = 0f; }
                s.WaterUsedTodayM3 = 0f; s.KwhToday = 0f;
            }
        }

        private void DepositCone(SimContext ctx, SnowGunState g, VehicleDef def, Vec2 aim, Vec2 windTo, float windKmh, float kg, float density)
        {
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            float reach = def.SpecOr("gunReachM", 30f);
            float half = def.SpecOr("coneHalfAngleDeg", 12f) * MathUtil.Deg2Rad;
            Vec2 drift = windTo * (windKmh * t.F("snowmaking.windDriftMPerKmh"));
            _cells.Clear();
            // deterministic sample pattern: 5 rings x 5 angles weighted toward 0.6 reach
            int rings = 5, angles = 5;
            float totalW = 0f;
            var weights = new float[rings * angles];
            var ids = new int[rings * angles];
            int k = 0;
            for (int r = 0; r < rings; r++)
            {
                float fr = (r + 0.5f) / rings;
                float dist = fr * reach;
                float w = MathF.Exp(-MathF.Pow((fr - 0.6f) / 0.3f, 2f)) * (0.3f + fr);
                for (int a = 0; a < angles; a++)
                {
                    float fa = (a + 0.5f) / angles * 2f - 1f;
                    Vec2 dir = aim.Rotate(fa * half);
                    Vec2 p = g.Pos + dir * dist + drift * fr;
                    int id = grid.EnsureCellAt(p);
                    ids[k] = id; weights[k] = id >= 0 ? w : 0f; totalW += weights[k]; k++;
                }
            }
            if (totalW <= 0f) return;
            float perKg = kg / grid.CellAreaM2;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] < 0 || weights[i] <= 0f) continue;
                SnowOps.DepositPacked(grid, ids[i], perKg * weights[i] / totalW, density);
            }
        }
    }
}
