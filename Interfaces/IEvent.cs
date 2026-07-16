namespace Pandora.Interfaces
{
    public interface IAgentEvent 
    { 
    }
    public interface IAgentEventWithResult<TResult> 
    {
    }
    public interface IEventBus 
    {
        void Subscribe<T>(Action<T> handler) where T : IAgentEvent;
        void Publish<T>(T @event) where T : IAgentEvent;
        T2? Publish<T1,T2>(T1 @event) where T1 : IAgentEventWithResult<T2>;
        void Subscribe<T1,T2>(Func<T1,T2> handler) where T1 : IAgentEventWithResult<T2>;
        bool HasHandler<T1>();
    }
}
