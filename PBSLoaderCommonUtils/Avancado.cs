using System.Collections.Generic;
using Il2CppMep;
using VPVehicleController = Il2Cpp.VPVehicleController;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Componentes do jogo por trás das fachadas do SDK, para quem precisa de algo que o SDK ainda não expõe.
    /// Os nomes e campos são os do jogo e podem mudar a cada versão dele: prefira as fachadas
    /// (<see cref="Veiculos.Veiculo"/> etc.) e peça o que faltar no SDK.
    /// Tudo é null quando não há veículo e também no <see cref="Sdk.ModoSeguro"/> (versão do jogo não testada).
    /// </summary>
    public static class Avancado
    {
        /// <summary>MEPControlesBus do veículo do jogador.</summary>
        public static MEPControlesBus Controles => Sdk.ModoSeguro ? null : ControlesJogo;
        /// <summary>Controlador VPP que tem o motor (null em EVP5).</summary>
        public static VPVehicleController Vpp => Sdk.ModoSeguro ? null : VppJogo;
        /// <summary>Todos os controladores VPP do veículo (articulados têm mais de um).</summary>
        public static IReadOnlyList<VPVehicleController> TodosVpp => Sdk.ModoSeguro ? Vazio : TodosVppLista;
        /// <summary>Controlador VPP com as rodas que esterçam (nos articulados, o da frente).</summary>
        public static VPVehicleController VppDirigido => Sdk.ModoSeguro ? null : VppDirigidoJogo;
        /// <summary>Este controlador VPP é do veículo do jogador (o tráfego usa os mesmos componentes).</summary>
        public static bool DoJogador(VPVehicleController vpp) => vpp != null && Veiculos.Veiculo.VppDoJogador(vpp.Pointer);
        /// <summary>Esta roda VPP é do veículo do jogador.</summary>
        public static bool DoJogador(Il2Cpp.VPWheelCollider roda) => roda != null && Veiculos.Veiculo.RodaDoJogador(roda.Pointer);

        // A própria PBSLoaderCommonUtils lê por aqui (as fachadas se protegem sozinhas, com try/catch).
        internal static MEPControlesBus ControlesJogo;
        internal static VPVehicleController VppJogo, VppDirigidoJogo;
        internal static IReadOnlyList<VPVehicleController> TodosVppLista = new List<VPVehicleController>();
        static readonly IReadOnlyList<VPVehicleController> Vazio = new List<VPVehicleController>();
    }
}
