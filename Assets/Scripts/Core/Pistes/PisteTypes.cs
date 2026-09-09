using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Pistes
{
    public enum PisteDifficulty { Green, Blue, Red, Black, Nordic, Park }
    public enum ZoneKind { Road, CatTrack, Lot, LiftRamp, BaseArea, ReservoirBank }
    public enum NodeKind { Base, LiftBottom, LiftTop, Junction, Landmark }

    /// <summary>A run: a polyline corridor of one width. Segments are consecutive point pairs.</summary>
    [Serializable]
    public sealed class PisteState
    {
        public string Id = "";
        public string Name = "";
        public PisteDifficulty Difficulty = PisteDifficulty.Blue;
        public float WidthM = 40f;
        public List<Vec2> Points = new List<Vec2>();
        public List<int> SegmentIds = new List<int>();
        /// <summary>Node ids: guests start skiing at TopNodeId and arrive at BottomNodeId.</summary>
        public string TopNodeId = "";
        public string BottomNodeId = "";
        public bool Open = true;
        public float LengthM;
        public float VerticalM;
        public float MaxGradeDeg;
        /// <summary>Piste-average PQI (0..100), refreshed every sim hour.</summary>
        public float Pqi;
        public float TrafficToday;
        public float TrafficTotal;
        public bool Groomable = true;
        public bool Nordic => Difficulty == PisteDifficulty.Nordic;
    }

    [Serializable]
    public sealed class PisteSegment
    {
        public int Id;
        public string PisteId = "";
        public int Index;
        public Vec2 A;
        public Vec2 B;
        public float WidthM;
        public float LengthM;
        public float GradeDeg;
        public float Pqi;
        public long LastPqiTick = -1;
        public float TrafficToday;
        [JsonIgnore] public List<int> Cells = new List<int>();
        public Vec2 Direction => (B - A).Normalized;
    }

    /// <summary>Road, cat track, lot, lift ramp or base disc that also lives in the snow grid.</summary>
    [Serializable]
    public sealed class SurfaceZone
    {
        public string Id = "";
        public ZoneKind Kind = ZoneKind.Lot;
        public List<Vec2> Points = new List<Vec2>();
        public float WidthM = 6f;
        public float RadiusM = 40f;
        /// <summary>0..100: cleared/plowed quality for roads and lots (100 = bare, salted, dry).</summary>
        public float ClearanceScore = 100f;
        public string Comment = "";
        [JsonIgnore] public List<int> Cells = new List<int>();
        public Vec2 Center => Points.Count > 0 ? Points[0] : Vec2.Zero;
    }

    [Serializable]
    public sealed class PisteNode
    {
        public string Id = "";
        public NodeKind Kind = NodeKind.Junction;
        public Vec2 Pos;
        public int LiftId = -1;
    }

    /// <summary>All pistes, zones and routing nodes, plus the resort-average PQI.</summary>
    [Serializable]
    public sealed class PisteNetwork
    {
        public List<PisteState> Pistes = new List<PisteState>();
        public List<PisteSegment> Segments = new List<PisteSegment>();
        public List<SurfaceZone> Zones = new List<SurfaceZone>();
        public List<PisteNode> Nodes = new List<PisteNode>();
        public float ResortPqi;
        public int NextSegmentId;

        public PisteState Piste(string id)
        {
            for (int i = 0; i < Pistes.Count; i++) if (Pistes[i].Id == id) return Pistes[i];
            return null;
        }

        public PisteSegment Segment(int id)
        {
            for (int i = 0; i < Segments.Count; i++) if (Segments[i].Id == id) return Segments[i];
            return null;
        }

        public SurfaceZone Zone(string id)
        {
            for (int i = 0; i < Zones.Count; i++) if (Zones[i].Id == id) return Zones[i];
            return null;
        }

        public PisteNode Node(string id)
        {
            for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Id == id) return Nodes[i];
            return null;
        }

        public PisteNode EnsureNode(string id, NodeKind kind, Vec2 pos, int liftId = -1)
        {
            var n = Node(id);
            if (n == null) { n = new PisteNode { Id = id, Kind = kind, Pos = pos, LiftId = liftId }; Nodes.Add(n); }
            else { n.Kind = kind; n.Pos = pos; n.LiftId = liftId; }
            return n;
        }

        /// <summary>Pistes that start at the given node (what a guest can ski after unloading there).</summary>
        public void PistesFrom(string nodeId, List<PisteState> into)
        {
            for (int i = 0; i < Pistes.Count; i++) if (Pistes[i].TopNodeId == nodeId) into.Add(Pistes[i]);
        }
    }
}
