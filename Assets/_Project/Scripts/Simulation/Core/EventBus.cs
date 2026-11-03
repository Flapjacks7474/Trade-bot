using System;
using System.Collections.Generic;

namespace FFTS.Core
{
    /// <summary>Marker interface for events published on the <see cref="EventBus"/>.</summary>
    public interface IGameEvent { }

    /// <summary>
    /// Synchronous, typed publish/subscribe hub. Systems communicate through events instead of
    /// reaching into each other's private state. Handler exceptions are isolated so one faulty
    /// listener cannot break unrelated systems, and recursion depth is bounded to catch event loops.
    /// </summary>
    public sealed class EventBus
    {
        public const int MaxPublishDepth = 32;

        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();
        private int _depth;

        /// <summary>Raised when a handler throws. The bus keeps delivering to other handlers.</summary>
        public event Action<IGameEvent, Exception> HandlerFailed;

        public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public int SubscriberCount<T>() where T : IGameEvent
            => _handlers.TryGetValue(typeof(T), out var list) ? list.Count : 0;

        public void Publish<T>(T evt) where T : IGameEvent
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            if (_depth >= MaxPublishDepth)
                throw new InvalidOperationException($"Event publish depth exceeded while publishing {typeof(T).Name}; probable event loop.");

            // Snapshot so handlers may subscribe/unsubscribe during delivery.
            var snapshot = list.ToArray();
            _depth++;
            try
            {
                foreach (var d in snapshot)
                {
                    try { ((Action<T>)d)(evt); }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("publish depth")) { throw; }
                    catch (Exception ex) { HandlerFailed?.Invoke(evt, ex); }
                }
            }
            finally { _depth--; }
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;
            public Subscription(Action dispose) { _dispose = dispose; }
            public void Dispose() { _dispose?.Invoke(); _dispose = null; }
        }
    }
}
