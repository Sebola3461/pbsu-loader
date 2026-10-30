using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppVehiclePhysics;
using VPVehicleController = Il2Cpp.VPVehicleController;
using VPWheelCollider = Il2Cpp.VPWheelCollider;

namespace PBSLoaderCommonUtils
{
    /// <summary>Um pedido de abrir ou fechar uma porta, antes de o jogo agir. Quem cancelar impede o comando (e o som).</summary>
    public sealed class PedidoDePorta
    {
        public Lado Lado { get; internal set; }
        /// <summary>Número da porta no jogo (1..6).</summary>
        public int Numero { get; internal set; }
        /// <summary>A porta, se o SDK já a conhece (null em ônibus que ainda estão montando).</summary>
        public Porta Porta { get; internal set; }
        /// <summary>true = vai abrir; false = vai fechar.</summary>
        public bool Abrindo { get; internal set; }
        public bool Cancelado { get; private set; }
        public string CanceladoPor { get; private set; }
        public void Cancelar(string mod) { if (!Cancelado) { Cancelado = true; CanceladoPor = mod; } }
    }

    /// <summary>
    /// Pontos do jogo onde os mods podem entrar sem escrever um patch Harmony. Cada um é um patch só, na
    /// PBSLoaderCommonUtils, e só dispara para o veículo do jogador (o tráfego usa os mesmos componentes).
    /// A ordem é a da inscrição; a PBSLoaderCommonUtils (freio a ar, portas sem ar) vem antes de todos.
    /// Os argumentos são componentes do jogo: os campos podem mudar entre versões dele.
    /// </summary>
    public static class Ganchos
    {
        static readonly Inscritos<Action<VPVehicleController>> _depoisDaFisica = new("Ganchos.DepoisDaFisica");
        static readonly Inscritos<Action<Brakes>> _antesDosFreios = new("Ganchos.AntesDosFreios");
        static readonly Inscritos<Action<VPWheelCollider>> _depoisDaRodaVisual = new("Ganchos.DepoisDaRodaVisual");
        static readonly Inscritos<Action<PedidoDePorta>> _portaPedida = new("Ganchos.PortaPedida");
        static readonly Inscritos<Action<Il2Cpp.MepSomMotor>> _depoisDoSomDoMotor = new("Ganchos.DepoisDoSomDoMotor");

        /// <summary>
        /// Depois de cada passo do VPP (VPVehicleController.DoUpdateBlocks) de um controlador do jogador. É aqui que
        /// se mexe em motor, câmbio e pneus: o próximo passo já usa o que foi escrito. Articulados disparam uma vez por controlador.
        /// </summary>
        public static event Action<VPVehicleController> DepoisDaFisica { add => _depoisDaFisica.Add(value); remove => _depoisDaFisica.Remove(value); }

        /// <summary>
        /// Antes de o VPP aplicar os freios do jogador (Brakes.DoUpdate, dentro de DoUpdateBlocks, depois de o pedal ser
        /// copiado do barramento). brakeInput e handbrakeInput já trazem o freio a ar, se ele estiver ligado.
        /// </summary>
        public static event Action<Brakes> AntesDosFreios { add => _antesDosFreios.Add(value); remove => _antesDosFreios.Remove(value); }

        /// <summary>Depois de o VPP posicionar a roda desenhada (suspensão, pinça e roda) de uma roda do jogador.</summary>
        public static event Action<VPWheelCollider> DepoisDaRodaVisual { add => _depoisDaRodaVisual.Add(value); remove => _depoisDaRodaVisual.Remove(value); }

        /// <summary>Alguém (jogador, painel, mod) pediu para abrir ou fechar uma porta. "Todas as portas" pede uma a uma.</summary>
        public static event Action<PedidoDePorta> PortaPedida { add => _portaPedida.Add(value); remove => _portaPedida.Remove(value); }

        /// <summary>
        /// Depois de o jogo atualizar uma camada do som do motor do jogador (MepSomMotor: motor, retarder, câmbio),
        /// todo quadro. É onde se ajusta pitch e volume do AudioSource da camada (asOn).
        /// </summary>
        public static event Action<Il2Cpp.MepSomMotor> DepoisDoSomDoMotor { add => _depoisDoSomDoMotor.Add(value); remove => _depoisDoSomDoMotor.Remove(value); }

        // ---------- patches ----------

        [HarmonyPatch(typeof(Il2Cpp.MepSomMotor), "RodaUpdateAgoraPorraaa")]
        static class PatchSomDoMotor
        {
            static void Postfix(Il2Cpp.MepSomMotor __instance)
            {
                if (_depoisDoSomDoMotor.Vazio || Sdk.ModoSeguro || __instance == null || !Veiculo.SomDoJogador(__instance.Pointer)) return;
                _depoisDoSomDoMotor.Disparar(__instance);
            }
        }

        [HarmonyPatch(typeof(VPVehicleController), "DoUpdateBlocks")]
        static class PatchFisica
        {
            static void Postfix(VPVehicleController __instance)
            {
                if (_depoisDaFisica.Vazio || Sdk.ModoSeguro || __instance == null || !Veiculo.VppDoJogador(__instance.Pointer)) return;
                _depoisDaFisica.Disparar(__instance);
            }
        }

        [HarmonyPatch(typeof(Brakes), nameof(Brakes.DoUpdate))]
        static class PatchFreios
        {
            static void Prefix(Brakes __instance)
            {
                if (Sdk.ModoSeguro || __instance == null || !Veiculo.FreioDoJogador(__instance.Pointer)) return;
                try { Ar.AntesDosFreios(__instance); } catch (Exception e) { Sdk.Registrar(e, "Ar.AntesDosFreios"); }
                if (!_antesDosFreios.Vazio) _antesDosFreios.Disparar(__instance);
            }
        }

        [HarmonyPatch(typeof(VPWheelCollider), nameof(VPWheelCollider.UpdateVisualWheel))]
        static class PatchRodaVisual
        {
            static void Postfix(VPWheelCollider __instance)
            {
                if (_depoisDaRodaVisual.Vazio || Sdk.ModoSeguro || __instance == null || !Veiculo.RodaDoJogador(__instance.Pointer)) return;
                _depoisDaRodaVisual.Disparar(__instance);
            }
        }

        // AbreFechaPorta{Dir,Esq}{1..6} tocam o som da porta antes de MEPCadaPortaBus.SetPodeAnimar inverter tahAberta;
        // AbreFechaTodasAsPortas chama as 12.
        [HarmonyPatch]
        static class PatchPortas
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var lado in new[] { "Dir", "Esq" })
                    for (int i = 1; i <= 6; i++)
                    {
                        var m = AccessTools.Method(typeof(Il2Cpp.MEPCtrlPortasPeds), "AbreFechaPorta" + lado + i);
                        if (m != null) yield return m;
                    }
            }

            static bool Prefix(MethodBase __originalMethod)
            {
                if (Sdk.ModoSeguro) return true;
                var nome = __originalMethod.Name; // AbreFechaPortaDir3
                var p = new PedidoDePorta
                {
                    Lado = nome.Contains("Esq") ? Lado.Esquerdo : Lado.Direito,
                    Numero = nome[^1] - '0',
                };
                foreach (var x in Veiculo.Portas.Todas)
                    if (x.Lado == p.Lado && x.Numero == p.Numero) { p.Porta = x; break; }
                p.Abrindo = p.Porta == null || !p.Porta.Aberta;
                Sdk.Seguro(() => Ar.PortaPedida(p), "Ar.PortaPedida");
                if (!_portaPedida.Vazio) _portaPedida.Disparar(p);
                return !p.Cancelado;
            }
        }
    }
}
