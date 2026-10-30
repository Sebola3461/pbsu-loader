using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Layout comum das telas de escolha (ônibus, pintura, mapa, linha, carga), no padrão da
    // garagem do DiRT Rally 2.0: cabeçalho com migalha; coluna esquerda com etapa, busca e lista;
    // prévia ao centro; coluna direita com nome, números e a ação principal; faixa de status no rodapé.
    // A lista substitui o Dropdown do jogo: escolher um item define dropdown.value, o que dispara
    // exatamente a mesma lógica de quando o jogador usa o dropdown original.
    internal abstract class TelaSelecao : Tela
    {
        protected abstract string Titulo { get; }
        protected override bool TransicaoCheia => true;
        // O botão de avançar leva a outra tela cheia (a linha leva ao carregamento, que já cobre a troca).
        protected virtual bool AvancarTrocaDeTela => true;
        // Etapas da nova viagem mostradas no cabeçalho, e a posição desta tela nelas.
        protected virtual string[] Fluxo => new[] { "Veículo", "Pintura", "Mapa", "Linha" };
        protected abstract int Passo { get; }
        protected abstract Dropdown Lista();
        protected abstract RawImage Previa();
        protected virtual InputField Filtro() => null;

        protected abstract Kit.Botao CriarVoltar(Transform pai);
        protected abstract Kit.Botao CriarSeta(Transform pai, int direcao);
        protected abstract Kit.Botao CriarAvancar(Transform pai);
        protected virtual void AcoesLista(Transform barra) { }   // ícones no rodapé da lista
        protected virtual void Detalhes(Transform pai) { }       // link do autor, descrição
        protected virtual void Opcoes(Transform pai) { }         // chaves e botões de opções
        protected virtual void Sobreposicoes() { }               // modais
        protected virtual string TextoStatus() => null;

        const float M = 28, G = 16, Topo = 72 + 16, Base = Kit.AlturaRodape + 16;
        const float LarguraLista = 440, LarguraDetalhes = 420;

        RectTransform _conteudoLista, _areaPrevia, _previaRt, _previa3DRt;
        ScrollRect _scroll;
        RawImage _previa, _previa3D;
        CanvasGroup _grupoPrevia2D;
        Text _carregando3D;
        float _mistura3D;

        // Prévia 3D (ônibus girando): qual ônibus (.ini) e skin (arquivo; null = a padrão). null = só imagem.
        protected virtual (string ini, string skin) Alvo3D() => (null, null);
        Text _contagem;
        string _posicao = "--";
        // Itens criados (podem sobrar, inativos, quando a lista encolhe) e os dados da lista atual.
        // A lista pode ter centenas de itens (ônibus e pinturas de mod): criar todos num frame só
        // travava a troca de tela. Os primeiros saem na hora; o resto é criado aos poucos, com
        // orçamento de tempo por frame (maior enquanto a transição cobre a tela).
        readonly List<Kit.ItemLista> _itens = new();
        readonly List<(string nome, string meta)> _dados = new();
        bool _rolarPendente;
        const int LinhasNaHora = 16;
        string _assinatura;
        Kit.Botao _voltar;
        internal void ApertarVoltar() => _voltar?.btn.onClick.Invoke();
        int _selecionado = -1;
        float _proxChecagem;

        protected override void Construir()
        {
            Kit.Atmosfera(raiz);

            var cab = Kit.Cabecalho(raiz, "Proton Bus", "Nova viagem", out _, out var acoes);
            _voltar = Navega(CriarVoltar(acoes));
            // Etapas centralizadas no cabeçalho, entre a identidade e o relógio.
            int i = cab.Find("Identidade").GetSiblingIndex();
            Kit.Espaco(cab, -1, -1, true).SetSiblingIndex(i + 1);
            Kit.Etapas(cab, Fluxo, Passo).SetSiblingIndex(i + 2);

            ColunaLista();
            ColunaDetalhes();
            Visor();
            Rodape();

            Sync(AtualizarLista);
            Sobreposicoes();
        }

        // ---------- colunas ----------

        RectTransform Coluna(string nome, float x0, float largura, bool daDireita)
        {
            var rt = Kit.Novo(nome, raiz);
            rt.anchorMin = new Vector2(daDireita ? 1 : 0, 0); rt.anchorMax = new Vector2(daDireita ? 1 : 0, 1);
            rt.pivot = new Vector2(daDireita ? 1 : 0, 0.5f);
            rt.offsetMin = new Vector2(daDireita ? -x0 - largura : x0, Base);
            rt.offsetMax = new Vector2(daDireita ? -x0 : x0 + largura, -Topo);
            return rt;
        }

        void ColunaLista()
        {
            var col = Coluna("Lista", M, LarguraLista, false);
            Kit.Fundo(col, Theme.Solido, Theme.Painel);
            var vl = Kit.Coluna(col, 12);
            Kit.Pad(vl, 18, 12, 20, 16);

            var cab = Kit.Novo("Titulo", col);
            Kit.Coluna(cab, 2);
            Kit.Tam(Kit.Trilha(cab, $"Etapa {Passo + 1} de {Fluxo.Length}", 12), -1, 16);
            Kit.Tam(Kit.Texto(cab, Titulo, 34, Theme.Texto, true), -1, 40);

            if (TemFiltro) Busca(col);

            var info = Kit.Novo("Info", col);
            Kit.Linha(info, 8);
            Kit.Tam(info, -1, 16);
            _contagem = Kit.Rotulo(info, "0 itens");
            Kit.Tam(_contagem, -1, 16, 1);

            _conteudoLista = Kit.Lista(col, out _scroll);

            var barra = Kit.Novo("Acoes", col);
            var bl = Kit.Linha(barra, 8);
            _ = bl;
            Kit.Tam(barra, -1, 40);
            AcoesLista(barra);
            Sync(() =>
            {
                bool alguma = false;
                for (int i = 0; i < barra.childCount; i++) if (barra.GetChild(i).gameObject.activeSelf) { alguma = true; break; }
                if (barra.gameObject.activeSelf != alguma) barra.gameObject.SetActive(alguma);
            });
        }

        void ColunaDetalhes()
        {
            var col = Coluna("Detalhes", M, LarguraDetalhes, true);
            Kit.Fundo(col, Theme.Solido, Theme.Painel);
            var vl = Kit.Coluna(col, 14);
            Kit.Pad(vl, 20, 20, 20, 20);

            Kit.Tam(Kit.Trilha(col, "Selecionado", 12), -1, 16);
            var nome = TextoF(col, De(() => Lista()?.captionText), 26, Theme.Texto, true, formato: NomeLegivel);
            nome.verticalOverflow = VerticalWrapMode.Overflow;
            nome.lineSpacing = 1.0f;
            Kit.Divisoria(col);

            // Números do item, como os "Max Power / 230 bhp" da garagem: rótulo pequeno, valor grande.
            var grade = Kit.Novo("Metricas", col);
            Kit.Linha(grade, 18, 0, TextAnchor.UpperLeft);
            Kit.Tam(grade, -1, 56);
            var origem = Metrica(grade, "Origem", "--");
            var divisor = Kit.DivisoriaV(grade, 48).gameObject;
            var formato = Metrica(grade, "Formato", "--");
            var blocoFormato = formato.transform.parent.gameObject;
            Sync(() =>
            {
                var dd = Lista();
                if (dd == null) return;
                int n = dd.options.Count;
                var s = n > 0 ? $"{dd.value + 1:00} / {n:00}" : "--";
                if (_posicao != s) _posicao = s;
                var cap = dd.captionText != null ? dd.captionText.text : null;
                var o = TipoDe(cap);
                if (origem.text != o) origem.text = o;
                var f = Extensao(cap);
                bool temFormato = f != null; // linhas não têm arquivo com extensão: some em vez de mostrar "--"
                if (blocoFormato.activeSelf != temFormato) { blocoFormato.SetActive(temFormato); divisor.SetActive(temFormato); }
                if (temFormato && formato.text != f) formato.text = f;
            });

            Detalhes(col);
            Opcoes(col);
            Kit.Espaco(col, -1, -1, true);

            var cta = CriarAvancar(col);
            if (AvancarTrocaDeTela) Navega(cta);
            Kit.Tam(cta.btn, -1, 52);
        }

        // Métrica: rótulo pequeno esmaecido em cima, número grande, reto e em negrito embaixo
        // (números em itálico perdem legibilidade).
        static Text Metrica(Transform pai, string rotulo, string valor)
        {
            var c = Kit.Novo("Metrica", pai);
            Kit.Coluna(c, 2);
            Kit.Tam(c, 120, 56);
            Kit.Tam(Kit.Texto(c, rotulo, 13, Theme.TextoMudo), -1, 16);
            var v = Kit.Texto(c, valor, 26, Theme.Texto, true);
            v.fontStyle = FontStyle.Bold;
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(v, -1, 32);
            return v;
        }

        void Visor()
        {
            var v = Kit.Novo("Visor", raiz);
            v.anchorMin = Vector2.zero; v.anchorMax = Vector2.one;
            v.offsetMin = new Vector2(M + LarguraLista + G, Base);
            v.offsetMax = new Vector2(-(M + LarguraDetalhes + G), -Topo);
            Kit.Visor(v, "Prévia", null);
            _areaPrevia = v;

            _previaRt = Kit.Ancorar(Kit.Novo("Imagem", v), new Vector2(0.5f, 0.5f), new Vector2(0, 18), new Vector2(100, 100));
            _previa = _previaRt.gameObject.AddComponent<RawImage>();
            _previa.raycastTarget = false;
            _grupoPrevia2D = _previaRt.gameObject.AddComponent<CanvasGroup>();
            // Moldura branca fina, como o tile selecionado do DiRT.
            Kit.Contorno(_previaRt, Theme.TextoDim);

            // Ônibus em 3D: ocupa a área inteira (sem moldura), entre o rótulo e a navegação.
            _previa3DRt = Kit.Novo("Previa3D", v);
            _previa3DRt.anchorMin = Vector2.zero; _previa3DRt.anchorMax = Vector2.one;
            _previa3DRt.offsetMin = new Vector2(0, 64); _previa3DRt.offsetMax = new Vector2(0, -36);
            _previa3D = _previa3DRt.gameObject.AddComponent<RawImage>();
            _previa3D.raycastTarget = false;
            _previa3D.enabled = false;
            _carregando3D = Kit.Texto(v, Theme.Rotular("Carregando modelo"), 13, Theme.TextoMudo, true, TextAnchor.MiddleCenter);
            Kit.Encher(_carregando3D.rectTransform);
            _carregando3D.gameObject.SetActive(false);
            Sync(AtualizarPrevia);

            // Barra de navegação: ‹  07 / 23  ›
            var nav = Kit.Ancorar(Kit.Novo("Navegacao", v), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(0, 40));
            Kit.Linha(nav, 14, 0, TextAnchor.MiddleCenter);
            nav.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            CriarSeta(nav, -1);
            var pos = Kit.Texto(nav, "--", 16, Theme.Texto, true, TextAnchor.MiddleCenter);
            Kit.Tam(pos, 96, 40);
            CriarSeta(nav, 1);
            Sync(() => { if (pos.text != _posicao) pos.text = _posicao; });
        }

        void Rodape()
        {
            Kit.Rodape(raiz, () =>
            {
                var extra = TextoStatus();
                return "Seleção de " + Titulo + (string.IsNullOrWhiteSpace(extra) ? "" : "  ·  " + Orig.Limpo(extra));
            });
        }

        bool TemFiltro { get { try { return Filtro() != null; } catch { return false; } } }

        void Busca(Transform pai)
        {
            var campo = Kit.Campo(pai, "Buscar...", s =>
            {
                var orig = Filtro();
                if (orig != null && orig.text != s) orig.text = s; // o jogo filtra no onValueChanged do campo dele
            }, s => Filtro()?.onEndEdit.Invoke(s));
            Sync(() =>
            {
                var orig = Filtro();
                if (orig == null) return;
                var ph = orig.placeholder != null ? orig.placeholder.TryCast<Text>() : null;
                var nosso = campo.placeholder.TryCast<Text>();
                // O placeholder do jogo é longo ("Filtro... (digite um trecho pequeno...)"): fica só o começo.
                if (ph != null && nosso != null)
                {
                    var s = ph.text;
                    int corte = s.IndexOf('(');
                    if (corte > 0) s = s.Substring(0, corte).Trim();
                    if (nosso.text != s) nosso.text = s;
                }
                if (!campo.isFocused && campo.text != orig.text) campo.text = orig.text;
            });
        }

        void AtualizarPrevia()
        {
            AtualizarPrevia3D();
            var o = Previa();
            var tex = o != null ? o.texture : null;
            if (_previa.texture != tex) _previa.texture = tex;
            _previa.enabled = tex != null;
            // Sem imagem, a moldura também some (ficava um quadro vazio no centro).
            if (tex == null) { _grupoPrevia2D.alpha = 0f; return; }
            var area = _areaPrevia.rect.size - new Vector2(96, 140);
            if (area.x <= 0 || area.y <= 0) return;
            float ar = tex.height > 0 ? tex.width / (float)tex.height : 2f;
            var tam = area.x / ar <= area.y ? new Vector2(area.x, area.x / ar) : new Vector2(area.y * ar, area.y);
            if ((_previaRt.sizeDelta - tam).sqrMagnitude > 1f) _previaRt.sizeDelta = tam;
        }

        // A imagem do jogo fica enquanto o 3D carrega (ou para ônibus sem modelo); quando o ônibus fica
        // pronto, as duas cruzam: a imagem some e o ônibus surge crescendo de leve.
        void AtualizarPrevia3D()
        {
            (string ini, string skin) alvo;
            try { alvo = Alvo3D(); } catch { alvo = (null, null); }
            if (alvo.ini != null) Garagem.Previa3D.Pedir(alvo.ini, alvo.skin, _previa3DRt);
            bool pronto = alvo.ini != null && Garagem.Previa3D.Pronto(alvo.ini) && Garagem.Previa3D.Saida != null;
            bool carregando = Garagem.Previa3D.Carregando(alvo.ini) && (Garagem.Previa3D.Pref == null || Garagem.Previa3D.Pref.Value);
            float ac = carregando && !pronto ? 0.35f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f) : 0f;
            if (_carregando3D.gameObject.activeSelf != (ac > 0f)) _carregando3D.gameObject.SetActive(ac > 0f);
            if (ac > 0f) _carregando3D.color = new Color(Theme.TextoMudo.r, Theme.TextoMudo.g, Theme.TextoMudo.b, ac + 0.3f);
            _mistura3D = Mathf.MoveTowards(_mistura3D, pronto ? 1f : 0f, Anim.Dt / (pronto ? 0.45f : 0.2f));
            float e = Anim.Expo(_mistura3D);
            bool mostra = _mistura3D > 0.001f;
            if (_previa3D.enabled != mostra) _previa3D.enabled = mostra;
            if (mostra)
            {
                if (_previa3D.texture != Garagem.Previa3D.Saida) _previa3D.texture = Garagem.Previa3D.Saida;
                _previa3D.color = new Color(1, 1, 1, e);
                // Troca de skin: pulso curto (cresce 2,5% e volta), para a troca não ser um corte seco.
                float tp = (Time.unscaledTime - Garagem.Previa3D.SkinTrocadaEm) / 0.4f;
                float pulso = tp >= 0f && tp < 1f ? Mathf.Sin(tp * Mathf.PI) * (1f - tp) : 0f;
                _previa3DRt.localScale = Vector3.one * (Mathf.Lerp(0.94f, 1f, e) + 0.025f * pulso);
            }
            // A imagem do jogo só aparece para ônibus sem prévia 3D (nativos, modelo ilegível).
            _grupoPrevia2D.alpha = carregando || pronto ? 0f : 1f - e;
        }

        void AtualizarLista()
        {
            var dd = Lista();
            if (dd == null) return;
            if (_itens.Count < _dados.Count) { Completar(Anim.Cobrindo ? 20f : 3f, null); _selecionado = -1; }
            else if (_rolarPendente)
            {
                _rolarPendente = false;
                int n = _dados.Count;
                if (n > 1) _scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - dd.value / (float)(n - 1));
            }
            if (Time.unscaledTime >= _proxChecagem)
            {
                _proxChecagem = Time.unscaledTime + 0.25f;
                var ops = dd.options;
                int n = ops.Count;
                string ass = n + "|" + (n > 0 ? ops[0].text + "|" + ops[n - 1].text : "");
                if (ass != _assinatura)
                {
                    _assinatura = ass;
                    Reconstruir(dd);
                    var c = Theme.Rotular(n == 1 ? "1 item" : n + " itens");
                    if (_contagem.text != c) _contagem.text = c;
                }
            }
            if (dd.value != _selecionado)
            {
                _selecionado = dd.value;
                for (int i = 0; i < _itens.Count; i++) _itens[i].Selecionado(i == _selecionado);
            }
        }

        // A lista do jogo mudou (cena nova, filtro, pasta atualizada): reaproveita os itens já criados
        // (só troca os textos) e cria os que faltam.
        void Reconstruir(Dropdown dd)
        {
            var ops = dd.options;
            _dados.Clear();
            for (int i = 0; i < ops.Count; i++)
            {
                var bruto = ops[i].text;
                _dados.Add((NomeCurto(bruto), MetaDe(bruto)));
            }
            var gos = new List<GameObject>();
            for (int i = 0; i < _itens.Count; i++)
            {
                var it = _itens[i];
                bool usa = i < _dados.Count;
                if (it.b.go.activeSelf != usa) it.b.go.SetActive(usa);
                if (!usa) continue;
                Preencher(it, i);
                if (gos.Count < LinhasNaHora) gos.Add(it.b.go);
            }
            Completar(0f, gos);
            _selecionado = -1;
            _rolarPendente = true;
            Anim.QuandoRevelar(() => Anim.EntrarLinhas(gos, AtrasoLinhas));
        }

        // Cria itens até completar a lista: os primeiros LinhasNaHora sempre (são os visíveis), o resto
        // enquanto couber em "orcamentoMs" neste frame.
        void Completar(float orcamentoMs, List<GameObject> gos)
        {
            var ini = System.Diagnostics.Stopwatch.GetTimestamp();
            double porMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
            while (_itens.Count < _dados.Count)
            {
                bool naHora = _itens.Count < LinhasNaHora;
                if (!naHora && (System.Diagnostics.Stopwatch.GetTimestamp() - ini) / porMs >= orcamentoMs) break;
                int idx = _itens.Count;
                var it = Kit.NovoItemLista(_conteudoLista, idx + 1, _dados[idx].nome, _dados[idx].meta, () =>
                {
                    var d = Lista();
                    if (d != null && d.value != idx) d.value = idx; // dispara o onValueChanged do jogo
                });
                _itens.Add(it);
                if (gos != null && gos.Count < LinhasNaHora) gos.Add(it.b.go);
            }
        }

        void Preencher(Kit.ItemLista it, int i)
        {
            var num = (i + 1).ToString("00");
            var (nome, meta) = _dados[i];
            if (it.indice.text != num) it.indice.text = num;
            if (it.nome.text != nome) it.nome.text = nome;
            if (it.meta.text != meta) it.meta.text = meta;
        }

        // ---------- nomes de arquivo -> nome + meta ----------

        static readonly string[] Extensoes = { ".ini", ".txt", ".map" };

        static string Extensao(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            foreach (var e in Extensoes) if (s.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return e.Substring(1).ToUpperInvariant();
            return null;
        }

        static string NomeCurto(string s)
        {
            s = NomeLegivel(s) ?? "";
            if (s.StartsWith("MOD ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            return s.Trim();
        }

        static string MetaDe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool mod = s.StartsWith("MOD ", StringComparison.OrdinalIgnoreCase);
            var ext = Extensao(s);
            var partes = new List<string> { mod ? "Mod" : "Padrão" };
            if (ext != null) partes.Add(ext);
            return Theme.Rotular(string.Join(" · ", partes));
        }

        static string TipoDe(string s) => string.IsNullOrEmpty(s) ? "--" :
            s.StartsWith("MOD ", StringComparison.OrdinalIgnoreCase) ? "Mod" : "Padrão";

        // ---------- utilidades para as telas ----------

        // Botão só de ícone com dica (para as ações da lista).
        protected Kit.Botao Icone(Transform pai, Func<Transform> alvo, string icone, string dica, Action direto = null)
        {
            var b = BotaoF(pai, alvo, icone, Estilo.Icone, null, null, null, direto, altura: 40);
            b.visual.dica = Kit.Dica(b.go.GetComponent<RectTransform>(), dica);
            return b;
        }

        // Link do autor do mod (Text com EventTrigger no jogo).
        protected void Link(Transform pai, Func<Transform> alvo, Func<Transform> texto)
        {
            var link = BotaoF(pai, alvo, Ico.Link, Estilo.Secundario, "Site do autor", texto, altura: 40, tamFonte: 12);
            link.maiusculas = false;
            link.rotulo.fontStyle = FontStyle.Normal; // endereço é dado, não comando
            VisivelF(link.go, texto);
        }
    }

    // ---------------- telas por caminho (dump conhecido) ----------------

    // Seleção de ônibus (LOAD1_cenaEscolhaOnibus).
    internal sealed class TelaOnibus : TelaSelecao
    {
        protected override string NomeCanvas => "Canvas CENA ONIBUS";
        protected override string Titulo => "Veículo";
        protected override int Passo => 0;
        protected override Dropdown Lista() => O("Dropdown")?.GetComponent<Dropdown>();
        protected override RawImage Previa() => O("img Bus")?.GetComponent<RawImage>();
        protected override InputField Filtro() => O("campoBusca")?.GetComponent<InputField>();

        // Veículo: o item escolhido na lista (só ônibus de mod têm arquivos para a prévia 3D).
        protected override (string ini, string skin) Alvo3D()
        {
            var dd = Lista();
            if (dd == null || dd.value < 0 || dd.value >= dd.options.Count) return (null, null);
            var ini = Garagem.Previa3D.IniDoItem(dd.options[dd.value].text);
            if (ini != null) Garagem.Previa3D.UltimoOnibus = ini;
            return (ini, null);
        }
        protected override string TextoStatus() => Orig.Texto(O("botoes CONFIGS/txtPasta"));
        protected override string[] Cobertos => new[]
        {
            "Image", "btOtimizarMod", "btInstalarMod", "btDesinstalaMod", "campoBusca", "Dropdown", "btNext", "img Bus",
            "btVolta", "btAvanca", "btAtualizaListaDaPasta", "btAjuda", "btVoltar", "link site autor",
            "tgUsarCambioManual", "aviso mods pesados", "btMostraHideConfigs", "botoes CONFIGS",
        };

        protected override Kit.Botao CriarVoltar(Transform pai) => Botao(pai, "btVoltar", Ico.Voltar, Estilo.Secundario, "Voltar", null, altura: 40);
        protected override Kit.Botao CriarSeta(Transform pai, int d) =>
            Botao(pai, d < 0 ? "btVolta" : "btAvanca", d < 0 ? Ico.SetaEsq : Ico.SetaDir, Estilo.Icone, null, null, altura: 40);
        protected override Kit.Botao CriarAvancar(Transform pai) =>
            Botao(pai, "btNext", Ico.Avancar, Estilo.Primario, "Escolher pintura", null, altura: 52);

        protected override void AcoesLista(Transform barra)
        {
            Icone(barra, P("btAtualizaListaDaPasta"), Ico.Atualizar, "Atualizar lista");
            Icone(barra, P("btInstalarMod"), Ico.Adicionar, "Instalar mod");
            Icone(barra, P("btDesinstalaMod"), Ico.Excluir, "Remover mod");
            Icone(barra, P("btOtimizarMod"), Ico.Ferramenta, "Otimizar texturas");
            Kit.Espaco(barra, -1, -1, true);
            Botao(barra, "btAjuda", Ico.Download, Estilo.Secundario, "Baixar mais", null, altura: 40);
        }

        protected override void Detalhes(Transform pai)
        {
            Link(pai, P("link site autor"), P("link site autor"));
        }

        protected override void Opcoes(Transform pai)
        {
            // Todas as opções do veículo (câmbio manual incluso) ficam no modal "Opções do veículo".
            Botao(pai, "btMostraHideConfigs", Ico.Config, Estilo.Secundario, "Opções do veículo", null, altura: 40);
        }

        // Grupo com rótulo e chaves (caminho, até, rótulo próprio). O rótulo some quando nenhuma
        // chave do grupo está visível (o jogo esconde opções que o veículo não tem).
        void GrupoDeChaves(Transform col, string titulo, params (string caminho, string ate, string rotulo)[] chaves)
        {
            var bloco = Kit.Novo(titulo, col);
            Kit.Coluna(bloco, 8);
            Kit.Tam(Kit.Rotulo(bloco, titulo), -1, 16);
            var lista = new List<Kit.Chave>();
            foreach (var (caminho, ate, rotulo) in chaves) lista.Add(Chave(bloco, caminho, ate, moldura: true, rotuloFixo: rotulo));
            Kit.Espaco(bloco, 4);
            Sync(() =>
            {
                bool alguma = lista.Exists(c => c.go.activeSelf);
                if (bloco.gameObject.activeSelf != alguma) bloco.gameObject.SetActive(alguma);
            });
        }

        // Modal "Configurações da seleção de ônibus" (painel "botoes CONFIGS", aberto pelo botão Opções).
        protected override void Sobreposicoes()
        {
            const string P_ = "botoes CONFIGS/";
            var card = Kit.Modal(raiz, 760, out var modal);
            Visivel(modal, "botoes CONFIGS");

            var cab = Kit.Novo("Cabecalho", card);
            Kit.Linha(cab, 12);
            Kit.Tam(cab, -1, 40);
            Kit.Tam(Kit.Texto(cab, "Opções do veículo", 22, Theme.Texto, true), -1, 40, 1);
            Botao(cab, P_ + "btFecharTela", Ico.Fechar, Estilo.Icone, null, null, altura: 36);
            Kit.Divisoria(card);

            var grade = Kit.Novo("Grade", card);
            var gl = Kit.Linha(grade, 10, 0, TextAnchor.UpperLeft);
            gl.childForceExpandWidth = true;
            var c1 = Kit.Novo("Col1", grade); Kit.Coluna(c1, 8); Kit.Tam(c1, -1, -1, 1);
            var c2 = Kit.Novo("Col2", grade); Kit.Coluna(c2, 8); Kit.Tam(c2, -1, -1, 1);
            // Direção primeiro, depois o que muda o veículo, depois desempenho e depuração.
            const string ATE = "botoes CONFIGS";
            GrupoDeChaves(c1, "Direção", ("tgUsarCambioManual", null, "Câmbio manual"), (P_ + "tgUsarEmbreagem", ATE, "Embreagem"),
                (P_ + "tgFreioDeMao", ATE, "Freio de estacionamento"), (P_ + "tgStartOn", ATE, "Começar com o motor ligado"));
            GrupoDeChaves(c1, "Depuração", (P_ + "tgCarregarConsoleDebug", ATE, "Console de depuração"));
            GrupoDeChaves(c2, "Veículo", (P_ + "tgUsarPlacas", ATE, "Placas"), (P_ + "tgUsarCobrador", ATE, "Cobrador"));
            GrupoDeChaves(c2, "Desempenho", (P_ + "tgUsarVersaoLite", ATE, "Modo Lite"), (P_ + "tgShadersLeves", ATE, "Shaders leves"),
                (P_ + "tgLimitarMetalico", ATE, "Limitar reflexos metálicos"));

            // Explicação do modo Lite (o que ele desliga).
            var nota = Texto(card, P_ + "txtAvisoVLite", 13, Theme.TextoMudo, ate: "botoes CONFIGS");
            nota.font = Theme.TextoCorrido; nota.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }

    // Seleção de pintura (LOAD2_cenaEscolhaSkinMod).
    internal sealed class TelaPintura : TelaSelecao
    {
        protected override string NomeCanvas => "Canvas CENA SKIN PASTA";
        protected override string Titulo => "Pintura";
        protected override int Passo => 1;
        protected override Dropdown Lista() => O("Dropdown")?.GetComponent<Dropdown>();
        protected override RawImage Previa() => O("img skin fundo")?.GetComponent<RawImage>();
        protected override string TextoStatus() => Orig.Texto(O("txtPasta"));
        protected override string[] Cobertos => new[]
        {
            "Image", "Dropdown", "Button", "img skin fundo", "btAvanca", "btVolta", "btAtualizaListaDaPasta",
            "txtPasta", "btVoltar", "btAjuda", "txtAnuncio",
        };

        protected override Kit.Botao CriarVoltar(Transform pai) => Botao(pai, "btVoltar", Ico.Voltar, Estilo.Secundario, "Voltar", null, altura: 40);
        protected override Kit.Botao CriarSeta(Transform pai, int d) =>
            Botao(pai, d < 0 ? "btVolta" : "btAvanca", d < 0 ? Ico.SetaEsq : Ico.SetaDir, Estilo.Icone, null, null, altura: 40);
        protected override Kit.Botao CriarAvancar(Transform pai) =>
            Botao(pai, "Button", Ico.Avancar, Estilo.Primario, "Escolher o mapa", null, altura: 52);

        // Pintura: o ônibus escolhido na tela anterior, com a skin selecionada na lista.
        Il2Cpp.cenaEscolhaSkinPastaMod _ctrl;

        protected override (string ini, string skin) Alvo3D()
        {
            if (_ctrl == null || _ctrl.WasCollected) _ctrl = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaSkinPastaMod>();
            string ini = Garagem.Previa3D.UltimoOnibus;
            if (_ctrl != null)
            {
                var bf = _ctrl.busFile;
                if (!string.IsNullOrEmpty(bf))
                {
                    var c = Garagem.Previa3D.IniDoArquivo(bf);
                    if (c != null) ini = c;
                }
            }
            if (ini == null) return (null, null);
            var dd = Lista();
            string skin = null;
            if (dd != null && dd.value >= 0 && dd.value < dd.options.Count && _ctrl != null)
            {
                var pasta = _ctrl.pastaDaSkinNoAparelho;
                string Arq(int i)
                {
                    if (string.IsNullOrEmpty(pasta) || i < 0 || i >= dd.options.Count) return null;
                    var nome = dd.options[i].text;
                    if (string.IsNullOrEmpty(nome)) return null;
                    var c = System.IO.Path.Combine(pasta, nome);
                    return System.IO.File.Exists(c) ? c : null;
                }
                skin = Arq(dd.value);
                // As vizinhas na lista já começam a decodificar: a troca para elas fica instantânea.
                Garagem.Previa3D.PreCarregarSkin(Arq(dd.value + 1));
                Garagem.Previa3D.PreCarregarSkin(Arq(dd.value - 1));
            }
            return (ini, skin);
        }

        protected override void AcoesLista(Transform barra) =>
            Icone(barra, P("btAtualizaListaDaPasta"), Ico.Atualizar, "Atualizar lista");
    }

    // ---------------- telas pelo controlador da cena ----------------

    // Vincula pelos campos públicos do script que controla a tela (cenaEscolhaMapa etc.) e pelos
    // botões cujo onClick chama métodos dele. Se o botão original não for achado, chama o método direto.
    internal abstract class TelaControlada<T> : TelaSelecao where T : MonoBehaviour
    {
        protected T ctrl;
        protected abstract Dropdown ListaDe(T c);
        protected abstract RawImage PreviaDe(T c);
        protected abstract string MetodoAvancar { get; }
        protected abstract string RotuloAvancar { get; }
        protected abstract void Avancar(T c);

        protected bool Vivo => ctrl != null && !ctrl.WasCollected;

        protected override Transform AcharRaiz()
        {
            var c = UnityEngine.Object.FindObjectOfType<T>();
            if (c == null) return null;
            var dd = ListaDe(c);
            var cv = dd != null ? dd.GetComponentInParent<Canvas>() : null;
            if (cv == null) return null;
            ctrl = c;
            return cv.rootCanvas.transform;
        }

        protected override Dropdown Lista() => Vivo ? ListaDe(ctrl) : null;
        protected override RawImage Previa() => Vivo ? PreviaDe(ctrl) : null;

        protected Kit.Botao Metodo(Transform pai, string metodo, int? arg, Action direto, string icone, Estilo estilo,
            string rotuloPadrao, bool espelharRotulo = true, float altura = 40, int tamFonte = 12) =>
            BotaoF(pai, Chama(metodo, arg), icone, estilo, rotuloPadrao, espelharRotulo ? Chama(metodo, arg) : null,
                null, () => { if (Vivo) direto(); }, altura, tamFonte);

        protected override Kit.Botao CriarVoltar(Transform pai) =>
            Metodo(pai, "clicouVoltar", null, () => Invocar("clicouVoltar"), Ico.Voltar, Estilo.Secundario, "Voltar", false, 40);

        protected override Kit.Botao CriarSeta(Transform pai, int d) =>
            Metodo(pai, "SetasVoltarAvancar", d, () => Invocar("SetasVoltarAvancar", d), d < 0 ? Ico.SetaEsq : Ico.SetaDir,
                Estilo.Icone, null, false, 40);

        protected override Kit.Botao CriarAvancar(Transform pai) =>
            Metodo(pai, MetodoAvancar, null, () => Avancar(ctrl), Ico.Avancar, Estilo.Primario, RotuloAvancar, false, 52, 15);

        protected void Invocar(string metodo, params object[] args)
        {
            if (!Vivo) return;
            var m = typeof(T).GetMethod(metodo);
            m?.Invoke(ctrl, args.Length == 0 ? null : args);
        }
    }

    // Seleção de mapa (cenaEscolhaMapa).
    internal sealed class TelaMapa : TelaControlada<Il2Cpp.cenaEscolhaMapa>
    {
        protected override string Titulo => "Mapa";
        protected override int Passo => 2;
        protected override Dropdown ListaDe(Il2Cpp.cenaEscolhaMapa c) => c.listaEscolhaMapa;
        protected override RawImage PreviaDe(Il2Cpp.cenaEscolhaMapa c) => c.imgDemoMapa;
        protected override InputField Filtro() => Vivo ? ctrl.filtroTextoInputField : null;
        protected override string MetodoAvancar => "SalvaMapaEVaiProEntrypoint";
        protected override string RotuloAvancar => "Escolher a linha";
        protected override void Avancar(Il2Cpp.cenaEscolhaMapa c) => c.SalvaMapaEVaiProEntrypoint();
        protected override string TextoStatus() => Vivo && ctrl.txtPasta != null ? ctrl.txtPasta.text : null;

        protected override IEnumerable<GameObject> Tratados()
        {
            yield return ctrl.botoesConfig;
            yield return ctrl.objPainelLoading;
            yield return ctrl.objAvisoRAM;
            yield return ctrl.objLinkSiteAutor;
        }

        protected override void AcoesLista(Transform barra)
        {
            Icone(barra, Chama("AtualizaListaDeMapas"), Ico.Atualizar, "Atualizar lista", () => ctrl.AtualizaListaDeMapas());
            Icone(barra, DeGo(() => ctrl.botaoInstalarMod), Ico.Adicionar, "Instalar mod");
            Icone(barra, DeGo(() => ctrl.botaoDesinstalarMod), Ico.Excluir, "Remover mod");
            Icone(barra, DeGo(() => ctrl.botaoOtimizarMod), Ico.Ferramenta, "Otimizar texturas");
            Kit.Espaco(barra, -1, -1, true);
            var baixar = DeGo(() => ctrl.botaoBaixarMaisMapas);
            BotaoF(barra, baixar, Ico.Download, Estilo.Secundario, "Baixar mais", null, altura: 40);
        }

        protected override void Detalhes(Transform pai)
        {
            Link(pai, DeGo(() => ctrl.objLinkSiteAutor), De(() => ctrl.objLinkSiteAutorTxt));
        }

        protected override void Opcoes(Transform pai)
        {
            var opcoes = DeGo(() => ctrl.botaoOpcoes);
            BotaoF(pai, opcoes, Ico.Config, Estilo.Secundario, "Opções do mapa", null, altura: 40);
        }

        protected override void Sobreposicoes()
        {
            var cfg = DeGo(() => ctrl.botoesConfig);
            var card = Kit.Modal(raiz, 640, out var modal);
            VisivelF(modal, cfg);

            var cab = Kit.Novo("Cabecalho", card);
            Kit.Linha(cab, 12);
            Kit.Tam(cab, -1, 40);
            Kit.Tam(Kit.Texto(cab, "Opções do mapa", 22, Theme.Texto, true), -1, 40, 1);
            BotaoF(cab, () => Orig.BotaoPorNome(cfg(), "fechar"), Ico.Fechar, Estilo.Icone, null,
                direto: () => { if (Vivo && ctrl.botoesConfig != null) ctrl.botoesConfig.SetActive(false); }, altura: 36);
            Kit.Divisoria(card);

            ChaveF(card, De(() => ctrl.usarDetalhesExtras), cfg, true);
            ChaveF(card, De(() => ctrl.usarSkinsDoMapa), cfg, true);
            ChaveF(card, De(() => ctrl.usarSkinsPersonalizadas), cfg, true);
            ChaveF(card, De(() => ctrl.tgDebugMapa), cfg, true);

            // "Carregando..." enquanto o jogo prepara o mapa
            var carregando = Kit.Modal(raiz, 460, out var overlay);
            VisivelF(overlay, DeGo(() => ctrl.objPainelLoading));
            Kit.Tam(Kit.Trilha(carregando, "Processando", 12), -1, 16);
            Kit.Tam(Kit.Texto(carregando, "Preparando o mapa", 22, Theme.Texto, true), -1, 30);
            var det = TextoF(carregando, DeGo(() => ctrl.objPainelLoading), 13, Theme.TextoMudo);
            det.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }

    // Seleção de linha (cenaEscolhaLinha).
    internal sealed class TelaLinha : TelaControlada<Il2Cpp.cenaEscolhaLinha>
    {
        protected override string Titulo => "Linha";
        protected override int Passo => 3;
        protected override Dropdown ListaDe(Il2Cpp.cenaEscolhaLinha c) => c.listaEscolhaLinha;
        protected override RawImage PreviaDe(Il2Cpp.cenaEscolhaLinha c) => c.imgDemoLinha;
        protected override string MetodoAvancar => "SalvaLinhaEVaiproMapa";
        protected override string RotuloAvancar => "Iniciar viagem";
        protected override bool AvancarTrocaDeTela => false;

        // A carga começa no clique: a tela nova aparece já, antes do painel de carregamento do jogo.
        protected override Kit.Botao CriarAvancar(Transform pai)
        {
            var b = base.CriarAvancar(pai);
            b.btn.onClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)Carregando.Antecipar);
            return b;
        }
        protected override void Avancar(Il2Cpp.cenaEscolhaLinha c) => c.SalvaLinhaEVaiproMapa();

        protected override void Detalhes(Transform pai)
        {
            var c = Kit.Cartao(pai, "Descrição", 14, 6);
            // Muitas linhas têm como "descrição" só o próprio nome: aí o cartão não acrescenta nada.
            var t = TextoF(c, De(() => ctrl.textoLinhaEscolhida), 13, Theme.TextoDim, alvoVisivel: c.gameObject,
                formato: s => s.Trim() == (ctrl.listaEscolhaLinha?.captionText?.text ?? "").Trim() ? "" : s);
            t.font = Theme.TextoCorrido;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }

    // Seleção de carga (cenaEscolhaCarga).
    internal sealed class TelaCarga : TelaControlada<Il2Cpp.cenaEscolhaCarga>
    {
        protected override string Titulo => "Carga";
        protected override string[] Fluxo => new[] { "Veículo", "Carga", "Pintura", "Mapa", "Linha" };
        protected override int Passo => 1;
        protected override Dropdown ListaDe(Il2Cpp.cenaEscolhaCarga c) => c.listaEscolhaCarga;
        protected override RawImage PreviaDe(Il2Cpp.cenaEscolhaCarga c) => c.imgDaCarga;
        protected override string MetodoAvancar => "SalvaCargaEVaiPraSkin";
        protected override string RotuloAvancar => "Escolher pintura";
        protected override void Avancar(Il2Cpp.cenaEscolhaCarga c) => c.SalvaCargaEVaiPraSkin();

        protected override IEnumerable<GameObject> Tratados()
        {
            yield return ctrl.objAvisoRAM;
            yield return ctrl.objLinkSiteAutor;
        }

        protected override void AcoesLista(Transform barra)
        {
            Icone(barra, Chama("AtualizaListaDeCargas"), Ico.Atualizar, "Atualizar lista", () => ctrl.AtualizaListaDeCargas());
        }

        protected override void Detalhes(Transform pai)
        {
            var c = Kit.Cartao(pai, "Descrição", 14, 6);
            var t = TextoF(c, De(() => ctrl.textoDaListaCarga), 13, Theme.TextoDim, alvoVisivel: c.gameObject);
            t.font = Theme.TextoCorrido;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Link(pai, DeGo(() => ctrl.objLinkSiteAutor), De(() => ctrl.objLinkSiteAutorTxt));
        }

        protected override void Opcoes(Transform pai) => ChaveF(pai, De(() => ctrl.engatarCargaNoInicio), null, true);
    }
}
