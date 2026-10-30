using PBSLoaderCommonUtils.Conteudo;
using PBSLoaderCommonUtils.Interface;
using PBSLoaderCommonUtils.Linhas;
using PBSLoaderCommonUtils.Mapas;

namespace ScriptDeMapaExemplo
{
    // Vem dentro da pasta do mapa (scripts/) e só roda com este mapa carregado: anuncia cada ponto em que o
    // ônibus do jogador para, com quantas pessoas esperam e o destino, e registra os tiles carregados.
    public class AnuncioDeParada : ScriptDeMapa
    {
        Parada _ultima;

        public override void Ativar() => Pacote.Log.Msg($"mapa {Mapa.Nome}: {Mapa.Paradas.Count} pontos carregados");

        public override void TileCarregou(string cena) => Pacote.Log.Msg($"tile {cena}, agora {Mapa.Paradas.Count} pontos");

        public override void Quadro()
        {
            var p = Linha.ParadaAtual;
            if (p == _ultima) return;
            _ultima = p;
            if (p == null) return;
            Avisos.Notificar(Pacote.Id, "ponto", Gravidade.Info, p.Nome,
                $"{p.Esperando} esperando{(p.Destino.Length > 0 ? " · destino " + p.Destino : "")}{(p.Final ? " · ponto final" : "")}", 6f);
        }
    }
}
