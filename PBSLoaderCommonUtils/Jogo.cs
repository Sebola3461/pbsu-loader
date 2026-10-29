using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Teclas que o jogo usa, para avisar conflitos. Duas fontes:
    ///  - os controles do Rewired (o mapa do jogador, com o que ele remapeou), lidos ao vivo;
    ///  - teclas fixas que scripts do jogo leem direto do Input (achadas desmontando o GameAssembly). Essas
    ///    ignoram modificadores: Ctrl+Alt+G também solta o G do trailer. Só contam quando o script está na cena.
    /// </summary>
    public static class Jogo
    {
        sealed class Fixa
        {
            internal string uso;
            internal KeyCode[] teclas;
            internal Func<bool> presente;
            internal bool ativa;
        }

        static readonly List<Fixa> _fixas = new()
        {
            F<Il2Cpp.PortasBusNOVO>("portas do ônibus", KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.KeypadDivide, KeyCode.KeypadMultiply, KeyCode.KeypadMinus),
            F<Il2Cpp.MepMobileControls>("câmera pelo teclado numérico",KeyCode.Keypad2, KeyCode.Keypad4, KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad8, KeyCode.RightArrow),
            F<Il2Cpp.MouseOrbitCSHARP>("câmera (WASD e setas)", KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow),
            F<Il2CppMep.MEPControlesBus>("ar-condicionado", KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5),
            F<Il2Cpp.MepTrailer>("trailer", KeyCode.G),
            F<Il2Cpp.CanvasDesafioTempo>("desafio de tempo", KeyCode.G),
            F<Il2Cpp.ApplicationQuit>("sair do jogo", KeyCode.Escape),
            F<Il2Cpp.CarregaBusNoInicio>("carregar o ônibus", KeyCode.Return),
            F<Il2Cpp.MepConsole>("console do jogo", KeyCode.KeypadEnter),
            F<Il2Cpp.MepCanvasPC>("pausa", KeyCode.Pause),
            F<Il2Cpp.AtivaDesativaObjetoViaTrigger>("objetos do mapa", KeyCode.X),
            F<Il2Cpp.PosicionadorDePathsProTrafegoRuntime>("editor de rotas do tráfego", KeyCode.Insert, KeyCode.Home, KeyCode.Equals),
            F<Il2Cpp.Lights>("luzes (script Lights)", KeyCode.K, KeyCode.L, KeyCode.S),
            F<Il2Cpp.PlayerPrefCrypt>("depuração do jogo", KeyCode.J, KeyCode.U),
            F<Il2Cpp.AbridorDaPorraToda>("abrir tudo (depuração)", KeyCode.Alpha1, KeyCode.Alpha2),
            F<Il2Cpp.LB_CameraMove>("câmera de depuração", KeyCode.E, KeyCode.F),
            F<Il2Cpp.FreeCam>("câmera de depuração", KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.E, KeyCode.Q, KeyCode.F, KeyCode.R, KeyCode.PageUp, KeyCode.PageDown),
            F<Il2Cpp.FreeCameraView>("câmera de depuração", KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.E, KeyCode.Q, KeyCode.F, KeyCode.R, KeyCode.PageUp, KeyCode.PageDown),
            F<Il2Cpp.VeiculoSimples>("veículo simples", KeyCode.Keypad2, KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.KeypadPlus, KeyCode.DownArrow, KeyCode.LeftArrow),
        };

        static Fixa F<T>(string uso, params KeyCode[] teclas) where T : Il2CppObjectBase =>
            new() { uso = uso, teclas = teclas, presente = () => UnityEngine.Object.FindObjectOfType(Il2CppType.Of<T>()) != null };

        // Controles do Rewired no teclado: tecla, modificadores (Ctrl/Alt/Shift) e nome da ação.
        static readonly List<(KeyCode tecla, bool ctrl, bool alt, bool shift, string acao)> _rewired = new();
        static bool _rewiredLido;

        /// <summary>Com o que a combinação colide no jogo, em texto para o jogador.</summary>
        public static List<string> Conflitos(Combinacao c, bool rewired = true)
        {
            var r = new List<string>();
            foreach (var f in _fixas)
                if (f.ativa && Array.IndexOf(f.teclas, c.Tecla) >= 0 && !r.Contains("jogo: " + f.uso))
                    r.Add("jogo: " + f.uso);
            if (!rewired) return r;
            foreach (var (tecla, ctrl, alt, shift, acao) in _rewired)
            {
                if (tecla != c.Tecla) continue;
                // Controle sem modificador dispara com qualquer modificador junto; com modificador, só com o mesmo.
                bool semMod = !ctrl && !alt && !shift;
                if (semMod || (ctrl == c.Ctrl && alt == c.Alt && shift == c.Shift))
                {
                    var t = "controles do jogo: " + acao;
                    if (!r.Contains(t)) r.Add(t);
                }
            }
            return r;
        }

        static float _lerEm = -1;

        internal static void CenaMudou() => _lerEm = Time.unscaledTime + 3f; // espera a cena montar

        /// <summary>Relê as teclas do jogo agora (ex.: ao abrir a tela de atalhos, depois de o jogador remapear).</summary>
        public static void Reler()
        {
            foreach (var f in _fixas)
            {
                try { f.ativa = f.presente(); }
                catch { f.ativa = false; }
            }
            LerRewired();
            Atalhos.Verificar();
            Gravar();
        }

        internal static void Atualizar()
        {
            if (_lerEm < 0 || Time.unscaledTime < _lerEm) return;
            _lerEm = -1;
            Reler();
        }

        internal static Il2CppRewired.Player Jogador()
        {
            try
            {
                var inst = Il2Cpp.InstanciaDoPlayerRewired.instance;
                if (inst != null && inst.player != null) return inst.player;
            }
            catch { }
            try { return Il2CppRewired.ReInput.players.GetPlayer(0); }
            catch { return null; }
        }

        static void LerRewired()
        {
            var p = Jogador();
            if (p == null) return; // mantém a última leitura
            try
            {
                var mapas = new Il2CppSystem.Collections.Generic.List<Il2CppRewired.ControllerMap>();
                p.controllers.maps.GetAllMaps(Il2CppRewired.ControllerType.Keyboard, mapas);
                _rewired.Clear();
                for (int i = 0; i < mapas.Count; i++)
                {
                    var m = mapas[i];
                    if (m == null || !m.enabled) continue;
                    var botoes = m.GetButtonMaps();
                    if (botoes == null) continue;
                    for (int j = 0; j < botoes.Length; j++)
                    {
                        var e = botoes[j];
                        if (e == null || !e.enabled || e.keyCode == KeyCode.None) continue;
                        bool ctrl = false, alt = false, shift = false;
                        foreach (var mk in new[] { e.modifierKey1, e.modifierKey2, e.modifierKey3 })
                        {
                            if (mk == Il2CppRewired.ModifierKey.Control) ctrl = true;
                            else if (mk == Il2CppRewired.ModifierKey.Alt) alt = true;
                            else if (mk == Il2CppRewired.ModifierKey.Shift) shift = true;
                        }
                        var acao = e.actionDescriptiveName;
                        if (string.IsNullOrEmpty(acao)) acao = "ação " + e.actionId;
                        _rewired.Add((e.keyCode, ctrl, alt, shift, acao));
                    }
                }
                _rewiredLido = true;
            }
            catch (Exception ex) { MelonLogger.Warning("[Atalhos] não consegui ler os controles do jogo (Rewired): " + ex.Message); }
        }

        static string _ultimoGravado;

        // UserData/PBSLoaderCommonUtils/teclas.txt: as teclas do jogo e os atalhos dos mods, com conflitos.
        static void Gravar()
        {
            var sb = new StringBuilder();
            sb.Append("# Gerado pelo PBSLoaderCommonUtils. Teclas do jogo e atalhos dos mods.\n\n");
            sb.Append(_rewiredLido ? "[Controles do jogo (Rewired), teclado]\n" : "[Controles do jogo (Rewired): ainda não lidos]\n");
            var linhas = new List<string>();
            foreach (var (tecla, ctrl, alt, shift, acao) in _rewired)
                linhas.Add($"{new Combinacao(tecla, ctrl, alt, shift),-22} {acao}");
            linhas.Sort(StringComparer.Ordinal);
            foreach (var l in linhas) sb.Append(l).Append('\n');
            sb.Append("\n[Teclas fixas de scripts do jogo presentes na cena]\n");
            foreach (var f in _fixas)
                if (f.ativa) sb.Append($"{string.Join(", ", f.teclas),-60} {f.uso}\n");
            sb.Append("\n[Atalhos dos mods]\n");
            foreach (var a in Atalhos.Todos)
            {
                sb.Append($"{a.Combinacao,-22} {a.Mod}: {a.Nome}");
                if (a.Conflitos.Count > 0) sb.Append("   CONFLITO: ").Append(string.Join("; ", a.Conflitos));
                sb.Append('\n');
            }
            var txt = sb.ToString();
            if (txt == _ultimoGravado) return;
            _ultimoGravado = txt;
            try
            {
                var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "teclas.txt"), txt);
            }
            catch (Exception ex) { MelonLogger.Warning("[Atalhos] não gravei teclas.txt: " + ex.Message); }
        }
    }
}
