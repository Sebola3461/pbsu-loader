using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppVehiclePhysics;
using UnityEngine;
using VPVehicleController = Il2Cpp.VPVehicleController;

namespace PBSLoaderCommonUtils.Veiculos
{
    /// <summary>
    /// Pedidos de condução de um mod (piloto automático, ajuda de direção, teste): acelerador, freio, embreagem, freio
    /// de mão e direção, no lugar do que o jogador está fazendo. Como em <see cref="Entrada.Mouse"/>, o pedido vale
    /// enquanto for repetido: chame a cada quadro; parou de chamar, o controle volta ao jogador no quadro seguinte.
    ///
    /// O valor é escrito no barramento de entrada do VPP logo antes de cada passo de física, depois de o jogo escrever o
    /// dele, então vence. <see cref="Veiculo.Pedais"/> passa a mostrar o pedido do mod. Com dois mods pedindo o mesmo
    /// pedal, vale o maior valor (freio e freio de mão) ou o último pedido (o resto). Só em veículos VPP.
    /// </summary>
    public static class Conducao
    {
        enum Eixo { Acelerador, Freio, Embreagem, FreioDeMao, Direcao }

        struct Pedido { public float Valor; public int Quadro; }

        static readonly Dictionary<string, Pedido>[] _pedidos =
        {
            new(), new(), new(), new(), new(),
        };

        /// <summary>Acelerador 0..1.</summary>
        public static void Acelerador(string dono, float valor) => Pedir(Eixo.Acelerador, dono, Mathf.Clamp01(valor));
        /// <summary>Freio 0..1 (passa pelo freio a ar, se ligado).</summary>
        public static void Freio(string dono, float valor) => Pedir(Eixo.Freio, dono, Mathf.Clamp01(valor));
        /// <summary>Embreagem 0..1, no sentido que o VPP recebe (ver <see cref="Veiculo.Pedais.Embreagem"/>).</summary>
        public static void Embreagem(string dono, float valor) => Pedir(Eixo.Embreagem, dono, Mathf.Clamp01(valor));
        /// <summary>Alavanca do freio de mão 0..1.</summary>
        public static void FreioDeMao(string dono, float valor) => Pedir(Eixo.FreioDeMao, dono, Mathf.Clamp01(valor));
        /// <summary>Direção -1 (esquerda) .. 1 (direita).</summary>
        public static void Direcao(string dono, float valor) => Pedir(Eixo.Direcao, dono, Mathf.Clamp(valor, -1f, 1f));

        /// <summary>
        /// Marcha do câmbio manual (-1 ré, 0 neutro, 1.. à frente), escrita no VPP e no painel do jogo. Para o câmbio
        /// automático use <see cref="Comandos.Marcha"/>. Mods de câmbio podem exigir a embreagem para engatar.
        /// </summary>
        public static void Marcha(string dono, int marcha) => _marchas[dono] = new PedidoMarcha { Marcha = marcha, Quadro = Time.frameCount };

        struct PedidoMarcha { public int Marcha, Quadro; }
        static readonly Dictionary<string, PedidoMarcha> _marchas = new();

        internal static bool MarchaPedida(out int m)
        {
            m = 0; int quadro = -1;
            foreach (var kv in _marchas)
                if (Time.frameCount - kv.Value.Quadro <= 1 && kv.Value.Quadro >= quadro) { m = kv.Value.Marcha; quadro = kv.Value.Quadro; }
            return quadro >= 0;
        }

        /// <summary>Buzina tocando enquanto for pedida (chame a cada quadro).</summary>
        public static void Buzina(string dono) => _buzina[dono] = Time.frameCount;

        /// <summary>Para de pedir tudo (o mesmo que não chamar no próximo quadro).</summary>
        public static void Soltar(string dono) { foreach (var d in _pedidos) d.Remove(dono); _buzina.Remove(dono); _marchas.Remove(dono); }

        static readonly Dictionary<string, int> _buzina = new();
        static bool _buzinando;

        internal static bool BuzinaPedida
        {
            get { foreach (var kv in _buzina) if (Time.frameCount - kv.Value <= 1) return true; return false; }
        }

        // O jogo chama TocaBuzina(false) todo quadro em que o jogador não aperta: o pedido do mod liga e mantém.
        internal static void Quadro()
        {
            bool quer = BuzinaPedida;
            var c = Avancado.ControlesJogo;
            if (c == null) { _buzinando = false; return; }
            if (MarchaPedida(out int mp) && Veiculo.Cambio.Manual)
            {
                try { c.busMarchaManual = mp; c.busMarchaManual_DESEJADAviaInput = mp; } catch (Exception e) { Sdk.Registrar(e, "Conducao.Marcha"); }
            }
            if (quer != _buzinando) { _buzinando = quer; try { c.TocaBuzina(quer); } catch (Exception e) { Sdk.Registrar(e, "Conducao.Buzina"); } }
        }

        [HarmonyPatch(typeof(Il2CppMep.MEPControlesBus), nameof(Il2CppMep.MEPControlesBus.TocaBuzina))]
        static class PatchBuzina
        {
            static void Prefix(Il2CppMep.MEPControlesBus __instance, ref bool ehPraTocar)
            {
                if (!ehPraTocar && _buzinando && __instance.Pointer == Avancado.ControlesJogo?.Pointer) ehPraTocar = true;
            }
        }

        // Valores pedidos agora, para Veiculo.Pedais mostrar o que o VPP recebe.
        internal static bool AceleradorPedido(out float v) => Ler(Eixo.Acelerador, false, out v);
        internal static bool FreioPedido(out float v) => Ler(Eixo.Freio, true, out v);
        internal static bool EmbreagemPedida(out float v) => Ler(Eixo.Embreagem, false, out v);
        internal static bool FreioDeMaoPedido(out float v) => Ler(Eixo.FreioDeMao, true, out v);
        internal static bool DirecaoPedida(out float v) => Ler(Eixo.Direcao, false, out v);

        /// <summary>Algum mod está pedindo algum pedal ou a direção agora.</summary>
        public static bool Controlando
        {
            get { foreach (var d in _pedidos) foreach (var kv in d) if (Vale(kv.Value)) return true; return MarchaPedida(out _); }
        }

        /// <summary>Quem está pedindo (diagnóstico).</summary>
        public static string Donos
        {
            get
            {
                var s = new HashSet<string>();
                foreach (var d in _pedidos) foreach (var kv in d) if (Vale(kv.Value)) s.Add(kv.Key);
                return string.Join(", ", s);
            }
        }

        static void Pedir(Eixo e, string dono, float v) => _pedidos[(int)e][dono] = new Pedido { Valor = v, Quadro = Time.frameCount };

        // Vale no quadro em que foi pedido e no seguinte (a física do próximo quadro roda antes do Update dos mods).
        static bool Vale(Pedido p) => Time.frameCount - p.Quadro <= 1;

        static bool Ler(Eixo e, bool maior, out float v)
        {
            v = 0f; bool tem = false; int quadro = -1;
            foreach (var kv in _pedidos[(int)e])
            {
                var p = kv.Value;
                if (!Vale(p)) continue;
                if (maior) { if (!tem || p.Valor > v) v = p.Valor; }
                else if (p.Quadro >= quadro) { v = p.Valor; quadro = p.Quadro; }
                tem = true;
            }
            return tem;
        }

        // Antes de o VPP copiar o barramento de entrada para os blocos (motor, câmbio, freios, direção).
        [HarmonyPatch(typeof(VPVehicleController), "DoUpdateBlocks")]
        static class PatchEntrada
        {
            static void Prefix(VPVehicleController __instance)
            {
                if (Sdk.ModoSeguro || __instance == null || !Veiculo.VppDoJogador(__instance.Pointer)) return;
                bool a = Ler(Eixo.Acelerador, false, out float acel), f = Ler(Eixo.Freio, true, out float freio),
                     em = Ler(Eixo.Embreagem, false, out float emb), m = Ler(Eixo.FreioDeMao, true, out float mao),
                     d = Ler(Eixo.Direcao, false, out float dir);
                bool g = MarchaPedida(out int marcha) && Veiculo.Cambio.Manual;
                if (!(a || f || em || m || d || g)) return;
                try
                {
                    var ent = __instance.data.Get(Channel.Input);
                    if (a) ent[InputData.Throttle] = (int)(acel * 10000f);
                    if (f) ent[InputData.Brake] = (int)(freio * 10000f);
                    if (em) ent[InputData.Clutch] = (int)(emb * 10000f);
                    if (m) ent[InputData.Handbrake] = (int)(mao * 10000f);
                    if (d) ent[InputData.Steer] = (int)(dir * 10000f);
                    if (g) ent[InputData.ManualGear] = marcha;
                }
                catch (Exception e) { Sdk.Registrar(e, "Conducao"); }
            }
        }
    }
}
