using MelonLoader;
using PBSLoaderCommonUtils;
using PBSLoaderCommonUtils.Entrada;
using PBSLoaderCommonUtils.Linhas;
using PBSLoaderCommonUtils.Mapas;
using PBSLoaderCommonUtils.Mundo;
using PBSLoaderCommonUtils.Veiculos;
using UnityEngine;

[assembly: MelonInfo(typeof(ModExemplo.ModExemploMod), "ModExemplo", "1.0.0", "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]
[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils")]

namespace ModExemplo
{
    // Mod mínimo do SDK: um painel de texto com o que o SDK lê do jogo, e os eventos no log.
    // Ctrl+Alt+Insert mostra/esconde o painel (o jogador pode trocar a tecla na tela de atalhos da PBSLoaderCommonInterface).
    public class ModExemploMod : MelonMod
    {
        bool _ligado = true;
        bool _ok;

        public override void OnInitializeMelon()
        {
            // Sem a versão certa do SDK o mod se desliga sozinho (e o jogador vê um aviso).
            _ok = Sdk.Requer("2.0", "ModExemplo");
            if (!_ok) return;

            Atalhos.Registrar("ModExemplo", "Painel", "Painel do exemplo", "Ctrl+Alt+Insert", () => _ligado = !_ligado);

            Sdk.FaseMudou += f => LoggerInstance.Msg($"fase: {f}");
            Sdk.VeiculoTrocou += () => LoggerInstance.Msg(Veiculo.Existe ? $"entrou no ônibus {Veiculo.Nome}" : "sem ônibus");
            Sdk.MapaCarregou += () => LoggerInstance.Msg($"mapa: {Mapa.Nome} ({Mapa.Origem})");
            Tempo.HoraPassou += h => LoggerInstance.Msg($"{h}h no relógio do jogo");
            Clima.Mudou += () => LoggerInstance.Msg($"clima: {Clima.Nome}, chuva {Clima.Chuva}");
            Passageiros.Embarcou += p => LoggerInstance.Msg($"embarcou (ponto {p.Origem?.Nome ?? "?"}), {Passageiros.ABordo} a bordo");
            Passageiros.Desceu += p => LoggerInstance.Msg($"desceu, {Passageiros.ABordo} a bordo");
        }

        public override void OnGUI()
        {
            if (!_ok || !_ligado || Sdk.Fase != Fase.Dirigindo) return;
            var atual = Linha.ParadaAtual;
            var prox = Linha.ProximaParada;
            string texto =
                $"{Veiculo.Nome}  {Veiculo.VelocidadeKmh:0} km/h  marcha {Veiculo.Cambio.MarchaTexto}  {Veiculo.Motor.Rpm:0} rpm\n" +
                $"{Tempo.Texto}{(Tempo.Noite ? " (noite)" : "")}  {(Clima.Chovendo ? "chuva " + Clima.Chuva : "sem chuva")}\n" +
                $"mapa {Mapa.Nome}  linha {Linha.Nome}\n" +
                $"ponto {(atual != null ? atual.Nome : "-")}  próximo {(prox != null ? $"{prox.Nome} a {Vector3.Distance(prox.Posicao, Veiculo.Raiz.position):0} m" : "-")}\n" +
                $"{Passageiros.ABordo} a bordo, {Passageiros.EmbarcaramSessao} embarcaram, {Passageiros.DesceramSessao} desceram" +
                $"{(Passageiros.SinalSolicitado ? "  [parada solicitada]" : "")}\n" +
                $"tráfego {Trafego.Carros} carros, {Trafego.Pedestres} pedestres";
            GUI.Box(new Rect(10, 10, 520, 112), "");
            GUI.Label(new Rect(18, 14, 510, 108), texto);
        }
    }
}
