using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Serialization;
using NUnit.Framework;

namespace AlpineSim.Tests.Foundation
{
    public class JsonTests
    {
        private enum Colour { Red, Green }

        private class Nested { public float F = 1.5f; public string S = "x"; public Colour C = Colour.Green; }

        private class Sample
        {
            public int I = 7;
            public long L = 123456789012345L;
            public ulong U = 18446744073709551615UL;
            public float F = 0.1f;
            public double D = 3.25;
            public bool B = true;
            public string S = "hé \"quoted\"\n";
            public string NullS = null;
            public int[] Arr = { 1, 2, 3 };
            public List<Nested> List = new List<Nested> { new Nested(), new Nested { F = -2f, S = null, C = Colour.Red } };
            public Dictionary<string, float> Dict = new Dictionary<string, float> { { "b", 2f }, { "a", 1f } };
            public Vec2 V = new Vec2(1.25f, -3f);
            public float? Opt = null;
            [JsonIgnore] public int Ignored = 99;
        }

        [Test]
        public void RoundTripPreservesEveryField()
        {
            var s = new Sample { Ignored = 5 };
            string json = JsonMapper.ToJsonString(s);
            var back = JsonMapper.FromJson<Sample>(json);
            Assert.AreEqual(s.I, back.I);
            Assert.AreEqual(s.L, back.L);
            Assert.AreEqual(s.U, back.U);
            Assert.AreEqual(s.F, back.F);
            Assert.AreEqual(s.D, back.D);
            Assert.AreEqual(s.B, back.B);
            Assert.AreEqual(s.S, back.S);
            Assert.IsNull(back.NullS);
            CollectionAssert.AreEqual(s.Arr, back.Arr);
            Assert.AreEqual(2, back.List.Count);
            Assert.AreEqual(-2f, back.List[1].F);
            Assert.IsNull(back.List[1].S);
            Assert.AreEqual(Colour.Red, back.List[1].C);
            Assert.AreEqual(1f, back.Dict["a"]);
            Assert.AreEqual(s.V, back.V);
            Assert.IsNull(back.Opt);
            Assert.AreEqual(99, back.Ignored, "ignored fields keep their default");
        }

        [Test]
        public void FloatsRoundTripExactly()
        {
            float[] values = { 0.1f, 1f / 3f, 123456.789f, -1e-7f, 5.5e10f, float.Epsilon, float.MaxValue };
            foreach (var v in values)
            {
                var node = JsonNode.FromFloat(v);
                var parsed = JsonParser.Parse(JsonWriter.Write(node));
                Assert.AreEqual(v, parsed.AsFloat, "float " + v);
            }
        }

        [Test]
        public void ParserHandlesCommentsAndEscapes()
        {
            var node = JsonParser.Parse("{ // comment\n \"a\": [1, 2.5, -3e2, true, null, \"\\u0041\\n\"], /* block */ \"b\": {} }");
            Assert.AreEqual(6, node["a"].Count);
            Assert.AreEqual(-300, node["a"][2].AsInt);
            Assert.AreEqual("A\n", node["a"][5].AsString);
            Assert.IsTrue(node["b"].IsObject);
            Assert.IsTrue(node["missing"].IsNull);
        }

        [Test]
        public void ParserRejectsGarbage()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\": }"));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("[1, 2"));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{} x"));
        }

        [Test]
        public void SortedWriterIsCanonical()
        {
            var a = JsonParser.Parse("{\"z\":1,\"a\":{\"y\":2,\"b\":3}}");
            var b = JsonParser.Parse("{\"a\":{\"b\":3,\"y\":2},\"z\":1}");
            Assert.AreEqual(JsonWriter.Write(a, false, true), JsonWriter.Write(b, false, true));
            Assert.AreNotEqual(JsonWriter.Write(a, false, false), JsonWriter.Write(b, false, false));
        }

        [Test]
        public void MissingMembersKeepDefaults()
        {
            var s = JsonMapper.FromJson<Sample>("{\"I\": 3}");
            Assert.AreEqual(3, s.I);
            Assert.AreEqual(0.1f, s.F);
            Assert.AreEqual(2, s.List.Count);
        }
    }
}
