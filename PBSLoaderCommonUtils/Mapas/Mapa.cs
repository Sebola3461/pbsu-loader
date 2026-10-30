using System;
using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using PBSLoaderCommonUtils.Partida;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBSLoaderCommonUtils.Mapas
{
    public enum OrigemDoMapa { Jogo, Mod }

    /// <summary>Um ponto de ônibus do mapa (MEPBusStop). O mesmo objeto segue o mesmo ponto enquanto o tile dele estiver carregado.</summary>
    public sealed class Parada
    {
        public string Nome
        {
            get
            {
                if (!Vivo) return "";
                var n = Jogo.nomeDaParada ?? "";
                // Mods de mapa nomeiam o gatilho do ponto ("PRACA TUBAL VILELA_trigger").
                return n.EndsWith("_trigger", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 8) : n;
            }
        }
        /// <summary>Destino anunciado no ponto (pode ser vazio).</summary>
        public string Destino => Vivo ? Jogo.destino ?? "" : "";
        /// <summary>Lado da rua em que o ponto fica (o ônibus abre as portas desse lado).</summary>
        public Lado Lado => Vivo && Jogo.ehEsquerda ? Lado.Esquerdo : Lado.Direito;
        public bool Inicial => Vivo && Jogo.ehInicial;
        public bool Final => Vivo && Jogo.ehFinal;
        public Vector3 Posicao
        {
            get
            {
                if (!Vivo) return default;
                var t = Jogo.transformPosicaoDoPonto;
                return t != null ? t.position : Jogo.transform.position;
            }
        }
        /// <summary>Pessoas esperando para entrar.</summary>
        public int Esperando => Vivo ? Jogo.qtdePedsEntrando : 0;
        /// <summary>Passageiros que vão descer aqui.</summary>
        public int Descendo => Vivo ? Jogo.qtdePedsSaindo : 0;
        /// <summary>Algum ônibus (o do jogador ou do tráfego) está parado no ponto.</summary>
        public bool TemOnibus => Vivo && Jogo.temBusaoNoPonto;
        /// <summary>O ponto liberou o embarque no ônibus que está nele (o jogo confere o letreiro).</summary>
        public bool PodemEntrar => Vivo && Jogo.podemEntrar;
        /// <summary>Componente do jogo, para quem precisar de mais.</summary>
        public MEPBusStop Jogo { get; internal set; }

        bool Vivo => Jogo != null && !Jogo.WasCollected;
        public override string ToString() => $"\"{Nome}\"{(Destino.Length > 0 ? " → " + Destino : "")} ({Lado}{(Inicial ? ", inicial" : "")}{(Final ? ", final" : "")})";
    }

    /// <summary>
    /// O mapa da partida. <see cref="Existe"/> fica true quando o jogador está dirigindo. Mapas grandes carregam em
    /// tiles conforme o ônibus anda: <see cref="Paradas"/> lista só os pontos dos tiles carregados agora.
    /// </summary>
    public static class Mapa
    {
        public static bool Existe { get; private set; }
        public static string Nome { get; private set; } = "";
        public static OrigemDoMapa Origem { get; private set; }
        /// <summary>Pasta do mod de mapa (vazia nos mapas do jogo).</summary>
        public static string Pasta { get; private set; } = "";
        /// <summary>Versão declarada pelo mod de mapa (0 nos do jogo).</summary>
        public static float Versao { get; private set; }
        public static string PontoDePartida => Sessao.PontoDePartida;
        /// <summary>Muda a cada mapa carregado. Ver também <see cref="Sdk.MapaCarregou"/>.</summary>
        public static int Geracao { get; private set; }
        /// <summary>O jogo está carregando tiles agora.</summary>
        public static bool CarregandoTiles
        {
            get { try { return GerenciaTiles.contadorCenasCarregando > 0; } catch { return false; } }
        }
        /// <summary>Pontos de ônibus carregados agora (reescaneados a cada 3 s).</summary>
        public static IReadOnlyList<Parada> Paradas => _paradas;

        /// <summary>Nomes das pastas de mods de mapa instalados.</summary>
        public static IReadOnlyList<string> Disponiveis()
        {
            var r = new List<string>();
            try
            {
                var p = Caminhos.ModsDeMapas;
                if (p.Length > 0 && Directory.Exists(p))
                    foreach (var d in Directory.GetDirectories(p)) r.Add(Path.GetFileName(d));
            }
            catch { }
            r.Sort(StringComparer.OrdinalIgnoreCase);
            return r;
        }

        /// <summary>Ponto mais perto de <paramref name="mundo"/> (null se nenhum estiver carregado).</summary>
        public static Parada MaisPerto(Vector3 mundo, float maxM = float.MaxValue)
        {
            Parada melhor = null; float d2 = maxM * maxM;
            foreach (var p in _paradas)
            {
                float d = (p.Posicao - mundo).sqrMagnitude;
                if (d < d2) { d2 = d; melhor = p; }
            }
            return melhor;
        }

        internal static Parada Achar(MEPBusStop b)
        {
            if (b == null) return null;
            foreach (var p in _paradas) if (p.Jogo != null && p.Jogo.Pointer == b.Pointer) return p;
            var nova = new Parada { Jogo = b };
            _paradas.Add(nova);
            return nova;
        }

        static readonly List<Parada> _paradas = new();
        static string _cena = "";
        static float _proximaBusca;

        internal static void Atualizar()
        {
            if (Sdk.Fase != Fase.Dirigindo)
            {
                // Trocar de ônibus na mesma cena some com o veículo por um instante: o mapa continua o mesmo.
                if (Existe && Sdk.Cena != _cena) { Existe = false; _paradas.Clear(); }
                return;
            }
            string nome = Sessao.Mapa;
            if (nome.EndsWith(".map.txt", StringComparison.OrdinalIgnoreCase)) nome = nome.Substring(0, nome.Length - 8);
            bool novo = !Existe || nome != Nome || Sdk.Cena != _cena;
            if (novo)
            {
                Existe = true; Nome = nome; _cena = Sdk.Cena;
                bool mod = false;
                try { mod = GerenciaTiles.ehModMapaStatic; } catch { }
                Origem = mod ? OrigemDoMapa.Mod : OrigemDoMapa.Jogo;
                Pasta = ""; Versao = 0f;
                if (mod)
                {
                    try { Pasta = GerenciaModMapa1.pastaBaseDoMapa ?? ""; Versao = GerenciaModMapa1.mapModVersion; } catch { }
                    // O jogo guarda o nome da pasta, relativo à pasta de mods de mapa.
                    if (Pasta.Length > 0 && !Path.IsPathRooted(Pasta) && Caminhos.ModsDeMapas.Length > 0)
                        Pasta = Path.GetFullPath(Path.Combine(Caminhos.ModsDeMapas, Pasta));
                }
                _paradas.Clear(); _proximaBusca = 0f;
                Geracao++;
            }
            if (Time.unscaledTime >= _proximaBusca)
            {
                _proximaBusca = Time.unscaledTime + 3f;
                BuscarParadas();
            }
            if (novo)
            {
                MelonLoader.MelonLogger.Msg($"[Mapa] \"{Nome}\" ({Origem}{(Pasta.Length > 0 ? ", " + Pasta : "")}), {_paradas.Count} pontos carregados, geração {Geracao}");
                Sdk.AvisarMapaCarregou();
            }
        }

        static void BuscarParadas()
        {
            var vistos = new HashSet<IntPtr>();
            foreach (var b in Object.FindObjectsOfType<MEPBusStop>())
            {
                vistos.Add(b.Pointer);
                Achar(b);
            }
            _paradas.RemoveAll(p => p.Jogo == null || p.Jogo.WasCollected || !vistos.Contains(p.Jogo.Pointer));
        }

        internal static string Resumo()
        {
            if (!Existe) return "sem mapa\n";
            return $"\"{Nome}\" ({Origem}, pasta \"{Pasta}\", versão {Versao}), ponto de partida \"{PontoDePartida}\", carregando tiles {CarregandoTiles}, geração {Geracao}\n" +
                   $"{_paradas.Count} pontos carregados: {string.Join("; ", _paradas)}\n";
        }
    }
}
