using System.Text.Json.Serialization;

namespace Vecc.Kubernetes.ObjectMirror.Models
{
    public class V1beta1SharedSecretSpec
    {
        [JsonPropertyName("finalizers")]
        public IList<string>? Finalizers
        {
            get;
            set;
        }

        [JsonPropertyName("source")]
        public V1beta1SharedSecretSource? Source
        {
            get;
            set;
        }

        [JsonPropertyName("target")]
        public V1beta1SharedSecretTarget? Target
        {
            get;
            set;
        }

        public V1beta1SharedSecretSpec()
        {
        }

        public V1beta1SharedSecretSpec(IList<string>? finalizers = null)
        {
            Finalizers = finalizers;
        }

        public virtual void Validate()
        {
        }
    }
}
