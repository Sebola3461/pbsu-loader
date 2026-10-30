using System;
using System.Collections.Generic;
using System.Text;
using Il2Cpp;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBSLoaderCommonUtils.Mundo
{
    public enum TipoNoTrafego { Carro, Onibus }

    /// <summary>Um veículo do trânsito (IA). O mesmo objeto segue o mesmo veículo enquanto ele existir.</summary>
    public sealed class VeiculoDoTrafego
    {
        public TipoNoTrafego Tipo { get; internal set; }
        public Transform Raiz { get; internal set; }
        public Vector3 Posicao => Vivo ? Raiz.position : default;
        public Vector3 Frente => Vivo ? Raiz.forward : Vector3.forward;
        public float VelocidadeKmh
        {
            get
            {
                if (!Vivo) return 0f;
                if (_rb == null || _rb.WasCollected) _rb = Raiz.GetComponentInChildren<Rigidbody>();
                return _rb != null ? _rb.velocity.magnitude * 3.6f : 0f;
            }
        }
        /// <summary>Distância até o ônibus do jogador, em metros (infinito sem ônibus).</summary>
        public float DistanciaM => Vivo && Veiculos.Veiculo.Raiz != null ? Vector3.Distance(Raiz.position, Veiculos.Veiculo.Raiz.position) : float.PositiveInfinity;

        Rigidbody _rb;
        internal bool Vivo => Raiz != null && !Raiz.WasCollected && Raiz.gameObject.activeInHierarchy;
        public override string ToString() => $"{Tipo} {Raiz?.name} a {DistanciaM:0} m, {VelocidadeKmh:0} km/h";
    }

    public enum CorDoSemaforo { Desconhecida, Verde, Amarelo, Vermelho, Desligado }

    /// <summary>Um semáforo do mapa (MEPAI_TrafficLights e variantes).</summary>
    public sealed class Semaforo
    {
        /// <summary>Componente do jogo ("MEPAI_TrafficLights", "MEPAI_TrafficLights2", "MEPAI_TrafficLightsV3_4fases").</summary>
        public string Tipo { get; internal set; }
        public Transform Raiz { get; internal set; }
        public Vector3 Posicao => Raiz != null && !Raiz.WasCollected ? Raiz.position : default;
        /// <summary>Fase atual (0..Fases-1), ou -1 se não deu para ler.</summary>
        public int Fase => Trafego.LerFase(this);
        public int Fases { get; internal set; }

        /// <summary>
        /// Focos acesos no passo atual (um semáforo comanda o cruzamento inteiro, com vários focos), cada um com a cor
        /// deduzida do nome do objeto ou do tom do material.
        /// </summary>
        public IReadOnlyList<LuzDeSemaforo> LuzesAcesas => Trafego.Acesas(this);

        /// <summary>Cor do foco aceso mais perto de <paramref name="ponto"/> (ex.: à frente do ônibus), até <paramref name="maxM"/>.</summary>
        public CorDoSemaforo CorPara(Vector3 ponto, float maxM = 40f)
        {
            LuzDeSemaforo melhor = null; float d2 = maxM * maxM;
            foreach (var l in LuzesAcesas)
            {
                float d = (l.Posicao - ponto).sqrMagnitude;
                if (d < d2 && l.Cor != CorDoSemaforo.Desconhecida) { d2 = d; melhor = l; }
            }
            return melhor?.Cor ?? CorDoSemaforo.Desconhecida;
        }

        public override string ToString() => $"{Tipo} {Raiz?.name}: fase {Fase}/{Fases}";

        internal Component Jogo;
    }

    /// <summary>Um foco aceso de um semáforo.</summary>
    public sealed class LuzDeSemaforo
    {
        public Transform Objeto { get; internal set; }
        public Vector3 Posicao => Objeto != null && !Objeto.WasCollected ? Objeto.position : default;
        public CorDoSemaforo Cor { get; internal set; }
        public override string ToString() => $"{Objeto?.name} {Cor}";
    }

    public static partial class Trafego
    {
        static readonly List<VeiculoDoTrafego> _veiculos = new();
        static readonly List<Transform> _pedestres = new();
        static readonly List<Semaforo> _semaforos = new();
        static float _pedidoEm = -10f, _proxima;

        /// <summary>
        /// Veículos do trânsito ativos agora (carros e ônibus da IA), reescaneados 1 vez por segundo enquanto alguém usar
        /// esta lista (a busca custa: não é feita sem ninguém pedindo).
        /// </summary>
        public static IReadOnlyList<VeiculoDoTrafego> Veiculos { get { Pedir(); return _veiculos; } }
        /// <summary>Pedestres e ciclistas da IA ativos agora (mesma regra de <see cref="Veiculos"/>).</summary>
        public static IReadOnlyList<Transform> PedestresAtivos { get { Pedir(); return _pedestres; } }
        /// <summary>Semáforos carregados agora (mesma regra de <see cref="Veiculos"/>).</summary>
        public static IReadOnlyList<Semaforo> Semaforos { get { Pedir(); return _semaforos; } }

        /// <summary>O veículo do trânsito mais perto do ônibus do jogador, à frente dele (cone de ±30°), até <paramref name="maxM"/>.</summary>
        public static VeiculoDoTrafego MaisPertoAFrente(float maxM = 100f)
        {
            var raiz = Veiculos.Count > 0 ? PBSLoaderCommonUtils.Veiculos.Veiculo.Raiz : null;
            if (raiz == null) return null;
            VeiculoDoTrafego melhor = null; float d2 = maxM * maxM;
            foreach (var v in _veiculos)
            {
                var d = v.Posicao - raiz.position;
                float m = d.sqrMagnitude;
                if (m < d2 && m > 1f && Vector3.Dot(d.normalized, raiz.forward) > 0.866f) { d2 = m; melhor = v; }
            }
            return melhor;
        }

        static void Pedir()
        {
            bool primeira = Time.unscaledTime - _pedidoEm > 2f;
            _pedidoEm = Time.unscaledTime;
            if (primeira) _proxima = 0f;
            Atualizar();
        }

        internal static void Atualizar()
        {
            if (Time.unscaledTime - _pedidoEm > 2f || Time.unscaledTime < _proxima) return;
            _proxima = Time.unscaledTime + 1f;
            if (!PBSLoaderCommonUtils.Veiculos.Veiculo.Existe) { _veiculos.Clear(); _pedestres.Clear(); _semaforos.Clear(); return; }

            var raizJogador = PBSLoaderCommonUtils.Veiculos.Veiculo.Raiz;
            var vistos = new Dictionary<IntPtr, (Transform raiz, TipoNoTrafego tipo)>();
            void Add(Component c, TipoNoTrafego t)
            {
                if (c == null || !c.gameObject.activeInHierarchy) return;
                var r = c.transform.root;
                if (raizJogador != null && r.Pointer == raizJogador.Pointer) return;
                if (!vistos.ContainsKey(r.Pointer) || t == TipoNoTrafego.Onibus) vistos[r.Pointer] = (r, t);
            }
            foreach (var c in Object.FindObjectsOfType<VeiculoSimples>()) Add(c, TipoNoTrafego.Carro);
            foreach (var c in Object.FindObjectsOfType<MEPAI_CarFrontalCollider>()) Add(c, TipoNoTrafego.Carro);
            foreach (var c in Object.FindObjectsOfType<MEPAI_Bus>()) Add(c, TipoNoTrafego.Onibus);

            var novos = new List<VeiculoDoTrafego>();
            foreach (var kv in vistos)
            {
                var v = _veiculos.Find(x => x.Raiz != null && !x.Raiz.WasCollected && x.Raiz.Pointer == kv.Key)
                        ?? new VeiculoDoTrafego { Raiz = kv.Value.raiz };
                v.Tipo = kv.Value.tipo;
                novos.Add(v);
            }
            _veiculos.Clear(); _veiculos.AddRange(novos);

            _pedestres.Clear();
            foreach (var p in Object.FindObjectsOfType<PedsMEPSegueWayPoint>()) if (p.gameObject.activeInHierarchy) _pedestres.Add(p.transform);
            foreach (var p in Object.FindObjectsOfType<PedsMEPSegueWayPointCiclista>()) if (p.gameObject.activeInHierarchy) _pedestres.Add(p.transform);
            foreach (var p in Object.FindObjectsOfType<PedsMEPSegueWayPointSoPedestre>()) if (p.gameObject.activeInHierarchy) _pedestres.Add(p.transform);

            var sems = new List<Semaforo>();
            foreach (var s in Object.FindObjectsOfType<MEPAI_TrafficLights>())
                sems.Add(Semaforo(s, "MEPAI_TrafficLights", s.Phases?.Length ?? 0));
            foreach (var s in Object.FindObjectsOfType<MEPAI_TrafficLights2>())
                sems.Add(Semaforo(s, "MEPAI_TrafficLights2", s.lightSections?.Length ?? 0));
            foreach (var s in Object.FindObjectsOfType<MEPAI_TrafficLightsV3_4fases>())
                sems.Add(Semaforo(s, "MEPAI_TrafficLightsV3_4fases", 4));
            _semaforos.Clear(); _semaforos.AddRange(sems);
        }

        static Semaforo Semaforo(Component c, string tipo, int fases)
        {
            foreach (var s in _semaforos) if (s.Jogo != null && !s.Jogo.WasCollected && s.Jogo.Pointer == c.Pointer) { s.Fases = fases; return s; }
            return new Semaforo { Jogo = c, Tipo = tipo, Raiz = c.transform, Fases = fases };
        }

        internal static IReadOnlyList<LuzDeSemaforo> Acesas(Semaforo s)
        {
            var r = new List<LuzDeSemaforo>();
            try
            {
                if (s.Jogo == null || s.Jogo.WasCollected) return r;
                var objs = new List<GameObject>();
                var t2 = s.Jogo.TryCast<MEPAI_TrafficLights2>();
                if (t2 != null && t2.lightSections != null && t2.cicloAtual >= 0 && t2.cicloAtual < t2.lightSections.Length)
                {
                    var on = t2.lightSections[t2.cicloAtual]?.objs?.objsToEnable;
                    if (on != null) foreach (var g in on) if (g != null) objs.Add(g);
                }
                var t1 = s.Jogo.TryCast<MEPAI_TrafficLights>();
                if (t1 != null && t1.Phases != null && t1.faseAtual >= 0 && t1.faseAtual < t1.Phases.Length && t1.Phases[t1.faseAtual] != null)
                    foreach (var tr in t1.Phases[t1.faseAtual].GetComponentsInChildren<Transform>(false)) objs.Add(tr.gameObject);
                var t3 = s.Jogo.TryCast<MEPAI_TrafficLightsV3_4fases>();
                if (t3 != null)
                    foreach (var arr in new[] { t3.fase1PreOn, t3.fase1RealOn, t3.fase1PosOn, t3.fase2Pre, t3.fase2Real, t3.fase2Pos, t3.fase3Pre, t3.fase3Real, t3.fase3Pos, t3.fase4Pre, t3.fase4Real, t3.fase4Pos })
                        if (arr != null) foreach (var g in arr) if (g != null && g.activeInHierarchy) objs.Add(g);
                foreach (var g in objs)
                {
                    if (!g.activeInHierarchy) continue;
                    r.Add(new LuzDeSemaforo { Objeto = g.transform, Cor = CorDe(g) });
                }
            }
            catch (Exception e) { Sdk.Registrar(e, "Semaforo.LuzesAcesas"); }
            return r;
        }

        // Cor de um foco: primeiro pelo nome (mapas costumam nomear "verde", "red", "amarelo"...), depois pelo tom do
        // material ou da luz.
        internal static CorDoSemaforo CorDe(GameObject g)
        {
            var n = (g.name ?? "").ToLowerInvariant();
            if (Tem(n, "verde", "green", "_vd", "vd_")) return CorDoSemaforo.Verde;
            if (Tem(n, "amarel", "yellow", "amber", "_am", "am_")) return CorDoSemaforo.Amarelo;
            if (Tem(n, "vermelh", "red", "_vm", "vm_")) return CorDoSemaforo.Vermelho;
            Color? c = null;
            var luz = g.GetComponentInChildren<Light>();
            if (luz != null) c = luz.color;
            else
            {
                var rd = g.GetComponentInChildren<Renderer>();
                var m = rd != null ? rd.sharedMaterial : null;
                if (m != null)
                {
                    var mn = (m.name ?? "").ToLowerInvariant();
                    if (Tem(mn, "verde", "green")) return CorDoSemaforo.Verde;
                    if (Tem(mn, "amarel", "yellow", "amber")) return CorDoSemaforo.Amarelo;
                    if (Tem(mn, "vermelh", "red")) return CorDoSemaforo.Vermelho;
                    if (m.HasProperty("_EmissionColor")) c = m.GetColor("_EmissionColor");
                    if ((c == null || c.Value.maxColorComponent < 0.05f) && m.HasProperty("_Color")) c = m.color;
                }
            }
            if (c == null || c.Value.maxColorComponent < 0.05f) return CorDoSemaforo.Desconhecida;
            Color.RGBToHSV(c.Value, out float h, out float sat, out _);
            if (sat < 0.25f) return CorDoSemaforo.Desconhecida;
            float graus = h * 360f;
            return graus < 20f || graus > 330f ? CorDoSemaforo.Vermelho
                : graus < 70f ? CorDoSemaforo.Amarelo
                : graus < 180f ? CorDoSemaforo.Verde
                : CorDoSemaforo.Desconhecida;
        }

        static bool Tem(string s, params string[] partes) { foreach (var p in partes) if (s.Contains(p)) return true; return false; }

        internal static int LerFase(Semaforo s)
        {
            try
            {
                if (s.Jogo == null || s.Jogo.WasCollected) return -1;
                var t1 = s.Jogo.TryCast<MEPAI_TrafficLights>();
                if (t1 != null) return t1.faseAtual;
                var t2 = s.Jogo.TryCast<MEPAI_TrafficLights2>();
                if (t2 != null) return t2.cicloAtual;
                return -1;
            }
            catch { return -1; }
        }

        // Estrutura de alguns semáforos (para descobrir como cada fase indica a cor).
        internal static string DiagnosticoSemaforos(int max)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var s in Semaforos)
            {
                if (n++ >= max) break;
                sb.Append($"{s}\n");
                try
                {
                    var t1 = s.Jogo.TryCast<MEPAI_TrafficLights>();
                    if (t1 != null && t1.Phases != null)
                    {
                        for (int i = 0; i < t1.Phases.Length; i++)
                        {
                            var g = t1.Phases[i];
                            sb.Append($"  fase {i} ({(t1.eachPhaseTimer != null && i < t1.eachPhaseTimer.Length ? t1.eachPhaseTimer[i] : -1)} s): {Arvore(g, 3)}\n");
                        }
                    }
                    var t2 = s.Jogo.TryCast<MEPAI_TrafficLights2>();
                    if (t2 != null)
                    {
                        sb.Append($"  ciclo {t2.cicloAtual}, relógio {t2.clockInSeconds}\n");
                        var on = t2.lightSections != null && t2.cicloAtual < t2.lightSections.Length ? t2.lightSections[t2.cicloAtual]?.objs?.objsToEnable : null;
                        sb.Append($"  liga agora: {(on != null ? string.Join(" | ", System.Linq.Enumerable.Select(on, g => Arvore(g, 1))) : "-")}\n");
                    }
                    sb.Append($"  acesas: {string.Join(", ", s.LuzesAcesas)}\n");
                    var t3 = s.Jogo.TryCast<MEPAI_TrafficLightsV3_4fases>();
                    if (t3 != null)
                        sb.Append($"  fase1 real: {Lista(t3.fase1RealOn)}; fase1 pré: {Lista(t3.fase1PreOn)}; fase2 real: {Lista(t3.fase2Real)}\n");
                }
                catch (Exception e) { sb.Append("  erro: ").Append(e.Message).Append('\n'); }
            }
            return sb.Length > 0 ? sb.ToString() : "nenhum semáforo\n";
        }

        static string Lista(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> a)
        {
            if (a == null) return "-";
            var l = new List<string>();
            foreach (var g in a) l.Add(g == null ? "null" : $"{g.name}{(g.activeSelf ? "*" : "")}");
            return string.Join(", ", l);
        }

        static string Arvore(GameObject g, int profundidade)
        {
            if (g == null) return "null";
            var sb = new StringBuilder();
            sb.Append(g.name).Append(g.activeSelf ? "*" : "");
            var luz = g.GetComponent<Light>();
            if (luz != null) sb.Append($"[luz {ColorUtility.ToHtmlStringRGB(luz.color)}]");
            var r = g.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null) sb.Append($"[mat {r.sharedMaterial.name}]");
            if (profundidade > 0 && g.transform.childCount > 0)
            {
                sb.Append(" {");
                for (int i = 0; i < g.transform.childCount && i < 8; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(Arvore(g.transform.GetChild(i).gameObject, profundidade - 1));
                }
                if (g.transform.childCount > 8) sb.Append("; …");
                sb.Append('}');
            }
            return sb.ToString();
        }
    }

    public static partial class Tempo
    {
        /// <summary>Muda a hora do jogo (mantém o dia e o ano).</summary>
        public static bool Definir(int hora, int minuto = 0)
        {
            var sky = Ceu();
            if (sky == null) return false;
            var t = sky.GameTime;
            sky.SetTime(t.Years, t.Days, Mathf.Clamp(hora, 0, 23), Mathf.Clamp(minuto, 0, 59), 0);
            return true;
        }

        /// <summary>Para ou solta o relógio do jogo (parado, a hora não anda).</summary>
        public static bool Pausar(bool parado)
        {
            var t = Ceu()?.GameTime;
            if (t == null) return false;
            if (parado) { if (t.ProgressTime != EnviroTime.TimeProgressMode.None) _modoAntes = t.ProgressTime; t.ProgressTime = EnviroTime.TimeProgressMode.None; }
            else t.ProgressTime = _modoAntes == EnviroTime.TimeProgressMode.None ? EnviroTime.TimeProgressMode.Simulated : _modoAntes;
            return true;
        }

        /// <summary>Quantos minutos reais dura um dia do jogo (o jogo usa o mesmo valor para a noite).</summary>
        public static bool DefinirDuracaoDoDia(float minutos)
        {
            var t = Ceu()?.GameTime;
            if (t == null || minutos <= 0f) return false;
            t.DayLengthInMinutes = minutos; t.NightLengthInMinutes = minutos;
            return true;
        }

        static EnviroTime.TimeProgressMode _modoAntes = EnviroTime.TimeProgressMode.Simulated;

        internal static EnviroSky Ceu() { try { return EnviroSky.instance; } catch { return null; } }
    }

    public static partial class Clima
    {
        /// <summary>Nomes dos climas do céu do jogo (presets do EnviroSky: "Clear Sky", "Cloudy 1", "Rain"...).</summary>
        public static IReadOnlyList<string> Presets
        {
            get
            {
                var l = new List<string>();
                try
                {
                    var ps = Tempo.Ceu()?.Weather?.weatherPresets;
                    if (ps != null) foreach (var p in ps) if (p != null) l.Add(p.Name);
                }
                catch { }
                return l;
            }
        }

        /// <summary>Muda o céu para o clima <paramref name="nome"/> (um de <see cref="Presets"/>), com transição.</summary>
        public static bool Definir(string nome)
        {
            var sky = Tempo.Ceu();
            if (sky == null || string.IsNullOrEmpty(nome)) return false;
            try { sky.ChangeWeather(nome); return true; }
            catch (Exception e) { Sdk.Registrar(e, "Clima.Definir"); return false; }
        }

        /// <summary>
        /// Faz chover (ou para): a chuva do jogo segue o clima do céu, então isto troca o clima para um com chuva da
        /// intensidade pedida ("Light Rain", "Heavy Rain", "Storm") ou para céu limpo. A troca tem transição de alguns
        /// segundos; <see cref="Chovendo"/> acompanha.
        /// </summary>
        public static bool Chover(Chuva intensidade)
        {
            var ps = Presets;
            string Achar(params string[] partes)
            {
                foreach (var parte in partes)
                    foreach (var p in ps) if (p.IndexOf(parte, StringComparison.OrdinalIgnoreCase) >= 0) return p;
                return null;
            }
            string alvo = intensidade switch
            {
                Chuva.Nenhuma => Achar("Clear", "Limpo") ?? (ps.Count > 0 ? ps[0] : null),
                Chuva.Leve => Achar("Light Rain", "Rain"),
                Chuva.Media => Achar("Heavy Rain", "Rain"),
                _ => Achar("Storm", "Heavy Rain", "Rain"),
            };
            if (alvo == null || !Definir(alvo)) return false;
            // A chuva do próprio jogo (gotas no para-brisa, rua molhada) é uma chave à parte do céu.
            try { MepMobileControls.TahChovendo = intensidade != Chuva.Nenhuma; } catch { }
            return true;
        }
    }
}
