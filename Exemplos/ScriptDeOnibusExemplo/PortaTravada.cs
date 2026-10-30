using MelonLoader;
using PBSLoaderCommonUtils;
using PBSLoaderCommonUtils.Conteudo;
using PBSLoaderCommonUtils.Interface;
using PBSLoaderCommonUtils.Veiculos;

namespace ScriptDeOnibusExemplo
{
    // Vem dentro da pasta do ônibus (scripts/) e só roda enquanto este ônibus é o do jogador.
    //  - trava as portas acima de uma velocidade (configurável) usando Ganchos.PortaPedida;
    //  - avisa se o ônibus anda com porta aberta;
    //  - um atalho do próprio ônibus liga o pisca-alerta.
    public class PortaTravada : ScriptDeOnibus
    {
        MelonPreferences_Entry<float> _limiteKmh;

        public override void Ativar()
        {
            _limiteKmh ??= Pacote.Config.CreateEntry("LimiteKmh", 3f, "Velocidade máxima para abrir as portas (km/h)");
            Pacote.Atalho("Alerta", "Pisca-alerta (exemplo)", "Ctrl+Alt+Shift+Insert", Comandos.PiscaAlerta);
            Ganchos.PortaPedida += AoPedirPorta;
            Pacote.Log.Msg($"ativo em {Veiculo.Nome}: {Veiculo.Portas.Todas.Count} portas, trava acima de {_limiteKmh.Value:0} km/h");
        }

        public override void Desativar()
        {
            // Inscrições em eventos do SDK não saem sozinhas.
            Ganchos.PortaPedida -= AoPedirPorta;
        }

        void AoPedirPorta(PedidoDePorta p)
        {
            if (p.Abrindo && Veiculo.VelocidadeKmh > _limiteKmh.Value)
            {
                p.Cancelar(Pacote.Id);
                Avisos.Notificar(Pacote.Id, "travada", Gravidade.Aviso, "Porta travada", $"pare abaixo de {_limiteKmh.Value:0} km/h", 3f);
            }
        }

        public override void Quadro()
        {
            Avisos.Definir(Pacote.Id, "andando-aberta", Veiculo.Portas.AlgumaAberta && Veiculo.VelocidadeKmh > 5f,
                Gravidade.Falha, "Andando com a porta aberta");
        }
    }
}
