using System;
using System.IO;
using Xunit;

namespace PbsuLoader.Testes
{
    public class JogoTests
    {
        [Fact]
        public void Procurar_UrlDoSiteNaoViraPastaExplicitaDoJogo()
        {
            var pasta = Path.Combine(Path.GetTempPath(), "jogo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);
            File.WriteAllText(Path.Combine(pasta, "Proton Bus Simulator.exe"), "");
            File.WriteAllText(Path.Combine(pasta, "GameAssembly.dll"), "");
            var anterior = Config.Ler("jogo");
            Config.Gravar("jogo", pasta);
            try
            {
                var c = Catalogo.Ler("{}");
                var esperado = Path.GetFullPath(pasta);
                // Antes do conserto, a URL do site entrava como "pasta explícita" e a detecção morria em null.
                Assert.Equal(esperado, Jogo.Procurar(c, new[] { "pbsloader://mod/UmMod" }));
            }
            finally
            {
                Config.Gravar("jogo", anterior ?? "");
                Directory.Delete(pasta, true);
            }
        }
    }
}
