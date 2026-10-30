using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    /// <summary>
    /// Um painel de mod: uma janela pequena por cima do jogo, que não pausa nem tampa a direção, montada a partir do que o
    /// mod descreve (textos, valores, barras, botões, chaves, faixas). A PBSLoaderCommonInterface desenha no visual dela,
    /// atualiza os valores a cada quadro e solta o cursor enquanto o painel está aberto. Crie com <see cref="Paineis.Criar"/>.
    /// Os itens são lidos e chamados na thread principal; uma exceção num item vira "—" e uma linha no log.
    /// </summary>
    public sealed class Painel
    {
        public string Mod { get; }
        public string Id { get; }
        public string Titulo { get; set; }
        /// <summary>Largura da janela, em pixels de uma tela 1920x1080.</summary>
        public float Largura { get; set; } = 440f;
        /// <summary>Atalho que abre e fecha o painel (registrado em <see cref="Paineis.Criar"/>), ou null.</summary>
        public PBSLoaderCommonUtils.Entrada.Atalho Atalho { get; internal set; }

        public bool Aberto { get; private set; }
        public void Abrir() { if (!Aberto) { Aberto = true; Paineis.Mostrar(this); } }
        public void Fechar() { if (Aberto) { Aberto = false; Paineis.Esconder(this); } }
        public void Alternar() { if (Aberto) Fechar(); else Abrir(); }

        internal enum Tipo { Secao, Texto, Valor, Barra, Botao, Chave, Faixa }

        internal sealed class Item
        {
            public Tipo Tipo;
            public string Rotulo, Descricao;
            public Func<string> Texto;
            public Func<float> Numero;
            public Func<bool> Ligado;
            public Action Acao;
            public Action<bool> Mudar;
            public Action<float> MudarNumero;
            public float Min, Max, Passo;
            public string Formato;
        }

        internal readonly List<Item> Itens = new();
        internal GameObject Janela;

        internal Painel(string mod, string id, string titulo) { Mod = mod; Id = id; Titulo = titulo; }

        Painel Add(Item i) { Itens.Add(i); Paineis.Refazer(this); return this; }

        /// <summary>Título de grupo.</summary>
        public Painel Secao(string titulo) => Add(new Item { Tipo = Tipo.Secao, Rotulo = titulo });
        /// <summary>Texto corrido, recalculado a cada quadro.</summary>
        public Painel Texto(Func<string> texto) => Add(new Item { Tipo = Tipo.Texto, Texto = texto });
        /// <summary>Rótulo à esquerda e valor à direita ("Pressão", "8,2 bar").</summary>
        public Painel Valor(string rotulo, Func<string> valor, string descricao = null) => Add(new Item { Tipo = Tipo.Valor, Rotulo = rotulo, Texto = valor, Descricao = descricao });
        /// <summary>Barra de 0 a 1, com um texto opcional ao lado.</summary>
        public Painel Barra(string rotulo, Func<float> valor01, Func<string> texto = null) => Add(new Item { Tipo = Tipo.Barra, Rotulo = rotulo, Numero = valor01, Texto = texto });
        /// <summary>Botão que roda <paramref name="acao"/>.</summary>
        public Painel Botao(string rotulo, Action acao, string textoDoBotao = "Executar", string descricao = null) =>
            Add(new Item { Tipo = Tipo.Botao, Rotulo = rotulo, Acao = acao, Texto = () => textoDoBotao, Descricao = descricao });
        /// <summary>Chave liga/desliga: <paramref name="ler"/> diz o estado, <paramref name="mudar"/> recebe o novo.</summary>
        public Painel Chave(string rotulo, Func<bool> ler, Action<bool> mudar, string descricao = null) =>
            Add(new Item { Tipo = Tipo.Chave, Rotulo = rotulo, Ligado = ler, Mudar = mudar, Descricao = descricao });
        /// <summary>Faixa arrastável de <paramref name="min"/> a <paramref name="max"/> (passo 0 = contínua).</summary>
        public Painel Faixa(string rotulo, Func<float> ler, Action<float> mudar, float min, float max, float passo = 0f, string formato = "0.##") =>
            Add(new Item { Tipo = Tipo.Faixa, Rotulo = rotulo, Numero = ler, MudarNumero = mudar, Min = min, Max = max, Passo = passo, Formato = formato });

        /// <summary>
        /// Uma preferência do mod, com o controle do tipo dela: bool vira chave; número vira faixa (com
        /// <paramref name="min"/> e <paramref name="max"/>) ou valor; o resto vira valor. Mudanças gravam o .cfg ao fechar o painel.
        /// </summary>
        public Painel Preferencia(MelonPreferences_Entry entrada, float min = float.NaN, float max = float.NaN, float passo = 0f)
        {
            string rotulo = string.IsNullOrEmpty(entrada.DisplayName) ? entrada.Identifier : entrada.DisplayName;
            var t = entrada.GetReflectedType();
            if (t == typeof(bool))
                return Chave(rotulo, () => (bool)entrada.BoxedValue, v => { entrada.BoxedValue = v; Paineis.Sujo = true; }, entrada.Description);
            bool numero = t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(long);
            if (numero && !float.IsNaN(min) && !float.IsNaN(max))
                return Faixa(rotulo, () => Convert.ToSingle(entrada.BoxedValue, CultureInfo.InvariantCulture),
                    v => { entrada.BoxedValue = Convert.ChangeType(v, t, CultureInfo.InvariantCulture); Paineis.Sujo = true; }, min, max,
                    t == typeof(int) || t == typeof(long) ? Math.Max(1f, passo) : passo);
            return Valor(rotulo, () => Convert.ToString(entrada.BoxedValue, CultureInfo.InvariantCulture), entrada.Description);
        }
    }

    /// <summary>
    /// Painéis dos mods (ver <see cref="Painel"/>). Exemplo:
    /// <code>
    /// var p = Paineis.Criar("MeuMod", "Freios", "Freios", "Ctrl+Alt+F5")
    ///     .Valor("Pressão", () => $"{Ar.Pressao:0.0} bar")
    ///     .Barra("Freio", () => Veiculo.Pedais.Freio)
    ///     .Chave("Freio motor", () => _freioMotor, v => _freioMotor = v)
    ///     .Botao("Zerar", Zerar);
    /// </code>
    /// Cada painel também aparece como ação na página do mod no menu Mods.
    /// </summary>
    public static class Paineis
    {
        static readonly List<Painel> _todos = new();
        static GameObject _canvas;
        internal static bool Sujo;

        public static IReadOnlyList<Painel> Todos => _todos;

        /// <summary>
        /// Cria (ou devolve o já criado) o painel <paramref name="id"/> do mod <paramref name="mod"/> (o Identifier da
        /// categoria de preferências dele). Com <paramref name="atalho"/>, registra um atalho que abre e fecha o painel.
        /// </summary>
        public static Painel Criar(string mod, string id, string titulo, string atalho = null)
        {
            foreach (var p in _todos) if (p.Mod == mod && p.Id == id) return p;
            var novo = new Painel(mod, id, titulo);
            _todos.Add(novo);
            if (atalho != null)
                novo.Atalho = PBSLoaderCommonUtils.Entrada.Atalhos.Registrar(mod, "Painel." + id, "Painel: " + titulo, atalho, novo.Alternar,
                    "Abre e fecha o painel " + titulo);
            PainelMods.Acao(mod, titulo, novo.Abrir, "Abre o painel por cima do jogo");
            return novo;
        }

        // ---------- desenho ----------

        internal static void Refazer(Painel p)
        {
            if (p.Janela == null) return;
            UnityEngine.Object.Destroy(p.Janela);
            p.Janela = null;
            if (p.Aberto) Mostrar(p);
        }

        internal static void Mostrar(Painel p)
        {
            if (p.Janela != null && !p.Janela.WasCollected) { p.Janela.SetActive(true); return; }
            Canvas();
            p.Janela = Montar(p).gameObject;
            Anim.Aparecer(p.Janela, p.Janela.GetComponent<RectTransform>());
        }

        internal static void Esconder(Painel p)
        {
            if (p.Janela != null && !p.Janela.WasCollected) p.Janela.SetActive(false);
            if (Sujo) { Sujo = false; MelonPreferences.Save(); }
        }

        static void Canvas()
        {
            if (_canvas != null && !_canvas.WasCollected) return;
            Theme.Carregar();
            _canvas = new GameObject("PBSLoaderCommonInterface_Paineis") { layer = 5 };
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            var cv = _canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 850; // abaixo do menu Mods (900), acima do jogo
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<GraphicRaycaster>();
        }

        // Janelas abertas em cascata a partir do canto superior esquerdo, abaixo do botão do menu lateral.
        static RectTransform Montar(Painel p)
        {
            int abertos = 0;
            foreach (var o in _todos) if (o != p && o.Aberto && o.Janela != null) abertos++;
            var j = Kit.Novo("Painel_" + p.Mod + "_" + p.Id, _canvas.transform);
            j.anchorMin = j.anchorMax = new Vector2(0, 1); j.pivot = new Vector2(0, 1);
            // Abaixo do botão do menu lateral (TelaAlt, 44 px no canto).
            j.anchoredPosition = new Vector2(24 + 28 * abertos, -84 - 28 * abertos);
            Kit.Tam(j, p.Largura);
            j.sizeDelta = new Vector2(p.Largura, 0);
            Kit.Fundo(j, Theme.Solido, Theme.Elevado);
            Kit.Contorno(j, Theme.Linha);
            Kit.FaixaTopo(j, Theme.Acento, 3);
            var col = Kit.Coluna(j, 0);
            Kit.Pad(col, 20, 20, 12, 14);
            var fit = j.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cab = Kit.Novo("Cabecalho", j);
            Kit.Linha(cab, 8);
            Kit.Tam(cab, -1, 40);
            var tit = Kit.Texto(cab, (p.Titulo ?? p.Id).ToUpperInvariant(), 18, Theme.Texto, true);
            Kit.Tam(tit, -1, 40, 1);
            if (p.Atalho != null)
            {
                var dica = Kit.Texto(cab, "", 12, Theme.TextoMudo, false, TextAnchor.MiddleRight);
                Kit.Tam(dica, 140, 40);
                Kit.AoQuadro(dica.gameObject, () => { var t = p.Atalho.Combinacao.Legivel(); if (dica.text != t) dica.text = t; });
            }
            Kit.NovoBotao(cab, null, Ico.Fechar, Estilo.Icone, p.Fechar, 36);
            Kit.Divisoria(j);

            foreach (var it in p.Itens) Item(j, p, it);
            return j;
        }

        static string Ler(Painel p, Func<string> f)
        {
            if (f == null) return "";
            try { return f() ?? ""; }
            catch (Exception e) { PBSLoaderCommonUtils.Sdk.Registrar(e, $"Painel {p.Mod}.{p.Id}"); return "—"; }
        }

        static float LerNumero(Painel p, Func<float> f)
        {
            try { return f(); }
            catch (Exception e) { PBSLoaderCommonUtils.Sdk.Registrar(e, $"Painel {p.Mod}.{p.Id}"); return 0f; }
        }

        static void Rodar(Painel p, Action a)
        {
            try { a(); }
            catch (Exception e) { MelonLogger.Warning($"[Painel] {p.Mod}.{p.Id}: {e.Message}"); }
        }

        // Linha: rótulo (e descrição) à esquerda, controle à direita. Devolve o slot do controle.
        static RectTransform Linha(Transform pai, string rotulo, string descricao, float larguraControle)
        {
            var l = Kit.Novo("Linha", pai);
            var hl = Kit.Linha(l, 12);
            Kit.Pad(hl, 0, 0, 7, 7);
            var esq = Kit.Novo("Rotulos", l);
            Kit.Coluna(esq, 2, 0, TextAnchor.MiddleLeft);
            Kit.Tam(esq, -1, -1, 1);
            var t = Kit.Texto(esq, rotulo ?? "", 14, Theme.Texto);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (!string.IsNullOrWhiteSpace(descricao))
            {
                var d = Kit.Texto(esq, descricao, 11, Theme.TextoMudo);
                d.font = Theme.TextoCorrido;
                d.verticalOverflow = VerticalWrapMode.Overflow;
            }
            var ctrl = Kit.Novo("Controle", l);
            Kit.Linha(ctrl, 6, 0, TextAnchor.MiddleRight);
            Kit.Tam(ctrl, larguraControle, 34);
            return ctrl;
        }

        static void Item(Transform j, Painel p, Painel.Item it)
        {
            switch (it.Tipo)
            {
                case Painel.Tipo.Secao:
                    {
                        Kit.Espaco(j, 6);
                        var t = Kit.Rotulo(j, it.Rotulo);
                        Kit.Tam(t, -1, 22);
                        break;
                    }
                case Painel.Tipo.Texto:
                    {
                        var t = Kit.Texto(j, "", 13, Theme.TextoDim);
                        t.font = Theme.TextoCorrido;
                        t.verticalOverflow = VerticalWrapMode.Overflow;
                        t.horizontalOverflow = HorizontalWrapMode.Wrap;
                        Kit.AoQuadro(t.gameObject, () => { var s = Ler(p, it.Texto); if (t.text != s) t.text = s; });
                        break;
                    }
                case Painel.Tipo.Valor:
                    {
                        var ctrl = Linha(j, it.Rotulo, it.Descricao, 170);
                        var v = Kit.Texto(ctrl, "", 15, Theme.Dado, true, TextAnchor.MiddleRight);
                        Kit.Tam(v, 170, 34);
                        Kit.AoQuadro(v.gameObject, () => { var s = Ler(p, it.Texto); if (v.text != s) v.text = s; });
                        break;
                    }
                case Painel.Tipo.Barra:
                    {
                        var ctrl = Linha(j, it.Rotulo, null, 220);
                        var trilho = Kit.Novo("Trilho", ctrl);
                        Kit.Tam(trilho, 150, 8);
                        Kit.Fundo(trilho, Theme.Solido, Theme.Linha);
                        var cheio = Kit.Novo("Cheio", trilho);
                        cheio.anchorMin = Vector2.zero; cheio.anchorMax = new Vector2(0, 1); cheio.pivot = new Vector2(0, 0.5f);
                        cheio.offsetMin = cheio.offsetMax = Vector2.zero;
                        Kit.Fundo(cheio, Theme.Solido, Theme.Acento);
                        var v = Kit.Texto(ctrl, "", 13, Theme.Texto, true, TextAnchor.MiddleRight);
                        Kit.Tam(v, 62, 34);
                        Kit.AoQuadro(trilho.gameObject, () =>
                        {
                            float x = Mathf.Clamp01(LerNumero(p, it.Numero));
                            cheio.anchorMax = new Vector2(x, 1);
                            var s = it.Texto != null ? Ler(p, it.Texto) : (x * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
                            if (v.text != s) v.text = s;
                        });
                        break;
                    }
                case Painel.Tipo.Botao:
                    {
                        var ctrl = Linha(j, it.Rotulo, it.Descricao, 150);
                        var b = Kit.NovoBotao(ctrl, Ler(p, it.Texto), Ico.Avancar, Estilo.Secundario, () => Rodar(p, it.Acao), 34);
                        b.icone.transform.SetAsLastSibling();
                        Kit.Tam(b.btn, 150, 34);
                        break;
                    }
                case Painel.Tipo.Chave:
                    {
                        var ctrl = Linha(j, it.Rotulo, it.Descricao, 60);
                        Kit.Chave c = null;
                        c = Kit.NovaChave(ctrl, "", () => Rodar(p, () => it.Mudar(!it.Ligado())));
                        c.rotulo.gameObject.SetActive(false);
                        Kit.Tam(c.go.GetComponent<RectTransform>(), 52, 34);
                        Kit.AoQuadro(c.go, () => { try { c.Definir(it.Ligado()); } catch { } });
                        break;
                    }
                case Painel.Tipo.Faixa:
                    {
                        var ctrl = Linha(j, it.Rotulo, null, 230);
                        bool sincronizando = false;
                        var s = Kit.NovoSlider(ctrl, 160, v =>
                        {
                            if (sincronizando) return;
                            float x = Mathf.Lerp(it.Min, it.Max, v);
                            if (it.Passo > 0) x = Mathf.Round(x / it.Passo) * it.Passo;
                            Rodar(p, () => it.MudarNumero(x));
                        });
                        var valor = Kit.Texto(ctrl, "", 13, Theme.Texto, true, TextAnchor.MiddleRight);
                        Kit.Tam(valor, 60, 34);
                        Kit.AoQuadro(s.gameObject, () =>
                        {
                            float atual = LerNumero(p, it.Numero);
                            float n = Mathf.InverseLerp(it.Min, it.Max, atual);
                            if (Mathf.Abs(s.value - n) > 0.001f) { sincronizando = true; s.value = n; sincronizando = false; }
                            var txt = atual.ToString(it.Formato, CultureInfo.InvariantCulture);
                            if (valor.text != txt) valor.text = txt;
                        });
                        break;
                    }
            }
        }

        // A cada quadro: cursor solto enquanto houver painel aberto (o jogo continua respondendo ao teclado).
        internal static void Atualizar()
        {
            foreach (var p in _todos)
            {
                if (p.Aberto && (p.Janela == null || p.Janela.WasCollected)) { p.Janela = null; Mostrar(p); }
                PBSLoaderCommonUtils.Entrada.Mouse.Soltar("Painel." + p.Mod + "." + p.Id, p.Aberto);
            }
        }

        internal static string Diagnostico()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in _todos)
                sb.Append($"{p.Mod}.{p.Id} \"{p.Titulo}\": {p.Itens.Count} itens, aberto {p.Aberto}, janela {(p.Janela != null && !p.Janela.WasCollected && p.Janela.activeInHierarchy)}\n");
            return sb.Length > 0 ? sb.ToString() : "nenhum painel\n";
        }
    }
}
