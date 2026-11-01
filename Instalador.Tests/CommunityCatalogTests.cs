using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PbsuLoader.Testes
{
    public class CommunityCatalogTests
    {
        const string Base = @"{
            ""formato"": 1,
            ""mods"": [
                { ""id"": ""PBSLoaderCommonUtils"", ""nome"": ""Base"", ""repo"": ""a/b"", ""tag"": ""x-v"", ""arquivo"": ""PBSLoaderCommonUtils.dll"", ""obrigatorio"": true },
                { ""id"": ""SemRepo"", ""arquivo"": ""SemRepo.dll"" },
                { ""id"": ""Exe"", ""repo"": ""a/b"", ""arquivo"": ""Exe.exe"" }
            ]
        }";

        static string Community(params string[] mods) => "{ \"formato\": 1, \"mods\": [" + string.Join(",", mods) + "] }";

        static string Plugin(string id, string extra = "")
            => "{ \"id\": \"" + id + "\", \"nome\": \"" + id + "\", \"tipo\": \"plugin\", \"origem\": \"github\", \"repo\": \"autor/" + id +
               "\", \"tag\": \"v\", \"arquivo\": \"" + id + ".dll\", \"dependencias\": [\"PBSLoaderCommonUtils\"]" + extra + " }";

        [Fact]
        public void Ler_IgnoraModsQueNaoDaParaInstalar()
        {
            var c = Catalogo.Ler(Base);
            Assert.Equal(new[] { "PBSLoaderCommonUtils" }, c.Mods.Select(m => m.Id));
            Assert.False(c.Mods[0].Community);
        }

        [Fact]
        public void MergeCommunity_AcrescentaPluginComoComunidade()
        {
            var c = Catalogo.Ler(Base);
            int added = c.MergeCommunity(Community(Plugin("Bom")));

            Assert.Equal(1, added);
            var mod = c.Mod("Bom");
            Assert.True(mod.Community);
            Assert.False(mod.Trusted);
            Assert.Equal("comunidade", mod.Estado);
            Assert.Equal("autor/Bom", mod.Origem.Repo);
            Assert.Equal(new[] { "PBSLoaderCommonUtils" }, mod.Dependencias);
        }

        [Fact]
        public void MergeCommunity_AutorConfiavel()
        {
            var c = Catalogo.Ler(Base);
            c.MergeCommunity(Community(Plugin("Bom", ", \"confiavel\": true")));
            Assert.True(c.Mod("Bom").Trusted);
            Assert.Equal("comunidade, confiável", c.Mod("Bom").Estado);
        }

        [Fact]
        public void MergeCommunity_NuncaObrigatorioNemPadrao()
        {
            var c = Catalogo.Ler(Base);
            c.MergeCommunity(Community(Plugin("Forcado", ", \"obrigatorio\": true, \"padrao\": true")));
            Assert.False(c.Mod("Forcado").Obrigatorio);
            Assert.False(c.Mod("Forcado").Padrao);
        }

        [Fact]
        public void MergeCommunity_ModBaseVenceIdRepetido()
        {
            var c = Catalogo.Ler(Base);
            int added = c.MergeCommunity(Community(Plugin("pbsloadercommonutils"), Plugin("Dup"), Plugin("DUP")));

            Assert.Equal(1, added);
            Assert.False(c.Mod("PBSLoaderCommonUtils").Community);
            Assert.Equal("a/b", c.Mod("PBSLoaderCommonUtils").Origem.Repo);
            Assert.Equal(1, c.Mods.Count(m => string.Equals(m.Id, "dup", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public void MergeCommunity_AceitaZipDeOutrosTiposEExternaHttps()
        {
            var c = Catalogo.Ler(Base);
            int added = c.MergeCommunity(Community(
                "{ \"id\": \"Onibus\", \"tipo\": \"onibus\", \"repo\": \"a/b\", \"tag\": \"v\", \"arquivo\": \"Onibus.zip\" }",
                "{ \"id\": \"Mapa\", \"tipo\": \"mapa\", \"origem\": \"externa\", \"url\": \"https://cdn.exemplo.com/mods/MapaLinha2.zip\" }",
                "{ \"id\": \"PluginZip\", \"tipo\": \"plugin\", \"repo\": \"a/b\", \"tag\": \"v\", \"arquivo\": \"PluginZip.zip\" }",
                "{ \"id\": \"ExternoHttp\", \"tipo\": \"mapa\", \"origem\": \"externa\", \"url\": \"http://cdn.exemplo.com/Inseguro.zip\" }",
                "{ \"id\": \"Pasta\", \"tipo\": \"plugin\", \"repo\": \"a/b\", \"tag\": \"v\", \"arquivo\": \"..\\\\Pasta.dll\" }",
                "{ \"id\": \"SemTipo\", \"repo\": \"a/b\", \"tag\": \"v\", \"arquivo\": \"SemTipo.dll\" }",
                "{ \"id\": \"ExternoSemNome\", \"tipo\": \"mapa\", \"origem\": \"externa\", \"url\": \"https://cdn.exemplo.com/pasta/\" }"));

            Assert.Equal(2, added);
            var mapa = c.Mod("Mapa");
            Assert.True(mapa.Origem.Externa);
            Assert.Equal("https://cdn.exemplo.com/mods/MapaLinha2.zip", mapa.Origem.Url);
            Assert.Equal("MapaLinha2.zip", mapa.Origem.Arquivo);
            Assert.Equal("comunidade", c.Mod("Onibus").Estado);
            Assert.Null(c.Mod("PluginZip"));       // plugin entra só como .dll
            Assert.Null(c.Mod("ExternoHttp"));     // externa só https
            Assert.Null(c.Mod("Pasta"));
            Assert.Null(c.Mod("SemTipo"));         // sem tipo declarado, não entra da comunidade
            Assert.Null(c.Mod("ExternoSemNome"));  // URL sem nome de arquivo
        }

        [Fact]
        public void MergeCommunity_JsonQuebradoLancaEDeixaBaseIntacta()
        {
            var c = Catalogo.Ler(Base);
            Assert.ThrowsAny<Exception>(() => c.MergeCommunity("{ \"mods\": ["));
            Assert.Single(c.Mods);
        }

        [Fact]
        public void MergeCommunity_ArquivoJaUsadoFicaComQuemChegouAntes()
        {
            var c = Catalogo.Ler(Base);
            int added = c.MergeCommunity(Community(
                "{ \"id\": \"Impostor\", \"tipo\": \"plugin\", \"repo\": \"mal/x\", \"tag\": \"v\", \"arquivo\": \"pbsloadercommonutils.dll\" }",
                Plugin("Primeiro"),
                "{ \"id\": \"Segundo\", \"tipo\": \"plugin\", \"repo\": \"x/y\", \"tag\": \"v\", \"arquivo\": \"PRIMEIRO.dll\" }"));

            Assert.Equal(1, added);
            Assert.Null(c.Mod("Impostor"));
            Assert.Null(c.Mod("Segundo"));
            Assert.Equal("a/b", c.Mods.Single(m => m.Origem.Arquivo == "PBSLoaderCommonUtils.dll").Origem.Repo);
        }

        [Fact]
        public void MergeCommunity_TiraQuemFicouSemDependenciaEmCadeia()
        {
            var c = Catalogo.Ler(Base);
            int added = c.MergeCommunity(Community(
                Plugin("Livre"),
                "{ \"id\": \"Meio\", \"tipo\": \"plugin\", \"repo\": \"x/m\", \"tag\": \"v\", \"arquivo\": \"Meio.dll\", \"dependencias\": [\"Fantasma\"] }",
                "{ \"id\": \"Topo\", \"tipo\": \"plugin\", \"repo\": \"x/t\", \"tag\": \"v\", \"arquivo\": \"Topo.dll\", \"dependencias\": [\"meio\"] }"));

            Assert.Equal(1, added);
            Assert.NotNull(c.Mod("Livre"));
            Assert.Null(c.Mod("Meio"));
            Assert.Null(c.Mod("Topo"));
        }

        [Fact]
        public void Instalar_ModDaComunidadeSemSha256NaoBaixa()
        {
            var e = new EstadoMod
            {
                Mod = new ModDoCatalogo { Id = "X", Nome = "X", Community = true, Origem = new Origem { Repo = "a/b", Arquivo = "X.dll" } },
                Remoto = new Lancamento { Versao = "1.0", Url = "https://exemplo.invalid/X.dll", Sha256 = null },
            };
            var ex = Assert.Throws<InvalidOperationException>(() => Instalacao.Instalar(e, Path.GetTempPath(), null, _ => { }));
            Assert.Contains("sem sha256", ex.Message);
        }

        [Fact]
        public void Instalar_ExternaNaoExigeSha_GithubComunidadeSemShaContinuaRecusado()
        {
            var github = new EstadoMod
            {
                Mod = new ModDoCatalogo { Id = "G", Nome = "G", Community = true, Origem = new Origem { Repo = "a/b", Arquivo = "G.dll" } },
                Remoto = new Lancamento { Versao = "1.0", Url = "https://exemplo.invalid/G.dll", Sha256 = null },
            };
            Assert.True(Instalacao.ExigeShaDoRelease(github.Mod, github.Remoto));
            Assert.Contains("sem sha256", Assert.Throws<InvalidOperationException>(() => Instalacao.Instalar(github, Path.GetTempPath(), null, _ => { })).Message);

            // Externa: não há como conferir de antemão — o hash gravado no registro vira a referência.
            var externa = new EstadoMod
            {
                Mod = new ModDoCatalogo { Id = "X", Nome = "X", Community = true, Origem = new Origem { Url = "https://cdn.exemplo.com/X.zip", Arquivo = "X.zip" } },
                Remoto = new Lancamento { Versao = "externa", Url = "https://cdn.exemplo.com/X.zip" },
            };
            Assert.False(Instalacao.ExigeShaDoRelease(externa.Mod, externa.Remoto));
        }

        [Fact]
        public void Avaliar_ExternaSemRegistroOfereceODownload()
        {
            var mod = new ModDoCatalogo { Id = "Mapa", Nome = "Mapa", Community = true, Origem = new Origem { Url = "https://cdn.exemplo.com/Mapa.zip", Arquivo = "Mapa.zip" } };
            var e = Instalacao.Avaliar(mod, Path.GetTempPath(), Registro.Abrir(Path.GetTempPath()), false);
            Assert.Equal(Situacao.NaoInstalado, e.Situacao);
            Assert.Equal("https://cdn.exemplo.com/Mapa.zip", e.Remoto.Url);
        }

        static EstadoMod State(string name, bool community, bool trusted = false, bool installed = false) => new EstadoMod
        {
            Mod = new ModDoCatalogo { Id = name, Nome = name, Community = community, Trusted = trusted },
            Instalado = installed ? new Registro.Item() : null,
        };

        [Fact]
        public void UnreviewedWarning_SoParaComunidadeSemConfiancaQueAindaNaoEstaInstalado()
        {
            Assert.Null(Instalacao.UnreviewedWarning(new[] { State("Base", false), State("Confiavel", true, trusted: true), State("JaTem", true, installed: true) }));

            string one = Instalacao.UnreviewedWarning(new[] { State("Novo", true), State("Base", false) });
            Assert.StartsWith("Atenção: Novo é um mod da comunidade", one);

            string two = Instalacao.UnreviewedWarning(new[] { State("A", true), State("B", true) });
            Assert.StartsWith("Atenção: A, B são mods da comunidade", two);
        }

        [Fact]
        public void Ler_ScriptsConfiaveisSoComSha256Valido()
        {
            var c = Catalogo.Ler("{ \"scriptsConfiaveis\": [ { \"nome\": \"Painel\", \"sha256\": \" " + new string('A', 64) + " \" }, { \"sha256\": \"curto\" }, { \"sha256\": \"" + new string('z', 64) + "\" } ] }");
            var s = Assert.Single(c.ScriptsConfiaveis);
            Assert.Equal(new string('a', 64), s.Sha256);
            Assert.Equal("Painel", s.Nome);
            Assert.Equal("", s.Autor);
        }

        [Fact]
        public void Carregar_SemCatalogoBaseUsaACopiaEmbutida()
        {
            var log = new StringBuilder();
            try
            {
                Environment.SetEnvironmentVariable("PBSU_CATALOGO", Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N") + ".json"));
                Environment.SetEnvironmentVariable("PBSU_CATALOGO_COMUNIDADE", Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N") + ".json"));
                var c = Catalogo.Carregar(s => log.AppendLine(s));
                Assert.Equal("cópia embutida", c.Fonte);
                Assert.NotNull(c.Mod("PBSLoaderCommonUtils"));
                Assert.Contains("Usando o que veio com o instalador", log.ToString());

                Environment.SetEnvironmentVariable("PBSU_CATALOGO_COMUNIDADE", "https://exemplo.invalid/c.json");
                log.Clear();
                Catalogo.Carregar(s => log.AppendLine(s));
                Assert.Contains("Sem rede: seguindo sem o catálogo da comunidade.", log.ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable("PBSU_CATALOGO", null);
                Environment.SetEnvironmentVariable("PBSU_CATALOGO_COMUNIDADE", null);
            }
        }

        [Fact]
        public void Carregar_LeAsDuasFontesPelasVariaveisDeAmbiente()
        {
            string pasta = Path.Combine(Path.GetTempPath(), "pbsu-catalogo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);
            string baseArq = Path.Combine(pasta, "base.json"), comArq = Path.Combine(pasta, "comunidade.json");
            File.WriteAllText(baseArq, Base, new UTF8Encoding(false));
            File.WriteAllText(comArq, Community(Plugin("Bom")), new UTF8Encoding(false));
            var log = new StringBuilder();
            try
            {
                Environment.SetEnvironmentVariable("PBSU_CATALOGO", baseArq);
                Environment.SetEnvironmentVariable("PBSU_CATALOGO_COMUNIDADE", comArq);
                var c = Catalogo.Carregar(s => log.AppendLine(s));
                Assert.Equal(baseArq, c.Fonte);
                Assert.True(c.Mod("Bom").Community);
                Assert.Contains("1 mod(s)", log.ToString());

                Environment.SetEnvironmentVariable("PBSU_CATALOGO_COMUNIDADE", Path.Combine(pasta, "nao-existe.json"));
                c = Catalogo.Carregar(s => log.AppendLine(s));
                Assert.Null(c.Mod("Bom"));
                Assert.Contains("Seguindo só com o catálogo base", log.ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable("PBSU_CATALOGO", null);
                Environment.SetEnvironmentVariable("PBSU_CATALOGO_COMUNIDADE", null);
                Directory.Delete(pasta, true);
            }
        }
    }
}
