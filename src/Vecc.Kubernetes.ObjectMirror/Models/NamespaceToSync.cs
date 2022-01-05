using k8s.Models;

namespace Vecc.Kubernetes.ObjectMirror.Models
{
    public class NamespaceToSync
    {
        public string Namespace { get; set; }
        public V1beta1SharedSecret SharedSecret { get; set; }

        public NamespaceToSync(V1beta1SharedSecret sharedSecret, string v1Namespace)
        {
            Namespace = v1Namespace;
            SharedSecret = sharedSecret;
        }
    }
}
