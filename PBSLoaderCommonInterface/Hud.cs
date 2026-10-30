using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    /// <summary>Um cartão de HUD de um mod (ver <see cref="Hud.Adicionar"/>).</summary>
    public sealed class Widget
    {
        public string Mod { get; }
        public string Id { get; }
        /// <summary>Rótulo pequeno em cima do valor ("PRESSÃO").</summary>
        public string Titulo { get; set; }
        /// <summary>Esconde o cartão sem removê-lo (ex.: só mostrar quando faz sentido).</summary>
        public bool Visivel { get; set; } = true;
        /// <summary>Cartões com ordem menor ficam mais em cima.</summary>
        public int Ordem { get; set; }
        public void Remover() => Hud.Remover(this);

        internal Func<string> Valor;
        internal Func<Color?> Cor;
        internal RectTransform Raiz;
        internal Text TextoTitulo, TextoValor;

        internal Widget(string mod, string id) { Mod = mod; Id = id; }
    }

    /// <summary>
    /// Cartões de texto de mods na tela, durante a direção, numa coluna no canto superior direito (o inferior é do
    /// painel de condução e dos avisos). Não recebem clique. O jogador pode esconder todos nas opções da interface.
    /// <code>Hud.Adicionar("MeuMod", "oleo", "Óleo", () => $"{_temp:0} °C");</code>
    /// </summary>
    public static class Hud
    {
        internal static MelonPreferences_Entry<bool> Pref;

        internal static void CriarPreferencias(MelonPreferences_Category cat)
        {
            Pref = cat.CreateEntry("WidgetsNaTela", true, "Cartões dos mods na tela",
                "Valores que os mods mostram durante a direção, no canto superior direito");
        }

        static readonly List<Widget> _todos = new();
        static GameObject _canvas;
        static RectTransform _coluna;
        static bool _ordenar;

        public static IReadOnlyList<Widget> Todos => _todos;

        /// <summary>
        /// Acrescenta (ou devolve o já existente) o cartão <paramref name="id"/> do mod. <paramref name="valor"/> é lido a
        /// cada quadro; <paramref name="cor"/>, se dado, colore o valor (null = cor padrão).
        /// </summary>
        public static Widget Adicionar(string mod, string id, string titulo, Func<string> valor, int ordem = 0, Func<Color?> cor = null)
        {
            foreach (var w in _todos)
                if (w.Mod == mod && w.Id == id) { w.Titulo = titulo; w.Valor = valor; w.Cor = cor; w.Ordem = ordem; _ordenar = true; return w; }
            var n = new Widget(mod, id) { Titulo = titulo, Valor = valor, Cor = cor, Ordem = ordem };
            _todos.Add(n);
            _ordenar = true;
            return n;
        }

        internal static void Remover(Widget w)
        {
            _todos.Remove(w);
            if (w.Raiz != null && !w.Raiz.WasCollected) UnityEngine.Object.Destroy(w.Raiz.gameObject);
            w.Raiz = null;
        }

        static void Construir()
        {
            if (_canvas != null && !_canvas.WasCollected) return;
            Theme.Carregar();
            _canvas = new GameObject("PBSLoaderCommonInterface_Hud") { layer = 5 };
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            var cv = _canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 300;
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _coluna = Kit.Novo("Coluna", _canvas.transform);
            _coluna.anchorMin = _coluna.anchorMax = new Vector2(1, 1); _coluna.pivot = new Vector2(1, 1);
            _coluna.sizeDelta = new Vector2(220, 0);
            var col = Kit.Coluna(_coluna, 6, 0, TextAnchor.UpperRight);
            var fit = _coluna.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _ = col;
        }

        static void Montar(Widget w)
        {
            var r = Kit.Novo("Widget_" + w.Mod + "_" + w.Id, _coluna);
            Kit.Tam(r, 220, 52);
            Kit.Fundo(r, Theme.Solido, Theme.Alfa(Theme.Painel, 0.84f)).raycastTarget = false;
            Kit.Marca(r, Theme.Acento, 3f).raycastTarget = false;
            var c = Kit.Coluna(r, 0, 0, TextAnchor.MiddleLeft);
            Kit.Pad(c, 14, 10, 6, 6);
            w.TextoTitulo = Kit.Texto(r, "", 11, Theme.TextoMudo, true);
            Kit.Tam(w.TextoTitulo, -1, 16);
            w.TextoValor = Kit.Texto(r, "", 20, Theme.Texto, true);
            w.TextoValor.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(w.TextoValor, -1, 26);
            foreach (var g in r.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            w.Raiz = r;
        }

        internal static void Atualizar(bool mostrar)
        {
            bool ligado = mostrar && (Pref == null || Pref.Value) && PBSLoaderCommonUtils.Veiculos.Veiculo.Existe && _todos.Count > 0;
            if (!ligado)
            {
                if (_canvas != null && !_canvas.WasCollected && _canvas.activeSelf) _canvas.SetActive(false);
                return;
            }
            Construir();
            if (!_canvas.activeSelf) _canvas.SetActive(true);
            _coluna.anchoredPosition = new Vector2(-24 - TelaAlt.LarguraOcupada, -24);
            if (_ordenar)
            {
                _ordenar = false;
                _todos.Sort((a, b) => a.Ordem != b.Ordem ? a.Ordem.CompareTo(b.Ordem) : string.CompareOrdinal(a.Mod + a.Id, b.Mod + b.Id));
                for (int i = 0; i < _todos.Count; i++) if (_todos[i].Raiz != null) _todos[i].Raiz.SetSiblingIndex(i);
            }
            foreach (var w in _todos)
            {
                if (w.Raiz == null || w.Raiz.WasCollected) { Montar(w); _ordenar = true; }
                if (w.Raiz.gameObject.activeSelf != w.Visivel) w.Raiz.gameObject.SetActive(w.Visivel);
                if (!w.Visivel) continue;
                string t = (w.Titulo ?? "").ToUpperInvariant();
                if (w.TextoTitulo.text != t) w.TextoTitulo.text = t;
                string v;
                try { v = w.Valor?.Invoke() ?? ""; }
                catch (Exception e) { PBSLoaderCommonUtils.Sdk.Registrar(e, $"Hud {w.Mod}.{w.Id}"); v = "—"; }
                if (w.TextoValor.text != v) w.TextoValor.text = v;
                Color cor = Theme.Texto;
                if (w.Cor != null) { try { cor = w.Cor() ?? Theme.Texto; } catch { } }
                if (w.TextoValor.color != cor) w.TextoValor.color = cor;
            }
        }

        internal static string Diagnostico()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var w in _todos) sb.Append($"{w.Mod}.{w.Id} \"{w.Titulo}\" visível {w.Visivel} texto \"{w.TextoValor?.text}\"\n");
            return sb.Length > 0 ? sb.ToString() : "nenhum cartão\n";
        }
    }
}
