using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace PbsuLoader.Testes
{
    sealed class PastaDeTeste : IDisposable
    {
        public readonly string Raiz, Jogo, ArquivoFora;

        public PastaDeTeste()
        {
            Raiz = Path.Combine(Path.GetTempPath(), "pbsu-testes-" + Guid.NewGuid().ToString("N"));
            Jogo = Path.Combine(Raiz, "jogo");
            Directory.CreateDirectory(Path.Combine(Jogo, "Mods"));
            ArquivoFora = Path.Combine(Raiz, "fora.txt");
            File.WriteAllText(ArquivoFora, "não apagar");
        }

        public string NoJogo(string relativo)
        {
            var c = Path.Combine(Jogo, relativo.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(c));
            File.WriteAllText(c, relativo);
            return c;
        }

        public void Registro(string json)
        {
            var arq = Path.Combine(Jogo, "UserData", "pbsu-loader", "instalado.json");
            Directory.CreateDirectory(Path.GetDirectoryName(arq));
            File.WriteAllText(arq, json, new UTF8Encoding(false));
        }

        public string Zip(params string[] entradas)
        {
            var arq = Path.Combine(Raiz, Guid.NewGuid().ToString("N") + ".zip");
            using (var z = ZipFile.Open(arq, ZipArchiveMode.Create))
                foreach (var e in entradas)
                {
                    var entrada = z.CreateEntry(e);
                    if (!e.EndsWith("/"))
                        using (var w = new StreamWriter(entrada.Open())) w.Write(e);
                }
            return arq;
        }

        public void Dispose()
        {
            try { Directory.Delete(Raiz, true); } catch (IOException) { }
        }
    }

    public class TentarAbsolutoTests
    {
        const string Jogo = @"X:\jogo";

        [Theory]
        [InlineData("Mods/A.dll", @"X:\jogo\Mods\A.dll")]
        [InlineData(@"UserLibs\B.dll", @"X:\jogo\UserLibs\B.dll")]
        [InlineData("Mods/../UserData/c.cfg", @"X:\jogo\UserData\c.cfg")]
        [InlineData("MelonLoader/", @"X:\jogo\MelonLoader\")]
        public void AceitaCaminhoDentroDaPasta(string relativo, string esperado)
        {
            Assert.True(Instalacao.TentarAbsoluto(Jogo, relativo, out var c));
            Assert.Equal(esperado, c);
        }

        [Theory]
        [InlineData(@"UserLibs\lib.dll")]
        [InlineData("UserLibs/../UserLibs/lib.dll")]
        [InlineData("./UserLibs/lib.dll")]
        public void ChaveTemUmaGrafiaSo(string relativo)
        {
            Assert.True(Instalacao.TentarAbsoluto(Jogo, relativo, out _, out var chave));
            Assert.Equal("UserLibs/lib.dll", chave);
        }

        [Theory]
        [InlineData("../fora.txt")]
        [InlineData(@"Mods\..\..\fora.txt")]
        [InlineData(@"..\jogo2\a.dll")]
        [InlineData(@"C:\Windows\System32\x.dll")]
        [InlineData("C:/Users/x/Documents/a.txt")]
        [InlineData("C:x.dll")]
        [InlineData(@"\raiz.txt")]
        [InlineData(@"\\servidor\pasta\a.dll")]
        [InlineData(".")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("a\0b")]
        public void RecusaCaminhoQueSaiDaPasta(string relativo)
        {
            Assert.False(Instalacao.TentarAbsoluto(Jogo, relativo, out var c));
            Assert.Null(c);
        }
    }

    public class RegistroTests
    {
        const string Adulterado = @"{
  ""formato"": 1,
  ""mods"": {
    ""Mau"": { ""versao"": ""1.0"", ""sha256"": ""aa"", ""arquivos"": {
      ""Mods/Mau.dll"": null,
      ""../fora.txt"": null,
      ""C:/Windows/win.ini"": null
    } }
  }
}";

        [Fact]
        public void AbrirDescartaCaminhosQueSaemDaPasta()
        {
            using (var t = new PastaDeTeste())
            {
                t.Registro(Adulterado);
                var reg = Registro.Abrir(t.Jogo);
                Assert.Equal(new[] { "Mods/Mau.dll" }, reg.Mods["Mau"].Arquivos.Keys);
                Assert.Equal(new[] { "../fora.txt", "C:/Windows/win.ini" }, reg.Ignorados);

                var log = new List<string>();
                reg.AvisarIgnorados(log.Add);
                reg.AvisarIgnorados(log.Add);
                Assert.Equal(2, log.Count);
                Assert.Contains("../fora.txt", log[0]);

                reg.Salvar();
                Assert.Empty(Registro.Abrir(t.Jogo).Ignorados);
            }
        }

        [Fact]
        public void AbrirSemArquivoDevolveRegistroVazio()
        {
            using (var t = new PastaDeTeste())
            {
                var reg = Registro.Abrir(t.Jogo);
                Assert.Empty(reg.Mods);
                Assert.Empty(reg.Ignorados);
            }
        }

        [Fact]
        public void RemoverComRegistroAdulteradoNaoApagaForaDaPasta()
        {
            using (var t = new PastaDeTeste())
            {
                var dll = t.NoJogo("Mods/Mau.dll");
                t.Registro(Adulterado);
                var reg = Registro.Abrir(t.Jogo);
                var mod = new ModDoCatalogo { Id = "Mau", Nome = "Mau", Origem = new Origem { Arquivo = "Mau.dll" } };
                var log = new List<string>();

                Instalacao.Remover(new EstadoMod { Mod = mod, Instalado = reg.Mods["Mau"] }, t.Jogo, reg, log.Add);

                Assert.False(File.Exists(dll));
                Assert.True(File.Exists(t.ArquivoFora));
                Assert.False(reg.Mods.ContainsKey("Mau"));
            }
        }

        [Fact]
        public void RemoverNaoApagaArquivoDeOutroModEscritoDeOutroJeito()
        {
            using (var t = new PastaDeTeste())
            {
                var lib = t.NoJogo("UserLibs/lib.dll");
                t.Registro(@"{ ""mods"": {
                    ""A"": { ""arquivos"": { ""UserLibs\\lib.dll"": null } },
                    ""B"": { ""arquivos"": { ""UserLibs/../UserLibs/lib.dll"": null } } } }");
                var reg = Registro.Abrir(t.Jogo);
                var a = new ModDoCatalogo { Id = "A", Nome = "A", Origem = new Origem { Arquivo = "A.zip" } };

                Instalacao.Remover(new EstadoMod { Mod = a, Instalado = reg.Mods["A"] }, t.Jogo, reg, _ => { });

                Assert.True(File.Exists(lib));
                Assert.Equal(new[] { "UserLibs/lib.dll" }, reg.Mods["B"].Arquivos.Keys);
            }
        }

        [Fact]
        public void RemoverRecusaNomeDeArquivoQueSaiDaPasta()
        {
            using (var t = new PastaDeTeste())
            {
                var reg = Registro.Abrir(t.Jogo);
                var mod = new ModDoCatalogo { Id = "Mau", Nome = "Mau", Origem = new Origem { Arquivo = @"..\..\fora.txt.dll" } };

                Assert.Throws<InvalidDataException>(() => Instalacao.Remover(new EstadoMod { Mod = mod }, t.Jogo, reg, _ => { }));
                Assert.True(File.Exists(t.ArquivoFora));
            }
        }
    }

    public class ZipTests
    {
        static EstadoMod ModZip(string zip) => new EstadoMod
        {
            Mod = new ModDoCatalogo { Id = "Zip", Nome = "Zip", Origem = new Origem { Arquivo = "Zip.zip" } },
            Remoto = new Lancamento { Versao = "1.0", Url = new Uri(zip).AbsoluteUri },
        };

        [Fact]
        public void ZipDeModComEntradaParaForaFalhaSemEscreverFora()
        {
            using (var t = new PastaDeTeste())
            {
                var zip = t.Zip("Mods/Zip.dll", "../escapou.txt");
                var reg = Registro.Abrir(t.Jogo);

                Assert.Throws<InvalidDataException>(() => Instalacao.Instalar(ModZip(zip), t.Jogo, reg, _ => { }));
                Assert.False(File.Exists(Path.Combine(t.Raiz, "escapou.txt")));
                Assert.False(reg.Mods.ContainsKey("Zip"));
            }
        }

        [Fact]
        public void ZipDeModRegistraCaminhosRelativos()
        {
            using (var t = new PastaDeTeste())
            {
                var zip = t.Zip("Mods/", "Mods/Zip.dll", "UserLibs/Lib.dll");
                var reg = Registro.Abrir(t.Jogo);

                Instalacao.Instalar(ModZip(zip), t.Jogo, reg, _ => { });

                Assert.Equal(new[] { "Mods/Zip.dll", "UserLibs/Lib.dll" }, reg.Mods["Zip"].Arquivos.Keys);
                Assert.True(File.Exists(Path.Combine(t.Jogo, "UserLibs", "Lib.dll")));
            }
        }

        [Fact]
        public void AtualizacaoApagaSoArquivosVelhosDoProprioMod()
        {
            using (var t = new PastaDeTeste())
            {
                var velha = t.NoJogo("UserLibs/Velha.dll");
                var compartilhada = t.NoJogo("UserLibs/Comum.dll");
                var reg = Registro.Abrir(t.Jogo);
                reg.Mods["Outro"] = new Registro.Item { Arquivos = { ["UserLibs/Comum.dll"] = null } };
                var e = ModZip(t.Zip("Mods/Zip.dll"));
                e.Instalado = new Registro.Item { Arquivos = { ["UserLibs/Velha.dll"] = null, ["UserLibs/Comum.dll"] = null } };

                Instalacao.Instalar(e, t.Jogo, reg, _ => { });

                Assert.False(File.Exists(velha));
                Assert.True(File.Exists(compartilhada));
                Assert.Equal(new[] { "Mods/Zip.dll" }, reg.Mods["Zip"].Arquivos.Keys);
            }
        }

        [Fact]
        public void DllVaiParaMods()
        {
            using (var t = new PastaDeTeste())
            {
                var origem = Path.Combine(t.Raiz, "Dll.dll");
                File.WriteAllText(origem, "dll");
                var e = new EstadoMod
                {
                    Mod = new ModDoCatalogo { Id = "Dll", Nome = "Dll", Origem = new Origem { Arquivo = "Dll.dll" } },
                    Remoto = new Lancamento { Versao = "2.0", Url = new Uri(origem).AbsoluteUri, Sha256 = Rede.Sha256(origem) },
                };
                var reg = Registro.Abrir(t.Jogo);

                Instalacao.Instalar(e, t.Jogo, reg, _ => { });

                Assert.Equal("dll", File.ReadAllText(Path.Combine(t.Jogo, "Mods", "Dll.dll")));
                Assert.Equal("2.0", reg.Mods["Dll"].Versao);
            }
        }

        [Fact]
        public void BuildLocalECopiado()
        {
            using (var t = new PastaDeTeste())
            {
                var build = Path.Combine(t.Raiz, "Local.dll");
                File.WriteAllText(build, "local");
                var e = new EstadoMod
                {
                    Mod = new ModDoCatalogo { Id = "Local", Nome = "Local", Origem = new Origem { Arquivo = "Local.dll" } },
                    Local = build,
                    LocalSha = Rede.Sha256(build),
                };
                var reg = Registro.Abrir(t.Jogo);

                Instalacao.Instalar(e, t.Jogo, reg, _ => { });

                Assert.Equal("local", File.ReadAllText(Path.Combine(t.Jogo, "Mods", "Local.dll")));
                Assert.Equal("local", reg.Mods["Local"].Versao);
            }
        }

        [Fact]
        public void MelonLoaderDeOutraVersaoESubstituido()
        {
            using (var t = new PastaDeTeste())
            {
                t.NoJogo("version.dll");
                t.NoJogo("MelonLoader/net6/MelonLoader.dll");
                var sobra = t.NoJogo("MelonLoader/sobra.txt");
                var c = new Catalogo { MelonVersao = "0.7.3", MelonUrl = new Uri(t.Zip("MelonLoader/net6/MelonLoader.dll", "version.dll")).AbsoluteUri };

                Instalacao.InstalarMelon(c, t.Jogo, _ => { });

                Assert.False(File.Exists(sobra));
                Assert.True(File.Exists(Path.Combine(t.Jogo, "version.dll")));
            }
        }

        [Fact]
        public void ZipDoMelonLoaderPulaEntradaParaFora()
        {
            using (var t = new PastaDeTeste())
            {
                var zip = t.Zip("MelonLoader/", "MelonLoader/net6/MelonLoader.dll", "version.dll", "../escapou.txt");
                var c = new Catalogo { MelonVersao = "0.7.3", MelonUrl = new Uri(zip).AbsoluteUri };

                Instalacao.InstalarMelon(c, t.Jogo, _ => { });

                Assert.True(File.Exists(Path.Combine(t.Jogo, "MelonLoader", "net6", "MelonLoader.dll")));
                Assert.True(File.Exists(Path.Combine(t.Jogo, "version.dll")));
                Assert.False(File.Exists(Path.Combine(t.Raiz, "escapou.txt")));
            }
        }
    }

    public class CatalogoTests
    {
        static string ComArquivo(string arquivo) =>
            "{\"mods\":[{\"id\":\"M\",\"repo\":\"a/b\",\"arquivo\":" + Json.Escrever(arquivo) + "}]}";

        [Theory]
        [InlineData("Mod.dll")]
        [InlineData("Pacote-1.zip")]
        public void AceitaNomeDeArquivo(string arquivo) => Assert.Single(Catalogo.Ler(ComArquivo(arquivo)).Mods);

        [Theory]
        [InlineData(@"..\..\Windows\x.dll")]
        [InlineData("sub/Mod.dll")]
        [InlineData(@"C:\Mod.dll")]
        [InlineData("..dll")]
        [InlineData(".Mod.dll")]
        public void RecusaArquivoComPasta(string arquivo) => Assert.Empty(Catalogo.Ler(ComArquivo(arquivo)).Mods);
    }

    [Collection("PastaDeConfianca")]
    public class ConfiaveisTests : IDisposable
    {
        readonly string _anterior = Instalacao.PastaDeConfianca;
        readonly PastaDeTeste _t = new PastaDeTeste();

        public void Dispose()
        {
            Instalacao.PastaDeConfianca = _anterior;
            _t.Dispose();
        }

        static Catalogo ComUmScript() => new Catalogo
        {
            ScriptsConfiaveis = { new ScriptConfiavel { Nome = "Painel", Autor = "fulano", Onde = "Torino", Sha256 = new string('a', 64) } },
        };

        [Fact]
        public void GravaForaDaPastaDoJogoEApagaACopiaAntiga()
        {
            Instalacao.PastaDeConfianca = Path.Combine(_t.Raiz, "perfil");
            var antigo = _t.NoJogo("UserData/PBSLoaderCommonUtils/confiaveis.txt");
            var log = new List<string>();

            Instalacao.GravarConfiaveis(ComUmScript(), _t.Jogo, log.Add);

            var texto = File.ReadAllText(Path.Combine(Instalacao.PastaDeConfianca, "confiaveis.txt"));
            Assert.Contains(new string('a', 64) + "\n", texto);
            Assert.Contains("# Painel — fulano (Torino)", texto);
            Assert.False(File.Exists(antigo));
            Assert.Single(log);

            Instalacao.GravarConfiaveis(ComUmScript(), _t.Jogo, log.Add);
            Assert.Single(log);
        }

        [Fact]
        public void SemPastaDoUsuarioSoAvisa()
        {
            Instalacao.PastaDeConfianca = null;
            var log = new List<string>();

            Instalacao.GravarConfiaveis(ComUmScript(), _t.Jogo, log.Add);

            Assert.Contains("vão perguntar", Assert.Single(log));
        }

        [Fact]
        public void ErroDeEscritaViraLog()
        {
            Instalacao.PastaDeConfianca = _t.ArquivoFora;
            var log = new List<string>();

            Instalacao.GravarConfiaveis(ComUmScript(), _t.Jogo, log.Add);

            Assert.StartsWith("Não consegui gravar", Assert.Single(log));
        }
    }

    public class AceitaPreLancamentoTests
    {
        [Fact]
        public void Obrigatorio_AceitaPreLancamento()
        {
            var mod = new ModDoCatalogo { Id = "X", Obrigatorio = true };
            Assert.True(Instalacao.AceitaPreLancamento(false, mod));
        }

        [Fact]
        public void Opcional_SemBeta_NaoAceita()
        {
            var mod = new ModDoCatalogo { Id = "Y", Obrigatorio = false };
            Assert.False(Instalacao.AceitaPreLancamento(false, mod));
        }

        [Fact]
        public void Opcional_ComBeta_Aceita()
        {
            var mod = new ModDoCatalogo { Id = "Z", Obrigatorio = false };
            Assert.True(Instalacao.AceitaPreLancamento(true, mod));
        }
    }
}
