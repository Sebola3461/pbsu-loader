using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PBSLoaderCommonUtils
{
    /// <summary>Um atalho registrado por um mod. A combinação vem das preferências (categoria "Atalhos").</summary>
    public sealed class Atalho
    {
        /// <summary>Identifier da categoria de preferências do mod (o mesmo que a UiModerna usa no menu).</summary>
        public string Mod { get; internal set; }
        public string Id { get; internal set; }
        public string Nome { get; internal set; }
        public string Descricao { get; internal set; }
        public Combinacao Padrao { get; internal set; }
        /// <summary>Atalho de segurar (a ação dura enquanto a tecla está apertada); não chama a ação ao apertar.</summary>
        public bool DeSegurar { get; internal set; }
        /// <summary>
        /// Só vale com o painel do mod aberto, que suspende os controles de teclado do jogo
        /// (<see cref="Teclado.SuspenderJogo"/>): conflitos com o Rewired não contam.
        /// </summary>
        public bool SoComPainel { get; internal set; }
        public Combinacao Combinacao { get; internal set; }

        internal MelonPreferences_Entry<string> Entrada;
        internal string TextoLido;
        internal Action Acao;
        internal readonly List<string> conflitos = new();

        /// <summary>Apertou neste frame, com os modificadores exatos, fora de campo de texto.</summary>
        public bool Apertou => Atalhos.Liberados && Combinacao.Apertou();
        /// <summary>Está apertado (modificadores a mais são tolerados), fora de campo de texto.</summary>
        public bool Segurando => Atalhos.Liberados && Combinacao.Segurando();

        /// <summary>Com o que este atalho colide: outros mods e teclas que o jogo usa. Vazio = livre.</summary>
        public IReadOnlyList<string> Conflitos => conflitos;

        /// <summary>Identifier da entrada nas preferências ("AjusteEspelhos_Painel").</summary>
        public string Chave => Mod + "_" + Id;

        /// <summary>Tem ação para executar (atalhos de segurar não têm).</summary>
        public bool TemAcao => Acao != null;

        /// <summary>Roda a ação do atalho como se a tecla tivesse sido apertada (botões de interface, por exemplo).</summary>
        public void Executar()
        {
            if (Acao == null) return;
            try { Acao(); }
            catch (Exception e) { MelonLogger.Warning($"[Atalhos] {Chave}: {e}"); }
        }

        /// <summary>Muda a combinação (e grava nas preferências no próximo save).</summary>
        public void Definir(Combinacao c)
        {
            Entrada.Value = c.ToString();
            Atalhos.Reler(this);
        }
    }

    /// <summary>
    /// Registro único de atalhos de todos os mods. Cada atalho fica numa entrada da categoria "Atalhos" das
    /// preferências, então o jogador troca tudo num lugar só (a UiModerna mostra essa categoria com captura
    /// de tecla e os conflitos). Atalhos não disparam enquanto o jogador digita num campo de texto, e os
    /// modificadores são exatos: "F9" não dispara com Ctrl+F9.
    /// </summary>
    public static class Atalhos
    {
        public const string Categoria = "Atalhos";

        static MelonPreferences_Category _cat;
        static readonly List<Atalho> _todos = new();

        public static IReadOnlyList<Atalho> Todos => _todos;

        /// <summary>Algum atalho foi registrado, mudou de tecla ou os conflitos foram recalculados.</summary>
        public static event Action Mudou;

        /// <summary>Desliga todos os atalhos (ex.: a UiModerna esperando o jogador apertar a tecla nova).</summary>
        public static bool Pausado { get; set; }

        /// <summary>
        /// Registra um atalho. <paramref name="mod"/> é o Identifier da categoria de preferências do mod;
        /// <paramref name="padrao"/> é como "Ctrl+Alt+F9" (vazio = sem atalho até o jogador definir).
        /// <paramref name="acao"/> roda ao apertar (para atalhos de segurar, leia <see cref="Atalho.Segurando"/>).
        /// <paramref name="soComPainel"/>: o mod só lê este atalho com o painel dele aberto e suspende os
        /// controles do jogo enquanto isso (ver <see cref="Atalho.SoComPainel"/>).
        /// </summary>
        public static Atalho Registrar(string mod, string id, string nome, string padrao, Action acao = null,
            string descricao = null, bool deSegurar = false, bool soComPainel = false)
        {
            if (!Combinacao.TentarLer(padrao, out var p))
            {
                MelonLogger.Warning($"[Atalhos] padrão inválido '{padrao}' em {mod}.{id}");
                p = Combinacao.Nenhuma;
            }
            var a = _todos.Find(x => x.Mod == mod && x.Id == id);
            if (a == null)
            {
                a = new Atalho { Mod = mod, Id = id };
                _todos.Add(a);
            }
            a.Nome = nome; a.Descricao = descricao; a.Padrao = p; a.DeSegurar = deSegurar; a.SoComPainel = soComPainel; a.Acao = acao;
            _cat ??= MelonPreferences.CreateCategory(Categoria, "Atalhos de teclado");
            // Registrar de novo o mesmo atalho reaproveita a entrada: o CreateEntry recusa uma chave que já existe.
            a.Entrada ??= _cat.GetEntry<string>(a.Chave) ?? _cat.CreateEntry(a.Chave, p.ToString(), nome, descricao);
            Reler(a);
            return a;
        }

        public static Atalho Achar(string mod, string id) => _todos.Find(x => x.Mod == mod && x.Id == id);

        // Os atalhos só valem fora de campos de texto (uGUI e TextMeshPro).
        internal static bool Liberados => !Pausado && !Digitando();

        static GameObject _selecionado;
        static UnityEngine.UI.InputField _campo;
        static Il2CppTMPro.TMP_InputField _campoTmp;

        internal static bool Digitando()
        {
            try
            {
                var es = EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null) return false;
                if (go != _selecionado)
                {
                    _selecionado = go;
                    _campo = go.GetComponent<UnityEngine.UI.InputField>();
                    _campoTmp = go.GetComponent<Il2CppTMPro.TMP_InputField>();
                }
                return (_campo != null && _campo.isFocused) || (_campoTmp != null && _campoTmp.isFocused);
            }
            catch { _selecionado = null; return false; }
        }

        internal static void Reler(Atalho a)
        {
            var t = a.Entrada.Value ?? "";
            a.TextoLido = t;
            if (Combinacao.TentarLer(t, out var c)) a.Combinacao = c;
            else
            {
                MelonLogger.Warning($"[Atalhos] '{t}' não é uma tecla válida ({a.Chave}); usando o padrão {a.Padrao}");
                a.Combinacao = a.Padrao;
            }
            Verificar();
        }

        /// <summary>Recalcula os conflitos de todos os atalhos (com os outros mods e com o jogo).</summary>
        public static void Verificar()
        {
            foreach (var a in _todos)
            {
                a.conflitos.Clear();
                if (a.Combinacao.Vazia) continue;
                // Atalhos de painel só colidem com outros atalhos de painel do mesmo mod (só um painel
                // fica aberto por vez); os globais colidem entre si.
                foreach (var b in _todos)
                    if (b != a && b.Combinacao == a.Combinacao && b.SoComPainel == a.SoComPainel && (!a.SoComPainel || a.Mod == b.Mod))
                        a.conflitos.Add($"{b.Mod}: {b.Nome}");
                a.conflitos.AddRange(Jogo.Conflitos(a.Combinacao, rewired: !a.SoComPainel));
            }
            try { Mudou?.Invoke(); } catch (Exception e) { MelonLogger.Warning("[Atalhos] " + e.Message); }
        }

        internal static void Atualizar()
        {
            // A UiModerna (ou o .cfg recarregado) pode ter mudado o texto da entrada.
            for (int i = 0; i < _todos.Count; i++)
            {
                var a = _todos[i];
                if (!ReferenceEquals(a.Entrada.Value, a.TextoLido)) Reler(a);
            }
            if (!Liberados) return;
            for (int i = 0; i < _todos.Count; i++)
            {
                var a = _todos[i];
                if (a.Acao == null || a.DeSegurar || !a.Combinacao.Apertou()) continue;
                try { a.Acao(); }
                catch (Exception e) { MelonLogger.Warning($"[Atalhos] {a.Chave}: {e}"); }
            }
        }
    }
}
