using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Ar comprimido do veículo: compressor movido pelo motor, reservatório, governador com purga do secador,
    /// freio de serviço pneumático e freio de molas (estacionamento). Um reservatório só para todos os mods:
    /// portas, suspensão a ar (<see cref="Gastar"/>) e o que vier.
    ///
    /// Unidades: pressão em bar manométrico; volumes de ar "livre" em litros a 1 bar. Pôr V litros livres num
    /// reservatório de R litros sobe a pressão em V / R bar (isotérmico).
    ///
    /// O freio atua no VPP: um prefixo em Brakes.DoUpdate troca o pedido do motorista (brakeInput) pela pressão
    /// que chegou às câmaras, e soma o freio de molas ao freio de mão. Veículos sem VPP só têm a simulação.
    /// </summary>
    public static class Ar
    {
        internal static MelonPreferences_Entry<bool> Ativo, FreioAAr, FreioDeMolas, SilenciarArFalso;
        internal static MelonPreferences_Entry<float> PressaoInicial, Corte, Retorno, VolumeTanque, Cilindrada, PressaoAlarme,
            Vazamento, ConsumoPorta, PressaoPortas, VolumeSom, VolumeAlarme;

        internal static void CriarPreferencias(MelonPreferences_Category c)
        {
            Ativo = c.CreateEntry("ArAtivo", true, "Simular o ar comprimido",
                "Compressor, reservatório, freio a ar e freio de molas. Desligado, o freio é o original do jogo");
            FreioAAr = c.CreateEntry("FreioAAr", true, "Freio a ar",
                "O freio segue a pressão nas câmaras (atraso pneumático, perde força com o reservatório baixo)");
            FreioDeMolas = c.CreateEntry("FreioDeMolas", true, "Freio de molas",
                "O freio de estacionamento é de molas: abaixo de ~5 bar ele aplica sozinho e o ônibus não sai do lugar");
            PressaoInicial = c.CreateEntry("PressaoInicial", 8.5f, "Pressão ao carregar o veículo (bar)",
                "0 = reservatório vazio: espere o compressor encher antes de sair");
            Corte = c.CreateEntry("PressaoCorte", 8.5f, "Corte do governador (bar)", "O compressor para de carregar nesta pressão e o secador purga");
            Retorno = c.CreateEntry("PressaoRetorno", 7.0f, "Retorno do governador (bar)", "O compressor volta a carregar abaixo desta pressão");
            VolumeTanque = c.CreateEntry("VolumeReservatorio", 100f, "Volume dos reservatórios (litros)");
            Cilindrada = c.CreateEntry("CilindradaCompressor", 0.6f, "Cilindrada do compressor (litros por volta)",
                "Com 0,6 L o reservatório vazio enche em ~4 min na lenta e ~2 min a 1500 rpm");
            PressaoAlarme = c.CreateEntry("PressaoAlarme", 5.5f, "Alarme de pressão baixa (bar)");
            Vazamento = c.CreateEntry("Vazamento", 0.02f, "Vazamento (bar por minuto)");
            ConsumoPorta = c.CreateEntry("ConsumoPorta", 6f, "Ar por movimento de porta (litros livres)");
            PressaoPortas = c.CreateEntry("PressaoPortas", 4f, "Pressão mínima para as portas (bar)",
                "Abaixo disso as portas pneumáticas não abrem nem fecham (0 = sempre funcionam)");
            VolumeSom = c.CreateEntry("VolumeSomAr", 1f, "Volume do chiado do ar");
            VolumeAlarme = c.CreateEntry("VolumeAlarme", 0.35f, "Volume do alarme de pressão baixa");
            SilenciarArFalso = c.CreateEntry("SilenciarArFalso", true, "Calar o chiado aleatório do jogo",
                "O ônibus original solta ar em intervalos sorteados; com a simulação o chiado vem dos freios de verdade");
        }

        // Câmaras e linhas: 1,5 L por roda freada; molas ~0,5 L por roda traseira.
        const float CamaraPorRoda = 1.5f, MolaPorRoda = 0.5f;
        const float PressaoFreioCheio = 6.0f;          // câmara com o pedal no fundo (válvula de pedal)
        const float MolaSolta = 5.0f, MolaAplicada = 2.0f;

        public static bool Simulando => Ativo != null && Ativo.Value && Veiculo.Existe;
        /// <summary>Reservatório (bar).</summary>
        public static float Pressao { get; private set; }
        public static float PressaoCorte => Corte?.Value ?? 8.5f;
        /// <summary>Compressor carregando (governador não cortou e o motor gira).</summary>
        public static bool CompressorCarregando { get; private set; }
        /// <summary>Ar que o compressor está mandando agora (litros livres por minuto).</summary>
        public static float VazaoCompressor { get; private set; }
        /// <summary>Pressão nas câmaras de freio de serviço (bar).</summary>
        public static float PressaoCamaras { get; private set; }
        /// <summary>Freio que chega às rodas, 0..1 (o que o VPP recebe no lugar do pedal).</summary>
        public static float FreioEfetivo { get; private set; }
        /// <summary>Pressão nas câmaras de mola (bar). Alta = molas soltas.</summary>
        public static float PressaoMolas { get; private set; }
        /// <summary>Quanto o freio de molas está aplicado, 0..1.</summary>
        public static float FreioMolas { get; private set; }
        public static bool Alarme { get; private set; }
        /// <summary>Ar saindo para a atmosfera agora (litros livres por segundo): freios soltando, purga.</summary>
        public static float Escape { get; private set; }
        /// <summary>Parte do <see cref="Escape"/> que vem da purga do secador (litros livres por segundo).</summary>
        public static float PurgaEnvelope => _purga;
        /// <summary>Reservatório em litros (para converter consumo).</summary>
        public static float VolumeLitros => VolumeTanque?.Value ?? 100f;

        /// <summary>"purga" (governador cortou), "molas" (freio de estacionamento aplicado), "alarme".</summary>
        public static event Action<string> Evento;

        /// <summary>Tira ar do reservatório (litros livres a 1 bar). Para mods que usam ar: portas, bolsas...</summary>
        public static void Gastar(string fonte, float litrosLivres)
        {
            if (!Simulando || litrosLivres <= 0f) return;
            Pressao = Mathf.Max(0f, Pressao - litrosLivres / VolumeLitros);
            _gastoFonte.TryGetValue(fonte, out var g);
            _gastoFonte[fonte] = g + litrosLivres;
        }

        /// <summary>Fração de pressão disponível para um consumidor que precisa de "minima" bar e funciona cheio com "cheia".</summary>
        public static float Disponivel(float minima, float cheia) =>
            !Simulando ? 1f : Mathf.Clamp01((Pressao - minima) / Mathf.Max(0.01f, cheia - minima));

        // Pedido do motorista no freio, lido no Brakes do VPP antes de o trocarmos (-1 = sem leitura).
        internal static float PedalLido = -1f;
        static float _pedalEm;
        /// <summary>Leitura do pedal ainda vale (o patch do freio rodou há pouco)?</summary>
        internal static bool PedalFresco => PedalLido >= 0f && Time.fixedTime - _pedalEm < 0.25f;
        static AudioSource _calado; // o chiado aleatório do jogo que nós silenciamos, para devolver o som

        static int _geracao = -1;
        static float _ultimoPasso, _contagemDesde; static int _passos;
        /// <summary>Diagnóstico: quantas vezes por segundo a simulação roda.</summary>
        public static float PassosPorSegundo { get; private set; }
        static bool _descarregado;
        static float _purga;                  // envelope da purga (litros/s)
        static readonly Dictionary<string, float> _gastoFonte = new();
        static readonly Dictionary<IntPtr, bool> _portas = new();
        static int _rodas = 4, _rodasTras = 2;
        static bool _alarmeAntes;

        internal static bool AtuaNoFreio => Simulando && FreioAAr.Value && Veiculo.Fisica == FisicaVeiculo.Vpp;
        internal static bool AtuaNasMolas => Simulando && FreioDeMolas.Value && Veiculo.Fisica == FisicaVeiculo.Vpp;

        static void NovoVeiculo()
        {
            _geracao = Veiculo.Geracao;
            Pressao = Mathf.Max(0f, PressaoInicial.Value);
            PressaoCamaras = 0f;
            PressaoMolas = Veiculo.FreioDeMao ? 0f : Pressao;
            _descarregado = Pressao >= PressaoCorte;
            _purga = 0f;
            _portas.Clear();
            _gastoFonte.Clear();
            PedalLido = -1f;
            // Rodas freadas: WheelColliders do VPP no veículo; molas só nos eixos de trás.
            int n = 0;
            if (Veiculo.Raiz != null) n = Veiculo.Raiz.GetComponentsInChildren<WheelCollider>().Length;
            _rodas = n > 0 ? n : 4;
            _rodasTras = Mathf.Max(2, _rodas - 2);
        }

        // Passo de física (OnFixedUpdate). O pedal vem do Brakes do passo anterior (20 ms de atraso, menor que o
        // atraso pneumático que estamos simulando).
        internal static void Passo()
        {
            if (!Simulando) { Zerar(); return; }
            if (_geracao != Veiculo.Geracao) { NovoVeiculo(); _ultimoPasso = Time.fixedTime; }
            // Integra pelo tempo de física que passou desde a última chamada: não depende de quantas vezes por
            // segundo o MelonLoader chama OnFixedUpdate.
            float dt = Mathf.Clamp(Time.fixedTime - _ultimoPasso, 0f, 0.25f);
            _ultimoPasso = Time.fixedTime;
            _passos++;
            if (Time.unscaledTime - _contagemDesde >= 1f) { PassosPorSegundo = _passos / (Time.unscaledTime - _contagemDesde); _passos = 0; _contagemDesde = Time.unscaledTime; }
            if (dt <= 0f) return;
            float V = Mathf.Max(10f, VolumeLitros);

            // Compressor: cilindrada x rotação x eficiência volumétrica (cai com a pressão de descarga).
            float rpm = Veiculo.MotorLigado ? Mathf.Max(0f, Veiculo.Rpm) : 0f;
            if (Pressao >= PressaoCorte && !_descarregado)
            {
                _descarregado = true;
                _purga = 3.5f; // o secador solta o ar do copo de uma vez
                Disparar("purga");
            }
            else if (Pressao <= Mathf.Min(Retorno.Value, PressaoCorte - 0.2f)) _descarregado = false;
            CompressorCarregando = !_descarregado && rpm > 100f;
            float ef = 0.85f * Mathf.Max(0f, 1f - Pressao / 13f);
            float q = CompressorCarregando ? Cilindrada.Value * rpm / 60f * ef : 0f; // litros livres por segundo
            VazaoCompressor = q * 60f;
            Pressao += q * dt / V;

            // Vazamento.
            Pressao = Mathf.Max(0f, Pressao - Vazamento.Value / 60f * dt);

            // Freio de serviço: a válvula de pedal pede pressão proporcional ao curso; não passa do reservatório.
            float pedal = PedalFresco ? PedalLido : Veiculo.Freio;
            float vc = CamaraPorRoda * _rodas;
            float alvo = Mathf.Min(pedal * PressaoFreioCheio, Pressao);
            float tau = alvo > PressaoCamaras ? 0.15f : 0.25f;
            float novo = PressaoCamaras + (alvo - PressaoCamaras) * (1f - Mathf.Exp(-dt / tau));
            float escape = 0f;
            if (novo > PressaoCamaras) Pressao = Mathf.Max(0f, Pressao - vc * (novo - PressaoCamaras) / V);
            else escape += vc * (PressaoCamaras - novo) / dt;
            PressaoCamaras = novo;
            FreioEfetivo = FreioAAr.Value ? Mathf.Clamp01(PressaoCamaras / PressaoFreioCheio) : pedal;

            // Freio de molas: com o freio de estacionamento solto, as câmaras de mola enchem do reservatório;
            // puxar o freio (ou faltar ar) esvazia e as molas aplicam.
            float vm = MolaPorRoda * _rodasTras;
            float alvoMolas = Veiculo.FreioDeMao ? 0f : Pressao;
            float tauM = alvoMolas > PressaoMolas ? 0.6f : 0.3f;
            float novoM = PressaoMolas + (alvoMolas - PressaoMolas) * (1f - Mathf.Exp(-dt / tauM));
            if (novoM > PressaoMolas) Pressao = Mathf.Max(0f, Pressao - vm * (novoM - PressaoMolas) / V);
            else
            {
                float saiu = vm * (PressaoMolas - novoM) / dt;
                if (saiu > 2f && PressaoMolas > MolaSolta && novoM <= MolaSolta) Disparar("molas");
                escape += saiu;
            }
            PressaoMolas = novoM;
            FreioMolas = FreioDeMolas.Value ? Mathf.Clamp01((MolaSolta - PressaoMolas) / (MolaSolta - MolaAplicada)) : 0f;

            _purga *= Mathf.Exp(-dt / 0.45f);
            Escape = escape + _purga;

            Alarme = Veiculo.EletricaLigada && Pressao < PressaoAlarme.Value;
            if (Alarme && !_alarmeAntes) Disparar("alarme");
            _alarmeAntes = Alarme;
        }

        // Por frame: portas (cada abrir/fechar gasta ar) e o chiado aleatório do jogo.
        internal static void Quadro()
        {
            if (!Simulando) return;
            foreach (var p in Veiculo.Portas)
            {
                if (p.Jogo == null) continue;
                var k = p.Jogo.Pointer;
                if (_portas.TryGetValue(k, out var antes) && antes != p.Aberta) Gastar("portas", ConsumoPorta.Value);
                _portas[k] = p.Aberta;
            }
            CalarArFalso(SilenciarArFalso.Value);
        }

        static void Zerar()
        {
            CompressorCarregando = false; VazaoCompressor = 0f; Escape = 0f; Alarme = false; FreioEfetivo = 0f; FreioMolas = 0f;
            _geracao = -1;
            PedalLido = -1f;
            CalarArFalso(false);
        }

        // Só desfaz o mute que nós pusemos: se o próprio jogo calou o som, ele continua calado.
        static void CalarArFalso(bool calar)
        {
            try
            {
                if (calar)
                {
                    var s = Veiculo.Controles?.ASsomFakeAir;
                    if (s != null && !s.mute) { s.mute = true; _calado = s; }
                }
                else if (_calado != null)
                {
                    _calado.mute = false;
                    _calado = null;
                }
            }
            catch { _calado = null; }
        }

        static void Disparar(string e)
        {
            try { Evento?.Invoke(e); } catch (Exception ex) { MelonLogger.Warning("[Ar] evento " + e + ": " + ex.Message); }
        }

        // Teste automático: põe o reservatório numa pressão (as molas seguem pela dinâmica normal).
        internal static void DefinirPressao(float bar)
        {
            Pressao = Mathf.Max(0f, bar);
            _descarregado = Pressao >= PressaoCorte;
        }

        internal static void LerPedal(float pedal)
        {
            PedalLido = pedal;
            _pedalEm = Time.fixedTime;
        }

        public static string Resumo()
        {
            if (!Simulando) return "ar: simulação desligada\n";
            var g = string.Join(", ", System.Linq.Enumerable.Select(_gastoFonte, kv => $"{kv.Key} {kv.Value:0} L"));
            return $"ar: reservatório {Pressao:0.00} bar ({VolumeLitros:0} L), compressor {(CompressorCarregando ? "carregando" : "parado")} {VazaoCompressor:0} L/min, " +
                   $"câmaras {PressaoCamaras:0.00} bar -> freio {FreioEfetivo:0.00}, molas {PressaoMolas:0.00} bar -> aplicado {FreioMolas:0.00}, " +
                   $"escape {Escape:0.0} L/s, alarme {Alarme}, {_rodas} rodas, {PassosPorSegundo:0} passos/s (fixedDeltaTime {Time.fixedDeltaTime:0.0000}); gasto: {(g.Length > 0 ? g : "nada")}\n";
        }
    }

    // Portas pneumáticas: sem pressão o comando não faz nada (nem o som da porta, que AbreFechaPorta* toca antes de
    // MEPCadaPortaBus.SetPodeAnimar inverter tahAberta). AbreFechaTodasAsPortas chama estas 12.
    [HarmonyPatch]
    internal static class PatchPortasSemAr
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var lado in new[] { "Dir", "Esq" })
                for (int i = 1; i <= 6; i++)
                {
                    var m = AccessTools.Method(typeof(Il2Cpp.MEPCtrlPortasPeds), "AbreFechaPorta" + lado + i);
                    if (m != null) yield return m;
                }
        }

        static float _avisouEm = -10f;

        static bool Prefix()
        {
            float min = Ar.PressaoPortas?.Value ?? 0f;
            if (!Ar.Simulando || min <= 0f || Ar.Pressao >= min) return true;
            if (Time.unscaledTime - _avisouEm > 0.5f) // "todas as portas" chama as 12 de uma vez: um aviso só
            {
                _avisouEm = Time.unscaledTime;
                Avisos.Notificar("PBSLoaderCommonUtils", "portas-sem-ar", Gravidade.Aviso, "Portas sem ar",
                    $"{Ar.Pressao:0.0} bar · precisam de {min:0.0} bar", 4f);
            }
            return false;
        }
    }

    // Antes de o VPP aplicar o freio: o pedido do motorista vira a pressão das câmaras, e o freio de molas soma
    // no freio de mão. Brakes.DoUpdate roda dentro de DoUpdateBlocks, depois de o pedal ser copiado do barramento.
    [HarmonyPatch(typeof(Il2CppVehiclePhysics.Brakes), nameof(Il2CppVehiclePhysics.Brakes.DoUpdate))]
    internal static class PatchFreio
    {
        static void Prefix(Il2CppVehiclePhysics.Brakes __instance)
        {
            if (!Ar.Simulando || !Veiculo.FreioDoJogador(__instance.Pointer)) return;
            Ar.LerPedal(__instance.brakeInput);
            if (Ar.AtuaNoFreio) __instance.brakeInput = Ar.FreioEfetivo;
            if (Ar.AtuaNasMolas && Ar.FreioMolas > __instance.handbrakeInput) __instance.handbrakeInput = Ar.FreioMolas;
        }
    }
}
