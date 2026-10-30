using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Tela de carregamento da partida. No jogo ela é o painel "fundo tela aguarde" dentro do
    // Canvas da HUD, então não dá para esconder o Canvas inteiro como nas outras telas: só esse
    // painel recebe um CanvasGroup transparente, e a versão nova é desenhada num Canvas próprio
    // acima de tudo enquanto o painel original estiver ativo.
    //
    // O painel original nasce depois do clique em "Iniciar viagem" e é recriado na troca de cena;
    // com os quadros lentos do carregamento, cada vão entre um painel e o próximo deixava a tela
    // antiga à vista por segundos. Por isso o Canvas novo tem vida própria: aparece já no clique
    // (Antecipar), continua na tela sem alvo enquanto um painel novo é procurado (Segurar) e só
    // some quando o jogo mantém o painel inativo por um instante, ou a partida/menu aparecem.
    internal sealed class Carregando
    {
        const string Painel = "fundo tela aguarde";

        Transform _alvo;
        CanvasGroup _grupo;
        bool _grupoCriado;
        static GameObject _canvas;
        static Text _status, _detalhe, _pct, _log;
        static RectTransform _trilho, _preench;
        static readonly List<string> _linhas = new();
        static string _ultimoStatus, _ultimoDetalhe;

        internal static Carregando Procurar()
        {
            var r = Orig.Raiz("Canvas", Painel);
            if (r == null) return null;
            Construir();
            // O registro só recomeça numa viagem nova (não a cada painel recriado no meio da carga).
            if (!_canvas.activeSelf) { _linhas.Clear(); _ultimoStatus = _ultimoDetalhe = null; _log.text = ""; }
            return new Carregando { _alvo = r.Find(Painel) };
        }

        static float _seguraAte = -1f, _inativoDesde = -1f;
        const float MargemInativo = 0.4f;

        // Mostra a tela já no clique que inicia a carga, antes de o jogo criar o painel dele.
        internal static void Antecipar()
        {
            Construir();
            Segurar(25f);
            if (_canvas.activeSelf) return;
            _linhas.Clear(); _ultimoStatus = _ultimoDetalhe = null; _log.text = "";
            _status.text = "Preparando o mapa e os veículos";
            _detalhe.text = "";
            _mostrado = 0f;
            _canvas.SetActive(true);
            Anim.Coreografia(_canvas.transform.GetChild(0).GetComponent<RectTransform>(), true);
        }

        // Mantém a tela por mais "segundos" mesmo sem painel original (troca de cena, painel recriado).
        internal static void Segurar(float segundos)
        {
            if (_canvas == null || _canvas.WasCollected) return;
            _seguraAte = Mathf.Max(_seguraAte, Time.unscaledTime + segundos);
        }

        internal static bool Segurando => _canvas != null && !_canvas.WasCollected && _canvas.activeSelf && Time.unscaledTime < _seguraAte;

        // Sem painel original: continua na tela (barra indeterminada) até o prazo, ou até a partida ou o
        // menu aparecerem.
        internal static void AtualizarSemAlvo(bool usarNova, bool outraTelaVisivel)
        {
            if (_canvas == null || _canvas.WasCollected || !_canvas.activeSelf) return;
            if (!usarNova || outraTelaVisivel || Time.unscaledTime >= _seguraAte) { _canvas.SetActive(false); _seguraAte = -1f; return; }
            Indeterminada();
        }

        static void Indeterminada()
        {
            float w = _trilho.rect.width;
            float seg = w * 0.22f;
            float t = Mathf.PingPong(Time.unscaledTime * 0.55f, 1f);
            _preench.sizeDelta = new Vector2(seg, 0);
            _preench.anchoredPosition = new Vector2(Mathf.SmoothStep(0, w - seg, t), 0);
            _pct.text = "--%";
        }

        static float _mostrado;

        internal bool Viva => _alvo != null && !_alvo.WasCollected;
        internal bool Visivel => _canvas != null && !_canvas.WasCollected && _canvas.activeSelf;

        internal void Atualizar(bool usarNova, bool outraTelaVisivel)
        {
            bool ativo = Viva && _alvo.gameObject.activeInHierarchy;
            // Painel inativo por um instante (fim da carga) esconde; um piscar dele (recriação) não.
            if (ativo) _inativoDesde = -1f;
            else if (_inativoDesde < 0f) _inativoDesde = Time.unscaledTime;
            bool segurar = !ativo && !outraTelaVisivel && _canvas.activeSelf
                && (Time.unscaledTime - _inativoDesde < MargemInativo || Time.unscaledTime < _seguraAte);
            bool mostrar = usarNova && (ativo || segurar);
            if (ativo) _seguraAte = -1f; // o painel apareceu: a antecipação cumpriu o papel
            // O antigo fica escondido sempre que a tela nova estiver na tela (ela é opaca, mas nada de
            // deixar o antigo por trás para aparecer num quadro de transição).
            EsconderOriginal(mostrar);
            if (_canvas.activeSelf != mostrar)
            {
                _canvas.SetActive(mostrar);
                if (mostrar) Anim.Coreografia(_canvas.transform.GetChild(0).GetComponent<RectTransform>(), true);
            }
            if (!mostrar) return;
            if (!ativo) { Indeterminada(); return; }

            var st = Orig.Limpo(TextoSeAtivo("texto progresso loading"));
            if (string.IsNullOrEmpty(st)) st = "Preparando o mapa e os veículos";
            if (_status.text != st) _status.text = st;
            var det = Tela.NomeLegivel(TextoSeAtivo("texto progresso detalhe")) ?? "";
            if (_detalhe.text != det) _detalhe.text = det;
            Registrar(st, det);

            float w = _trilho.rect.width;
            var sl = _alvo.Find("slider progresso");
            var slider = sl != null && sl.gameObject.activeSelf ? sl.GetComponent<Slider>() : null;
            if (slider != null)
            {
                // A barra e o número correm até o valor do jogo em vez de pular a cada arquivo.
                float alvo = slider.normalizedValue;
                _mostrado = alvo < _mostrado ? alvo : Mathf.Lerp(_mostrado, alvo, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.18f));
                float v = _mostrado;
                _preench.anchoredPosition = Vector2.zero;
                _preench.sizeDelta = new Vector2(Mathf.Max(2f, w * v), 0);
                _pct.text = Mathf.RoundToInt(v * 100) + "%";
            }
            else Indeterminada(); // sem progresso conhecido: segmento indo e voltando
        }

        // Log de inicialização: cada mudança de status/detalhe vira uma linha "> ...".
        static void Registrar(string status, string detalhe)
        {
            bool mudou = false;
            if (status != _ultimoStatus) { _ultimoStatus = status; _linhas.Add("> " + status); mudou = true; }
            if (!string.IsNullOrEmpty(detalhe) && detalhe != _ultimoDetalhe) { _ultimoDetalhe = detalhe; _linhas.Add("  " + detalhe); mudou = true; }
            if (!mudou) return;
            while (_linhas.Count > 5) _linhas.RemoveAt(0);
            _log.text = string.Join("\n", _linhas);
        }

        string TextoSeAtivo(string nome)
        {
            var t = _alvo.Find(nome);
            return t != null && t.gameObject.activeSelf ? Orig.Texto(t) : null;
        }

        void EsconderOriginal(bool esconder)
        {
            if (!Viva) return;
            if (_grupo == null || _grupo.WasCollected)
            {
                _grupo = _alvo.GetComponent<CanvasGroup>();
                if (_grupo == null) { _grupo = _alvo.gameObject.AddComponent<CanvasGroup>(); _grupoCriado = true; }
            }
            float a = esconder ? 0f : 1f;
            if (_grupo.alpha != a) _grupo.alpha = a;
        }

        // Devolve o painel original (sem esconder a tela nova: quem decide é Atualizar/AtualizarSemAlvo).
        internal void Soltar()
        {
            if (_grupo != null && !_grupo.WasCollected)
            {
                if (_grupoCriado) Object.Destroy(_grupo);
                else _grupo.alpha = 1f;
            }
        }

        internal void Destruir()
        {
            Soltar();
            if (_canvas != null) _canvas.SetActive(false);
        }

        static void Construir()
        {
            if (_canvas != null && !_canvas.WasCollected) return;
            Theme.Carregar();

            _canvas = new GameObject("PBSLoaderCommonInterface_Carregando") { layer = 5 };
            Object.DontDestroyOnLoad(_canvas);
            var cv = _canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 1000; // acima da HUD e de qualquer outro Canvas do jogo
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<GraphicRaycaster>();
            var raiz = Kit.Encher(Kit.Novo("Raiz", _canvas.transform));

            Kit.Atmosfera(raiz);
            raiz.GetComponent<Image>().raycastTarget = true; // bloqueia cliques durante o carregamento
            Kit.Cabecalho(raiz, "Proton Bus", "Carregando partida", out _, out _);

            // Bloco no canto inferior esquerdo, como a apresentação de etapa do DiRT Rally 2.0:
            // título enorme em negrito itálico, status embaixo e o percentual grande em vermelho.
            var bloco = Kit.Novo("Bloco", raiz);
            bloco.anchorMin = new Vector2(0, 0); bloco.anchorMax = new Vector2(1, 0); bloco.pivot = new Vector2(0, 0);
            bloco.offsetMin = new Vector2(96, Kit.AlturaRodape + 64); bloco.offsetMax = new Vector2(-96, Kit.AlturaRodape + 64);
            var vl = Kit.Coluna(bloco, 10);
            _ = vl;
            bloco.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var topo = Kit.Novo("Topo", bloco);
            Kit.Linha(topo, 24, 0, TextAnchor.LowerLeft);
            Kit.Tam(topo, -1, 120);
            var titulos = Kit.Novo("Titulos", topo);
            Kit.Coluna(titulos, 4, 0, TextAnchor.LowerLeft);
            Kit.Tam(titulos, -1, 120, 1);
            Kit.Tam(Kit.Trilha(titulos, "Carregando partida", 14), -1, 18);
            var titulo = Kit.Texto(titulos, "Preparando a viagem", 56, Theme.Texto, true);
            titulo.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(titulo, -1, 64);
            _status = Kit.Texto(titulos, "", 16, Theme.TextoDim);
            Kit.Tam(_status, -1, 22);

            _pct = Kit.Texto(topo, "--%", 88, Theme.AcentoVivo, true, TextAnchor.LowerRight);
            _pct.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(_pct, 300, 120);

            _trilho = Kit.Novo("Trilho", bloco);
            Kit.Tam(_trilho, -1, 4);
            Kit.Fundo(_trilho, null, Theme.LinhaForte);
            _preench = Kit.Novo("Progresso", _trilho);
            _preench.anchorMin = new Vector2(0, 0); _preench.anchorMax = new Vector2(0, 1); _preench.pivot = new Vector2(0, 0.5f);
            _preench.sizeDelta = Vector2.zero;
            Kit.Fundo(_preench, null, Theme.Acento);

            Kit.Espaco(bloco, 4);
            var linhaLog = Kit.Novo("Registro", bloco);
            Kit.Linha(linhaLog, 24, 0, TextAnchor.UpperLeft);
            Kit.Tam(linhaLog, -1, 80);
            _log = Kit.Texto(linhaLog, "", 12, Theme.TextoMudo);
            _log.verticalOverflow = VerticalWrapMode.Overflow;
            _log.lineSpacing = 1.3f;
            _log.alignment = TextAnchor.UpperLeft;
            Kit.Tam(_log, -1, 80, 1);
            _detalhe = Kit.Texto(linhaLog, "", 13, Theme.TextoDim, false, TextAnchor.UpperRight);
            Kit.Tam(_detalhe, 520, 80);

            Kit.Rodape(raiz, () => "Proton Bus  ·  Carregando " + (_ultimoDetalhe ?? "partida"));

            _canvas.SetActive(false);
        }
    }
}
