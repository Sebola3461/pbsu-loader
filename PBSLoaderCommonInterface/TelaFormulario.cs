using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Gera uma lista de configurações moderna a partir de um painel original de controles
    // posicionados à mão. Lê a posição de cada filho para saber a ordem de leitura e quais itens
    // dividem a mesma linha, e então monta, em UMA coluna:
    //   título grande           -> início de seção (cartão próprio)
    //   texto longo sozinho     -> parágrafo de ajuda
    //   texto curto sozinho     -> rótulo do grupo da linha seguinte, ou subtítulo
    //   Toggle solto            -> linha "rótulo .......... chave"
    //   Toggle de ToggleGroup   -> linha "rótulo .... [segmentado]" (até 4 opções curtas) ou lista de opções
    //   Slider (+ valor)        -> linha "rótulo .... [slider] valor"; com Toggle na mesma linha, a chave vai junto
    //   Dropdown / Button       -> linha "rótulo .......... [controle]"
    //   imagem "GPU"/"alerta"   -> selo antes do rótulo seguinte
    // Controles ficam alinhados à direita em todas as linhas. Cada item espelha visibilidade,
    // estado e texto do original, e altera o original ao ser usado.
    internal abstract class TelaFormulario : Tela
    {
        enum Tipo { Texto, Titulo, Chave, Segmento, Slider, Seletor, Campo, Botao, SeloGpu, SeloAlerta, Grupo }

        sealed class Item
        {
            public Transform t;
            public Tipo tipo;
            public float x, y, w, h;
            public string texto;
        }

        static Color CorSecao => Theme.Cartao;
        const float LarguraControle = 380f;

        Transform _pai;
        RectTransform _secao;
        int _linhasNaSecao;
        Func<Transform> _ate;

        protected void Formulario(Transform pai, Transform painel)
        {
            if (painel == null) return;
            _pai = pai; _secao = null; _linhasNaSecao = 0;
            _ate = () => painel;

            var itens = new List<Item>();
            Coletar(painel, itens);
            itens.Sort((a, b) => b.y.CompareTo(a.y));
            var linhas = new List<List<Item>>();
            foreach (var it in itens)
            {
                var ultima = linhas.Count > 0 ? linhas[linhas.Count - 1] : null;
                if (ultima != null && Mathf.Abs(ultima[0].y - it.y) <= Mathf.Max(18f, Mathf.Min(ultima[0].h, it.h) * 0.45f))
                    ultima.Add(it);
                else
                    linhas.Add(new List<Item> { it });
            }
            foreach (var l in linhas) l.Sort((a, b) => a.x.CompareTo(b.x));

            Item rotuloPendente = null;
            for (int li = 0; li < linhas.Count; li++)
            {
                var linha = linhas[li];
                var semSelos = linha.FindAll(x => x.tipo != Tipo.SeloGpu && x.tipo != Tipo.SeloAlerta);
                if (semSelos.Count == 0) continue;

                if (semSelos.Count == 1 && semSelos[0].tipo == Tipo.Titulo)
                {
                    Titulo(semSelos[0]);
                    continue;
                }
                if (semSelos.Count == 1 && semSelos[0].tipo == Tipo.Texto)
                {
                    var it = semSelos[0];
                    bool longo = it.w >= 600 || (it.texto?.Length ?? 0) > 60;
                    bool proximaEhGrupo = li + 1 < linhas.Count && SoSegmentos(linhas[li + 1]);
                    if (!longo && proximaEhGrupo) { rotuloPendente = it; continue; }
                    if (longo) Paragrafo(it); else Subtitulo(it);
                    continue;
                }

                // Linha com controles: percorre da esquerda para a direita.
                Item rotulo = rotuloPendente;
                rotuloPendente = null;
                string selo = null;
                for (int i = 0; i < linha.Count; i++)
                {
                    var it = linha[i];
                    switch (it.tipo)
                    {
                        case Tipo.SeloGpu: selo = "GPU"; break;
                        case Tipo.SeloAlerta: selo = "!"; break;
                        case Tipo.Texto:
                        case Tipo.Titulo:
                            rotulo = it;
                            break;

                        case Tipo.Chave:
                            if (i + 1 < linha.Count && linha[i + 1].tipo == Tipo.Slider)
                            {
                                var valor = ValorApos(linha, i + 1);
                                LinhaSlider(Rotulo(it.t.Find("Label")), selo, linha[i + 1], valor, it);
                                i += valor != null ? 2 : 1;
                            }
                            else LinhaChave(it, selo, rotulo);
                            rotulo = null; selo = null;
                            break;

                        case Tipo.Slider:
                        {
                            var valor = ValorApos(linha, i);
                            LinhaSlider(rotulo != null ? Rotulo(rotulo.t) : RotuloInterno(it.t), selo, it, valor, null);
                            if (valor != null) i++;
                            rotulo = null; selo = null;
                            break;
                        }

                        case Tipo.Segmento:
                        {
                            // Junta as opções do mesmo grupo nesta linha e nas seguintes (ex.: 7 presets em 3 linhas).
                            var grupo = it.t.GetComponent<Toggle>().group;
                            var opcoes = new List<Item>();
                            for (; i < linha.Count && Mesmo(linha[i], grupo); i++) opcoes.Add(linha[i]);
                            i--;
                            if (i == linha.Count - 1)
                                while (li + 1 < linhas.Count && SoSegmentos(linhas[li + 1], grupo))
                                {
                                    li++;
                                    foreach (var o in linhas[li]) if (o.tipo == Tipo.Segmento) opcoes.Add(o);
                                }
                            LinhaSegmentos(rotulo, selo, opcoes);
                            rotulo = null; selo = null;
                            break;
                        }

                        case Tipo.Seletor:
                            LinhaControle(rotulo != null ? Rotulo(rotulo.t) : RotuloInterno(it.t), selo, p =>
                                Kit.Tam(SeletorF(p, Fixo<Dropdown>(it.t), 280, _ate).btn, 280, 40));
                            rotulo = null; selo = null;
                            break;

                        case Tipo.Campo:
                            LinhaControle(rotulo != null ? Rotulo(rotulo.t) : null, selo, p =>
                                CampoF(p, Fixo<InputField>(it.t), 200, _ate));
                            rotulo = null; selo = null;
                            break;

                        case Tipo.Botao:
                            LinhaControle(rotulo != null ? Rotulo(rotulo.t) : null, selo, p =>
                                BotaoF(p, Fixo(it.t), IconeDoBotao(it.t), Estilo.Secundario, Orig.Texto(it.t) ?? it.t.name,
                                    Fixo(it.t), _ate, altura: 40));
                            rotulo = null; selo = null;
                            break;
                    }
                }
                // Texto que sobrou sozinho no fim da linha (sem controle depois dele)
                if (rotulo != null) Subtitulo(rotulo);
            }
        }

        // ---------- montagem por especificação ----------
        // Em vez de seguir a ordem e as abas do original, a tela diz onde cada controle vai e com que
        // rótulo; os controles são achados por nome em "origem". Tudo o que for usado fica registrado
        // em Usados, para a tela poder listar no fim o que sobrou (nada some se o jogo ganhar opções).

        Transform _origem;
        protected readonly HashSet<string> Usados = new();

        protected void Comecar(Transform pai, Transform origem, Func<Transform> ate)
        {
            _pai = pai; _secao = null; _linhasNaSecao = 0; _origem = origem; _ate = ate;
        }

        protected Transform Achar(string nome)
        {
            var t = Buscar(_origem, nome);
            if (t != null) Usados.Add(nome);
            return t;
        }

        // Visibilidade de um controle vai até o painel da aba original dele (filho direto de "origem"),
        // sem contar o painel: as abas do jogo ficam inativas, mas os controles continuam valendo.
        Func<Transform> AteDe(Transform t)
        {
            var x = t;
            while (x != null && x.parent != _origem) x = x.parent;
            // Filho direto da origem: o limite é a própria origem (senão o controle nunca seria checado).
            var ate = x == null || x == t ? _origem : x;
            return () => ate;
        }

        static Transform Buscar(Transform raiz, string nome)
        {
            if (raiz == null) return null;
            for (int i = 0; i < raiz.childCount; i++)
            {
                var f = raiz.GetChild(i);
                if (f.name == nome) return f;
                var r = Buscar(f, nome);
                if (r != null) return r;
            }
            return null;
        }

        // Título de seção (cartão novo começa na próxima linha).
        protected void OpSecao(string texto)
        {
            _secao = null;
            Kit.Espaco(_pai, 14);
            Kit.Tam(Kit.Texto(_pai, texto, 22, Theme.Texto, true), -1, 32);
        }

        // Rótulo de grupo dentro da seção atual.
        protected void OpGrupo(string texto)
        {
            var s = Secao();
            if (_linhasNaSecao > 0) Kit.Divisoria(s);
            var t = Kit.Rotulo(s, texto);
            Kit.Tam(t, -1, 34);
            t.alignment = TextAnchor.LowerLeft;
            _linhasNaSecao = 0;
        }

        // Rótulo fixo da linha; a cor de impacto vem do texto original indicado (se houver).
        Func<Transform, Text> RotuloFixo(string texto, Transform corDe) => linha =>
        {
            Kit.Impacto(linha, Impacto(corDe)).transform.SetAsFirstSibling();
            var t = Kit.Texto(linha, texto, 15, Theme.Texto);
            Kit.Tam(t, -1, -1, 1);
            return t;
        };

        protected void OpChave(string nome, string rotulo, bool gpu = false)
        {
            var t = Achar(nome);
            if (t == null) return;
            _ate = AteDe(t);
            var s = Secao();
            if (_linhasNaSecao++ > 0) Kit.Divisoria(s);
            var c = ChaveF(s, Fixo(t), _ate, rotuloFixo: rotulo);
            Kit.Tam(c.go.GetComponent<RectTransform>(), -1, 52);
            Kit.Pad(c.go.GetComponent<HorizontalLayoutGroup>(), 0, 4, 0, 0);
            if (gpu) Selo(c.go.transform, "GPU").transform.SetAsFirstSibling();
            Kit.Impacto(c.go.transform, Impacto(t.Find("Label"))).transform.SetAsFirstSibling();
        }

        // Slider com valor à direita; "chave": toggle que liga/desliga o mesmo item (fica no fim da linha).
        protected void OpSlider(string slider, string rotulo, string textoOriginal = null, string valor = null, string chave = null, bool gpu = false)
        {
            var s = Achar(slider);
            if (s == null) return;
            _ate = AteDe(s);
            var corDe = textoOriginal != null ? Achar(textoOriginal) : null;
            var v = valor != null ? Achar(valor) : null;
            var c = chave != null ? Achar(chave) : null;
            LinhaSlider(RotuloFixo(rotulo, corDe), gpu ? "GPU" : null,
                new Item { t = s, tipo = Tipo.Slider }, v != null ? new Item { t = v, tipo = Tipo.Texto } : null,
                c != null ? new Item { t = c, tipo = Tipo.Chave } : null);
        }

        // Opções exclusivas (toggles de um grupo): segmentado se couber, senão lista com marca.
        protected void OpOpcoes(string rotulo, string textoOriginal, bool gpu, params string[] toggles)
        {
            var itens = new List<Item>();
            foreach (var n in toggles) { var t = Achar(n); if (t != null) itens.Add(new Item { t = t, tipo = Tipo.Segmento }); }
            if (itens.Count == 0) return;
            _ate = AteDe(itens[0].t);
            var corDe = textoOriginal != null ? Achar(textoOriginal) : null;
            LinhaSegmentosX(RotuloFixo(rotulo, corDe), () => OpGrupo(rotulo), gpu ? "GPU" : null, itens);
        }

        // Texto de ajuda do original, como parágrafo esmaecido sob a linha anterior.
        protected void OpAjuda(string nome)
        {
            var t = Achar(nome);
            if (t == null) return;
            _ate = AteDe(t);
            Paragrafo(new Item { t = t, tipo = Tipo.Texto });
        }

        protected void OpAcao(string botao, string rotulo, string textoBotao = "Executar", string icone = null) =>
            OpAcao(Achar(botao), rotulo, textoBotao, icone);

        // Linha "rótulo ........ [botão]" para um botão do original (ação ou atalho para outro painel).
        protected void OpAcao(Transform t, string rotulo, string textoBotao = "Executar", string icone = null)
        {
            if (t == null) return;
            Usados.Add(t.name);
            _ate = AteDe(t);
            var ate = _ate;
            LinhaControle(RotuloFixo(rotulo, null), null, p =>
                Kit.Tam(BotaoF(p, Fixo(t), icone, Estilo.Secundario, textoBotao, null, ate, altura: 40).btn, 140, 40));
        }

        // Seletor (Dropdown do original) com rótulo próprio.
        protected void OpSeletor(string nome, string rotulo)
        {
            var t = Achar(nome);
            if (t == null) return;
            _ate = AteDe(t);
            var ate = _ate;
            LinhaControle(RotuloFixo(rotulo, null), null, p => Kit.Tam(SeletorF(p, Fixo<Dropdown>(t), 280, ate).btn, 280, 40));
        }

        // Controles do original que nenhuma especificação usou (Toggle, Slider, Dropdown, Button,
        // InputField), com o rótulo do próprio jogo. Ignorados: os de "fora".
        protected void OpRestantes(ISet<string> fora, string titulo)
        {
            var lista = new List<Transform>();
            ColetarRestantes(_origem, lista, fora);
            if (lista.Count > 0) OpSecao(titulo);
            foreach (var t in lista)
            {
                Usados.Add(t.name);
                _ate = AteDe(t);
                if (t.GetComponent<Toggle>() is Toggle tg)
                {
                    if (tg.group != null) LinhaSegmentosX(null, null, null, new List<Item> { new Item { t = t, tipo = Tipo.Segmento } });
                    else LinhaChave(new Item { t = t, tipo = Tipo.Chave }, null, null);
                }
                else if (t.GetComponent<Slider>() != null) LinhaSlider(RotuloInterno(t), null, new Item { t = t, tipo = Tipo.Slider }, null, null);
                else if (t.GetComponent<Dropdown>() != null)
                    LinhaControle(RotuloInterno(t), null, p => Kit.Tam(SeletorF(p, Fixo<Dropdown>(t), 280, _ate).btn, 280, 40));
                else if (t.GetComponent<InputField>() != null)
                    LinhaControle(null, null, p => CampoF(p, Fixo<InputField>(t), 200, _ate));
                else if (t.GetComponent<Button>() != null)
                    LinhaControle(null, null, p => BotaoF(p, Fixo(t), null, Estilo.Secundario, Orig.Texto(t) ?? t.name, Fixo(t), _ate, altura: 40));
            }
        }

        void ColetarRestantes(Transform raiz, List<Transform> saida, ISet<string> fora)
        {
            for (int i = 0; i < raiz.childCount; i++)
            {
                var f = raiz.GetChild(i);
                if (fora.Contains(f.name) || Ignorar(f)) continue;
                bool controle = f.GetComponent<Toggle>() != null || f.GetComponent<Slider>() != null || f.GetComponent<Dropdown>() != null
                    || f.GetComponent<InputField>() != null || f.GetComponent<Button>() != null;
                if (controle) { if (!Usados.Contains(f.name)) saida.Add(f); continue; } // não desce em filhos de controle
                ColetarRestantes(f, saida, fora);
            }
        }

        // ---------- coleta ----------

        void Coletar(Transform painel, List<Item> saida)
        {
            for (int i = 0; i < painel.childCount; i++)
            {
                var c = painel.GetChild(i);
                if (Ignorar(c)) continue;
                var tipo = Classificar(c);
                if (tipo == null) continue;
                if (tipo == Tipo.Grupo) { Coletar(c, saida); continue; }
                var rt = c.TryCast<RectTransform>();
                if (rt == null) continue;
                var centro = _ate().InverseTransformPoint(rt.TransformPoint(rt.rect.center));
                var txt = c.GetComponent<Text>();
                saida.Add(new Item { t = c, tipo = tipo.Value, x = centro.x, y = centro.y, w = rt.rect.width, h = rt.rect.height, texto = txt != null ? txt.text : null });
            }
        }

        // Elementos que a janela nova já tem (fechar, fundo) ou que são decoração do original.
        protected virtual bool Ignorar(Transform c)
        {
            var n = c.name;
            return n.StartsWith("btFechar", StringComparison.Ordinal) || n == "btOK_fechar"
                || n.StartsWith("fundo", StringComparison.Ordinal) || n.StartsWith("icone ", StringComparison.Ordinal);
        }

        static Tipo? Classificar(Transform c)
        {
            if (c.GetComponent<Toggle>() is Toggle tg) return tg.group != null ? Tipo.Segmento : Tipo.Chave;
            if (c.GetComponent<InputField>() != null) return Tipo.Campo;
            if (c.GetComponent<Slider>() != null) return Tipo.Slider;
            if (c.GetComponent<Dropdown>() != null) return Tipo.Seletor;
            if (c.GetComponent<Button>() != null) return Tipo.Botao;
            if (c.GetComponent<Text>() is Text t) return t.fontSize >= 33 ? Tipo.Titulo : Tipo.Texto;
            var img = c.GetComponent<Image>();
            if (img != null && img.sprite != null)
            {
                var n = img.sprite.name.ToLowerInvariant();
                if (n.Contains("gpu")) return Tipo.SeloGpu;
                if (n.Contains("alerta")) return Tipo.SeloAlerta;
                return null; // decoração
            }
            if (img == null && c.childCount > 0 && c.GetComponent<Graphic>() == null) return Tipo.Grupo;
            return null;
        }

        static bool Mesmo(Item it, ToggleGroup grupo) =>
            it.tipo == Tipo.Segmento && it.t.GetComponent<Toggle>().group == grupo;

        static bool SoSegmentos(List<Item> linha, ToggleGroup grupo = null)
        {
            bool algum = false;
            foreach (var it in linha)
            {
                if (it.tipo == Tipo.SeloGpu || it.tipo == Tipo.SeloAlerta) continue;
                if (it.tipo != Tipo.Segmento) return false;
                if (grupo != null && it.t.GetComponent<Toggle>().group != grupo) return false;
                algum = true;
            }
            return algum;
        }

        // Texto curto logo depois de um slider = valor dele ("20", "55%", "492m").
        static Item ValorApos(List<Item> linha, int iSlider)
        {
            int j = iSlider + 1;
            return j < linha.Count && linha[j].tipo == Tipo.Texto && linha[j].w <= 180 ? linha[j] : null;
        }

        // ---------- seções e linhas ----------

        RectTransform Secao()
        {
            if (_secao != null) return _secao;
            _secao = Kit.Novo("Secao", _pai);
            Kit.Fundo(_secao, Theme.Solido, CorSecao);
            var c = Kit.Coluna(_secao, 0);
            Kit.Pad(c, 20, 20, 6, 6);
            _linhasNaSecao = 0;
            Kit.Espaco(_pai, 4).SetAsLastSibling();
            _secao.SetSiblingIndex(_pai.childCount - 2);
            return _secao;
        }

        protected RectTransform SecaoAtual() => Secao();

        // Nova linha dentro da seção atual (com divisória entre linhas).
        RectTransform NovaLinha(float altura = 52)
        {
            var s = Secao();
            if (_linhasNaSecao++ > 0) Kit.Divisoria(s);
            var rt = Kit.Novo("Linha", s);
            var hl = Kit.Linha(rt, 12);
            Kit.Tam(rt, -1, altura);
            _ = hl;
            return rt;
        }

        void Titulo(Item it)
        {
            _secao = null;
            Kit.Espaco(_pai, 14);
            var t = TextoF(_pai, Fixo(it.t), 22, Theme.Texto, true, ate: _ate);
            Kit.Tam(t, -1, 32);
        }

        void Subtitulo(Item it)
        {
            var s = Secao();
            if (_linhasNaSecao > 0) Kit.Divisoria(s);
            // Rótulo de grupo: o mesmo estilo de Kit.Rotulo (maiúsculas espaçadas, pequeno, esmaecido).
            var t = TextoF(s, Fixo(it.t), Theme.TamRotulo, Theme.TextoMudo, ate: _ate, formato: x => Theme.Rotular(x.TrimEnd(':')));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(t, -1, 34);
            t.alignment = TextAnchor.LowerLeft;
            _linhasNaSecao = 0; // a próxima linha vem logo abaixo do subtítulo, sem divisória
        }

        void Paragrafo(Item it)
        {
            var s = Secao();
            var t = TextoF(s, Fixo(it.t), 13, Theme.TextoMudo, ate: _ate);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Kit.Espaco(s, 2);
        }

        // Rótulo (espelhado do original); devolve a função que cria o rótulo na linha. O marcador de
        // impacto vai sempre na primeira posição da linha (transparente quando não há), para todos os
        // rótulos começarem no mesmo x.
        Func<Transform, Text> Rotulo(Transform origem)
        {
            if (origem == null) return null;
            return linha =>
            {
                Kit.Impacto(linha, Impacto(origem)).transform.SetAsFirstSibling();
                var t = TextoF(linha, Fixo(origem), 15, Theme.Texto, ate: _ate, formato: x => x.Replace("\n", " ").TrimEnd(':'));
                Kit.Tam(t, -1, -1, 1);
                return t;
            };
        }

        Func<Transform, Text> RotuloInterno(Transform controle)
        {
            for (int i = 0; i < controle.childCount; i++)
            {
                var f = controle.GetChild(i);
                if (f.name != "Label" && f.GetComponent<Text>() != null) return Rotulo(f);
            }
            return null;
        }

        void Cabeca(RectTransform linha, Func<Transform, Text> rotulo, string selo)
        {
            if (selo != null) Selo(linha, selo);
            if (rotulo != null) rotulo(linha);
            else Kit.Tam(Kit.Novo("Vazio", linha), -1, -1, 1);
        }

        void LinhaChave(Item it, string selo, Item rotuloExtra)
        {
            var s = Secao();
            if (_linhasNaSecao++ > 0) Kit.Divisoria(s);
            var c = ChaveF(s, Fixo(it.t), _ate);
            Kit.Tam(c.go.GetComponent<RectTransform>(), -1, 52);
            var hl = c.go.GetComponent<HorizontalLayoutGroup>();
            Kit.Pad(hl, 0, 4, 0, 0);
            if (selo != null) Selo(c.go.transform, selo).transform.SetAsFirstSibling();
            Kit.Impacto(c.go.transform, Impacto(it.t.Find("Label"))).transform.SetAsFirstSibling();
            _ = rotuloExtra;
        }

        void LinhaSlider(Func<Transform, Text> rotulo, string selo, Item slider, Item valor, Item chave)
        {
            var l = NovaLinha();
            Cabeca(l, rotulo, selo);
            Kit.Tam(SliderF(l, Fixo<Slider>(slider.t), 300, _ate), 300, 36);
            // Valor: número em negrito reto, branco (dado lido de relance).
            var v = valor != null ? TextoF(l, Fixo(valor.t), 15, Theme.Texto, true, ate: _ate) : Kit.Texto(l, "", 15, Theme.Texto, true);
            Kit.Tam(v, 64, 32);
            v.alignment = TextAnchor.MiddleRight;
            if (chave != null)
            {
                var c = ChaveF(l, Fixo(chave.t), _ate);
                c.rotulo.gameObject.SetActive(false); // o rótulo da chave já é o da linha
                Kit.Tam(c.go.GetComponent<RectTransform>(), 72, 44);
            }
            VisivelLinha(l);
        }

        void LinhaSegmentos(Item rotulo, string selo, List<Item> opcoes) =>
            LinhaSegmentosX(rotulo != null ? Rotulo(rotulo.t) : null, rotulo != null ? () => Subtitulo(rotulo) : null, selo, opcoes);

        // rotuloLinha: rótulo da linha no modo segmentado; subtitulo: o que pôr acima da lista no modo lista.
        void LinhaSegmentosX(Func<Transform, Text> rotuloLinha, Action subtitulo, string selo, List<Item> opcoes)
        {
            int chars = 0;
            foreach (var o in opcoes) chars += (Orig.Texto(o.t.Find("Label")) ?? "").Length;
            bool compacto = opcoes.Count <= 4 && chars <= 44;

            if (compacto)
            {
                var l = NovaLinha();
                Cabeca(l, rotuloLinha, selo);
                var grupo = Kit.Novo("Segmentado", l);
                Kit.Fundo(grupo, null, Theme.Alfa(Color.white, 0f));
                var gl = Kit.Linha(grupo, 2);
                Kit.Pad(gl, 3, 3, 3, 3);
                Kit.Tam(grupo, -1, 44);
                foreach (var o in opcoes) Segmento(grupo, Fixo<Toggle>(o.t), o.t.name, _ate);
                VisivelLinha(l);
                return;
            }

            // Muitas opções ou rótulos longos: subtítulo + uma linha por opção, com marca de seleção.
            subtitulo?.Invoke();
            var ateLista = _ate; // capturado agora: _ate muda entre linhas na montagem por especificação
            foreach (var o in opcoes)
            {
                var s = Secao();
                if (_linhasNaSecao++ > 0) Kit.Divisoria(s);
                var tgF = Fixo<Toggle>(o.t);
                var b = Kit.NovoBotao(s, o.t.name, Ico.Marca, Estilo.Fantasma, () =>
                {
                    var tg = tgF();
                    Escolher(tg);
                }, 48);
                b.icone.transform.SetAsLastSibling();
                Kit.Tam(b.rotulo, -1, -1, 1);
                Sync(() =>
                {
                    var tg = tgF();
                    bool vis = tg != null && Orig.Ativo(tg.transform, ateLista());
                    b.Visivel(vis);
                    if (!vis) return;
                    var txt = Orig.Limpo(Orig.Texto(tg.transform.Find("Label")));
                    if (!string.IsNullOrEmpty(txt)) b.Rotulo(txt);
                    if (b.icone.enabled != tg.isOn) b.icone.enabled = tg.isOn;
                    b.Selecionado(tg.isOn); // opção escolhida vira o bloco branco da seleção
                    b.Interagivel(tg.interactable);
                });
            }
        }

        void LinhaControle(Func<Transform, Text> rotulo, string selo, Action<Transform> criar)
        {
            var l = NovaLinha();
            Cabeca(l, rotulo, selo);
            criar(l);
            VisivelLinha(l);
        }

        // Linha some quando o controle dela some (o rótulo sozinho não sustenta a linha).
        void VisivelLinha(RectTransform l)
        {
            Sync(() =>
            {
                bool algum = false;
                for (int i = 0; i < l.childCount; i++)
                {
                    var f = l.GetChild(i);
                    if (f.GetComponent<Text>() != null || f.name == "Selo" || f.name == "Vazio") continue;
                    if (f.gameObject.activeSelf) { algum = true; break; }
                }
                if (l.gameObject.activeSelf != algum) l.gameObject.SetActive(algum);
            });
        }

        // "GPU": selo neutro. "!": no jogo é um ícone de alerta, mas a legenda dele diz "recurso extra
        // para colaboradores"; aqui vira uma estrela âmbar, que não sugere perigo.
        static GameObject Selo(Transform pai, string texto)
        {
            if (texto == "GPU") return Kit.Selo(pai, texto).gameObject;
            var ic = Kit.Icone(pai, Ico.Estrela, 14, Theme.Dado);
            ic.gameObject.name = "Selo";
            return ic.gameObject;
        }

        static string IconeDoBotao(Transform t)
        {
            var img = t.GetComponent<Image>();
            var n = img != null && img.sprite != null ? img.sprite.name.ToLowerInvariant() : "";
            if (n.Contains("mais")) return Ico.Adicionar;
            if (n.Contains("menos")) return Ico.Remover;
            if (n.Contains("fechar")) return Ico.Fechar;
            if (n.Contains("voltar")) return Ico.Voltar;
            return null;
        }

        // O jogo colore o texto pelo impacto no desempenho (verde leve, amarelo médio, vermelho pesado).
        // Aqui a cor vira a categoria do marcador; texto branco/cinza = sem categoria (transparente).
        static Color Impacto(Transform t)
        {
            var txt = t != null ? t.GetComponent<Text>() : null;
            if (txt == null) return new Color(0, 0, 0, 0);
            var c = txt.color;
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (max <= 0.01f || (max - min) / max < 0.2f) return new Color(0, 0, 0, 0);
            if (c.g >= c.r * 1.25f) return Theme.ImpactoLeve;
            if (c.g >= c.r * 0.55f) return Theme.ImpactoMedio;
            return Theme.ImpactoPesado;
        }

        static Func<Transform> Fixo(Transform t) => () => t != null && !t.WasCollected ? t : null;
        static Func<T> Fixo<T>(Transform t) where T : Component => () => t != null && !t.WasCollected ? t.GetComponent<T>() : null;
    }

    // Configurações (Canvas OPCOES NOVAS). As cinco abas do jogo misturam assuntos (ABS em
    // "Simulação", física e GC em "Extras", espelhos junto do tráfego...). Aqui os mesmos controles
    // são redistribuídos em categorias próprias, com rótulos curtos; o jogo continua sendo o dono
    // de cada valor. O que o jogo tiver e a especificação não citar aparece em Avançado > Outras.
    internal sealed class TelaOpcoes : TelaFormulario
    {
        const string Cont = "Scroll View/Container";

        protected override string NomeCanvas => "Canvas OPCOES NOVAS";
        protected override bool TransicaoCheia => true;
        protected override int? OrdemCanvas => 700;
        protected override string[] Cobertos => new[]
        {
            "Image", "t legenda das cores", "aviso alerta", "t aviso colaborador", "t aviso gpu", "aviso placa de video",
            "btVoltar", "btReset", "bt pc baixo", "bt pc medio", "bt pc alto", "bt pc ultra",
            "bt op performance", "bt op graficas", "bt op simulacao", "bt op som", "bt op extras",
            "bt android super leve", "bt android leve", "bt android bom", "Scroll View", "painel in game",
        };

        static readonly (string nome, string icone)[] Categorias =
        {
            ("Gráficos", Ico.Video), ("Iluminação", Ico.Lampada), ("Desempenho", Ico.Gpu), ("Tráfego e mundo", Ico.Carro),
            ("Direção", Ico.Onibus), ("Câmera e espelhos", Ico.Camera), ("Som", Ico.Som), ("Avançado", Ico.Ferramenta),
        };

        readonly List<Kit.Botao> _abas = new();
        readonly List<GameObject> _blocos = new();
        ScrollRect _scroll;
        int _atual = -1;

        protected override void Construir()
        {
            Kit.Atmosfera(raiz);
            raiz.GetComponent<Image>().raycastTarget = true;
            Navegacao();
            Conteudo();
            Mostrar(0);
        }

        void Navegacao()
        {
            var nav = Kit.Novo("Navegacao", raiz);
            nav.anchorMin = Vector2.zero; nav.anchorMax = new Vector2(0, 1); nav.pivot = new Vector2(0, 0.5f);
            nav.sizeDelta = new Vector2(320, 0);
            Kit.Fundo(nav, null, Theme.Painel);
            var col = Kit.Coluna(nav, 4);
            Kit.Pad(col, 24, 24, 32, 24);

            var cab = Kit.Novo("Cabecalho", nav);
            Kit.Linha(cab, 10);
            Kit.Tam(cab, -1, 48);
            Botao(cab, "btVoltar", Ico.Voltar, Estilo.Icone, null, null, altura: 44);
            Kit.Tam(Kit.Texto(cab, "Configurações", 28, Theme.Texto, true), -1, 44, 1);
            Kit.Espaco(nav, 20);

            for (int i = 0; i < Categorias.Length; i++)
            {
                int idx = i;
                _abas.Add(Kit.NovoBotao(nav, Categorias[i].nome, Categorias[i].icone, Estilo.Fantasma, () => Mostrar(idx), 44));
            }

            Kit.Espaco(nav, 20);
            Botao(nav, "btReset", Ico.Reset, Estilo.Perigo, "Restaurar padrões", null, altura: 40);

            Kit.Espaco(nav, -1, -1, true);
            Legenda(nav);
        }

        void Conteudo()
        {
            var area = Kit.Novo("Conteudo", raiz);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(320 + 40, 32); area.offsetMax = new Vector2(-40, -32);
            Kit.Coluna(area, 12);

            var aviso = Kit.Novo("AvisoInGame", area);
            Kit.Fundo(aviso, Theme.Solido, Theme.Tingir(Theme.Painel, Theme.Perigo, 0.12f));
            Kit.Marca(aviso, Theme.Perigo, 3);
            var ahl = Kit.Linha(aviso, 12);
            Kit.Pad(ahl, 16, 16, 12, 12);
            Kit.Icone(aviso, Ico.Alerta, 18, Theme.Perigo);
            var at = Texto(aviso, "painel in game/AVISO INFERIOR IN-GAME", 14, Theme.Texto, alvoVisivel: aviso.gameObject);
            at.verticalOverflow = VerticalWrapMode.Overflow;
            Kit.Tam(at, -1, -1, 1);

            var cartao = Kit.Novo("Cartao", area);
            Kit.Tam(cartao, -1, -1, 1, 1);
            Kit.Fundo(cartao, Theme.Solido, Theme.Painel);
            var ccol = Kit.Coluna(cartao, 0);
            Kit.Pad(ccol, 32, 16, 8, 24);
            var conteudo = Kit.Lista(cartao, out _scroll, 6);

            var origem = O(Cont);
            System.Action[] specs = { Graficos, Iluminacao, Desempenho, Mundo, Direcao, CameraEEspelhos, Som, Avancado };
            foreach (var spec in specs)
            {
                var bloco = Kit.Novo("Categoria", conteudo);
                Kit.Coluna(bloco, 6);
                bloco.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                Comecar(bloco, origem, () => origem);
                spec();
                _blocos.Add(bloco.gameObject);
            }
        }

        internal int TotalCategorias => _blocos.Count;

        internal void Mostrar(int i)
        {
            if (i == _atual) return;
            _atual = i;
            for (int k = 0; k < _blocos.Count; k++)
            {
                _blocos[k].SetActive(k == i);
                _abas[k].Selecionado(k == i);
            }
            _scroll.verticalNormalizedPosition = 1f;
            // Seções da categoria entram em cascata.
            var b = _blocos[i].transform;
            var secoes = new List<GameObject>();
            for (int k = 0; k < b.childCount && secoes.Count < 14; k++) if (b.GetChild(k).gameObject.activeSelf) secoes.Add(b.GetChild(k).gameObject);
            Anim.Cascata(secoes, 0.045f, 0.5f, 14, 36f);
        }

        // ---------- categorias ----------

        void Graficos()
        {
            OpSecao("Qualidade");
            Predefinicoes();
            OpOpcoes("Qualidade da engine", "t presets graficos da engine", false,
                "tg quality 0", "tg quality 1", "tg quality 2", "tg quality 3", "tg quality 4", "tg quality 5", "tg quality 6");
            OpAjuda("txt descricaoVsync");

            OpSecao("Resolução e FPS");
            OpOpcoes("Resolução de renderização", "t resolucao", false, "tg resolucao 100", "tg resolucao 75", "tg resolucao 50", "tg resolucao 25");
            OpChave("tg res dinamica xframe", "Resolução dinâmica (XFrame)");
            OpAjuda("txt Dist Vis (4)");
            OpOpcoes("Limite de FPS", "t limite fps", false, "tg fps 24", "tg fps 30", "tg fps 60", "tg fps 90", "tg fps 999");
            OpChave("tg anti serrilhado", "Suavizar bordas (anti-serrilhado)");
            OpOpcoes("Modo do anti-serrilhado", "t modo antialiasing", false, "tg antialias nenhum", "tg antialias fxaa", "tg antialias smaa", "tg antialias taa");

            OpSecao("Detalhes");
            OpSlider("sliderVisibilidade", "Distância visível", "t descricao visibilidade", "t visibilidade metros", gpu: true);
            OpChave("tg arvores melhores", "Árvores melhores");
            OpOpcoes("Qualidade da água", "t qualidade da agua", false, "tg agua baixa", "tg agua media", "tg agua alta");

            OpSecao("Sombras e reflexos");
            OpChave("tg sombras de dia", "Sombras de dia", gpu: true);
            OpChave("tg sombras de noite", "Sombras à noite", gpu: true);
            OpChave("tg reflexos", "Reflexos na carroceria", gpu: true);

            OpSecao("Clima e efeitos");
            OpChave("tg chuva melhor", "Chuva detalhada", gpu: true);
            OpChave("tg fog", "Neblina", gpu: true);
            OpChave("tg raios e trovoes", "Raios e trovões");
            OpChave("tg particulas fumaca", "Fumaça do escapamento");
            OpOpcoes("Pós-processamento", "t efeitos", false, "tg nenhum efeito", "tg efeitos sleek", "tg efeitos unity", "tg efeitos Teste");
            OpChave("tg bloom glares flares", "Bloom e reflexos de lente");
            OpChave("tg corrigir iluminacao ao usar efeitos no celular", "Corrigir iluminação dos efeitos (celular)");

            OpSecao("Modelos");
            OpOpcoes("Pessoas", "t modelos das pessoas", false, "tg pessoas low", "tg pessoas med", "tg pessoas high", "tg pessoas extras");
            OpOpcoes("Carros do tráfego", "t carros do trafego", false, "tg carros simples", "tg carros medios", "tg carros detalhados");
            OpOpcoes("Ônibus do tráfego", "t modelos dos onibus do trafego", false, "tg onibus simples", "tg onibus medios", "tg onibus detalhados");
        }

        // Atalhos de qualidade (só existem no menu principal; somem na partida).
        void Predefinicoes()
        {
            var botoes = new[] { "bt pc baixo", "bt pc medio", "bt pc alto", "bt pc ultra", "bt android super leve", "bt android leve", "bt android bom" };
            var linha = Kit.Novo("Predefinicoes", SecaoAtual());
            var hl = Kit.Linha(linha, 6);
            hl.childForceExpandWidth = true;
            Kit.Pad(hl, 0, 0, 10, 10);
            Kit.Tam(linha, -1, 56);
            Kit.Tam(Kit.Texto(linha, "Predefinição", 15, Theme.Texto), 180, 36);
            foreach (var b in botoes)
            {
                var bt = Botao(linha, b, null, Estilo.Secundario, b, altura: 36, alin: TextAnchor.MiddleCenter);
                Kit.Tam(bt.btn, -1, 36, 1);
            }
            Sync(() =>
            {
                bool algum = false;
                for (int i = 1; i < linha.childCount; i++) if (linha.GetChild(i).gameObject.activeSelf) { algum = true; break; }
                if (linha.gameObject.activeSelf != algum) linha.gameObject.SetActive(algum);
            });
        }

        void Iluminacao()
        {
            OpSecao("Céu e ambiente");
            OpOpcoes("Dia e noite", "t ambiente dia e noite", false, "tg ceu estatico", "tg enviro sem luz real", "tg enviro com luz real");
            OpOpcoes("Noite com céu estático", "t modo de noite", false, "tg noite clara", "tg noite escura", "tg noite dinamica");
            OpChave("tg cor do ambiente baseada no ceu", "Cor do ambiente baseada no céu");
            OpAjuda("t aviso cor ambiente");

            OpSecao("Luzes");
            OpChave("tg luz real deferred", "Luzes em tempo real", gpu: true);
            OpGrupo("Fontes de luz");
            OpChave("tg luz postes", "Postes e cenário");
            OpChave("tg luz semaforos", "Semáforos");
            OpChave("tg luz farois", "Faróis do seu veículo");
            OpChave("tg luz farois AI", "Faróis do tráfego");
            OpChave("tg luz internas", "Luzes internas do veículo");
            OpChave("tg luz portas", "Luzes das portas");
            OpChave("tg otimizar luzes", "Otimizar luzes distantes");
            OpAjuda("t aviso peso luz");

            OpSecao("Experimental");
            OpChave("tg Radiant GI", "Iluminação global indireta (Radiant GI)", gpu: true);
        }

        void Desempenho()
        {
            OpSecao("Otimizador automático");
            OpChave("tg priorizar performance", "Priorizar desempenho automaticamente");
            OpOpcoes("Intensidade", null, false, "tg performance opcoes suaves", "tg performance opcoes fortes");
            OpChave("tg performance opcao salvar configs", "Manter as mudanças automáticas");
            OpAjuda("t priorizar performance");

            OpSecao("Texturas e mods");
            OpChave("tg otimizar mods automaticamente", "Otimizar texturas dos mods automaticamente");
            OpChave("tgComprimirTexturasRuntime", "Compactar texturas ao carregar");
            OpAcao("botaoOtimizarSkinsNativos", "Otimizar pinturas dos veículos nativos", "Otimizar");
            OpAcao("botaoOtimizarSkinsAI", "Otimizar pinturas do tráfego", "Otimizar");
            OpAcao("botaoOtimizarLetreirosPlacas", "Otimizar letreiros e placas", "Otimizar");
            OpAjuda("t otimizar tex");

            OpSecao("Memória e carregamento");
            OpChave("tg cache de objetos na ram", "Manter objetos na memória RAM");
            OpAjuda("t usar cache de objetos na ram");
            OpChave("tgUsarGCIncremental", "Coleta de lixo incremental");
            OpChave("tgDesativarGCNoCarregamento", "Pausar a coleta de lixo durante o carregamento");

            OpSecao("Objetos distantes");
            OpChave("tgUsarGrid", "Otimizar objetos distantes");
            OpChave("tgUsarGridArvores", "Grade de otimização das árvores");
            OpChave("tgUsarTriggerArvores", "Otimizar árvores distantes por proximidade");

            OpSecao("Renderização");
            OpOpcoes("Standard Shader nos mods", "t usar standard shader nos mods", false,
                "tg standardShader Por Mod", "tg standardShader Sempre Off", "tg standardShader Sempre On");
            OpOpcoes("Brilho padrão dos materiais", "t smooth std", false, "tg smoothstd0", "tg smoothstd1", "tg smoothstd2");
            OpChave("tg vertex lit", "Forçar renderização simples (VertexLit)");
            OpAjuda("t vertex lit");
        }

        void Mundo()
        {
            OpSecao("Tráfego");
            OpSlider("slider max carros", "Máximo de carros", "t maximo de carros", "tvis max carros");
            OpSlider("slider max onibus", "Máximo de ônibus", "t maximo de onibus", "tvis max onibus");
            OpSlider("slider max carros estacionados", "Carros estacionados", "t porcentagem carros estacionados", "tvis max carros estacionados");
            OpChave("tg ciclistas", "Ciclistas");
            OpChave("tg onibus estacionados", "Ônibus estacionados");
            OpChave("tg trens no cenario", "Trens no cenário");

            OpSecao("Pessoas");
            OpSlider("slider max pedestres", "Máximo de pedestres", "t maximo pedestres", "tvis max peds", gpu: true);
            OpSlider("slider porcentagem passageiros", "Passageiros nos pontos", "t porcentagem passageiros", "tvis porcentagem passageiros", gpu: true);
            OpChave("tg pessoas no cenario", "Pessoas paradas no cenário", gpu: true);
            OpChave("tg pessoas com guarda chuva", "Guarda-chuva quando chove");

            OpSecao("Operação");
            OpChave("tg trava das portas", "Trava das portas");
            OpChave("tg pontos a direita nos corredores", "Pontos à direita nos corredores");
            OpChave("tg colisao", "Colisão entre veículos");
            OpAjuda("t colisao aviso");
        }

        void Direcao()
        {
            OpSecao("Assistências");
            OpChave("tg assistente de direcao", "Assistente de direção");
            OpChave("tg abs", "ABS (freio antitravamento)");
            OpChave("tg esc", "ESC (controle de estabilidade)");
            OpChave("tg tcs", "TCS (controle de tração)");
            OpChave("tg asr", "ASR (antipatinagem)");

            OpSecao("Pedais");
            OpChave("tg acelerador progressivo", "Acelerador progressivo");
            OpChave("tg suavizar acelerador", "Suavizar acelerador");
            OpChave("tg freio progressivo", "Freio progressivo");
            OpSlider("slider multiplicador freio g29", "Força do freio em pedais duros", "t multiplicador freio g29");
            OpChave("tg inverter acelerador e embreagem thrustmaster", "Inverter acelerador e embreagem (Thrustmaster)");
            OpChave("tgDesativaLogitech", "Ignorar o SDK da Logitech");

            OpSecao("Setas e câmbio");
            OpChave("tg desligar setas automaticamente", "Desligar setas automaticamente");
            OpChave("tg setas cam manual", "Trocar marchas com as setas");

            OpSecao("Física");
            OpChave("tg fisica 50 hz", "Física a 50 Hz");
            OpChave("tg mais substeps", "Mais subpassos de física");
            OpChave("tg rk4", "Integração Runge-Kutta 4 (RK4)");
        }

        void CameraEEspelhos()
        {
            OpSecao("Câmera");
            OpChave("tg movimento da cabeca", "Movimento da cabeça");
            OpChave("tg girar camera com volante", "Girar a câmera com o volante");
            OpChave("tgGirarCamComMouse", "Girar a câmera com o mouse");
            OpChave("tg cam. volta sozinha", "Câmera interna volta ao centro");
            OpChave("tg Permitir Fov Exagerado", "Permitir campo de visão exagerado");

            OpSecao("Espelhos");
            OpChave("tg espelho esquerdo", "Espelho esquerdo");
            OpChave("tg espelho direito", "Espelho direito");
            OpChave("tg espelho meio", "Espelho central", gpu: true);
            OpChave("tg espelhos de longa distancia", "Espelhos de longa distância", gpu: true);
            OpChave("tg otimizar espelhos", "Otimizar espelhos");
        }

        void Som()
        {
            OpSecao("Efeitos sonoros");
            OpChave("tg som trafego", "Veículos do tráfego");
            OpChave("tg som ambiente", "Som ambiente");
            OpChave("tg som aviso parada solicitada", "Aviso de parada solicitada");
            OpChave("tg som trepidacoes", "Trepidações do veículo");
            OpChave("tg som pisada no freio", "Pisada no freio");
            OpChave("tg som validador e catraca", "Validador e catraca");
            OpAjuda("t aviso opcoes som");

            OpSecao("Passageiros");
            OpChave("tg passageiros conversando entre si", "Conversas entre passageiros");
            OpChave("tg passageiros conversando com motorista", "Passageiros falam com o motorista");

            OpSecao("Falas dos passageiros");
            OpAjuda("t descricao frequencia das falas");
            OpSlider("slider saudacoes", "Saudações", "t frequencia das falas", chave: "tgVozSaudacoes");
            OpSlider("slider despedidas", "Despedidas", chave: "tgVozDespedidas");
            OpSlider("slider passou do ponto", "Passou do ponto", chave: "tgVozPassouDoPonto");
            OpSlider("slider velocidade alta", "Velocidade alta", chave: "tgVozVelocidadeAlta");
            OpSlider("slider velocidade baixa", "Velocidade baixa", chave: "tgVozVelocidadeBaixa");
            OpSlider("slider reclamacoes", "Reclamações", chave: "tgVozReclamacoes");
            OpSlider("slider freadas", "Freadas bruscas", chave: "tgVozFreadas");
        }

        void Avancado()
        {
            OpSecao("Desenvolvimento");
            OpChave("tg opcoes desenvolvedor", "Opções de depuração para desenvolvedores");
            OpChave("tg destravar opcoes ocultas", "Destravar opções ocultas");
            OpAjuda("t aviso opcoes ocultas");
            OpChave("tg mostrar news home", "Mostrar novidades na tela inicial");
            // Tudo o que o jogo tiver e não foi colocado acima (opções novas de versões futuras).
            OpRestantes(new HashSet<string> { "btAjuda" }, "Outras opções");
        }

        // ---------- legenda ----------

        // Legenda dos marcadores usados nas linhas (os mesmos componentes, não o texto do jogo).
        static void Legenda(Transform pai)
        {
            Kit.Tam(Kit.Rotulo(pai, "Impacto no desempenho"), -1, 22);
            ItemLegenda(pai, Kit.Impacto(null, Theme.ImpactoLeve), "Leve");
            ItemLegenda(pai, Kit.Impacto(null, Theme.ImpactoMedio), "Médio");
            ItemLegenda(pai, Kit.Impacto(null, Theme.ImpactoPesado), "Pesado");
            Kit.Espaco(pai, 6);
            ItemLegenda(pai, Kit.Selo(null, "GPU"), "Exige placa de vídeo dedicada");
            ItemLegenda(pai, Kit.Icone(null, Ico.Estrela, 14, Theme.Dado), "Recurso para colaboradores");
        }

        static void ItemLegenda(Transform pai, Component marca, string texto)
        {
            var linha = Kit.Novo("Legenda", pai);
            Kit.Linha(linha, 10, 0, TextAnchor.MiddleLeft);
            Kit.Tam(linha, -1, 22);
            var slot = Kit.Novo("Marca", linha);
            Kit.Linha(slot, 0, 0, TextAnchor.MiddleLeft);
            Kit.Tam(slot, 40, 22);
            marca.transform.SetParent(slot, false);
            Kit.Texto(linha, texto, 13, Theme.TextoMudo);
        }
    }
}
