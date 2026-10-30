using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApiExtractor
{
    // Formato do .api.json lido pelo site (pbs-loader-docs/scripts/reference). Mudança incompatível sobe o Schema.
    public sealed class ApiDocument
    {
        public int Schema { get; set; } = 1;
        public string Assembly { get; set; }
        public string Version { get; set; }
        public List<ApiType> Types { get; set; } = new List<ApiType>();
    }

    public sealed class ApiType
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Namespace { get; set; }
        public string Kind { get; set; }
        public string DeclaringType { get; set; }
        public string BaseType { get; set; }
        public string Signature { get; set; }
        public string Obsolete { get; set; }
        public ApiDocs Docs { get; set; }
        public List<ApiMember> Members { get; set; } = new List<ApiMember>();
    }

    public sealed class ApiMember
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Signature { get; set; }
        public bool IsStatic { get; set; }
        public string Value { get; set; }
        public string Obsolete { get; set; }
        public ApiDocs Docs { get; set; }
        public List<ApiParameter> Parameters { get; set; }
        public ApiReturn Returns { get; set; }
    }

    public sealed class ApiParameter
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
    }

    public sealed class ApiReturn
    {
        public string Type { get; set; }
        public string Description { get; set; }
    }

    public sealed class ApiDocs
    {
        public string Summary { get; set; }
        public string Remarks { get; set; }
        public string Returns { get; set; }
        public string Value { get; set; }
        public string Example { get; set; }
        public Dictionary<string, string> TypeParameters { get; set; }

        // Vai para ApiParameter.Description; não sai duplicado no JSON.
        [JsonIgnore] public Dictionary<string, string> Parameters { get; set; }
    }
}
