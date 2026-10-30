using System.Text;
using PBSLoaderCommonUtils.Conteudo;
using PBSLoaderCommonUtils.Entrada;
using PBSLoaderCommonUtils.Interface;
using PBSLoaderCommonUtils.Linhas;
using PBSLoaderCommonUtils.Mapas;
using PBSLoaderCommonUtils.Mundo;
using PBSLoaderCommonUtils.Partida;
using PBSLoaderCommonUtils.Simulacao;
using PBSLoaderCommonUtils.Veiculos;

namespace PBSLoaderCommonUtils
{
    // Tudo que o SDK está lendo, em texto, uma seção por área (sdk.txt, teste automático).
    internal static class Diagnostico
    {
        internal static string Texto()
        {
            var sb = new StringBuilder();
            sb.Append($"[sdk] versão {Sdk.Versao}, fase {Sdk.Fase}, cena {Sdk.Cena}; jogo {Sdk.VersaoDoJogo} (testado {Sdk.JogoTestado}, modo seguro {Sdk.ModoSeguro}, GameAssembly {VersaoDoJogo.Sha})\n\n");
            sb.Append("[partida]\n").Append(Sessao.Resumo()).Append(Vista.Resumo()).Append(Caminhos.Resumo()).Append('\n');
            sb.Append("[mapa]\n").Append(Mapa.Resumo()).Append('\n');
            sb.Append("[linha]\n").Append(Linha.Resumo()).Append('\n');
            sb.Append("[mundo]\n").Append(LeitorDoMundo.Resumo()).Append('\n');
            sb.Append("[veículo]\n").Append(Veiculo.Resumo()).Append('\n');
            sb.Append("[scripts de conteúdo]\n").Append(Carregador.Resumo()).Append('\n');
            sb.Append("[opções do jogo]\n").Append(Opcoes.Resumo()).Append('\n');
            sb.Append("[salvos]\n").Append(Salvos.Resumo()).Append('\n');
            sb.Append("[avisos]\n").Append(Avisos.Resumo()).Append('\n');
            sb.Append("[som do ar]\n").Append(SomAr.Diagnostico()).Append("\n\n");
            sb.Append("[controles do jogo]\n").Append(ControlesDoJogo.Resumo()).Append('\n');
            sb.Append("[mouse]\n").Append(Mouse.Diagnostico()).Append('\n');
            return sb.ToString();
        }
    }
}
