using System;
using System.Collections.Generic;
using MelonLoader;
using PBSLoaderCommonUtils;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(PBSLoaderCommonInterface.PBSLoaderCommonInterfaceMod), "PBSLoaderCommonInterface", "0.4.0", "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]

namespace PBSLoaderCommonInterface
{
    public class PBSLoaderCommonInterfaceMod : MelonMod
    {
        static MelonPreferences_Entry<bool> _ativo;

        // Telas redesenhadas. Várias podem estar ativas juntas (ex.: overlay do ALT + configurações
        // + painel do banco na partida). O que não tem tela aqui continua com a UI original.
        static readonly Func<Tela>[] Telas =
        {
            () => new TelaMenu(), () => new TelaOnibus(), () => new TelaPintura(),
            () => new TelaMapa(), () => new TelaLinha(), () => new TelaCarga(),
            () => new TelaAlt(), () => new TelaOpcoes(), () => new TelaBanco(), () => new TelaDestino(), () => new TelaClima(),
            () => new TelaPausa(), () => new TelaSalvar(), () => new TelaControles(),
            // Painéis simples do HUD, gerados pelo formulário
            () => new TelaPainelAuto("botoesAjusteEspelhoVirtual", "Espelhos virtuais"),
            () => new TelaPainelAuto("botoesAjustesCargas", "Carga"),
            () => new TelaMaisOpcoes(),
            () => new TelaPainelAuto("botoesAjustesLuzDia", "Luz do dia"),
            () => new TelaPainelAuto("botoesAjustesEfeitosUnity", "Efeitos visuais"),
            () => new TelaPainelAuto("botoesAjusteIluminacao", "Iluminação"),
            () => new TelaPainelAuto("botoesControlesTerreno", "Terreno"),
        };

        RectTransform _canvas;
        readonly List<Tela> _telas = new();
        readonly HashSet<string> _comErro = new();        // não remonta em loop uma tela que deu erro (por Nome)
        readonly Dictionary<Tela, string> _motivoOriginal = new();
        float _procurarRapidoAte, _proximaProcura;

        Carregando _carregando;
        float _procurarCarregandoAte;

        float _gravarEm = -1;
        string _cenaParaGravar;
        readonly HashSet<string> _cenasGravadas = new();

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("PBSLoaderCommonInterface", "UI Moderna");
            _ativo = cat.CreateEntry("UiNovaAtiva", true, "Usa a UI nova nas telas redesenhadas");
            // Atalhos ficam no registro comum (PBSLoaderCommonUtils, categoria "Atalhos"), com os dos outros mods.
            Atalhos.Registrar("PBSLoaderCommonInterface", "Alternar", "Interface nova ou original", "Ctrl+Alt+F2", () =>
            {
                _ativo.Value = !_ativo.Value;
                LoggerInstance.Msg(_ativo.Value ? "UI nova ligada" : "UI original");
            });
            Atalhos.Registrar("PBSLoaderCommonInterface", "GravarUi", "Gravar a hierarquia da interface", "", () =>
            {
                Gravar("manual");
                foreach (var t in _telas)
                    try { Auditar(t, true); } catch (Exception e) { LoggerInstance.Warning("Falha na auditoria: " + e.Message); }
            }, "Depuração: grava a UI em UserData e audita o layout");
            TelaAlt.TeclaMenu = Atalhos.Registrar("PBSLoaderCommonInterface", "MenuLateral", "Menu lateral da viagem", "BackQuote",
                descricao: "Abre e fecha o menu lateral durante a partida");
            TelaAlt.TeclaCarroceria = Atalhos.Registrar("PBSLoaderCommonInterface", "Carroceria", "Painel da carroceria", "",
                descricao: "Abre e fecha o painel da direita com luzes, portas, ar-condicionado e outros controles do ônibus");
            Theme.CriarPreferencias(cat);
            Som.CriarPreferencias(cat);
            Garagem.Previa3D.CriarPreferencias(cat);
            Garagem.Previa3D.CriarPreferenciasCinema(cat);
            HudAvisos.CriarPreferencias(cat);
            HudPainel.CriarPreferencias(cat);
            Hud.CriarPreferencias(cat);
            PermissaoScripts.Iniciar();
            TesteSdk.Iniciar();
            Atalhos.Registrar("PBSLoaderCommonInterface", "Painel", "Mostrar ou esconder o painel de condução", "", HudPainel.Alternar,
                "Velocidade, rotação e luzes no canto inferior direito");
            Inspecao.DiagnosticoFontes("Bahnschrift SemiBold Condensed", "Bahnschrift Condensed", "Bahnschrift", "Segoe UI");
            Inspecao.Iniciar(() => _telas);
        }

        // sceneLoaded roda antes do primeiro frame da cena: montar aqui evita que a UI original
        // apareça por alguns frames. Telas cujo original continua vivo (cena aditiva, como as
        // configurações abertas durante a partida) não são fechadas.
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Podar();
            _comErro.Clear();
            // Troca de cena no meio da carga: a tela nova segura até achar o painel da cena nova.
            _carregando?.Soltar();
            _carregando = null;
            Carregando.Segurar(4f);
            _proximaProcura = 0f;
            _procurarRapidoAte = Time.unscaledTime + 30f;
            _procurarCarregandoAte = Time.unscaledTime + 8f;
            // Gravar a UI percorre a hierarquia toda (travada): só no tour de inspeção; no jogo, pelo atalho "GravarUi".
            if (Inspecao.Ativa && !_cenasGravadas.Contains(sceneName)) { _cenaParaGravar = sceneName; _gravarEm = Time.unscaledTime + 1.5f; }
            Desempenho.Marco("cena carregada (" + sceneName + ")");
            using (Desempenho.Medir("·cena (total)")) Tick();
        }

        public override void OnUpdate()
        {
            if (_gravarEm >= 0 && Time.unscaledTime >= _gravarEm)
            {
                _gravarEm = -1;
                if (_cenasGravadas.Add(_cenaParaGravar)) Gravar("original");
            }
            Tick();
            using (Desempenho.Medir("som"))
            {
                Som.Iniciar();
                Som.Atualizar();
                Som.Musica(_ativo.Value && EmMenu());
            }
            using (Desempenho.Medir("painel mods")) TelaMods.Atualizar();
            using (Desempenho.Medir("paineis")) Paineis.Atualizar();
            using (Desempenho.Medir("permissão")) PermissaoScripts.Atualizar();
            using (Desempenho.Medir("visuais")) Kit.AtualizarVisuais();
            using (Desempenho.Medir("animações")) { Anim.Atualizar(); Anim.AtualizarTransicao(); }
            using (Desempenho.Medir("prévia 3D")) Garagem.Previa3D.Atualizar();
            using (Desempenho.Medir("painel")) HudPainel.Atualizar(_ativo.Value);
            using (Desempenho.Medir("avisos")) HudAvisos.Atualizar(_ativo.Value);
            using (Desempenho.Medir("cartões")) Hud.Atualizar(_ativo.Value);
            using (Desempenho.Medir("·tour")) Inspecao.Atualizar();
            TesteSdk.Quadro();
        }

        // Também no LateUpdate: se algo do jogo mudar a UI original depois do nosso Update,
        // o estado (original escondido/visível) é reaplicado antes de a tela ser desenhada.
        public override void OnLateUpdate()
        {
            using (Desempenho.Medir("mostrar (late)"))
            {
                Inspecao.ManterAlt();
                // Painel do mod aberto na partida (clima, banco, destino, configurações...): o cursor fica
                // livre e a área de arraste da câmera do jogo deixa de roubar os cliques.
                bool painel = _telas.Exists(t => t is TelaFormulario && t.RaizNova != null && t.RaizNova.gameObject.activeInHierarchy);
                TelaAlt.ManterCursor(painel);
                AreasDeClique(painel);
                foreach (var t in _telas)
                {
                    try { if (t.Viva) t.Mostrar(_ativo.Value && !t.PrecisaOriginal()); }
                    catch { }
                }
            }
            Desempenho.FimDoQuadro();
        }

        void Tick()
        {
            using (Desempenho.Medir("carregamento")) TickCarregando();
            using (Desempenho.Medir("procurar")) Procurar();
            Podar();

            for (int i = _telas.Count - 1; i >= 0; i--)
            {
                var t = _telas[i];
                try
                {
                    var motivo = t.ElementoNaoCoberto();
                    _motivoOriginal.TryGetValue(t, out var anterior);
                    if (motivo != null && motivo != anterior)
                        LoggerInstance.Msg($"[{t.Nome}] '{motivo}' não tem versão nova: mostrando a UI original enquanto estiver aberto");
                    _motivoOriginal[t] = motivo;

                    bool nova = _ativo.Value && motivo == null;
                    using (Desempenho.Medir("mostrar")) t.Mostrar(nova);
                    if (nova && t.RaizNova.gameObject.activeInHierarchy)
                    {
                        using (Desempenho.Medir("sync " + t.Nome)) t.Atualizar();
                        if (Inspecao.Ativa) Auditar(t, false); // auditoria contínua só no tour (o atalho "GravarUi" audita na hora)
                    }
                }
                catch (Exception e)
                {
                    // Erro na UI nova não pode travar o jogo: volta para a original nesta tela.
                    LoggerInstance.Error($"Erro na tela {t.Nome}, voltando para a UI original: {e}");
                    Fechar(t);
                    _comErro.Add(t.Nome);
                }
            }
        }

        // Procura telas ainda não montadas: rápido logo após a troca de cena, depois a cada 1 s
        // (configurações e painéis podem ser carregados sob demanda no meio da partida).
        void Procurar()
        {
            if (Time.unscaledTime < _proximaProcura) return;
            _proximaProcura = Time.unscaledTime + (Time.unscaledTime <= _procurarRapidoAte ? 0.25f : 1f);

            foreach (var criar in Telas)
            {
                var t = criar();
                var nome = t.Nome;
                if (_comErro.Contains(nome) || _telas.Exists(x => x.Nome == nome)) continue;
                try
                {
                    if (!t.Detectar()) continue;
                    Theme.Carregar();
                    var ini = System.Diagnostics.Stopwatch.GetTimestamp();
                    using (Desempenho.Medir("·montar " + t.Nome)) t.Montar(Canvas());
                    _telas.Add(t);
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - ini) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    LoggerInstance.Msg($"Tela '{t.Nome}' montada em {ms:0} ms");
                    Desempenho.Marco("montada " + t.Nome);
                }
                catch (Exception e)
                {
                    LoggerInstance.Error($"Erro ao montar a tela {t.Nome}, mantendo a UI original: {e}");
                    try { t.Destruir(); } catch { }
                    _comErro.Add(nome);
                }
            }
        }

        // Descarta telas cujo original foi destruído (troca de cena).
        void Podar()
        {
            for (int i = _telas.Count - 1; i >= 0; i--)
                if (!_telas[i].Viva) Fechar(_telas[i]);
        }

        void Fechar(Tela t)
        {
            try { t.Destruir(); } catch { }
            _telas.Remove(t);
            _motivoOriginal.Remove(t);
        }

        // Auditoria de layout: enquanto a tela está visível, mede a cada 1 s (depois de o layout
        // assentar) e registra só problemas ainda não vistos nesta sessão.
        readonly Dictionary<string, HashSet<string>> _jaReportado = new();
        readonly Dictionary<string, float> _proximaAuditoria = new();

        void Auditar(Tela t, bool forcar)
        {
            _proximaAuditoria.TryGetValue(t.Nome, out var quando);
            if (!forcar && Time.unscaledTime < quando) return;
            _proximaAuditoria[t.Nome] = Time.unscaledTime + 1f;
            var raiz = t.RaizNova;
            if (raiz == null || !raiz.gameObject.activeInHierarchy) return;
            // Durante a entrada a tela ainda está deslocada pelo deslize: medir agora acusaria "fora da tela".
            if (!forcar && Mathf.Abs(raiz.anchoredPosition.x) > 0.5f) { _proximaAuditoria[t.Nome] = Time.unscaledTime + 0.3f; return; }

            if (!_jaReportado.TryGetValue(t.Nome, out var vistos))
                _jaReportado[t.Nome] = vistos = new HashSet<string>();

            var novos = new List<string>();
            foreach (var p in Auditoria.Verificar(raiz))
                if (forcar || vistos.Add(p)) novos.Add(p);
            if (novos.Count == 0)
            {
                if (forcar) LoggerInstance.Msg($"[Auditoria {t.Nome}] nenhum problema de layout");
                return;
            }

            var arq = Auditoria.Gravar(t.Nome, novos);
            LoggerInstance.Msg($"[Auditoria {t.Nome}] {novos.Count} problema(s) de layout, detalhes em {arq}");
            for (int i = 0; i < novos.Count && i < 12; i++) LoggerInstance.Msg("  " + novos[i]);
        }

        // Áreas invisíveis do jogo que recebem clique por cima dos painéis: o "JoystickCameras" (arrastar
        // para girar a câmera) cobre o meio da tela e ficava com os cliques dos botões do clima, do banco...
        // Com um painel aberto, elas deixam de receber cliques; ao fechar, voltam como eram.
        const string AreaCamera = "UI normal/JoystickCameras";
        readonly List<Graphic> _areasDesligadas = new();
        float _procurarAreaEm;

        void AreasDeClique(bool painel)
        {
            if (painel && _areasDesligadas.Count == 0 && Time.unscaledTime >= _procurarAreaEm)
            {
                _procurarAreaEm = Time.unscaledTime + 1f;
                var area = Orig.Raiz("Canvas", AreaCamera)?.Find(AreaCamera);
                if (area == null) return;
                foreach (var g in area.GetComponentsInChildren<Graphic>(true))
                    if (g.raycastTarget) { g.raycastTarget = false; _areasDesligadas.Add(g); }
            }
            else if (!painel && _areasDesligadas.Count > 0)
            {
                foreach (var g in _areasDesligadas) if (g != null && !g.WasCollected) g.raycastTarget = true;
                _areasDesligadas.Clear();
                _procurarAreaEm = 0f;
            }
        }

        // A partida (menu lateral) ou o menu principal à vista: a carga acabou (ou nem começou).
        bool TelaDePartidaOuMenu()
        {
            foreach (var t in _telas)
                if ((t is TelaAlt || t is TelaMenu) && t.RaizNova != null && t.RaizNova.gameObject.activeInHierarchy) return true;
            return false;
        }

        // Menus = menu principal, seleções e carregamento (onde a música toca). Na partida, silêncio.
        bool EmMenu()
        {
            if (_carregando != null && _carregando.Visivel) return true;
            foreach (var t in _telas)
                if ((t is TelaMenu || t is TelaSelecao) && t.RaizNova != null && t.RaizNova.gameObject.activeInHierarchy) return true;
            return false;
        }

        // Tela de carregamento: independente das telas, porque fica dentro do Canvas da HUD.
        void TickCarregando()
        {
            try
            {
                // Procura a cada frame enquanto a tela está sendo segurada (cada frame da carga pode
                // durar segundos); fora disso, só logo depois de uma troca de cena.
                if (_carregando == null && (Carregando.Segurando || (Time.unscaledTime <= _procurarCarregandoAte && Time.unscaledTime >= _proximaProcura)))
                {
                    _carregando = Carregando.Procurar();
                    if (_carregando != null) LoggerInstance.Msg("Tela de carregamento vinculada");
                }
                if (_carregando != null && !_carregando.Viva)
                {
                    _carregando.Soltar(); // painel destruído: a tela nova fica até o próximo aparecer
                    _carregando = null;
                    Carregando.Segurar(4f);
                }
                bool outra = TelaDePartidaOuMenu();
                if (_carregando != null) _carregando.Atualizar(_ativo.Value, outra);
                else Carregando.AtualizarSemAlvo(_ativo.Value, outra);
            }
            catch (Exception e)
            {
                LoggerInstance.Error("Erro na tela de carregamento, voltando para a original: " + e);
                _carregando?.Destruir();
                _carregando = null;
                _procurarCarregandoAte = 0;
            }
        }

        // Canvas próprio, persistente entre cenas, abaixo dos outros canvases do jogo
        // (telas que precisam ficar por cima usam um Canvas aninhado com ordem própria).
        RectTransform Canvas()
        {
            if (_canvas != null && !_canvas.WasCollected) return _canvas;
            var go = new GameObject("PBSLoaderCommonInterface") { layer = 5 };
            UnityEngine.Object.DontDestroyOnLoad(go);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = -10;
            cv.pixelPerfect = false;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            sc.referencePixelsPerUnit = 100;
            go.AddComponent<GraphicRaycaster>();
            _canvas = go.GetComponent<RectTransform>();
            return _canvas;
        }

        void Gravar(string sufixo)
        {
            try { LoggerInstance.Msg("UI gravada em " + UiDumper.Despejar(sufixo)); }
            catch (Exception e) { LoggerInstance.Warning("Falha ao gravar a UI: " + e.Message); }
        }
    }
}
