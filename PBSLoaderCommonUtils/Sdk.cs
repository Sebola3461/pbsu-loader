using System;
using System.Collections.Generic;
using MelonLoader;
using PBSLoaderCommonUtils.Interface;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PBSLoaderCommonUtils
{
    /// <summary>Em que parte do jogo o jogador está.</summary>
    public enum Fase { Menu, Selecao, Carregando, Dirigindo }

    /// <summary>
    /// Ciclo de vida do SDK: versão, fase do jogo e eventos. Tudo roda na thread principal, no quadro da
    /// PBSLoaderCommonUtils (que tem prioridade -1000, então os dados já estão atualizados quando o
    /// OnUpdate dos outros mods roda). Uma exceção num inscrito não impede os outros de receber o evento.
    /// </summary>
    public static class Sdk
    {
        /// <summary>Versão da API (semver). Mudança no primeiro número quebra mods.</summary>
        public const string Versao = "2.0.0";

        /// <summary>
        /// Confere se o SDK instalado tem pelo menos <paramref name="versaoMinima"/>. Se não tiver, registra
        /// o erro, mostra uma falha na tela e retorna false (o mod deve se desligar).
        /// </summary>
        public static bool Requer(string versaoMinima, string mod)
        {
            if (Version.TryParse(Versao, out var atual) && Version.TryParse(versaoMinima, out var min) && atual >= min) return true;
            MelonLogger.Error($"[{mod}] precisa da PBSLoaderCommonUtils {versaoMinima} ou mais nova (instalada: {Versao}). Atualize pelo PBSLoader.");
            Avisos.Notificar("PBSLoaderCommonUtils", "requer-" + mod, Gravidade.Falha, $"{mod} precisa de uma PBSLoaderCommonUtils mais nova",
                $"instalada {Versao}, pedida {versaoMinima}", 30f);
            return false;
        }

        /// <summary>Versão do jogo ("307"), ou "não testada" numa versão que o SDK não conhece.</summary>
        public static string VersaoDoJogo => PBSLoaderCommonUtils.VersaoDoJogo.Nome;
        /// <summary>O SDK foi testado nesta versão do jogo (pelo SHA-256 do GameAssembly.dll).</summary>
        public static bool JogoTestado => PBSLoaderCommonUtils.VersaoDoJogo.Testada && !PBSLoaderCommonUtils.VersaoDoJogo.ForcarDesconhecida;
        /// <summary>
        /// Versão do jogo não testada (e o jogador não liberou): os ganchos não disparam, pedidos de condução e o freio a
        /// ar não escrevem no jogo e <see cref="Avancado"/> devolve null. As fachadas de leitura continuam funcionando.
        /// Mods que mexem no jogo por conta própria devem conferir isto.
        /// </summary>
        public static bool ModoSeguro => !JogoTestado && !(PBSLoaderCommonUtils.VersaoDoJogo.Permitir?.Value ?? false);

        /// <summary>Fase atual (menu, telas de escolha, carregando a partida, dirigindo).</summary>
        public static Fase Fase { get; private set; } = Fase.Menu;
        /// <summary>Cena ativa da Unity.</summary>
        public static string Cena { get; private set; } = "";

        static readonly Inscritos<Action<Fase>> _faseMudou = new("FaseMudou");
        static readonly Inscritos<Action<string>> _cenaCarregou = new("CenaCarregou");
        static readonly Inscritos<Action> _veiculoTrocou = new("VeiculoTrocou"), _mapaCarregou = new("MapaCarregou"), _quadro = new("Quadro"),
            _passoFisico = new("PassoFisico"), _quadroTardio = new("QuadroTardio");

        /// <summary>A fase mudou (o argumento é a nova).</summary>
        public static event Action<Fase> FaseMudou { add => _faseMudou.Add(value); remove => _faseMudou.Remove(value); }
        /// <summary>Uma cena terminou de carregar (inclui as aditivas, como os tiles do mapa).</summary>
        public static event Action<string> CenaCarregou { add => _cenaCarregou.Add(value); remove => _cenaCarregou.Remove(value); }
        /// <summary>O veículo do jogador apareceu ou sumiu. Veja <see cref="Veiculos.Veiculo.Existe"/>.</summary>
        public static event Action VeiculoTrocou { add => _veiculoTrocou.Add(value); remove => _veiculoTrocou.Remove(value); }
        /// <summary>Um mapa novo começou (o jogador está dirigindo nele). Veja <see cref="Mapas.Mapa"/>.</summary>
        public static event Action MapaCarregou { add => _mapaCarregou.Add(value); remove => _mapaCarregou.Remove(value); }
        /// <summary>Todo quadro (Update), depois de todos os dados do SDK atualizados.</summary>
        public static event Action Quadro { add => _quadro.Add(value); remove => _quadro.Remove(value); }
        /// <summary>Todo passo de física (FixedUpdate), depois do ar comprimido.</summary>
        public static event Action PassoFisico { add => _passoFisico.Add(value); remove => _passoFisico.Remove(value); }
        /// <summary>Todo quadro, no LateUpdate.</summary>
        public static event Action QuadroTardio { add => _quadroTardio.Add(value); remove => _quadroTardio.Remove(value); }

        internal static void AvisarVeiculoTrocou() => _veiculoTrocou.Disparar();
        internal static void AvisarCena(string cena) => _cenaCarregou.Disparar(cena);
        internal static void AvisarMapaCarregou() => _mapaCarregou.Disparar();
        internal static void AvisarQuadro() => _quadro.Disparar();
        internal static void AvisarPassoFisico() => _passoFisico.Disparar();
        internal static void AvisarQuadroTardio() => _quadroTardio.Disparar();

        internal static void AtualizarFase()
        {
            Cena = SceneManager.GetActiveScene().name ?? "";
            var f = Veiculos.Veiculo.Existe ? Fase.Dirigindo
                : Cena == "MainMenu" ? Fase.Menu
                : Cena.StartsWith("LOAD", StringComparison.Ordinal) ? Fase.Selecao
                : Fase.Carregando;
            if (f == Fase) return;
            Fase = f;
            MelonLogger.Msg($"[Sdk] fase {f} (cena {Cena})");
            _faseMudou.Disparar(f);
        }

        // ---------- execução protegida ----------

        // Erros repetidos (todo quadro) aparecem no máximo a cada 10 s por lugar; a pilha só na primeira vez.
        static readonly Dictionary<string, float> _ultimoErro = new();
        static readonly HashSet<string> _comPilha = new();

        /// <summary>Roda <paramref name="a"/> e registra a exceção sem derrubar quem chamou (no máximo a cada 10 s por <paramref name="onde"/>).</summary>
        public static bool Seguro(Action a, string onde)
        {
            try { a(); return true; }
            catch (Exception e) { Registrar(e, onde); return false; }
        }

        /// <summary>Registra uma exceção no log, no máximo a cada 10 s por <paramref name="onde"/> (a pilha só na primeira vez).</summary>
        public static void Registrar(Exception e, string onde)
        {
            float agora = Time.realtimeSinceStartup;
            if (_ultimoErro.TryGetValue(onde, out var t) && agora - t < 10f) return;
            _ultimoErro[onde] = agora;
            MelonLogger.Warning(_comPilha.Add(onde) ? $"[{onde}] {e}" : $"[{onde}] {e.GetType().Name}: {e.Message}");
        }
    }
}
