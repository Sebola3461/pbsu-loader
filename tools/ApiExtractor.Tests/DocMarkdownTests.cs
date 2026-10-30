using System.Xml.Linq;
using Xunit;

namespace ApiExtractor.Tests
{
    public class DocMarkdownTests
    {
        static string Render(string xml) => DocMarkdown.Render(XElement.Parse(xml, LoadOptions.PreserveWhitespace));

        [Fact]
        public void NullAndEmptyGiveNull()
        {
            Assert.Null(DocMarkdown.Render(null));
            Assert.Null(Render("<summary>   </summary>"));
        }

        [Fact]
        public void CollapsesWhitespaceAndEscapesAngleBrackets()
            => Assert.Equal("Uma List&lt;T&gt; com espaço.", Render("<summary>\n   Uma List&lt;T&gt;\n   com   espaço.\n</summary>"));

        [Fact]
        public void InlineElements()
        {
            Assert.Equal("Use `Veiculo.Existe`, `null`, `nome` e `T`.",
                Render("<summary>Use <see cref=\"P:Ns.Veiculo.Existe\"/>, <see langword=\"null\"/>, <paramref name=\"nome\"/> e <typeparamref name=\"T\"/>.</summary>"));
            Assert.Equal("**forte** e *leve*", Render("<summary><b>forte</b> e <i>leve</i></summary>"));
            Assert.Equal("**forte** e *leve*", Render("<summary><strong>forte</strong> e <em>leve</em></summary>"));
            Assert.Equal("antes\ndepois", Render("<summary>antes<br/>depois</summary>"));
            Assert.Equal("texto", Render("<summary>texto<seealso cref=\"T:Ns.X\"/></summary>"));
            Assert.Equal("dentro", Render("<summary><desconhecido>dentro</desconhecido></summary>"));
            Assert.Equal("sem alvo", Render("<summary><see>sem alvo</see></summary>"));
        }

        [Fact]
        public void Links()
        {
            Assert.Equal("[site](https://x.dev)", Render("<summary><see href=\"https://x.dev\">site</see></summary>"));
            Assert.Equal("[https://x.dev](https://x.dev)", Render("<summary><see href=\"https://x.dev\"/></summary>"));
        }

        [Fact]
        public void CodeBlocksKeepIndentation()
        {
            string xml = "<example>Assim:\n<code>\n    if (a)\n        b();\n</code>\nFim.</example>";
            Assert.Equal("Assim:\n\n```csharp\nif (a)\n    b();\n```\n\nFim.", Render(xml));
        }

        [Fact]
        public void Lists()
        {
            Assert.Equal("Itens:\n\n- um\n- dois: segundo",
                Render("<remarks>Itens:<list type=\"bullet\"><item>um</item><item><term>dois</term><description>segundo</description></item></list></remarks>"));
            Assert.Equal("1. a\n2. b",
                Render("<remarks><list type=\"number\"><item><description>a</description></item><item>b</item></list></remarks>"));
        }

        [Theory]
        [InlineData("T:Ns.Sub.Tipo", "Tipo")]
        [InlineData("M:Ns.Tipo.Metodo(System.Int32)", "Tipo.Metodo")]
        [InlineData("M:Ns.Tipo.#ctor", "Tipo")]
        [InlineData("T:Ns.Gaveta`1", "Gaveta")]
        [InlineData("M:Ns.Tipo.Ler``1(System.String)", "Tipo.Ler")]
        [InlineData("Solto", "Solto")]
        public void CrefNames(string cref, string expected) => Assert.Equal(expected, DocMarkdown.CrefName(cref));

        [Fact]
        public void LoadsNothingWithoutFile()
        {
            Assert.False(XmlDocumentation.Load(null).Found);
            Assert.False(XmlDocumentation.Load("nao-existe.xml").Found);
            Assert.Null(XmlDocumentation.Empty.For("T:X"));
        }
    }
}
