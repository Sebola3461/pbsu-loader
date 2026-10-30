using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PBSLoaderCommonUtils.Entrada
{
    public enum TipoDeAcao { Botao, Eixo }

    /// <summary>Uma ação dos controles do jogo (Rewired): "Acelerar", "Seta Esquerda", "Abrir Porta 1"...</summary>
    public sealed class AcaoDoJogo
    {
        public int Id { get; internal set; }
        /// <summary>Nome interno da ação (o que se passa para <see cref="ControlesDoJogo.Botao"/>).</summary>
        public string Nome { get; internal set; }
        /// <summary>Nome que o jogo mostra (pode ser igual ao interno).</summary>
        public string Descricao { get; internal set; }
        public TipoDeAcao Tipo { get; internal set; }
        public override string ToString() => $"{Nome}{(Descricao != Nome && Descricao.Length > 0 ? " (" + Descricao + ")" : "")} [{Tipo}]";
    }

    /// <summary>
    /// O que o jogador está fazendo nos controles do jogo (teclado, volante, controle), pelas ações do Rewired, já com o
    /// mapeamento que ele escolheu. Serve para reagir ao comando em vez da tecla: "apertou a seta" vale no volante e no
    /// teclado. Só leitura; para escrever pedais use <see cref="Veiculos.Conducao"/>. Enquanto
    /// <see cref="Teclado.SuspenderJogo"/> estiver ativo, o teclado não conta.
    /// </summary>
    public static class ControlesDoJogo
    {
        static readonly List<AcaoDoJogo> _acoes = new();
        static readonly Dictionary<string, AcaoDoJogo> _porNome = new(StringComparer.OrdinalIgnoreCase);
        static float _proximaLeitura;

        /// <summary>Todas as ações que o jogo define (lidas uma vez, quando os controles do jogo existem).</summary>
        public static IReadOnlyList<AcaoDoJogo> Acoes { get { Carregar(); return _acoes; } }

        /// <summary>A ação com este nome interno ou descritivo, ou null.</summary>
        public static AcaoDoJogo Achar(string nome)
        {
            Carregar();
            return nome != null && _porNome.TryGetValue(nome, out var a) ? a : null;
        }

        /// <summary>O botão da ação está apertado agora.</summary>
        public static bool Botao(string acao) => Ler(acao, (p, id) => p.GetButton(id));
        /// <summary>O botão da ação foi apertado neste quadro.</summary>
        public static bool Apertou(string acao) => Ler(acao, (p, id) => p.GetButtonDown(id));
        /// <summary>O botão da ação foi solto neste quadro.</summary>
        public static bool Soltou(string acao) => Ler(acao, (p, id) => p.GetButtonUp(id));
        /// <summary>Valor do eixo da ação, -1..1 (pedais e volante).</summary>
        public static float Eixo(string acao)
        {
            var a = Achar(acao);
            var p = TeclasDoJogo.Jogador();
            if (a == null || p == null) return 0f;
            try { return p.GetAxis(a.Id); } catch { return 0f; }
        }

        static bool Ler(string acao, Func<Il2CppRewired.Player, int, bool> f)
        {
            var a = Achar(acao);
            var p = TeclasDoJogo.Jogador();
            if (a == null || p == null) return false;
            try { return f(p, a.Id); } catch { return false; }
        }

        static readonly Inscritos<Action<AcaoDoJogo>> _apertada = new("ControlesDoJogo.AcaoApertada");

        /// <summary>
        /// Um botão de ação foi apertado (uma vez por aperto, em qualquer controle). Enquanto houver inscrito, a
        /// PBSLoaderCommonUtils confere todas as ações a cada quadro.
        /// </summary>
        public static event Action<AcaoDoJogo> AcaoApertada { add => _apertada.Add(value); remove => _apertada.Remove(value); }

        /// <summary>Controles conectados (nomes que o Rewired dá: teclado, mouse, volante, controle).</summary>
        public static IReadOnlyList<string> Dispositivos
        {
            get
            {
                var l = new List<string>();
                try
                {
                    var cs = Il2CppRewired.ReInput.controllers?.Controllers;
                    int n = cs != null ? cs.Cast<Il2CppSystem.Collections.Generic.ICollection<Il2CppRewired.Controller>>().Count : 0;
                    for (int i = 0; i < n; i++) { var c = cs[i]; if (c != null) l.Add($"{c.name} ({c.type})"); }
                }
                catch { }
                return l;
            }
        }

        internal static void Quadro()
        {
            if (_apertada.Vazio) return;
            var p = TeclasDoJogo.Jogador();
            if (p == null) return;
            Carregar();
            foreach (var a in _acoes)
            {
                if (a.Tipo != TipoDeAcao.Botao) continue;
                bool apertou;
                try { apertou = p.GetButtonDown(a.Id); } catch { continue; }
                if (apertou) _apertada.Disparar(a);
            }
        }

        static void Carregar()
        {
            if (_acoes.Count > 0 || Time.unscaledTime < _proximaLeitura) return;
            _proximaLeitura = Time.unscaledTime + 2f;
            try
            {
                if (!Il2CppRewired.ReInput.isReady) return;
                var lista = Il2CppRewired.ReInput.mapping.Actions;
                if (lista == null) return;
                int n = lista.Cast<Il2CppSystem.Collections.Generic.ICollection<Il2CppRewired.InputAction>>().Count;
                for (int i = 0; i < n; i++)
                {
                    var ia = lista[i];
                    if (ia == null) continue;
                    var a = new AcaoDoJogo
                    {
                        Id = ia.id,
                        Nome = ia.name ?? "",
                        Descricao = ia.descriptiveName ?? "",
                        Tipo = ia.type == Il2CppRewired.InputActionType.Axis ? TipoDeAcao.Eixo : TipoDeAcao.Botao,
                    };
                    _acoes.Add(a);
                    if (a.Nome.Length > 0) _porNome[a.Nome] = a;
                    if (a.Descricao.Length > 0 && !_porNome.ContainsKey(a.Descricao)) _porNome[a.Descricao] = a;
                }
            }
            catch (Exception e) { Sdk.Registrar(e, "ControlesDoJogo"); }
        }

        internal static string Resumo()
        {
            var sb = new StringBuilder();
            sb.Append($"dispositivos: {string.Join(", ", Dispositivos)}\n");
            sb.Append($"{Acoes.Count} ações: {string.Join("; ", Acoes)}\n");
            return sb.ToString();
        }
    }
}
