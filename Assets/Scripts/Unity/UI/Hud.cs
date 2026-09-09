using System;
using System.Collections.Generic;
using System.Text;
using AlpineSim.Core.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>
    /// Always-on overlay: clock, speed, cash/PQI lines contributed by later milestones, the message
    /// ticker, and a hotkey legend. Milestone views register status providers instead of editing this class.
    /// </summary>
    public sealed class Hud
    {
        private readonly Bootstrap _boot;
        private readonly Text _clockText;
        private readonly Text _statusText;
        private readonly Text _logText;
        private readonly Text _legendText;
        private readonly Text _rightText;
        private readonly List<Func<string>> _statusProviders = new List<Func<string>>();
        private readonly List<Func<string>> _rightProviders = new List<Func<string>>();
        private readonly List<string> _legend = new List<string>();
        private readonly StringBuilder _sb = new StringBuilder(512);
        private readonly List<GameLogEntry> _recent = new List<GameLogEntry>();
        private Action<LogEvent> _logHandler;

        public Hud(Bootstrap boot, Transform canvas)
        {
            _boot = boot;
            var top = UiFactory.Panel("HudTop", canvas, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -34f), new Vector2(0f, 0f), UiFactory.PanelBg);
            _clockText = UiFactory.FillLabel("Clock", top, "", 16, TextAnchor.MiddleLeft, 8f);
            _rightText = UiFactory.FillLabel("Right", top, "", 14, TextAnchor.MiddleRight, 8f, UiFactory.TextDim);

            var left = UiFactory.Panel("HudStatus", canvas, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -240f), new Vector2(360f, -36f), new Color(0f, 0f, 0f, 0.35f));
            _statusText = UiFactory.FillLabel("Status", left, "", 13, TextAnchor.UpperLeft, 8f);

            var bottom = UiFactory.Panel("HudLog", canvas, new Vector2(0f, 0f), new Vector2(0.55f, 0f), new Vector2(0f, 0f), new Vector2(0f, 110f), new Color(0f, 0f, 0f, 0.45f));
            _logText = UiFactory.FillLabel("Log", bottom, "", 13, TextAnchor.LowerLeft, 8f);

            var legend = UiFactory.Panel("HudLegend", canvas, new Vector2(0.55f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 110f), new Color(0f, 0f, 0f, 0.35f));
            _legendText = UiFactory.FillLabel("Legend", legend, "", 12, TextAnchor.LowerLeft, 8f, UiFactory.TextDim);

            AddLegend("WASD/QE fly  RMB look  Shift fast  Scroll speed  C camera");
            AddLegend("P/Space pause  1-4 or Tab time compression  F5 save  F9 load");
        }

        public void AddStatusProvider(Func<string> provider) => _statusProviders.Add(provider);
        public void AddRightProvider(Func<string> provider) => _rightProviders.Add(provider);
        public void AddLegend(string line) { _legend.Add(line); _legendText.text = string.Join("\n", _legend.ToArray()); }

        public void OnWorldBuilt()
        {
            _recent.Clear();
            foreach (var e in _boot.Sim.World.Log) Push(e);
            _logHandler = e => Push(e.Entry);
            _boot.Sim.Events.Subscribe(_logHandler);
        }

        public void OnWorldTeardown()
        {
            if (_boot.Sim != null && _logHandler != null) _boot.Sim.Events.Unsubscribe(_logHandler);
            _logHandler = null;
        }

        private void Push(GameLogEntry e)
        {
            _recent.Add(e);
            if (_recent.Count > 6) _recent.RemoveAt(0);
        }

        public void Update()
        {
            var sim = _boot.Sim;
            if (sim == null) return;
            var clock = _boot.Clock;
            var t = sim.World.Time;
            _sb.Length = 0;
            _sb.Append("<b>").Append(t.ToString()).Append("</b>   ");
            _sb.Append(clock.Paused ? UiFactory.ColorTag(UiFactory.Warn, "PAUSED") : ("x" + clock.Compression));
            _sb.Append("   ").Append(IsNight(t) ? UiFactory.ColorTag(new Color(0.6f, 0.7f, 1f), "NIGHT SHIFT") : UiFactory.ColorTag(UiFactory.Warn, "DAY - RESORT OPEN"));
            _clockText.text = _sb.ToString();

            _sb.Length = 0;
            for (int i = 0; i < _rightProviders.Count; i++)
            {
                string s = _rightProviders[i]();
                if (string.IsNullOrEmpty(s)) continue;
                if (_sb.Length > 0) _sb.Append("   ");
                _sb.Append(s);
            }
            if (_sb.Length > 0) _sb.Append("   ");
            _sb.Append(UiFactory.F(_boot.Runner.Fps, 0)).Append(" fps  cam:").Append(_boot.Cameras.Mode);
            _rightText.text = _sb.ToString();

            _sb.Length = 0;
            for (int i = 0; i < _statusProviders.Count; i++)
            {
                string s = _statusProviders[i]();
                if (string.IsNullOrEmpty(s)) continue;
                if (_sb.Length > 0) _sb.Append('\n');
                _sb.Append(s);
            }
            _statusText.text = _sb.ToString();

            _sb.Length = 0;
            for (int i = 0; i < _recent.Count; i++)
            {
                var e = _recent[i];
                var st = new SimTime { Tick = e.Tick, StartHour = t.StartHour, StartDayOfWeek = t.StartDayOfWeek };
                Color c = e.Level == LogLevel.Alert ? UiFactory.Bad : (e.Level == LogLevel.Warning ? UiFactory.Warn : UiFactory.TextColor);
                _sb.Append(UiFactory.ColorTag(UiFactory.TextDim, st.Clock + " ")).Append(UiFactory.ColorTag(c, e.Message));
                if (i < _recent.Count - 1) _sb.Append('\n');
            }
            _logText.text = _sb.ToString();
        }

        private bool IsNight(SimTime t)
        {
            var tuning = _boot.Data.Tuning;
            int open = tuning.I("simulation.resortOpenHour");
            int close = tuning.I("simulation.resortCloseHour");
            return t.HourOfDay < open || t.HourOfDay >= close;
        }
    }
}
