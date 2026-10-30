using System;
using System.Collections.Generic;
using PBSLoaderCommonUtils;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Menu da partida. No original é o overlay do ALT (CanvasPC, ativo só com a tecla pressionada).
    // Aqui ele vira uma sidebar à esquerda, no estilo do menu lateral do Assetto Corsa EVO, que não
    // depende do ALT: fechada, sobra só um chevron semi-opaco no canto superior esquerdo; aberta:
    //   cabeçalho com o chevron de fechar;
    //   seções em acordeão (veículo, controles, funções, animações, ambiente), cada uma com chevron;
    //   rodapé fixo com volume, menu principal e sair.
    // O atalho configurável (padrão `, a crase) também abre e fecha. Com a sidebar aberta o cursor fica livre;
    // ao fechar, o PBSLoaderCommonUtils devolve o cursor ao jogo.
    // Os itens chamam os botões originais direto (onClick), sem abrir os submenus do jogo.
    internal sealed partial class TelaAlt : Tela
    {
        protected override string NomeCanvas => "CanvasPC";
        protected override string FilhoObrigatorio => "btSeuOnibus";
        // A HUD (Canvas ordem 0) tem imagens invisíveis da interface mobile que recebem cliques.
        protected override int? OrdemCanvas => 500;
        protected override bool SempreVisivel => true;

        const float Largura = 340, Margem = 24;

        internal static Atalho TeclaMenu;
        static string NomeTecla => TeclaMenu != null ? TeclaMenu.Combinacao.Legivel() : "` (crase)";

        // Itens com rótulo próprio, agrupados (filho do CanvasPC ou do submenu, rótulo).
        static readonly (string grupo, (string filho, string rotulo)[] itens)[] MenuVeiculo =
        {
            ("Viagem", new[] { ("btDestino", "Destino e letreiro"), ("btSalvarJogo", "Salvar viagem"),
                               ("btCarregarJogo", "Carregar viagem"), ("btRelatorio", "Relatório da viagem") }),
            ("Cabine", new[] { ("btPosBancoMotorista", "Ajuste do banco") }),
            ("Tela", new[] { ("btResolucao", "Resolução"), ("btResolucao FULLSCREEN", "Alternar tela cheia") }),
            ("Sistema", new[] { ("btOpcoes Diversas", "Configurações"), ("btAtualizaRadio1", "Recarregar músicas da rádio") }),
            ("Ferramentas de mod", new[] { ("btTelemetry", "Telemetria"), ("btInspector", "Inspetor técnico"),
                                           ("btAeroSurface", "Superfícies aerodinâmicas"), ("btTrisCounter", "Contador de polígonos") }),
        };

        static readonly (string grupo, (string filho, string rotulo)[] itens)[] MenuControles =
        {
            ("Modo de controle", new[] { ("btTeclado", "Teclado"), ("btMouse", "Mouse"), ("btTecladoNumerico", "Teclado numérico"),
                                         ("btTecladoMouse", "Mouse e teclado"), ("btVolanteGenerico", "Volante ou joystick"),
                                         ("btVolanteG27", "Volante Logitech (antigo)"), ("btVolanteThrustmaster", "Volante Thrustmaster (antigo)") }),
            ("Ajustes", new[] { ("btSensibilidade", "Sensibilidade e opções"), ("btRemap", "Remapear controles") }),
        };

        // Funções do veículo (caminho no CanvasPC, ícone, rótulo), em grupos. As de carroceria (janelas, limpadores)
        // ficam no painel da direita (TelaAltCarroceria.cs).
        static readonly (string grupo, (string caminho, string icone, string rotulo)[] itens)[] Funcoes =
        {
            ("Condução", new[] { ("btTravaDiferencial", Ico.Carro, "Trava do diferencial"), ("btFogClima", Ico.Nuvem, "Neblina") }),
            ("Fotos", new[] { ("bt screenshot", Ico.Camera, "Tirar foto"), ("bt screenshot 360", Ico.Foto360, "Foto 360°") }),
        };

        static readonly string[] Animacoes =
            { "btAnim", "btAnim (1)", "btAnim (2)", "btAnim (3)", "btAnim (4)", "btAnim (5)", "btAnim (6)", "btAnim (7)", "btAnim (8)", "btAnim (9)" };

        protected override string[] Cobertos
        {
            get
            {
                var l = new List<string> { "btControles", "btHoraEClima", "btConsole", "btSair", "btEntrada", "btSeuOnibus", "sliderVolume", "btDica do dia" };
                foreach (var g in Funcoes) foreach (var f in g.itens) l.Add(f.caminho);
                foreach (var g in FuncoesCarroceria) foreach (var f in g.itens) l.Add(f.caminho);
                l.AddRange(Animacoes);
                return l.ToArray();
            }
        }

        // Estado entre partidas (enquanto o jogo estiver aberto): sidebar aberta e seções expandidas.
        static bool _aberta;
        static readonly HashSet<string> _abertas = new() { "Veículo" };
        static RectTransform _raizAtual;

        sealed class Secao
        {
            public string nome, icone;
            public RectTransform cabeca, corpo;
            public Text chevron;
        }

        readonly List<Secao> _secoes = new();
        RectTransform _sidebar, _chevron;
        ScrollRect _scroll;

        protected override void Construir()
        {
            _raizAtual = raiz;
            Sidebar();
            Chevron();
            Aplicar();
            Carroceria();
            ChevronCarroceria();
            AplicarCarroceria();
            Sync(() =>
            {
                if (TeclaMenu == null || !TeclaMenu.Apertou) return;
                Abrir(!_aberta);
                Som.Tocar(_aberta ? Som.Efeito.Lista : Som.Efeito.Cancelar);
            });
        }

        // ---------- sidebar expandida ----------

        void Sidebar()
        {
            _sidebar = Kit.Novo("Sidebar", raiz);
            _sidebar.anchorMin = new Vector2(0, 0); _sidebar.anchorMax = new Vector2(0, 1); _sidebar.pivot = new Vector2(0, 0.5f);
            _sidebar.offsetMin = new Vector2(Margem, Margem); _sidebar.offsetMax = new Vector2(Margem + Largura, -Margem);
            Kit.Fundo(_sidebar, Theme.Solido, Theme.Painel);
            Kit.Contorno(_sidebar, Theme.Linha);
            var col = Kit.Coluna(_sidebar, 0);
            Kit.Pad(col, 0, 0, 0, 0);

            // Cabeçalho: marca + título + chevron de recolher.
            var cab = Kit.Novo("Cabecalho", _sidebar);
            var hl = Kit.Linha(cab, 10);
            Kit.Pad(hl, 18, 10, 0, 0);
            Kit.Tam(cab, -1, 64);
            var marca = Kit.Novo("Marca", cab);
            Kit.Tam(marca, 10, 30);
            var barra = Kit.Ancorar(Kit.Novo("Barra", marca), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 30));
            barra.localEulerAngles = new Vector3(0, 0, -14f);
            Kit.Fundo(barra, null, Theme.Acento).raycastTarget = false;
            var tit = Kit.Texto(cab, "Menu da viagem", 20, Theme.Texto, true);
            tit.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(tit, -1, 30, 1);
            var fechar = Kit.NovoBotao(cab, null, Ico.SetaEsq, Estilo.Icone, () => Abrir(false), 40);
            fechar.visual.som = Som.Efeito.Cancelar;
            fechar.visual.dica = Kit.Dica(fechar.go.GetComponent<RectTransform>(), "Fechar", direita: true);
            Kit.Divisoria(_sidebar);

            // Corpo com rolagem: seções em acordeão.
            var corpoLista = Kit.Novo("Corpo", _sidebar);
            Kit.Tam(corpoLista, -1, -1, 1, 1);
            var cl = Kit.Coluna(corpoLista, 0);
            Kit.Pad(cl, 8, 4, 8, 8);
            var conteudo = Kit.Lista(corpoLista, out _scroll, 2);

            SecaoMenu(conteudo, "Veículo", Ico.Onibus, "btSeuOnibus/menuSeuOnibus", MenuVeiculo);
            SecaoMenu(conteudo, "Controles", Ico.Teclado, "btControles/menuControles", MenuControles);
            SecaoFuncoes(conteudo);
            SecaoAmbiente(conteudo);
            SecaoMods(conteudo);

            // Rodapé fixo.
            Kit.Divisoria(_sidebar);
            var rod = Kit.Novo("Rodape", _sidebar);
            var rc = Kit.Coluna(rod, 4);
            Kit.Pad(rc, 12, 12, 10, 12);
            Volume(rod);
            Item(rod, "btEntrada", Ico.Casa, "Menu principal", null);
            var sair = Botao(rod, "btSair", Ico.Desligar, Estilo.Perigo, "Sair do jogo", null, altura: 42);
            Kit.Tam(sair.btn, -1, 42);
        }

        // Seção recolhível: cabeçalho (ícone, nome, chevron) e corpo.
        Secao NovaSecao(Transform pai, string nome, string icone)
        {
            var s = new Secao { nome = nome, icone = icone };
            var cab = Kit.NovoBotao(pai, nome, icone, Estilo.Fantasma, () => Alternar(s), 46);
            cab.rotulo.fontStyle = FontStyle.Bold;
            Kit.Pad(cab.go.GetComponent<HorizontalLayoutGroup>(), 12, 14, 0, 0);
            // Chevron "›" que gira 90° para "⌄" quando a seção abre.
            s.chevron = Kit.Icone(cab.go.transform, Ico.SetaDir, 11, Theme.TextoMudo);
            s.cabeca = cab.go.GetComponent<RectTransform>();
            s.corpo = Kit.Novo("Corpo " + nome, pai);
            Kit.Coluna(s.corpo, 2);
            Kit.Pad(s.corpo.GetComponent<VerticalLayoutGroup>(), 0, 0, 0, 10);
            _secoes.Add(s);
            Atualizar(s);
            return s;
        }

        void Alternar(Secao s)
        {
            if (!_abertas.Remove(s.nome)) _abertas.Add(s.nome);
            Atualizar(s, true);
        }

        void Atualizar(Secao s, bool animar = false)
        {
            bool aberta = _abertas.Contains(s.nome);
            bool mudou = s.corpo.gameObject.activeSelf != aberta;
            if (mudou) s.corpo.gameObject.SetActive(aberta);
            if (!animar) { s.chevron.transform.localEulerAngles = new Vector3(0, 0, aberta ? -90f : 0f); return; }
            Anim.Girar(s.chevron.transform, aberta ? -90f : 0f);
            if (mudou && aberta)
            {
                var filhos = new List<GameObject>();
                for (int i = 0; i < s.corpo.childCount; i++) if (s.corpo.GetChild(i).gameObject.activeSelf) filhos.Add(s.corpo.GetChild(i).gameObject);
                Anim.Cascata(filhos, 0.025f, 0.4f, 20, -24f);
            }
        }

        void SecaoMenu(Transform pai, string nome, string icone, string caminhoMenu, (string grupo, (string filho, string rotulo)[] itens)[] grupos)
        {
            var menu = O(caminhoMenu);
            if (menu == null) return;
            var s = NovaSecao(pai, nome, icone);
            var usados = new HashSet<string>();
            foreach (var (grupo, itens) in grupos)
            {
                var rotulo = RotuloGrupo(s.corpo, grupo);
                var botoes = new List<Kit.Botao>();
                foreach (var (filho, texto) in itens)
                {
                    usados.Add(filho);
                    if (menu.Find(filho) == null) continue;
                    botoes.Add(Item(s.corpo, caminhoMenu + "/" + filho, null, texto, caminhoMenu));
                    if (filho == "btResolucao") LinhaResolucao(s.corpo, menu, caminhoMenu, usados);
                }
                EsconderRotuloSemItens(rotulo, botoes);
            }

            // Botões que o jogo tenha e que não estão na lista acima (versões futuras).
            List<Kit.Botao> outros = null;
            GameObject rotuloOutros = null;
            for (int i = 0; i < menu.childCount; i++)
            {
                var f = menu.GetChild(i);
                if (usados.Contains(f.name) || f.GetComponent<Button>() == null) continue;
                if (rotuloOutros == null) { rotuloOutros = RotuloGrupo(s.corpo, "Outros"); outros = new List<Kit.Botao>(); }
                outros.Add(Botao(s.corpo, caminhoMenu + "/" + f.name, null, Estilo.Fantasma, f.name, "", caminhoMenu, altura: 38));
            }
            if (rotuloOutros != null) EsconderRotuloSemItens(rotuloOutros, outros);
        }

        void SecaoFuncoes(Transform pai)
        {
            var s = NovaSecao(pai, "Funções do veículo", Ico.Ferramenta);
            foreach (var (grupo, itens) in Funcoes)
            {
                var rotulo = RotuloGrupo(s.corpo, grupo);
                var botoes = new List<Kit.Botao>();
                foreach (var (caminho, icone, texto) in itens) botoes.Add(Item(s.corpo, caminho, icone, texto, null));
                EsconderRotuloSemItens(rotulo, botoes);
            }
        }

        // Animações do mod do veículo (o jogo mostra só as que o veículo tem), em grade de 5.
        void SecaoAnimacoes(Transform pai)
        {
            var s = NovaSecao(pai, "Animações", Ico.Play);
            RectTransform linha = null;
            for (int i = 0; i < Animacoes.Length; i++)
            {
                if (i % 5 == 0)
                {
                    linha = Kit.Novo("Animacoes", s.corpo);
                    var hl = Kit.Linha(linha, 6);
                    Kit.Pad(hl, 12, 4, 4, 4);
                    Kit.Tam(linha, -1, 52);
                }
                var b = Botao(linha, Animacoes[i], null, Estilo.Secundario, (i + 1).ToString(), null, altura: 44, tamFonte: 16, alin: TextAnchor.MiddleCenter);
                Kit.Tam(b.btn, 52, 44);
            }
            // A seção some quando o veículo não tem animações.
            Sync(() =>
            {
                bool alguma = false;
                foreach (var a in Animacoes) { var o = O(a); if (o != null && o.gameObject.activeSelf) { alguma = true; break; } }
                if (s.cabeca.gameObject.activeSelf != alguma) s.cabeca.gameObject.SetActive(alguma);
                if (!alguma && s.corpo.gameObject.activeSelf) s.corpo.gameObject.SetActive(false);
                if (alguma) Atualizar(s);
            });
        }

        void SecaoAmbiente(Transform pai)
        {
            var s = NovaSecao(pai, "Ambiente e ferramentas", Ico.Brilho);
            Item(s.corpo, "btHoraEClima", Ico.Relogio, "Hora e clima", null);
            Item(s.corpo, "btConsole", Ico.Console, "Console", null);
        }

        // Mods: as ações que cada mod registrou (PainelMods.Acao), agrupadas por mod, e o painel de opções.
        void SecaoMods(Transform pai)
        {
            var s = NovaSecao(pai, "Mods", Ico.Config);
            foreach (var kv in PainelMods.Mods)
            {
                if (kv.Value.acoes.Count == 0) continue;
                RotuloGrupo(s.corpo, TelaMods.NomeDe(kv.Key));
                foreach (var (rotulo, _, acao) in kv.Value.acoes)
                {
                    var a = acao;
                    var r = rotulo;
                    var b = Kit.NovoBotao(s.corpo, rotulo, null, Estilo.Fantasma, () =>
                    {
                        Abrir(false); // o painel do mod aparece sem a sidebar por cima
                        try { a(); } catch (Exception e) { MelonLoader.MelonLogger.Warning($"[Mods] ação '{r}' falhou: {e.Message}"); }
                    }, 38);
                    Kit.Pad(b.go.GetComponent<HorizontalLayoutGroup>(), 12, 12, 0, 0);
                }
            }
            RotuloGrupo(s.corpo, "Ajustes");
            var op = Kit.NovoBotao(s.corpo, "Opções dos mods", Ico.Config, Estilo.Fantasma, () => { Abrir(false); TelaMods.Abrir(); }, 38);
            Kit.Pad(op.go.GetComponent<HorizontalLayoutGroup>(), 12, 12, 0, 0);
        }

        Kit.Botao Item(Transform pai, string caminho, string icone, string rotulo, string ate)
        {
            var b = Botao(pai, caminho, icone, Estilo.Fantasma, rotulo, null, ate, altura: 38);
            Kit.Pad(b.go.GetComponent<HorizontalLayoutGroup>(), 12, 12, 0, 0);
            return b;
        }

        // Rótulo de grupo com o mesmo recuo do texto dos itens (12 px).
        GameObject RotuloGrupo(Transform pai, string texto)
        {
            var g = Kit.Novo("Grupo", pai);
            var hl = Kit.Linha(g, 0, 0, TextAnchor.LowerLeft);
            Kit.Pad(hl, 12, 0, 0, 4);
            Kit.Tam(g, -1, 28);
            Kit.Rotulo(g, texto);
            return g.gameObject;
        }

        // O rótulo do grupo some quando nenhum item dele está visível (ex.: ferramentas de mod desligadas).
        void EsconderRotuloSemItens(GameObject rotulo, List<Kit.Botao> botoes)
        {
            Sync(() =>
            {
                bool algum = false;
                foreach (var b in botoes) if (b.go.activeSelf) { algum = true; break; }
                if (rotulo.activeSelf != algum) rotulo.SetActive(algum);
            });
        }

        // Percentuais de resolução lado a lado, logo abaixo do item "Resolução".
        void LinhaResolucao(Transform pai, Transform menu, string caminhoMenu, HashSet<string> usados)
        {
            var linha = Kit.Novo("Resolucao", pai);
            var hl = Kit.Linha(linha, 4);
            Kit.Pad(hl, 12, 4, 2, 6);
            hl.childForceExpandWidth = true;
            Kit.Tam(linha, -1, 42);
            for (int i = 0; i < menu.childCount; i++)
            {
                var f = menu.GetChild(i);
                if (!f.name.StartsWith("btResolucao ", StringComparison.Ordinal) || f.name.Contains("FULLSCREEN")) continue;
                usados.Add(f.name);
                var b = Botao(linha, caminhoMenu + "/" + f.name, null, Estilo.Secundario, f.name, "", caminhoMenu, altura: 34, alin: TextAnchor.MiddleCenter);
                Kit.Tam(b.btn, -1, 34, 1);
            }
        }

        void Volume(Transform pai)
        {
            var grupo = Kit.Novo("Volume", pai);
            var hl = Kit.Linha(grupo, 10);
            Kit.Pad(hl, 12, 4, 0, 0);
            Kit.Tam(grupo, -1, 40);
            Kit.Icone(grupo, Ico.Som, 16, Theme.TextoDim);
            bool sincronizando = false;
            var nosso = Kit.NovoSlider(grupo, 200, v =>
            {
                if (sincronizando) return;
                var o = O("sliderVolume")?.GetComponent<Slider>();
                if (o != null) o.value = Mathf.Lerp(o.minValue, o.maxValue, v); // dispara o onValueChanged do jogo
            });
            Kit.Tam(nosso, -1, 32, 1);
            Sync(() =>
            {
                var o = O("sliderVolume")?.GetComponent<Slider>();
                bool vis = o != null && o.gameObject.activeSelf;
                if (grupo.gameObject.activeSelf != vis) grupo.gameObject.SetActive(vis);
                if (!vis) return;
                float v = o.normalizedValue;
                if (Mathf.Abs(nosso.value - v) > 0.001f) { sincronizando = true; nosso.value = v; sincronizando = false; }
            });
        }

        // ---------- chevron (sidebar fechada) ----------

        // Botão discreto no canto superior esquerdo: semi-opaco em repouso, opaco com o mouse em cima.
        void Chevron()
        {
            var b = Kit.NovoBotao(raiz, null, Ico.SetaDir, Estilo.Icone, () => Abrir(true), 44);
            b.visual.som = Som.Efeito.Lista;
            _chevron = b.go.GetComponent<RectTransform>();
            Kit.Ancorar(_chevron, new Vector2(0, 1), new Vector2(Margem, -Margem), new Vector2(44, 44));
            b.go.GetComponent<LayoutElement>().ignoreLayout = true;
            b.visual.dica = Kit.Dica(_chevron, "Menu da viagem (" + NomeTecla + ")", direita: true);
            var grupo = b.go.AddComponent<CanvasGroup>();
            Kit.AoQuadro(b.go, () => grupo.alpha = Mathf.Lerp(0.45f, 1f, b.visual.h));
        }

        internal void Abrir(bool aberta)
        {
            _aberta = aberta;
            Aplicar();
            if (!aberta) return;
            Anim.Deslizar(_sidebar);
            // Cabeçalhos e itens visíveis entram em cascata logo atrás do painel.
            var itens = new List<GameObject>();
            var c = _scroll.content;
            for (int i = 0; i < c.childCount && itens.Count < 24; i++) if (c.GetChild(i).gameObject.activeSelf) itens.Add(c.GetChild(i).gameObject);
            Anim.Cascata(itens, 0.025f, 0.45f, 24, -40f);
        }

        void Aplicar()
        {
            _sidebar.gameObject.SetActive(_aberta);
            _chevron.gameObject.SetActive(!_aberta);
        }

        // ---------- cursor ----------

        // Chamado a cada frame: com a sidebar aberta e visível, a tela de mods ou algum painel do mod (clima,
        // banco, configurações...) aberto, o cursor fica solto. Quem decide o cursor é o PBSLoaderCommonUtils; ao parar de
        // pedir, ele volta ao jogo (sem restaurar estados guardados, que travavam o mouse).
        internal static void ManterCursor(bool painel = false)
        {
            bool usando = TelaMods.Aberta || painel
                || ((_aberta || _carroceriaAberta) && _raizAtual != null && !_raizAtual.WasCollected && _raizAtual.gameObject.activeInHierarchy);
            Mouse.Soltar("PBSLoaderCommonInterface", usando);
        }
    }
}
