using System;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Painéis que o jogo abre durante a partida. A "raiz" de cada tela é o próprio painel (não o
    // Canvas inteiro): ele é escondido por um CanvasGroup e a versão nova aparece só enquanto o
    // jogo mantém o painel ativo.
    internal abstract class TelaPainel : TelaFormulario
    {
        protected abstract string Canvas { get; }
        protected abstract string Painel { get; }
        protected virtual string Titulo => null;
        protected virtual float Largura => 720;
        protected override int? OrdemCanvas => 600;

        protected override Transform AcharRaiz() => Orig.Raiz(Canvas, Painel)?.Find(Painel);

        // Modal padrão: fundo escurecido, cartão com título e botão de fechar.
        protected RectTransform Janela(string caminhoFechar, Func<Transform> titulo = null)
        {
            var card = Kit.Modal(raiz, Largura, out _);
            var cab = Kit.Novo("Cabecalho", card);
            Kit.Linha(cab, 12);
            Kit.Tam(cab, -1, 44);
            Text t;
            if (titulo != null) t = TextoF(cab, titulo, 22, Theme.Texto, true);
            else t = Kit.Texto(cab, Titulo ?? "", 22, Theme.Texto, true);
            Kit.Tam(t, -1, 44, 1);
            if (caminhoFechar != null) Botao(cab, caminhoFechar, Ico.Fechar, Estilo.Icone, null, null, altura: 40);
            return card;
        }

        protected Kit.Botao Acao(Transform pai, string caminho, Estilo estilo, string rotulo, string icone = null) =>
            Botao(pai, caminho, icone, estilo, rotulo, altura: 44, tamFonte: 15, alin: TextAnchor.MiddleCenter);

        // Conteúdo com rolagem dentro da janela: cresce com o formulário até "max" e depois rola.
        protected RectTransform AreaRolavel(RectTransform card, float max)
        {
            var conteudo = Kit.Lista(card, out var scroll, 6);
            var le = Kit.Tam(scroll.GetComponent<RectTransform>(), -1, 40);
            Sync(() =>
            {
                float h = Mathf.Clamp(conteudo.rect.height, 40, max);
                if (Mathf.Abs(le.preferredHeight - h) > 0.5f) le.preferredHeight = le.minHeight = h;
            });
            return conteudo;
        }

        protected RectTransform Rodape(Transform card)
        {
            Kit.Espaco(card, 4);
            var r = Kit.Novo("Rodape", card);
            Kit.Linha(r, 10, 0, TextAnchor.MiddleRight);
            Kit.Tam(r, -1, 44);
            return r;
        }
    }

    // Ajuste do banco do motorista (HUD Canvas / botoesAjusteBancoMotorista).
    internal sealed class TelaBanco : TelaPainel
    {
        protected override string Canvas => "Canvas";
        protected override string Painel => "botoesAjusteBancoMotorista";
        protected override string Titulo => "Ajuste do banco";
        protected override float Largura => 640;
        protected override string[] Cobertos => new[]
        {
            "fundo pra n clicar", "fundo", "btGiroHorizontalMais", "btGiroHorizontalMenos", "btResetBancoMotorista",
            "btLadosMais", "btLadosMenos", "btSalvar", "btFovMais", "btFovMenos", "btFrenteTrasMais", "btFrenteTrasMenos",
            "btCimaBaixoMais", "btCimaBaixoMenos", "btGiroVerticalMais", "btGiroVerticalMenos",
            "Text", "Text (1)", "Text (2)", "Text (3)", "Text (4)", "Text (5)", "btTestar", "btReload", "btFecharTela",
        };

        // Rótulo do jogo -> par de botões
        static readonly (string rotulo, string padrao, string menos, string mais)[] Ajustes =
        {
            ("Text", "Giro horizontal", "btGiroHorizontalMenos", "btGiroHorizontalMais"),
            ("Text (1)", "Giro vertical", "btGiroVerticalMenos", "btGiroVerticalMais"),
            ("Text (5)", "Cima/Baixo", "btCimaBaixoMenos", "btCimaBaixoMais"),
            ("Text (2)", "Posição lateral", "btLadosMenos", "btLadosMais"),
            ("Text (4)", "Frente/Trás", "btFrenteTrasMenos", "btFrenteTrasMais"),
            ("Text (3)", "Campo de visão", "btFovMenos", "btFovMais"),
        };

        protected override void Construir()
        {
            var card = Janela("btFecharTela");
            foreach (var a in Ajustes)
            {
                var linha = Kit.Novo("Ajuste", card);
                var hl = Kit.Linha(linha, 10);
                Kit.Pad(hl, 4, 0, 0, 0);
                Kit.Tam(linha, -1, 52);
                var t = TextoF(linha, P(a.rotulo), 16, Theme.Texto, formato: s => s.Replace("\n", " "));
                Kit.Tam(t, -1, 44, 1);
                Botao(linha, a.menos, Ico.Remover, Estilo.Secundario, null, null, altura: 44);
                Botao(linha, a.mais, Ico.Adicionar, Estilo.Secundario, null, null, altura: 44);
            }
            var r = Rodape(card);
            Acao(r, "btResetBancoMotorista", Estilo.Fantasma, "Resetar padrão", Ico.Reset);
            Acao(r, "btReload", Estilo.Fantasma, "Último salvo");
            Kit.Espaco(r, -1, -1, true);
            Acao(r, "btSalvar", Estilo.Secundario, "Salvar", Ico.Salvar);
            Kit.Tam(Acao(r, "btTestar", Estilo.Primario, "OK").btn, 100, 44);
        }
    }

    // Mods de letreiros / destino (HUD Canvas / botoesModsLetreiros).
    internal sealed class TelaDestino : TelaPainel
    {
        protected override string Canvas => "Canvas";
        protected override string Painel => "botoesModsLetreiros";
        protected override float Largura => 520;
        protected override string[] Cobertos => new[]
        {
            "fundo pra n clicar", "fundo", "btFecharTela", "btSalvar", "txt", "txt pasta dos letreiros", "Panel",
            "text visor mod letreiro", "btLimpar", "bt0", "bt1", "bt2", "bt3", "bt4", "bt5", "bt6", "bt7", "bt8", "bt9",
            "btMais", "btMenos", "btTutorial",
        };

        protected override void Construir()
        {
            var card = Janela("btFecharTela", P("txt"));

            // Visor com o código do letreiro
            var visor = Kit.Novo("Visor", card);
            Kit.Fundo(visor, Theme.Solido, Theme.Fundo);
            Kit.Tam(visor, -1, 84);
            var vt = Texto(visor, "text visor mod letreiro", 44, Theme.Dado, true, alvoVisivel: null);
            Kit.Encher(vt.rectTransform, 8);
            vt.alignment = TextAnchor.MiddleCenter;

            // Teclado: 1-9, depois - 0 +
            string[][] teclas =
            {
                new[] { "bt1", "bt2", "bt3" }, new[] { "bt4", "bt5", "bt6" }, new[] { "bt7", "bt8", "bt9" },
                new[] { "btMenos", "bt0", "btMais" },
            };
            foreach (var fileira in teclas)
            {
                var l = Kit.Novo("Teclas", card);
                var hl = Kit.Linha(l, 8);
                hl.childForceExpandWidth = true;
                Kit.Tam(l, -1, 52);
                foreach (var k in fileira)
                {
                    var b = Botao(l, k, null, Estilo.Secundario, k, altura: 52, tamFonte: 22, alin: TextAnchor.MiddleCenter);
                    Kit.Tam(b.btn, -1, 52, 1);
                }
            }

            var pasta = Texto(card, "txt pasta dos letreiros", 13, Theme.Texto3);
            pasta.verticalOverflow = VerticalWrapMode.Overflow;

            var r = Rodape(card);
            Acao(r, "btTutorial", Estilo.Fantasma, "Tutorial", Ico.Ajuda);
            Acao(r, "btLimpar", Estilo.Fantasma, "Limpar", Ico.Excluir);
            Kit.Espaco(r, -1, -1, true);
            Kit.Tam(Acao(r, "btSalvar", Estilo.Primario, "OK").btn, 120, 44);
        }
    }

    // Hora e clima (CanvasEnviro / painel do enviro): quatro colunas geradas pelo formulário.
    internal sealed class TelaClima : TelaPainel
    {
        protected override string Canvas => "CanvasEnviro(Clone)";
        protected override string Painel => "painel do enviro";
        protected override string Titulo => "Hora e clima";
        protected override float Largura => 1320;
        protected override string[] Cobertos => new[] { "BGImage", "Time", "Season", "Weather", "Quality", "btFechar" };

        protected override void Construir()
        {
            var card = Janela("btFechar");
            var colunas = Kit.Novo("Colunas", card);
            var hl = Kit.Linha(colunas, 20, 0, TextAnchor.UpperLeft);
            hl.childForceExpandWidth = true;

            foreach (var grupo in new[] { "Time", "Season", "Weather", "Quality" })
            {
                var g = O(grupo);
                if (g == null) continue;
                var col = Kit.Novo(grupo, colunas);
                Kit.Fundo(col, Theme.Solido, Theme.Cartao);
                var vl = Kit.Coluna(col, 8, 16);
                _ = vl;
                Kit.Tam(col, -1, -1, 1);
                var cab = TextoF(col, P(grupo + "/Header"), Theme.TamRotulo, Theme.TextoMudo, formato: Theme.Rotular);
                cab.horizontalOverflow = HorizontalWrapMode.Overflow;
                Kit.Tam(cab, -1, 22);
                if (grupo == "Time")
                {
                    var hora = Texto(col, "Time/TimeText", 40, Theme.Texto, true);
                    hora.fontStyle = FontStyle.Bold; // número: negrito reto, como os dados da garagem
                    Kit.Tam(hora, -1, 52);
                }
                FormularioColuna(col, g);
            }
        }

        // Cada controle do grupo em sua própria linha (coluna estreita).
        void FormularioColuna(Transform pai, Transform grupo)
        {
            for (int i = 0; i < grupo.childCount; i++)
            {
                var c = grupo.GetChild(i);
                if (c.name == "Header" || c.name == "TimeText") continue;
                var alvo = c;
                Func<Transform> ate = () => grupo;
                var slider = c.GetComponent<Slider>();
                var dd = c.GetComponent<Dropdown>();
                var tg = c.GetComponent<Toggle>();
                if (slider != null || dd != null)
                {
                    // Rótulo que o jogo deixa como filho do controle
                    for (int k = 0; k < c.childCount; k++)
                    {
                        var f = c.GetChild(k);
                        if (f.name != "Label" && f.GetComponent<Text>() != null)
                        {
                            var t = TextoF(pai, () => f, 13, Theme.TextoMudo, ate: ate);
                            Kit.Tam(t, -1, 20);
                            break;
                        }
                    }
                    if (slider != null) Kit.Tam(SliderF(pai, () => alvo != null ? alvo.GetComponent<Slider>() : null, 240, ate), -1, 36, 1);
                    else Kit.Tam(SeletorF(pai, () => alvo != null ? alvo.GetComponent<Dropdown>() : null, 240, ate).btn, -1, 40, 1);
                }
                else if (tg != null) ChaveF(pai, () => alvo, ate);
            }
        }
    }
    // Pausa (HUD Canvas / "UI pausada PC"): continuar, voltar ao menu ou sair, e os atalhos de teclado.
    // Os botões de redes sociais e sites do original ficam de fora.
    internal sealed class TelaPausa : TelaPainel
    {
        protected override string Canvas => "Canvas";
        protected override string Painel => "UI pausada PC";
        protected override string Titulo => "Jogo pausado";
        protected override float Largura => 560;
        protected override string[] Cobertos => new[]
        {
            "Panel", "Text", "txtAcompanheCanais", "botaoHome", "botaoSair", "site pbsu", "site pbsr",
            "face pbsu", "face pbsr", "yt pbsu", "yt pbsr", "btFechar",
        };

        protected override void Construir()
        {
            var card = Janela("btFechar");
            var atalhos = Kit.Cartao(card, "Atalhos", 16, 8);
            var t = Texto(atalhos, "Text", 13, Theme.TextoDim, alvoVisivel: atalhos.gameObject);
            t.font = Theme.TextoCorrido;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            Kit.Espaco(card, 4);
            var continuar = Botao(card, "btFechar", Ico.Play, Estilo.Primario, "Continuar", null, altura: 52);
            Kit.Tam(continuar.btn, -1, 52);
            var r = Rodape(card);
            Botao(r, "botaoHome", Ico.Casa, Estilo.Secundario, "Menu principal", null, altura: 44);
            Kit.Espaco(r, -1, -1, true);
            Botao(r, "botaoSair", Ico.Desligar, Estilo.Perigo, "Sair do jogo", null, altura: 44);
        }
    }

    // Salvar viagem (HUD Canvas / botoesSaveGame): 12 slots em grade, carregar e saídas no rodapé.
    internal sealed class TelaSalvar : TelaPainel
    {
        protected override string Canvas => "Canvas";
        protected override string Painel => "botoesSaveGame";
        protected override string Titulo => "Salvar viagem";
        protected override float Largura => 720;

        protected override void Construir()
        {
            var card = Janela("btFecharTela");
            var instr = Texto(card, "labelSaveGame", 13, Theme.TextoMudo);
            instr.font = Theme.TextoCorrido;
            instr.verticalOverflow = VerticalWrapMode.Overflow;

            Kit.Tam(Kit.Rotulo(card, "Espaços de salvamento"), -1, 16);
            RectTransform linha = null;
            for (int i = 0; i < 12; i++)
            {
                if (i % 6 == 0)
                {
                    linha = Kit.Novo("Slots", card);
                    var hl = Kit.Linha(linha, 8);
                    hl.childForceExpandWidth = true;
                    Kit.Tam(linha, -1, 56);
                }
                var nome = i == 0 ? "bt1" : $"bt1 ({i})";
                var b = Botao(linha, nome, null, Estilo.Secundario, (i + 1).ToString(), "", altura: 56, tamFonte: 20, alin: TextAnchor.MiddleCenter);
                Kit.Tam(b.btn, -1, 56, 1);
            }

            var r = Rodape(card);
            Botao(r, "btHome", Ico.Casa, Estilo.Fantasma, "Menu principal", null, altura: 44);
            Botao(r, "btSairJogo", Ico.Desligar, Estilo.Perigo, "Sair do jogo", null, altura: 44);
            Kit.Espaco(r, -1, -1, true);
            Botao(r, "btCarregarSave", Ico.Abrir, Estilo.Secundario, "Carregar viagem", null, altura: 44);
        }
    }

    // Controles (HUD Canvas / listaControles): abas por tipo de controle no topo; abaixo, o assistente
    // de direção e as opções do tipo escolhido, geradas pelo formulário. As abas de celular saem.
    internal sealed class TelaControles : TelaPainel
    {
        protected override string Canvas => "Canvas";
        protected override string Painel => "listaControles";
        protected override string Titulo => "Controles";
        protected override float Largura => 960;

        // Aba (botão original, rótulo) e o painel de opções que ela mostra.
        static readonly (string botao, string rotulo, string painel)[] Abas =
        {
            ("btTeclado", "Teclado", "opcoesTeclado"),
            ("btMouse", "Mouse", "opcoesMouse"),
            ("btTecNumerico", "Teclado numérico", null),
            ("btMouseTeclado", "Mouse e teclado", null),
            ("btVolantePC", "Volante ou joystick", "opcoesVolantePC"),
        };

        static readonly string[] Opcoes = { "opcoesTeclado", "opcoesMouse", "opcoesSetas", "opcoesTilt", "opcoesVolante", "opcoesVolantePC" };

        protected override void Construir()
        {
            var card = Janela("btOK_fechar");

            var abas = Kit.Novo("Abas", card);
            var hl = Kit.Linha(abas, 4);
            hl.childForceExpandWidth = true;
            Kit.Tam(abas, -1, 40);
            foreach (var a in Abas)
            {
                var b = Botao(abas, a.botao, null, Estilo.Secundario, a.rotulo, null, altura: 40, alin: TextAnchor.MiddleCenter);
                Kit.Tam(b.btn, -1, 40, 1);
                if (a.painel != null) { var p = a.painel; Sync(() => b.Selecionado(O(p)?.gameObject.activeSelf == true)); }
            }

            var area = AreaRolavel(card, 640);
            Bloco(area, "lista assistente direcao", "Direção");
            foreach (var p in Opcoes) Bloco(area, p, null);
        }

        // Formulário de um subpainel; aparece só enquanto o jogo mantém o subpainel ativo.
        void Bloco(Transform area, string painel, string titulo)
        {
            var orig = O(painel);
            if (orig == null) return;
            var bloco = Kit.Novo(painel, area);
            Kit.Coluna(bloco, 6);
            bloco.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (titulo != null) Kit.Tam(Kit.Rotulo(bloco, titulo), -1, 20);
            Formulario(bloco, orig);
            Sync(() =>
            {
                var o = O(painel);
                bool vis = o != null && o.gameObject.activeSelf;
                if (bloco.gameObject.activeSelf != vis) bloco.gameObject.SetActive(vis);
            });
        }

        protected override bool Ignorar(Transform c) =>
            base.Ignorar(c) || c.name == "btOK" || c.name == "botoesControlesMobile";
    }

    // Painéis simples do HUD (listas de chaves, sliders e botões): janela com título próprio e o
    // formulário gerado do original. Um registro por painel, com o título que ele deve ter.
    internal sealed class TelaPainelAuto : TelaPainel
    {
        readonly string _painel, _titulo;
        readonly float _largura;

        internal TelaPainelAuto(string painel, string titulo, float largura = 760)
        {
            _painel = painel; _titulo = titulo; _largura = largura;
        }

        internal override string Nome => "Painel:" + _painel;
        protected override string Canvas => "Canvas";
        protected override string Painel => _painel;
        protected override string Titulo => _titulo;
        protected override float Largura => _largura;

        protected override void Construir()
        {
            var card = Janela(Fechar());
            Formulario(AreaRolavel(card, 680), raizOrig);
        }

        string Fechar()
        {
            foreach (var n in new[] { "btFecharTela", "btFechar", "btOK_fechar" })
                if (O(n) != null) return n;
            return null;
        }

        // O título do original ("Espelhos Virtuais") já é o título da janela.
        protected override bool Ignorar(Transform c)
        {
            if (base.Ignorar(c)) return true;
            var t = c.GetComponent<Text>();
            return t != null && string.Equals(Orig.Limpo(t.text)?.Trim(), _titulo, StringComparison.OrdinalIgnoreCase);
        }
    }
    // Mais opções (HUD Canvas / botoesControlesExtras): ações e ajustes da partida, que no original
    // ficam soltos num painel só. Aqui em seções, com rótulos claros; os atalhos para outros painéis
    // ficam juntos no fim.
    internal sealed class TelaMaisOpcoes : TelaPainel
    {
        protected override string Canvas => "Canvas";
        protected override string Painel => "botoesControlesExtras";
        protected override string Titulo => "Mais opções";
        protected override float Largura => 760;

        protected override void Construir()
        {
            var card = Janela("btFecharTela");
            Comecar(AreaRolavel(card, 720), raizOrig, () => raizOrig);

            OpSecao("Viagem");
            OpSeletor("dropListaEscolhaEntrypoints", "Ponto de partida");
            OpChave("tgReposicionarBusao", "Reposicionar o veículo");
            OpAcao("btForcarDesembarque", "Forçar desembarque");
            OpAcao("btForcarDesembarque2", "Forçar desembarque (alternativo)");
            OpAcao("btLimparColisoes", "Limpar colisões");
            OpAcao("btTempoX2", "Acelerar o tempo", "×2");
            OpAcao("btTempoX5", "Acelerar mais o tempo", "×5");

            OpSecao("Trocar mapa ou linha");
            OpAcao("btTrocarMapa", "Trocar o mapa", "Trocar");
            OpAcao("btTrocarLinha", "Trocar a linha", "Trocar");
            OpAjuda("txtTrocarMapaLinha");

            OpSecao("Visual");
            OpSlider("sliderFOVexterno", "Campo de visão externo", "textoFOVexterno");
            OpAcao("btTrocaCeu", "Trocar o céu", "Trocar");

            OpSecao("Interface");
            OpAcao(PorSprite("FPS"), "Contador de FPS", "Alternar");
            OpAcao("btFPSVelho", "Informações de desempenho", "Alternar");
            OpAcao(PorSprite("GPS"), "GPS", "Alternar");

            OpSecao("Som");
            OpSlider("SliderVolume", "Volume geral", "textoVolume");

            OpSecao("Atalhos");
            OpAcao("btAjusteIluminacao", "Iluminação do céu", "Abrir");
            OpAcao("btEfeitos", "Efeitos visuais", "Abrir");
            OpAcao("btSombras", "Sombras", "Abrir");
            OpAcao("btCorAmbiente", "Cor do ambiente", "Abrir");
            OpAcao("btEspelhosVirtuais", "Espelhos virtuais", "Abrir");
            OpAcao("btOpcoesTerreno", "Terreno", "Abrir");
            OpAcao("btDificuldadeCarga", "Carga e reboque", "Abrir");
            OpAcao("btModsDeLetreiros", "Letreiros (mods)", "Abrir");
            OpAcao("btOpcoesInGame", "Todas as configurações", "Abrir");

            OpRestantes(new System.Collections.Generic.HashSet<string> { "letMais", "letMenos" }, "Outras");
        }

        // Dois botões do original se chamam "btFPS"; o ícone (sprite) diz qual é qual.
        Transform PorSprite(string sprite)
        {
            for (int i = 0; i < raizOrig.childCount; i++)
            {
                var f = raizOrig.GetChild(i);
                var img = f.GetComponent<Image>();
                if (f.GetComponent<Button>() != null && img != null && img.sprite != null && img.sprite.name == sprite) return f;
            }
            return null;
        }
    }
}
