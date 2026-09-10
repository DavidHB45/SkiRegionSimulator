using AlpineSim.Core.Fleet;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;
using NUnit.Framework;

namespace AlpineSim.Tests.Fleet
{
    /// <summary>Fuel logistics: a dry tank halts the machine, a service call brings fuel, and an empty depot blocks the night.</summary>
    [TestFixture]
    public sealed class FuelLogisticsTests
    {
        private static VehicleState Find(Simulation sim, string defId)
        {
            foreach (var v in sim.World.Vehicles.List) if (v.DefId == defId) return v;
            Assert.Fail("scenario has no " + defId);
            return null;
        }

        private static OperatorState OperatorWith(Simulation sim, OperatorLicense l)
        {
            foreach (var o in sim.World.Fleet.Operators) if (!o.IsPlayer && o.Has(l) && o.AssignedVehicleId < 0) return o;
            Assert.Fail("no free operator with " + l);
            return null;
        }

        private static void StrandTheCat(Simulation sim, out VehicleState cat)
        {
            var ctx = sim.Ctx;
            var vs = sim.GetSystem<VehicleSystem>();
            var fs = sim.GetSystem<FleetSystem>();
            cat = Find(sim, "groomer_mid");
            var op = OperatorWith(sim, OperatorLicense.Groomer);
            Assert.IsTrue(fs.AssignOperator(ctx, op.Id, cat.Id, out string reason), reason);
            sim.World.Fleet.Fuel.DieselL = 0f; // the depot is dry, so the reserve run home cannot save it
            cat.Fuel = 0.6f; // litres: enough to start and drive a couple of hundred metres
            for (int i = 0; i < 40 && !cat.EngineOn; i++) vs.StartEngine(ctx, cat.Id, out _);
            Assert.IsTrue(cat.EngineOn);
            vs.AssignGroom(ctx, cat.Id, "t1");
            for (int t = 0; t < SimTime.TicksPerHour && !cat.Stranded; t++) sim.Step();
        }

        [Test]
        public void RunningDryHaltsTheMachineAndRaisesAServiceCall()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 31, "test_small");
            StrandTheCat(sim, out var cat);
            Assert.IsTrue(cat.Stranded, "the cat should run dry within the hour");
            StringAssert.Contains("fuel", cat.StrandedReason);
            Assert.LessOrEqual(cat.Fuel, 0.02f);
            sim.StepSeconds(30);
            Assert.Less(cat.SpeedKmh, 0.1f, "a stranded machine does not move");
            ServiceCall call = null;
            foreach (var c in sim.World.Fleet.ServiceCalls) if (c.VehicleId == cat.Id && !c.Resolved) call = c;
            Assert.IsNotNull(call, "running dry should raise a service call");
            var task = sim.GetSystem<TaskSystem>().Get(sim.Ctx, call.TaskId);
            Assert.IsNotNull(task);
            Assert.AreEqual(TaskKind.Refuel, task.Kind);
        }

        [Test]
        public void ServiceTruckRefuelsAStrandedMachine()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 32, "test_small");
            var ctx = sim.Ctx;
            StrandTheCat(sim, out var cat);
            Assert.IsTrue(cat.Stranded);
            var fs = sim.GetSystem<FleetSystem>();
            var ts = sim.GetSystem<TaskSystem>();
            var truck = Find(sim, "service_truck");
            Assert.Greater(truck.FuelCargoL, 100f, "the service truck starts with fuel cargo");
            var driver = OperatorWith(sim, OperatorLicense.Cdl);
            Assert.IsTrue(fs.AssignOperator(ctx, driver.Id, truck.Id, out string reason), reason);
            ServiceCall call = null;
            foreach (var c in sim.World.Fleet.ServiceCalls) if (c.VehicleId == cat.Id && !c.Resolved) call = c;
            Assert.IsNotNull(call);
            Assert.IsTrue(ts.Assign(ctx, call.TaskId, truck.Id, out reason), reason);
            float cargoBefore = truck.FuelCargoL;
            for (int t = 0; t < SimTime.TicksPerHour * 3 && cat.Stranded; t++) sim.Step();
            Assert.IsFalse(cat.Stranded, "the service truck should have refuelled the cat within three hours");
            Assert.Greater(cat.Fuel, 20f, "the cat should have fuel again");
            Assert.Less(truck.FuelCargoL, cargoBefore, "fuel came out of the truck's cargo tank");
            bool open = false;
            foreach (var c in sim.World.Fleet.ServiceCalls) if (c.VehicleId == cat.Id && !c.Resolved) open = true;
            Assert.IsFalse(open, "the service call should be resolved");
        }

        [Test]
        public void EmptyDepotBlocksRefuellingUntilADeliveryArrives()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 33, "test_small");
            var ctx = sim.Ctx;
            var fs = sim.GetSystem<FleetSystem>();
            var cat = Find(sim, "groomer_mid");
            var depot = sim.World.Fleet.Fuel;
            depot.DieselL = 0f;
            cat.Fuel = 1f;
            float got = fs.RefuelAtDepot(ctx, cat.Id, out string reason);
            Assert.AreEqual(0f, got);
            StringAssert.Contains("dry", reason);
            Assert.IsTrue(fs.OrderFuel(ctx, 5000f, false, out reason), reason);
            Assert.AreEqual(1, depot.Pending.Count);
            float lead = TestEnv.Data.Stations.FuelDepot.DeliveryLeadHours;
            sim.StepHours(lead * 0.5);
            Assert.AreEqual(0f, fs.RefuelAtDepot(ctx, cat.Id, out _), "the delivery has not arrived yet");
            sim.StepHours(lead * 0.5 + 1.5);
            Assert.AreEqual(0, depot.Pending.Count, "the delivery should have landed");
            Assert.Greater(depot.DieselL, 1000f);
            Assert.Greater(fs.RefuelAtDepot(ctx, cat.Id, out reason), 100f, reason);
        }

        [Test]
        public void AiMachinesReturnToRefuelBeforeTheyRunDry()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 34, "test_small");
            var ctx = sim.Ctx;
            var vs = sim.GetSystem<VehicleSystem>();
            var fs = sim.GetSystem<FleetSystem>();
            var cat = Find(sim, "groomer_mid");
            var def = TestEnv.Data.Vehicle(cat.DefId);
            var op = OperatorWith(sim, OperatorLicense.Groomer);
            Assert.IsTrue(fs.AssignOperator(ctx, op.Id, cat.Id, out string reason), reason);
            float reserve = TestEnv.Data.Tuning.F("vehicles.fuelReserveWarningFrac");
            cat.Fuel = def.FuelCapacityL * reserve * 1.3f;
            for (int i = 0; i < 40 && !cat.EngineOn; i++) vs.StartEngine(ctx, cat.Id, out _);
            vs.AssignGroom(ctx, cat.Id, "t1");
            bool wentForFuel = false;
            for (int t = 0; t < SimTime.TicksPerHour * 2; t++)
            {
                sim.Step();
                if (cat.Ai.Mode == AiMode.Refuel) wentForFuel = true;
                if (cat.Stranded) break;
            }
            Assert.IsTrue(wentForFuel, "the AI should head for the depot at the reserve threshold");
            Assert.IsFalse(cat.Stranded, "the AI should not run dry when the depot has fuel");
        }
    }
}
