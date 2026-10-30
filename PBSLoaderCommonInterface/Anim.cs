using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Animações da UI nova, em código gerenciado (sem MonoBehaviour injetado no IL2CPP):
    // cada animação é uma função t∈[0,1] chamada a cada frame por Anim.Atualizar(), em tempo
    // não escalado (funciona com o jogo pausado).
    internal static class Anim
    {
        sealed class Tween
        {
            public float inicio, duracao;
            public Action<float> passo;
            public Func<bool> vivo;
            public object dono;
        }

        static readonly List<Tween> _ativos = new();

        // Relógio das animações: avança no máximo 1/30 s por frame. Um frame travado (troca de cena,
        // carregamento) não faz a animação pular para o fim: ela continua de onde parou.
        internal const float PassoMaximo = 1f / 30f;
        internal static float Dt => Mathf.Min(Time.unscaledDeltaTime, PassoMaximo);
        internal static float Relogio { get; private set; }

        internal static void Atualizar()
        {
            Relogio += Dt;
            float agora = Relogio;
            for (int i = _ativos.Count - 1; i >= 0; i--)
            {
                var tw = _ativos[i];
                bool fim;
                try
                {
                    if (tw.vivo != null && !tw.vivo()) { _ativos.RemoveAt(i); continue; }
                    float t = tw.duracao <= 0 ? 1f : Mathf.Clamp01((agora - tw.inicio) / tw.duracao);
                    if (t <= 0f && agora < tw.inicio) continue; // ainda no atraso
                    tw.passo(t);
                    fim = t >= 1f;
                }
                catch { fim = true; try { tw.passo(1f); } catch { } } // falhou no meio: força o estado final
                if (fim) _ativos.RemoveAt(i);
            }
        }

        // Inicia (substituindo outra animação do mesmo dono, se houver).
        internal static void Iniciar(object dono, float duracao, Action<float> passo, float atraso = 0f, Func<bool> vivo = null)
        {
            if (dono != null) _ativos.RemoveAll(x => x.dono == dono);
            var tw = new Tween { inicio = Relogio + atraso, duracao = duracao, passo = passo, vivo = vivo, dono = dono };
            _ativos.Add(tw);
            if (atraso <= 0f) { try { passo(0f); } catch { } }
        }

        // Ease-out quártico: arranca rápido e para seco, como os menus de corrida (sem o
        // assentamento longo de interfaces sci-fi). Usado em hover, entradas e marcas.
        internal static float Suave(float t) { t = Mathf.Clamp01(t); t = 1f - t; return 1f - t * t * t * t; }

        // Curvas de motion graphics: expo-out para entradas (arranque forte, chegada longa e macia),
        // back-out para o que "assenta" com um leve passo além (modais), in-out para varreduras.
        internal static float Expo(float t) => t >= 1f ? 1f : t <= 0f ? 0f : 1f - Mathf.Pow(2f, -10f * t);
        internal static float Back(float t, float s = 1.25f) { t = Mathf.Clamp01(t) - 1f; return 1f + t * t * ((s + 1f) * t + s); }
        internal static float InOut(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f; }

        static bool Vivo(UnityEngine.Object o) => o != null && !o.WasCollected;

        // Posição de repouso de cada elemento animado, guardada na primeira vez: reanimar no meio de
        // uma animação não desloca o ponto de chegada.
        static readonly Dictionary<IntPtr, Vector2> _repouso = new();

        static Vector2 Repouso(RectTransform rt)
        {
            if (!_repouso.TryGetValue(rt.Pointer, out var pos)) _repouso[rt.Pointer] = pos = rt.anchoredPosition;
            return pos;
        }

        static CanvasGroup Grupo(GameObject go)
        {
            var g = go.GetComponent<CanvasGroup>();
            return g != null ? g : go.AddComponent<CanvasGroup>();
        }

        // Entrada de tela: fade + deslize horizontal curto (as telas do DiRT entram de lado).
        internal static void EntrarTela(RectTransform rt, float deslize = 24f, float duracao = 0.26f)
        {
            var g = Grupo(rt.gameObject);
            Iniciar(rt, duracao, t =>
            {
                float e = Suave(t);
                g.alpha = e;
                var p = rt.anchoredPosition;
                rt.anchoredPosition = new Vector2(deslize * (1f - e), p.y);
            }, 0f, () => Vivo(rt));
        }

        // Modal/submenu: o fundo escurece rápido; a janela sobe 28 px e assenta com um leve passo além
        // (back-out), e a faixa vermelha do topo cresce da esquerda logo depois.
        internal static void Aparecer(GameObject go, Transform escalar = null, float duracao = 0.46f)
        {
            var g = Grupo(go);
            var janela = escalar != null ? escalar.TryCast<RectTransform>() : null;
            var pos = janela != null ? Repouso(janela) : Vector2.zero;
            var faixa = escalar != null ? escalar.Find("FaixaTopo")?.TryCast<RectTransform>() : null;
            if (faixa != null) faixa.pivot = new Vector2(0f, 1f);
            Iniciar(go, duracao, t =>
            {
                g.alpha = Expo(Mathf.Clamp01(t / 0.45f));
                if (janela != null)
                {
                    janela.localScale = Vector3.one * Mathf.LerpUnclamped(0.94f, 1f, Back(t));
                    janela.anchoredPosition = pos + new Vector2(0, -28f * (1f - Expo(t)));
                }
                if (faixa != null) faixa.localScale = new Vector3(Expo(Mathf.Clamp01((t - 0.15f) / 0.85f)), 1f, 1f);
            }, 0f, () => Vivo(go));
        }

        // Painel lateral entrando: vem de "de" px (de fora da tela) em expo-out, com fade curto.
        internal static void Deslizar(RectTransform rt, float de = -380f, float duracao = 0.5f)
        {
            var pos = Repouso(rt);
            var g = Grupo(rt.gameObject);
            Iniciar(rt, duracao, t =>
            {
                float e = Expo(t);
                g.alpha = Mathf.Clamp01(t * 4f);
                rt.anchoredPosition = pos + new Vector2(de * (1f - e), 0);
            }, 0f, () => Vivo(rt));
        }

        // Entrada coreografada de uma tela cheia: cada filho direto da raiz entra do lado em que está
        // ancorado (cabeçalho de cima, coluna esquerda da esquerda, direita da direita, rodapé de baixo,
        // centro crescendo), em sequência. Fundos (foto, vinheta, riscas) ficam parados.
        static readonly HashSet<string> Fundos = new() { "Foto", "Cinema", "Vinheta", "Listras", "GradLateral", "FundoFoto", "Modal", "ListaSuspensa" };

        // "fundoOpaco": a raiz não passa por transparência (tela de carregamento: o que estiver atrás
        // dela, como o carregamento antigo do jogo, nunca pode aparecer); só o conteúdo anima.
        internal static void Coreografia(RectTransform raiz, bool fundoOpaco = false)
        {
            if (!Vivo(raiz)) return;
            var g = Grupo(raiz.gameObject);
            if (fundoOpaco) { _ativos.RemoveAll(x => x.dono is RectTransform r && r.Pointer == raiz.Pointer); g.alpha = 1f; }
            else Iniciar(raiz, 0.2f, t => g.alpha = Expo(t), 0f, () => Vivo(raiz));
            var pos = raiz.anchoredPosition;
            raiz.anchoredPosition = new Vector2(0, pos.y); // desfaz o deslize antigo, se houver

            for (int i = 0; i < raiz.childCount; i++)
            {
                var c = raiz.GetChild(i).TryCast<RectTransform>();
                if (c == null || !c.gameObject.activeSelf || Fundos.Contains(c.name)) continue;
                Vector2 de; float atraso;
                if (c.anchorMin.y >= 0.99f) { de = new Vector2(0, 46); atraso = 0f; }              // topo
                else if (c.anchorMax.y <= 0.01f) { de = new Vector2(0, -36); atraso = 0.28f; }     // base
                else if (c.anchorMax.x <= 0.01f || (c.anchorMax.x < 0.5f && c.anchorMin.x <= 0.01f)) { de = new Vector2(-110, 0); atraso = 0.07f; } // esquerda
                else if (c.anchorMin.x >= 0.99f || (c.anchorMin.x > 0.5f && c.anchorMax.x >= 0.99f)) { de = new Vector2(110, 0); atraso = 0.21f; } // direita
                else { de = Vector2.zero; atraso = 0.14f; }                                      // centro
                Entrar(c, de, atraso, 0.62f);
            }
        }

        static void Entrar(RectTransform rt, Vector2 de, float atraso, float duracao)
        {
            var pos = Repouso(rt);
            var g = Grupo(rt.gameObject);
            bool escala = de == Vector2.zero;
            g.alpha = 0f;
            rt.anchoredPosition = pos + de;
            if (escala) rt.localScale = Vector3.one * 0.96f;
            Iniciar(rt, duracao, t =>
            {
                float e = Expo(t);
                g.alpha = Mathf.Clamp01(t * 3f);
                rt.anchoredPosition = pos + de * (1f - e);
                if (escala) rt.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
            }, atraso, () => Vivo(rt));
        }

        // Gira um elemento até "graus" (chevron do acordeão).
        internal static void Girar(Transform t, float graus, float duracao = 0.18f)
        {
            float de = t.localEulerAngles.z;
            if (de > 180f) de -= 360f;
            Iniciar(t, duracao, p => t.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(de, graus, Suave(p))), 0f, () => Vivo(t));
        }

        // ---------- transição entre telas cheias (cobrir e revelar) ----------
        // Ao navegar, um bloco escuro em diagonal (com a borda vermelha na frente e um traço branco)
        // entra pela direita e cobre a tela; a troca de cena acontece por baixo, com a marca do jogo no
        // centro. Quando a tela nova aparece, o bloco sai pela esquerda revelando-a. Sem cobertura
        // prévia (primeira tela), só a passagem rápida da faixa.
        enum EstadoTransicao { Livre, Cobrindo, Coberto, Revelando }
        static EstadoTransicao _estado;
        static RectTransform _transicao, _bloco, _marca;
        static CanvasGroup _grupoMarca;
        static float _cobertoDesde;
        static bool _revelarDepois, _revelarPendente;
        static Action _aoCobrir;
        static readonly List<Action> _aoRevelar = new();
        static int _framesEstaveis;
        static float _pedidoRevelar;
        // Marca "PROTON BUS": progresso da entrada dela (no relógio das animações, que não salta depois
        // de uma travada) e a barra vermelha que pulsa enquanto a tela espera.
        static float _marcaT;
        static RectTransform _barraMarca;
        const float LarguraBloco = 3400f, Fora = 2600f;

        static void CriarTransicao()
        {
            if (_transicao != null && !_transicao.WasCollected) return;
            var go = new GameObject("PBSLoaderCommonInterface_Transicao") { layer = 5 };
            UnityEngine.Object.DontDestroyOnLoad(go);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 950; // por cima das telas e do painel de mods; abaixo do carregamento
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            _transicao = go.GetComponent<RectTransform>();

            // Bloco: corpo escuro largo + borda vermelha na frente (esquerda, que é quem entra
            // primeiro vindo da direita) + traço branco fino atrás dela. Tudo inclinado como "/".
            _bloco = Kit.Novo("Bloco", _transicao);
            Kit.Ancorar(_bloco, new Vector2(0.5f, 0.5f), new Vector2(Fora, 0), new Vector2(LarguraBloco, 2400f));
            _bloco.localEulerAngles = new Vector3(0, 0, -14f);
            Kit.Fundo(_bloco, null, Theme.PaginaFundo).raycastTarget = true; // bloqueia cliques durante a troca
            var borda = Kit.Ancorar(Kit.Novo("Borda", _bloco), new Vector2(0f, 0.5f), new Vector2(0, 0), new Vector2(150f, 2400f));
            borda.pivot = new Vector2(1f, 0.5f);
            Kit.Fundo(borda, null, Theme.Acento).raycastTarget = false;
            var traco = Kit.Ancorar(Kit.Novo("Traco", _bloco), new Vector2(0f, 0.5f), new Vector2(-190f, 0), new Vector2(22f, 2400f));
            traco.pivot = new Vector2(1f, 0.5f);
            Kit.Fundo(traco, null, Color.white).raycastTarget = false;

            // Marca no centro enquanto coberto: barra vermelha inclinada + nome.
            _marca = Kit.Ancorar(Kit.Novo("Marca", _transicao), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 60));
            var hl = Kit.Linha(_marca, 14, 0, TextAnchor.MiddleCenter);
            _ = hl;
            var m = Kit.Novo("Barra", _marca);
            Kit.Tam(m, 12, 44);
            var mb = Kit.Ancorar(Kit.Novo("B", m), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7, 46));
            mb.localEulerAngles = new Vector3(0, 0, -14f);
            Kit.Fundo(mb, null, Theme.Acento).raycastTarget = false;
            _barraMarca = mb;
            var t = Kit.Texto(_marca, "PROTON BUS", 30, Theme.Texto, true);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            _grupoMarca = _marca.gameObject.AddComponent<CanvasGroup>();
            _grupoMarca.alpha = 0f;
            _grupoMarca.blocksRaycasts = false;
            go.SetActive(false);
        }

        // Cobre a tela (clique que leva a outra tela cheia). "aoCobrir" roda com a tela já coberta: é
        // onde o clique original (que troca de cena e trava o jogo) acontece, para o travamento ficar
        // escondido em vez de cortar a animação.
        internal static void Cobrir(Action aoCobrir = null, float duracao = 0.42f)
        {
            CriarTransicao();
            if (_estado == EstadoTransicao.Coberto) { Rodar(aoCobrir); return; }
            if (_estado == EstadoTransicao.Cobrindo) { _aoCobrir += aoCobrir; return; }
            _aoCobrir = aoCobrir;
            _transicao.gameObject.SetActive(true);
            _estado = EstadoTransicao.Cobrindo;
            _marcaT = 0f;
            _grupoMarca.alpha = 0f;
            _revelarDepois = false;
            float de = _bloco.anchoredPosition.x > 0 ? _bloco.anchoredPosition.x : Fora;
            Iniciar(_transicao, duracao, t =>
            {
                _bloco.anchoredPosition = new Vector2(Mathf.Lerp(de, 0f, InOut(t)), 0);
                if (t < 1f) return;
                _estado = EstadoTransicao.Coberto;
                _cobertoDesde = Time.unscaledTime;
                var a = _aoCobrir; _aoCobrir = null;
                if (a != null) Desempenho.Marco("clique");
                Rodar(a);
                if (_revelarDepois) { _revelarDepois = false; PedirRevelar(); }
            }, 0f, () => Vivo(_transicao));
        }

        static void Rodar(Action a)
        {
            if (a == null) return;
            try { a(); } catch (Exception e) { MelonLoader.MelonLogger.Warning("[Transição] " + e); }
        }

        // Roda "a" quando a tela nova for revelada (na hora, se não houver transição em curso).
        // A coreografia e a entrada das linhas esperam a revelação: com a tela coberta, elas
        // aconteceriam por baixo do bloco.
        internal static void QuandoRevelar(Action a)
        {
            if (_estado == EstadoTransicao.Cobrindo || _estado == EstadoTransicao.Coberto) _aoRevelar.Add(a);
            else Rodar(a);
        }

        static void PedirRevelar()
        {
            _revelarPendente = true;
            _framesEstaveis = 0;
            _pedidoRevelar = Time.unscaledTime;
        }

        // Pede a revelação da tela nova. Coberta, ela espera a cena ficar estável (2 frames rápidos
        // seguidos, ou 0,6 s) para a animação não sair picotada pelos primeiros frames da cena nova.
        internal static void Revelar()
        {
            if (_estado == EstadoTransicao.Cobrindo) { _revelarDepois = true; return; }
            if (_estado == EstadoTransicao.Coberto) { PedirRevelar(); return; }
            Passagem();
        }

        static void RevelarAgora(float duracao = 0.6f)
        {
            _revelarPendente = false;
            _estado = EstadoTransicao.Revelando;
            var fila = new List<Action>(_aoRevelar);
            _aoRevelar.Clear();
            Desempenho.Marco("revelada");
            foreach (var a in fila) Rodar(a);
            // A marca (se chegou a aparecer) sai junto com o bloco, um pouco mais devagar que ele.
            float alfaMarca = _grupoMarca.alpha, xMarca = _marca.anchoredPosition.x;
            Iniciar(_transicao, duracao, t =>
            {
                float x = Mathf.Lerp(0f, -Fora - 600f, InOut(t));
                _bloco.anchoredPosition = new Vector2(x, 0);
                _marca.anchoredPosition = new Vector2(xMarca + x * 0.55f, 0);
                _grupoMarca.alpha = alfaMarca * (1f - Mathf.Clamp01(t * 1.8f));
                if (t < 1f) return;
                _marcaT = 0f;
                _marca.anchoredPosition = Vector2.zero;
                _estado = EstadoTransicao.Livre;
                _bloco.anchoredPosition = new Vector2(Fora, 0);
                _transicao.gameObject.SetActive(false);
            }, 0f, () => Vivo(_transicao));
        }

        // Sem cobertura antes (primeira tela, F8): só a faixa vermelha atravessa rápido.
        static void Passagem(float duracao = 0.7f)
        {
            CriarTransicao();
            if (_estado != EstadoTransicao.Livre) return;
            _transicao.gameObject.SetActive(true);
            _estado = EstadoTransicao.Revelando;
            _grupoMarca.alpha = 0f;
            // O bloco passa inteiro: entra pela direita e sai pela esquerda sem parar.
            Iniciar(_transicao, duracao, t =>
            {
                _bloco.anchoredPosition = new Vector2(Mathf.Lerp(Fora, -Fora - 600f, InOut(t)), 0);
                if (t < 1f) return;
                _estado = EstadoTransicao.Livre;
                _bloco.anchoredPosition = new Vector2(Fora, 0);
                _transicao.gameObject.SetActive(false);
            }, 0f, () => Vivo(_transicao));
        }

        // A cada frame: a marca aparece se a tela ficar coberta por mais tempo (carga da cena), e a
        // cobertura nunca fica presa (revela sozinha depois de 4 s).
        internal static void AtualizarTransicao()
        {
            if (_estado != EstadoTransicao.Coberto || _grupoMarca == null) return;
            float t = Time.unscaledTime - _cobertoDesde;
            // A marca só entra se a troca estiver demorando de fato (a cena ainda carregando): se a
            // revelação já foi pedida, ela não aparece mais; se já estava na tela, fica até sair com o bloco.
            bool mostrar = t > 0.35f && (!_revelarPendente || _marcaT > 0f);
            _marcaT = Mathf.MoveTowards(_marcaT, mostrar ? 1f : 0f, Dt / 0.35f);
            float e = Expo(_marcaT);
            _grupoMarca.alpha = e;
            _marca.anchoredPosition = new Vector2(48f * (1f - e), 0);
            if (_barraMarca != null && _marcaT > 0f)
                _barraMarca.localScale = new Vector3(1f, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Relogio * 3.4f)), 1f);
            if (_revelarPendente)
            {
                _framesEstaveis = Time.unscaledDeltaTime < 0.05f ? _framesEstaveis + 1 : 0;
                if (_framesEstaveis >= 2 || Time.unscaledTime - _pedidoRevelar > 0.6f) RevelarAgora();
            }
            else if (t > 4f) RevelarAgora(); // nenhuma tela nova apareceu: não deixa a tela presa
        }

        internal static bool Cobrindo => _estado == EstadoTransicao.Cobrindo || _estado == EstadoTransicao.Coberto;

        // Linha de lista entrando: o conteúdo desliza da esquerda dentro da própria linha (pelo recuo
        // do layout dela, que o layout respeita) com fade. A linha em si não sai do lugar.
        internal static void EntrarLinha(GameObject item, float atraso, float dx = 44f, float duracao = 0.55f)
        {
            if (!Vivo(item)) return; // fila de revelação de uma tela que já foi destruída
            var hl = item.GetComponent<HorizontalLayoutGroup>();
            var g = Grupo(item);
            if (hl == null) { g.alpha = 1f; return; }
            var pad = hl.padding;
            int l = pad.left, r = pad.right, c = pad.top, bx = pad.bottom;
            g.alpha = 0f;
            hl.padding = Kit.Margens(l + (int)dx, r, c, bx);
            Iniciar(item, duracao, t =>
            {
                float e = Expo(t);
                g.alpha = Mathf.Clamp01(t * 2.2f);
                hl.padding = Kit.Margens(l + Mathf.RoundToInt(dx * (1f - e)), r, c, bx);
            }, atraso, () => Vivo(item));
        }

        // Várias linhas, uma depois da outra (as que passam do limite aparecem direto).
        internal static void EntrarLinhas(IList<GameObject> itens, float atrasoBase, float intervalo = 0.04f, int maximo = 18)
        {
            for (int i = 0; i < itens.Count; i++)
            {
                if (i >= maximo) { if (Vivo(itens[i])) Grupo(itens[i]).alpha = 1f; continue; }
                EntrarLinha(itens[i], atrasoBase + i * intervalo);
            }
        }

        // Itens em cascata: fade e escala de 96% a 100%, um depois do outro. Itens dentro de layout
        // groups não podem mudar de posição: o layout é dono dela (e, logo depois de criados, ela
        // nem foi calculada ainda). Escala e alfa o layout ignora. "dx" fica só na assinatura antiga.
        internal static void Cascata(IList<GameObject> itens, float intervalo = 0.035f, float duracao = 0.42f, int maximo = 20, float dx = 0f)
        {
            for (int i = 0; i < itens.Count; i++)
            {
                var go = itens[i];
                var g = Grupo(go);
                var t0 = go.transform;
                if (i >= maximo) { g.alpha = 1f; continue; }
                g.alpha = 0f;
                t0.localScale = Vector3.one * 0.96f;
                Iniciar(go, duracao, t =>
                {
                    float e = Expo(t);
                    g.alpha = Mathf.Clamp01(t * 2.5f);
                    t0.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
                }, i * intervalo, () => Vivo(go));
            }
        }

        // Título surgindo logo depois da tela (fade com atraso curto). Só mexe na cor: o texto
        // está dentro de layout groups, que sobrescreveriam qualquer deslocamento de posição.
        // O alfa original fica guardado por objeto: se a tela reabrir no meio da animação,
        // a cor capturada já estaria esmaecida e o título ficaria apagado de vez.
        static readonly Dictionary<IntPtr, float> _alfaOriginal = new();

        internal static void Revelar(Graphic g, float atraso = 0.12f, float duracao = 0.4f)
        {
            if (g == null) return;
            if (!_alfaOriginal.TryGetValue(g.Pointer, out var alfa)) _alfaOriginal[g.Pointer] = alfa = g.color.a;
            g.color = Theme.Alfa(g.color, 0f);
            Iniciar(g, duracao, t => g.color = Theme.Alfa(g.color, alfa * Suave(t)), atraso, () => Vivo(g));
        }
    }
}
