using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ApiExtractor
{
    /// <summary>O arquivo XML que o compilador gera com <c>GenerateDocumentationFile</c>, indexado pelo ID do membro.</summary>
    internal sealed class XmlDocumentation
    {
        readonly Dictionary<string, XElement> members;

        XmlDocumentation(Dictionary<string, XElement> members) => this.members = members;

        public static XmlDocumentation Empty { get; } = new XmlDocumentation(new Dictionary<string, XElement>());

        public bool Found => members.Count > 0;

        public static XmlDocumentation Load(string path)
        {
            if (path == null || !File.Exists(path)) return Empty;
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var member in document.Descendants("member"))
            {
                string name = (string)member.Attribute("name");
                if (name != null) members[name] = member;
            }
            return new XmlDocumentation(members);
        }

        public ApiDocs For(string id)
        {
            if (!members.TryGetValue(id, out var member)) return null;
            return new ApiDocs
            {
                Summary = DocMarkdown.Render(member.Element("summary")),
                Remarks = DocMarkdown.Render(member.Element("remarks")),
                Returns = DocMarkdown.Render(member.Element("returns")),
                Value = DocMarkdown.Render(member.Element("value")),
                Example = DocMarkdown.Render(member.Element("example")),
                Parameters = Named(member, "param"),
                TypeParameters = Named(member, "typeparam"),
            };
        }

        static Dictionary<string, string> Named(XElement member, string tag)
        {
            var entries = member.Elements(tag)
                .Select(element => (Name: (string)element.Attribute("name"), Text: DocMarkdown.Render(element)))
                .Where(entry => entry.Name != null && entry.Text != null)
                .ToList();
            return entries.Count == 0 ? null : entries.ToDictionary(entry => entry.Name, entry => entry.Text, StringComparer.Ordinal);
        }
    }
}
