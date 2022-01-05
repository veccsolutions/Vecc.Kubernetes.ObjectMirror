using System.Collections.Concurrent;
using Vecc.Kubernetes.ObjectMirror.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services
{
    public class SharedData
    {
        public List<string> KnownNamespaces { get; set; } = new List<string>();
        public List<string> KnownSecrets { get; set; } = new List<string>();
        public ConcurrentQueue<NamespaceToSync> NamespacesToSync { get; set; } = new ConcurrentQueue<NamespaceToSync>();
        public AutoResetEvent ResetEvent { get; } = new AutoResetEvent(false);
        public ConcurrentQueue<DispatchedEvent<V1beta1SharedSecret>> SecretsToSync { get; } = new ConcurrentQueue<DispatchedEvent<V1beta1SharedSecret>>();
        public List<V1beta1SharedSecret> SharedSecrets { get; set; } = new List<V1beta1SharedSecret>();
    }
}
