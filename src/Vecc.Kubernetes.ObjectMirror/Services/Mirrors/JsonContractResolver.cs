using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Vecc.Kubernetes.ObjectMirror.Services.Mirrors
{
    public class JsonContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            var attribute = member.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attribute != null)
            {
                property.PropertyName = attribute.Name;
            }

            return property;
        }
    }
}
