using k8s;
using k8s.Models;
using System.Text.Json.Serialization;

namespace Vecc.Kubernetes.ObjectMirror.Models
{
    [KubernetesEntity(Group = "veccsolutions.com", Kind = "SharedSecret", ApiVersion = "betav1", PluralName = "sharedsecrets")]
    public class V1beta1SharedSecret : IKubernetesObject<V1ObjectMeta>, IKubernetesObject, IMetadata<V1ObjectMeta>, ISpec<V1beta1SharedSecretSpec>, IValidate
    {
        public const string KubeApiVersion = "betav1";

        public const string KubeKind = "SharedSecret";

        public const string KubeGroup = "veccsolutions.com";

        [JsonPropertyName("apiVersion")]
        public string? ApiVersion
        {
            get;
            set;
        }

        [JsonPropertyName("kind")]
        public string? Kind
        {
            get;
            set;
        }

        [JsonPropertyName("metadata")]
        public V1ObjectMeta? Metadata
        {
#pragma warning disable CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
            get;
#pragma warning restore CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
            set;
        }

        [JsonPropertyName("spec")]
        public V1beta1SharedSecretSpec? Spec
        {
#pragma warning disable CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
            get;
#pragma warning restore CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
            set;
        }

        public V1beta1SharedSecret()
        {
        }

        public V1beta1SharedSecret(string? apiVersion = null, string? kind = null, V1ObjectMeta? metadata = null, V1beta1SharedSecretSpec? spec = null)
        {
            ApiVersion = apiVersion;
            Kind = kind;
            Metadata = metadata;
            Spec = spec;
        }


        public virtual void Validate()
        {
            Metadata?.Validate();
            Spec?.Validate();
        }
    }
}
