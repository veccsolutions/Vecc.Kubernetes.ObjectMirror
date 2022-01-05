using k8s;
using k8s.Models;
using Vecc.Kubernetes.ObjectMirror.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Watchers
{
    public class SharedSecretWatcher : ClusterObjectWatcher<V1beta1SharedSecret, V1ObjectMeta>
    {
        protected override string Group => "veccsolutions.com";
        protected override string Plural => "sharedsecrets";
        protected override string ApiVersion => "v1beta1";
        protected override int InitialDelay => 0;

        public SharedSecretWatcher(IKubernetes kubernetes,
            ILogger<ClusterObjectWatcher<V1beta1SharedSecret, V1ObjectMeta>> logger,
            Dispatcher<V1beta1SharedSecret> dispatcher)
            : base(kubernetes, logger, dispatcher)
        {
        }
    }
}
