using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace PbsuLoader.Testes
{
    public class InstanciaUnicaTests
    {
        [Fact]
        public void SegundaInstancia_EncaminhaOPedidoParaAPrimeira()
        {
            InstanciaUnica.Sufixo = "-" + Guid.NewGuid().ToString("N");
            try
            {
                var recebidos = new List<string>();
                var chegou = new AutoResetEvent(false);
                Assert.True(InstanciaUnica.Entrar(null, s => { recebidos.Add(s); chegou.Set(); }));

                // A segunda instância é outro processo: simule em outra thread — o mutex é dela por thread.
                bool dona = true;
                var segunda = new Thread(() => dona = InstanciaUnica.Entrar("freewalk-caminhar-a-pe", _ => { }));
                segunda.Start();
                segunda.Join();
                Assert.False(dona);
                Assert.True(chegou.WaitOne(TimeSpan.FromSeconds(5)));
                Assert.Equal(new[] { "freewalk-caminhar-a-pe" }, recebidos);

                // Mensagem vazia = só trazer a janela à frente.
                dona = true;
                var terceira = new Thread(() => dona = InstanciaUnica.Entrar(null, _ => { }));
                terceira.Start();
                terceira.Join();
                Assert.False(dona);
                Assert.True(chegou.WaitOne(TimeSpan.FromSeconds(5)));
                Assert.Equal("", recebidos[1]);
            }
            finally { InstanciaUnica.Sufixo = ""; }
        }
    }
}
