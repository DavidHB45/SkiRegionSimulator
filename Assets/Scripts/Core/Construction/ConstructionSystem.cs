using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Terrain;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Construction
{
    /// <summary>
    /// Lift and run building as a driven job chain (construction.json stages). Staking evaluates
    /// the line against the type's max length / vertical / span / grade and the terrain's
    /// NoFoundation cells (span = longest gap between placeable tower sites), returning reasons
    /// that name the constraint. Committing creates the lift (UnderConstruction: a visible,
    /// blocking object earning nothing), the project, and the current stage's tasks on the job
    /// board. Stages advance on TaskCompletedEvent; stages with weather windows wait; helicopter
    /// mode replaces crane tower setting with paid flight hours; the final Inspection stage opens
    /// the lift (Closed, ready to open). Run projects clear and grade a corridor, then add the piste
    /// to the network and the snow grid. Ticks after Lifts, before Guests.
    /// </summary>
    public sealed class ConstructionSystem : ISimSystem
    {
        public string Name => "Construction";

        private Action<TaskCompletedEvent> _onTask;

        public void Initialize(SimContext ctx, bool newGame)
        {
            if (ctx.World.Construction == null) ctx.World.Construction = new ConstructionState();
            _onTask = e => OnTaskCompleted(ctx, e);
            ctx.Events.Subscribe(_onTask);
        }

        public IReadOnlyList<ConstructionProject> Projects(SimContext ctx) => ctx.World.Construction.Projects;

        // ------------------------------------------------------------------ staking
        public LiftPlanResult Stake(SimContext ctx, string typeId, Vec2 bottom, Vec2 top, List<string> options = null)
        {
            var plan = new LiftPlanResult { TypeId = typeId, Bottom = bottom, Top = top };
            var type = ctx.Data.LiftType(typeId);
            if (type == null) { plan.Reasons.Add("unknown lift type " + typeId); return plan; }
            var terrain = ctx.Terrain;
            var t = ctx.Tuning;
            plan.LengthM = Vec2.Distance(bottom, top);
            plan.VerticalM = terrain.SampleHeight(top) - terrain.SampleHeight(bottom);
            plan.Towers = LiftSystem.PlaceTowers(ctx, type, bottom, top, out float span, out float grade);
            plan.MaxSpanM = span;
            plan.MaxGradeDeg = grade;
            if (!terrain.InBounds(bottom.X, bottom.Y) || !terrain.InBounds(top.X, top.Y)) plan.Reasons.Add("terminal outside the map");
            if (plan.LengthM < t.F("construction.minLiftLengthM")) plan.Reasons.Add("length " + Fmt(plan.LengthM) + " m is below the minimum " + Fmt(t.F("construction.minLiftLengthM")) + " m");
            if (plan.VerticalM < t.F("construction.minLiftVerticalM")) plan.Reasons.Add("top terminal must be above the bottom (vertical " + Fmt(plan.VerticalM) + " m)");
            if (plan.LengthM > type.MaxLengthM) plan.Reasons.Add("length " + Fmt(plan.LengthM) + " m exceeds max length " + Fmt(type.MaxLengthM) + " m for " + type.DisplayName);
            if (plan.VerticalM > type.MaxVerticalM) plan.Reasons.Add("vertical " + Fmt(plan.VerticalM) + " m exceeds max vertical " + Fmt(type.MaxVerticalM) + " m");
            if (plan.MaxSpanM > type.MaxSpanM) plan.Reasons.Add("span " + Fmt(plan.MaxSpanM) + " m between buildable tower sites exceeds max span " + Fmt(type.MaxSpanM) + " m");
            if (plan.MaxGradeDeg > type.MaxGradeDeg) plan.Reasons.Add("grade " + Fmt(plan.MaxGradeDeg) + " deg exceeds max grade " + Fmt(type.MaxGradeDeg) + " deg");
            if (terrain.HasFlag(bottom.X, bottom.Y, TerrainFlags.NoFoundation)) plan.Reasons.Add("bottom terminal site cannot take a foundation");
            if (terrain.HasFlag(top.X, top.Y, TerrainFlags.NoFoundation)) plan.Reasons.Add("top terminal site cannot take a foundation");
            // gorge crossing check (informational + helicopter)
            Vec2 dir = (top - bottom).Normalized;
            for (float s = 0f; s <= plan.LengthM; s += 10f)
                if (terrain.HasFlag(bottom.X + dir.X * s, bottom.Y + dir.Y * s, TerrainFlags.Gorge)) { plan.CrossesGorge = true; break; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.CanBuyLiftTier(ctx, type.Tier))
                plan.Reasons.Add(type.DisplayName + " is tier " + type.Tier + "; " + eco.ActDef(ctx).DisplayName + " allows up to tier " + eco.ActDef(ctx).MaxLiftTier);
            // intersection with existing lifts
            foreach (var l in ctx.World.Lifts.Lifts)
                if (SegmentsIntersect(bottom, top, l.Bottom, l.Top)) plan.Reasons.Add("line crosses " + l.Name);
            // costing
            float spacing = type.CarrierSpacingM > 0f ? type.CarrierSpacingM : MathF.Max(8f, type.LineSpeedMs * 3600f * type.SeatsOrCabinCapacity / MathF.Max(1f, type.CapacityPph));
            plan.Carriers = type.RopeConfiguration == RopeConfig.Reversible ? 2 : System.Math.Max(2, (int)MathF.Ceiling(2f * plan.LengthM / spacing));
            plan.CapexLine = type.CapexPerKm * plan.LengthM / 1000.0;
            plan.CapexTowers = type.CapexPerTower * plan.Towers.Count;
            plan.CapexTerminals = type.CapexTerminalDrive + type.CapexTerminalReturn;
            plan.CapexCarriers = type.CapexPerCarrier * plan.Carriers;
            plan.CapexBarn = type.CabinBarnCapex;
            plan.CapexTotal = LiftSystem.Capex(ctx, type, plan.LengthM, plan.Towers.Count, plan.Carriers, options);
            plan.ConcreteM3 = type.FoundationConcreteM3PerTower * plan.Towers.Count + t.F("construction.terminalConcreteM3") * 2f;
            plan.RideTimeS = plan.LengthM / MathF.Max(0.3f, type.RideSpeedMs) + type.LoadTimeS;
            plan.CapacityPph = type.CapacityPph;
            plan.NeedsHelicopter = type.HelicopterRequired || plan.CrossesGorge;
            var stages = ctx.Data.Construction.StagesFor(type.Family);
            float hours = 0f;
            foreach (var st in stages)
            {
                int units = UnitsFor(st, plan.Towers.Count, plan.LengthM, plan.Carriers, plan.ConcreteM3);
                hours += st.WorkHoursPerUnit * units;
                plan.CapexTotal += st.CostPerUnit * units;
                if (st.HelicopterOption && plan.NeedsHelicopter) plan.HelicopterHours += st.HelicopterHoursPerUnit * units;
            }
            plan.CapexTotal += plan.ConcreteM3 * ctx.Data.Construction.ConcretePricePerM3 + ctx.Data.Construction.InspectionFee;
            if (plan.NeedsHelicopter) plan.CapexTotal += plan.HelicopterHours * ctx.Data.Construction.HelicopterRatePerHour + ctx.Data.Construction.HelicopterMobilization;
            plan.EstimatedBuildDays = hours / t.F("construction.workHoursPerDay") + stages.Count * 0.5f;
            plan.Ok = plan.Reasons.Count == 0;
            return plan;
        }

        private static string Fmt(float v) => MathF.Round(v).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

        private static bool SegmentsIntersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            float d1 = Vec2.Cross(b - a, c - a), d2 = Vec2.Cross(b - a, d - a), d3 = Vec2.Cross(d - c, a - c), d4 = Vec2.Cross(d - c, b - c);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        private static int UnitsFor(ConstructionStageDef st, int towers, float lengthM, int carriers, float concreteM3)
        {
            switch ((st.PerUnit ?? "lift").ToLowerInvariant())
            {
                case "tower": return System.Math.Max(1, towers);
                case "terminal": return 2;
                case "km": return System.Math.Max(1, (int)MathF.Ceiling(lengthM / 1000f));
                case "carrier": return System.Math.Max(1, carriers);
                case "m3": return System.Math.Max(1, (int)MathF.Ceiling(concreteM3));
                default: return 1;
            }
        }

        // ------------------------------------------------------------------ commit
        public ConstructionProject Commit(SimContext ctx, LiftPlanResult plan, string name, List<string> options, bool helicopterMode, out string reason)
        {
            reason = "";
            if (plan == null || !plan.Ok) { reason = plan == null ? "no plan" : string.Join("; ", plan.Reasons.ToArray()); return null; }
            var type = ctx.Data.RequireLiftType(plan.TypeId);
            var t = ctx.Tuning;
            double deposit = plan.CapexTotal * t.F("construction.depositFrac");
            ctx.TryGetSystem<EconomySystem>(out var eco);
            if (eco != null && !eco.CanAfford(ctx, deposit)) { reason = "deposit of " + EconomySystem.Money(deposit) + " is not affordable"; return null; }
            var lifts = ctx.System<LiftSystem>();
            var lift = lifts.Build(ctx, plan.TypeId, plan.Bottom, plan.Top, name, options, null, false);
            lift.Status = LiftStatus.UnderConstruction;
            lift.BookValue = 0;
            var state = ctx.World.Construction;
            var project = new ConstructionProject
            {
                Id = state.NextId++, Kind = ProjectKind.Lift, Name = string.IsNullOrEmpty(name) ? type.DisplayName : name, LiftId = lift.Id,
                Budget = plan.CapexTotal, StartDay = ctx.Time.Day, HelicopterMode = helicopterMode || type.HelicopterRequired,
                TowersTotal = lift.Towers.Count, ConcreteRequiredM3 = plan.ConcreteM3, Site = plan.Bottom, Status = ProjectStatus.InProgress,
            };
            project.Corridor.Add(plan.Bottom); project.Corridor.Add(plan.Top);
            project.CorridorWidthM = t.F("construction.liftCorridorWidthM");
            foreach (var st in ctx.Data.Construction.StagesFor(type.Family))
            {
                int units = UnitsFor(st, lift.Towers.Count, lift.LengthM, lift.Carriers, plan.ConcreteM3);
                project.Stages.Add(new StageProgress { Kind = st.Kind, DisplayName = st.DisplayName, UnitsRequired = units, WorkRequired = st.WorkHoursPerUnit * units });
            }
            project.LaborCrew = 2;
            state.Projects.Add(project);
            eco?.Post(ctx, LedgerCategory.Construction, -deposit, project.Name + ": equipment deposit");
            project.Spent += deposit;
            StartStage(ctx, project);
            ctx.Sim.Log("Construction started: " + project.Name + " (" + type.DisplayName + ", " + Fmt(lift.LengthM) + " m, " + lift.Towers.Count + " towers). Estimated " + EconomySystem.Money(plan.CapexTotal) + ".");
            return project;
        }

        private void StartStage(SimContext ctx, ConstructionProject p)
        {
            var stage = p.CurrentStage;
            if (stage == null) { CompleteProject(ctx, p); return; }
            stage.StartedTick = ctx.Time.Tick;
            stage.TaskIds.Clear();
            ctx.Events.Publish(new ProjectStagedEvent { ProjectId = p.Id, StageIndex = p.StageIndex, StageName = stage.DisplayName });
            var ts = ctx.System<TaskSystem>();
            var lift = p.LiftId >= 0 ? ctx.World.Lifts.Get(p.LiftId) : null;
            var def = FindStageDef(ctx, p, stage);
            bool heli = p.HelicopterMode && def != null && def.HelicopterOption;
            if (heli)
            {
                // flight days: no machine needed, progress by time when the weather allows
                stage.WorkRequired = def.HelicopterHoursPerUnit * stage.UnitsRequired;
                p.StatusReason = "helicopter: " + stage.DisplayName;
                return;
            }
            string perUnit = def != null ? (def.PerUnit ?? "lift").ToLowerInvariant() : "lift";
            TaskKind kind = KindFor(stage.Kind);
            if (p.Kind == ProjectKind.Run)
            {
                var task = ts.Create(ctx, kind, p.Name + ": " + stage.DisplayName, p.Site, p.PisteId, -1, stage.WorkRequired, 2f, p.Id);
                task.Notes = "run construction";
                stage.TaskIds.Add(task.Id);
                return;
            }
            if (lift == null) return;
            if (perUnit == "tower")
            {
                for (int i = 0; i < lift.Towers.Count; i++)
                {
                    var task = ts.Create(ctx, kind, p.Name + ": " + stage.DisplayName + " " + (i + 1) + "/" + lift.Towers.Count, lift.Towers[i], p.LiftId.ToString(), i, stage.WorkRequired / lift.Towers.Count, 2f, p.Id);
                    ApplyStageDef(task, def);
                    stage.TaskIds.Add(task.Id);
                }
            }
            else if (perUnit == "terminal")
            {
                var b = ts.Create(ctx, kind, p.Name + ": " + stage.DisplayName + " (bottom)", lift.Bottom, p.LiftId.ToString(), 0, stage.WorkRequired * 0.5f, 2f, p.Id);
                var tp = ts.Create(ctx, kind, p.Name + ": " + stage.DisplayName + " (top)", lift.Top, p.LiftId.ToString(), 1, stage.WorkRequired * 0.5f, 2f, p.Id);
                ApplyStageDef(b, def); ApplyStageDef(tp, def);
                stage.TaskIds.Add(b.Id); stage.TaskIds.Add(tp.Id);
            }
            else if (perUnit == "m3" || stage.Kind == ConstructionStageKind.PourConcrete)
            {
                float kg = p.ConcreteRequiredM3 * ctx.Tuning.F("construction.concreteKgPerM3");
                var task = ts.Create(ctx, TaskKind.PourConcrete, p.Name + ": haul and pour " + Fmt(p.ConcreteRequiredM3) + " m3 concrete", lift.Towers.Count > 0 ? lift.Towers[lift.Towers.Count / 2] : lift.Bottom, p.LiftId.ToString(), -1, stage.WorkRequired, 2f, p.Id);
                task.LoadAt = ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.WorkshopLandmarkId).Pos;
                task.LoadAtId = "batchPlant";
                task.QuantityRequired = kg;
                task.CargoKind = "concrete";
                ApplyStageDef(task, def);
                stage.TaskIds.Add(task.Id);
            }
            else
            {
                Vec2 site = stage.Kind == ConstructionStageKind.Commission || stage.Kind == ConstructionStageKind.LoadTest || stage.Kind == ConstructionStageKind.Inspection || stage.Kind == ConstructionStageKind.BuildCabinBarn ? lift.Bottom : Vec2.Lerp(lift.Bottom, lift.Top, 0.5f);
                var task = ts.Create(ctx, kind, p.Name + ": " + stage.DisplayName, site, p.LiftId.ToString(), -1, stage.WorkRequired, 2f, p.Id);
                ApplyStageDef(task, def);
                if (stage.Kind == ConstructionStageKind.StringHaulRope || stage.Kind == ConstructionStageKind.PullTrackRopes) task.SiteRadiusM = MathF.Max(task.SiteRadiusM, lift.LengthM * 0.55f);
                stage.TaskIds.Add(task.Id);
            }
        }

        private static void ApplyStageDef(WorkTask task, ConstructionStageDef def)
        {
            if (def == null) return;
            if (def.RequiredRoles.Count > 0) { task.RequiredRoles.Clear(); task.RequiredRoles.AddRange(def.RequiredRoles); }
            task.RequiredLicense = def.RequiredLicense;
        }

        private static TaskKind KindFor(ConstructionStageKind k)
        {
            switch (k)
            {
                case ConstructionStageKind.Survey: return TaskKind.Survey;
                case ConstructionStageKind.ClearCorridor: case ConstructionStageKind.ClearVegetation: return TaskKind.Clear;
                case ConstructionStageKind.ExcavateFootings: return TaskKind.Excavate;
                case ConstructionStageKind.PourConcrete: return TaskKind.PourConcrete;
                case ConstructionStageKind.SetTowers: return TaskKind.SetTower;
                case ConstructionStageKind.PlaceTerminals: return TaskKind.PlaceTerminal;
                case ConstructionStageKind.StringHaulRope: return TaskKind.StringRope;
                case ConstructionStageKind.PullTrackRopes: return TaskKind.PullTrackRopes;
                case ConstructionStageKind.HangCarriers: return TaskKind.HangCarriers;
                case ConstructionStageKind.BuildCabinBarn: return TaskKind.BuildBarn;
                case ConstructionStageKind.Commission: return TaskKind.Commission;
                case ConstructionStageKind.LoadTest: return TaskKind.LoadTest;
                case ConstructionStageKind.Inspection: return TaskKind.Inspect;
                case ConstructionStageKind.GradeRun: return TaskKind.GradeRun;
                case ConstructionStageKind.InstallSnowmaking: return TaskKind.Custom;
                default: return TaskKind.Custom;
            }
        }

        private static ConstructionStageDef FindStageDef(SimContext ctx, ConstructionProject p, StageProgress stage)
        {
            List<ConstructionStageDef> defs;
            if (p.Kind == ProjectKind.Run) defs = ctx.Data.Construction.RunStages;
            else
            {
                var lift = ctx.World.Lifts.Get(p.LiftId);
                var type = lift != null ? ctx.Data.LiftType(lift.TypeId) : null;
                defs = type != null ? ctx.Data.Construction.StagesFor(type.Family) : new List<ConstructionStageDef>();
            }
            foreach (var d in defs) if (d.Kind == stage.Kind && d.DisplayName == stage.DisplayName) return d;
            foreach (var d in defs) if (d.Kind == stage.Kind) return d;
            return null;
        }

        private void OnTaskCompleted(SimContext ctx, TaskCompletedEvent e)
        {
            if (e.ProjectId < 0) return;
            var p = ctx.World.Construction.Get(e.ProjectId);
            if (p == null || p.Status == ProjectStatus.Complete || p.Status == ProjectStatus.Cancelled) return;
            var stage = p.CurrentStage;
            if (stage == null || !stage.TaskIds.Contains(e.TaskId)) return;
            stage.UnitsDone++;
            if (e.Kind == TaskKind.SetTower) p.TowersSet++;
            var ts = ctx.System<TaskSystem>();
            bool allDone = true;
            foreach (var id in stage.TaskIds) { var t = ts.Get(ctx, id); if (t != null && t.Status != TaskStatus.Done) { allDone = false; break; } }
            if (allDone) CompleteStage(ctx, p);
        }

        private void CompleteStage(SimContext ctx, ConstructionProject p)
        {
            var stage = p.CurrentStage;
            if (stage == null) return;
            stage.Complete = true;
            stage.CompletedTick = ctx.Time.Tick;
            var def = FindStageDef(ctx, p, stage);
            ctx.TryGetSystem<EconomySystem>(out var eco);
            double cost = def != null ? def.CostPerUnit * stage.UnitsRequired + stage.WorkRequired * ctx.Data.Construction.LaborRatePerHour * System.Math.Max(1, def.LaborCrew) : 0;
            if (stage.Kind == ConstructionStageKind.PourConcrete) cost += p.ConcreteRequiredM3 * ctx.Data.Construction.ConcretePricePerM3;
            if (stage.Kind == ConstructionStageKind.Inspection) cost += ctx.Data.Construction.InspectionFee;
            if (p.HelicopterMode && def != null && def.HelicopterOption) cost += stage.WorkRequired * ctx.Data.Construction.HelicopterRatePerHour + (p.HelicopterHoursUsed <= 0f ? ctx.Data.Construction.HelicopterMobilization : 0);
            if (p.HelicopterMode && def != null && def.HelicopterOption) p.HelicopterHoursUsed += stage.WorkRequired;
            // equipment capex instalments: 40% at commit (deposit), 40% when terminals are placed, 20% at commissioning
            var lift = p.LiftId >= 0 ? ctx.World.Lifts.Get(p.LiftId) : null;
            var t = ctx.Tuning;
            if (lift != null && stage.Kind == ConstructionStageKind.PlaceTerminals) cost += p.Budget * t.F("construction.deliveryFrac");
            if (lift != null && stage.Kind == ConstructionStageKind.Commission) cost += p.Budget * (1f - t.F("construction.depositFrac") - t.F("construction.deliveryFrac"));
            if (cost > 0) { eco?.Post(ctx, LedgerCategory.Construction, -cost, p.Name + ": " + stage.DisplayName); p.Spent += cost; }
            stage.CostPaid = cost;
            ctx.Sim.Log(p.Name + ": " + stage.DisplayName + " complete.");
            p.StageIndex++;
            if (p.StageIndex >= p.Stages.Count) CompleteProject(ctx, p);
            else StartStage(ctx, p);
        }

        private void CompleteProject(SimContext ctx, ConstructionProject p)
        {
            p.Status = ProjectStatus.Complete;
            p.CompletedDay = ctx.Time.Day;
            if (p.Kind == ProjectKind.Lift)
            {
                var lift = ctx.World.Lifts.Get(p.LiftId);
                if (lift != null)
                {
                    lift.Status = LiftStatus.Closed;
                    lift.BookValue = lift.CapexTotal;
                    lift.BuildDay = ctx.Time.Day;
                    ctx.Events.Publish(new LiftStatusChangedEvent { LiftId = lift.Id, Status = LiftStatus.Closed, Reason = "inspection signed off" });
                    ctx.Sim.Log(lift.Name + " passed inspection and is ready to open.", LogLevel.Alert);
                }
            }
            else if (p.Kind == ProjectKind.Run)
            {
                var piste = ctx.World.Pistes.Piste(p.PisteId);
                if (piste != null) { piste.Open = true; ctx.Sim.Log(piste.Name + " is open.", LogLevel.Alert); }
            }
            ctx.Events.Publish(new ProjectCompletedEvent { ProjectId = p.Id, LiftId = p.LiftId, PisteId = p.PisteId });
        }

        // ------------------------------------------------------------------ runs
        public bool StakeRun(SimContext ctx, string id, string name, PisteDifficulty difficulty, float widthM, List<Vec2> points, string topNodeId, string bottomNodeId, out double cost, out List<string> reasons)
        {
            reasons = new List<string>();
            cost = 0;
            var t = ctx.Tuning;
            var terrain = ctx.Terrain;
            if (points == null || points.Count < 2) { reasons.Add("a run needs at least two points"); return false; }
            if (widthM < t.F("construction.minRunWidthM") || widthM > t.F("construction.maxRunWidthM")) reasons.Add("width must be " + t.F("construction.minRunWidthM") + "-" + t.F("construction.maxRunWidthM") + " m");
            if (ctx.World.Pistes.Piste(id) != null) reasons.Add("a run with id " + id + " exists");
            float length = 0f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                float segLen = Vec2.Distance(points[i], points[i + 1]);
                length += segLen;
                float dh = terrain.SampleHeight(points[i]) - terrain.SampleHeight(points[i + 1]);
                float grade = segLen > 0.1f ? MathF.Atan(dh / segLen) * MathUtil.Rad2Deg : 0f;
                if (grade < -3f) reasons.Add("segment " + (i + 1) + " climbs " + Fmt(-dh) + " m; runs must descend");
                if (grade > t.F("construction.maxRunGradeDeg")) reasons.Add("segment " + (i + 1) + " grade " + Fmt(grade) + " deg exceeds max run grade " + Fmt(t.F("construction.maxRunGradeDeg")) + " deg");
                for (float s = 0f; s <= segLen; s += 10f)
                {
                    Vec2 p = Vec2.Lerp(points[i], points[i + 1], s / MathF.Max(0.1f, segLen));
                    if (terrain.HasFlag(p.X, p.Y, TerrainFlags.Gorge)) { reasons.Add("segment " + (i + 1) + " crosses the valley"); break; }
                }
            }
            if (length < t.F("construction.minRunLengthM")) reasons.Add("run is only " + Fmt(length) + " m long");
            cost = length * ctx.Data.Construction.RunCostPerM + length * widthM / 10000f * ctx.Data.Construction.VegetationClearHoursPerHectare * ctx.Data.Construction.LaborRatePerHour * 3;
            return reasons.Count == 0;
        }

        public ConstructionProject CommitRun(SimContext ctx, string id, string name, PisteDifficulty difficulty, float widthM, List<Vec2> points, string topNodeId, string bottomNodeId, out string reason)
        {
            reason = "";
            if (!StakeRun(ctx, id, name, difficulty, widthM, points, topNodeId, bottomNodeId, out double cost, out var reasons)) { reason = string.Join("; ", reasons.ToArray()); return null; }
            ctx.TryGetSystem<EconomySystem>(out var eco);
            if (eco != null && !eco.TrySpend(ctx, LedgerCategory.Construction, cost * ctx.Tuning.F("construction.depositFrac"), name + ": run construction deposit", out reason)) return null;
            // the piste exists from now on but is closed and unallocated until graded
            var piste = PisteNetworkBuilder.AddPiste(ctx, id, name, difficulty, widthM, points.ToArray(), topNodeId, bottomNodeId, false);
            piste.Open = false;
            var state = ctx.World.Construction;
            var p = new ConstructionProject { Id = state.NextId++, Kind = ProjectKind.Run, Name = name, PisteId = id, Budget = cost, StartDay = ctx.Time.Day, Site = Vec2.Lerp(points[0], points[points.Count - 1], 0.5f), Status = ProjectStatus.InProgress, CorridorWidthM = widthM };
            p.Corridor.AddRange(points);
            float hectares = piste.LengthM * widthM / 10000f;
            foreach (var st in ctx.Data.Construction.RunStages)
                p.Stages.Add(new StageProgress { Kind = st.Kind, DisplayName = st.DisplayName, UnitsRequired = 1, WorkRequired = st.WorkHoursPerUnit * MathF.Max(1f, hectares) });
            if (p.Stages.Count == 0) p.Stages.Add(new StageProgress { Kind = ConstructionStageKind.GradeRun, DisplayName = "Grade run", UnitsRequired = 1, WorkRequired = 8f * MathF.Max(1f, hectares) });
            state.Projects.Add(p);
            p.Spent += cost * ctx.Tuning.F("construction.depositFrac");
            StartStage(ctx, p);
            ctx.Sim.Log("Run construction started: " + name + " (" + Fmt(piste.LengthM) + " m, " + Fmt(widthM) + " m wide).");
            return p;
        }

        public bool Cancel(SimContext ctx, int projectId, out string reason)
        {
            reason = "";
            var p = ctx.World.Construction.Get(projectId);
            if (p == null || p.Status == ProjectStatus.Complete || p.Status == ProjectStatus.Cancelled) { reason = "project cannot be cancelled"; return false; }
            var ts = ctx.System<TaskSystem>();
            foreach (var st in p.Stages) foreach (var id in st.TaskIds) ts.Cancel(ctx, id);
            p.Status = ProjectStatus.Cancelled;
            if (p.Kind == ProjectKind.Lift && p.LiftId >= 0) ctx.System<LiftSystem>().Remove(ctx, p.LiftId);
            if (p.Kind == ProjectKind.Run)
            {
                var net = ctx.World.Pistes;
                var piste = net.Piste(p.PisteId);
                if (piste != null) { net.Pistes.Remove(piste); net.Segments.RemoveAll(s => s.PisteId == piste.Id); }
            }
            ctx.Sim.Log(p.Name + " cancelled; " + EconomySystem.Money(p.Spent) + " already spent is lost.", LogLevel.Warning);
            ctx.Events.Publish(new ProjectStatusEvent { ProjectId = p.Id, Status = ProjectStatus.Cancelled, Reason = "cancelled" });
            return true;
        }

        public void SetHelicopterMode(SimContext ctx, int projectId, bool on)
        {
            var p = ctx.World.Construction.Get(projectId);
            if (p != null) p.HelicopterMode = on;
        }

        public List<Vec2> PlaceTowers(SimContext ctx, LiftTypeDef type, Vec2 bottom, Vec2 top, out float maxSpanM) => LiftSystem.PlaceTowers(ctx, type, bottom, top, out maxSpanM, out _);

        // ------------------------------------------------------------------ tick
        public void Tick(SimContext ctx, float dt)
        {
            if (!ctx.Time.IsMinuteStart) return;
            var projects = ctx.World.Construction.Projects;
            var w = ctx.World.Weather.Current;
            var ts = ctx.System<TaskSystem>();
            for (int i = 0; i < projects.Count; i++)
            {
                var p = projects[i];
                if (p.Status != ProjectStatus.InProgress && p.Status != ProjectStatus.WaitingWeather) continue;
                var stage = p.CurrentStage;
                if (stage == null) continue;
                var def = FindStageDef(ctx, p, stage);
                bool daylight = ctx.Time.HourOfDay >= 7 && ctx.Time.HourOfDay < 17;
                bool ok = def == null || (w.WindKmh <= def.MaxWindKmh && w.TempC >= def.MinTempC && (!def.NeedsDaylight || daylight));
                bool heli = p.HelicopterMode && def != null && def.HelicopterOption;
                if (!ok)
                {
                    if (p.Status != ProjectStatus.WaitingWeather)
                    {
                        p.Status = ProjectStatus.WaitingWeather;
                        p.StatusReason = w.WindKmh > (def?.MaxWindKmh ?? 999f) ? "wind " + Fmt(w.WindKmh) + " km/h" : (w.TempC < (def?.MinTempC ?? -99f) ? "too cold" : "waiting for daylight");
                        foreach (var id in stage.TaskIds) ts.Block(ctx, id, "weather: " + p.StatusReason);
                        ctx.Events.Publish(new ProjectStatusEvent { ProjectId = p.Id, Status = p.Status, Reason = p.StatusReason });
                    }
                    continue;
                }
                if (p.Status == ProjectStatus.WaitingWeather)
                {
                    p.Status = ProjectStatus.InProgress; p.StatusReason = "";
                    foreach (var id in stage.TaskIds) ts.Unblock(ctx, id);
                    ctx.Events.Publish(new ProjectStatusEvent { ProjectId = p.Id, Status = p.Status, Reason = "weather window" });
                }
                if (heli)
                {
                    stage.WorkDone += 1f / 60f; // one minute of flying
                    if (stage.WorkDone >= stage.WorkRequired) { stage.UnitsDone = stage.UnitsRequired; CompleteStage(ctx, p); }
                }
                else
                {
                    float done = 0f;
                    foreach (var id in stage.TaskIds) { var t = ts.Get(ctx, id); if (t != null) done += t.WorkDone; }
                    stage.WorkDone = done;
                }
            }
        }
    }
}
