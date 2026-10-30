using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiExtractor
{
    internal static class Extraction
    {
        public const int StrictFailure = 3;

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            // Acentos legíveis no arquivo; ele não vai para HTML sem passar pelo gerador do site.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static (ApiDocument Document, List<string> Warnings) Extract(Options options)
        {
            string dll = Path.GetFullPath(options.DllPath);
            string xml = options.XmlPath ?? Path.ChangeExtension(dll, ".xml");
            var documentation = XmlDocumentation.Load(xml);

            using var context = new MetadataLoadContext(new PathAssemblyResolver(ReferencePaths(dll, options.ReferenceDirectories)));
            var assembly = context.LoadFromAssemblyPath(dll);
            var builder = new ApiBuilder(documentation, options.ExcludedBaseTypes);
            var document = builder.Build(assembly, options.Version ?? assembly.GetName().Version?.ToString(3));

            var warnings = new List<string>();
            if (!documentation.Found) warnings.Add("documentation file not found or empty: " + xml);
            warnings.AddRange(builder.Warnings);
            return (document, warnings);
        }

        public static int Run(Options options, TextWriter output, TextWriter error)
        {
            var (document, warnings) = Extract(options);
            string path = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(document, Json) + "\n");

            foreach (string warning in warnings) error.WriteLine("warning: " + warning);
            var members = document.Types.SelectMany(t => t.Members).ToList();
            int documented = members.Count(m => m.Docs?.Summary != null);
            output.WriteLine($"{document.Assembly} {document.Version}: {document.Types.Count} types, {members.Count} members ({documented} documented) -> {path}");
            return options.Strict && warnings.Count > 0 ? StrictFailure : 0;
        }

        // A DLL alvo, as vizinhas dela, as pastas pedidas e o runtime do .NET (para System.Private.CoreLib).
        static IEnumerable<string> ReferencePaths(string dll, IEnumerable<string> directories)
        {
            var folders = new[] { Path.GetDirectoryName(dll), RuntimeEnvironment.GetRuntimeDirectory() }.Concat(directories);
            return folders.Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*.dll"))
                .Append(dll)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
