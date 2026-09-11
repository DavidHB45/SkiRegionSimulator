using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Unity.Input;
using AlpineSim.Unity.Terrain;
using UnityEngine;

namespace AlpineSim.Unity.Construction
{
    /// <summary>
    /// One modal placement session at a time: collects map points from terrain clicks and reports
    /// them to whoever started the session (lift staking, run staking, gun and hydrant placement).
    /// Draws the picked points and the live cursor point as a polyline.
    /// </summary>
    public sealed class PlacementController : MonoBehaviour
    {
        public sealed class Session
        {
            public string Label = "";
            public int MaxPoints = 2;
            public List<Vec2> Points = new List<Vec2>();
            public Vec2 Cursor;
            public bool HasCursor;
            public float HeadingDeg;
            public Action<Session> OnChanged;
            public Action<Session> OnFinished;
            public Action OnCancelled;
            public Func<Session, string> Preview;
        }

        private Bootstrap _boot;
        private Session _session;
        private LineRenderer _line;
        private readonly List<Vector3> _pts = new List<Vector3>();
        public Session Current => _session;
        public bool Active => _session != null;

        public void Construct(Bootstrap boot)
        {
            _boot = boot;
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.startWidth = 1.5f;
            _line.endWidth = 1.5f;
            _line.positionCount = 0;
            var sh = Shader.Find("Sprites/Default");
            _line.sharedMaterial = new Material(sh != null ? sh : Shader.Find("AlpineSim/VertexColor")) { name = "placement" };
            _line.startColor = new Color(1f, 0.85f, 0.2f);
            _line.endColor = new Color(1f, 0.85f, 0.2f);
            boot.Ui.Hud.AddStatusProvider(Status);
        }

        public void Begin(Session s)
        {
            Cancel();
            _session = s;
        }

        public void Cancel()
        {
            var s = _session;
            _session = null;
            _line.positionCount = 0;
            if (s != null) s.OnCancelled?.Invoke();
        }

        private void Finish()
        {
            var s = _session;
            _session = null;
            _line.positionCount = 0;
            if (s != null && s.Points.Count > 0) s.OnFinished?.Invoke(s);
            else s?.OnCancelled?.Invoke();
        }

        private void Update()
        {
            if (_session == null || _boot.Sim == null) return;
            var input = _boot.Input.Placement;
            if (input.Cancel.WasPressedThisFrame() || _boot.Input.Game.Escape.WasPressedThisFrame()) { Cancel(); return; }
            if (input.Undo.WasPressedThisFrame() && _session.Points.Count > 0) { _session.Points.RemoveAt(_session.Points.Count - 1); _session.OnChanged?.Invoke(_session); }
            float rot = input.Rotate.ReadValue<float>();
            if (Mathf.Abs(rot) > 0.01f) { _session.HeadingDeg = Mathf.Repeat(_session.HeadingDeg + (rot > 0f ? 15f : -15f), 360f); _session.OnChanged?.Invoke(_session); }
            bool overUi = _boot.Ui.PointerOverUi;
            _session.HasCursor = !overUi && TerrainPicker.Pick(_boot.Cameras.Camera, InputBindings.MousePosition, _boot.Sim.Terrain, out _session.Cursor);
            if (_session.HasCursor && input.Place.WasPressedThisFrame())
            {
                _session.Points.Add(_session.Cursor);
                _session.OnChanged?.Invoke(_session);
                if (_session.Points.Count >= _session.MaxPoints) { Finish(); return; }
            }
            if (input.Finish.WasPressedThisFrame() && _session.Points.Count > 0) { Finish(); return; }
            DrawLine();
        }

        private void DrawLine()
        {
            _pts.Clear();
            foreach (var p in _session.Points) _pts.Add(_boot.SurfacePoint(p.X, p.Y) + Vector3.up * 2f);
            if (_session.HasCursor) _pts.Add(_boot.SurfacePoint(_session.Cursor.X, _session.Cursor.Y) + Vector3.up * 2f);
            _line.positionCount = _pts.Count;
            for (int i = 0; i < _pts.Count; i++) _line.SetPosition(i, _pts[i]);
        }

        private string Status()
        {
            if (_session == null) return "";
            string s = "<b>" + _session.Label + "</b>  " + _session.Points.Count + "/" + _session.MaxPoints + " points  (left click place, right click cancel, Backspace undo" + (_session.MaxPoints > 2 ? ", Enter finish" : "") + ")";
            if (_session.HasCursor) s += "\ncursor " + UI.UiFactory.F(_session.Cursor.X, 0) + ", " + UI.UiFactory.F(_session.Cursor.Y, 0) + "  elev " + UI.UiFactory.F(_boot.Sim.Terrain.SampleHeight(_session.Cursor.X, _session.Cursor.Y), 0) + " m";
            if (_session.Preview != null) { string p = _session.Preview(_session); if (!string.IsNullOrEmpty(p)) s += "\n" + p; }
            return s;
        }
    }
}
