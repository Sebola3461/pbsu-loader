using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ApiExtractor
{
    /// <summary>Converte um trecho da documentação XML (summary, remarks...) em Markdown.</summary>
    internal static class DocMarkdown
    {
        static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        static readonly Regex ParagraphBreak = new Regex(@"\n{2,}", RegexOptions.Compiled);

        public static string Render(XElement element)
        {
            if (element == null) return null;
            // Blocos de código saem do texto antes de normalizar os espaços, e voltam no fim intactos.
            var blocks = new List<string>();
            string text = Normalize(RenderNodes(element.Nodes(), blocks));
            for (int i = 0; i < blocks.Count; i++) text = text.Replace(Placeholder(i), blocks[i]);
            return text.Length == 0 ? null : text;
        }

        static string RenderNodes(IEnumerable<XNode> nodes, List<string> blocks)
        {
            var builder = new StringBuilder();
            foreach (var node in nodes)
            {
                if (node is XText text) builder.Append(Escape(Spaces.Replace(text.Value, " ")));
                else if (node is XElement child) builder.Append(RenderElement(child, blocks));
            }
            return builder.ToString();
        }

        static string RenderElement(XElement element, List<string> blocks)
        {
            switch (element.Name.LocalName)
            {
                case "c": return "`" + Spaces.Replace(element.Value, " ").Trim() + "`";
                case "code": return AddBlock(blocks, "```csharp\n" + Dedent(element.Value) + "\n```");
                case "para": return "\n\n" + RenderNodes(element.Nodes(), blocks) + "\n\n";
                case "see": return RenderSee(element, blocks);
                case "seealso": return "";
                case "paramref":
                case "typeparamref": return "`" + (string)element.Attribute("name") + "`";
                case "list": return RenderList(element, blocks);
                case "br": return "\n";
                case "b":
                case "strong": return "**" + RenderNodes(element.Nodes(), blocks).Trim() + "**";
                case "i":
                case "em": return "*" + RenderNodes(element.Nodes(), blocks).Trim() + "*";
                default: return RenderNodes(element.Nodes(), blocks);
            }
        }

        static string RenderSee(XElement element, List<string> blocks)
        {
            string cref = (string)element.Attribute("cref");
            if (cref != null) return "`" + CrefName(cref) + "`";
            string langword = (string)element.Attribute("langword");
            if (langword != null) return "`" + langword + "`";
            string href = (string)element.Attribute("href");
            if (href == null) return RenderNodes(element.Nodes(), blocks);
            string label = RenderNodes(element.Nodes(), blocks).Trim();
            return "[" + (label.Length > 0 ? label : href) + "](" + href + ")";
        }

        static string RenderList(XElement element, List<string> blocks)
        {
            bool numbered = (string)element.Attribute("type") == "number";
            var lines = element.Elements("item").Select((item, index) =>
            {
                string term = RenderNodes(item.Element("term")?.Nodes() ?? Enumerable.Empty<XNode>(), blocks).Trim();
                var description = item.Element("description");
                string body = RenderNodes(description != null ? description.Nodes() : item.Nodes(), blocks).Trim();
                if (item.Element("term") != null && description != null) body = term + ": " + body;
                return (numbered ? (index + 1) + ". " : "- ") + body;
            });
            return "\n\n" + string.Join("\n", lines) + "\n\n";
        }

        /// <summary><c>M:Ns.Tipo.Metodo(System.Int32)</c> vira <c>Tipo.Metodo</c>; <c>T:Ns.Tipo</c> vira <c>Tipo</c>.</summary>
        public static string CrefName(string cref)
        {
            bool isType = cref.StartsWith("T:", StringComparison.Ordinal);
            string name = cref.Length > 1 && cref[1] == ':' ? cref.Substring(2) : cref;
            int parenthesis = name.IndexOf('(');
            if (parenthesis >= 0) name = name.Substring(0, parenthesis);
            name = Regex.Replace(name, "`+\\d+", "");
            var parts = name.Split('.');
            if (parts.Length > 1 && parts[parts.Length - 1] == "#ctor") return parts[parts.Length - 2];
            if (isType || parts.Length == 1) return parts[parts.Length - 1];
            return parts[parts.Length - 2] + "." + parts[parts.Length - 1];
        }

        static string AddBlock(List<string> blocks, string block)
        {
            blocks.Add(block);
            return "\n\n" + Placeholder(blocks.Count - 1) + "\n\n";
        }

        static string Placeholder(int index) => "\u0001" + index + "\u0001";

        // Sem isto, "List<T>" num texto vira uma tag HTML no Markdown e some da página.
        static string Escape(string text) => text.Replace("<", "&lt;").Replace(">", "&gt;");

        static string Normalize(string text)
        {
            var paragraphs = ParagraphBreak.Split(text)
                .Select(paragraph => string.Join("\n", paragraph.Split('\n')
                    .Select(line => Regex.Replace(line, " {2,}", " ").Trim())
                    .Where(line => line.Length > 0)))
                .Where(paragraph => paragraph.Length > 0);
            return string.Join("\n\n", paragraphs);
        }

        static string Dedent(string code)
        {
            var lines = code.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            int indent = lines.Where(line => line.Trim().Length > 0)
                .Select(line => line.Length - line.TrimStart().Length)
                .DefaultIfEmpty(0).Min();
            return string.Join("\n", lines.Select(line => line.Length >= indent ? line.Substring(indent) : line.TrimStart()));
        }
    }
}
