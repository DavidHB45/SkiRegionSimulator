using System;
using System.Collections.Generic;
using AlpineSim.Core.Construction;
using AlpineSim.Core.Lifts;
using NUnit.Framework;

namespace AlpineSim.Tests.Data
{
    /// <summary>Schema and balance invariants of lifts.json and construction.json.</summary>
    public class LiftDataTests
    {
        private static List<LiftTypeDef> Lifts => TestEnv.Data.LiftTypes;

        private static LiftTypeDef Lift(string id)
        {
            var d = TestEnv.Data.LiftType(id);
            Assert.IsNotNull(d, "lifts.json must define " + id);
            return d;
        }

        [Test]
        public void LadderHasTwentySixTypesInSixFamiliesWithUniqueIds()
        {
            Assert.AreEqual(26, Lifts.Count, "lifts.json defines exactly 26 lift types");
            var ids = new HashSet<string>();
            var families = new HashSet<LiftFamily>();
            var tiers = new HashSet<int>();
            foreach (var l in Lifts)
            {
                Assert.IsFalse(string.IsNullOrEmpty(l.Id), "every lift has an id");
                Assert.IsTrue(ids.Add(l.Id), "duplicate lift id " + l.Id);
                Assert.IsFalse(string.IsNullOrEmpty(l.DisplayName), l.Id + " has a display name");
                Assert.IsFalse(string.IsNullOrEmpty(l.Comment), l.Id + " carries a comment with its real-world basis");
                families.Add(l.Family);
                tiers.Add(l.Tier);
            }
            foreach (LiftFamily f in Enum.GetValues(typeof(LiftFamily))) Assert.IsTrue(families.Contains(f), "family " + f + " has at least one type");
            for (int t = 1; t <= 5; t++) Assert.IsTrue(tiers.Contains(t), "tier " + t + " is present");
            Assert.IsFalse(tiers.Contains(0), "no tier-0 lift");
            Assert.IsFalse(tiers.Contains(6), "no tier above 5");
        }

        [Test]
        public void EveryTypeHasRealisticCapacityAndPositiveCapex()
        {
            foreach (var l in Lifts)
            {
                Assert.GreaterOrEqual(l.CapacityPph, 300f, l.Id + " capacity");
                Assert.LessOrEqual(l.CapacityPph, 6000f, l.Id + " capacity");
                Assert.Greater(l.CapexPerKm, 0.0, l.Id + " capex per km");
                Assert.Greater(l.CapexTerminalDrive, 0.0, l.Id + " drive terminal capex");
                Assert.Greater(l.OpexPerOperatingHour, 0.0, l.Id + " opex");
                Assert.Greater(l.LineSpeedMs, 0f, l.Id + " line speed");
                Assert.Greater(l.RideSpeedMs, 0f, l.Id + " ride speed");
                Assert.Greater(l.MaxLengthM, 0f, l.Id + " max length");
                Assert.Greater(l.MaxSpanM, 0f, l.Id + " max span");
                Assert.Greater(l.MtbfHours, 0f, l.Id + " MTBF");
                Assert.Greater(l.InspectionIntervalDays, 0, l.Id + " inspection interval");
                Assert.Greater(l.StaffRequired.Total, 0, l.Id + " needs staff");
                Assert.GreaterOrEqual(l.WeatherExposure, 0f, l.Id + " exposure");
                Assert.LessOrEqual(l.WeatherExposure, 1f, l.Id + " exposure");
                Assert.GreaterOrEqual(l.ComfortScore, 0f, l.Id + " comfort");
                Assert.LessOrEqual(l.ComfortScore, 1f, l.Id + " comfort");
                Assert.Greater(l.SeatsOrCabinCapacity, 0, l.Id + " seats");
            }
        }

        [Test]
        public void OnlyTramsAndTricableCanSpanTheGorge()
        {
            var gorgeCapable = new HashSet<string> { "tricable_3s", "aerial_tram_80", "aerial_tram_150" };
            foreach (var l in Lifts)
            {
                if (gorgeCapable.Contains(l.Id)) Assert.GreaterOrEqual(l.MaxSpanM, 1000f, l.Id + " must span the 1,000 m gorge");
                else Assert.Less(l.MaxSpanM, 1000f, l.Id + " must not be able to span the gorge");
                if (l.Family == LiftFamily.Surface)
                {
                    Assert.LessOrEqual(l.MaxSpanM, 120f, l.Id + " surface lift span");
                    Assert.LessOrEqual(l.MaxLengthM, 1800f, l.Id + " surface lift length");
                }
            }
            foreach (var id in gorgeCapable) Assert.IsNotNull(TestEnv.Data.LiftType(id), id + " exists");
            Assert.LessOrEqual(Lift("funitel").MaxSpanM, 700f);
            Assert.LessOrEqual(Lift("gondola_15").MaxSpanM, 700f);
            foreach (var l in Lifts) if (l.Family == LiftFamily.Chair && l.Grip == GripType.Fixed) Assert.LessOrEqual(l.MaxSpanM, 400f, l.Id + " fixed-grip chair span");
        }

        [Test]
        public void GradeLimitsFollowTheFamily()
        {
            foreach (var l in Lifts)
            {
                switch (l.Family)
                {
                    case LiftFamily.Surface: Assert.LessOrEqual(l.MaxGradeDeg, 25f, l.Id); break;
                    case LiftFamily.Chair: Assert.LessOrEqual(l.MaxGradeDeg, 40f, l.Id); break;
                    case LiftFamily.Gondola: Assert.LessOrEqual(l.MaxGradeDeg, 45f, l.Id); break;
                    case LiftFamily.Aerial: Assert.LessOrEqual(l.MaxGradeDeg, 60f, l.Id); break;
                }
            }
            Assert.AreEqual(50f, Lift("funicular").MaxGradeDeg, 0.01f);
            Assert.AreEqual(25f, Lift("cog_rail").MaxGradeDeg, 0.01f);
        }

        [Test]
        public void TowerSpacingFitsInsideTheMaxSpan()
        {
            // LiftSystem.PlaceTowers spaces towers at max(MinTowerSpacingM, 1000 / TowersPerKm); if that
            // exceeded MaxSpanM the type could never be staked on flat ground.
            foreach (var l in Lifts)
            {
                float spacing = Math.Max(l.MinTowerSpacingM, 1000f / Math.Max(0.5f, l.TowersPerKm));
                Assert.LessOrEqual(spacing, l.MaxSpanM + 0.01f, l.Id + " nominal tower spacing " + spacing + " m exceeds its max span " + l.MaxSpanM + " m");
            }
        }

        [Test]
        public void CarrierSpacingMatchesCapacityOnCirculatingLifts()
        {
            int checkedCount = 0;
            foreach (var l in Lifts)
            {
                bool circulating = l.RopeConfiguration != RopeConfig.Reversible && l.Family != LiftFamily.Rail;
                if (!circulating) continue;
                Assert.Greater(l.CarrierSpacingM, 0f, l.Id + " circulating lift needs a carrier spacing");
                float derived = l.SeatsOrCabinCapacity * 3600f * l.LineSpeedMs / l.CarrierSpacingM;
                Assert.AreEqual(l.CapacityPph, derived, l.CapacityPph * 0.10f, l.Id + ": seats x 3600 x speed / spacing = " + derived + " pph vs rated " + l.CapacityPph);
                checkedCount++;
            }
            Assert.GreaterOrEqual(checkedCount, 20, "most of the ladder is circulating");
        }

        [Test]
        public void OptionsExistAndApplyToTheFamily()
        {
            var data = TestEnv.Data;
            Assert.GreaterOrEqual(data.LiftOptions.Count, 8, "eight options are defined");
            var ids = new HashSet<string>();
            foreach (var o in data.LiftOptions)
            {
                Assert.IsTrue(ids.Add(o.Id), "duplicate option " + o.Id);
                Assert.Greater(o.Families.Count, 0, "option " + o.Id + " applies to at least one family");
                Assert.Greater(o.LoadTimeFactor, 0f, "option " + o.Id + " load time factor");
            }
            foreach (var id in new[] { "bubble", "heated_seats", "loading_carpet", "night_lighting", "wind_fairings", "cabin_barn_heating", "wifi", "safety_bar_auto" })
                Assert.IsNotNull(data.LiftOption(id), "option " + id + " exists");
            foreach (var l in Lifts)
            {
                foreach (var oid in l.Options)
                {
                    var o = data.LiftOption(oid);
                    Assert.IsNotNull(o, l.Id + " references unknown option " + oid);
                    Assert.IsTrue(o.Families.Contains(l.Family), l.Id + " lists option " + oid + " which does not apply to " + l.Family);
                }
            }
            var carpet = data.LiftOption("loading_carpet");
            Assert.Greater(carpet.CapacityPct, 0f);
            Assert.Less(carpet.LoadTimeFactor, 1f);
            Assert.IsTrue(carpet.BeginnerBonus);
            Assert.IsTrue(data.LiftOption("night_lighting").NightOperation);
            Assert.AreEqual(10f, data.LiftOption("wind_fairings").WindHoldBonusKmh, 0.01f);
        }

        [Test]
        public void DetachableQuadCostsMoreToBuildAndRunThanFixedQuad()
        {
            var fixedQuad = Lift("fixed_quad");
            var detachable = Lift("detachable_quad");
            Assert.Greater(detachable.CapexPerKm, fixedQuad.CapexPerKm, "the Act IV trap needs the detachable line to cost more per km");
            Assert.GreaterOrEqual(detachable.CapexPerKm / fixedQuad.CapexPerKm, 2.0, "about 2.2x per km");
            Assert.Greater(detachable.OpexPerOperatingHour, fixedQuad.OpexPerOperatingHour, "and more per operating hour");
            Assert.Greater(detachable.CapexTerminalDrive, fixedQuad.CapexTerminalDrive);
            Assert.Greater(detachable.CapacityPph, fixedQuad.CapacityPph);
            Assert.AreEqual(GripType.Detachable, detachable.Grip);
            Assert.AreEqual(GripType.Fixed, fixedQuad.Grip);
            Assert.Greater(detachable.LineSpeedMs, fixedQuad.LineSpeedMs * 2f);
        }

        [Test]
        public void SpecificTypesMatchTheBrief()
        {
            var tbar = Lift("t_bar");
            Assert.AreEqual(LiftFamily.Surface, tbar.Family);
            Assert.AreEqual(1, tbar.Tier);
            Assert.AreEqual(RopeConfig.Surface, tbar.RopeConfiguration);
            Assert.AreEqual(1200f, tbar.MaxLengthM, 0.01f);
            var quad = Lift("fixed_quad");
            Assert.AreEqual(2, quad.Tier);
            Assert.AreEqual(2400f, quad.CapacityPph, 0.01f);
            Assert.AreEqual(60f, quad.WindHoldKmh, 0.01f);
            var tram = Lift("aerial_tram_80");
            Assert.AreEqual(RopeConfig.Reversible, tram.RopeConfiguration);
            Assert.IsTrue(tram.HelicopterRequired);
            Assert.AreEqual(CraneClass.Helicopter, tram.CraneClass);
            var s3 = Lift("tricable_3s");
            Assert.AreEqual(RopeConfig.Tri, s3.RopeConfiguration);
            Assert.AreEqual(5, s3.Tier);
            Assert.IsTrue(s3.HelicopterRequired);
            Assert.AreEqual(RopeConfig.Funitel, Lift("funitel").RopeConfiguration);
            Assert.AreEqual(RopeConfig.Bi, Lift("gondola_15").RopeConfiguration);
            Assert.AreEqual(RopeConfig.Rack, Lift("cog_rail").RopeConfiguration);
            Assert.AreEqual(LiftFamily.Rail, Lift("funicular").Family);
            Assert.AreEqual(LiftFamily.Hybrid, Lift("chondola").Family);
            Assert.IsTrue(Lift("magic_carpet").BeginnerFriendly);
            Assert.Less(Lift("fixed_quad_bubble").WeatherExposure, quad.WeatherExposure);
            Assert.Greater(Lift("fixed_quad_bubble").ComfortScore, quad.ComfortScore);
            // wind hold ordering: surface < fixed-grip < detachable < funitel < 3S
            Assert.Less(quad.WindHoldKmh, Lift("detachable_quad").WindHoldKmh);
            Assert.Less(Lift("detachable_quad").WindHoldKmh, Lift("funitel").WindHoldKmh);
            Assert.Less(Lift("funitel").WindHoldKmh, s3.WindHoldKmh);
            foreach (var l in Lifts) if (l.Grip == GripType.Detachable || l.Family == LiftFamily.Gondola) Assert.Greater(l.CabinBarnCapex, 0.0, l.Id + " parks its carriers in a barn");
        }

        [Test]
        public void ConstructionChainsCoverEveryFamily()
        {
            var c = TestEnv.Data.Construction;
            foreach (var key in new[] { "surface", "chair", "gondola", "hybrid", "aerial", "rail" })
            {
                Assert.IsTrue(c.StagesByFamily.ContainsKey(key), "construction.json has a chain for " + key);
                var chain = c.StagesByFamily[key];
                Assert.Greater(chain.Count, 5, key + " chain has several stages");
                Assert.AreEqual(ConstructionStageKind.Survey, chain[0].Kind, key + " starts with Survey");
                Assert.AreEqual(ConstructionStageKind.Inspection, chain[chain.Count - 1].Kind, key + " ends with Inspection");
                bool towers = false, commission = false;
                foreach (var st in chain)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(st.DisplayName), key + " stage " + st.Kind + " has a name");
                    Assert.Greater(st.RequiredRoles.Count, 0, key + " stage " + st.Kind + " needs a role");
                    Assert.Greater(st.WorkHoursPerUnit, 0f, key + " stage " + st.Kind + " has work");
                    Assert.Greater(st.LaborCrew, 0, key + " stage " + st.Kind + " has a crew");
                    if (st.Kind == ConstructionStageKind.SetTowers)
                    {
                        towers = true;
                        Assert.AreEqual("tower", st.PerUnit, key + " SetTowers is per tower");
                        Assert.AreNotEqual(CraneClass.None, st.MinCrane, key + " SetTowers needs a crane");
                        if (key == "aerial")
                        {
                            Assert.IsTrue(st.HelicopterOption, "aerial towers fly in");
                            Assert.Greater(st.HelicopterHoursPerUnit, 0f);
                            Assert.AreEqual(CraneClass.Helicopter, st.MinCrane);
                        }
                    }
                    if (st.Kind == ConstructionStageKind.Commission) commission = true;
                    if (st.Kind == ConstructionStageKind.PourConcrete) Assert.AreEqual("m3", st.PerUnit, key + " concrete is per m3");
                }
                Assert.IsTrue(towers, key + " chain sets towers");
                Assert.IsTrue(commission, key + " chain commissions");
            }
            Assert.IsTrue(c.StagesByFamily["aerial"].Exists(s => s.Kind == ConstructionStageKind.PullTrackRopes), "aerial lines pull track ropes");
            Assert.IsTrue(c.StagesByFamily["gondola"].Exists(s => s.Kind == ConstructionStageKind.BuildCabinBarn), "gondolas build a barn");
            Assert.IsTrue(c.StagesByFamily["hybrid"].Exists(s => s.Kind == ConstructionStageKind.BuildCabinBarn), "chondolas build a barn");
            foreach (LiftFamily f in Enum.GetValues(typeof(LiftFamily))) Assert.AreSame(c.StagesByFamily[f.ToString().ToLowerInvariant()], c.StagesFor(f), "StagesFor resolves " + f);
            Assert.Greater(c.RunStages.Count, 0, "run stages are defined");
            Assert.IsTrue(c.RunStages.Exists(s => s.Kind == ConstructionStageKind.GradeRun), "runs are graded");
            foreach (var st in c.RunStages) Assert.AreEqual("run", st.PerUnit, "run stages are per run");
            Assert.Greater(c.HelicopterRatePerHour, 0.0);
            Assert.Greater(c.ConcretePricePerM3, 0.0);
            Assert.Greater(c.LaborRatePerHour, 0.0);
            Assert.Greater(c.InspectionFee, 0.0);
            Assert.Greater(c.RunCostPerM, 0.0);
        }

        [Test]
        public void NoLoadWarningsAboutLiftsOrConstruction()
        {
            foreach (var w in TestEnv.Data.Warnings)
            {
                string lower = w.ToLowerInvariant();
                Assert.IsFalse(lower.Contains("lift"), "warning: " + w);
                Assert.IsFalse(lower.Contains("construction"), "warning: " + w);
            }
        }
    }
}
