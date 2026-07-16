using System.Collections.Concurrent;
using Pandora.Interfaces;

namespace Pandora.Event
{
    public class EventBus : IEventBus
    {
        private readonly ConcurrentDictionary<Type, Delegate> _events = new();
        public void Subscribe<T>(Action<T> handler) where T : IAgentEvent
        {
            var type = typeof(T);
            _events[type] = handler;
        }
        public void Subscribe<T1,T2>(Func<T1,T2> handler) where T1 : IAgentEventWithResult<T2>
        {
            var type = typeof(T1);
            _events[type] = handler;
        }
        public T2? Publish<T1,T2>(T1 @event) where T1 : IAgentEventWithResult<T2>
        {
            var type = typeof(T1);
            if (_events.TryGetValue(type, out var handler))
            {
                return ((Func<T1,T2>)handler)(@event);
            }
            return default;
        }
        public bool HasHandler<T1>()
        {
            var type = typeof(T1);
            return _events.ContainsKey(type);
        }
        public void Publish<T>(T @event) where T : IAgentEvent
        {
            var type = typeof(T);
            if (_events.TryGetValue(type, out var handler))
            {
                ((Action<T>)handler)(@event);
            }
        }
    }
}
