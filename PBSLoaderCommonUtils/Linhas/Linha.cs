using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using PBSLoaderCommonUtils.Mapas;
using PBSLoaderCommonUtils.Partida;
using UnityEngine;

namespace PBSLoaderCommonUtils.Linhas
{
    /// <summary>A linha que o ônibus do jogador faz.</summary>
    public static class Linha
    {
        /// <summary>
        /// A linha escolhida na tela de linhas ("100 CIRCULAR CENTRO"). Sem essa tela (save carregado, partida
        /// rápida) fica o ponto de partida do mapa.
        /// </summary>
        public static string Nome => _escolhida.Length > 0 ? _escolhida : Sessao.PontoDePartida;

        static string _escolhida = "";

        // A escolha só existe na tela de linhas: lê o item da lista quando o jogador confirma.
        [HarmonyPatch(typeof(cenaEscolhaLinha), nameof(cenaEscolhaLinha.SalvaLinhaEVaiproMapa))]
        static class PatchEscolha
        {
            static void Prefix(cenaEscolhaLinha __instance)
            {
                try
                {
                    var dd = __instance.listaEscolhaLinha;
                    if (dd != null && dd.value >= 0 && dd.value < dd.options.Count) _escolhida = dd.options[dd.value].text?.Trim() ?? "";
                    MelonLoader.MelonLogger.Msg($"[Linha] escolhida \"{_escolhida}\"");
                }
                catch (Exception e) { Sdk.Registrar(e, "Linha.Escolha"); }
            }
        }

        /// <summary>Linhas que o mapa atual oferece (lista do jogo; vazia fora da tela de escolha em alguns mapas).</summary>
        public static IReadOnlyList<string> Disponiveis
        {
            get
            {
                try
                {
                    var r = rootListaDeLinhas.instance;
                    var l = r?.tdsAsLinhas;
                    if (l != null) { var lista = new List<string>(); foreach (var s in l) lista.Add(s); return lista; }
                }
                catch { }
                return Array.Empty<string>();
            }
        }

        /// <summary>O ponto em que o ônibus do jogador está agora (o jogo detecta pelo gatilho do ponto), ou null.</summary>
        public static Parada ParadaAtual
        {
            get
            {
                var c = Avancado.ControlesJogo;
                if (c == null || c.WasCollected) return null;
                MEPBusStop b = null;
                try { b = c.pontoQueOBusaoTah; } catch { }
                return b != null && !b.WasCollected ? Mapa.Achar(b) : null;
            }
        }

        public static bool NoPonto => ParadaAtual != null;

        /// <summary>
        /// O próximo ponto à frente do ônibus (até 2 km, num cone de ±45° na direção de marcha), pelos pontos
        /// carregados. É uma estimativa: o jogo não guarda o itinerário em ordem.
        /// </summary>
        public static Parada ProximaParada
        {
            get
            {
                var raiz = Veiculo.Raiz;
                if (raiz == null) return null;
                var atual = ParadaAtual;
                Vector3 pos = raiz.position, frente = raiz.forward;
                if (Veiculo.VelocidadeSinalKmh < -1f) frente = -frente;
                Parada melhor = null; float melhorD = 2000f * 2000f;
                foreach (var p in Mapa.Paradas)
                {
                    if (p == atual) continue;
                    var d = p.Posicao - pos;
                    float d2 = d.sqrMagnitude;
                    if (d2 > melhorD || d2 < 1f) continue;
                    if (Vector3.Dot(d.normalized, frente) < 0.707f) continue;
                    melhor = p; melhorD = d2;
                }
                return melhor;
            }
        }

        internal static string Resumo()
        {
            var atual = ParadaAtual; var prox = ProximaParada;
            return $"linha \"{Nome}\", no ponto {atual?.ToString() ?? "-"}, próximo {(prox != null ? prox + $" a {Vector3.Distance(prox.Posicao, Veiculo.Raiz.position):0} m" : "-")}\n" +
                   $"letreiro {Letreiro.Indice} (ligado {Letreiro.Ligado}; {Letreiro.Bruto()}), linhas do mapa: {string.Join(", ", Disponiveis)}\n" +
                   Passageiros.Resumo();
        }
    }

    /// <summary>Letreiro (itinerário) do ônibus do jogador (TrocaItinerario).</summary>
    public static class Letreiro
    {
        /// <summary>Número do letreiro escolhido (o que o jogador digita no teclado do itinerário), ou -1 sem letreiro.</summary>
        public static int Indice
        {
            get
            {
                var t = Jogo;
                if (t == null) return -1;
                try { return t.idLetreiroSalvo; } catch { return -1; }
            }
        }

        /// <summary>O letreiro do ônibus está aceso.</summary>
        public static bool Ligado
        {
            get
            {
                var c = Avancado.ControlesJogo;
                try { var m = c?.multiTelaLetreiroLigado; return m != null && m.enabled; } catch { return false; }
            }
        }

        /// <summary>
        /// Escolhe o letreiro <paramref name="numero"/>, como se o jogador digitasse no teclado do itinerário do jogo e
        /// confirmasse. Retorna false se o ônibus não tem esse teclado.
        /// </summary>
        public static bool Definir(int numero)
        {
            if (numero < 0 || Jogo == null) return false;
            CanvasItinerarioMep teclado = null;
            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<CanvasItinerarioMep>())
                    if (t != null && !t.WasCollected && t.gameObject.scene.IsValid()) { teclado = t; break; }
            }
            catch { }
            if (teclado == null) return false;
            try
            {
                teclado.btCancel();
                foreach (var ch in numero.ToString(System.Globalization.CultureInfo.InvariantCulture)) teclado.btNum(ch - '0');
                teclado.btOK();
                return true;
            }
            catch (Exception e) { Sdk.Registrar(e, "Letreiro.Definir"); return false; }
        }

        internal static string Bruto()
        {
            var t = Jogo;
            if (t == null) return "sem TrocaItinerario";
            try { return $"indexAtual {t.indexAtual}, idLetreiroSalvo {t.idLetreiroSalvo}, anterior {t.idLetreiroSalvoAnterior}, digital {t.usarLetreiroDigital}, lona {t.usarLetreiroLonaInteiro}"; }
            catch (Exception e) { return e.GetType().Name; }
        }

        /// <summary>Componente do jogo (null sem letreiro).</summary>
        public static TrocaItinerario Jogo
        {
            get
            {
                var c = Avancado.ControlesJogo;
                if (c == null || c.WasCollected) return null;
                try { var t = c.scriptLetreiro; if (t != null && !t.WasCollected) return t; } catch { }
                // Alguns ônibus não ligam o letreiro no MEPControlesBus: procura no próprio ônibus, uma vez por veículo.
                if (_procurado != Veiculo.Geracao)
                {
                    _procurado = Veiculo.Geracao;
                    _achado = Veiculo.Raiz != null ? Veiculo.Raiz.GetComponentInChildren<TrocaItinerario>(true) : null;
                }
                return _achado != null && !_achado.WasCollected ? _achado : null;
            }
        }

        static int _procurado = -1;
        static TrocaItinerario _achado;
    }

    /// <summary>Um passageiro do ônibus do jogador.</summary>
    public sealed class Passageiro
    {
        /// <summary>Ponto em que entrou (null se o tile dele já descarregou).</summary>
        public Parada Origem { get; internal set; }
        public bool Sentado => Vivo && Jogo.passageiroSentadoNoBusao;
        /// <summary>Já decidiu descer no próximo ponto.</summary>
        public bool VaiDescer => Vivo && Jogo.vaiDescer;
        public bool PassouNaCatraca => Vivo && Jogo.jaPassouNaCatraca;
        /// <summary>Componente do jogo.</summary>
        public Il2Cpp.Passageiros Jogo { get; internal set; }

        internal bool Entrou, Desceu;
        bool Vivo => Jogo != null && !Jogo.WasCollected;
    }

    /// <summary>
    /// Passageiros do ônibus do jogador. As contagens vêm do jogo; os eventos vêm de uma varredura dos passageiros
    /// do jogo duas vezes por segundo (então chegam com até meio segundo de atraso).
    /// </summary>
    public static class Passageiros
    {
        public static int ABordo => Veiculo.Cabine.PassageirosABordo;
        /// <summary>Passaram na catraca nesta sessão.</summary>
        public static int EmbarcaramSessao => Veiculo.Cabine.PassageirosSessao;
        /// <summary>Desceram nesta partida (contados pelo SDK).</summary>
        public static int DesceramSessao { get; private set; }
        public static bool SinalSolicitado => Veiculo.Cabine.ParadaSolicitada;
        /// <summary>O jogo deixa passageiros embarcarem neste ônibus.</summary>
        public static bool PodeEmbarcar
        {
            get { var c = Avancado.ControlesJogo; try { return c != null && c.podeEmbarcarPassageirosNesse; } catch { return false; } }
        }
        /// <summary>Os que estão dentro agora (pela varredura).</summary>
        public static IReadOnlyList<Passageiro> Dentro => _dentro;

        static readonly Inscritos<Action<Passageiro>> _embarcou = new("Passageiros.Embarcou"), _desceu = new("Passageiros.Desceu");
        public static event Action<Passageiro> Embarcou { add => _embarcou.Add(value); remove => _embarcou.Remove(value); }
        public static event Action<Passageiro> Desceu { add => _desceu.Add(value); remove => _desceu.Remove(value); }

        static readonly Dictionary<IntPtr, Passageiro> _todos = new();
        static readonly List<Passageiro> _dentro = new();
        static float _proxima;
        static int _geracao = -1;

        // Varredura de todos os Passageiros da cena (os mapas de mod nem sempre usam o PassageirosPool), 1 vez por segundo.
        internal static void Atualizar()
        {
            if (_geracao != Veiculo.Geracao) { _geracao = Veiculo.Geracao; _todos.Clear(); _dentro.Clear(); DesceramSessao = 0; }
            var c = Avancado.ControlesJogo;
            if (c == null || Time.unscaledTime < _proxima) return;
            _proxima = Time.unscaledTime + 1f;

            IntPtr meu = c.Pointer;
            var raiz = Veiculo.Raiz;
            IntPtr raizGo = raiz != null ? raiz.gameObject.Pointer : IntPtr.Zero;
            int total = 0, ligados = 0, entraram = 0, contados = 0, sentados = 0;
            _dentro.Clear();
            foreach (var j in UnityEngine.Object.FindObjectsOfType<Il2Cpp.Passageiros>())
            {
                total++;
                if (!_todos.TryGetValue(j.Pointer, out var p)) _todos[j.Pointer] = p = new Passageiro { Jogo = j };
                var bus = j.scriptDoMeuBusaoQueTou;
                var go = j.MeuBusaoQueTou;
                bool doMeu = (bus != null && bus.Pointer == meu) || (go != null && (go.Pointer == raizGo || go.transform.root.gameObject.Pointer == raizGo));
                if (!doMeu) { p.Entrou = p.Desceu = false; continue; }
                ligados++;
                if (j.jaEntrou) entraram++;
                if (j.jaLiberouContagemPassageiroNoBusao) contados++;
                if (j.passageiroSentadoNoBusao) sentados++;
                bool dentro = (j.jaEntrou || j.jaLiberouContagemPassageiroNoBusao || j.passageiroSentadoNoBusao) && !j.jaDesceu && !j.jaTaForaDoBusao;
                if (dentro)
                {
                    _dentro.Add(p);
                    if (!p.Entrou)
                    {
                        p.Entrou = true; p.Desceu = false;
                        p.Origem = j.meuPonto != null ? Mapa.Achar(j.meuPonto) : null;
                        _embarcou.Disparar(p);
                    }
                }
                else if (p.Entrou && !p.Desceu && (j.jaDesceu || j.jaTaForaDoBusao))
                {
                    p.Desceu = true; p.Entrou = false;
                    DesceramSessao++;
                    _desceu.Disparar(p);
                }
            }
            if (_todos.Count > 4 * Math.Max(64, total)) _todos.Clear(); // pedestres reciclados pelo jogo
            _varredura = $"{total} na cena, {ligados} ligados a este ônibus ({entraram} jaEntrou, {contados} contados, {sentados} sentados)";
        }

        static string _varredura = "";

        internal static string Resumo() =>
            $"passageiros: {ABordo} a bordo (varredura vê {_dentro.Count}), {EmbarcaramSessao} embarcaram, {DesceramSessao} desceram, sinal {SinalSolicitado}, ônibus aceita {PodeEmbarcar}\n  varredura: {_varredura}\n";
    }
}
