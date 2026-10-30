using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Modo de inspeção (jogo aberto com --pbsloadercommoninterface-tour): percorre as telas sozinho, fotografa
    // cada uma em UserData/PBSLoaderCommonInterface/capturas e fecha o jogo. É o que permite revisar o visual
    // renderizado de verdade, rodada após rodada, sem alguém navegar manualmente.
    internal static class Inspecao
    {
        internal static bool Ativa { get; private set; }
        internal static bool ForcarAlt { get; private set; }
        static bool SoPartida;

        sealed class Passo
        {
            public string nome;           // nome do print (null = só ação)
            public Func<bool> pronto;     // condição para fotografar/agir
            public Action acao;           // depois do print
            public float espera = 1.1f;   // tempo para animações assentarem antes do print
            public float limite = 40f;    // segundos esperando "pronto" antes de desistir
        }

        static readonly List<Passo> _passos = new();
        static int _i;
        static float _desde, _prontoDesde = -1, _proximoPasso;
        static string _dir, _log;
        static Func<IEnumerable<Tela>> _telas;
        static string _forcarSubmenu;
        static float _proximaCarga, _rajadaAte, _proximaRajada;
        static int _nRajada;
        static int _nCarga;

        internal static void Iniciar(Func<IEnumerable<Tela>> telas)
        {
            var a = Environment.GetCommandLineArgs();
            // --pbsloadercommoninterface-partida: só atravessa os menus até a partida carregar (sem prints, sem fechar
            // o jogo). Usado pelo laboratório do GraficosPlus para chegar no mapa sozinho.
            bool soPartida = Array.Exists(a, x => x == "--pbsloadercommoninterface-partida");
            Ativa = soPartida || Array.Exists(a, x => x == "--pbsloadercommoninterface-tour" || x == "--pbsloadercommoninterface-tour-menu" || x == "--pbsloadercommoninterface-tour-telas" || x == "--pbsloadercommoninterface-tour-cinema" || x == "--pbsloadercommoninterface-tour-abertura");
            if (!Ativa) return;
            _telas = telas;
            _dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonInterface", "capturas");
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.GetFiles(_dir, "*.png")) File.Delete(f);
            _log = Path.Combine(_dir, "tour.log");
            File.WriteAllText(_log, $"Tour iniciado {DateTime.Now:HH:mm:ss}\n");
            Roteiro();
            if (soPartida)
            {
                int k = _passos.FindIndex(p => p.limite >= 300f);
                if (k >= 0)
                {
                    _passos.RemoveRange(k + 1, _passos.Count - k - 1);
                    _passos[k].acao = () => Registrar("partida carregada");
                }
                foreach (var p in _passos) p.nome = null;
                // A tela de carregamento pode nem ser vista (carregamento rápido): não esperar por ela.
                int car = _passos.FindIndex(p => p.espera == 0.6f && p.acao == null);
                if (car >= 0) _passos.RemoveAt(car);
                SoPartida = true;
            }
            _desde = Time.unscaledTime;
            Registrar("passos: " + _passos.Count);
        }

        static void Registrar(string s)
        {
            MelonLogger.Msg("[Tour] " + s);
            try { File.AppendAllText(_log, $"{DateTime.Now:HH:mm:ss} {s}\n"); } catch { }
        }

        static T Tela<T>() where T : Tela
        {
            foreach (var t in _telas()) if (t is T x && x.RaizNova != null && x.RaizNova.gameObject.activeInHierarchy) return x;
            return null;
        }

        static bool Visivel<T>() where T : Tela => Tela<T>() != null;

        static void Clicar(Transform t, string oque)
        {
            if (t == null) { Registrar("  (não achei " + oque + ")"); return; }
            Desempenho.Marco("clique");
            Orig.Clicar(t);
        }

        static Transform ChamaEm(string canvas, string filho, string metodo, int? arg = null) =>
            Orig.BotaoQueChama(Orig.Raiz(canvas, filho), metodo, arg);

        static void Add(string nome, Func<bool> pronto, Action acao = null, float espera = 1.1f) =>
            _passos.Add(new Passo { nome = nome, pronto = pronto, acao = acao, espera = espera });

        // Roteiro: menu -> ônibus (+ opções) -> pintura -> mapa (+ opções) -> linha -> carregamento -> partida (ALT e painéis).
        static void Roteiro()
        {
            var args = Environment.GetCommandLineArgs();
            bool atravessar = Array.Exists(args, x => x == "--pbsloadercommoninterface-partida");
            Action iniciar = () => Clicar(Orig.Raiz("Canvas", "MENU ENTRADA")?.Find("btIniciar Jogo"), "Iniciar jogo");
            // Tour da abertura (--pbsloadercommoninterface-tour-abertura): fotografa a cada 0,25 s desde o primeiro frame até o
            // menu novo assentar (o que aparece antes dele na abertura do jogo).
            if (Array.Exists(args, x => x == "--pbsloadercommoninterface-tour-abertura"))
            {
                _rajadaAte = float.MaxValue;
                Add(null, Visivel<TelaMenu>, null, 4f);
                Add("fim", () => true, () => { Registrar("fim do tour da abertura"); Application.Quit(); }, 0.2f);
                return;
            }
            // Tour do cinema (--pbsloadercommoninterface-tour-cinema): fotografa o fundo do menu por ~40 s e sai.
            if (Array.Exists(args, x => x == "--pbsloadercommoninterface-tour-cinema"))
            {
                for (int k = 0; k < 16; k++) Add($"cine_{k:00}", Visivel<TelaMenu>, null, 2.5f);
                Add(null, Visivel<TelaMenu>, () => Tela<TelaMenu>()?.AbrirCondicoes(true), 0.2f);
                Add("cine_condicoes", Visivel<TelaMenu>, null, 1.0f);
                Add(null, Visivel<TelaMenu>, () => Tela<TelaMenu>()?.AbrirCondicoes(false), 0.2f);
                Add("fim", () => true, () => { Registrar("fim do tour do cinema"); Application.Quit(); }, 0.2f);
                return;
            }
            if (atravessar) Add("01_menu", Visivel<TelaMenu>, iniciar, 2.0f); // laboratório: direto para a partida
            else
            {
                // Painel de mods no menu principal (lista e um mod com ações e faixas).
                Add("01_menu", Visivel<TelaMenu>, () => PainelMods.Abrir(), 2.0f);
                // Print e ação em passos separados: o print só é gravado no fim do frame, e uma ação que
                // fecha ou reanima o painel no mesmo frame sairia na imagem.
                Add("01b_mods", () => TelaMods.Aberta, null, 0.8f);
                Add(null, () => TelaMods.Aberta, () => PainelMods.Abrir("VolanteReal"), 0.1f);
                Add("01c_mods_volante", () => TelaMods.Aberta, null, 0.8f);
                Add(null, () => TelaMods.Aberta, () => { TelaMods.Fechar(); iniciar(); }, 0.1f);
                // Tour curto (--pbsloadercommoninterface-tour-menu): só o menu principal e o painel de mods.
                if (Array.Exists(args, x => x == "--pbsloadercommoninterface-tour-menu"))
                {
                    Add("fim", () => true, () => { Registrar("fim do tour curto"); Application.Quit(); }, 0.2f);
                    return;
                }
            }
            // --pbsloadercommoninterface-onibus=TEXTO: escolhe na lista o primeiro ônibus cujo nome contém TEXTO.
            string onibus = Array.Find(Environment.GetCommandLineArgs(), x => x.StartsWith("--pbsloadercommoninterface-onibus="))?.Substring("--pbsloadercommoninterface-onibus=".Length);
            if (!string.IsNullOrEmpty(onibus))
                Add(null, Visivel<TelaOnibus>, () =>
                {
                    var dd = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaOnibus>()?.listaEscolhaBus;
                    if (dd == null) { Registrar("lista de ônibus não encontrada"); return; }
                    for (int i = 0; i < dd.options.Count; i++)
                        if (dd.options[i].text.Contains(onibus, StringComparison.OrdinalIgnoreCase))
                        {
                            dd.value = i; dd.RefreshShownValue();
                            Registrar("ônibus escolhido: " + dd.options[i].text);
                            return;
                        }
                    Registrar("ônibus não encontrado: " + onibus);
                }, 1.0f);
            // Tour das telas: fotografa a prévia 3D do ônibus atual, de um segundo ônibus e, adiante, da pintura.
            bool telas = Array.Exists(args, x => x == "--pbsloadercommoninterface-tour-telas");
            if (telas)
            {
                // Cada ônibus de mod da lista em 3D (pronto, ou desistiu), depois as setas de baixo.
                bool Pronto3D() => Garagem.Previa3D.Pronto(Garagem.Previa3D.UltimoOnibus) || Garagem.Previa3D.Falhou(Garagem.Previa3D.UltimoOnibus);
                for (int k = 0; k < 14; k++)
                {
                    int idx = k;
                    Add(null, Visivel<TelaOnibus>, () =>
                    {
                        var dd = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaOnibus>()?.listaEscolhaBus;
                        if (dd == null) return;
                        int n = 0;
                        for (int i = 0; i < dd.options.Count; i++)
                            if (dd.options[i].text.StartsWith("MOD ") && n++ == idx) { dd.value = i; dd.RefreshShownValue(); Registrar("ônibus: " + dd.options[i].text); return; }
                    }, 0.3f);
                    Add($"3d_{idx:00}", () => Visivel<TelaOnibus>() && Pronto3D(), null, 1.6f);
                }
                Add(null, Visivel<TelaOnibus>, () => Clicar(Orig.Raiz("Canvas CENA ONIBUS")?.Find("btVolta"), "seta voltar"), 0.3f);
                Add("3d_seta1", () => Visivel<TelaOnibus>() && Pronto3D(), null, 1.6f);
                Add(null, Visivel<TelaOnibus>, () => Clicar(Orig.Raiz("Canvas CENA ONIBUS")?.Find("btVolta"), "seta voltar"), 0.3f);
                Add("3d_seta2", () => Visivel<TelaOnibus>() && Pronto3D(), null, 1.6f);
                Add(null, Visivel<TelaOnibus>, () =>
                {
                    var dd = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaOnibus>()?.listaEscolhaBus;
                    if (dd == null) return;
                    for (int i = 0; i < dd.options.Count; i++)
                        if (dd.options[i].text.Contains("Neobus", StringComparison.OrdinalIgnoreCase)) { dd.value = i; dd.RefreshShownValue(); return; }
                }, 0.3f);
                Add(null, () => Visivel<TelaOnibus>() && Pronto3D(), null, 0.5f);
            }
            Add("02_onibus", Visivel<TelaOnibus>, () => Clicar(Orig.Raiz("Canvas CENA ONIBUS")?.Find("btMostraHideConfigs"), "opções do ônibus"), 2.0f);
            Add("03_onibus_opcoes", () => Orig.Raiz("Canvas CENA ONIBUS")?.Find("botoes CONFIGS")?.gameObject.activeSelf == true,
                () => Clicar(Orig.Raiz("Canvas CENA ONIBUS")?.Find("botoes CONFIGS/btFecharTela"), "fechar opções"), 0.8f);
            Add(null, () => Orig.Raiz("Canvas CENA ONIBUS")?.Find("botoes CONFIGS")?.gameObject.activeSelf == false,
                () => Clicar(Orig.Raiz("Canvas CENA ONIBUS")?.Find("btNext"), "escolher pintura"), 0.3f);
            if (telas)
            {
                bool Pronto3D() => Garagem.Previa3D.Pronto(Garagem.Previa3D.UltimoOnibus) || Garagem.Previa3D.Falhou(Garagem.Previa3D.UltimoOnibus);
                Add("04a_pintura_3d", () => Visivel<TelaPintura>() && Pronto3D(), null, 2.0f);
                for (int k = 1; k <= 2; k++)
                {
                    int idx = k;
                    Add(null, Visivel<TelaPintura>, () =>
                    {
                        var dd = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaSkinPastaMod>()?.listaEscolhaSkin;
                        if (dd == null || dd.options.Count <= idx) return;
                        dd.value = idx; dd.RefreshShownValue();
                        Registrar("skin: " + dd.options[idx].text);
                    }, 0.3f);
                    Add($"04{(char)('a' + idx)}_pintura_skin{idx}", Visivel<TelaPintura>, null, 2.5f);
                }
            }
            Add("04_pintura", Visivel<TelaPintura>, () => Clicar(Orig.Raiz("Canvas CENA SKIN PASTA")?.Find("Button"), "escolher mapa"));
            // --pbsloadercommoninterface-mapa=TEXTO: escolhe na lista o primeiro mapa cujo nome contém TEXTO.
            string mapa = Array.Find(Environment.GetCommandLineArgs(), x => x.StartsWith("--pbsloadercommoninterface-mapa="))?.Substring("--pbsloadercommoninterface-mapa=".Length);
            if (!string.IsNullOrEmpty(mapa))
                Add(null, Visivel<TelaMapa>, () =>
                {
                    var dd = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaMapa>()?.listaEscolhaMapa;
                    if (dd == null) { Registrar("lista de mapas não encontrada"); return; }
                    for (int i = 0; i < dd.options.Count; i++)
                        if (dd.options[i].text.Contains(mapa, StringComparison.OrdinalIgnoreCase))
                        {
                            dd.value = i; dd.RefreshShownValue();
                            Registrar("mapa escolhido: " + dd.options[i].text);
                            return;
                        }
                    Registrar("mapa não encontrado: " + mapa);
                }, 1.0f);
            Add("05_mapa", Visivel<TelaMapa>, () =>
            {
                var c = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaMapa>();
                Clicar(c != null && c.botaoOpcoes != null ? c.botaoOpcoes.transform : null, "opções do mapa");
            });
            Add("06_mapa_opcoes", () => UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaMapa>()?.botoesConfig?.activeSelf == true, () =>
            {
                var c = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaMapa>();
                if (c != null && c.botoesConfig != null) c.botoesConfig.SetActive(false);
                var dd = c != null ? c.listaEscolhaMapa : null;
                var raiz = dd != null ? dd.GetComponentInParent<Canvas>().rootCanvas.transform : null;
                Clicar(Orig.BotaoQueChama(raiz, "SalvaMapaEVaiProEntrypoint"), "escolher linha");
            }, 0.8f);
            // Tour das telas (--pbsloadercommoninterface-tour-telas): percorre as seleções e volta, sem carregar a partida.
            if (Array.Exists(args, x => x == "--pbsloadercommoninterface-tour-telas"))
            {
                Add("07_linha", Visivel<TelaLinha>, () => Voltar(), 1.5f);
                Add(null, Visivel<TelaMapa>, () => Voltar(), 1.5f);
                Add(null, Visivel<TelaPintura>, () => Voltar(), 1.5f);
                Add(null, Visivel<TelaOnibus>, () => Voltar(), 1.5f);
                Add("fim", Visivel<TelaMenu>, () => { Registrar("fim do tour das telas"); Application.Quit(); }, 1.5f);
                return;
            }
            Add("07_linha", Visivel<TelaLinha>, () =>
            {
                var c = UnityEngine.Object.FindObjectOfType<Il2Cpp.cenaEscolhaLinha>();
                var dd = c != null ? c.listaEscolhaLinha : null;
                var raiz = dd != null ? dd.GetComponentInParent<Canvas>().rootCanvas.transform : null;
                var b = Orig.BotaoQueChama(raiz, "SalvaLinhaEVaiproMapa");
                // Igual ao botão "Iniciar viagem" da tela nova: clica o original e antecipa o carregamento.
                if (b != null) Clicar(b, "iniciar viagem"); else c?.SalvaLinhaEVaiproMapa();
                Carregando.Antecipar();
                _rajadaAte = Time.unscaledTime + 6f; // fotografa de perto o que aparece logo depois do clique
            });
            Add("08_carregando", () => GameObject.Find("PBSLoaderCommonInterface_Carregando") != null, null, 0.6f);
            // Partida carregada: painel de carregamento sumiu e a overlay do ALT foi montada.
            Add(null, () => GameObject.Find("PBSLoaderCommonInterface_Carregando") == null && TemTela<TelaAlt>(), () =>
            {
                ForcarAlt = true;
                // Avisos de teste (curto, detalhe longo, título longo) para fotografar os cards.
                Avisos.Notificar("Tour", "a1", Gravidade.Info, "Ajustes salvos", null, 60f);
                Avisos.Notificar("Tour", "a2", Gravidade.Aviso, "Porta traseira aberta",
                    "Feche a porta antes de sair do ponto: o freio de porta segura o ônibus enquanto ela estiver aberta", 60f);
                Avisos.Notificar("Tour", "a3", Gravidade.Falha, "Pressão do ar baixa no circuito do freio de serviço",
                    "4,2 bar · freio fraco · enchendo", 60f);
            }, 4.0f);
            _passos[_passos.Count - 1].limite = 300f; // mapas de mod pesados levam minutos para carregar
            Add(null, Visivel<TelaAlt>, () => Tela<TelaAlt>()?.Abrir(true), 0.5f);
            Add("09_alt", Visivel<TelaAlt>, () =>
            {
                Clicar(Orig.Raiz("CanvasPC", "btSeuOnibus")?.Find("btSeuOnibus"), "menu do veículo");
                _forcarSubmenu = "btSeuOnibus/menuSeuOnibus"; // o jogo fecha o submenu sem o ALT pressionado
            });
            Add("10_alt_menu_veiculo", () => Orig.Raiz("CanvasPC", "btSeuOnibus")?.Find("btSeuOnibus/menuSeuOnibus")?.gameObject.activeSelf == true, () =>
            {
                _forcarSubmenu = null;
                ForcarAlt = false;
                // Os painéis abrem pelo onClick dos botões originais, mesmo com o menu fechado.
                Clicar(Orig.Raiz("CanvasPC", "btSeuOnibus")?.Find("btSeuOnibus/menuSeuOnibus/btPosBancoMotorista"), "ajuste do banco");
            }, 0.8f);
            Add("11_banco", Visivel<TelaBanco>, () => Clicar(Orig.Raiz("Canvas", "botoesAjusteBancoMotorista")?.Find("botoesAjusteBancoMotorista/btFecharTela"), "fechar banco"));
            Add(null, () => !Visivel<TelaBanco>(), () => Clicar(Orig.Raiz("CanvasPC", "btSeuOnibus")?.Find("btSeuOnibus/menuSeuOnibus/btDestino"), "destino"), 0.5f);
            Add("12_destino", Visivel<TelaDestino>, () => Clicar(Orig.Raiz("Canvas", "botoesModsLetreiros")?.Find("botoesModsLetreiros/btFecharTela"), "fechar destino"));
            Add(null, () => !Visivel<TelaDestino>(), () => Clicar(Orig.Raiz("CanvasPC", "btHoraEClima")?.Find("btHoraEClima"), "hora e clima"), 0.5f);
            Add("13_clima", Visivel<TelaClima>, () => Clicar(Orig.Raiz("CanvasEnviro(Clone)", "painel do enviro")?.Find("painel do enviro/btFechar"), "fechar clima"));
            Add(null, () => !Visivel<TelaClima>(), () =>
                Clicar(Orig.Raiz("CanvasPC", "btSeuOnibus")?.Find("btSeuOnibus/menuSeuOnibus/btSalvarJogo"), "salvar"), 0.5f);
            Add("14_salvar", Visivel<TelaSalvar>, () => Clicar(Orig.Raiz("Canvas", "botoesSaveGame")?.Find("botoesSaveGame/btFecharTela"), "fechar salvar"));
            Add(null, () => !Visivel<TelaSalvar>(), () =>
                Clicar(Orig.Raiz("CanvasPC", "btControles")?.Find("btControles/menuControles/btSensibilidade"), "sensibilidade"), 0.5f);
            Add("15_controles", Visivel<TelaControles>, () => Clicar(Orig.Raiz("Canvas", "listaControles")?.Find("listaControles/btOK_fechar"), "fechar controles"));
            // Painéis sem botão fácil de alcançar: ativados direto, fotografados e fechados.
            foreach (var (print, painel) in new[] { ("16_pausa", "UI pausada PC"), ("17_espelhos", "botoesAjusteEspelhoVirtual"),
                                                    ("18_carga", "botoesAjustesCargas"), ("19_mais_opcoes", "botoesControlesExtras") })
            {
                Add(null, () => true, () => Orig.Raiz("Canvas", painel)?.Find(painel)?.gameObject.SetActive(true), 0.3f);
                Add(print, () => VisivelRaiz(painel), () => Orig.Raiz("Canvas", painel)?.Find(painel)?.gameObject.SetActive(false));
            }
            Add(null, () => true, () =>
                Clicar(Orig.Raiz("CanvasPC", "btSeuOnibus")?.Find("btSeuOnibus/menuSeuOnibus/btOpcoes Diversas"), "opções diversas"), 0.5f);
            for (int c = 0; c < 8; c++)
            {
                int cat = c;
                Add(null, Visivel<TelaOpcoes>, () => Tela<TelaOpcoes>()?.Mostrar(cat), 0.4f);
                Add($"{20 + cat}_config_{cat + 1}", Visivel<TelaOpcoes>, null, 0.9f);
            }
            Add("fim", () => true, () => { Registrar("fim do tour"); Application.Quit(); }, 0.2f);
        }

        // Aperta o botão Voltar da tela nova visível (caminho real: cobre, clica o original, revela).
        static void Voltar()
        {
            foreach (var t in _telas())
                if (t is TelaSelecao s && t.RaizNova != null && t.RaizNova.gameObject.activeInHierarchy) { s.ApertarVoltar(); return; }
            Registrar("  (sem tela de seleção para voltar)");
        }

        // Alguma tela nova visível cuja raiz original é o painel com esse nome.
        static bool VisivelRaiz(string painel)
        {
            foreach (var t in _telas())
                if (t.RaizNova != null && t.RaizNova.gameObject.activeInHierarchy && t.RaizOriginal != null && t.RaizOriginal.name == painel) return true;
            return false;
        }

        static bool TemTela<T>() where T : Tela
        {
            foreach (var t in _telas()) if (t is T) return true;
            return false;
        }

        // Chamado a cada frame (OnUpdate).
        internal static void Atualizar()
        {
            if (!Ativa || _i >= _passos.Count) return;
            if (Time.unscaledTime < _proximoPasso) return;
            var p = _passos[_i];

            if (!SoPartida && Time.unscaledTime < _rajadaAte && Time.unscaledTime >= _proximaRajada)
            {
                _proximaRajada = Time.unscaledTime + 0.25f;
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"rajada_{++_nRajada:00}.png"));
                Registrar($"rajada {_nRajada:00} cena={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            }

            // Durante o carregamento do mapa (o passo longo), fotografa a cada 3 s e grava a UI uma vez
            // no meio: é como se vê o que aparece entre a seleção da linha e a partida.
            if (!SoPartida && p.limite >= 300f && p.nome == null && Time.unscaledTime >= _proximaCarga)
            {
                _proximaCarga = Time.unscaledTime + 3f;
                _nCarga++;
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"carga_{_nCarga:00}.png"));
                if (_nCarga == 3 || _nCarga == 8) try { UiDumper.Despejar("carga" + _nCarga); } catch { }
            }

            bool pronto;
            try { pronto = p.pronto(); } catch { pronto = false; }
            if (!pronto)
            {
                _prontoDesde = -1;
                if (Time.unscaledTime - _desde > p.limite)
                {
                    // No modo partida o jogo nunca é fechado: só desiste do roteiro.
                    Registrar($"tempo esgotado no passo {_i} ({p.nome ?? "ação"}), {(SoPartida ? "parando o roteiro" : "encerrando")}");
                    if (!SoPartida) Application.Quit();
                    _i = _passos.Count;
                }
                return;
            }
            if (_prontoDesde < 0) _prontoDesde = Time.unscaledTime;
            if (Time.unscaledTime - _prontoDesde < p.espera) return;

            if (p.nome != null && p.nome != "fim")
            {
                var arq = Path.Combine(_dir, p.nome + ".png");
                ScreenCapture.CaptureScreenshot(arq);
                Registrar("print " + p.nome);
                try { VerificarBloqueios(); } catch (Exception e) { Registrar("bloqueios: " + e.Message); }
            }
            try { p.acao?.Invoke(); }
            catch (Exception e) { Registrar($"erro na ação do passo {_i}: {e.Message}"); }

            _i++;
            _prontoDesde = -1;
            _desde = Time.unscaledTime;
            _proximoPasso = Time.unscaledTime + 0.35f; // deixa o print ser gravado e a ação começar
        }

        // Clica (por raycast, como o mouse) no centro de cada botão visível das telas novas: se o primeiro
        // objeto atingido não for o botão, algo invisível por cima está roubando o clique.
        static readonly HashSet<string> _bloqueiosVistos = new();

        static void VerificarBloqueios()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) { Registrar("bloqueios: sem EventSystem"); return; }
            foreach (var t in _telas())
            {
                if (t.RaizNova == null || !t.RaizNova.gameObject.activeInHierarchy) continue;
                foreach (var sel in t.RaizNova.GetComponentsInChildren<Selectable>(false))
                {
                    if (!sel.IsActive() || !sel.interactable) continue;
                    var rt = sel.GetComponent<RectTransform>();
                    var c = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                    if (c.x < 0 || c.y < 0 || c.x > Screen.width || c.y > Screen.height) continue;
                    var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = c };
                    var res = new Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                    es.RaycastAll(ped, res);
                    if (res.Count == 0) continue;
                    var topo = res[0].gameObject;
                    if (topo == null || topo.transform.IsChildOf(sel.transform)) continue;
                    var cv = topo.GetComponentInParent<Canvas>();
                    var chave = Caminho(topo.transform) + " -> " + Caminho(sel.transform);
                    if (_bloqueiosVistos.Add(chave))
                        Registrar($"BLOQUEIO em {t.Nome}: '{Caminho(sel.transform)}' coberto por '{Caminho(topo.transform)}' (canvas {cv?.rootCanvas?.name} ordem {cv?.rootCanvas?.sortingOrder})");
                }
            }
        }

        static string Caminho(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null && s.Length < 160; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        // Mantém a overlay do ALT aberta durante o tour (o jogo só a ativa com a tecla pressionada).
        internal static void ManterAlt()
        {
            if (!Ativa || !ForcarAlt) return;
            var cpc = Orig.Raiz("CanvasPC", "btSeuOnibus");
            if (cpc != null && !cpc.gameObject.activeSelf) cpc.gameObject.SetActive(true);
            var sub = _forcarSubmenu != null ? cpc?.Find(_forcarSubmenu) : null;
            if (sub != null && !sub.gameObject.activeSelf) sub.gameObject.SetActive(true);
        }

        // ---------- diagnóstico de fontes ----------

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr ListaFn();

        // Registra se o Unity enxerga as fontes que o tema pede (instaladas só para o usuário, por exemplo).
        internal static void DiagnosticoFontes(params string[] procuradas)
        {
            try
            {
                var fn = IL2CPP.ResolveICall<ListaFn>("UnityEngine.Font::GetOSInstalledFontNames");
                var nomes = new Il2CppStringArray(fn());
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in nomes) set.Add(n);
                foreach (var p in procuradas)
                    MelonLogger.Msg($"[Fontes] '{p}': {(set.Contains(p) ? "encontrada" : "NÃO encontrada")}");
                MelonLogger.Msg($"[Fontes] {set.Count} fontes visíveis para o Unity");
            }
            catch (Exception e) { MelonLogger.Warning("[Fontes] diagnóstico falhou: " + e.Message); }
        }
    }
}
