using Newtonsoft.Json;

namespace Vecc.Kubernetes.ObjectMirror.Models
{
    public class V1beta1SharedSecretTarget
    {
        [JsonProperty(PropertyName = "allowedNamespaces")]
        public string[]? AllowedNamespaces { get; set; }
    }
}
