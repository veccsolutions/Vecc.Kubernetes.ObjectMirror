using k8s;
using System.Collections.Concurrent;
using Vecc.Kubernetes.ObjectMirror.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services
{
    public class Dispatcher<T>
        where T : class
    {
        private readonly ConcurrentQueue<DispatchedEvent<T>> _queue;
        private readonly AutoResetEvent _waiter;

        public int QueueCount { get => _queue.Count; }

        public Dispatcher()
        {
            _queue = new ConcurrentQueue<DispatchedEvent<T>>();
            _waiter = new AutoResetEvent(false);
        }

        public void Dispatch(T item, WatchEventType watchEvent)
        {
            _queue.Enqueue(new DispatchedEvent<T>
            {
                Item = item,
                EventType = watchEvent,
            });
            _waiter.Set();
        }

        public Task<DispatchedEvent<T>> GetItemAsync(CancellationToken cancellationToken)
        {
            return Task.Run<DispatchedEvent<T>>(() =>
            {
                //it's possible that when many items are added to the queue in rapid succession the waiter
                // doesn't get reset. This happens when the namespaces/sharedsecrets are re-added to the list.
                // This acts as a circuit breaker for those scenarios.
                if (_queue.TryDequeue(out var item))
                {
                    return item;
                }
                _waiter.WaitOne(new TimeSpan(0, 0, 1));
                if (_queue.TryDequeue(out var result))
                {
                    return result;
                }
                return null;
            }, cancellationToken);
        }
    }
}
