using System;
using System.Collections.Generic;
using AlpineSim.Core.Lifts;
using UnityEngine;

namespace AlpineSim.Unity.Lifts
{
    /// <summary>Keeps one LiftView per lift in the world; creates views for lifts staked or built later.</summary>
    public sealed class LiftViewManager : MonoBehaviour
    {
        private Bootstrap _boot;
        private readonly Dictionary<int, LiftView> _views = new Dictionary<int, LiftView>();
        private Action<LiftBuiltEvent> _onBuilt;
        public IReadOnlyDictionary<int, LiftView> Views => _views;

        public void Construct(Bootstrap boot)
        {
            _boot = boot;
            Sync();
            _onBuilt = e => Sync();
            boot.Sim.Events.Subscribe(_onBuilt);
        }

        private void Sync()
        {
            var lifts = _boot.Sim.World.Lifts.Lifts;
            var seen = new HashSet<int>();
            foreach (var l in lifts)
            {
                seen.Add(l.Id);
                if (_views.ContainsKey(l.Id)) continue;
                var type = _boot.Data.LiftType(l.TypeId);
                if (type == null) continue;
                var go = new GameObject("lift");
                go.transform.SetParent(transform, false);
                var v = go.AddComponent<LiftView>();
                v.Construct(_boot, l, type);
                _views[l.Id] = v;
            }
            var gone = new List<int>();
            foreach (var kv in _views) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone) { Destroy(_views[id].gameObject); _views.Remove(id); }
        }

        private int _syncCounter;
        private void LateUpdate()
        {
            if (_boot.Sim == null || _boot.Clock == null) return;
            if (++_syncCounter % 30 == 0 && _views.Count != _boot.Sim.World.Lifts.Lifts.Count) Sync();
            float seconds = (float)((_boot.Sim.World.Time.Tick + _boot.Clock.InterpolationAlpha) / (double)Core.Sim.SimTime.TicksPerSecond);
            foreach (var v in _views.Values) v.Present(seconds);
        }
    }
}
