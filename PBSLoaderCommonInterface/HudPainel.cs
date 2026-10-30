using MelonLoader;
using PBSLoaderCommonUtils;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Painel básico de condução no canto inferior direito, com a mesma largura e o mesmo visual dos avisos
    // (HudAvisos fica logo acima dele):
    //   linha 1   velocidade grande, marcha numa caixa e o limitador quando ativo
    //   linha 2   barra de rotação (vermelha perto do corte) e o número
    //   linha 3   luzes de painel: setas (no ritmo do pisca do jogo), farol, freio de mão, porta, parada solicitada, ar
    // Luzes apagadas ficam quase invisíveis; a parada solicitada pulsa ao acender. Nada recebe clique.
    internal static class HudPainel
    {
        internal static MelonPreferences_Entry<bool> Pref;

        internal static void CriarPreferencias(MelonPreferences_Category cat)
        {
            Pref = cat.CreateEntry("PainelNaTela", true, "Painel de condução na tela",
                "Velocidade, rotação, marcha e luzes do painel (setas, farol, freio de mão, porta, parada solicitada, ar) no canto inferior direito");
        }

        internal static void Alternar()
        {
            Pref.Value = !Pref.Value;
            MelonPreferences.Save();
        }

        const float Largura = 300f, Altura = 98f;

        static readonly Color Verde = new(0.24f, 0.86f, 0.52f, 1f);
        static readonly Color Azul = new(0.30f, 0.64f, 1f, 1f);
        static readonly Color Apagada = new(1f, 1f, 1f, 0.13f);

        static GameObject _canvas;
        static RectTransform _raiz, _rpmEnche;
        static Image _rpmCor;
        static Text _vel, _marcha, _limite, _rpm;
        static Text _setaE, _setaD, _farol, _mao, _porta, _parada, _ar;
        static int _velMostrada = -1, _rpmMostrada = -1;
        static string _marchaMostrada, _arMostrado;
        static float _paradaDesde = -10f;
        static bool _paradaAntes;

        /// <summary>Altura que o painel ocupa agora (0 escondido), para os avisos ficarem em cima.</summary>
        internal static float Ocupa { get; private set; }

        static void Construir()
        {
            if (_canvas != null && !_canvas.WasCollected) return;
            Theme.Carregar();
            _canvas = new GameObject("PBSLoaderCommonInterface_Painel") { layer = 5 };
            Object.DontDestroyOnLoad(_canvas);
            var cv = _canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 300;
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;

            _raiz = Kit.Novo("Painel", _canvas.transform);
            Kit.Ancorar(_raiz, new Vector2(1, 0), new Vector2(-24, 24), new Vector2(Largura, Altura));
            Kit.Fundo(_raiz, Theme.Solido, Theme.Alfa(Theme.Painel, 0.84f)).raycastTarget = false;
            Kit.Marca(_raiz, Theme.Acento, 3f).raycastTarget = false;
            var col = Kit.Coluna(_raiz, 6, 0, TextAnchor.UpperLeft);
            Kit.Pad(col, 15, 12, 8, 8);

            // Linha 1: velocidade, unidade, limitador, marcha.
            var l1 = Kit.Novo("Velocidade", _raiz);
            Kit.Linha(l1, 6, 0, TextAnchor.LowerLeft);
            Kit.Tam(l1, -1, 38);
            _vel = Kit.Texto(l1, "0", 34, Theme.Texto, true, TextAnchor.LowerLeft);
            _vel.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(_vel, -1, 38); // largura do texto: "km/h" fica colado no número
            var un = Kit.Texto(l1, "km/h", 12, Theme.TextoMudo, false, TextAnchor.LowerLeft);
            Kit.Tam(un, 36, 30);
            _limite = Kit.Texto(l1, "", 11, Theme.Aviso, true, TextAnchor.LowerLeft);
            _limite.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(_limite, -1, 30, 1);
            var caixa = Kit.Painel(l1, "Marcha", Theme.Cartao);
            Kit.Tam(caixa, 38, 34);
            _marcha = Kit.Texto(Kit.Encher(Kit.Novo("Texto", caixa)), "N", 20, Theme.Texto, true, TextAnchor.MiddleCenter);
            Kit.Encher(_marcha.rectTransform);

            // Linha 2: rotação.
            var l2 = Kit.Novo("Rotacao", _raiz);
            Kit.Linha(l2, 8, 0, TextAnchor.MiddleLeft);
            Kit.Tam(l2, -1, 14);
            var trilho = Kit.Novo("Trilho", l2);
            Kit.Fundo(trilho, Theme.Solido, Theme.Linha).raycastTarget = false;
            Kit.Tam(trilho, -1, 5, 1);
            _rpmEnche = Kit.Novo("Enche", trilho);
            _rpmEnche.anchorMin = Vector2.zero; _rpmEnche.anchorMax = new Vector2(0, 1);
            _rpmEnche.offsetMin = _rpmEnche.offsetMax = Vector2.zero;
            _rpmCor = Kit.Fundo(_rpmEnche, Theme.Solido, Theme.TextoDim);
            _rpmCor.raycastTarget = false;
            _rpm = Kit.Texto(l2, "0", 11, Theme.TextoMudo, false, TextAnchor.MiddleRight);
            Kit.Tam(_rpm, 64, 14);

            // Linha 3: luzes de painel.
            var l3 = Kit.Novo("Luzes", _raiz);
            Kit.Linha(l3, 4, 0, TextAnchor.MiddleLeft);
            Kit.Tam(l3, -1, 18);
            _setaE = Luz(l3, Ico.SetaEsq, true, 18);
            _farol = Luz(l3, Ico.Lampada, true, 22);
            _mao = Luz(l3, "P", false, 18);
            _porta = Luz(l3, "PORTA", false, 42);
            _parada = Luz(l3, "PARADA", false, 50);
            _ar = Luz(l3, "AR", false, 58);
            Kit.Espaco(l3, -1, -1, true);
            _setaD = Luz(l3, Ico.SetaDir, true, 18);

            _canvas.SetActive(false);
        }

        static Text Luz(Transform pai, string s, bool icone, float largura)
        {
            var t = icone ? Kit.Icone(pai, s, 13, Apagada) : Kit.Texto(pai, s, 11, Apagada, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(t, largura, 18);
            return t;
        }

        // Por frame. "mostrar": a UI nova está ligada.
        internal static void Atualizar(bool mostrar)
        {
            bool ligado = mostrar && (Pref == null || Pref.Value) && Veiculo.Existe;
            if (!ligado)
            {
                Ocupa = 0f;
                if (_canvas != null && !_canvas.WasCollected && _canvas.activeSelf) _canvas.SetActive(false);
                return;
            }
            Construir();
            if (!_canvas.activeSelf) _canvas.SetActive(true);
            float margem = HudAvisos.Margem?.Value ?? 24f;
            _raiz.anchoredPosition = new Vector2(-24 - TelaAlt.LarguraOcupada, margem); // à esquerda do painel da carroceria, se aberto
            Ocupa = Altura + 8f;

            int vel = Mathf.RoundToInt(Mathf.Abs(Veiculo.VelocidadeKmh));
            if (vel != _velMostrada) { _velMostrada = vel; _vel.text = vel.ToString(); }

            var marcha = Veiculo.Cambio.MarchaTexto;
            if (marcha != _marchaMostrada) { _marchaMostrada = marcha; _marcha.text = marcha; }
            _marcha.color = Veiculo.Cambio.Marcha < 0 ? Theme.Aviso : Theme.Texto;
            string lim = Veiculo.Ajudas.LimitadorAtivo ? $"LIM {Veiculo.Ajudas.LimiteKmh:0}" : Veiculo.Ajudas.PilotoAutomatico ? "PILOTO" : "";
            if (_limite.text != lim) _limite.text = lim;

            float max = Veiculo.Motor.RpmMax > 100f ? Veiculo.Motor.RpmMax : 3000f;
            float f = Mathf.Clamp01(Veiculo.Motor.Rpm / max);
            _rpmEnche.anchorMax = new Vector2(f, 1);
            _rpmCor.color = !Veiculo.Motor.Ligado ? Theme.Linha : f > 0.9f ? Theme.Perigo : f > 0.75f ? Theme.Aviso : Theme.TextoDim;
            int rpm = Veiculo.Motor.Ligado ? Mathf.RoundToInt(Veiculo.Motor.Rpm / 10f) * 10 : 0;
            if (rpm != _rpmMostrada) { _rpmMostrada = rpm; _rpm.text = rpm + " rpm"; }

            // Setas no ritmo do pisca do jogo; alerta acende as duas.
            var seta = Veiculo.Luzes.Seta;
            bool pisca = Veiculo.Luzes.PiscaAceso;
            _setaE.color = pisca && (seta == Seta.Esquerda || seta == Seta.Alerta) ? Verde : Apagada;
            _setaD.color = pisca && (seta == Seta.Direita || seta == Seta.Alerta) ? Verde : Apagada;
            _farol.color = Veiculo.Luzes.Farol == Farol.Alto ? Azul : Veiculo.Luzes.Farol == Farol.Baixo ? Verde : Apagada;
            _mao.color = Veiculo.Pedais.FreioDeMaoPuxado ? Theme.Perigo : Apagada;
            _porta.color = Veiculo.Portas.AlgumaAberta ? Theme.Aviso : Apagada;

            // Parada solicitada: pulsa nos primeiros 2 s, depois fica acesa.
            bool parada = Veiculo.Cabine.ParadaSolicitada;
            if (parada && !_paradaAntes) _paradaDesde = Time.unscaledTime;
            _paradaAntes = parada;
            float t = Time.unscaledTime - _paradaDesde;
            float brilho = parada ? (t < 2f ? 0.55f + 0.45f * Mathf.Cos(t * Mathf.PI * 3f) : 1f) : 0f;
            _parada.color = parada ? Theme.Alfa(Theme.Aviso, brilho) : Apagada;

            // Ar: pressão sempre (dica de quanto tem), vermelho no alarme.
            if (Ar.Simulando)
            {
                string ar = $"AR {Ar.Pressao:0.0}";
                if (ar != _arMostrado) { _arMostrado = ar; _ar.text = ar; }
                _ar.color = Ar.Alarme ? Theme.Perigo : Theme.Alfa(Theme.TextoMudo, 0.6f);
            }
            else if (_arMostrado != "") { _arMostrado = ""; _ar.text = ""; }
        }
    }
}
