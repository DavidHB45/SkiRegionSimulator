using System;
using System.Collections.Generic;
using AlpineSim.Core.Fleet;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;
using NUnit.Framework;

namespace AlpineSim.Tests.Data
{
    /// <summary>Fleet data integrity: fifty-plus machines that are complete, distinct and consumable by every system that reads them.</summary>
    [TestFixture]
    public sealed class FleetDataTests
    {
        private static List<VehicleDef> Fleet => TestEnv.Data.Vehicles;

        [Test]
        public void FleetSpansTenCategoriesAndFiveTiers()
        {
            Assert.GreaterOrEqual(Fleet.Count, 50, "the brief asks for fifty machine classes");
            var cats = new HashSet<VehicleCategory>();
            var tiers = new HashSet<int>();
            var ids = new HashSet<string>();
            foreach (var v in Fleet)
            {
                cats.Add(v.Category);
                tiers.Add(v.Tier);
                Assert.IsTrue(ids.Add(v.Id), "duplicate id " + v.Id);
            }
            foreach (VehicleCategory c in Enum.GetValues(typeof(VehicleCategory))) Assert.IsTrue(cats.Contains(c), "no machine in category " + c);
            for (int t = 1; t <= 5; t++) Assert.IsTrue(tiers.Contains(t), "no tier " + t + " machine");
        }

        [Test]
        public void EveryMachineIsComplete()
        {
            foreach (var v in Fleet)
            {
                string where = "machine " + v.Id;
                Assert.IsFalse(string.IsNullOrEmpty(v.DisplayName), where + " has no display name");
                Assert.Greater(v.Roles.Count, 0, where + " has no roles");
                Assert.Greater(v.PurchasePrice, 0, where + " has no price");
                Assert.Greater(v.MassKg, 0f, where + " has no mass");
                Assert.GreaterOrEqual(v.ResaleCurve.Length, 3, where + " needs a resale curve");
                for (int i = 1; i < v.ResaleCurve.Length; i++) Assert.LessOrEqual(v.ResaleCurve[i], v.ResaleCurve[i - 1] + 1e-6f, where + " resale curve must not rise");
                bool fuelled = v.FuelType != FuelType.None && v.EnginePowerKw > 0f;
                if (fuelled)
                {
                    if (v.FuelType != FuelType.Electric) Assert.GreaterOrEqual(v.TorqueCurve.Count, 3, where + " needs a torque curve");
                    var b = v.BurnLPerHour;
                    Assert.IsTrue(b.Idle <= b.Transit + 1e-6f && b.Transit <= b.Working + 1e-6f && b.Working <= b.HighLoad + 1e-6f, where + " burn rates must be ordered idle <= transit <= working <= high load");
                    Assert.Greater(b.Idle, 0f, where + " burns nothing at idle");
                    Assert.Greater(v.EnergyCapacity, 0f, where + " has no tank or battery");
                }
                Assert.Greater(v.WearRates.Engine, 0f, where); Assert.Greater(v.WearRates.Hydraulics, 0f, where); Assert.Greater(v.WearRates.Drivetrain, 0f, where);
                Assert.Greater(v.WearRates.TracksOrTires, 0f, where); Assert.Greater(v.WearRates.Attachment, 0f, where);
                Assert.IsFalse(string.IsNullOrEmpty(v.Visual.Silhouette), where + " has no silhouette");
                Assert.IsFalse(string.IsNullOrEmpty(v.Comment), where + " needs a comment with its real-world basis");
                Assert.Greater(v.MtbfHours, 0f, where); Assert.Greater(v.ServiceIntervalHours, 0f, where);
                foreach (var slot in v.AttachmentSlots)
                {
                    if (string.IsNullOrEmpty(slot.DefaultAttachmentId)) continue;
                    var att = TestEnv.Data.Attachment(slot.DefaultAttachmentId);
                    Assert.IsNotNull(att, where + " default attachment " + slot.DefaultAttachmentId + " missing");
                    Assert.IsTrue(att.FitsSlot(slot.Position), where + ": " + att.Id + " does not fit " + slot.Position);
                    Assert.LessOrEqual(att.MassKg, slot.MaxMassKg, where + ": default " + att.Id + " is too heavy for its slot");
                    Assert.LessOrEqual(att.MinHostPowerKw, v.EnginePowerKw, where + ": default " + att.Id + " needs more power than the host has");
                    if (att.HydraulicFlowLpm > 0f) Assert.LessOrEqual(att.HydraulicFlowLpm, v.HydraulicFlowLpm, where + ": default " + att.Id + " needs more hydraulic flow than the host has");
                    if (att.RequiresPto) Assert.IsTrue(v.Pto, where + ": default " + att.Id + " needs a PTO");
                }
            }
        }

        [Test]
        public void NoTwoMachinesInACategoryAreWithinFivePercentOnEveryKeyFigure()
        {
            var offenders = new List<string>();
            for (int i = 0; i < Fleet.Count; i++)
            {
                for (int j = i + 1; j < Fleet.Count; j++)
                {
                    var a = Fleet[i]; var b = Fleet[j];
                    if (a.Category != b.Category) continue;
                    bool distinct = Differs(a.MassKg, b.MassKg) || Differs(a.EnginePowerKw, b.EnginePowerKw) || Differs((float)a.PurchasePrice, (float)b.PurchasePrice) || Differs(a.TopSpeedKmh, b.TopSpeedKmh);
                    if (!distinct) offenders.Add(a.Id + " ~ " + b.Id);
                }
            }
            Assert.IsEmpty(offenders, "machines that play identically: " + string.Join(", ", offenders.ToArray()));
        }

        private static bool Differs(float a, float b)
        {
            float scale = Math.Max(Math.Abs(a), Math.Abs(b));
            if (scale < 1e-6f) return false;
            return Math.Abs(a - b) / scale > 0.05f;
        }

        [Test]
        public void BlowersDeclareCapacityRisingWithTier()
        {
            var blowers = new List<VehicleDef>();
            foreach (var v in Fleet) if (v.Category == VehicleCategory.Blower) blowers.Add(v);
            Assert.GreaterOrEqual(blowers.Count, 4);
            blowers.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            float last = 0f;
            foreach (var v in blowers)
            {
                Assert.IsTrue(v.HasSpec("blowerCapacityTph") && v.HasSpec("throwDistanceM") && v.HasSpec("intakeWidthM"), v.Id + " needs blower specs");
                Assert.IsTrue(v.HasRole(VehicleRole.Blow), v.Id + " must blow");
                Assert.Greater(v.Spec("blowerCapacityTph"), last, v.Id + " capacity should rise with tier");
                last = v.Spec("blowerCapacityTph");
            }
        }

        [Test]
        public void SnowGunsHaveWetBulbOutputCurves()
        {
            int guns = 0;
            foreach (var v in Fleet)
            {
                if (!v.HasRole(VehicleRole.MakeSnow)) continue;
                guns++;
                Assert.IsTrue(v.HasCurve("gunOutputByWetBulb"), v.Id + " needs gunOutputByWetBulb");
                float at0 = v.Curve("gunOutputByWetBulb", 0f), at3 = v.Curve("gunOutputByWetBulb", -3f), at10 = v.Curve("gunOutputByWetBulb", -10f);
                if (v.Id == "snow_factory") { Assert.Greater(at0, 0f); Assert.AreEqual(at0, at10, 1e-3f); continue; }
                Assert.AreEqual(0f, at0, 1e-4f, v.Id + " must make nothing at 0 C wet-bulb");
                Assert.Greater(at3, 0f, v.Id + " should produce at -3 C wet-bulb");
                Assert.GreaterOrEqual(at10, at3, v.Id + " should produce more when colder");
                Assert.IsTrue(v.HasSpec("gunPowerKw") && v.HasSpec("gunReachM") && v.HasSpec("coneHalfAngleDeg"), v.Id + " needs gun specs");
            }
            Assert.GreaterOrEqual(guns, 5);
        }

        [Test]
        public void EveryAttachmentKindExistsAndTillersDifferByWidth()
        {
            var kinds = new HashSet<AttachmentKind>();
            var ids = new HashSet<string>();
            foreach (var a in TestEnv.Data.Attachments)
            {
                kinds.Add(a.Kind);
                Assert.IsTrue(ids.Add(a.Id), "duplicate attachment " + a.Id);
                Assert.Greater(a.CompatibleSlots.Count, 0, a.Id + " fits no slot");
                Assert.Greater(a.PurchasePrice, 0, a.Id);
            }
            foreach (AttachmentKind k in Enum.GetValues(typeof(AttachmentKind))) Assert.IsTrue(kinds.Contains(k), "no attachment of kind " + k);
            Assert.GreaterOrEqual(TestEnv.Data.Attachments.Count, 25);
            var t43 = TestEnv.Data.RequireAttachment("tiller_4_3");
            var t60 = TestEnv.Data.RequireAttachment("tiller_6_0");
            Assert.AreEqual(4.3f, t43.WorkingWidthM, 1e-3f);
            Assert.AreEqual(6.0f, t60.WorkingWidthM, 1e-3f);
            Assert.IsTrue(t43.GrantsRoles.Contains(VehicleRole.Groom) && t60.GrantsRoles.Contains(VehicleRole.Groom));
            Assert.Greater(t60.MinHostPowerKw, t43.MinHostPowerKw);
        }

        [Test]
        public void EveryTaskKindLicenceAndPartsKindHasData()
        {
            foreach (TaskKind k in Enum.GetValues(typeof(TaskKind))) Assert.IsNotNull(TestEnv.Data.Tasks.Kind(k), "tasks.json lacks " + k);
            foreach (OperatorLicense l in Enum.GetValues(typeof(OperatorLicense))) Assert.IsNotNull(TestEnv.Data.Operators.License(l), "operators.json lacks " + l);
            var parts = new HashSet<string>();
            foreach (var p in TestEnv.Data.Stations.PartsKinds) parts.Add(p.Id);
            foreach (var need in new[] { "filters", "engineKit", "drivetrainKit", "hydraulicPump", "trackPads", "tracksOrTires", "battery" }) Assert.IsTrue(parts.Contains(need), "stations.json lacks parts kind " + need);
            var tiers = TestEnv.Data.Stations.WorkshopTiers;
            Assert.AreEqual(4, tiers.Count);
            for (int i = 0; i < tiers.Count; i++)
            {
                Assert.AreEqual(i + 1, tiers[i].Tier);
                if (i > 0) Assert.IsTrue(tiers[i].Bays > tiers[i - 1].Bays || tiers[i].CanRepair.Count > tiers[i - 1].CanRepair.Count || (tiers[i].CanRebuild && !tiers[i - 1].CanRebuild), "workshop tier " + (i + 1) + " adds nothing");
            }
            Assert.GreaterOrEqual(TestEnv.Data.Operators.FirstNames.Count, 20);
            Assert.GreaterOrEqual(TestEnv.Data.Operators.LastNames.Count, 20);
        }

        [Test]
        public void DataLoadsWithoutWarnings()
        {
            Assert.IsEmpty(TestEnv.Data.Warnings, string.Join("; ", TestEnv.Data.Warnings.ToArray()));
        }
    }
}
