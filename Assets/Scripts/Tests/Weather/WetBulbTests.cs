using AlpineSim.Core.Weather;
using NUnit.Framework;

namespace AlpineSim.Tests.Weather
{
    /// <summary>Wet-bulb boundaries: the Stull approximation against published values and the snowmaking window edges.</summary>
    [TestFixture]
    public sealed class WetBulbTests
    {
        [TestCase(0f, 100f, 0f, 0.05f)]       // saturated air: wet-bulb equals dry-bulb
        [TestCase(0f, 50f, -2.84f, 0.15f)]    // psychrometric table: 0 C / 50 % at sea level
        [TestCase(-5f, 30f, -8.08f, 0.15f)]   // cold and dry: good snowmaking
        [TestCase(2f, 30f, -2.46f, 0.15f)]    // above freezing but dry: still marginal
        [TestCase(-10f, 80f, -10.64f, 0.15f)]
        [TestCase(10f, 20f, 2.64f, 0.15f)]
        public void PsychrometricSolutionMatchesTables(float tempC, float rh, float expected, float tolerance)
        {
            float wb = WetBulb.FromTempAndHumidity(tempC, rh);
            Assert.AreEqual(expected, wb, tolerance, "wet-bulb for " + tempC + " C / " + rh + "%");
        }

        [Test]
        public void LowerPressureAtAltitudeLowersTheWetBulb()
        {
            float sea = WetBulb.FromTempAndHumidity(0f, 50f, 1013.25f);
            float alpine = WetBulb.FromTempAndHumidity(0f, 50f, WetBulb.PressureAtElevationHpa(1400f));
            Assert.AreEqual(856f, WetBulb.PressureAtElevationHpa(1400f), 2f);
            Assert.Less(alpine, sea);
            Assert.AreEqual(-3.16f, alpine, 0.15f);
        }

        [Test]
        public void WetBulbNeverExceedsDryBulb()
        {
            for (int t = -25; t <= 10; t++)
                for (int rh = 5; rh <= 100; rh += 5)
                    Assert.LessOrEqual(WetBulb.FromTempAndHumidity(t, rh), t + 0.01f, "T=" + t + " RH=" + rh);
        }

        [Test]
        public void WetBulbIsMonotonicInHumidityAndTemperature()
        {
            for (int t = -20; t <= 5; t++)
            {
                float prev = float.MinValue;
                for (int rh = 10; rh <= 100; rh += 10)
                {
                    float wb = WetBulb.FromTempAndHumidity(t, rh);
                    Assert.GreaterOrEqual(wb, prev - 1e-4f, "wet-bulb must not fall as humidity rises (T=" + t + ")");
                    prev = wb;
                }
            }
            for (int rh = 20; rh <= 100; rh += 20)
            {
                float prev = float.MinValue;
                for (int t = -20; t <= 5; t++)
                {
                    float wb = WetBulb.FromTempAndHumidity(t, rh);
                    Assert.GreaterOrEqual(wb, prev - 1e-4f, "wet-bulb must not fall as temperature rises (RH=" + rh + ")");
                    prev = wb;
                }
            }
        }

        [Test]
        public void LapseRateCoolsTheSummit()
        {
            float baseT = -1f;
            float top = WetBulb.TempAtElevation(baseT, 1400f, 1900f, 0.65f);
            Assert.AreEqual(-4.25f, top, 1e-3f);
            Assert.AreEqual(baseT, WetBulb.TempAtElevation(baseT, 1400f, 1400f, 0.65f), 1e-6f);
        }

        [Test]
        public void SnowmakingWindowEdgesFromTuning()
        {
            float threshold = TestEnv.Data.Tuning.F("snowmaking.wetBulbMaxC");
            Assert.Less(threshold, 0f);
            Assert.Greater(threshold, -6f);
            // humid air at -2 C sits on the wrong side of the window; dry air at -2 C is inside it
            Assert.Greater(WetBulb.FromTempAndHumidity(-2f, 98f), threshold);
            Assert.Less(WetBulb.FromTempAndHumidity(-2f, 40f), threshold);
            Assert.Less(WetBulb.FromTempAndHumidity(-2f, 90f, WetBulb.PressureAtElevationHpa(1400f)), threshold);
        }
    }
}
