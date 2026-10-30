using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Il2CppMep;
using Il2CppVehiclePhysics;
using PBSLoaderCommonUtils.Simulacao;
using UnityEngine;
using VPVehicleController = Il2Cpp.VPVehicleController;

namespace PBSLoaderCommonUtils.Veiculos
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

    /// <summary>Uma roda do veículo (VPP). O mesmo objeto segue a mesma roda enquanto o veículo existir.</summary>
    public sealed class Roda
    {
        /// <summary>Lado pela posição em relação ao centro da carroceria.</summary>
        public Lado Lado { get; internal set; }
        /// <summary>Eixo, da frente para trás (0 = dianteiro). Rodas duplas do mesmo lado têm o mesmo eixo.</summary>
        public int Eixo { get; internal set; }
        /// <summary>Posição de montagem no espaço local da raiz do veículo.</summary>
        public Vector3 PosicaoLocal { get; internal set; }
        public bool Esterca => _estado != null && Vivo && _estado.steerable;
        public bool NoChao => _estado != null && Vivo && _estado.grounded;
        /// <summary>Ângulo de esterço agora, em graus.</summary>
        public float AnguloGraus => _estado != null && Vivo ? _estado.steerAngle : 0f;
        /// <summary>Componente do jogo (VPP), para quem precisar de mais.</summary>
        public Il2Cpp.VPWheelCollider Jogo { get; internal set; }
        /// <summary>Controlador VPP dono da roda (articulados têm mais de um).</summary>
        public VPVehicleController Controlador { get; internal set; }

        internal VehicleBase.WheelState _estado;
        bool Vivo => Jogo != null && !Jogo.WasCollected;
        public override string ToString() => $"eixo {Eixo} {(Lado == Lado.Direito ? "R" : "L")}{(Esterca ? " esterça" : "")}";
    }

    public enum PosicaoEspelho { Esquerdo, Direito, Meio }

    /// <summary>Um dos três espelhos que o jogo renderiza (câmera + material que mostra a imagem).</summary>
    public sealed class Espelho
    {
        public PosicaoEspelho Posicao { get; internal set; }
        public UnityEngine.Camera Camera { get; internal set; }
        public Material Material { get; internal set; }
        /// <summary>Objeto da câmera no jogo (MEPControlesBus.camEsp*).</summary>
        public GameObject Objeto { get; internal set; }
        public override string ToString() => $"{Posicao}: câmera {(Camera != null ? Camera.name : "-")}, material {(Material != null ? Material.name : "-")}";
    }

    /// <summary>
    /// Veículo que o jogador dirige, lido a cada quadro do MEPControlesBus e do Vehicle Physics Pro.
    /// Tudo é só leitura e vale "agora". Sem veículo (menus), <see cref="Existe"/> é false e o resto fica zerado.
    /// Os grupos (<see cref="Motor"/>, <see cref="Cambio"/>, <see cref="Pedais"/>...) seguem a mesma regra.
    /// Os componentes do jogo ficam em <see cref="Avancado"/>.
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
        /// <summary>Muda a cada veículo carregado (para quem guarda estado por veículo). Ver também <see cref="Sdk.VeiculoTrocou"/>.</summary>
        public static int Geracao { get; private set; }
        public static float Massa { get; private set; }
        /// <summary>Caixa do veículo no espaço local da raiz (sem retrovisores soltos e outliers).</summary>
        public static Bounds Caixa => CaixaLocal();
        /// <summary>
        /// Pasta do mod do ônibus (a do .ini, ou a apontada por [config] baseDir dele), ou "" nos ônibus que vêm com o
        /// jogo. É onde ficam modelos, texturas, sons e a pasta scripts/ dos scripts de conteúdo.
        /// </summary>
        public static string Pasta
        {
            get
            {
                if (!Existe) return "";
                if (_pastaDe != Geracao) { _pastaDe = Geracao; _pasta = AcharPasta(); }
                return _pasta;
            }
        }
        static string _pasta = ""; static int _pastaDe = -1;

        static string AcharPasta()
        {
            try
            {
                if (Partida.Sessao.OrigemDoOnibus != Partida.OrigemDoOnibus.Mod) return "";
                var raiz = Partida.Caminhos.ModsDeOnibus;
                var nome = Partida.Sessao.Onibus;
                if (raiz.Length == 0 || nome.Length == 0) return "";
                var arq = System.IO.Path.GetFileName(nome);
                if (!arq.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) arq += ".ini";
                var ini = System.IO.Path.Combine(raiz, arq);
                string baseDir = System.IO.Path.GetFileNameWithoutExtension(arq);
                if (System.IO.File.Exists(ini)) baseDir = ValorIni(ini, "config", "baseDir") ?? baseDir;
                var p = System.IO.Path.GetFullPath(System.IO.Path.Combine(raiz, baseDir));
                return System.IO.Directory.Exists(p) ? p : "";
            }
            catch { return ""; }
        }

        internal static string ValorIni(string arquivo, string secao, string chave)
        {
            string atual = "";
            foreach (var bruta in System.IO.File.ReadAllLines(arquivo, System.Text.Encoding.Latin1))
            {
                var l = bruta.Trim();
                if (l.StartsWith("[") && l.EndsWith("]")) { atual = l.Substring(1, l.Length - 2).Trim(); continue; }
                int i = l.IndexOf('=');
                if (i > 0 && atual.Equals(secao, StringComparison.OrdinalIgnoreCase) && l.Substring(0, i).Trim().Equals(chave, StringComparison.OrdinalIgnoreCase))
                    return l.Substring(i + 1).Trim();
            }
            return null;
        }

        // ---------- movimento ----------
        public static float VelocidadeKmh { get; private set; }
        /// <summary>Sinal pela direção do movimento em relação à frente (negativo = de ré).</summary>
        public static float VelocidadeSinalKmh { get; private set; }
        public static bool Parado { get; private set; }
        public static float OdometroSessaoKm { get; private set; }
        public static float OdometroTotalKm { get; private set; }
        /// <summary>Elétrica (chave) ligada.</summary>
        public static bool EletricaLigada { get; private set; }

        public static class Motor
        {
            public static bool Ligado { get; internal set; }
            public static float Rpm { get; internal set; }
            public static float RpmLenta { get; internal set; }
            public static float RpmMax { get; internal set; }
            /// <summary>Carga do motor 0..1 (VPP).</summary>
            public static float Carga { get; internal set; }
            public static float TorqueNm { get; internal set; }
        }

        public static class Cambio
        {
            /// <summary>Marcha engatada: -1 ré, 0 neutro, 1.. à frente.</summary>
            public static int Marcha { get; internal set; }
            public static string MarchaTexto => Marcha < 0 ? "R" : Marcha == 0 ? "N" : Marcha.ToString(CultureInfo.InvariantCulture);
            public static bool Trocando { get; internal set; }
            public static bool Manual { get; internal set; }
            public static bool Automatizado { get; internal set; }
            public static int RetarderNivel { get; internal set; }
            public static int RetarderNiveis { get; internal set; }
        }

        /// <summary>
        /// O que o motorista está pedindo (barramento de entrada do VPP, 0..1, direção -1..1), antes de câmbio,
        /// piloto automático e freio a ar. Em veículos sem VPP (EVP5) vem dos campos do jogo.
        /// </summary>
        public static class Pedais
        {
            public static float Acelerador { get; internal set; }
            public static float Freio { get; internal set; }
            /// <summary>Embreagem como o VPP recebe (ver README: sentido medido no jogo).</summary>
            public static float Embreagem { get; internal set; }
            /// <summary>Alavanca do freio de mão como o VPP recebe, 0..1.</summary>
            public static float FreioDeMao { get; internal set; }
            /// <summary>Freio de mão puxado no painel do jogo.</summary>
            public static bool FreioDeMaoPuxado { get; internal set; }
            /// <summary>Volante, -1 (esquerda) .. 1 (direita).</summary>
            public static float Direcao { get; internal set; }
        }

        public static class Ajudas
        {
            public static bool PilotoAutomatico { get; internal set; }
            public static bool TemLimitador { get; internal set; }
            public static bool LimitadorAtivo { get; internal set; }
            public static float LimiteKmh { get; internal set; }
            public static bool AbsAtuando { get; internal set; }
            public static bool TracaoAtuando { get; internal set; }
        }

        public static class Luzes
        {
            public static Farol Farol { get; internal set; }
            public static Seta Seta { get; internal set; }
            /// <summary>Fase do pisca (setas e alerta), a mesma que o jogo usa nas luzes e no som.</summary>
            public static bool PiscaAceso { get; internal set; }
            public static bool Re { get; internal set; }
            public static bool Freio { get; internal set; }
            /// <summary>Luzes internas 1..3 acesas (bit 0 = luz 1).</summary>
            public static int Internas { get; internal set; }
        }

        public static class Portas
        {
            public static int Direitas { get; internal set; }
            public static int Esquerdas { get; internal set; }
            public static bool TemEsquerda => Esquerdas > 0;
            public static bool AlgumaAberta { get; internal set; }
            /// <summary>Portas mestras, ordenadas (direitas e depois esquerdas). O mesmo objeto segue a mesma porta.</summary>
            public static IReadOnlyList<Porta> Todas => _portas;
        }

        public static class Cabine
        {
            public static bool Buzinando { get; internal set; }
            /// <summary>0 parado, 1 normal, 2 rápido.</summary>
            public static int Limpadores { get; internal set; }
            public static bool ParadaSolicitada { get; internal set; }
            public static bool TemArCondicionado { get; internal set; }
            public static bool ArCondicionadoLigado { get; internal set; }
            public static int ArCondicionadoVentilador { get; internal set; }
            public static float TemperaturaInterna { get; internal set; }
            public static int TemperaturaAlvo { get; internal set; }
            public static bool JanelaMotoristaAberta { get; internal set; }
            public static bool TemCobrador { get; internal set; }
            /// <summary>Passageiros que passaram na catraca nesta sessão.</summary>
            public static int PassageirosSessao { get; internal set; }
            /// <summary>Passageiros dentro do veículo agora.</summary>
            public static int PassageirosABordo { get; internal set; }
        }

        public static class Rodas
        {
            /// <summary>Todas as rodas, ordenadas por eixo (frente para trás) e lado.</summary>
            public static IReadOnlyList<Roda> Todas => _rodas;
            public static int Eixos { get; internal set; }
        }

        /// <summary>
        /// Os três espelhos que o jogo renderiza. Lidos do jogo a cada acesso (o jogo recria câmeras e
        /// materiais quando o ônibus termina de carregar); null quando o veículo não tem aquele espelho.
        /// </summary>
        public static class Espelhos
        {
            public static Espelho Esquerdo => Ler(PosicaoEspelho.Esquerdo);
            public static Espelho Direito => Ler(PosicaoEspelho.Direito);
            public static Espelho Meio => Ler(PosicaoEspelho.Meio);
            public static IEnumerable<Espelho> Todos
            {
                get
                {
                    for (int i = 0; i < 3; i++) { var e = Ler((PosicaoEspelho)i); if (e != null) yield return e; }
                }
            }

            static readonly Espelho[] _cache = new Espelho[3];

            static Espelho Ler(PosicaoEspelho pos)
            {
                var c = Avancado.ControlesJogo;
                if (c == null || c.WasCollected) return null;
                GameObject go; Material mat;
                switch (pos)
                {
                    case PosicaoEspelho.Esquerdo: go = c.camEspEsq; mat = c.matEspelhoEsq; break;
                    case PosicaoEspelho.Direito: go = c.camEspDir; mat = c.matEspelhoDir; break;
                    default: go = c.camEspMeio; mat = c.matEspelhoMeio; break;
                }
                if (go == null && mat == null) return null;
                UnityEngine.Camera cam = null;
                if (go != null) { cam = go.GetComponent<UnityEngine.Camera>(); if (cam == null) cam = go.GetComponentInChildren<UnityEngine.Camera>(true); }
                var e = _cache[(int)pos] ??= new Espelho { Posicao = pos };
                e.Objeto = go; e.Camera = cam; e.Material = mat;
                return e;
            }
        }

        /// <summary>
        /// Objetos do modelo do ônibus do jogador, pelo nome (os mods de ônibus usam nomes como convenção: "_skin001_",
        /// "_wipers1_", "_r1y5_"...). A busca inclui objetos desligados e ignora maiúsculas. A lista é refeita quando o
        /// veículo é reescaneado (a cada 2 s), então guardar o Transform achado é seguro enquanto o veículo existir.
        /// </summary>
        public static class Objetos
        {
            /// <summary>O primeiro objeto com exatamente este nome, ou null.</summary>
            public static Transform Achar(string nome)
            {
                foreach (var t in Lista()) if (Vivo(t) && string.Equals(t.name, nome, StringComparison.OrdinalIgnoreCase)) return t;
                return null;
            }

            /// <summary>Todos os objetos cujo nome contém <paramref name="trecho"/>.</summary>
            public static IReadOnlyList<Transform> Todos(string trecho)
            {
                var r = new List<Transform>();
                foreach (var t in Lista()) if (Vivo(t) && Contem(t.name, trecho)) r.Add(t);
                return r;
            }

            /// <summary>Renderers dos objetos cujo nome contém <paramref name="trecho"/> ("" = todos).</summary>
            public static IReadOnlyList<Renderer> Renderizadores(string trecho = "")
            {
                var r = new List<Renderer>();
                foreach (var t in Lista())
                {
                    if (!Vivo(t) || !Contem(t.name, trecho)) continue;
                    var rd = t.GetComponent<Renderer>();
                    if (rd != null) r.Add(rd);
                }
                return r;
            }

            /// <summary>
            /// Materiais do ônibus (instâncias dele) com este nome (sem o " (Instance)"). Com <paramref name="trecho"/> true,
            /// os que contêm o texto: cuidado, mods usam nomes repetidos ("matvazio...") em centenas de materiais.
            /// </summary>
            public static IReadOnlyList<Material> Materiais(string nome, bool trecho = false)
            {
                var r = new List<Material>();
                var vistos = new HashSet<IntPtr>();
                foreach (var rd in Renderizadores())
                    foreach (var m in rd.materials)
                    {
                        if (m == null) continue;
                        var n = NomeDoMaterial(m);
                        bool ok = trecho ? Contem(n, nome) : string.Equals(n, nome, StringComparison.OrdinalIgnoreCase);
                        if (ok && vistos.Add(m.Pointer)) r.Add(m);
                    }
                return r;
            }

            /// <summary>Liga ou desliga todos os objetos cujo nome contém <paramref name="trecho"/>. Retorna quantos.</summary>
            public static int Mostrar(string trecho, bool visivel)
            {
                int n = 0;
                foreach (var t in Todos(trecho)) { t.gameObject.SetActive(visivel); n++; }
                return n;
            }

            /// <summary>
            /// Põe <paramref name="textura"/> nos materiais do ônibus com este nome (ou que contêm o texto, com <paramref name="trecho"/>).
            /// Mexe só nas instâncias deste ônibus (o tráfego pode usar o mesmo modelo). Retorna quantos materiais mudaram.
            /// </summary>
            public static int TrocarTextura(string material, Texture textura, string propriedade = "_MainTex", bool trecho = false)
            {
                int n = 0;
                foreach (var m in Materiais(material, trecho))
                    if (m.HasProperty(propriedade)) { m.SetTexture(propriedade, textura); n++; }
                return n;
            }

            /// <summary>Nome do material sem o " (Instance)" que a Unity acrescenta.</summary>
            public static string NomeDoMaterial(Material m)
            {
                var n = m != null ? m.name ?? "" : "";
                while (n.EndsWith(" (Instance)", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 11);
                return n;
            }

            static List<Transform> _lista = new();
            static int _de = -1;
            static float _em;

            static List<Transform> Lista()
            {
                var raiz = Raiz;
                if (raiz == null) { _lista.Clear(); return _lista; }
                if (_de != Geracao || _em < _buscaEm)
                {
                    _de = Geracao; _em = _buscaEm;
                    _lista = new List<Transform>(raiz.GetComponentsInChildren<Transform>(true));
                }
                return _lista;
            }

            static bool Vivo(Transform t) => t != null && !t.WasCollected;
            static bool Contem(string nome, string trecho) => string.IsNullOrEmpty(trecho) || (nome ?? "").IndexOf(trecho, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Suspensão a ar, informada por um mod (o SuspensaoAr) via <see cref="Informar"/>.</summary>
        public static class Suspensao
        {
            const string Mod = "Veiculo.Suspensao";
            /// <summary>Algum mod de suspensão a ar está controlando este veículo.</summary>
            public static bool TemAr => Dados.Tem(Mod, "nivel");
            /// <summary>"normal", "ajoelhado", "elevado"... (texto do mod que informou).</summary>
            public static string Nivel => Dados.Ler(Mod, "nivel", "");
            /// <summary>Altura em relação ao normal, em metros (negativo = baixou).</summary>
            public static float AlturaM => Dados.Ler(Mod, "altura", 0f);

            /// <summary>Um mod de suspensão a ar informa o estado (chame quando mudar ou a cada quadro).</summary>
            public static void Informar(string nivel, float alturaM)
            {
                Dados.Publicar(Mod, "nivel", nivel ?? "");
                Dados.Publicar(Mod, "altura", alturaM);
            }
        }

        // ---------- estado interno ----------
        static readonly List<VPVehicleController> _vpps = new();
        static readonly List<Porta> _portas = new();
        static readonly List<Roda> _rodas = new();
        static readonly HashSet<IntPtr> _rodasDoJogador = new();
        static readonly HashSet<IntPtr> _sonsDoJogador = new();
        static readonly HashSet<IntPtr> _freiosDoJogador = new();
        static IntPtr _ctrlPtr;
        static float _proximaBusca;
        internal static bool BuzinaPedida;

        /// <summary>Este Brakes do VPP é do veículo do jogador (para o freio a ar).</summary>
        internal static bool FreioDoJogador(IntPtr brakes) => _freiosDoJogador.Contains(brakes);
        internal static bool RodaDoJogador(IntPtr roda) => _rodasDoJogador.Contains(roda);
        internal static bool SomDoJogador(IntPtr som) => _sonsDoJogador.Contains(som);
        internal static bool VppDoJogador(IntPtr vpp) { foreach (var v in _vpps) if (v.Pointer == vpp) return true; return false; }

        internal static void CenaMudou()
        {
            Zerar();
            _ctrlPtr = IntPtr.Zero;
        }

        static void Zerar()
        {
            bool tinha = Existe;
            Existe = false; Raiz = null; Nome = "";
            Tipo = TipoVeiculo.Nenhum; Fisica = FisicaVeiculo.Desconhecida;
            Massa = 0f; VelocidadeKmh = 0f; VelocidadeSinalKmh = 0f; Parado = true;
            OdometroSessaoKm = 0f; OdometroTotalKm = 0f; EletricaLigada = false;
            Motor.Ligado = false; Motor.Rpm = 0f; Motor.RpmLenta = 0f; Motor.RpmMax = 0f; Motor.Carga = 0f; Motor.TorqueNm = 0f;
            Cambio.Marcha = 0; Cambio.Trocando = false; Cambio.Manual = false; Cambio.Automatizado = false;
            Cambio.RetarderNivel = 0; Cambio.RetarderNiveis = 0;
            Pedais.Acelerador = 0f; Pedais.Freio = 0f; Pedais.Embreagem = 0f; Pedais.FreioDeMao = 0f;
            Pedais.FreioDeMaoPuxado = false; Pedais.Direcao = 0f;
            Ajudas.PilotoAutomatico = false; Ajudas.TemLimitador = false; Ajudas.LimitadorAtivo = false;
            Ajudas.LimiteKmh = 0f; Ajudas.AbsAtuando = false; Ajudas.TracaoAtuando = false;
            Luzes.Farol = Farol.Desligado; Luzes.Seta = Seta.Nenhuma; Luzes.PiscaAceso = false;
            Luzes.Re = false; Luzes.Freio = false; Luzes.Internas = 0;
            Portas.Direitas = 0; Portas.Esquerdas = 0; Portas.AlgumaAberta = false;
            Cabine.Buzinando = false; Cabine.Limpadores = 0; Cabine.ParadaSolicitada = false;
            Cabine.TemArCondicionado = false; Cabine.ArCondicionadoLigado = false; Cabine.ArCondicionadoVentilador = 0;
            Cabine.TemperaturaInterna = 0f; Cabine.TemperaturaAlvo = 0; Cabine.JanelaMotoristaAberta = false;
            Cabine.TemCobrador = false; Cabine.PassageirosSessao = 0; Cabine.PassageirosABordo = 0;
            Avancado.ControlesJogo = null; Avancado.VppJogo = null; Avancado.VppDirigidoJogo = null; Rodas.Eixos = 0;
            _rodas.Clear(); _rodasDoJogador.Clear(); _sonsDoJogador.Clear();
            _vpps.Clear(); _portas.Clear(); _freiosDoJogador.Clear(); _caixaDe = null;
            BuzinaPedida = false;
            if (tinha) Sdk.AvisarVeiculoTrocou();
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
                Avancado.ControlesJogo = c;
                Geracao++;
                _proximaBusca = 0f;
            }
            // Peças (controladores, portas, massa) a cada 2 s: alguns mods montam o ônibus aos poucos.
            if (Time.unscaledTime >= _proximaBusca)
            {
                _proximaBusca = Time.unscaledTime + 2f;
                Buscar(c);
            }
            bool novo = !Existe;
            if (novo)
                MelonLoader.MelonLogger.Msg($"[Veiculo] {Nome} ({Tipo}, física {Fisica}, {_vpps.Count} VPP, {_portas.Count} portas), geração {Geracao}");
            Existe = true;
            Ler(c);
            PedidosDeMods();
            if (novo) Sdk.AvisarVeiculoTrocou();
        }

        static float _buscaEm;

        static void Buscar(MEPControlesBus c)
        {
            _buscaEm = Time.unscaledTime;
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
            Avancado.TodosVppLista = _vpps;

            VPVehicleController vpp = null;
            foreach (var v in new[] { c.scriptMotorVPP, c.scriptDirigivelVP, c.scriptDirigivelVPTracao })
                if (v != null && v.m_engine != null) { vpp = v; break; }
            if (vpp == null) foreach (var v in _vpps) if (v.m_engine != null) { vpp = v; break; }
            Avancado.VppJogo = vpp;

            // O dirigido é o que tem rodas que esterçam (nos articulados, o da frente).
            VPVehicleController dirigido = null;
            foreach (var v in new[] { c.scriptDirigivelVP, c.scriptMotorVPP, c.scriptDirigivelVPTracao })
                if (v != null && TemRodaQueEsterca(v)) { dirigido = v; break; }
            if (dirigido == null) foreach (var v in _vpps) if (TemRodaQueEsterca(v)) { dirigido = v; break; }
            Avancado.VppDirigidoJogo = dirigido;
            BuscarRodas();

            _sonsDoJogador.Clear();
            foreach (var m in Raiz.GetComponentsInChildren<Il2Cpp.MepSomMotor>(true)) _sonsDoJogador.Add(m.Pointer);

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

        static bool TemRodaQueEsterca(VPVehicleController v)
        {
            var ws = v.wheelState;
            if (ws == null) return false;
            for (int i = 0; i < ws.Length; i++) if (ws[i] != null && ws[i].steerable) return true;
            return false;
        }

        static void BuscarRodas()
        {
            var novas = new List<Roda>();
            _rodasDoJogador.Clear();
            foreach (var v in _vpps)
            {
                VehicleBase.WheelState[] ws = null;
                try { ws = v.wheelState; } catch { }
                if (ws == null) continue;
                foreach (var w in ws)
                {
                    var col = w?.wheelCol;
                    if (col == null || !_rodasDoJogador.Add(col.Pointer)) continue;
                    var r = _rodas.Find(x => x.Jogo != null && x.Jogo.Pointer == col.Pointer) ?? new Roda { Jogo = col };
                    r.Controlador = v; r._estado = w;
                    r.PosicaoLocal = Raiz.InverseTransformPoint(col.transform.position);
                    novas.Add(r);
                }
            }
            float cx = CaixaLocal().center.x;
            foreach (var r in novas) r.Lado = r.PosicaoLocal.x < cx ? Lado.Esquerdo : Lado.Direito;
            // Eixos: rodas a menos de 40 cm uma da outra no comprimento são do mesmo eixo.
            novas.Sort((a, b) => b.PosicaoLocal.z.CompareTo(a.PosicaoLocal.z));
            int eixo = -1; float zEixo = float.MaxValue;
            foreach (var r in novas)
            {
                if (zEixo - r.PosicaoLocal.z > 0.4f) { eixo++; zEixo = r.PosicaoLocal.z; }
                r.Eixo = eixo;
            }
            novas.Sort((a, b) => a.Eixo != b.Eixo ? a.Eixo.CompareTo(b.Eixo) : a.Lado.CompareTo(b.Lado));
            _rodas.Clear(); _rodas.AddRange(novas);
            Rodas.Eixos = eixo + 1;
        }

        static void Ler(MEPControlesBus c)
        {
            VelocidadeKmh = c.speedkmh;
            Parado = c.tahParado;
            OdometroSessaoKm = c.distPercorrida / 1000f;
            OdometroTotalKm = c.distPercorridaTotal / 1000f;
            EletricaLigada = c.onibusLigado;
            Motor.Ligado = c.motorLigado;
            Pedais.FreioDeMaoPuxado = c.freioDeMaoAtivado;
            Cambio.Manual = c.ehCambioManual && !c.ehSoAutomatico;
            Cambio.Automatizado = c.ehCambioAutomatizado;
            Cambio.RetarderNivel = c.currentRetarderLevel;
            Cambio.RetarderNiveis = c.retarderMaxLevels;
            Ajudas.PilotoAutomatico = c.cruiseControltahAtivado;
            Ajudas.TemLimitador = c.temLimitadorDeVelocidade;
            Ajudas.LimitadorAtivo = c.speedLimitertahAtivado;
            Ajudas.LimiteKmh = c.speedLimit;

            Luzes.Farol = c.farolAltoAceso ? Farol.Alto : c.farolBaixoAceso ? Farol.Baixo : Farol.Desligado;
            Luzes.Seta = c.piscaAlertaAtivado ? Seta.Alerta : c.setaEsquerdaAtivada ? Seta.Esquerda : c.setaDireitaAtivada ? Seta.Direita : Seta.Nenhuma;
            Luzes.PiscaAceso = c.luzPiscando;
            Luzes.Re = c.luzDeReAtivada;
            Luzes.Freio = c.luzDeFreioMaisForteAtivada;
            Luzes.Internas = (c.luzInterna1Ativada ? 1 : 0) | (c.luzInterna2Ativada ? 2 : 0) | (c.luzInterna3Ativada ? 4 : 0);
            Cabine.Limpadores = !c.limpadoresAtivados ? 0 : c.limpadoresRapidosLigados ? 2 : 1;
            Cabine.ParadaSolicitada = c.paradasolTahAcesa;
            try { var b = c.ASsomBuzina; Cabine.Buzinando = BuzinaPedida || Conducao.BuzinaPedida || (b != null && b.isPlaying && b.loop); }
            catch { Cabine.Buzinando = BuzinaPedida || Conducao.BuzinaPedida; }

            Portas.Direitas = c.qtdePortasDireitas;
            Portas.Esquerdas = c.qtdePortasEsquerdas;
            Portas.AlgumaAberta = c.temPortaAberta;
            foreach (var p in _portas)
            {
                try { p.Aberta = p.Jogo != null && !p.Jogo.WasCollected && p.Jogo.tahAberta; } catch { p.Aberta = false; }
            }

            Cabine.TemArCondicionado = c.temArCondicionado;
            Cabine.ArCondicionadoLigado = c.arCondicionadoLigado;
            Cabine.ArCondicionadoVentilador = c.arCondicionadoNivelVentilador;
            Cabine.TemperaturaInterna = c.temperaturaInterna;
            Cabine.TemperaturaAlvo = c.temperaturaAlvo;
            Cabine.JanelaMotoristaAberta = c.janelaMotoristaAberta;
            Cabine.TemCobrador = c.temCobrador;
            Cabine.PassageirosSessao = c.qtdeCatracadosSessao;
            Cabine.PassageirosABordo = c.qtdePassageirosNoBusao;

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

            var vp = Avancado.VppJogo;
            if (vp != null && !vp.WasCollected && vp.data != null)
            {
                try
                {
                    var ent = vp.data.Get(Channel.Input);
                    var vei = vp.data.Get(Channel.Vehicle);
                    Pedais.Acelerador = Mathf.Clamp01(ent[InputData.Throttle] / 10000f);
                    Pedais.Freio = Mathf.Clamp01(ent[InputData.Brake] / 10000f);
                    Pedais.Embreagem = Mathf.Clamp01(ent[InputData.Clutch] / 10000f);
                    Pedais.FreioDeMao = Mathf.Clamp01(ent[InputData.Handbrake] / 10000f);
                    Pedais.Direcao = Mathf.Clamp(ent[InputData.Steer] / 10000f, -1f, 1f);
                    Motor.Rpm = vei[VehicleData.EngineRpm] / 1000f;
                    Motor.Carga = Mathf.Clamp01(vei[VehicleData.EngineLoad] / 1000f);
                    Motor.TorqueNm = vei[VehicleData.EngineTorque] / 1000f;
                    Cambio.Marcha = vei[VehicleData.GearboxGear];
                    Cambio.Trocando = vei[VehicleData.GearboxShifting] != 0;
                    Ajudas.AbsAtuando = vei[VehicleData.AbsEngaged] != 0;
                    Ajudas.TracaoAtuando = vei[VehicleData.TcsEngaged] != 0 || vei[VehicleData.AsrEngaged] != 0;
                    var s = vp.m_engine?.settings;
                    if (s != null) { Motor.RpmLenta = s.idleRpm; Motor.RpmMax = s.maxRpm; }
                    // O freio a ar guarda o pedido do motorista antes de mudar o freio do VPP.
                    if (Ar.PedalFresco) Pedais.Freio = Ar.PedalLido;
                    return;
                }
                catch { /* cai nos campos do jogo */ }
            }
            Pedais.Acelerador = Mathf.Clamp01(c.busAcelerador);
            Pedais.Freio = Mathf.Clamp01(c.busFreio);
            Pedais.FreioDeMao = Mathf.Clamp01(c.busFreioDeMao);
            Pedais.Embreagem = 0f;
            Pedais.Direcao = Mathf.Clamp(c.busSteer, -1f, 1f);
            Motor.Rpm = c.busRPM;
            Motor.RpmMax = c.busMaxRPM;
            Cambio.Marcha = c.ehCambioManual ? c.busMarchaManual : c.busMarchaAutomatica;
            Cambio.Trocando = c.tahTrocandoDeMarcha;
        }

        // Um mod pedindo pedais (Conducao): o VPP recebe o pedido dele, então é isso que os pedais mostram.
        static void PedidosDeMods()
        {
            if (Conducao.AceleradorPedido(out var a)) Pedais.Acelerador = a;
            if (Conducao.FreioPedido(out var f)) Pedais.Freio = f;
            if (Conducao.EmbreagemPedida(out var e)) Pedais.Embreagem = e;
            if (Conducao.FreioDeMaoPedido(out var m)) Pedais.FreioDeMao = m;
            if (Conducao.DirecaoPedida(out var d)) Pedais.Direcao = d;
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
            if (!Existe) return "sem veículo\n";
            var sb = new StringBuilder();
            sb.Append($"{Nome} ({Tipo}, física {Fisica}, {_vpps.Count} controlador(es) VPP, massa {Massa:0} kg, caixa {Caixa.size}), geração {Geracao}\n");
            sb.Append($"velocidade {VelocidadeKmh:0.0} km/h (sinal {VelocidadeSinalKmh:0.0}), parado {Parado}, odômetro {OdometroSessaoKm:0.00}/{OdometroTotalKm:0.0} km\n");
            sb.Append($"elétrica {EletricaLigada}, motor {Motor.Ligado}, {Motor.Rpm:0} rpm (lenta {Motor.RpmLenta:0}, máx {Motor.RpmMax:0}), carga {Motor.Carga:0.00}, torque {Motor.TorqueNm:0} Nm\n");
            sb.Append($"marcha {Cambio.MarchaTexto} (trocando {Cambio.Trocando}), manual {Cambio.Manual}, automatizado {Cambio.Automatizado}, retarder {Cambio.RetarderNivel}/{Cambio.RetarderNiveis}\n");
            sb.Append($"acelerador {Pedais.Acelerador:0.00}, freio {Pedais.Freio:0.00}, embreagem {Pedais.Embreagem:0.00}, freio de mão {Pedais.FreioDeMaoPuxado} (alavanca {Pedais.FreioDeMao:0.00}), direção {Pedais.Direcao:0.00}\n");
            sb.Append($"piloto automático {Ajudas.PilotoAutomatico}, limitador {Ajudas.TemLimitador}/{Ajudas.LimitadorAtivo} {Ajudas.LimiteKmh:0} km/h, ABS {Ajudas.AbsAtuando}, tração {Ajudas.TracaoAtuando}\n");
            sb.Append($"farol {Luzes.Farol}, seta {Luzes.Seta}, ré {Luzes.Re}, luz de freio {Luzes.Freio}, internas {Luzes.Internas}, buzina {Cabine.Buzinando}, limpadores {Cabine.Limpadores}, parada solicitada {Cabine.ParadaSolicitada}\n");
            sb.Append($"portas: {Portas.Direitas} direita(s), {Portas.Esquerdas} esquerda(s), alguma aberta {Portas.AlgumaAberta}: {string.Join(", ", _portas)}\n");
            sb.Append($"ar-condicionado {Cabine.TemArCondicionado}/{Cabine.ArCondicionadoLigado} ventilador {Cabine.ArCondicionadoVentilador}, {Cabine.TemperaturaInterna:0.0} °C (alvo {Cabine.TemperaturaAlvo}), janela {Cabine.JanelaMotoristaAberta}, cobrador {Cabine.TemCobrador}\n");
            sb.Append($"passageiros: {Cabine.PassageirosABordo} a bordo, {Cabine.PassageirosSessao} na catraca nesta sessão\n");
            sb.Append($"rodas: {_rodas.Count} em {Rodas.Eixos} eixo(s): {string.Join(", ", _rodas)}; dirigido {Avancado.VppDirigidoJogo?.name ?? "-"}, motor {Avancado.VppJogo?.name ?? "-"}\n");
            sb.Append($"espelhos: {string.Join("; ", Espelhos.Todos)}\n");
            sb.Append($"suspensão a ar {Suspensao.TemAr} {Suspensao.Nivel} {Mathf.Round(Suspensao.AlturaM * 1000f) / 1000f + 0f:+0.000;-0.000;0.000} m, câmera dentro {CameraDentro}\n");
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
            if (__instance.Pointer == Avancado.ControlesJogo?.Pointer) Veiculo.BuzinaPedida = ehPraTocar;
        }
    }
}
