using System;
using System.Collections.Generic;

namespace PBSLoaderCommonInterface
{
    // API para outros mods aparecerem no menu da PBSLoaderCommonInterface, sem precisar de atalho de teclado.
    //
    // Sem fazer nada, todo mod que usa MelonPreferences já ganha um painel de opções automático
    // (no menu lateral da partida e no menu principal, em "Mods"): bool vira chave, número vira
    // campo com − e +, texto vira campo, KeyCode vira "aperte uma tecla", enum vira seletor.
    // A API só acrescenta o que as preferências não dizem sozinhas:
    //
    //   Acao     botão que roda código do mod (ex.: abrir o painel próprio dele);
    //   Faixa    min/max de um número, que passa a ser slider;
    //   Ocultar  entrada que não deve aparecer (ex.: valores internos);
    //   Nome     título e ícone do mod no menu (padrão: o DisplayName da categoria).
    //
    // "categoria" é o Identifier da MelonPreferences_Category do mod.
    //
    // A PBSLoaderCommonInterface é base do PBSU Loader (sempre instalada), então é só chamar, de preferência no
    // OnLateInitializeMelon (quando todas as categorias de preferências já existem):
    //
    //   [assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils", "PBSLoaderCommonInterface")]
    //   ...
    //   PainelMods.Acao("AjusteEspelhos", "Ajustar retrovisores", Painel.Alternar);
    //   PainelMods.Faixa("AjusteEspelhos", "Velocidade", 1f, 60f, 1f);
    //
    // Para um painel próprio por cima do jogo, sem desenhar nada à mão, veja Paineis; para valores na tela durante a
    // direção, Hud.
    public static class PainelMods
    {
        internal sealed class Info
        {
            public string titulo, icone;
            public readonly List<(string rotulo, string descricao, Action acao)> acoes = new();
            public readonly Dictionary<string, (float min, float max, float passo)> faixas = new();
            public readonly HashSet<string> ocultas = new();
        }

        internal static readonly Dictionary<string, Info> Mods = new(StringComparer.Ordinal);

        // Avisa as telas abertas que algo mudou (montam de novo a parte dos mods).
        internal static event Action Mudou;

        static Info De(string categoria)
        {
            if (string.IsNullOrEmpty(categoria)) throw new ArgumentException("categoria vazia");
            if (!Mods.TryGetValue(categoria, out var i)) Mods[categoria] = i = new Info();
            return i;
        }

        /// <summary>Botão que roda <paramref name="acao"/> (ex.: abrir o painel do mod).</summary>
        public static void Acao(string categoria, string rotulo, Action acao, string descricao = null)
        {
            if (acao == null) throw new ArgumentNullException(nameof(acao));
            var info = De(categoria);
            info.acoes.RemoveAll(a => a.rotulo == rotulo);
            info.acoes.Add((rotulo, descricao, acao));
            Mudou?.Invoke();
        }

        /// <summary>Faixa de um número: vira slider de min a max (passo 0 = contínuo).</summary>
        public static void Faixa(string categoria, string entrada, float min, float max, float passo = 0f)
        {
            if (max <= min) throw new ArgumentException("max precisa ser maior que min");
            De(categoria).faixas[entrada] = (min, max, passo);
            Mudou?.Invoke();
        }

        /// <summary>Esconde uma entrada das preferências do painel.</summary>
        public static void Ocultar(string categoria, string entrada)
        {
            De(categoria).ocultas.Add(entrada);
            Mudou?.Invoke();
        }

        /// <summary>Título (e ícone da Segoe Fluent Icons, opcional) do mod no menu.</summary>
        public static void Nome(string categoria, string titulo, string icone = null)
        {
            var info = De(categoria);
            info.titulo = titulo;
            info.icone = icone;
            Mudou?.Invoke();
        }

        /// <summary>Abre o painel de mods (na categoria indicada, se houver).</summary>
        public static void Abrir(string categoria = null) => TelaMods.Abrir(categoria);
    }
}
