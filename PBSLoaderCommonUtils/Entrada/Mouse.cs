using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace PBSLoaderCommonUtils.Entrada
{
    /// <summary>
    /// Dono único do cursor. Cada mod diz, a cada frame, o que precisa: "solto" (painel aberto, precisa clicar)
    /// ou "preso" (câmera girando pelo mouse). Solto ganha de preso. Quem para de pedir sai sozinho no frame
    /// seguinte, então um painel fechado (ou um mod desligado) nunca deixa o cursor preso.
    ///
    /// Ninguém guarda e restaura o estado antigo: quando o último pedido some, o cursor volta ao estado do
    /// jogo, que nunca prende o mouse (lockState None) e só o esconde parado (MostraEscondeMouse).
    /// Guardar e restaurar era o que travava o mouse: dois painéis abertos e fechados fora de ordem
    /// devolviam um estado velho.
    /// </summary>
    public static class Mouse
    {
        static readonly Dictionary<string, int> _soltos = new(), _presos = new();
        static bool _noControle;

        /// <summary>Chame a cada frame enquanto precisar do cursor livre e visível (ex.: painel aberto).</summary>
        public static void Soltar(string dono, bool precisa = true) => Pedir(_soltos, dono, precisa);

        /// <summary>Chame a cada frame enquanto precisar do cursor preso e escondido (ex.: câmera pelo mouse).</summary>
        public static void Prender(string dono, bool precisa = true) => Pedir(_presos, dono, precisa);

        static void Pedir(Dictionary<string, int> d, string dono, bool precisa)
        {
            if (precisa) d[dono] = Time.frameCount;
            else d.Remove(dono);
        }

        /// <summary>Algum mod está pedindo o cursor solto neste frame.</summary>
        public static bool Solto => Vivos(_soltos);
        /// <summary>Algum mod está pedindo o cursor preso neste frame (e nenhum pede solto).</summary>
        public static bool Preso => !Solto && Vivos(_presos);
        /// <summary>Algum mod controla o cursor agora (o jogo não esconde nem mostra sozinho).</summary>
        public static bool Controlando => Solto || Vivos(_presos);

        // Pedido feito neste frame ou no anterior (quem pede no LateUpdate e quem pede no Update valem igual).
        static bool Vivos(Dictionary<string, int> d)
        {
            int f = Time.frameCount;
            foreach (var kv in d) if (f - kv.Value <= 1) return true;
            return false;
        }

        // No LateUpdate, depois do jogo (que mexe no cursor no Update).
        internal static void Aplicar()
        {
            if (Solto) Definir(CursorLockMode.None, true);
            else if (Vivos(_presos)) Definir(CursorLockMode.Locked, false);
            else if (_noControle)
            {
                _noControle = false;
                Definir(CursorLockMode.None, true); // o jogo volta a esconder sozinho quando o mouse para
                return;
            }
            else return;
            _noControle = true;
        }

        static void Definir(CursorLockMode trava, bool visivel)
        {
            if (Cursor.lockState != trava) Cursor.lockState = trava;
            if (Cursor.visible != visivel) Cursor.visible = visivel;
        }

        public static string Diagnostico()
        {
            var sb = new StringBuilder();
            int f = Time.frameCount;
            sb.Append($"cursor: lockState={Cursor.lockState} visible={Cursor.visible} controlando={_noControle}\n");
            foreach (var kv in _soltos) sb.Append($"  solto  {kv.Key} (há {f - kv.Value} frames)\n");
            foreach (var kv in _presos) sb.Append($"  preso  {kv.Key} (há {f - kv.Value} frames)\n");
            return sb.ToString();
        }
    }

    // MepMobileControls.MostraEscondeMouse, chamado todo frame pelo jogo, esconde o cursor quando o mouse fica
    // parado e o mostra quando ele se mexe. Com um painel aberto ele sumia com o cursor; com a câmera presa
    // ele o fazia piscar. Enquanto algum mod controla o cursor, o jogo não mexe nele.
    [HarmonyPatch(typeof(Il2Cpp.MepMobileControls), nameof(Il2Cpp.MepMobileControls.MostraEscondeMouse))]
    internal static class PatchMostraEscondeMouse
    {
        static bool Prefix() => !Mouse.Controlando;
    }
}
