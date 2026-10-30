using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Uma tela redesenhada. Ela substitui visualmente um Canvas original do jogo:
    //  - o original é escondido por um CanvasGroup (scripts continuam rodando);
    //  - cada controle novo é vinculado a um controle original (clique, estado, texto, visibilidade);
    //  - se o jogo ativar no Canvas original algo que esta tela não cobre (popup desconhecido),
    //    a UI original volta a aparecer naquele momento, para nada ficar invisível.
    internal abstract class Tela
    {
        protected Transform raizOrig;
        protected RectTransform raiz;
        readonly List<Action> _sync = new();
        readonly Dictionary<string, Transform> _cache = new();
        readonly Dictionary<string, float> _tentarDeNovo = new();
        HashSet<string> _cobertos;
        float _fotografarAte;

        // Raiz do Canvas original. Padrão: root da cena com esse nome (e esse filho).
        protected virtual string NomeCanvas => null;
        protected virtual string FilhoObrigatorio => null;
        protected virtual Transform AcharRaiz() => Orig.Raiz(NomeCanvas, FilhoObrigatorio);

        // Filhos diretos do Canvas original que esta tela desenha (ou descarta de propósito).
        // null = "fotografia": vale o que estiver ativo no primeiro segundo + Tratados().
        protected virtual string[] Cobertos => null;
        // Objetos que a tela trata mesmo estando inativos ao montar (modais, painel de carregando).
        protected virtual IEnumerable<GameObject> Tratados() { yield break; }

        protected abstract void Construir();

        // Identidade da tela (registro, auditoria, log). Telas genéricas com várias instâncias sobrescrevem.
        internal virtual string Nome => GetType().Name;
        internal RectTransform RaizNova => raiz;
        internal Transform RaizOriginal => raizOrig;

        internal bool Detectar()
        {
            raizOrig = AcharRaiz();
            return raizOrig != null;
        }

        internal void Montar(RectTransform pai)
        {
            var canvas = raizOrig.GetComponent<Canvas>();
            if (canvas != null && !canvas.enabled) canvas.enabled = true; // quem esconde é o CanvasGroup
            raiz = Kit.Encher(Kit.Novo(Nome, pai));
            // Montada inativa: criar centenas de componentes sob um objeto inativo é bem mais barato (sem
            // OnEnable nem registro no canvas a cada um), e a primeira exibição passa por AoAparecer, que
            // pede a revelação da transição e roda a coreografia. Ativa desde o nascimento, a tela nova
            // nunca "aparecia": a cobertura só saía pelo limite de 4 s.
            raiz.gameObject.SetActive(false);
            if (OrdemCanvas.HasValue)
            {
                var cv = raiz.gameObject.AddComponent<Canvas>();
                cv.overrideSorting = true;
                cv.sortingOrder = OrdemCanvas.Value;
                raiz.gameObject.AddComponent<GraphicRaycaster>();
            }
            Construir();

            var lista = Cobertos;
            _cobertos = lista != null ? new HashSet<string>(lista) : new HashSet<string>();
            if (lista == null)
            {
                _fotografarAte = Time.unscaledTime + 1f;
                Fotografar();
            }
            foreach (var go in Tratados())
            {
                var filho = FilhoDiretoDaRaiz(go != null ? go.transform : null);
                if (filho != null) _cobertos.Add(filho.name);
            }
        }

        void Fotografar()
        {
            for (int i = 0; i < raizOrig.childCount; i++)
            {
                var f = raizOrig.GetChild(i);
                if (f.gameObject.activeSelf) _cobertos.Add(f.name);
            }
        }

        Transform FilhoDiretoDaRaiz(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.parent == raizOrig) return t;
            return null;
        }

        internal bool Viva
        {
            get
            {
                try { return raizOrig != null && !raizOrig.WasCollected && raizOrig.gameObject != null; }
                catch { return false; } // original destruído (troca de cena)
            }
        }

        internal string ElementoNaoCoberto()
        {
            if (Time.unscaledTime < _fotografarAte) { Fotografar(); return null; }
            for (int i = 0; i < raizOrig.childCount; i++)
            {
                var f = raizOrig.GetChild(i);
                if (f.gameObject.activeSelf && !_cobertos.Contains(f.name)) return f.name;
            }
            return null;
        }

        internal bool PrecisaOriginal() => ElementoNaoCoberto() != null;

        // O original é escondido por um CanvasGroup (alpha 0, sem cliques) em vez de Canvas.enabled:
        // o jogo não mexe nesse grupo, então não há disputa frame a frame, e ele também esconde
        // Canvas aninhados dentro do original.
        CanvasGroup _grupo;
        bool _grupoCriado;

        internal void Mostrar(bool nova)
        {
            if (_grupo == null || _grupo.WasCollected)
            {
                _grupo = raizOrig.GetComponent<CanvasGroup>();
                if (_grupo == null) { _grupo = raizOrig.gameObject.AddComponent<CanvasGroup>(); _grupoCriado = true; }
            }
            float alfa = nova ? 0f : 1f;
            if (_grupo.alpha != alfa) _grupo.alpha = alfa;
            if (_grupo.blocksRaycasts == nova) _grupo.blocksRaycasts = !nova;
            if (_grupo.interactable == nova) _grupo.interactable = !nova;
            // A versão nova acompanha o Canvas original: se o jogo o desativa (ex.: overlay do ALT
            // solto), a nova some junto. O CanvasGroup continua transparente para não piscar ao reabrir.
            bool visivel = nova && (SempreVisivel || raizOrig.gameObject.activeInHierarchy);
            if (raiz.gameObject.activeSelf != visivel)
            {
                raiz.gameObject.SetActive(visivel);
                if (visivel) AoAparecer();
            }
        }

        // Entrada: fade + deslize lateral curto, e os títulos fixos surgem logo depois.
        void AoAparecer()
        {
            if (TransicaoCheia)
            {
                // A coreografia e as linhas esperam a revelação (que espera a cena estabilizar).
                Anim.Revelar();
                Anim.QuandoRevelar(() => { _apareceuEm = Time.unscaledTime; Anim.Coreografia(raiz); });
            }
            else { _apareceuEm = Time.unscaledTime; Anim.EntrarTela(raiz); }
            // Janela de painel (TelaPainel): sobe e assenta como os modais.
            var modal = raiz.Find("Modal");
            if (modal != null && modal.gameObject.activeSelf) Anim.Aparecer(modal.gameObject, modal.Find("Janela"));
            foreach (var t in raiz.GetComponentsInChildren<Text>(false))
                if (t.name == "Titulo") Anim.Revelar(t);
            // Itens de lista visíveis entram em cascata (ações do menu principal, listas de seleção).
            var itens = new List<GameObject>();
            foreach (var b in raiz.GetComponentsInChildren<Button>(false))
                if (b.name == "Item" && itens.Count < 16) itens.Add(b.gameObject);
            if (itens.Count > 0)
            {
                Anim.QuandoRevelar(() => Anim.EntrarLinhas(itens, AtrasoLinhas));
            }
        }

        // Telas que ficam disponíveis mesmo com o Canvas original inativo (ex.: a sidebar da partida,
        // que não depende mais do ALT para aparecer).
        protected virtual bool SempreVisivel => false;

        // Linhas de lista começam a entrar depois que a coluna delas chegou (coreografia), senão a
        // entrada delas acontece com a coluna ainda invisível.
        float _apareceuEm = -10f;
        protected float AtrasoLinhas => Mathf.Max(0f, 0.3f - (Time.unscaledTime - _apareceuEm));

        // Botão que leva a outra tela cheia: cobre a tela no clique (a nova revela ao aparecer).
        protected static Kit.Botao Navega(Kit.Botao b)
        {
            b.navega = true;
            return b;
        }

        // Telas cheias (menu, seleções, configurações): entram com a varredura diagonal e coreografia.
        protected virtual bool TransicaoCheia => false;

        // Ordem própria de desenho/cliques (Canvas aninhado com overrideSorting). Necessário quando
        // outro Canvas do jogo, com imagens invisíveis que recebem cliques, fica por cima do nosso.
        protected virtual int? OrdemCanvas => null;

        internal void Atualizar()
        {
            foreach (var a in _sync) a();
        }

        internal void Destruir()
        {
            if (_grupo != null && !_grupo.WasCollected)
            {
                if (_grupoCriado) UnityEngine.Object.Destroy(_grupo);
                else { _grupo.alpha = 1f; _grupo.blocksRaycasts = true; _grupo.interactable = true; }
            }
            if (raiz != null && !raiz.WasCollected) UnityEngine.Object.Destroy(raiz.gameObject);
        }

        // ---------- localizar originais ----------

        protected Transform O(string caminho) => Cache("p:" + caminho, () => raizOrig.Find(caminho));

        protected Func<Transform> P(string caminho) => caminho == null ? null : () => O(caminho);

        // Botão original cujo onClick chama "metodo" (com esse argumento int, se informado).
        protected Func<Transform> Chama(string metodo, int? arg = null) =>
            () => Cache($"m:{metodo}:{arg}", () => Orig.BotaoQueChama(raizOrig, metodo, arg));

        protected static Func<Transform> De(Func<Component> c) => () =>
        {
            var x = c();
            return x != null && !x.WasCollected ? x.transform : null;
        };

        protected static Func<Transform> DeGo(Func<GameObject> g) => () =>
        {
            var x = g();
            return x != null && !x.WasCollected ? x.transform : null;
        };

        // Cache com nova tentativa a cada 1 s quando não achou (evita varrer a cena todo frame).
        Transform Cache(string chave, Func<Transform> achar)
        {
            if (_cache.TryGetValue(chave, out var t) && t != null && !t.WasCollected) return t;
            if (_tentarDeNovo.TryGetValue(chave, out var quando) && Time.unscaledTime < quando) return null;
            t = achar();
            _cache[chave] = t;
            if (t == null) _tentarDeNovo[chave] = Time.unscaledTime + 1f;
            return t;
        }

        protected void Sync(Action a) => _sync.Add(a);

        bool AtivoAte(Transform o, Func<Transform> ate) => o != null && Orig.Ativo(o, ate?.Invoke() ?? raizOrig);

        // ---------- vínculos ----------

        // Botão novo -> botão (ou EventTrigger) original. Rótulo vem do original (traduzido pelo jogo).
        // "direto": chamado quando o original não é encontrado (ex.: método do controlador).
        protected Kit.Botao BotaoF(Transform pai, Func<Transform> alvo, string icone, Estilo estilo, string rotuloPadrao,
            Func<Transform> rotulo = null, Func<Transform> ate = null, Action direto = null,
            float altura = 48, int tamFonte = 17, TextAnchor alin = TextAnchor.MiddleLeft)
        {
            var b = Kit.NovoBotao(pai, rotuloPadrao, icone, estilo, null, altura, tamFonte, alin);
            return Vincular(b, alvo, rotulo, ate, direto);
        }

        // Liga um botão já criado a um original: clique, visibilidade, interactable e rótulo.
        protected Kit.Botao Vincular(Kit.Botao b, Func<Transform> alvo, Func<Transform> rotulo = null,
            Func<Transform> ate = null, Action direto = null)
        {
            b.btn.onClick.AddListener((UnityEngine.Events.UnityAction)(Action)(() =>
            {
                void Clicar()
                {
                    var o = alvo?.Invoke();
                    if (o != null) Orig.Clicar(o);
                    else direto?.Invoke();
                }
                if (b.navega) Anim.Cobrir(Clicar); // a troca de cena (e o travamento) fica escondida
                else Clicar();
            }));
            Sync(() =>
            {
                var o = alvo?.Invoke();
                bool vis = o != null ? AtivoAte(o, ate) : direto != null;
                b.Visivel(vis);
                if (!vis || o == null) return;
                b.Interagivel(Orig.Clicavel(o));
                if (b.rotulo != null && rotulo != null)
                {
                    var s = Orig.Limpo(Orig.Texto(rotulo()));
                    if (!string.IsNullOrEmpty(s)) b.Rotulo(s);
                }
            });
            return b;
        }

        // Versão por caminho. caminhoRotulo: null = não espelha; "" = texto do próprio botão.
        protected Kit.Botao Botao(Transform pai, string caminho, string icone, Estilo estilo, string rotuloPadrao,
            string caminhoRotulo = "", string ate = null, float altura = 48, int tamFonte = 17, TextAnchor alin = TextAnchor.MiddleLeft)
        {
            var alvo = P(caminho);
            var rot = caminhoRotulo == null ? null : caminhoRotulo == "" ? alvo : P(caminhoRotulo);
            return BotaoF(pai, alvo, icone, estilo, rotuloPadrao, rot, P(ate), null, altura, tamFonte, alin);
        }

        // "rotuloFixo": usa este texto em vez do rótulo do jogo (quando o original é ruim, ex.: "Info...").
        protected Kit.Chave ChaveF(Transform pai, Func<Transform> alvo, Func<Transform> ate = null, bool moldura = false, string rotuloFixo = null)
        {
            var c = Kit.NovaChave(pai, "", () =>
            {
                var tg = alvo()?.GetComponent<Toggle>();
                if (tg != null && tg.interactable) tg.isOn = !tg.isOn; // dispara os onValueChanged do jogo
            }, moldura);
            Sync(() =>
            {
                var o = alvo();
                bool vis = AtivoAte(o, ate);
                c.Visivel(vis);
                if (!vis) return;
                var tg = o.GetComponent<Toggle>();
                // tg.interactable (a flag do próprio toggle): IsInteractable() consideraria o CanvasGroup
                // que o mod põe no original para escondê-lo e acusaria tudo como bloqueado.
                if (tg != null) { c.Definir(tg.isOn); c.Interagivel(tg.interactable); }
                if (rotuloFixo != null) { c.Rotulo(rotuloFixo); return; }
                var lbl = o.Find("Label");
                var s = Orig.Limpo(lbl != null ? Orig.Texto(lbl) : Orig.Texto(o));
                if (!string.IsNullOrEmpty(s)) c.Rotulo(s);
            });
            return c;
        }

        protected Kit.Chave Chave(Transform pai, string caminho, string ate = null, bool moldura = false, string rotuloFixo = null) =>
            ChaveF(pai, P(caminho), P(ate), moldura, rotuloFixo);

        // Espelha um Text original. "alvoVisivel": o que esconder junto quando o original some/esvazia.
        protected Text TextoF(Transform pai, Func<Transform> alvo, int tam, Color cor, bool forte = false,
            GameObject alvoVisivel = null, Func<Transform> ate = null, Func<string, string> formato = null)
        {
            var t = Kit.Texto(pai, "", tam, cor, forte);
            // Texto espelhado não entra na animação de entrada dos títulos (o jogo o reescreve);
            // se for título, segue em maiúsculas como os fixos.
            bool titulo = t.name == "Titulo";
            t.gameObject.name = titulo ? "TituloEspelho" : "TextoEspelho";
            var esconder = alvoVisivel ?? t.gameObject;
            Sync(() =>
            {
                var o = alvo();
                var s = AtivoAte(o, ate) ? Orig.Texto(o) : null;
                if (formato != null && s != null) s = formato(s);
                if (titulo && s != null) s = s.ToUpperInvariant();
                bool vis = !string.IsNullOrWhiteSpace(s);
                if (esconder.activeSelf != vis) esconder.SetActive(vis);
                if (vis && t.text != s) t.text = s;
            });
            return t;
        }

        protected Text Texto(Transform pai, string caminho, int tam, Color cor, bool forte = false, GameObject alvoVisivel = null, string ate = null) =>
            TextoF(pai, P(caminho), tam, cor, forte, alvoVisivel, P(ate));

        protected void VisivelF(GameObject go, Func<Transform> alvo, Func<Transform> ate = null)
        {
            Sync(() =>
            {
                bool vis = AtivoAte(alvo(), ate);
                if (go.activeSelf == vis) return;
                go.SetActive(vis);
                // Modais surgem com fade + leve escala da janela.
                if (vis && go.name == "Modal") Anim.Aparecer(go, go.transform.Find("Janela"));
            });
        }

        protected void Visivel(GameObject go, string caminho, string ate = null) => VisivelF(go, P(caminho), P(ate));

        // Fundo de tela: espelha a textura de um RawImage original (a foto que o jogo carrega).
        protected RawImage FundoEspelhado(Transform pai, string caminho)
        {
            var rt = Kit.Encher(Kit.Novo("FundoFoto", pai));
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = Color.white;
            Sync(() =>
            {
                var o = O(caminho)?.GetComponent<RawImage>();
                var tex = o != null ? o.texture : null;
                if (raw.texture != tex) raw.texture = tex;
                raw.enabled = tex != null;
            });
            return raw;
        }

        // ---------- campo, slider, segmento, seletor ----------

        // Campo de texto novo <-> InputField original (texto nos dois sentidos; confirmar repassa o onEndEdit).
        protected InputField CampoF(Transform pai, Func<InputField> orig, float largura, Func<Transform> ate = null)
        {
            var campo = Kit.Campo(pai, "", s =>
            {
                var o = orig();
                if (o != null && o.text != s) o.text = s; // dispara o onValueChanged do jogo
            }, s => orig()?.onEndEdit.Invoke(s), null);
            Kit.Tam(campo, largura, 40);
            Sync(() =>
            {
                var o = orig();
                bool vis = o != null && AtivoAte(o.transform, ate);
                if (campo.gameObject.activeSelf != vis) campo.gameObject.SetActive(vis);
                if (!vis) return;
                if (!campo.isFocused && campo.text != o.text) campo.text = o.text;
            });
            return campo;
        }

        // Slider novo <-> Slider original (valor normalizado; respeita wholeNumbers do original).
        protected Slider SliderF(Transform pai, Func<Slider> orig, float largura, Func<Transform> ate = null)
        {
            float ultimoToque = -10f;
            bool sincronizando = false;
            var s = Kit.NovoSlider(pai, largura, v =>
            {
                if (sincronizando) return;
                var o = orig();
                if (o == null || !o.interactable) return;
                ultimoToque = Time.unscaledTime;
                float alvo = Mathf.Lerp(o.minValue, o.maxValue, v);
                if (o.wholeNumbers) alvo = Mathf.Round(alvo);
                if (o.value != alvo) o.value = alvo; // dispara o onValueChanged do jogo
            });
            Sync(() =>
            {
                var o = orig();
                bool vis = o != null && AtivoAte(o.transform, ate);
                if (s.gameObject.activeSelf != vis) s.gameObject.SetActive(vis);
                if (!vis || Time.unscaledTime - ultimoToque < 0.3f) return; // não briga com o arraste
                float v = o.normalizedValue;
                if (Mathf.Abs(s.value - v) > 0.001f) { sincronizando = true; s.value = v; sincronizando = false; }
            });
            return s;
        }

        // Toggle de um ToggleGroup (opção exclusiva) como botão de segmento.
        protected Kit.Botao Segmento(Transform pai, Func<Toggle> orig, string rotuloPadrao, Func<Transform> ate = null)
        {
            var b = Kit.NovoBotao(pai, rotuloPadrao, null, Estilo.Secundario, () => Escolher(orig()), 38, 14, TextAnchor.MiddleCenter);
            Sync(() =>
            {
                var tg = orig();
                bool vis = tg != null && AtivoAte(tg.transform, ate);
                b.Visivel(vis);
                if (!vis) return;
                b.Selecionado(tg.isOn);
                b.Interagivel(tg.interactable); // o jogo bloqueia algumas opções (ex.: dependem de outra)
                var lbl = tg.transform.Find("Label");
                var txt = Orig.Limpo(lbl != null ? Orig.Texto(lbl) : null);
                if (!string.IsNullOrEmpty(txt)) b.Rotulo(txt);
            });
            return b;
        }

        // Escolhe uma opção de um grupo exclusivo (ToggleGroup) do original. O próprio grupo só desliga
        // as outras opções quando o objeto dele está ativo, e as configurações usam controles de abas
        // que o jogo deixa inativas: sem isto, duas opções ficavam ligadas e não dava mais para voltar.
        // Reproduz a ordem do Unity: desliga as outras e liga a escolhida por último, para o último
        // onValueChanged que o jogo recebe ser o da opção escolhida.
        protected void Escolher(Toggle tg)
        {
            if (tg == null || !tg.interactable) return;
            var g = tg.group;
            if (g != null)
                foreach (var outro in raizOrig.GetComponentsInChildren<Toggle>(true))
                    if (outro != tg && outro.group == g && outro.isOn) outro.isOn = false;
            if (!tg.isOn) tg.isOn = true;
        }

        // Dropdown original como seletor: botão com o valor atual e lista suspensa ao clicar.
        protected Kit.Botao SeletorF(Transform pai, Func<Dropdown> orig, float largura, Func<Transform> ate = null)
        {
            Kit.Botao b = null;
            b = Kit.NovoBotao(pai, "—", Ico.Abaixo, Estilo.Secundario, () => AbrirLista(b, orig), 40);
            b.visual.som = Som.Efeito.Lista;
            // Ícone à direita, como num select. O texto é o valor escolhido (dado, não comando):
            // caixa normal, peso normal, tamanho de corpo.
            b.icone.transform.SetAsLastSibling();
            b.maiusculas = false;
            b.rotulo.fontStyle = FontStyle.Normal;
            b.rotulo.fontSize = 15;
            Kit.Tam(b.btn, largura, 40);
            Sync(() =>
            {
                var dd = orig();
                bool vis = dd != null && AtivoAte(dd.transform, ate);
                b.Visivel(vis);
                if (!vis) return;
                b.Interagivel(dd.interactable);
                var cap = dd.captionText != null ? dd.captionText.text : null;
                if (!string.IsNullOrEmpty(cap)) b.Rotulo(cap);
            });
            return b;
        }

        GameObject _lista;

        void AbrirLista(Kit.Botao origem, Func<Dropdown> orig)
        {
            var dd = orig();
            if (dd == null || !dd.interactable) return;
            FecharLista();

            // Bloqueador de tela inteira: clicar fora fecha.
            var bloq = Kit.Encher(Kit.Novo("ListaSuspensa", raiz));
            Kit.Fundo(bloq, null, Theme.Alfa(Color.black, 0.01f)).raycastTarget = true;
            bloq.gameObject.AddComponent<Button>().onClick.AddListener((UnityEngine.Events.UnityAction)(Action)FecharLista);
            _lista = bloq.gameObject;

            var brt = origem.go.GetComponent<RectTransform>();
            float larg = Mathf.Max(brt.rect.width, 220);
            var ops = dd.options;
            float alt = Mathf.Min(ops.Count * 40 + 16, 360);

            var card = Kit.Novo("Opcoes", bloq);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0, 1);
            card.sizeDelta = new Vector2(larg, alt);
            Kit.Fundo(card, Theme.Solido, Theme.Menu);
            Kit.Contorno(card, Theme.LinhaForte);
            var col = Kit.Coluna(card, 0);
            Kit.Pad(col, 6, 0, 8, 8);
            var conteudo = Kit.Lista(card, out var scroll, 2);
            for (int i = 0; i < ops.Count; i++)
            {
                int idx = i;
                var item = Kit.NovoBotao(conteudo, ops[i].text, null, Estilo.Fantasma, () =>
                {
                    var d = orig();
                    if (d != null && d.value != idx) d.value = idx; // dispara o onValueChanged do jogo
                    FecharLista();
                }, 36, 14);
                item.Selecionado(i == dd.value);
            }

            // Abaixo do botão; se não couber, acima.
            float baseBotao = brt.position.y - brt.rect.height * brt.lossyScale.y * brt.pivot.y;
            float esquerda = brt.position.x - brt.rect.width * brt.lossyScale.x * brt.pivot.x;
            float alturaPx = alt * card.lossyScale.y;
            float topo = baseBotao - 6 * card.lossyScale.y;
            if (topo - alturaPx < 0) topo = baseBotao + brt.rect.height * brt.lossyScale.y + alturaPx + 6 * card.lossyScale.y;
            card.position = new Vector3(esquerda, topo, 0);
            if (ops.Count > 1) scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - dd.value / (float)(ops.Count - 1));
        }

        void FecharLista()
        {
            if (_lista != null && !_lista.WasCollected) UnityEngine.Object.Destroy(_lista);
            _lista = null;
        }

        internal static string NomeLegivel(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (var ext in new[] { ".ini", ".txt" })
                if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return s.Substring(0, s.Length - ext.Length);
            return s;
        }
    }
}
