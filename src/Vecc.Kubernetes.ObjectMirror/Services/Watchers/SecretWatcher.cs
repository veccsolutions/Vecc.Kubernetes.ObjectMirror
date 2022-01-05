using k8s;
using k8s.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Watchers
{
    public class SecretWatcher : NamespacedObjectWatcher<V1Secret, V1ObjectMeta>
    {
        protected override string ApiVersion => "v1";
        protected override string Group => string.Empty;
        protected override string Plural => "secrets";
        protected override int InitialDelay => 1;

        public SecretWatcher(IKubernetes kubernetes,
            ILogger<NamespacedObjectWatcher<V1Secret, V1ObjectMeta>> logger,
            Dispatcher<V1Secret> dispatcher)
            : base(kubernetes, logger, dispatcher)
        {
        }
    }
}
