using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Primario = ação principal (bloco vermelho); Secundario = comando sobre fundo translúcido;
    // Fantasma = item de menu sem moldura; Perigo = ação destrutiva; Icone = quadrado só com ícone.
    internal enum Estilo { Primario, Secundario, Fantasma, Perigo, Icone }

    // Componentes da UI nova (uGUI puro, montados em tempo de execução) e o motor de estados visuais:
    // cada controle tem cores de fundo/contorno/texto/marca por estado (normal, hover, pressionado,
    // ativo, desabilitado) interpoladas a cada frame. "Ativo" é a seleção, desenhada como nos menus
    // de rali: o item vira um bloco branco com texto escuro.
    internal static class Kit
    {
        const int CamadaUI = 5;

        // =====================================================================================
        // Motor de estados visuais
        // =====================================================================================

        internal sealed class Visual
        {
            public Selectable sel;
            public Image fundo, borda, marca;
            public Graphic texto, icone;
            public Color fN, fH, fP, fA, bN, bH, bA, tN, tH, tA, mN, mH, mA;
            public bool ativo;
            public Func<bool> foco;
            public RectTransform dica;
            public float h, p, a, d = 1f;
            // Som do clique (null = mudo) e se o controle encolhe ao ser pressionado.
            public Som.Efeito? som = Som.Efeito.Confirmar;
            public bool encolher;
            // Varredura: camada que cresce da esquerda para a direita no hover/seleção (motion graphics),
            // no lugar de só trocar a cor do fundo. "v" é o progresso dela, mais lento que o hover.
            public RectTransform varre;
            public Image varreImg;
            float v;
            bool _dentroAntes, _escalado;

            public void Passo(float k)
            {
                bool inter = sel.interactable;
                bool dentro = inter && (sel.isPointerInside || (foco != null && foco()));
                bool baixo = inter && sel.isPointerDown;
                if (dentro && !_dentroAntes) Som.Tocar(Som.Efeito.Hover);
                _dentroAntes = dentro;
                h = Mathf.Lerp(h, dentro ? 1f : 0f, k);
                p = Mathf.Lerp(p, baixo ? 1f : 0f, Mathf.Min(1f, k * 1.6f));
                a = Mathf.Lerp(a, ativo ? 1f : 0f, k);
                d = Mathf.Lerp(d, inter ? 1f : 0.4f, k);

                // Hover sobre o normal; a seleção cobre o hover (o bloco branco não "acinzenta"
                // com o mouse em cima); o pressionado só aparece fora da seleção.
                Color Mix(Color n, Color hv, Color pr, Color at)
                {
                    var c = Color.Lerp(n, hv, h);
                    c = Color.Lerp(c, at, a);
                    return Color.Lerp(c, pr, p * (1f - a));
                }
                if (varre != null)
                {
                    // O fundo fica no estado normal; a camada varre por cima com a cor de hover/seleção.
                    v = Mathf.Lerp(v, Mathf.Max(dentro ? 1f : 0f, ativo ? 1f : 0f), 1f - Mathf.Exp(-Anim.Dt / 0.075f));
                    float e = 1f - (1f - v) * (1f - v) * (1f - v);
                    varre.anchorMax = new Vector2(e, 1f);
                    var cv = Color.Lerp(Color.Lerp(fH, fA, a), fP, p * (1f - a));
                    varreImg.color = cv;
                    if (fundo != null) fundo.color = fN;
                }
                else if (fundo != null) fundo.color = Mix(fN, fH, fP, fA);
                if (borda != null) { var bc = Mix(bN, bH, bH, bA); bc.a *= d; borda.color = bc; }
                if (marca != null)
                {
                    marca.color = Mix(mN, mH, mH, mA);
                    // A barra vermelha cresce do centro para as pontas.
                    float m = Mathf.Max(h, a);
                    marca.rectTransform.localScale = new Vector3(1f, 1f - (1f - m) * (1f - m), 1f);
                }
                var tc = Mix(tN, tH, tH, tA);
                tc.a *= d;
                if (texto != null) texto.color = tc;
                if (icone != null) icone.color = tc;

                // Pressionar afunda o botão um pouco (escala não briga com os layout groups).
                // Só durante o clique (e uma vez para voltar a 1): fora disso a escala fica livre para as
                // animações de entrada.
                if (encolher && (p > 0.001f || _escalado))
                {
                    sel.transform.localScale = Vector3.one * (1f - 0.03f * p);
                    _escalado = p > 0.001f;
                }

                if (dica != null)
                {
                    var g = dica.GetComponent<CanvasGroup>();
                    if (g != null) g.alpha = Mathf.Clamp01((h - 0.35f) / 0.65f);
                }
            }
        }

        static readonly List<Visual> _visuais = new();
        static readonly List<(GameObject dono, Action acao)> _aoQuadro = new();

        // Chamado a cada frame pelo mod.
        internal static void AtualizarVisuais()
        {
            float k = 1f - Mathf.Exp(-Anim.Dt / 0.04f);
            for (int i = _visuais.Count - 1; i >= 0; i--)
            {
                var v = _visuais[i];
                try
                {
                    if (v.sel == null || v.sel.WasCollected) { _visuais.RemoveAt(i); continue; }
                    if (!v.sel.gameObject.activeInHierarchy) continue;
                    v.Passo(k);
                }
                catch { _visuais.RemoveAt(i); }
            }
            for (int i = _aoQuadro.Count - 1; i >= 0; i--)
            {
                var (dono, acao) = _aoQuadro[i];
                try
                {
                    if (dono == null || dono.WasCollected) { _aoQuadro.RemoveAt(i); continue; }
                    if (dono.activeInHierarchy) acao();
                }
                catch { _aoQuadro.RemoveAt(i); }
            }
        }

        // Atualização contínua enquanto "dono" estiver ativo (relógio, barra de status).
        internal static void AoQuadro(GameObject dono, Action acao) => _aoQuadro.Add((dono, acao));

        static Visual Registrar(Visual v)
        {
            v.sel.transition = Selectable.Transition.None; // o motor acima cuida das cores
            var nav = v.sel.navigation; nav.mode = Navigation.Mode.None; v.sel.navigation = nav;
            _visuais.Add(v);
            var botao = v.sel.TryCast<Button>();
            if (botao != null)
            {
                v.encolher = true;
                botao.onClick.AddListener((UnityAction)(System.Action)(() => { if (v.som.HasValue) Som.Tocar(v.som.Value); }));
            }
            v.Passo(1f);
            return v;
        }

        static readonly Color Transparente = new Color(0, 0, 0, 0);

        // =====================================================================================
        // Base
        // =====================================================================================

        internal static RectTransform Novo(string nome, Transform pai)
        {
            var go = new GameObject(nome) { layer = CamadaUI };
            var rt = go.AddComponent<RectTransform>();
            if (pai != null) rt.SetParent(pai, false);
            return rt;
        }

        internal static RectTransform Encher(RectTransform rt, float margem = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margem, margem); rt.offsetMax = new Vector2(-margem, -margem);
            return rt;
        }

        internal static RectTransform Ancorar(RectTransform rt, Vector2 ancora, Vector2 pos, Vector2 tam)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = ancora;
            rt.anchoredPosition = pos;
            rt.sizeDelta = tam;
            return rt;
        }

        // Fundo chapado (sprite sólido ou nenhum). "contorno": linha de 1 px no mesmo retângulo.
        internal static Image Fundo(Component alvo, Sprite sprite, Color cor, float ppuMult = 1f, bool contorno = false)
        {
            var img = alvo.gameObject.GetComponent<Image>() ?? alvo.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = ppuMult;
            img.color = cor;
            if (contorno) Contorno(alvo, Theme.Linha);
            return img;
        }

        // Contorno de 1 px (filho que o layout ignora, desenhado antes do conteúdo).
        internal static Image Contorno(Component alvo, Color cor, Sprite sprite = null)
        {
            var existente = alvo.transform.Find("Contorno");
            if (existente != null) { var e = existente.GetComponent<Image>(); e.color = cor; return e; }
            var rt = Encher(Novo("Contorno", alvo.transform));
            rt.SetAsFirstSibling();
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite ?? Theme.Borda;
            img.type = Image.Type.Sliced;
            img.color = cor;
            img.raycastTarget = false;
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return img;
        }

        // Painel reto, com contorno opcional.
        internal static RectTransform Painel(Transform pai, string nome, Color fundo, Color? borda = null)
        {
            var rt = Novo(nome, pai);
            Fundo(rt, Theme.Solido, fundo);
            if (borda.HasValue) Contorno(rt, borda.Value);
            return rt;
        }

        // Faixa colorida no topo do painel (cartões do roadmap do Assetto Corsa EVO).
        internal static Image FaixaTopo(Component alvo, Color cor, float altura = 3f)
        {
            var rt = Novo("FaixaTopo", alvo.transform);
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, altura);
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = Fundo(rt, null, cor);
            img.raycastTarget = false;
            return img;
        }

        // Camada da varredura: ancorada à esquerda, atrás do conteúdo; o motor anima a largura.
        static (RectTransform, Image) Varredura(RectTransform alvo)
        {
            var rt = Novo("Varre", alvo);
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rt.SetSiblingIndex(alvo.Find("Contorno") != null ? 1 : 0);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Theme.Solido; img.type = Image.Type.Sliced;
            img.color = Transparente;
            img.raycastTarget = false;
            return (rt, img);
        }

        // Barra vertical à esquerda (marca de hover/seleção dos itens de menu).
        internal static Image Marca(Component alvo, Color cor, float largura = 3f)
        {
            var rt = Novo("Marca", alvo.transform);
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(largura, 0);
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = Fundo(rt, null, cor);
            img.raycastTarget = false;
            return img;
        }

        internal static LayoutElement Tam(Component c, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; if (flexW < 0) le.flexibleWidth = 0; }
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; if (flexH < 0) le.flexibleHeight = 0; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        internal static VerticalLayoutGroup Coluna(Component c, float espaco = 12, int pad = 0, TextAnchor alin = TextAnchor.UpperLeft, bool esticarLargura = true)
        {
            var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = espaco;
            g.padding = Margens(pad, pad, pad, pad);
            g.childAlignment = alin;
            g.childControlWidth = esticarLargura; g.childControlHeight = true;
            g.childForceExpandWidth = esticarLargura; g.childForceExpandHeight = false;
            return g;
        }

        internal static HorizontalLayoutGroup Linha(Component c, float espaco = 12, int pad = 0, TextAnchor alin = TextAnchor.MiddleLeft)
        {
            var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = espaco;
            g.padding = Margens(pad, pad, pad, pad);
            g.childAlignment = alin;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            return g;
        }

        internal static void Pad(LayoutGroup g, int esq, int dir, int cima, int baixo) => g.padding = Margens(esq, dir, cima, baixo);

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        delegate void SetIntFn(IntPtr self, int valor);
        static SetIntFn _setRight;

        // O construtor RectOffset(l, r, t, b) e o setter "right" foram removidos do build (stripping).
        internal static RectOffset Margens(int esq, int dir, int cima, int baixo)
        {
            var ro = new RectOffset();
            ro.left = esq; ro.top = cima; ro.bottom = baixo;
            _setRight ??= Il2CppInterop.Runtime.IL2CPP.ResolveICall<SetIntFn>("UnityEngine.RectOffset::set_right");
            _setRight(ro.Pointer, dir);
            return ro;
        }

        internal static RectTransform Espaco(Transform pai, float h = -1, float w = -1, bool flex = false)
        {
            var rt = Novo("Espaco", pai);
            bool horizontal = pai.GetComponent<HorizontalLayoutGroup>() != null;
            Tam(rt, w, h, flex && horizontal ? 1 : -1, flex && !horizontal ? 1 : -1);
            return rt;
        }

        internal static Image Divisoria(Transform pai)
        {
            var rt = Novo("Divisoria", pai);
            Tam(rt, -1, 1, 1);
            return Fundo(rt, null, Theme.Linha);
        }

        internal static Image DivisoriaV(Transform pai, float altura = 24)
        {
            var rt = Novo("DivisoriaV", pai);
            Tam(rt, 1, altura);
            return Fundo(rt, null, Theme.Linha);
        }

        // =====================================================================================
        // Texto
        // =====================================================================================

        // "forte": negrito. Título (forte e 20 px ou mais): negrito itálico em maiúsculas,
        // o "LARGE TILE TITLE" do guia de estilo do DiRT Rally 2.0.
        internal static Text Texto(Transform pai, string s, int tam, Color cor, bool forte = false, TextAnchor alin = TextAnchor.MiddleLeft)
        {
            bool titulo = forte && tam >= 20;
            var rt = Novo(titulo ? "Titulo" : "Texto", pai);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Theme.Display;
            t.fontSize = tam;
            t.fontStyle = titulo ? FontStyle.BoldAndItalic : forte ? FontStyle.Bold : FontStyle.Normal;
            t.color = cor;
            t.alignment = alin;
            t.text = titulo ? (s ?? "").ToUpperInvariant() : (s ?? "");
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            t.lineSpacing = 1.05f;
            return t;
        }

        // Rótulo: maiúsculas espaçadas, pequeno e esmaecido.
        internal static Text Rotulo(Transform pai, string s, int tam = Theme.TamRotulo, Color? cor = null)
        {
            var t = Texto(pai, Theme.Rotular(s), tam, cor ?? Theme.TextoMudo);
            t.gameObject.name = "Rotulo";
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        // Migalha de navegação em vermelho itálico ("/ MAIN MENU" do DiRT, "/Settings/Controls" do ACE).
        internal static Text Trilha(Transform pai, string s, int tam = 13)
        {
            var t = Texto(pai, "/ " + (s ?? "").ToUpperInvariant(), tam, Theme.AcentoVivo);
            t.fontStyle = FontStyle.BoldAndItalic;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.gameObject.name = "Trilha";
            return t;
        }

        // Parágrafo em fonte de texto corrido (legível em blocos longos).
        internal static Text Paragrafo(Transform pai, string s, int tam = Theme.TamCorpo, Color? cor = null)
        {
            var t = Texto(pai, s, tam, cor ?? Theme.TextoDim);
            t.font = Theme.TextoCorrido;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.2f;
            t.gameObject.name = "Paragrafo";
            return t;
        }

        internal static Text Icone(Transform pai, string glifo, int tam, Color cor)
        {
            var t = Texto(pai, glifo, tam, cor, false, TextAnchor.MiddleCenter);
            if (Theme.FonteIcones != null) t.font = Theme.FonteIcones;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Tam(t, tam + 4, tam + 4);
            t.gameObject.name = "Icone";
            return t;
        }

        // =====================================================================================
        // Botões
        // =====================================================================================

        internal sealed class Botao
        {
            public GameObject go;
            public Button btn;
            public Image fundo;
            public Text rotulo, icone;
            public LayoutElement le;
            public Estilo estilo;
            public Visual visual;
            public bool maiusculas;
            public bool navega; // troca de tela cheia: o clique original só roda com a tela coberta

            public void Rotulo(string s)
            {
                if (rotulo == null) return;
                s ??= "";
                if (maiusculas) s = s.Trim().TrimEnd('.', '…', ' ').ToUpperInvariant();
                if (rotulo.text != s) rotulo.text = s;
            }
            public void Visivel(bool v) { if (go.activeSelf != v) go.SetActive(v); }
            public void Interagivel(bool v) { if (btn.interactable != v) btn.interactable = v; }
            public void Selecionado(bool sel) { if (visual != null) visual.ativo = sel; }
        }

        static void Paleta(Visual v, Estilo e)
        {
            var branco = Color.white;
            v.mN = v.mH = v.mA = Transparente;
            switch (e)
            {
                case Estilo.Primario:
                    v.fN = Theme.Acento; v.fH = Theme.AcentoHover; v.fP = Theme.AcentoPress; v.fA = Theme.AcentoHover;
                    v.bN = v.bH = v.bA = Transparente;
                    v.tN = v.tH = v.tA = branco;
                    break;
                case Estilo.Fantasma:
                    v.fN = Transparente; v.fH = Theme.Veu(0.08f); v.fP = Theme.Veu(0.14f); v.fA = branco;
                    v.bN = v.bH = v.bA = Transparente;
                    v.tN = Theme.TextoDim; v.tH = branco; v.tA = Theme.TextoInverso;
                    v.mH = Theme.AcentoVivo; v.mA = Theme.Acento;
                    break;
                case Estilo.Perigo:
                    v.fN = Transparente; v.fH = Theme.PerigoFundo; v.fP = Theme.Tingir(Theme.PerigoFundo, Color.black, 0.2f); v.fA = v.fH;
                    v.bN = Theme.Tingir(Theme.Painel, Theme.Perigo, 0.55f); v.bH = Theme.Perigo; v.bA = v.bH;
                    v.tN = Theme.PerigoTexto; v.tH = branco; v.tA = branco;
                    break;
                default: // Secundario / Icone
                    v.fN = Theme.Veu(0.05f); v.fH = Theme.Veu(0.12f); v.fP = Theme.Veu(0.2f); v.fA = branco;
                    v.bN = Theme.LinhaForte; v.bH = Theme.TextoMudo; v.bA = branco;
                    v.tN = Theme.TextoDim; v.tH = branco; v.tA = Theme.TextoInverso;
                    break;
            }
        }

        internal static int TamanhoDoRotulo(Estilo e) => e == Estilo.Primario ? 16 : e == Estilo.Fantasma ? 15 : 13;

        internal static Botao NovoBotao(Transform pai, string texto, string icone, Estilo estilo, Action aoClicar,
            float altura = 40, int tamFonte = 13, TextAnchor alin = TextAnchor.MiddleLeft)
        {
            var rt = Novo("Botao", pai);
            var b = new Botao { go = rt.gameObject, estilo = estilo, maiusculas = estilo != Estilo.Fantasma };
            b.fundo = Fundo(rt, Theme.Solido, Color.white);
            var borda = Contorno(rt, Theme.Linha);
            Image marca = estilo == Estilo.Fantasma ? Marca(rt, Transparente) : null;
            b.btn = rt.gameObject.AddComponent<Button>();
            b.btn.targetGraphic = b.fundo;

            bool soIcone = string.IsNullOrEmpty(texto);
            var linha = Linha(rt, 10, 0, soIcone ? TextAnchor.MiddleCenter : alin);
            int padH = soIcone ? 0 : texto.Length <= 2 ? 6 : 14;
            Pad(linha, padH, padH, 0, 0);

            if (!soIcone && texto.Length > 2) tamFonte = TamanhoDoRotulo(estilo);
            if (!string.IsNullOrEmpty(icone)) b.icone = Icone(rt, icone, soIcone ? Mathf.RoundToInt(altura * 0.4f) : tamFonte + 2, Theme.TextoDim);
            if (!soIcone)
            {
                // O estilo decide o tamanho (a ação principal 16, comandos 13, itens de menu 15), para
                // o mesmo tipo de botão ser igual em todas as telas. Rótulos de 1-2 caracteres (teclado
                // numérico, animações) mantêm o tamanho pedido.
                b.rotulo = Texto(rt, "", tamFonte, Theme.TextoDim, false, alin);
                b.rotulo.horizontalOverflow = HorizontalWrapMode.Overflow;
                // Ação principal em negrito itálico ("RESUME CHAMPIONSHIP"); comandos em negrito reto.
                b.rotulo.fontStyle = estilo == Estilo.Primario ? FontStyle.BoldAndItalic
                    : estilo == Estilo.Fantasma ? FontStyle.Normal : FontStyle.Bold;
                Tam(b.rotulo, -1, -1, 1);
                b.Rotulo(texto);
            }
            // Ação principal: ícone de seta à direita.
            if (estilo == Estilo.Primario && b.icone != null) b.icone.transform.SetAsLastSibling();

            b.le = Tam(rt, soIcone ? altura : -1, altura);
            b.le.flexibleWidth = 0;
            if (aoClicar != null) b.btn.onClick.AddListener((UnityAction)(System.Action)aoClicar);

            var v = new Visual { sel = b.btn, fundo = b.fundo, borda = borda, marca = marca, texto = b.rotulo, icone = b.icone };
            if (!soIcone) (v.varre, v.varreImg) = Varredura(rt);
            if (icone == Ico.Voltar || icone == Ico.Fechar) v.som = Som.Efeito.Cancelar; // sair de uma tela soa diferente
            Paleta(v, estilo);
            b.visual = Registrar(v);
            return b;
        }

        // Linha de lista: número grande em itálico à esquerda (os "01 02 03" das etapas do DiRT),
        // nome + meta. Hover acende a marca vermelha; seleção inverte a linha para branco.
        internal sealed class ItemLista
        {
            public Botao b;
            public Text indice, nome, meta;
            public Image barra;
            public void Selecionado(bool s)
            {
                b.Selecionado(s);
                indice.color = s ? Theme.Acento : Theme.TextoMudo;
                meta.color = s ? Theme.TextoInversoMudo : Theme.TextoMudo;
            }
        }

        internal static ItemLista NovoItemLista(Transform pai, int numero, string nome, string meta, Action aoClicar)
        {
            var rt = Novo("Item", pai);
            var fundo = Fundo(rt, Theme.Solido, Color.white);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = fundo;
            if (aoClicar != null) btn.onClick.AddListener((UnityAction)(System.Action)aoClicar);
            var hl = Linha(rt, 14);
            Pad(hl, 16, 14, 8, 8);
            var le = Tam(rt, -1, 56);

            var item = new ItemLista();
            item.barra = Marca(rt, Transparente, 4);
            item.indice = Texto(rt, numero.ToString("00"), 22, Theme.TextoMudo, false, TextAnchor.MiddleLeft);
            item.indice.fontStyle = FontStyle.BoldAndItalic;
            item.indice.horizontalOverflow = HorizontalWrapMode.Overflow;
            Tam(item.indice, 34, 36);

            var corpo = Novo("Corpo", rt);
            Coluna(corpo, 3, 0, TextAnchor.MiddleLeft);
            Tam(corpo, -1, -1, 1);
            item.nome = Texto(corpo, nome, 15, Theme.TextoDim, true);
            item.nome.horizontalOverflow = HorizontalWrapMode.Wrap; // nomes longos cortam na palavra, sem vazar
            Tam(item.nome, -1, 19);
            item.meta = Texto(corpo, meta ?? "", 11, Theme.TextoMudo);
            Tam(item.meta, -1, 14);

            var v = new Visual { sel = btn, fundo = fundo, marca = item.barra, texto = item.nome };
            (v.varre, v.varreImg) = Varredura(rt);
            v.fN = Theme.Cartao; v.fH = Theme.Clarear(Theme.Cartao, 0.08f); v.fP = Theme.Clarear(Theme.Cartao, 0.15f); v.fA = Color.white;
            v.bN = v.bH = v.bA = Transparente;
            v.tN = Theme.TextoDim; v.tH = Color.white; v.tA = Theme.TextoInverso;
            v.mN = Transparente; v.mH = Theme.AcentoVivo; v.mA = Theme.Acento;
            item.b = new Botao { go = rt.gameObject, btn = btn, fundo = fundo, rotulo = item.nome, le = le, estilo = Estilo.Secundario, visual = Registrar(v) };
            return item;
        }

        // Item de dock: ícone num quadrado de 48, rótulo aparece como dica acima no hover.
        // "canto": marca curta no canto inferior direito, para diferenciar botões de mesmo ícone.
        internal static Botao BotaoDock(Transform pai, string icone, string legenda, Action aoClicar, string canto = null, float altura = 48)
        {
            var b = NovoBotao(pai, null, icone, Estilo.Secundario, aoClicar, altura);
            b.icone.fontSize = 20;
            b.visual.dica = Dica(b.go.GetComponent<RectTransform>(), legenda);
            if (!string.IsNullOrEmpty(canto))
            {
                var t = Texto(b.go.transform, canto, 10, Theme.TextoDim, true, TextAnchor.LowerRight);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Encher(t.rectTransform, 4);
            }
            return b;
        }

        // Dica (tooltip): bloco branco com texto escuro, acima do controle, ou abaixo dele para
        // controles no topo da tela (cabeçalho, barra do ALT), onde acima sairia da tela.
        // "direita": ao lado do controle (trilhos na borda esquerda da tela).
        internal static RectTransform Dica(RectTransform alvo, string texto, bool abaixo = false, bool direita = false)
        {
            var rt = Painel(alvo, "Dica", Color.white);
            if (direita)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(10, 0);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, abaixo ? 0 : 1); rt.pivot = new Vector2(0.5f, abaixo ? 1 : 0);
                rt.anchoredPosition = new Vector2(0, abaixo ? -8 : 8);
            }
            var t = Texto(rt, texto, 12, Theme.TextoInverso, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Encher(t.rectTransform);
            rt.sizeDelta = new Vector2(t.preferredWidth + 20, 26);
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0; g.blocksRaycasts = false; g.interactable = false;
            return rt;
        }

        // =====================================================================================
        // Caixa de marcação
        // =====================================================================================

        internal sealed class Chave
        {
            public GameObject go;
            public Button btn;
            public Text rotulo;
            public Image trilho;          // contorno da caixa
            public RectTransform botao;   // marca interna
            public Image caixaFundo;
            public Visual visual;
            public Color corRotulo = Theme.TextoDim;
            public bool estado;
            float _pos = -1;

            public void Visivel(bool v) { if (go.activeSelf != v) go.SetActive(v); }
            public void Rotulo(string s) { if (rotulo.text != s) rotulo.text = s ?? ""; }
            public void Interagivel(bool v) { if (btn.interactable != v) btn.interactable = v; }

            // Ligada: caixa cheia de vermelho com o visto branco.
            public void Definir(bool ligado, bool animar = true)
            {
                estado = ligado;
                float alvo = ligado ? 1f : 0f;
                _pos = !animar || _pos < 0 ? alvo : Mathf.MoveTowards(_pos, alvo, Time.unscaledDeltaTime * 9f);
                float e = Anim.Suave(_pos);
                trilho.color = Color.Lerp(Theme.TextoMudo, Theme.Acento, e);
                caixaFundo.color = Theme.Alfa(Theme.Acento, e);
                botao.GetComponent<Text>().color = Theme.Alfa(Color.white, e);
                botao.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, e);
            }
        }

        internal static Chave NovaChave(Transform pai, string rotulo, Action aoClicar, bool moldura = false)
        {
            var rt = Novo("Chave", pai);
            var c = new Chave { go = rt.gameObject };
            var fundo = Fundo(rt, Theme.Solido, Color.white);
            var borda = Contorno(rt, Theme.Linha);
            c.btn = rt.gameObject.AddComponent<Button>();
            c.btn.targetGraphic = fundo;

            var linha = Linha(rt, 12);
            Pad(linha, 12, 12, 0, 0);

            var caixa = Novo("Caixa", rt);
            Tam(caixa, 16, 16);
            c.caixaFundo = Fundo(caixa, null, Transparente);
            var cont = Encher(Novo("Contorno", caixa));
            c.trilho = cont.gameObject.AddComponent<Image>();
            c.trilho.sprite = Theme.Borda; c.trilho.type = Image.Type.Sliced; c.trilho.raycastTarget = false;
            var marca = Icone(caixa, Ico.Marca, 11, Color.white);
            marca.GetComponent<LayoutElement>().ignoreLayout = true;
            c.botao = Encher(marca.rectTransform);

            c.rotulo = Texto(rt, rotulo, 15, Theme.TextoDim);
            c.rotulo.verticalOverflow = VerticalWrapMode.Overflow;
            Tam(c.rotulo, -1, -1, 1);

            Tam(rt, -1, 40);
            if (aoClicar != null) c.btn.onClick.AddListener((UnityAction)(System.Action)aoClicar);

            // A seleção da chave é a própria caixa vermelha: a linha não inverte para branco.
            var v = new Visual { sel = c.btn, fundo = fundo, borda = borda, texto = c.rotulo };
            v.mN = v.mH = v.mA = Transparente;
            // Com moldura (chave solta numa coluna ou grade) ela se parece com os botões ao lado;
            // sem moldura (linha de formulário) a própria linha é o alvo.
            v.fN = moldura ? Theme.Veu(0.05f) : Transparente;
            v.fH = Theme.Veu(0.1f); v.fP = Theme.Veu(0.16f); v.fA = v.fN;
            v.bN = v.bA = moldura ? Theme.LinhaForte : Transparente;
            v.bH = moldura ? Theme.TextoMudo : Transparente;
            v.tN = Theme.TextoDim; v.tH = Color.white; v.tA = Theme.TextoDim;
            c.visual = Registrar(v);
            c.Definir(false, false);
            return c;
        }

        // =====================================================================================
        // Campo, slider, lista
        // =====================================================================================

        internal static InputField Campo(Transform pai, string placeholder, Action<string> aoMudar, Action<string> aoConfirmar, string icone = Ico.Busca)
        {
            var rt = Novo("Campo", pai);
            var fundo = Fundo(rt, Theme.Solido, Theme.Campo);
            var borda = Contorno(rt, Theme.LinhaForte);
            Tam(rt, -1, 40);

            var linha = Linha(rt, 10);
            Pad(linha, 14, 14, 0, 0);
            linha.childControlHeight = true; linha.childForceExpandHeight = true;
            if (icone != null) Icone(rt, icone, 14, Theme.TextoMudo);

            var area = Novo("Area", rt);
            Tam(area, -1, -1, 1, 1);
            var ph = Texto(area, placeholder, 14, Theme.TextoMudo);
            ph.fontStyle = FontStyle.Italic;
            Encher(ph.rectTransform);
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;
            var txt = Texto(area, "", 14, Theme.Texto);
            Encher(txt.rectTransform);
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            area.gameObject.AddComponent<RectMask2D>();

            var campo = rt.gameObject.AddComponent<InputField>();
            campo.textComponent = txt;
            campo.placeholder = ph;
            campo.caretColor = Theme.AcentoVivo;
            campo.selectionColor = Theme.Alfa(Theme.Acento, 0.45f);
            campo.customCaretColor = true;
            campo.caretWidth = 2;
            campo.lineType = InputField.LineType.SingleLine;
            campo.targetGraphic = fundo;

            var v = new Visual { sel = campo, fundo = fundo, borda = borda, foco = () => campo.isFocused };
            v.fN = v.fH = v.fP = v.fA = Theme.Campo;
            v.bN = Theme.LinhaForte; v.bH = Theme.TextoDim; v.bA = v.bH;
            v.tN = v.tH = v.tA = Theme.Texto;
            v.mN = v.mH = v.mA = Transparente;
            Registrar(v);

            if (aoMudar != null) campo.onValueChanged.AddListener((UnityAction<string>)aoMudar);
            if (aoConfirmar != null) campo.onEndEdit.AddListener((UnityAction<string>)aoConfirmar);
            return campo;
        }

        // Slider do ACE: trilho escuro de 4 px, preenchimento vermelho e alça branca reta.
        internal static Slider NovoSlider(Transform pai, float largura, Action<float> aoMudar)
        {
            var rt = Novo("Slider", pai);
            Tam(rt, largura, 32);

            RectTransform Faixa(string nome, Transform p, float alt)
            {
                var f = Novo(nome, p);
                f.anchorMin = new Vector2(0, 0.5f); f.anchorMax = new Vector2(1, 0.5f); f.pivot = new Vector2(0.5f, 0.5f);
                f.sizeDelta = new Vector2(0, alt);
                return f;
            }

            Fundo(Faixa("Trilho", rt, 4), null, Theme.Campo);
            var areaPreench = Faixa("AreaPreench", rt, 4);
            var preench = Novo("Preench", areaPreench);
            preench.sizeDelta = Vector2.zero;
            Fundo(preench, null, Theme.Acento);

            var areaAlca = Faixa("AreaAlca", rt, 20);
            areaAlca.sizeDelta = new Vector2(-6, 20);
            var alca = Novo("Alca", areaAlca);
            alca.sizeDelta = new Vector2(6, 0);
            var imgAlca = Fundo(alca, null, Color.white);

            var s = rt.gameObject.AddComponent<Slider>();
            s.fillRect = preench;
            s.handleRect = alca;
            s.targetGraphic = imgAlca;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = 0; s.maxValue = 1;
            var v = new Visual { sel = s, fundo = imgAlca };
            v.fN = Theme.Clarear(Theme.TextoDim, 0.5f); v.fH = Color.white; v.fP = Color.white; v.fA = v.fN;
            v.bN = v.bH = v.bA = Transparente;
            v.mN = v.mH = v.mA = Transparente;
            Registrar(v);
            if (aoMudar != null) s.onValueChanged.AddListener((UnityAction<float>)aoMudar);
            return s;
        }

        // Lista com rolagem e barra fina à direita.
        internal static RectTransform Lista(Transform pai, out ScrollRect scroll, float espaco = 4)
        {
            var rt = Novo("Lista", pai);
            Tam(rt, -1, -1, 1, 1);
            scroll = rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.1f;

            var viewport = Novo("Viewport", rt);
            Encher(viewport);
            viewport.offsetMax = new Vector2(-10, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            Fundo(viewport, null, Transparente).raycastTarget = true;

            var conteudo = Novo("Conteudo", viewport);
            conteudo.anchorMin = new Vector2(0, 1); conteudo.anchorMax = new Vector2(1, 1); conteudo.pivot = new Vector2(0.5f, 1);
            conteudo.offsetMin = conteudo.offsetMax = Vector2.zero;
            Coluna(conteudo, espaco);
            conteudo.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = conteudo;

            // barra de rolagem de 6 px (fina, mas ainda dá para arrastar)
            var barra = Novo("Rolagem", rt);
            barra.anchorMin = new Vector2(1, 0); barra.anchorMax = new Vector2(1, 1); barra.pivot = new Vector2(1, 0.5f);
            barra.sizeDelta = new Vector2(6, 0);
            Fundo(barra, null, Theme.Linha);
            var area = Encher(Novo("Area", barra));
            var alca = Encher(Novo("Alca", area));
            var imgAlca = Fundo(alca, null, Theme.TextoMudo);
            var sb = barra.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = alca;
            sb.targetGraphic = imgAlca;
            sb.direction = Scrollbar.Direction.BottomToTop;
            var nav = sb.navigation; nav.mode = Navigation.Mode.None; sb.navigation = nav;
            sb.transition = Selectable.Transition.None;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return conteudo;
        }

        // =====================================================================================
        // Painéis compostos
        // =====================================================================================

        // Cartão: bloco translúcido sem moldura, rótulo no topo e conteúdo em coluna.
        internal static RectTransform Cartao(Transform pai, string titulo = null, int pad = 16, float espaco = 10)
        {
            var rt = Painel(pai, "Cartao", Theme.Cartao);
            Coluna(rt, espaco, pad);
            if (!string.IsNullOrEmpty(titulo)) Tam(Rotulo(rt, titulo), -1, 16);
            return rt;
        }

        // Modal: fundo escurecido + janela reta com faixa vermelha no topo.
        internal static RectTransform Modal(Transform pai, float largura, out GameObject raiz)
        {
            var overlay = Encher(Novo("Modal", pai));
            Fundo(overlay, null, Theme.Overlay).raycastTarget = true;
            raiz = overlay.gameObject;
            var card = Ancorar(Novo("Janela", overlay), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(largura, 0));
            Fundo(card, Theme.Solido, Theme.Elevado);
            Contorno(card, Theme.Linha);
            FaixaTopo(card, Theme.Acento, 4);
            var col = Coluna(card, 16);
            Pad(col, 28, 28, 28, 28);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return card;
        }

        // Fundo de tela: cor base, foto opcional escurecida, vinheta e riscas diagonais finas.
        internal static RawImage Atmosfera(Transform pai, bool comFoto = false)
        {
            Fundo(pai.GetComponent<RectTransform>(), null, Theme.PaginaFundo);
            RawImage foto = null;
            if (comFoto)
            {
                foto = Encher(Novo("Foto", pai)).gameObject.AddComponent<RawImage>();
                foto.raycastTarget = false;
                foto.color = new Color(0.5f, 0.5f, 0.52f, 1f);
                // Zoom lento e contínuo na foto (Ken Burns): o fundo nunca fica parado.
                var frt = foto.rectTransform;
                AoQuadro(foto.gameObject, () => frt.localScale = Vector3.one * (1.06f + 0.04f * Mathf.Sin(Time.unscaledTime * 0.07f)));
            }
            var vin = Encher(Novo("Vinheta", pai)).gameObject.AddComponent<RawImage>();
            vin.texture = Theme.Vinheta; vin.raycastTarget = false;

            // Riscas só na metade direita, sumindo para a esquerda (o fundo do menu do ACE).
            var lrt = Novo("Listras", pai);
            lrt.anchorMin = new Vector2(0.45f, 0); lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var listras = lrt.gameObject.AddComponent<RawImage>();
            listras.texture = Theme.Listras; listras.raycastTarget = false;
            listras.color = Theme.Veu(0.025f);
            AoQuadro(lrt.gameObject, () =>
            {
                var r = lrt.rect;
                // As riscas derivam devagar para a direita.
                if (r.width > 0) listras.uvRect = new Rect(-Time.unscaledTime * 0.18f, 0, r.width / 24f, r.height / 24f);
            });
            return foto;
        }

        // Rótulo da área de prévia: migalha vermelha no canto superior esquerdo.
        internal static void Visor(RectTransform alvo, string rotuloCima, string rotuloBaixo)
        {
            void Marcar(string s, Vector2 ancora, Vector2 pos)
            {
                var t = Trilha(alvo, s, 12);
                t.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Ancorar(t.rectTransform, ancora, pos, new Vector2(400, 16));
            }
            if (rotuloCima != null) Marcar(rotuloCima, new Vector2(0, 1), new Vector2(20, -16));
            if (rotuloBaixo != null) Marcar(rotuloBaixo, new Vector2(0, 0), new Vector2(20, 16));
        }

        // Cabeçalho: barra vermelha inclinada + título em negrito itálico, migalha vermelha embaixo
        // ("credito" vira "/ CREDITO"), relógio e slot de ações à direita. Uma linha fina separa do conteúdo.
        internal static RectTransform Cabecalho(Transform pai, string titulo, string credito, out Text textoCredito, out RectTransform acoes)
        {
            var rt = Novo("Cabecalho", pai);
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 72);
            var hl = Linha(rt, 16);
            Pad(hl, 28, 28, 12, 12);

            var marca = Novo("Marca", rt);
            Tam(marca, 14, 44);
            var barra = Ancorar(Novo("Barra", marca), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7, 46));
            barra.localEulerAngles = new Vector3(0, 0, -14f); // inclinada como "/"
            Fundo(barra, null, Theme.Acento).raycastTarget = false;

            var ident = Novo("Identidade", rt);
            Coluna(ident, 2, 0, TextAnchor.MiddleLeft);
            Tam(ident, 520, 48);
            var tt = Texto(ident, titulo, 28, Theme.Texto, true);
            tt.horizontalOverflow = HorizontalWrapMode.Overflow;
            Tam(tt, -1, 30);
            textoCredito = Texto(ident, string.IsNullOrEmpty(credito) ? "" : "/ " + credito.ToUpperInvariant(), 12, Theme.AcentoVivo);
            textoCredito.fontStyle = FontStyle.BoldAndItalic;
            textoCredito.horizontalOverflow = HorizontalWrapMode.Overflow;
            Tam(textoCredito, -1, 15);

            Espaco(rt, -1, -1, true);
            Relogio(rt);

            acoes = Novo("Acoes", rt);
            Linha(acoes, 6, 0, TextAnchor.MiddleRight);
            acoes.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Tam(acoes, -1, 40);

            var lin = Novo("Linha", rt);
            lin.anchorMin = new Vector2(0, 0); lin.anchorMax = new Vector2(1, 0); lin.pivot = new Vector2(0.5f, 0);
            lin.offsetMin = new Vector2(28, 0); lin.offsetMax = new Vector2(-28, 1);
            lin.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fundo(lin, null, Theme.Linha).raycastTarget = false;
            return rt;
        }

        // Hora local em número grande e reto, separado das ações por uma divisória.
        static void Relogio(Transform pai)
        {
            var rt = Novo("Relogio", pai);
            Linha(rt, 14, 0, TextAnchor.MiddleRight);
            rt.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Tam(rt, -1, 40);
            var hora = Texto(rt, "00:00", 20, Theme.TextoDim, true, TextAnchor.MiddleRight);
            hora.fontStyle = FontStyle.Bold;
            hora.horizontalOverflow = HorizontalWrapMode.Overflow;
            DivisoriaV(rt, 24);
            AoQuadro(rt.gameObject, () =>
            {
                var s = DateTime.Now.ToString("HH:mm");
                if (hora.text != s) hora.text = s;
            });
        }

        // Faixa de status no rodapé (a barra inferior do ACE): texto esmaecido numa linha, à esquerda.
        // Retorna a faixa; o texto é o primeiro filho.
        internal static RectTransform LinhaDeStatus(Transform pai, Func<string> texto)
        {
            var rt = Novo("Status", pai);
            var t = Texto(rt, "", 12, Theme.TextoMudo);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            Encher(t.rectTransform);
            string atual = null;
            AoQuadro(rt.gameObject, () =>
            {
                var s = (texto() ?? "").Replace("\n", "  ");
                if (s != atual) { atual = s; t.text = s; }
            });
            return rt;
        }

        // Rodapé de tela inteira: faixa escura de 36 px no pé da tela com a linha de status.
        internal static RectTransform Rodape(Transform pai, Func<string> texto, float margem = 28)
        {
            var r = Novo("Rodape", pai);
            r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(1, 0); r.pivot = new Vector2(0.5f, 0);
            r.offsetMin = Vector2.zero; r.offsetMax = new Vector2(0, AlturaRodape);
            Fundo(r, null, Theme.Barra);
            var s = LinhaDeStatus(r, texto);
            Encher(s);
            s.offsetMin = new Vector2(margem, 0); s.offsetMax = new Vector2(-margem, 0);
            return r;
        }

        internal const float AlturaRodape = 36;

        // Indicador de etapas: "01 VEÍCULO — 02 PINTURA — 03 MAPA — 04 LINHA". A etapa atual fica branca
        // com sublinhado vermelho; as já feitas, com visto; as próximas, esmaecidas.
        internal static RectTransform Etapas(Transform pai, string[] nomes, int atual)
        {
            var rt = Novo("Etapas", pai);
            Linha(rt, 10, 0, TextAnchor.MiddleCenter);
            rt.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Tam(rt, -1, 40);
            for (int i = 0; i < nomes.Length; i++)
            {
                if (i > 0)
                {
                    var lig = Novo("Ligacao", rt);
                    Tam(lig, 28, 1);
                    Fundo(lig, null, i <= atual ? Theme.TextoMudo : Theme.LinhaForte).raycastTarget = false;
                }
                bool feita = i < atual, agora = i == atual;
                var passo = Novo("Etapa", rt);
                Linha(passo, 6, 0, TextAnchor.MiddleLeft);
                passo.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                Tam(passo, -1, 40);
                var cor = agora ? Theme.Texto : Theme.TextoMudo;
                if (feita) Icone(passo, Ico.Marca, 12, Theme.TextoMudo);
                else
                {
                    var n = Texto(passo, (i + 1).ToString("00"), 14, agora ? Theme.AcentoVivo : Theme.TextoMudo, true);
                    n.fontStyle = FontStyle.BoldAndItalic;
                    n.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
                var t = Texto(passo, nomes[i].ToUpperInvariant(), 13, cor, true);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                if (agora)
                {
                    var sub = Novo("Sublinhado", passo);
                    sub.anchorMin = new Vector2(0, 0); sub.anchorMax = new Vector2(1, 0); sub.pivot = new Vector2(0.5f, 0);
                    sub.sizeDelta = new Vector2(0, 3);
                    sub.anchoredPosition = new Vector2(0, 2);
                    sub.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    Fundo(sub, null, Theme.Acento).raycastTarget = false;
                }
            }
            return rt;
        }

        // Selo: etiqueta neutra de 20 px (contorno + texto em negrito), ex.: "GPU". Neutra de propósito:
        // vermelho fica reservado para a ação principal e a seleção.
        internal static RectTransform Selo(Transform pai, string texto)
        {
            var rt = Novo("Selo", pai);
            Fundo(rt, Theme.Solido, Theme.Cartao);
            Contorno(rt, Theme.LinhaForte);
            var t = Texto(rt, texto, 10, Theme.TextoDim, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Encher(t.rectTransform);
            Tam(rt, Mathf.Max(20, texto.Length * 8 + 14), 20);
            return rt;
        }

        // Marcador de impacto no desempenho: barra de 4x18 na cor da categoria, antes do rótulo.
        // Substitui o texto colorido do jogo (o rótulo fica branco, e a cor não é a única pista:
        // a legenda explica as categorias).
        internal static Image Impacto(Transform pai, Color cor)
        {
            var rt = Novo("Impacto", pai);
            Tam(rt, 4, 18);
            var img = Fundo(rt, null, cor);
            img.raycastTarget = false;
            return img;
        }
    }
}
