using Newtonsoft.Json;

namespace Vecc.Kubernetes.ObjectMirror.Models
{
    public class V1beta1SharedSecretSource
    {
        [JsonProperty(PropertyName = "name")]
        public string? Name
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "namespace")]
        public string? Namespace
        {
            get;
            set;
        }
    }
}
