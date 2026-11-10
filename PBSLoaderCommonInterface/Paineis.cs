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
        /// <summary>Abre no canto superior direito (abaixo dos cartões de HUD) em vez do esquerdo. Vale na próxima abertura.</summary>
        public bool Direita { get; set; }
        /// <summary>Atalho que abre e fecha o painel (registrado em <see cref="Paineis.Criar"/>), ou null.</summary>
        public PBSLoaderCommonUtils.Entrada.Atalho Atalho { get; internal set; }

        public bool Aberto { get; private set; }
        public void Abrir() { if (!Aberto) { Aberto = true; Paineis.Mostrar(this); } }
        public void Fechar() { if (Aberto) { Aberto = false; Paineis.Esconder(this); } }
        public void Alternar() { if (Aberto) Fechar(); else Abrir(); }

        internal enum Tipo { Secao, Texto, Valor, Barra, Botao, Chave, Faixa, Campo, Lista, Botoes, Busca }

        internal readonly struct LinhaDaLista
        {
            public readonly string Nome, Detalhe;
            public readonly bool Selecionada;
            public LinhaDaLista(string nome, string detalhe, bool selecionada) { Nome = nome; Detalhe = detalhe; Selecionada = selecionada; }
        }

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
            public Func<bool> Visivel;
            public Action<string> MudarTexto;
            public Func<string, string> Validar;
            // Campo numérico compara pelo valor (no seu mudar), não pelo texto: "1,5" e "1.50" são o mesmo número.
            public bool CompararPeloValor;
            public Func<IReadOnlyList<LinhaDaLista>> Linhas;
            public Action<int> Escolher;
            public float Altura;
            public string Vazio;
            public bool Compacta;
            public (string rotulo, Action acao)[] Botoes;
        }

        internal readonly List<Item> Itens = new();
        internal GameObject Janela;

        internal Painel(string mod, string id, string titulo) { Mod = mod; Id = id; Titulo = titulo; }

        Func<bool> _condicao;

        Painel Add(Item i) { i.Visivel = _condicao; Itens.Add(i); Paineis.Refazer(this); return this; }

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
        /// Campo de texto de uma linha. Mostra <paramref name="ler"/> enquanto não está em edição e chama
        /// <paramref name="mudar"/> ao confirmar (Enter ou clique fora), só se o texto mudou. Com <paramref name="validar"/>,
        /// um texto que devolve mensagem não é aplicado: a mensagem aparece embaixo do rótulo até a próxima edição válida.
        /// Enquanto o campo tem foco, os atalhos e o teclado do jogo ficam parados.
        /// </summary>
        public Painel Campo(string rotulo, Func<string> ler, Action<string> mudar, string descricao = null, Func<string, string> validar = null) =>
            Add(new Item { Tipo = Tipo.Campo, Rotulo = rotulo, Texto = ler, MudarTexto = mudar, Descricao = descricao, Validar = validar });

        /// <summary>
        /// Campo numérico: aceita ponto ou vírgula decimal e, se dados, recusa valores fora de
        /// [<paramref name="min"/>, <paramref name="max"/>] com uma mensagem no próprio campo.
        /// </summary>
        public Painel CampoNumero(string rotulo, Func<float> ler, Action<float> mudar, string formato = "0.###", string descricao = null,
            float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        {
            string Erro(string s)
            {
                if (!LerNumero(s, out var v)) return "Digite um número";
                if (v < min || v > max) return $"Entre {Num(min, formato)} e {Num(max, formato)}";
                return null;
            }
            Campo(rotulo, () => Num(ler(), formato), s => { if (LerNumero(s, out var v) && v != ler()) mudar(v); }, descricao, Erro);
            Itens[^1].CompararPeloValor = true;
            return this;
        }

        static string Num(float v, string formato) => v.ToString(formato, CultureInfo.InvariantCulture);

        static bool LerNumero(string s, out float v) =>
            float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            && !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>
        /// Lista rolável de altura fixa, relida a cada 0,1 s: linhas entram e saem sem remontar o painel. Um clique chama
        /// <paramref name="escolher"/> com o item da linha; <paramref name="selecionado"/> marca as linhas em destaque e
        /// <paramref name="detalhe"/> dá a segunda linha de texto. Sem itens, mostra <paramref name="vazio"/>.
        /// </summary>
        public Painel Lista<T>(Func<IReadOnlyList<T>> itens, Func<T, string> nome, Action<T> escolher, Func<T, bool> selecionado = null,
            Func<T, string> detalhe = null, float altura = 260f, string vazio = "Nada para mostrar", bool compacta = false)
        {
            IReadOnlyList<T> ultimo = Array.Empty<T>();
            IReadOnlyList<LinhaDaLista> Linhas()
            {
                ultimo = itens() ?? Array.Empty<T>();
                var r = new LinhaDaLista[ultimo.Count];
                for (int i = 0; i < r.Length; i++)
                {
                    var x = ultimo[i];
                    r[i] = new LinhaDaLista(nome(x) ?? "", detalhe?.Invoke(x) ?? "", selecionado != null && selecionado(x));
                }
                return r;
            }
            return Add(new Item
            {
                Tipo = Tipo.Lista, Linhas = Linhas, Altura = altura, Vazio = vazio, Compacta = compacta,
                Escolher = i => { if (i >= 0 && i < ultimo.Count) escolher(ultimo[i]); },
            });
        }

        /// <summary>
        /// Fileira de botões de mesma largura numa linha só, sem rótulo à esquerda: barra de ações de uma ferramenta
        /// ("Salvar", "Desfazer", "Refazer"). Rótulos curtos; o atalho pode ir no próprio texto ("Ponto (P)").
        /// </summary>
        public Painel Botoes(params (string rotulo, Action acao)[] botoes) => Add(new Item { Tipo = Tipo.Botoes, Botoes = botoes });

        /// <summary>
        /// Campo de busca que chama <paramref name="mudar"/> a cada letra (não espera Enter), para filtrar uma
        /// <see cref="Lista{T}"/> enquanto se digita.
        /// </summary>
        public Painel Busca(string dica, Action<string> mudar) => Add(new Item { Tipo = Tipo.Busca, Rotulo = dica, MudarTexto = mudar });

        /// <summary>
        /// Os itens adicionados daqui até <see cref="FimSe"/> só aparecem enquanto <paramref name="condicao"/> for verdadeira
        /// (inspetor da seleção atual, opções avançadas). Blocos não se aninham: um novo Se substitui o anterior.
        /// </summary>
        public Painel Se(Func<bool> condicao) { _condicao = condicao; return this; }

        /// <summary>Fecha o bloco aberto por <see cref="Se"/>.</summary>
        public Painel FimSe() { _condicao = null; return this; }

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
            foreach (var o in _todos) if (o != p && o.Aberto && o.Janela != null && o.Direita == p.Direita) abertos++;
            var j = Kit.Novo("Painel_" + p.Mod + "_" + p.Id, _canvas.transform);
            j.anchorMin = j.anchorMax = new Vector2(0, 1); j.pivot = new Vector2(0, 1);
            // Abaixo do botão do menu lateral (TelaAlt, 44 px no canto).
            j.anchoredPosition = new Vector2(24 + 28 * abertos, -84 - 28 * abertos);
            if (p.Direita)
            {
                // À direita, abaixo da coluna de cartões de HUD do canto (até ~100 px).
                j.anchorMin = j.anchorMax = new Vector2(1, 1); j.pivot = new Vector2(1, 1);
                j.anchoredPosition = new Vector2(-24 - 28 * abertos, -110 - 28 * abertos);
            }
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
            Kit.AoQuadro(tit.gameObject, () => { var t = (p.Titulo ?? p.Id).ToUpperInvariant(); if (tit.text != t) tit.text = t; });
            if (p.Atalho != null)
            {
                var dica = Kit.Texto(cab, "", 12, Theme.TextoMudo, false, TextAnchor.MiddleRight);
                Kit.Tam(dica, 140, 40);
                Kit.AoQuadro(dica.gameObject, () => { var t = p.Atalho.Combinacao.Legivel(); if (dica.text != t) dica.text = t; });
            }
            Kit.NovoBotao(cab, null, Ico.Fechar, Estilo.Icone, p.Fechar, 36);
            Kit.Divisoria(j);

            // Itens seguidos com a mesma condição dividem um grupo, que a janela liga e desliga a cada quadro.
            RectTransform grupo = null;
            Func<bool> condicaoDoGrupo = null;
            foreach (var it in p.Itens)
            {
                if (it.Visivel == null) { grupo = null; condicaoDoGrupo = null; Item(j, p, it); continue; }
                if (grupo == null || it.Visivel != condicaoDoGrupo)
                {
                    grupo = Kit.Novo("Grupo", j);
                    Kit.Coluna(grupo, 0);
                    condicaoDoGrupo = it.Visivel;
                    var g = grupo.gameObject;
                    var c = it.Visivel;
                    g.SetActive(LerCondicao(p, c));
                    Kit.AoQuadro(j.gameObject, () => { bool v = LerCondicao(p, c); if (g.activeSelf != v) g.SetActive(v); });
                }
                Item(grupo, p, it);
            }
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

        static bool LerCondicao(Painel p, Func<bool> f)
        {
            try { return f(); }
            catch (Exception e) { PBSLoaderCommonUtils.Sdk.Registrar(e, $"Painel {p.Mod}.{p.Id}"); return false; }
        }

        static void Rodar(Painel p, Action a)
        {
            try { a(); }
            catch (Exception e) { MelonLogger.Warning($"[Painel] {p.Mod}.{p.Id}: {e.Message}"); }
        }

        // Linha: rótulo (e descrição) à esquerda, controle à direita. Devolve o slot do controle.
        static RectTransform Linha(Transform pai, string rotulo, string descricao, float larguraControle) =>
            Linha(pai, rotulo, descricao, larguraControle, out _);

        static RectTransform Linha(Transform pai, string rotulo, string descricao, float larguraControle, out RectTransform rotulos)
        {
            var l = Kit.Novo("Linha", pai);
            var hl = Kit.Linha(l, 12);
            Kit.Pad(hl, 0, 0, 7, 7);
            var esq = Kit.Novo("Rotulos", l);
            rotulos = esq;
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
                case Painel.Tipo.Campo:
                    MontarCampo(j, p, it);
                    break;
                case Painel.Tipo.Lista:
                    MontarLista(j, p, it);
                    break;
                case Painel.Tipo.Botoes:
                    {
                        var fileira = Kit.Novo("Botoes", j);
                        var hl = Kit.Linha(fileira, 6);
                        Kit.Pad(hl, 0, 0, 5, 5);
                        foreach (var (rotulo, acao) in it.Botoes ?? Array.Empty<(string, Action)>())
                        {
                            var a = acao;
                            var b = Kit.NovoBotao(fileira, rotulo, null, Estilo.Secundario, () => Rodar(p, a), 34, 12, TextAnchor.MiddleCenter);
                            b.le.flexibleWidth = 1;
                        }
                        break;
                    }
                case Painel.Tipo.Busca:
                    {
                        var campo = Kit.Campo(j, it.Rotulo ?? "Buscar", s => Rodar(p, () => it.MudarTexto(s)), null);
                        Kit.Tam(campo, -1, 34);
                        Kit.Espaco(j, 4);
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

        static void MontarCampo(Transform j, Painel p, Painel.Item it)
        {
            var ctrl = Linha(j, it.Rotulo, it.Descricao, 200, out var rotulos);
            var erro = Kit.Texto(rotulos, "", 11, Theme.PerigoTexto);
            erro.verticalOverflow = VerticalWrapMode.Overflow;
            erro.gameObject.SetActive(false);
            var campo = Kit.Campo(ctrl, "", null, null, null);
            Kit.Tam(campo, 200, 34);
            campo.onEndEdit.AddListener((UnityEngine.Events.UnityAction<string>)(texto =>
            {
                string msg = null;
                if (it.Validar != null) { try { msg = it.Validar(texto); } catch (Exception e) { msg = e.Message; } }
                erro.text = msg ?? "";
                erro.gameObject.SetActive(msg != null);
                if (msg == null && (it.CompararPeloValor || texto != Ler(p, it.Texto))) Rodar(p, () => it.MudarTexto(texto));
            }));
            // Com erro na tela, o texto recusado fica no campo para o usuário corrigir.
            Kit.AoQuadro(campo.gameObject, () =>
            {
                if (campo.isFocused || erro.gameObject.activeSelf) return;
                var s = Ler(p, it.Texto);
                if (campo.text != s) campo.text = s;
            });
        }

        // Linhas reaproveitadas: só se criam ou destroem linhas quando a contagem muda; textos e destaque mudam no lugar.
        static void MontarLista(Transform j, Painel p, Painel.Item it)
        {
            var caixa = Kit.Novo("CaixaDaLista", j);
            Kit.Tam(caixa, -1, it.Altura);
            Kit.Coluna(caixa, 0);
            var conteudo = Kit.Lista(caixa, out _);
            var vazio = Kit.Texto(caixa, it.Vazio ?? "", 13, Theme.TextoMudo, false, TextAnchor.MiddleCenter);
            vazio.font = Theme.TextoCorrido;
            vazio.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Kit.Encher(vazio.rectTransform);

            var linhas = new List<Kit.ItemLista>();
            var ultimas = new List<Painel.LinhaDaLista>();
            float proxima = 0f;
            Kit.AoQuadro(caixa.gameObject, () =>
            {
                if (Time.unscaledTime < proxima) return;
                proxima = Time.unscaledTime + 0.1f;
                IReadOnlyList<Painel.LinhaDaLista> atual;
                try { atual = it.Linhas(); }
                catch (Exception e) { PBSLoaderCommonUtils.Sdk.Registrar(e, $"Painel {p.Mod}.{p.Id}"); return; }
                if (atual.Count != linhas.Count) RefazerLinhas(conteudo, p, it, atual, linhas, ultimas);
                else AtualizarLinhas(atual, linhas, ultimas);
                bool semItens = atual.Count == 0;
                if (vazio.gameObject.activeSelf != semItens) vazio.gameObject.SetActive(semItens);
            });
        }

        static void RefazerLinhas(Transform conteudo, Painel p, Painel.Item it, IReadOnlyList<Painel.LinhaDaLista> atual,
            List<Kit.ItemLista> linhas, List<Painel.LinhaDaLista> ultimas)
        {
            foreach (var l in linhas) UnityEngine.Object.Destroy(l.b.go);
            linhas.Clear();
            ultimas.Clear();
            for (int i = 0; i < atual.Count; i++)
            {
                int indice = i;
                var l = Kit.NovoItemLista(conteudo, i + 1, atual[i].Nome, atual[i].Detalhe, () => Rodar(p, () => it.Escolher(indice)));
                if (it.Compacta)
                {
                    // Lista longa (centenas de itens): linha de 40 px, número menor.
                    l.b.le.preferredHeight = l.b.le.minHeight = 40f;
                    l.indice.fontSize = 15;
                    Kit.Tam(l.indice, 26, 28);
                }
                l.Selecionado(atual[i].Selecionada);
                linhas.Add(l);
                ultimas.Add(atual[i]);
            }
        }

        static void AtualizarLinhas(IReadOnlyList<Painel.LinhaDaLista> atual, List<Kit.ItemLista> linhas, List<Painel.LinhaDaLista> ultimas)
        {
            for (int i = 0; i < atual.Count; i++)
            {
                var a = atual[i];
                var u = ultimas[i];
                if (a.Nome == u.Nome && a.Detalhe == u.Detalhe && a.Selecionada == u.Selecionada) continue;
                linhas[i].nome.text = a.Nome;
                linhas[i].meta.text = a.Detalhe;
                linhas[i].Selecionado(a.Selecionada);
                ultimas[i] = a;
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
