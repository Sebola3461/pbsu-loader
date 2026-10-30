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
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var cena = SceneManager.GetSceneAt(i);
                if (!cena.isLoaded) continue;
                foreach (var go in cena.GetRootGameObjects())
                {
                    if (go == null || go.name != nome) continue;
                    if (filho == null || go.transform.Find(filho) != null) return go.transform;
                }
            }
            // Canvas que não estão no topo da cena (dentro de outro objeto) ou em DontDestroyOnLoad,
            // que não aparece em SceneManager: procura pelos componentes Canvas.
            foreach (var cv in Canvases())
            {
                if (cv == null || !cv.isRootCanvas || cv.name != nome) continue;
                if (cv.name == "PBSLoaderCommonInterface" || cv.name == "PBSLoaderCommonInterface_Carregando") continue;
                if (filho == null || cv.transform.Find(filho) != null) return cv.transform;
            }
            return null;
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
