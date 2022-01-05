using k8s;

namespace Vecc.Kubernetes.ObjectMirror.Models
{
    public class DispatchedEvent<T>
        where T : class
    {
        public WatchEventType EventType { get; set; }
        public T? Item { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}
