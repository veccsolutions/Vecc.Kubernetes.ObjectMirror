using Newtonsoft.Json;

namespace Vecc.Kubernetes.ObjectMirror.Models
{
    public class V1beta1SharedSecretTarget
    {
        [JsonProperty(PropertyName = "allowedNamespaces")]
        public string[]? AllowedNamespaces { get; set; }

        [JsonProperty(PropertyName = "blockedNamespaces")]
        public string[]? BlockedNamespaces { get; set; }
    }
}
