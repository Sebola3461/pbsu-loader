using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace PBSLoaderCommonInterface
{
    // Medição do custo do mod por frame. Só registra no log os frames lentos (do jogo ou nossos), com
    // a divisão do que o mod gastou neles, e o tempo de cada troca de tela (clique -> cena -> revelação).
    internal static class Desempenho
    {
        internal static bool Ligado = true;

        static readonly Dictionary<string, double> _quadro = new();
        static float _ultimoRegistro;
        static int _g0, _g2;
        static long _gIl;
        static readonly double MsPorTick = 1000.0 / Stopwatch.Frequency;

        internal readonly struct Trecho : IDisposable
        {
            readonly string _nome;
            readonly long _ini;
            internal Trecho(string nome) { _nome = nome; _ini = Ligado ? Stopwatch.GetTimestamp() : 0; }
            public void Dispose()
            {
                if (!Ligado) return;
                double ms = (Stopwatch.GetTimestamp() - _ini) * MsPorTick;
                _quadro.TryGetValue(_nome, out var a);
                _quadro[_nome] = a + ms;
            }
        }

        internal static Trecho Medir(string nome) => new(nome);

        // Fim do frame (OnLateUpdate): registra se o frame foi lento ou se o mod pesou nele.
        internal static void FimDoQuadro()
        {
            if (!Ligado) return;
            double nosso = 0;
            foreach (var kv in _quadro) if (!kv.Key.StartsWith("·")) nosso += kv.Value;
            float quadroMs = Time.unscaledDeltaTime * 1000f;
            bool lento = quadroMs > 120f || nosso > 6.0;
            if (lento && (Time.unscaledTime - _ultimoRegistro > 0.1f || nosso > 20.0))
            {
                _ultimoRegistro = Time.unscaledTime;
                var sb = new StringBuilder();
                sb.Append($"[Desempenho] quadro {quadroMs:0} ms, mod {nosso:0.0} ms");
                var lista = new List<KeyValuePair<string, double>>(_quadro);
                lista.Sort((a, b) => b.Value.CompareTo(a.Value));
                int n = 0;
                foreach (var kv in lista)
                {
                    if (kv.Value < 0.3 || n++ >= 8) break;
                    sb.Append(n == 1 ? ": " : ", ").Append(kv.Key).Append(' ').Append(kv.Value.ToString("0.0"));
                }
                // Coletas de lixo neste frame (.NET do MelonLoader e GC do jogo): pausam tudo.
                int g0 = GC.CollectionCount(0), g2 = GC.CollectionCount(2);
                long il = Il2CppSystem.GC.CollectionCount(0);
                if (g0 != _g0 || g2 != _g2 || il != _gIl)
                    sb.Append($" | GC .NET g0 +{g0 - _g0} g2 +{g2 - _g2}, GC jogo +{il - _gIl}");
                MelonLogger.Msg(sb.ToString());
            }
            _g0 = GC.CollectionCount(0); _g2 = GC.CollectionCount(2); _gIl = Il2CppSystem.GC.CollectionCount(0);
            _quadro.Clear();
        }

        // ---------- trocas de tela ----------
        static long _clique;
        static bool _emTroca;

        static double Desde() => (Stopwatch.GetTimestamp() - _clique) * MsPorTick;

        internal static void Marco(string oque)
        {
            if (!Ligado) return;
            if (oque == "clique") { _clique = Stopwatch.GetTimestamp(); _emTroca = true; MelonLogger.Msg("[Troca] clique"); return; }
            if (!_emTroca) return;
            MelonLogger.Msg($"[Troca] {oque}: +{Desde():0} ms");
            if (oque == "revelada") _emTroca = false;
        }
    }
}
