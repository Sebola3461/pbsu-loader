using System.IO;
using System.Linq;
using Xunit;

namespace ApiExtractor.Tests
{
    public sealed class ExtractedFixtures
    {
        public ExtractedFixtures()
        {
            string dll = typeof(ExtractedFixtures).Assembly.Location;
            var options = Options.Parse(new[] { "--dll", dll, "--output", "unused.json", "--version", "9.9.9",
                "--exclude-base", "ApiExtractor.Tests.Fixtures.ExcludedBase" });
            (Document, Warnings) = Extraction.Extract(options);
        }

        public ApiDocument Document { get; }
        public System.Collections.Generic.List<string> Warnings { get; }

        public ApiType Type(string name) => Document.Types.Single(t => t.Name == name);
    }

    public class ExtractionTests : IClassFixture<ExtractedFixtures>
    {
        readonly ExtractedFixtures api;

        public ExtractionTests(ExtractedFixtures api) => this.api = api;

        ApiMember Member(string type, string name) => api.Type(type).Members.Single(m => m.Name == name);

        [Fact]
        public void ReadsAssemblyAndVersionWithoutWarnings()
        {
            Assert.Equal("ApiExtractor.Tests", api.Document.Assembly);
            Assert.Equal("9.9.9", api.Document.Version);
            Assert.Equal(1, api.Document.Schema);
            Assert.Empty(api.Warnings);
        }

        [Fact]
        public void LeavesOutNonPublicHiddenAndExcludedTypes()
        {
            var names = api.Document.Types.Select(t => t.Name).ToList();
            Assert.DoesNotContain("InternalType", names);
            Assert.DoesNotContain("HiddenType", names);
            Assert.DoesNotContain("ExcludedChild", names);
            Assert.Contains("ExcludedBase", names);
        }

        [Fact]
        public void StaticClassHasSignatureAndMarkdownDocs()
        {
            var type = api.Type("StaticSample");
            Assert.Equal("public static class StaticSample", type.Signature);
            Assert.Equal("class", type.Kind);
            Assert.Equal("T:ApiExtractor.Tests.Fixtures.StaticSample", type.Id);
            Assert.Equal("Uma classe estática com `código` e `Derived`.", type.Docs.Summary);
            Assert.Equal("Primeiro.\n\nSegundo.", type.Docs.Remarks);
            Assert.DoesNotContain(type.Members, m => m.Name == "Hidden");
            Assert.DoesNotContain(type.Members, m => m.Kind == "constructor");
        }

        [Fact]
        public void ConstantsPropertiesAndEvents()
        {
            var version = Member("StaticSample", "Version");
            Assert.Equal("public const string Version = \"1.0\"", version.Signature);
            Assert.Equal("constant", version.Kind);
            Assert.Equal("Versão.", version.Docs.Summary);

            Assert.Equal("public static float SpeedKmh { get; }", Member("StaticSample", "SpeedKmh").Signature);

            var changed = Member("StaticSample", "Changed");
            Assert.Equal("public static event Action<SampleKind> Changed", changed.Signature);
            Assert.Equal("E:ApiExtractor.Tests.Fixtures.StaticSample.Changed", changed.Id);
            Assert.True(changed.IsStatic);
            Assert.Equal("Mudou.", changed.Docs.Summary);
        }

        [Fact]
        public void MethodParametersReturnsAndDocs()
        {
            var add = Member("StaticSample", "Add");
            Assert.Equal("public static int Add(int a, int b = 2)", add.Signature);
            Assert.Equal("M:ApiExtractor.Tests.Fixtures.StaticSample.Add(System.Int32,System.Int32)", add.Id);
            Assert.Equal("Primeiro.", add.Parameters[0].Description);
            Assert.Null(add.Parameters[1].Description);
            Assert.Equal("int", add.Returns.Type);
            Assert.Equal("A soma.", add.Returns.Description);
            Assert.Null(Member("StaticSample", "Grid").Returns);
        }

        [Theory]
        [InlineData("TryRead", "public static bool TryRead(string key, out float value, ref int count, in int limit, params string[] extra)",
            "M:ApiExtractor.Tests.Fixtures.StaticSample.TryRead(System.String,System.Single@,System.Int32@,System.Int32@,System.String[])")]
        [InlineData("Read", "public static T Read<T>(string key, T fallback = default)",
            "M:ApiExtractor.Tests.Fixtures.StaticSample.Read``1(System.String,``0)")]
        [InlineData("Maybe", "public static int? Maybe(List<string> names, Dictionary<string, int[]> map, (int, string) pair)",
            "M:ApiExtractor.Tests.Fixtures.StaticSample.Maybe(System.Collections.Generic.List{System.String},System.Collections.Generic.Dictionary{System.String,System.Int32[]},System.ValueTuple{System.Int32,System.String})")]
        [InlineData("Grid", "public static void Grid(int[,] grid)", "M:ApiExtractor.Tests.Fixtures.StaticSample.Grid(System.Int32[0:,0:])")]
        [InlineData("Raw", "public static void Raw(int* pointer)", "M:ApiExtractor.Tests.Fixtures.StaticSample.Raw(System.Int32*)")]
        public void SignaturesAndIdsMatchTheCompiler(string name, string signature, string id)
        {
            var member = Member("StaticSample", name);
            Assert.Equal(signature, member.Signature);
            Assert.Equal(id, member.Id);
            Assert.NotNull(member.Docs?.Summary);
        }

        [Fact]
        public void TypeParameterDocs()
            => Assert.Equal("O tipo.", Member("StaticSample", "Read").Docs.TypeParameters["T"]);

        [Fact]
        public void ExtensionMethodsShowThis()
            => Assert.Equal("public static int Twice(this int value)", Member("Extensions", "Twice").Signature);

        [Fact]
        public void EnumsListValues()
        {
            var kind = api.Type("SampleKind");
            Assert.Equal("enum", kind.Kind);
            Assert.Equal("public enum SampleKind : byte", kind.Signature);
            Assert.Equal(new[] { "None", "First", "Second" }, kind.Members.Select(m => m.Name));
            Assert.Equal("4", kind.Members[2].Value);
            Assert.Equal("Second = 4", kind.Members[2].Signature);
            Assert.Equal("O primeiro.", kind.Members[1].Docs.Summary);
            Assert.Equal("public enum PlainKind", api.Type("PlainKind").Signature);
        }

        [Fact]
        public void AbstractBaseMembers()
        {
            var type = api.Type("ScriptBase");
            Assert.Equal("public abstract class ScriptBase", type.Signature);
            Assert.Equal("public virtual void Activate()", Member("ScriptBase", "Activate").Signature);
            Assert.Equal("public abstract void Frame()", Member("ScriptBase", "Frame").Signature);
            Assert.Equal("protected void Helper()", Member("ScriptBase", "Helper").Signature);
            Assert.Equal("protected void Helper2()", Member("ScriptBase", "Helper2").Signature);
            Assert.DoesNotContain(type.Members, m => m.Name == "Hidden");

            var indexer = Member("ScriptBase", "Item");
            Assert.Equal("public string this[int index] { get; }", indexer.Signature);
            Assert.Equal("P:ApiExtractor.Tests.Fixtures.ScriptBase.Item(System.Int32)", indexer.Id);
            Assert.Equal("Indexador.", indexer.Docs.Summary);
        }

        [Fact]
        public void SealedDerivedTypeWithOverridesAndConstructors()
        {
            var type = api.Type("Derived");
            Assert.Equal("public sealed class Derived : ScriptBase, IDisposable", type.Signature);
            Assert.Equal("ScriptBase", type.BaseType);
            Assert.Equal("public override void Frame()", Member("Derived", "Frame").Signature);
            Assert.Equal("public sealed override void Activate()", Member("Derived", "Activate").Signature);
            Assert.Equal("public void Dispose()", Member("Derived", "Dispose").Signature);
            Assert.DoesNotContain(type.Members, m => m.Name == "HiddenBecausePrivate");

            var constructors = type.Members.Where(m => m.Kind == "constructor").ToList();
            Assert.Equal(new[] { "public Derived()", "public Derived(int x)" }, constructors.Select(c => c.Signature));
            Assert.Equal("M:ApiExtractor.Tests.Fixtures.Derived.#ctor(System.Int32)", constructors[1].Id);
            Assert.Equal("Com número.", constructors[1].Docs.Summary);
        }

        [Fact]
        public void NestedTypesAndFields()
        {
            var nested = api.Type("ScriptBase.Nested");
            Assert.Equal("ApiExtractor.Tests.Fixtures.ScriptBase.Nested", nested.FullName);
            Assert.Equal("ApiExtractor.Tests.Fixtures.ScriptBase", nested.DeclaringType);
            Assert.Equal("public class Nested", nested.Signature);
            Assert.Equal("Aninhado.", nested.Docs.Summary);
            Assert.Equal("public int Value", Member("ScriptBase.Nested", "Value").Signature);
            Assert.Equal("public readonly float Fixed", Member("ScriptBase.Nested", "Fixed").Signature);
            Assert.Equal("public static readonly string Shared", Member("ScriptBase.Nested", "Shared").Signature);
            Assert.Equal("protected int Guarded", Member("ScriptBase.Nested", "Guarded").Signature);
        }

        [Fact]
        public void GenericTypes()
        {
            var generic = api.Type("Generic<T>");
            Assert.Equal("public class Generic<T>", generic.Signature);
            Assert.Equal("T:ApiExtractor.Tests.Fixtures.Generic`1", generic.Id);
            Assert.Equal("public T Item { get; set; }", Member("Generic<T>", "Item").Signature);

            var set = Member("Generic<T>", "Set");
            Assert.Equal("M:ApiExtractor.Tests.Fixtures.Generic`1.Set(`0,System.Collections.Generic.List{`0})", set.Id);
            Assert.Equal("Define.", set.Docs.Summary);

            var map = Member("Generic<T>", "Map");
            Assert.Equal("public U Map<U>(Func<T, U> map)", map.Signature);
            Assert.Equal("M:ApiExtractor.Tests.Fixtures.Generic`1.Map``1(System.Func{`0,``0})", map.Id);
            Assert.Equal("Mapeia.", map.Docs.Summary);
        }

        [Fact]
        public void DelegatesStructsAndInterfaces()
        {
            var handler = api.Type("SampleHandler");
            Assert.Equal("delegate", handler.Kind);
            Assert.Equal("public delegate bool SampleHandler(string name, int count)", handler.Signature);
            Assert.Empty(handler.Members);
            Assert.Null(handler.BaseType);

            var point = api.Type("Point");
            Assert.Equal("struct", point.Kind);
            Assert.Equal("public struct Point", point.Signature);
            Assert.DoesNotContain(point.Members, m => m.Name.StartsWith("op_"));

            var sample = api.Type("ISample");
            Assert.Equal("interface", sample.Kind);
            Assert.Equal("public interface ISample", sample.Signature);
            Assert.Equal("public string Name { get; }", Member("ISample", "Name").Signature);
        }

        [Fact]
        public void PropertyAccessors()
        {
            Assert.Equal("public int Id { get; init; }", Member("Record", "Id").Signature);
            Assert.Equal("public int Count { get; protected set; }", Member("Record", "Count").Signature);
            Assert.Equal("protected int Secret { get; set; }", Member("Record", "Secret").Signature);
            Assert.Equal("public int WriteOnly { set; }", Member("Record", "WriteOnly").Signature);
        }

        [Fact]
        public void HiddenMembersAreSkipped()
            => Assert.Equal(new[] { "WithHiddenMember", "Visible" }, api.Type("WithHiddenMember").Members.Select(m => m.Name));

        [Fact]
        public void ObsoleteMessages()
        {
            Assert.Equal("Use Derived.", api.Type("OldType").Obsolete);
            Assert.Equal("", Member("OldType", "Old").Obsolete);
            Assert.Null(api.Type("Derived").Obsolete);
        }

        [Fact]
        public void DefaultValueLiterals()
        {
            Assert.Equal(
                "public void Many(string s = \"a\\\"b\\\\\", bool flag = true, bool off = false, char c = 'x', double d = 1.5, " +
                "SampleKind kind = SampleKind.First, object o = null, Point p = default, float f = 0.25f, SampleKind other = (SampleKind)9, long big = 10)",
                Member("Defaults", "Many").Signature);
        }

        [Fact]
        public void DecimalAndNullableLiterals()
        {
            Assert.Equal("1.5m", Signatures.Literal(1.5m, typeof(decimal)));
            Assert.Equal("null", Signatures.Literal(null, typeof(int?)));
        }
    }

    public class RunTests
    {
        static string Dll => typeof(RunTests).Assembly.Location;

        [Fact]
        public void WritesJsonAndSummary()
        {
            string output = Path.Combine(Path.GetTempPath(), "apiextractor-tests", Path.GetRandomFileName(), "out.api.json");
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int code = Program.Run(new[] { "--dll", Dll, "--output", output, "--strict" }, stdout, stderr);

            Assert.Equal(0, code);
            Assert.Equal("", stderr.ToString());
            string json = File.ReadAllText(output);
            Assert.Contains("\"assembly\": \"ApiExtractor.Tests\"", json);
            Assert.Contains("Uma classe estática com `código`", json);
            Assert.DoesNotContain("\"parameters\": null", json);
            Assert.Contains("ApiExtractor.Tests", stdout.ToString());
        }

        [Fact]
        public void StrictFailsWhenDocumentationIsMissing()
        {
            string output = Path.Combine(Path.GetTempPath(), "apiextractor-tests", Path.GetRandomFileName(), "out.api.json");
            var stderr = new StringWriter();
            int code = Program.Run(new[] { "--dll", Dll, "--xml", "missing.xml", "--output", output, "--strict" }, new StringWriter(), stderr);

            Assert.Equal(Extraction.StrictFailure, code);
            Assert.Contains("documentation file not found", stderr.ToString());
        }

        [Fact]
        public void MissingDocumentationIsOnlyAWarningWithoutStrict()
        {
            string output = Path.Combine(Path.GetTempPath(), "apiextractor-tests", Path.GetRandomFileName(), "out.api.json");
            Assert.Equal(0, Program.Run(new[] { "--dll", Dll, "--xml", "missing.xml", "--output", output }, new StringWriter(), new StringWriter()));
        }

        [Fact]
        public void MissingDllIsAnError()
        {
            var stderr = new StringWriter();
            int code = Program.Run(new[] { "--dll", "does-not-exist.dll", "--output", "x.json" }, new StringWriter(), stderr);
            Assert.Equal(1, code);
            Assert.StartsWith("error:", stderr.ToString());
        }

        [Theory]
        [InlineData(new string[0], "missing --dll")]
        [InlineData(new[] { "--dll", "a.dll" }, "missing --output")]
        [InlineData(new[] { "--dll" }, "missing value for --dll")]
        [InlineData(new[] { "--nope" }, "unknown argument: --nope")]
        public void InvalidArgumentsPrintUsage(string[] args, string message)
        {
            var stderr = new StringWriter();
            Assert.Equal(2, Program.Run(args, new StringWriter(), stderr));
            Assert.Contains(message, stderr.ToString());
            Assert.Contains("usage:", stderr.ToString());
        }

        [Fact]
        public void ParsesAllOptions()
        {
            var options = Options.Parse(new[] { "--dll", "a.dll", "--xml", "a.xml", "--output", "o.json", "--version", "1.2.3",
                "--references", "x; y ;", "--references", "z", "--exclude-base", "Ns.Base", "--strict" });
            Assert.Equal("a.xml", options.XmlPath);
            Assert.Equal("1.2.3", options.Version);
            Assert.True(options.Strict);
            Assert.Equal(new[] { "x", "y", "z" }, options.ReferenceDirectories);
            Assert.Equal(new[] { "MelonLoader.MelonBase", "Ns.Base" }, options.ExcludedBaseTypes);
        }
    }
}
