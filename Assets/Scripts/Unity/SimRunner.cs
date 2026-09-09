using AlpineSim.Core.Sim;
using UnityEngine;

namespace AlpineSim.Unity
{
    /// <summary>
    /// Drives the SimulationClock from Unity's frame loop and handles the global game keys
    /// (pause, time compression, save/load). Views read state after ticks; nothing in Core knows this exists.
    /// </summary>
    public sealed class SimRunner : MonoBehaviour
    {
        private Bootstrap _boot;
        public int LastTicks { get; private set; }
        public float Fps { get; private set; }
        private float _fpsAccum;
        private int _fpsFrames;

        public event System.Action<int> Ticked;

        public void Bind(Bootstrap boot) { _boot = boot; }

        private void Update()
        {
            if (_boot == null || _boot.Sim == null || _boot.Clock == null) return;
            var input = _boot.Input;
            var clock = _boot.Clock;

            if (input.Game.Pause.WasPressedThisFrame()) clock.Paused = !clock.Paused;
            if (input.Game.CycleSpeed.WasPressedThisFrame()) clock.CycleCompression();
            if (input.Game.Speed1.WasPressedThisFrame()) clock.SetCompression(1);
            if (input.Game.Speed2.WasPressedThisFrame()) clock.SetCompression(4);
            if (input.Game.Speed3.WasPressedThisFrame()) clock.SetCompression(16);
            if (input.Game.Speed4.WasPressedThisFrame()) clock.SetCompression(60);
            if (input.Game.QuickSave.WasPressedThisFrame()) _boot.SaveGame("quicksave");
            if (input.Game.QuickLoad.WasPressedThisFrame()) { _boot.LoadGame("quicksave"); return; }

            LastTicks = clock.Advance(Time.unscaledDeltaTime);
            if (LastTicks > 0) Ticked?.Invoke(LastTicks);

            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAccum >= 0.5f)
            {
                Fps = _fpsFrames / _fpsAccum;
                _fpsAccum = 0f;
                _fpsFrames = 0;
            }
        }
    }
}
