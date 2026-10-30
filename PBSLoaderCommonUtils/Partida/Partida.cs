using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PBSLoaderCommonUtils.Partida
{
    /// <summary>De onde veio o ônibus da partida.</summary>
    public enum OrigemDoOnibus { Desconhecida, Jogo, Mod }

    /// <summary>O que o jogador escolheu para a partida (ônibus, pintura, mapa, ponto de partida) e se está pausado.</summary>
    public static class Sessao
    {
        /// <summary>Nome do ônibus escolhido (o mesmo que <see cref="Veiculos.Veiculo.Nome"/> quando a partida já carregou).</summary>
        public static string Onibus => Primeiro(() => LoadConfigsAndMap.onibus, () => MepCanvasCarregaBusEMapa.bus, () => MepMobileControls.strNomeDoBusAtual);
        public static OrigemDoOnibus OrigemDoOnibus
        {
            get
            {
                try
                {
                    return CarregaBusNoInicio.tipoDeModACarregar == CarregaBusNoInicio.tipoDeModBus.viaModV1 ? OrigemDoOnibus.Mod
                        : CarregaBusNoInicio.tipoDeModACarregar == CarregaBusNoInicio.tipoDeModBus.viaResourcesFolder ? OrigemDoOnibus.Jogo
                        : OrigemDoOnibus.Desconhecida;
                }
                catch { return OrigemDoOnibus.Desconhecida; }
            }
        }
        /// <summary>Pasta da pintura escolhida (mods de ônibus).</summary>
        public static string PastaDaPintura => Primeiro(() => MepCanvasCarregaBusEMapa.pastaBusSkin);
        public static string ArquivoDaPintura => Primeiro(() => MepCanvasCarregaBusEMapa.arquivoSkin);
        public static string Mapa => Primeiro(() => GerenciaTiles.nomeDoMapa, () => LoadConfigsAndMap.mapa, () => MepCanvasCarregaBusEMapa.mapa);
        public static string PontoDePartida => Primeiro(() => LoadConfigsAndMap.entrypoint, () => MepCanvasCarregaBusEMapa.entrypoint, () => GerenciaModMapa1.entrypoint);

        /// <summary>O jogo está pausado (menu de pausa aberto).</summary>
        public static bool Pausado
        {
            get { try { return MepMobileControls.taPausado; } catch { return false; } }
        }

        static string Primeiro(params Func<string>[] fontes)
        {
            foreach (var f in fontes)
            {
                try { var s = f(); if (!string.IsNullOrWhiteSpace(s)) return s; } catch { }
            }
            return "";
        }

        internal static string Resumo() =>
            $"ônibus \"{Onibus}\" ({OrigemDoOnibus}), pintura \"{PastaDaPintura}\" / \"{ArquivoDaPintura}\", mapa \"{Mapa}\", ponto de partida \"{PontoDePartida}\", pausado {Pausado}\n";
    }

    /// <summary>Câmera do jogo.</summary>
    public enum ModoDaVista { Nenhuma, Motorista, Salao, Externa, Livre, Painel, Editor }

    /// <summary>
    /// Câmera do jogador (MouseOrbitCSHARP). Um mod que posiciona a câmera por conta própria (andar a pé, câmera
    /// de cinema) pede <see cref="Assumir"/> a cada quadro: enquanto alguém pedir, o jogo não move a câmera.
    /// </summary>
    public static class Vista
    {
        public static ModoDaVista Modo
        {
            get
            {
                try
                {
                    return MouseOrbitCSHARP.MEP_SelCamera switch
                    {
                        MouseOrbitCSHARP.ZipoCamera.camInterna => ModoDaVista.Motorista,
                        MouseOrbitCSHARP.ZipoCamera.camSalao => ModoDaVista.Salao,
                        MouseOrbitCSHARP.ZipoCamera.camExternaGiro => ModoDaVista.Externa,
                        MouseOrbitCSHARP.ZipoCamera.camExternaLivre => ModoDaVista.Livre,
                        MouseOrbitCSHARP.ZipoCamera.camExternaMouseLivre => ModoDaVista.Livre,
                        MouseOrbitCSHARP.ZipoCamera.camBotoesPainel => ModoDaVista.Painel,
                        MouseOrbitCSHARP.ZipoCamera.camMapEditor => ModoDaVista.Editor,
                        _ => ModoDaVista.Nenhuma,
                    };
                }
                catch { return ModoDaVista.Nenhuma; }
            }
        }

        /// <summary>O jogo considera a câmera dentro do veículo (som abafado, espelhos...).</summary>
        public static bool Interna
        {
            get { try { return MouseOrbitCSHARP.tahNaCameraInterna; } catch { return false; } }
        }

        /// <summary>Câmera que o jogo está usando agora.</summary>
        public static Camera Camera
        {
            get
            {
                try { var m = MepMobileControls.instance; if (m != null && m.cameraAtual != null) return m.cameraAtual; } catch { }
                return Camera.main;
            }
        }

        /// <summary>Volta para a câmera do motorista (como a tecla do jogo).</summary>
        public static void IrParaMotorista()
        {
            var m = MepMobileControls.instance;
            if (m != null) m.SetCameraMotorista();
        }

        /// <summary>
        /// O mod <paramref name="dono"/> controla a câmera neste quadro (chame todo quadro enquanto precisar; o pedido
        /// vence no quadro seguinte). Enquanto houver um dono, MouseOrbitCSHARP.Update/LateUpdate não rodam.
        /// </summary>
        public static void Assumir(string dono, bool precisa = true)
        {
            if (precisa) _pedidos[dono] = Time.frameCount;
            else _pedidos.Remove(dono);
        }

        /// <summary>Algum mod está controlando a câmera.</summary>
        public static bool Assumida
        {
            get
            {
                int q = Time.frameCount;
                foreach (var kv in _pedidos) if (q - kv.Value <= 1) return true;
                return false;
            }
        }

        public static string Dono
        {
            get
            {
                int q = Time.frameCount;
                foreach (var kv in _pedidos) if (q - kv.Value <= 1) return kv.Key;
                return "";
            }
        }

        static readonly Dictionary<string, int> _pedidos = new();

        static readonly Inscritos<Action<ModoDaVista>> _mudou = new("Vista.Mudou");
        /// <summary>O modo da câmera mudou.</summary>
        public static event Action<ModoDaVista> Mudou { add => _mudou.Add(value); remove => _mudou.Remove(value); }

        static ModoDaVista _ultimo;
        internal static void Atualizar()
        {
            var m = Modo;
            if (m == _ultimo) return;
            _ultimo = m;
            _mudou.Disparar(m);
        }

        [HarmonyPatch(typeof(MouseOrbitCSHARP), nameof(MouseOrbitCSHARP.Update))]
        static class PatchUpdate { static bool Prefix() => !Assumida; }

        [HarmonyPatch(typeof(MouseOrbitCSHARP), nameof(MouseOrbitCSHARP.LateUpdate))]
        static class PatchLateUpdate { static bool Prefix() => !Assumida; }

        internal static string Resumo() => $"câmera {Modo} (interna {Interna}, {Camera?.name ?? "-"}), assumida por \"{Dono}\"\n";
    }

    /// <summary>Pastas do jogo (ProtonPaths). Vazias até o jogo defini-las no menu inicial.</summary>
    public static class Caminhos
    {
        public static string Mods => Ler(() => ProtonPaths.pastaMods);
        public static string ModsDeOnibus => Ler(() => ProtonPaths.pastaModsBuses);
        public static string ModsDeMapas => Ler(() => ProtonPaths.pastaModsMapas);
        public static string ModsDeLetreiros => Ler(() => ProtonPaths.pastaModsLetreiros);
        public static string ModsDeCargas => Ler(() => ProtonPaths.pastaModsCargas);
        public static string Dados => Ler(() => ProtonPaths.pastaDados);
        public static string DadosDosVeiculos => Ler(() => ProtonPaths.pastaDadosVeiculos);
        public static string Capturas => Ler(() => ProtonPaths.pastaScreenshots);

        static string Ler(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }

        internal static string Resumo() => $"pastas: mods \"{Mods}\", ônibus \"{ModsDeOnibus}\", mapas \"{ModsDeMapas}\", letreiros \"{ModsDeLetreiros}\", dados \"{Dados}\"\n";
    }
}
