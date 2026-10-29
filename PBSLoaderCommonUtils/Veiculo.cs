using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Il2CppMep;
using Il2CppVehiclePhysics;
using UnityEngine;
using VPVehicleController = Il2Cpp.VPVehicleController;

namespace PBSLoaderCommonUtils
{
    public enum TipoVeiculo { Nenhum, Onibus, Articulado, Biarticulado, Trem }
    /// <summary>Física que move o veículo no jogo. O freio a ar só atua em VPP.</summary>
    public enum FisicaVeiculo { Desconhecida, Evp5, Vpp, Trem }
    public enum Farol { Desligado, Baixo, Alto }
    public enum Seta { Nenhuma, Esquerda, Direita, Alerta }
    public enum Lado { Direito, Esquerdo }

    /// <summary>Uma porta do veículo. "Numero" é o do jogo (R1..R6 / L1..L6).</summary>
    public sealed class Porta
    {
        public Lado Lado { get; internal set; }
        public int Numero { get; internal set; }
        public bool Aberta { get; internal set; }
        public bool Entrada { get; internal set; }
        public bool Saida { get; internal set; }
        public bool TemRampa { get; internal set; }
        /// <summary>Componente do jogo, para quem precisar de mais.</summary>
        public Il2Cpp.MEPCadaPortaBus Jogo { get; internal set; }
        public override string ToString() => (Lado == Lado.Direito ? "R" : "L") + Numero + (Aberta ? " aberta" : " fechada");
    }

    /// <summary>
    /// Metadados do veículo que o jogador dirige, lidos a cada frame do MEPControlesBus e do Vehicle Physics Pro.
    /// Tudo é só leitura e vale "agora". Sem veículo (menus), <see cref="Existe"/> é false e o resto fica zerado.
    ///
    /// Pedais e volante são o que o motorista está pedindo (barramento de entrada do VPP, 0..1, volante -1..1),
    /// antes de câmbio, piloto automático e freio a ar. Em veículos sem VPP (EVP5) vêm dos campos do jogo.
    /// </summary>
    public static class Veiculo
    {
        // ---------- identidade ----------
        public static bool Existe { get; private set; }
        /// <summary>Nome do ônibus escolhido, como o jogo guarda (MepMobileControls.strNomeDoBusAtual).</summary>
        public static string Nome { get; private set; } = "";
        public static TipoVeiculo Tipo { get; private set; }
        public static FisicaVeiculo Fisica { get; private set; }
        public static Transform Raiz { get; private set; }
        /// <summary>Muda a cada veículo carregado (para quem guarda estado por veículo).</summary>
        public static int Geracao { get; private set; }
        public static MEPControlesBus Controles { get; private set; }
        /// <summary>Controlador VPP que tem o motor (null em EVP5).</summary>
        public static VPVehicleController Vpp { get; private set; }
        /// <summary>Todos os controladores VPP do veículo (articulados têm mais de um).</summary>
        public static IReadOnlyList<VPVehicleController> TodosVpp => _vpps;
        public static float Massa { get; private set; }
        /// <summary>Caixa do veículo no espaço local da raiz (sem retrovisores soltos e outliers).</summary>
        public static Bounds Caixa => CaixaLocal();

        // ---------- movimento ----------
        public static float VelocidadeKmh { get; private set; }
        /// <summary>Sinal pela direção do movimento em relação à frente (negativo = de ré).</summary>
        public static float VelocidadeSinalKmh { get; private set; }
        public static bool Parado { get; private set; }
        public static float OdometroSessaoKm { get; private set; }
        public static float OdometroTotalKm { get; private set; }

        // ---------- motor e câmbio ----------
        /// <summary>Elétrica (chave) ligada.</summary>
        public static bool EletricaLigada { get; private set; }
        public static bool MotorLigado { get; private set; }
        public static float Rpm { get; private set; }
        public static float RpmLenta { get; private set; }
        public static float RpmMax { get; private set; }
        /// <summary>Carga do motor 0..1 (VPP).</summary>
        public static float CargaMotor { get; private set; }
        public static float TorqueMotorNm { get; private set; }
        /// <summary>Marcha engatada: -1 ré, 0 neutro, 1.. à frente.</summary>
        public static int Marcha { get; private set; }
        public static string MarchaTexto => Marcha < 0 ? "R" : Marcha == 0 ? "N" : Marcha.ToString(CultureInfo.InvariantCulture);
        public static bool TrocandoMarcha { get; private set; }
        public static bool CambioManual { get; private set; }
        public static bool CambioAutomatizado { get; private set; }
        public static int RetarderNivel { get; private set; }
        public static int RetarderNiveis { get; private set; }

        // ---------- pedais e volante (pedido do motorista) ----------
        public static float Acelerador { get; private set; }
        public static float Freio { get; private set; }
        /// <summary>Embreagem como o VPP recebe (ver README: sentido medido no jogo).</summary>
        public static float Embreagem { get; private set; }
        public static float FreioDeMaoPedido { get; private set; }
        public static float Volante { get; private set; }
        public static bool FreioDeMao { get; private set; }

        // ---------- ajudas ----------
        public static bool PilotoAutomatico { get; private set; }
        public static bool TemLimitador { get; private set; }
        public static bool LimitadorAtivo { get; private set; }
        public static float LimiteKmh { get; private set; }
        public static bool AbsAtuando { get; private set; }
        public static bool TracaoAtuando { get; private set; }

        // ---------- luzes e sinais ----------
        public static Farol Farol { get; private set; }
        public static Seta Seta { get; private set; }
        /// <summary>Fase do pisca (setas e alerta), a mesma que o jogo usa nas luzes e no som.</summary>
        public static bool PiscaAceso { get; private set; }
        public static bool LuzDeRe { get; private set; }
        public static bool LuzDeFreio { get; private set; }
        /// <summary>Luzes internas 1..3 acesas (bit 0 = luz 1).</summary>
        public static int LuzesInternas { get; private set; }
        public static bool Buzinando { get; private set; }
        /// <summary>0 parado, 1 normal, 2 rápido.</summary>
        public static int Limpadores { get; private set; }
        public static bool ParadaSolicitada { get; private set; }

        // ---------- portas ----------
        public static int PortasDireitas { get; private set; }
        public static int PortasEsquerdas { get; private set; }
        public static bool TemPortaEsquerda => PortasEsquerdas > 0;
        public static bool AlgumaPortaAberta { get; private set; }
        public static IReadOnlyList<Porta> Portas => _portas;

        // ---------- conforto ----------
        public static bool TemArCondicionado { get; private set; }
        public static bool ArCondicionadoLigado { get; private set; }
        public static int ArCondicionadoVentilador { get; private set; }
        public static float TemperaturaInterna { get; private set; }
        public static int TemperaturaAlvo { get; private set; }
        public static bool JanelaMotoristaAberta { get; private set; }
        public static bool TemCobrador { get; private set; }
        public static int PassageirosSessao { get; private set; }

        // ---------- suspensão a ar (informada pelo mod SuspensaoAr) ----------
        /// <summary>Algum mod de suspensão a ar está controlando este veículo.</summary>
        public static bool TemSuspensaoAr => _suspensaoGeracao == Geracao && _suspensaoNivel != null;
        /// <summary>"normal", "ajoelhado", "elevado"... (texto do mod que informou).</summary>
        public static string NivelSuspensao => TemSuspensaoAr ? _suspensaoNivel : "";
        /// <summary>Altura em relação ao normal, em metros (negativo = baixou).</summary>
        public static float AlturaSuspensao => TemSuspensaoAr ? _suspensaoAltura : 0f;

        /// <summary>Um mod de suspensão a ar informa o estado (chame quando mudar ou a cada frame).</summary>
        public static void InformarSuspensao(string nivel, float alturaM)
        {
            _suspensaoNivel = nivel; _suspensaoAltura = alturaM; _suspensaoGeracao = Geracao;
        }

        static string _suspensaoNivel; static float _suspensaoAltura; static int _suspensaoGeracao = -1;

        // ---------- estado interno ----------
        static readonly List<VPVehicleController> _vpps = new();
        static readonly List<Porta> _portas = new();
        static readonly HashSet<IntPtr> _freiosDoJogador = new();
        static IntPtr _ctrlPtr;
        static float _proximaBusca;
        internal static bool BuzinaPedida;

        /// <summary>Este Brakes do VPP é do veículo do jogador (para o freio a ar).</summary>
        internal static bool FreioDoJogador(IntPtr brakes) => _freiosDoJogador.Contains(brakes);

        internal static void CenaMudou()
        {
            Zerar();
            _ctrlPtr = IntPtr.Zero;
        }

        static void Zerar()
        {
            Existe = false; Controles = null; Vpp = null; Raiz = null; Nome = "";
            Tipo = TipoVeiculo.Nenhum; Fisica = FisicaVeiculo.Desconhecida;
            _vpps.Clear(); _portas.Clear(); _freiosDoJogador.Clear(); _caixaDe = null;
            BuzinaPedida = false;
        }

        internal static void Atualizar()
        {
            var c = MEPControlesBus.instance;
            if (c == null || c.WasCollected)
            {
                if (Existe) Zerar();
                _ctrlPtr = IntPtr.Zero;
                return;
            }
            if (c.Pointer != _ctrlPtr)
            {
                Zerar();
                _ctrlPtr = c.Pointer;
                Controles = c;
                Geracao++;
                _proximaBusca = 0f;
            }
            // Peças (controladores, portas, massa) a cada 2 s: alguns mods montam o ônibus aos poucos.
            if (Time.unscaledTime >= _proximaBusca)
            {
                _proximaBusca = Time.unscaledTime + 2f;
                Buscar(c);
            }
            if (!Existe)
                MelonLoader.MelonLogger.Msg($"[Veiculo] {Nome} ({Tipo}, física {Fisica}, {_vpps.Count} VPP, {_portas.Count} portas), geração {Geracao}");
            Existe = true;
            Ler(c);
        }

        static void Buscar(MEPControlesBus c)
        {
            Raiz = c.transform.root;
            try { Nome = Il2Cpp.MepMobileControls.strNomeDoBusAtual ?? ""; } catch { Nome = ""; }

            _vpps.Clear();
            var vistos = new HashSet<IntPtr>();
            void Add(VPVehicleController v) { if (v != null && vistos.Add(v.Pointer)) _vpps.Add(v); }
            Add(c.scriptMotorVPP); Add(c.scriptDirigivelVP); Add(c.scriptDirigivelVPTracao);
            var lista = c.listaVPVehicleControllers;
            if (lista != null) foreach (var v in lista) Add(v);
            if (_vpps.Count == 0 && c.scriptCarroTipo == MEPControlesBus.tipoControleCarro.VPPRO)
                foreach (var v in Raiz.GetComponentsInChildren<VPVehicleController>()) Add(v);

            Vpp = null;
            foreach (var v in new[] { c.scriptMotorVPP, c.scriptDirigivelVP, c.scriptDirigivelVPTracao })
                if (v != null && v.m_engine != null) { Vpp = v; break; }
            if (Vpp == null) foreach (var v in _vpps) if (v.m_engine != null) { Vpp = v; break; }

            _freiosDoJogador.Clear();
            foreach (var v in _vpps)
            {
                try { var b = v.m_brakes; if (b != null) _freiosDoJogador.Add(b.Pointer); } catch { }
            }

            Fisica = c.scriptDirigivelTrem != null ? FisicaVeiculo.Trem
                : c.scriptCarroTipo == MEPControlesBus.tipoControleCarro.VPPRO ? FisicaVeiculo.Vpp
                : c.scriptCarroTipo == MEPControlesBus.tipoControleCarro.EVP5 ? FisicaVeiculo.Evp5
                : FisicaVeiculo.Desconhecida;
            Tipo = Fisica == FisicaVeiculo.Trem ? TipoVeiculo.Trem
                : c.ehBiarticulado ? TipoVeiculo.Biarticulado
                : c.ehArticulado ? TipoVeiculo.Articulado
                : TipoVeiculo.Onibus;

            float massa = 0f;
            foreach (var rb in Raiz.GetComponentsInChildren<Rigidbody>()) if (!rb.isKinematic) massa += rb.mass;
            Massa = massa;

            // Portas: cada MEPCadaPortaBus "mestre" (as escravas acompanham a mestre).
            var portas = Raiz.GetComponentsInChildren<Il2Cpp.MEPCadaPortaBus>(true);
            var escravas = new HashSet<IntPtr>();
            foreach (var p in portas)
            {
                var s = p.minhasPortasSlaves;
                if (s != null) foreach (var x in s) if (x != null) escravas.Add(x.Pointer);
            }
            var novas = new List<Porta>();
            foreach (var p in portas)
            {
                if (escravas.Contains(p.Pointer)) continue;
                int id = (int)p.tipoPorta; // R1..R6 = 0..5, L1..L6 = 6..11
                // A mesma porta continua sendo o mesmo objeto Porta (quem guardou a referência segue vendo o estado).
                var porta = _portas.Find(x => x.Jogo != null && x.Jogo.Pointer == p.Pointer) ?? new Porta { Jogo = p };
                porta.Lado = id >= 6 ? Lado.Esquerdo : Lado.Direito; porta.Numero = id % 6 + 1;
                porta.Entrada = p.ehEntrada; porta.Saida = p.ehSaida; porta.TemRampa = p.temRampas; porta.Aberta = p.tahAberta;
                novas.Add(porta);
            }
            novas.Sort((a, b) => a.Lado != b.Lado ? a.Lado.CompareTo(b.Lado) : a.Numero.CompareTo(b.Numero));
            _portas.Clear(); _portas.AddRange(novas);
        }

        static void Ler(MEPControlesBus c)
        {
            VelocidadeKmh = c.speedkmh;
            Parado = c.tahParado;
            OdometroSessaoKm = c.distPercorrida / 1000f;
            OdometroTotalKm = c.distPercorridaTotal / 1000f;
            EletricaLigada = c.onibusLigado;
            MotorLigado = c.motorLigado;
            FreioDeMao = c.freioDeMaoAtivado;
            CambioManual = c.ehCambioManual && !c.ehSoAutomatico;
            CambioAutomatizado = c.ehCambioAutomatizado;
            RetarderNivel = c.currentRetarderLevel;
            RetarderNiveis = c.retarderMaxLevels;
            PilotoAutomatico = c.cruiseControltahAtivado;
            TemLimitador = c.temLimitadorDeVelocidade;
            LimitadorAtivo = c.speedLimitertahAtivado;
            LimiteKmh = c.speedLimit;

            Farol = c.farolAltoAceso ? Farol.Alto : c.farolBaixoAceso ? Farol.Baixo : Farol.Desligado;
            Seta = c.piscaAlertaAtivado ? Seta.Alerta : c.setaEsquerdaAtivada ? Seta.Esquerda : c.setaDireitaAtivada ? Seta.Direita : Seta.Nenhuma;
            PiscaAceso = c.luzPiscando;
            LuzDeRe = c.luzDeReAtivada;
            LuzDeFreio = c.luzDeFreioMaisForteAtivada;
            LuzesInternas = (c.luzInterna1Ativada ? 1 : 0) | (c.luzInterna2Ativada ? 2 : 0) | (c.luzInterna3Ativada ? 4 : 0);
            Limpadores = !c.limpadoresAtivados ? 0 : c.limpadoresRapidosLigados ? 2 : 1;
            ParadaSolicitada = c.paradasolTahAcesa;
            try { var b = c.ASsomBuzina; Buzinando = BuzinaPedida || (b != null && b.isPlaying && b.loop); }
            catch { Buzinando = BuzinaPedida; }

            PortasDireitas = c.qtdePortasDireitas;
            PortasEsquerdas = c.qtdePortasEsquerdas;
            AlgumaPortaAberta = c.temPortaAberta;
            foreach (var p in _portas)
            {
                try { p.Aberta = p.Jogo != null && !p.Jogo.WasCollected && p.Jogo.tahAberta; } catch { p.Aberta = false; }
            }

            TemArCondicionado = c.temArCondicionado;
            ArCondicionadoLigado = c.arCondicionadoLigado;
            ArCondicionadoVentilador = c.arCondicionadoNivelVentilador;
            TemperaturaInterna = c.temperaturaInterna;
            TemperaturaAlvo = c.temperaturaAlvo;
            JanelaMotoristaAberta = c.janelaMotoristaAberta;
            TemCobrador = c.temCobrador;
            PassageirosSessao = c.qtdeCatracadosSessao;

            var raiz = Raiz;
            if (raiz != null)
            {
                var rb = c.GetComponentInParent<Rigidbody>();
                if (rb != null)
                {
                    float vz = Vector3.Dot(rb.velocity, raiz.forward);
                    VelocidadeSinalKmh = vz * 3.6f;
                }
                else VelocidadeSinalKmh = VelocidadeKmh;
            }

            var vp = Vpp;
            if (vp != null && !vp.WasCollected && vp.data != null)
            {
                try
                {
                    var ent = vp.data.Get(Channel.Input);
                    var vei = vp.data.Get(Channel.Vehicle);
                    Acelerador = Mathf.Clamp01(ent[InputData.Throttle] / 10000f);
                    Freio = Mathf.Clamp01(ent[InputData.Brake] / 10000f);
                    Embreagem = Mathf.Clamp01(ent[InputData.Clutch] / 10000f);
                    FreioDeMaoPedido = Mathf.Clamp01(ent[InputData.Handbrake] / 10000f);
                    Volante = Mathf.Clamp(ent[InputData.Steer] / 10000f, -1f, 1f);
                    Rpm = vei[VehicleData.EngineRpm] / 1000f;
                    CargaMotor = Mathf.Clamp01(vei[VehicleData.EngineLoad] / 1000f);
                    TorqueMotorNm = vei[VehicleData.EngineTorque] / 1000f;
                    Marcha = vei[VehicleData.GearboxGear];
                    TrocandoMarcha = vei[VehicleData.GearboxShifting] != 0;
                    AbsAtuando = vei[VehicleData.AbsEngaged] != 0;
                    TracaoAtuando = vei[VehicleData.TcsEngaged] != 0 || vei[VehicleData.AsrEngaged] != 0;
                    var s = vp.m_engine?.settings;
                    if (s != null) { RpmLenta = s.idleRpm; RpmMax = s.maxRpm; }
                    // O freio a ar guarda o pedido do motorista antes de mudar o freio do VPP.
                    if (Ar.PedalFresco) Freio = Ar.PedalLido;
                    return;
                }
                catch { /* cai nos campos do jogo */ }
            }
            Acelerador = Mathf.Clamp01(c.busAcelerador);
            Freio = Mathf.Clamp01(c.busFreio);
            FreioDeMaoPedido = Mathf.Clamp01(c.busFreioDeMao);
            Embreagem = 0f;
            Volante = Mathf.Clamp(c.busSteer, -1f, 1f);
            Rpm = c.busRPM;
            RpmMax = c.busMaxRPM;
            Marcha = c.ehCambioManual ? c.busMarchaManual : c.busMarchaAutomatica;
            TrocandoMarcha = c.tahTrocandoDeMarcha;
        }

        // ---------- caixa e câmera ----------

        static Transform _caixaDe;
        static Bounds _caixa;

        static Bounds CaixaLocal()
        {
            var raiz = Raiz;
            if (raiz == null) return default;
            if (raiz == _caixaDe) return _caixa;
            bool tem = false; var b = new Bounds();
            foreach (var r in raiz.GetComponentsInChildren<MeshRenderer>())
            {
                var rb = r.bounds;
                if (rb.size.magnitude > 25f || rb.size.magnitude < 0.05f) continue;
                for (int i = 0; i < 8; i++)
                {
                    var canto = new Vector3((i & 1) == 0 ? rb.min.x : rb.max.x, (i & 2) == 0 ? rb.min.y : rb.max.y, (i & 4) == 0 ? rb.min.z : rb.max.z);
                    var l = raiz.InverseTransformPoint(canto);
                    if (!tem) { b = new Bounds(l, Vector3.zero); tem = true; } else b.Encapsulate(l);
                }
            }
            if (!tem) b = new Bounds(new Vector3(0f, 1.6f, 0f), new Vector3(2.5f, 3.2f, 12f));
            _caixa = b; _caixaDe = raiz;
            return b;
        }

        /// <summary>O ponto (mundo) está dentro da carroceria (cabine ou salão).</summary>
        public static bool Dentro(Vector3 mundo)
        {
            var raiz = Raiz;
            if (raiz == null) return false;
            var c = CaixaLocal();
            var p = raiz.InverseTransformPoint(mundo);
            float mx = Mathf.Min(0.45f, c.extents.x * 0.3f);
            return Mathf.Abs(p.x - c.center.x) < c.extents.x - mx && p.y > c.min.y && p.y < c.max.y
                && Mathf.Abs(p.z - c.center.z) < c.extents.z - 0.3f;
        }

        /// <summary>A câmera atual está dentro do veículo.</summary>
        public static bool CameraDentro
        {
            get { var cam = Camera.main; return cam != null && Dentro(cam.transform.position); }
        }

        /// <summary>Tudo em texto (diagnóstico).</summary>
        public static string Resumo()
        {
            if (!Existe) return "sem veículo";
            var sb = new StringBuilder();
            sb.Append($"{Nome} ({Tipo}, física {Fisica}, {_vpps.Count} controlador(es) VPP, massa {Massa:0} kg, caixa {Caixa.size})\n");
            sb.Append($"velocidade {VelocidadeKmh:0.0} km/h (sinal {VelocidadeSinalKmh:0.0}), parado {Parado}, odômetro {OdometroSessaoKm:0.00}/{OdometroTotalKm:0.0} km\n");
            sb.Append($"elétrica {EletricaLigada}, motor {MotorLigado}, {Rpm:0} rpm (lenta {RpmLenta:0}, máx {RpmMax:0}), carga {CargaMotor:0.00}, torque {TorqueMotorNm:0} Nm\n");
            sb.Append($"marcha {MarchaTexto} (trocando {TrocandoMarcha}), manual {CambioManual}, automatizado {CambioAutomatizado}, retarder {RetarderNivel}/{RetarderNiveis}\n");
            sb.Append($"acelerador {Acelerador:0.00}, freio {Freio:0.00}, embreagem {Embreagem:0.00}, freio de mão {FreioDeMao} (pedido {FreioDeMaoPedido:0.00}), volante {Volante:0.00}\n");
            sb.Append($"piloto automático {PilotoAutomatico}, limitador {TemLimitador}/{LimitadorAtivo} {LimiteKmh:0} km/h, ABS {AbsAtuando}, tração {TracaoAtuando}\n");
            sb.Append($"farol {Farol}, seta {Seta}, ré {LuzDeRe}, luz de freio {LuzDeFreio}, internas {LuzesInternas}, buzina {Buzinando}, limpadores {Limpadores}, parada solicitada {ParadaSolicitada}\n");
            sb.Append($"portas: {PortasDireitas} direita(s), {PortasEsquerdas} esquerda(s), alguma aberta {AlgumaPortaAberta}: {string.Join(", ", _portas)}\n");
            sb.Append($"ar-condicionado {TemArCondicionado}/{ArCondicionadoLigado} ventilador {ArCondicionadoVentilador}, {TemperaturaInterna:0.0} °C (alvo {TemperaturaAlvo}), janela {JanelaMotoristaAberta}, cobrador {TemCobrador}, passageiros {PassageirosSessao}\n");
            sb.Append($"suspensão a ar {TemSuspensaoAr} {NivelSuspensao} {AlturaSuspensao:+0.000;-0.000} m, câmera dentro {CameraDentro}\n");
            sb.Append(Ar.Resumo());
            return sb.ToString();
        }
    }

    // A buzina passa por MEPControlesBus.TocaBuzina(bool): o pedido exato, sem depender do som.
    [HarmonyPatch(typeof(MEPControlesBus), nameof(MEPControlesBus.TocaBuzina))]
    internal static class PatchBuzina
    {
        static void Postfix(MEPControlesBus __instance, bool ehPraTocar)
        {
            if (__instance.Pointer == Veiculo.Controles?.Pointer) Veiculo.BuzinaPedida = ehPraTocar;
        }
    }
}
