using System;
using System.Collections.Generic;
using PBSLoaderCommonUtils;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Painel da direita: controles de carroceria do ônibus. Fechado, sobra um chevron discreto no meio da borda
    // direita; aberto, desliza da direita. Mostra só o que o ônibus tem:
    //   Luzes            farol baixo/alto, pisca-alerta, luzes do salão 1..3
    //   Portas           cada porta e "todas" (pneumáticas: sem ar não mexem)
    //   Suspensão        ajoelhar/elevar (mod SuspensaoAr)
    //   Ar-condicionado  liga, ventilador, temperatura
    //   Retarder         nível e automático
    //   Janelas, limpadores e animações do mod do ônibus: os botões originais do CanvasPC (o jogo sabe quais existem)
    // Os comandos vêm do Comandos; o estado (botão aceso) do PBSLoaderCommonUtils.Veiculo.
    internal sealed partial class TelaAlt
    {
        static readonly (string grupo, (string caminho, string icone, string rotulo)[] itens)[] FuncoesCarroceria =
        {
            ("Janelas e cabine", new[] { ("btJanelasPeds", Ico.Janela, "Janelas dos passageiros"), ("btJanelaMoto", Ico.Janela, "Janela do motorista"),
                                         ("btTampoes", Ico.Cadeado, "Tampões do teto"), ("btParasol", Ico.Brilho, "Para-sol") }),
            ("Limpadores", new[] { ("btLimpador1", Ico.Girar, "Limpador 1"), ("btLimpador 1 rapido", Ico.Girar, "Limpador 1 rápido"),
                                   ("btLimpador 2", Ico.Girar, "Limpador 2"), ("btLimpador 2 rapido", Ico.Girar, "Limpador 2 rápido") }),
        };

        const float LarguraCarroceria = 300;

        internal static Atalho TeclaCarroceria;
        static bool _carroceriaAberta;
        RectTransform _carroceria, _chevronCarroceria, _dinamico;
        ScrollRect _scrollCarroceria;
        int _geracaoMontada = -1;
        static bool _abrirNoTeste = Environment.CommandLine.Contains("--pbsloadercommoninterface-abrir-carroceria");

        void Carroceria()
        {
            _carroceria = Kit.Novo("Carroceria", raiz);
            _carroceria.anchorMin = new Vector2(1, 0); _carroceria.anchorMax = new Vector2(1, 1); _carroceria.pivot = new Vector2(1, 0.5f);
            _carroceria.offsetMin = new Vector2(-Margem - LarguraCarroceria, Margem); _carroceria.offsetMax = new Vector2(-Margem, -Margem);
            Kit.Fundo(_carroceria, Theme.Solido, Theme.Painel);
            Kit.Contorno(_carroceria, Theme.Linha);
            var col = Kit.Coluna(_carroceria, 0);
            Kit.Pad(col, 0, 0, 0, 0);

            var cab = Kit.Novo("Cabecalho", _carroceria);
            var hl = Kit.Linha(cab, 10);
            Kit.Pad(hl, 10, 18, 0, 0);
            Kit.Tam(cab, -1, 64);
            var fechar = Kit.NovoBotao(cab, null, Ico.SetaDir, Estilo.Icone, () => AbrirCarroceria(false), 40);
            fechar.visual.som = Som.Efeito.Cancelar;
            fechar.visual.dica = Kit.Dica(fechar.go.GetComponent<RectTransform>(), "Fechar");
            var tit = Kit.Texto(cab, "Carroceria", 20, Theme.Texto, true, TextAnchor.MiddleRight);
            tit.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(tit, -1, 30, 1);
            Kit.Divisoria(_carroceria);

            var corpoLista = Kit.Novo("Corpo", _carroceria);
            Kit.Tam(corpoLista, -1, -1, 1, 1);
            var cl = Kit.Coluna(corpoLista, 0);
            Kit.Pad(cl, 8, 4, 8, 8);
            var conteudo = Kit.Lista(corpoLista, out _scrollCarroceria, 2);

            // Parte que depende do ônibus (refeita ao abrir e ao trocar de ônibus).
            _dinamico = Kit.Novo("Comandos", conteudo);
            Kit.Coluna(_dinamico, 2);

            // Botões originais do jogo: aparecem só quando o ônibus os tem.
            foreach (var (grupo, itens) in FuncoesCarroceria)
            {
                var rotulo = RotuloGrupo(conteudo, grupo);
                var botoes = new List<Kit.Botao>();
                foreach (var (caminho, icone, texto) in itens) botoes.Add(Item(conteudo, caminho, icone, texto, null));
                EsconderRotuloSemItens(rotulo, botoes);
            }
            AnimacoesCarroceria(conteudo);

            Sync(() =>
            {
                if (TeclaCarroceria != null && TeclaCarroceria.Apertou)
                {
                    AbrirCarroceria(!_carroceriaAberta);
                    Som.Tocar(_carroceriaAberta ? Som.Efeito.Lista : Som.Efeito.Cancelar);
                }
                // Remonta quando muda o ônibus ou o que ele oferece (a SuspensaoAr se apresenta depois, por exemplo).
                if (_carroceriaAberta && (Veiculo.Geracao != _geracaoMontada || Assinatura() != _assinaturaMontada)) MontarComandos();
                // Teste automático (print do PBSLoaderCommonUtils): abre o painel uma vez, assim que houver ônibus.
                if (_abrirNoTeste && Veiculo.Existe && Time.unscaledTime > 20f) { _abrirNoTeste = false; AbrirCarroceria(true); }
            });
        }

        // Animações do mod do ônibus (btAnim ... btAnim (9)), em grade de 5; o grupo some sem nenhuma.
        void AnimacoesCarroceria(Transform pai)
        {
            var rotulo = RotuloGrupo(pai, "Animações");
            RectTransform linha = null;
            for (int i = 0; i < Animacoes.Length; i++)
            {
                if (i % 5 == 0)
                {
                    linha = Kit.Novo("Animacoes", pai);
                    var hl = Kit.Linha(linha, 6);
                    Kit.Pad(hl, 12, 4, 4, 4);
                    Kit.Tam(linha, -1, 52);
                }
                var b = Botao(linha, Animacoes[i], null, Estilo.Secundario, (i + 1).ToString(), null, altura: 44, tamFonte: 16, alin: TextAnchor.MiddleCenter);
                Kit.Tam(b.btn, 46, 44);
            }
            Sync(() =>
            {
                bool alguma = false;
                foreach (var a in Animacoes) { var o = O(a); if (o != null && o.gameObject.activeSelf) { alguma = true; break; } }
                if (rotulo.activeSelf != alguma) rotulo.SetActive(alguma);
            });
        }

        // ---------- comandos do ônibus (Comandos) ----------

        RectTransform _grade;
        int _naGrade;

        int _assinaturaMontada;

        // O que o ônibus oferece hoje, num número: muda quando aparece/some um grupo de comandos.
        static int Assinatura() =>
            Veiculo.Portas.Todas.Count | (Veiculo.Suspensao.TemAr && Comandos.TemComandoSuspensao ? 1 << 8 : 0)
            | (Veiculo.Cabine.TemArCondicionado ? 1 << 9 : 0) | (Veiculo.Cambio.RetarderNiveis > 0 ? 1 << 10 : 0)
            | (Comandos.TemLuzInterna(1) ? 1 << 11 : 0) | (Comandos.TemLuzInterna(2) ? 1 << 12 : 0) | (Comandos.TemLuzInterna(3) ? 1 << 13 : 0);

        /// <summary>O painel da carroceria está aberto (o painel de condução e os avisos saem da frente).</summary>
        internal static bool CarroceriaAberta => _carroceriaAberta && _raizAtual != null && !_raizAtual.WasCollected && _raizAtual.gameObject.activeInHierarchy;
        internal static float LarguraOcupada => CarroceriaAberta ? LarguraCarroceria + Margem : 0f;

        void MontarComandos()
        {
            _geracaoMontada = Veiculo.Geracao;
            _assinaturaMontada = Assinatura();
            for (int i = _dinamico.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_dinamico.GetChild(i).gameObject);
            if (!Veiculo.Existe) return;

            Grupo("Luzes");
            Bloco("Farol baixo", Ico.Lampada, Comandos.FarolBaixo, () => Veiculo.Luzes.Farol == Farol.Baixo);
            Bloco("Farol alto", Ico.Lampada, Comandos.FarolAlto, () => Veiculo.Luzes.Farol == Farol.Alto);
            Bloco("Pisca-alerta", Ico.Alerta, Comandos.PiscaAlerta, () => Veiculo.Luzes.Seta == Seta.Alerta);
            for (int n = 1; n <= 3; n++)
            {
                if (!Comandos.TemLuzInterna(n)) continue;
                int k = n;
                Bloco("Salão " + k, Ico.Lampada, () => Comandos.LuzInterna(k), () => Comandos.LuzInternaAcesa(k));
            }

            if (Veiculo.Portas.Todas.Count > 0)
            {
                Grupo("Portas");
                bool doisLados = Veiculo.Portas.Esquerdas > 0 && Veiculo.Portas.Direitas > 0;
                foreach (var p in Veiculo.Portas.Todas)
                {
                    var porta = p;
                    string nome = "Porta " + porta.Numero + (doisLados ? (porta.Lado == Lado.Direito ? " dir." : " esq.") : porta.Lado == Lado.Esquerdo ? " esq." : "");
                    Bloco(nome, Ico.Abrir, () => Comandos.Porta(porta), () => porta.Aberta);
                }
                if (Veiculo.Portas.Todas.Count > 1) Bloco("Todas", Ico.Abrir, Comandos.TodasAsPortas, () => Veiculo.Portas.AlgumaAberta);
            }

            if (Veiculo.Suspensao.TemAr && Comandos.TemComandoSuspensao)
            {
                Grupo("Suspensão");
                Bloco("Ajoelhar", Ico.Abaixo, Comandos.Ajoelhar, () => Veiculo.Suspensao.Nivel == "ajoelhado");
                Bloco("Elevar", null, Comandos.Elevar, () => Veiculo.Suspensao.Nivel == "elevado");
            }

            if (Veiculo.Cabine.TemArCondicionado)
            {
                Grupo("Ar-condicionado");
                Bloco("Ligado", Ico.Nuvem, Comandos.ArCondicionado, () => Veiculo.Cabine.ArCondicionadoLigado);
                Bloco("Ventilador", Ico.Girar, Comandos.ArCondicionadoVentilador, null, () => "Ventilador " + Veiculo.Cabine.ArCondicionadoVentilador);
                Passo(() => Veiculo.Cabine.TemperaturaAlvo + " °C", () => Comandos.ArCondicionadoTemperatura(-1), () => Comandos.ArCondicionadoTemperatura(1));
            }

            if (Veiculo.Cambio.RetarderNiveis > 0)
            {
                Grupo("Retarder");
                Passo(() => $"Nível {Veiculo.Cambio.RetarderNivel} de {Veiculo.Cambio.RetarderNiveis}", Comandos.RetarderMenos, Comandos.RetarderMais);
                Bloco("Automático", Ico.Girar, Comandos.RetarderAuto, () => Comandos.RetarderAutomatico);
            }
        }

        void Grupo(string titulo)
        {
            RotuloGrupo(_dinamico, titulo);
            _grade = null; _naGrade = 0;
        }

        // Botão de comando em grade de 2, aceso quando "ligado" é verdade. "rotuloVivo": texto que muda (ventilador).
        void Bloco(string rotulo, string icone, Action acao, Func<bool> ligado, Func<string> rotuloVivo = null)
        {
            if (_grade == null || _naGrade == 2)
            {
                _grade = Kit.Novo("Linha", _dinamico);
                var hl = Kit.Linha(_grade, 6);
                Kit.Pad(hl, 12, 4, 2, 2);
                hl.childForceExpandWidth = true;
                Kit.Tam(_grade, -1, 44);
                _naGrade = 0;
            }
            var b = Kit.NovoBotao(_grade, rotulo, icone, Estilo.Secundario, acao, 40);
            b.maiusculas = false;
            Kit.Tam(b.btn, -1, 40, 1);
            _naGrade++;
            string ultimo = rotulo;
            Kit.AoQuadro(b.go, () =>
            {
                if (ligado != null) b.Selecionado(ligado());
                if (rotuloVivo == null) return;
                var s = rotuloVivo();
                if (s != ultimo) { ultimo = s; b.Rotulo(s); }
            });
        }

        // Linha "−  valor  +".
        void Passo(Func<string> texto, Action menos, Action mais)
        {
            var l = Kit.Novo("Passo", _dinamico);
            var hl = Kit.Linha(l, 6);
            Kit.Pad(hl, 12, 4, 2, 2);
            Kit.Tam(l, -1, 44);
            Kit.NovoBotao(l, null, Ico.Remover, Estilo.Secundario, menos, 40);
            var t = Kit.Texto(l, "", 15, Theme.Texto, true, TextAnchor.MiddleCenter);
            Kit.Tam(t, -1, 40, 1);
            Kit.AoQuadro(t.gameObject, () => { var s = texto(); if (t.text != s) t.text = s; });
            Kit.NovoBotao(l, null, Ico.Adicionar, Estilo.Secundario, mais, 40);
            _grade = null;
        }

        // ---------- chevron e abrir/fechar ----------

        // Botão discreto no meio da borda direita: semi-opaco em repouso, opaco com o mouse em cima.
        void ChevronCarroceria()
        {
            var b = Kit.NovoBotao(raiz, null, Ico.SetaEsq, Estilo.Icone, () => AbrirCarroceria(true), 44);
            b.visual.som = Som.Efeito.Lista;
            _chevronCarroceria = b.go.GetComponent<RectTransform>();
            Kit.Ancorar(_chevronCarroceria, new Vector2(1, 0.5f), new Vector2(-Margem, 0), new Vector2(44, 44));
            b.go.GetComponent<LayoutElement>().ignoreLayout = true;
            string tecla = TeclaCarroceria != null && !TeclaCarroceria.Combinacao.Vazia ? " (" + TeclaCarroceria.Combinacao.Legivel() + ")" : "";
            b.visual.dica = Kit.Dica(_chevronCarroceria, "Controles da carroceria" + tecla);
            var grupo = b.go.AddComponent<CanvasGroup>();
            Kit.AoQuadro(b.go, () => grupo.alpha = Mathf.Lerp(0.45f, 1f, b.visual.h));
        }

        internal void AbrirCarroceria(bool aberta)
        {
            _carroceriaAberta = aberta;
            AplicarCarroceria();
            if (!aberta) return;
            MontarComandos();
            Anim.Deslizar(_carroceria, 380f);
        }

        void AplicarCarroceria()
        {
            _carroceria.gameObject.SetActive(_carroceriaAberta);
            _chevronCarroceria.gameObject.SetActive(!_carroceriaAberta);
        }
    }
}
