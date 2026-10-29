using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Suspende os controles de teclado do jogo (Rewired) enquanto algum mod precisa do teclado: um painel que
    /// usa teclas (ex.: ajustar espelhos pelo teclado numérico sem ligar a seta) ou o jogador digitando num campo
    /// de texto. Mesmo contrato do <see cref="Mouse"/>: chame a cada frame; quem para de pedir sai sozinho.
    /// Teclas que scripts do jogo leem direto do Input não passam pelo Rewired e continuam valendo.
    /// </summary>
    public static class Teclado
    {
        static readonly Dictionary<string, int> _pedidos = new();
        static Il2CppRewired.Player _suspenso;

        public static void SuspenderJogo(string dono, bool precisa = true)
        {
            if (precisa) _pedidos[dono] = Time.frameCount;
            else _pedidos.Remove(dono);
        }

        /// <summary>Os controles de teclado do jogo estão suspensos agora.</summary>
        public static bool JogoSuspenso => _suspenso != null;

        static bool Pedindo()
        {
            int f = Time.frameCount;
            foreach (var kv in _pedidos) if (f - kv.Value <= 1) return true;
            return false;
        }

        internal static void Aplicar()
        {
            bool querer = Pedindo() || Atalhos.Digitando();
            try
            {
                if (querer && _suspenso == null)
                {
                    var p = Jogo.Jogador();
                    if (p == null || !p.controllers.hasKeyboard) return;
                    p.controllers.hasKeyboard = false;
                    _suspenso = p;
                }
                else if (!querer && _suspenso != null)
                {
                    var p = _suspenso;
                    _suspenso = null;
                    if (!p.WasCollected) p.controllers.hasKeyboard = true;
                }
            }
            catch (Exception e)
            {
                _suspenso = null;
                MelonLogger.Warning("[Teclado] " + e.Message);
            }
        }

        // Ao trocar de cena o Player pode ser outro: devolve o teclado ao antigo antes.
        internal static void CenaMudou()
        {
            if (_suspenso == null) return;
            try { if (!_suspenso.WasCollected) _suspenso.controllers.hasKeyboard = true; } catch { }
            _suspenso = null;
        }
    }
}
