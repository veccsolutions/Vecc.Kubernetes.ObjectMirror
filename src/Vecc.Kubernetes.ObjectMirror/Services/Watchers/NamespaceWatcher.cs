using k8s;
using k8s.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Watchers
{
    public class NamespaceWatcher : ClusterObjectWatcher<V1Namespace, V1ObjectMeta>
    {
        protected override string Group => string.Empty;
        protected override string Plural => "namespaces";
        protected override string ApiVersion => "v1";
        protected override int InitialDelay => 1;

        public NamespaceWatcher(IKubernetes kubernetes,
            ILogger<ClusterObjectWatcher<V1Namespace, V1ObjectMeta>> logger,
            Dispatcher<V1Namespace> dispatcher)
            : base(kubernetes, logger, dispatcher)
        {
        }
    }
}
