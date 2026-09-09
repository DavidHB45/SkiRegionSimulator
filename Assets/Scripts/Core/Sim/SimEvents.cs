using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Sim
{
    public enum LogLevel { Info, Warning, Alert }

    /// <summary>A persisted, player-visible message.</summary>
    [Serializable]
    public sealed class GameLogEntry
    {
        public long Tick;
        public LogLevel Level;
        public string Message;
    }

    /// <summary>
    /// Typed publish/subscribe bus. The Unity view layer subscribes here; Core systems publish.
    /// Handlers are invoked synchronously during the tick, so views must only cache data, not mutate state.
    /// Events are not part of WorldState (they are transient).
    /// </summary>
    public sealed class SimEvents
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public void Subscribe<T>(Action<T> handler)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }
            list.Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            if (_handlers.TryGetValue(typeof(T), out var list)) list.Remove(handler);
        }

        public void Publish<T>(T evt)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list)) return;
            // Copy so handlers may unsubscribe during dispatch.
            var snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++) ((Action<T>)snapshot[i])(evt);
        }

        public void Clear() => _handlers.Clear();
    }

    // ---- foundation events (later milestones add their own event types next to their systems) ----

    public struct HourChangedEvent { public long AbsoluteHour; public int Day; public int HourOfDay; }
    public struct DayChangedEvent { public int Day; }
    public struct LogEvent { public GameLogEntry Entry; }
}
