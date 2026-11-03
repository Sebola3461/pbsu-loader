using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Ponte com a UI original do jogo. A UI nova nunca reimplementa lógica: ela lê estado/textos
    // daqui e dispara os mesmos callbacks que um clique real dispararia.
    internal static class Orig
    {
        // Raiz da cena ativa com esse nome que contém "filho" (há vários roots chamados "Canvas").
        // Procura em todas as cenas carregadas: no sceneLoaded a cena nova ainda pode não ser a ativa.
        internal static Transform Raiz(string nome, string filho = null)
        {
            // As telas originais são Canvas na raiz da cena: o índice de Canvas por nome resolve quase sempre.
            CanvasesPorNome().TryGetValue(nome, out var canvases);
            if (canvases != null)
                foreach (var c in canvases)
                    if (c.t != null && c.topo && (filho == null || c.t.Find(filho) != null)) return c.t;
            if (Raizes().TryGetValue(nome, out var comNome))
                foreach (var t in comNome)
                    if (t != null && (filho == null || t.Find(filho) != null)) return t;
            // Canvas que não estão no topo da cena (dentro de outro objeto) ou em DontDestroyOnLoad,
            // que não aparece em SceneManager.
            if (canvases != null && nome != "PBSLoaderCommonInterface" && nome != "PBSLoaderCommonInterface_Carregando")
                foreach (var c in canvases)
                    if (c.t != null && c.raizCanvas && (filho == null || c.t.Find(filho) != null)) return c.t;
            return null;
        }

        // Raízes das cenas carregadas por nome: o mapa tem milhares de raízes, e cada
        // tela que ainda não achou o seu original varria todas elas de novo (25-30 ms a cada segundo na partida).
        // Fica válida 5 s (a troca de cena invalida): só serve quando a raiz com o nome procurado não é um Canvas.
        static readonly Dictionary<string, List<Transform>> _raizes = new();
        static float _raizesAte = -1f;

        static float _cenaEm;

        internal static void CenaMudou() { _raizesAte = -1f; _cenaEm = Time.unscaledTime; }

        internal static float SegundosDesdeCena => Time.unscaledTime - _cenaEm;

        static Dictionary<string, List<Transform>> Raizes()
        {
            if (Time.unscaledTime < _raizesAte) return _raizes;
            _raizesAte = Time.unscaledTime + 5f;
            foreach (var l in _raizes.Values) l.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var cena = SceneManager.GetSceneAt(i);
                if (!cena.isLoaded) continue;
                foreach (var go in cena.GetRootGameObjects())
                {
                    if (go == null) continue;
                    if (!_raizes.TryGetValue(go.name, out var l)) _raizes[go.name] = l = new List<Transform>();
                    l.Add(go.transform);
                }
            }
            return _raizes;
        }

        // Canvas por nome, montado uma vez por frame: percorrer a lista pela ponte do IL2CPP (um objeto e um nome
        // convertido por Canvas, e o mapa tem centenas em placas e letreiros) custava ~4 ms a cada tela procurada.
        static readonly Dictionary<string, List<(Transform t, bool topo, bool raizCanvas)>> _porNome = new();
        static int _framePorNome = -1;

        static Dictionary<string, List<(Transform t, bool topo, bool raizCanvas)>> CanvasesPorNome()
        {
            if (_framePorNome == Time.frameCount) return _porNome;
            _framePorNome = Time.frameCount;
            foreach (var l in _porNome.Values) l.Clear();
            foreach (var cv in Canvases())
            {
                if (cv == null) continue;
                var t = cv.transform;
                if (!_porNome.TryGetValue(cv.name, out var l)) _porNome[cv.name] = l = new List<(Transform, bool, bool)>();
                l.Add((t, t.parent == null, cv.isRootCanvas));
            }
            return _porNome;
        }

        // Lista de Canvas da cena, no máximo uma varredura por frame (várias telas procuram ao mesmo tempo).
        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Canvas> _canvases;
        static int _frameCanvases = -1;

        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Canvas> Canvases()
        {
            if (_frameCanvases != Time.frameCount || _canvases == null)
            {
                _canvases = Object.FindObjectsOfType<Canvas>(true);
                _frameCanvases = Time.frameCount;
            }
            return _canvases;
        }

        // Ativo considerando os pais até "raiz" (exclusive). Permite mostrar itens de painéis
        // que o jogo mantém fechados (ex.: MENU ENTRADA), respeitando o que o jogo esconde dentro deles.
        internal static bool Ativo(Transform t, Transform raiz)
        {
            for (var x = t; x != null && x != raiz; x = x.parent)
                if (!x.gameObject.activeSelf) return false;
            return t != null;
        }

        // Clique equivalente ao do jogador: Button.onClick, senão os callbacks de clique do EventTrigger.
        internal static void Clicar(Transform t)
        {
            if (t == null) return;
            var btn = t.GetComponent<Button>();
            if (btn != null)
            {
                if (btn.interactable) btn.onClick.Invoke();
                return;
            }
            var et = t.GetComponent<EventTrigger>();
            if (et == null) return;
            EventTrigger.Entry escolhido = null;
            foreach (var e in et.triggers)
            {
                if (e.eventID == EventTriggerType.PointerClick) { escolhido = e; break; }
                if (escolhido == null && (e.eventID == EventTriggerType.PointerDown || e.eventID == EventTriggerType.PointerUp)) escolhido = e;
            }
            escolhido?.callback.Invoke(new PointerEventData(EventSystem.current));
        }

        internal static bool Clicavel(Transform t)
        {
            if (t == null) return false;
            var btn = t.GetComponent<Button>();
            return btn == null || btn.interactable;
        }

        // Texto do próprio objeto ou do primeiro Text ativo dentro dele (botões têm o rótulo num filho).
        internal static string Texto(Transform t)
        {
            if (t == null) return null;
            var txt = t.GetComponent<Text>();
            if (txt != null) return txt.text;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = t.GetChild(i);
                if (!f.gameObject.activeSelf) continue;
                var ft = f.GetComponent<Text>();
                if (ft != null && !string.IsNullOrWhiteSpace(ft.text)) return ft.text;
            }
            return null;
        }

        // Botão cujo onClick tem uma chamada persistente (configurada no editor) para "metodo".
        // GetPersistentMethodName foi removido do build, mas os campos internos do UnityEvent
        // (m_PersistentCalls.m_Calls[i].m_MethodName / m_Arguments) continuam lá.
        // Ler as chamadas de cada botão é caro (vários acessos ao IL2CPP por botão), e uma tela procura
        // vários métodos ao montar: os botões da raiz são indexados uma vez por frame.
        internal static Transform BotaoQueChama(Transform raiz, string metodo, int? arg = null)
        {
            if (raiz == null) return null;
            foreach (var (b, m, a) in Indice(raiz))
            {
                if (m != metodo) continue;
                if (arg.HasValue && a != arg.Value) continue;
                if (b != null && !b.WasCollected) return b.transform;
            }
            return null;
        }

        static readonly List<(Button b, string metodo, int? arg)> _indice = new();
        static System.IntPtr _indiceRaiz;
        static int _indiceQuadro = -1;

        static List<(Button b, string metodo, int? arg)> Indice(Transform raiz)
        {
            if (_indiceQuadro == Time.frameCount && _indiceRaiz == raiz.Pointer) return _indice;
            _indiceQuadro = Time.frameCount;
            _indiceRaiz = raiz.Pointer;
            _indice.Clear();
            foreach (var b in raiz.GetComponentsInChildren<Button>(true))
            {
                var chamadas = b.onClick?.m_PersistentCalls?.m_Calls;
                if (chamadas == null) continue;
                for (int i = 0; i < chamadas.Count; i++)
                {
                    var c = chamadas[i];
                    if (c == null) continue;
                    _indice.Add((b, c.m_MethodName, c.m_Arguments != null ? c.m_Arguments.m_IntArgument : (int?)null));
                }
            }
            return _indice;
        }

        // Primeiro botão dentro de "raiz" cujo nome contém "parte" (sem diferenciar maiúsculas).
        internal static Transform BotaoPorNome(Transform raiz, string parte)
        {
            if (raiz == null) return null;
            foreach (var b in raiz.GetComponentsInChildren<Button>(true))
                if (b.name.IndexOf(parte, System.StringComparison.OrdinalIgnoreCase) >= 0) return b.transform;
            return null;
        }

        internal static string Limpo(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("\n", " ").Trim();
    }
}
