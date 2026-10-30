using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Menu principal (cena cenaCARREGA0, Canvas com MEPCanvasStart), no formato do hub do DiRT Rally 2.0:
    //   esquerda: jogar (as três ações), condições da viagem (numa janela), o que leva a outras telas
    //             (configurações, mods) e o aviso que o jogo exibir;
    //   resto:    livre para as cinematográficas dos ônibus (ou a foto de fundo).
    // Fica de fora o que não ajuda a jogar: tutorial, grupo de fãs, redes sociais, "Download para PC",
    // pedidos de apoio, aviso de mods, opt-in de anúncios, e os atalhos que repetem a tela de
    // configurações (presets gráficos, reset, sons/extras soltos). Os originais continuam escondidos.
    internal sealed class TelaMenu : Tela
    {
        const string ME = "MENU ENTRADA";
        const float M = 28, G = 16, Topo = 72 + 16, Base = Kit.AlturaRodape + 16;

        protected override string NomeCanvas => "Canvas";
        protected override bool TransicaoCheia => true;
        protected override string FilhoObrigatorio => ME;
        protected override string[] Cobertos => new[]
        {
            "fundo aviso (1)", "txtAVISO MODS", "Image", "logo pbsc", "fundo aviso", "texto aviso da colaboracao",
            "btPatrocine", "painel avisos mais", "painel aviso nao suporado", "btContinuar Jogo", "txt Continuar Jogo",
            "btIniciar Jogo", "txt Iniciar Jogo", "btGrupos Fas", "txt Grupo de Fas", "bt Tutorial", "txt Tutorial",
            "fundo tex", ME, "btGrupoBotoes",
        };

        protected override void Construir()
        {
            var foto = Kit.Atmosfera(raiz, true);
            // Cinematográficas dos ônibus em 3D (estúdio escuro). Enquanto o primeiro ônibus carrega, o fundo
            // fica escuro (a foto antiga do jogo aparecia na abertura); a foto só volta sem cinema possível.
            var cine = Kit.Encher(Kit.Novo("Cinema", raiz)).gameObject.AddComponent<RawImage>();
            cine.raycastTarget = false;
            cine.enabled = false;
            cine.transform.SetSiblingIndex(foto.transform.GetSiblingIndex() + 1);
            float mistura = 0f;
            Sync(() =>
            {
                var o = O("Image")?.GetComponent<RawImage>();
                var tex = o != null ? o.texture : null;
                if (foto.texture != tex) foto.texture = tex;
                Garagem.Previa3D.PedirCinema();
                bool ativo = Garagem.Previa3D.CinemaAtivo && Garagem.Previa3D.SaidaCinema != null;
                mistura = Mathf.MoveTowards(mistura, ativo ? 1f : 0f, Anim.Dt / 1.2f);
                foto.enabled = tex != null && Garagem.Previa3D.CinemaIndisponivel;
                if (foto.enabled) foto.color = new Color(0.5f, 0.5f, 0.52f, 1f);
                cine.enabled = mistura > 0f;
                if (cine.enabled)
                {
                    if (cine.texture != Garagem.Previa3D.SaidaCinema) cine.texture = Garagem.Previa3D.SaidaCinema;
                    float k = 1f - Garagem.Previa3D.Escuro; // cortes entre ônibus passam pelo preto
                    cine.color = new Color(k, k, k, Anim.Expo(mistura));
                }
            });
            // Escurece a lateral esquerda para a coluna ler bem sobre a foto.
            var gl = Kit.Encher(Kit.Novo("GradLateral", raiz)).gameObject.AddComponent<RawImage>();
            gl.texture = Theme.GradienteLateral; gl.raycastTarget = false; gl.color = new Color(1, 1, 1, 0.85f);

            Cabecalho();
            ColunaJogar();
            JanelaCondicoes();
            Kit.Rodape(raiz, () =>
            {
                // Versão do jogo ("L0/Free (BETA v307 - PC Direct3D11)").
                var v = Orig.Texto(O(ME + "/txtIdentificaColaborador"));
                return "Proton Bus" + (string.IsNullOrWhiteSpace(v) ? "" : "  ·  " + Orig.Limpo(v));
            });
        }

        // Cabeçalho: idioma, ajuda e conta à direita; sair isolado no fim.
        void Cabecalho()
        {
            Kit.Cabecalho(raiz, "Proton Bus", "Menu principal", out _, out var acoes);
            foreach (var (caminho, sigla) in new[] { ("/botaoPortugues", "PT"), ("/botaoEnglish", "EN") })
                Kit.Tam(Botao(acoes, ME + caminho, null, Estilo.Secundario, sigla, null, ME, altura: 40, tamFonte: 13, alin: TextAnchor.MiddleCenter).btn, 44, 40);
            Kit.Espaco(acoes, -1, 10);
            IconeComDica(acoes, ME + "/btSobre", Ico.Ajuda, "Sobre o jogo");
            IconeComDica(acoes, ME + "/btConta", Ico.Contato, "Conta");
            Kit.DivisoriaV(acoes, 24);
            var sair = Botao(acoes, ME + "/btSairGame", Ico.Desligar, Estilo.Perigo, null, null, ME, altura: 40);
            sair.visual.dica = Kit.Dica(sair.go.GetComponent<RectTransform>(), "Sair do jogo", abaixo: true);
        }

        void IconeComDica(Transform pai, string caminho, string icone, string dica)
        {
            var b = Botao(pai, caminho, icone, Estilo.Icone, null, null, ME, altura: 40);
            b.visual.dica = Kit.Dica(b.go.GetComponent<RectTransform>(), dica, abaixo: true);
        }

        RectTransform Coluna(string nome, float largura, bool daDireita)
        {
            var rt = Kit.Novo(nome, raiz);
            rt.anchorMin = new Vector2(daDireita ? 1 : 0, 0); rt.anchorMax = new Vector2(daDireita ? 1 : 0, 1);
            rt.pivot = new Vector2(daDireita ? 1 : 0, 0.5f);
            rt.offsetMin = new Vector2(daDireita ? -M - largura : M, Base);
            rt.offsetMax = new Vector2(daDireita ? -M : M + largura, -Topo);
            Kit.Fundo(rt, Theme.Solido, Theme.Painel);
            var vl = Kit.Coluna(rt, 8);
            Kit.Pad(vl, 20, 20, 20, 20);
            return rt;
        }

        void Secao(Transform pai, string titulo, bool espacoAntes = true)
        {
            if (espacoAntes) Kit.Espaco(pai, 12);
            Kit.Tam(Kit.Rotulo(pai, titulo), -1, 16);
        }

        void ColunaJogar()
        {
            var col = Coluna("Jogar", 440, false);
            Secao(col, "Jogar", false);
            AcaoPrincipal(col, 1, "btIniciar Jogo", "txt Iniciar Jogo", "Iniciar jogo", "Escolher veículo, pintura, mapa e linha", true);
            AcaoPrincipal(col, 2, "btContinuar Jogo", "txt Continuar Jogo", "Continuar jogo", "Retomar a última viagem", false);
            AcaoPrincipal(col, 3, ME + "/btCarregar Salvo", ME + "/txt Carregar Salvo", "Carregar salvo", "Abrir uma viagem salva", false, ME);

            Secao(col, "Ajustes");
            Kit.NovoBotao(col, "Condições da viagem", Ico.Nuvem, Estilo.Fantasma, () => AbrirCondicoes(true), 40);
            Navega(Item(col, ME + "/btOpcoesGraficas", Ico.Config, "Configurações", null));
            Item(col, ME + "/btTeclado", Ico.Teclado, "Controles", null);

            Secao(col, "Mods");
            Item(col, ME + "/btMods", Ico.Pasta, "Abrir pasta dos mods", null);
            Item(col, ME + "/btBaixarMods", Ico.Download, "Baixar mods", null);
            Kit.NovoBotao(col, "Opções dos mods", Ico.Config, Estilo.Fantasma, () => PainelMods.Abrir(), 40);

            Kit.Espaco(col, -1, -1, true);
            // Aviso só quando o jogo o exibe (versão não suportada).
            Aviso(col, "painel aviso nao suporado/txt pagamento urbano", Theme.Aviso, "Aviso");
            var pol = Botao(col, ME + "/txt Politica de Privacidade", null, Estilo.Fantasma, "Política de privacidade", "", ME, altura: 32);
            pol.maiusculas = false;
            pol.rotulo.fontSize = 13;
            pol.visual.tN = Theme.TextoMudo; // link discreto (o motor de estados pinta o texto)
        }

        // Ação principal como linha grande numerada (os tiles do hub do DiRT): 01 INICIAR JOGO / descrição.
        // A ação de destaque é um bloco vermelho, como o botão principal.
        void AcaoPrincipal(Transform pai, int n, string caminho, string caminhoRotulo, string rotulo, string meta, bool destaque, string ate = null)
        {
            var item = Kit.NovoItemLista(pai, n, rotulo.ToUpperInvariant(), meta, null);
            item.b.maiusculas = true;
            item.nome.fontSize = 20;
            item.nome.fontStyle = FontStyle.BoldAndItalic;
            Kit.Tam(item.nome, -1, 24);
            Kit.Tam(item.b.go.GetComponent<RectTransform>(), -1, 66);
            if (destaque)
            {
                var v = item.b.visual;
                v.fN = Theme.Acento; v.fH = Theme.AcentoHover; v.fP = Theme.AcentoPress; v.fA = Theme.AcentoHover;
                v.tN = v.tH = v.tA = Color.white;
                v.mH = v.mA = new Color(0, 0, 0, 0);
                item.indice.color = Theme.Tingir(Theme.Acento, Color.white, 0.75f);
                item.meta.color = Color.white;
            }
            Vincular(item.b, P(caminho), P(caminhoRotulo), P(ate));
            if (n == 1) Navega(item.b); // "Iniciar jogo" vai para a seleção do veículo
        }

        // Item de menu com rótulo fixo (os textos do jogo variam: "Opções do Simulador", "Pasta dos mods"...).
        Kit.Botao Item(Transform pai, string caminho, string icone, string rotulo, string caminhoRotulo) =>
            Botao(pai, caminho, icone, Estilo.Fantasma, rotulo, caminhoRotulo, ME, altura: 40);

        // Condições da próxima viagem numa janela (as chaves existem só nesta cena do jogo): o lado
        // direito da tela fica livre para o fundo.
        GameObject _condicoes;

        void JanelaCondicoes()
        {
            var card = Kit.Modal(raiz, 460, out _condicoes);
            var cab = Kit.Novo("Cabecalho", card);
            Kit.Linha(cab, 12);
            Kit.Tam(cab, -1, 40);
            Kit.Tam(Kit.Texto(cab, "Condições da viagem", 22, Theme.Texto, true), -1, 40, 1);
            Kit.NovoBotao(cab, null, Ico.Fechar, Estilo.Icone, () => AbrirCondicoes(false), 36);
            Kit.Divisoria(card);
            var cond = Kit.Novo("Condicoes", card);
            Kit.Coluna(cond, 0);
            Chave(cond, ME + "/tgJogarANoite", ME, rotuloFixo: "Viajar à noite");
            Chave(cond, ME + "/tgHorarioLocal", ME, rotuloFixo: "Usar o horário local");
            Chave(cond, ME + "/tgChuva", ME, rotuloFixo: "Chuva");
            Chave(cond, ME + "/tgNeve", ME, rotuloFixo: "Neve");
            Chave(cond, ME + "/tgMascaras", ME, rotuloFixo: "Pessoas com máscara");
            Secao(card, "Interface");
            Chave(card, ME + "/tgSystemInfo", ME, rotuloFixo: "Mostrar informações do sistema");
            // Clicar fora da janela fecha.
            _condicoes.AddComponent<Button>().onClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)(() => AbrirCondicoes(false)));
            _condicoes.SetActive(false);
        }

        internal void AbrirCondicoes(bool abrir)
        {
            if (_condicoes == null || _condicoes.activeSelf == abrir) return;
            _condicoes.SetActive(abrir);
            if (abrir) Anim.Aparecer(_condicoes, _condicoes.transform.Find("Janela"));
        }

        // Cartão de aviso espelhando um aviso do jogo: marca colorida à esquerda.
        void Aviso(Transform pai, string caminho, Color cor, string rotulo)
        {
            var card = Kit.Painel(pai, "Aviso", Theme.Tingir(Theme.Painel, cor, 0.08f));
            Kit.Marca(card, cor, 3);
            var vl = Kit.Coluna(card, 6);
            Kit.Pad(vl, 16, 14, 12, 12);
            Kit.Tam(Kit.Rotulo(card, rotulo, Theme.TamRotulo, cor), -1, 14);
            var t = Texto(card, caminho, 13, Theme.TextoDim, alvoVisivel: card.gameObject);
            t.font = Theme.TextoCorrido;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
