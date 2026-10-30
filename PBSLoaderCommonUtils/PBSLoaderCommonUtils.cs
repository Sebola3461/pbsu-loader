using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using PBSLoaderCommonUtils.Conteudo;
using PBSLoaderCommonUtils.Entrada;
using PBSLoaderCommonUtils.Interface;
using PBSLoaderCommonUtils.Linhas;
using PBSLoaderCommonUtils.Mapas;
using PBSLoaderCommonUtils.Mundo;
using PBSLoaderCommonUtils.Partida;
using PBSLoaderCommonUtils.Simulacao;
using PBSLoaderCommonUtils.Veiculos;
using UnityEngine;

[assembly: MelonInfo(typeof(PBSLoaderCommonUtils.PBSLoaderCommonUtilsMod), "PBSLoaderCommonUtils", PBSLoaderCommonUtils.Sdk.Versao, "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]
[assembly: MelonPriority(-1000)] // antes dos mods que dependem dela

namespace PBSLoaderCommonUtils
{
    // SDK dos mods (ver SDK.md). Cada namespace é uma área:
    //   (raiz)       Sdk (versão, fase, eventos), Ganchos (patches compartilhados), Dados (valores entre mods),
    //                Salvos (dados guardados entre sessões, por mod, ônibus e mapa),
    //                Avancado (componentes do jogo)
    //   Partida      Sessao (escolhas, pausa), Vista (câmera), Caminhos (pastas do jogo)
    //   Mapas        Mapa (nome, origem, pontos de ônibus carregados)
    //   Linhas       Linha (ponto atual e próximo), Letreiro, Passageiros (contagens e eventos)
    //   Midia        Carregar (texturas, sons e modelos pelos carregadores do jogo), Sons (som 3D)
    //   Mundo        Tempo (hora), Clima (chuva), Trafego (IA)
    //   Veiculos     Veiculo (velocidade, motor, câmbio, pedais, luzes, portas, cabine...) e Comandos
    //   Simulacao    Ar: compressor, reservatório, freio a ar e freio de molas (atua no freio do VPP)
    //   Entrada      Atalhos (registro único de teclas), Mouse (um dono só para o cursor), Teclado, TeclasDoJogo
    //   Conteudo     scripts que vêm com um ônibus ou mapa (ScriptDeOnibus, ScriptDeMapa), com permissão do jogador
    //   Interface    Avisos (avisos e falhas, a PBSLoaderCommonInterface mostra), Faixa (ar na faixa do Alt do jogo)
    public class PBSLoaderCommonUtilsMod : MelonMod
    {
        static bool _sairAposLer;
        static float _sairEm = -1;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("PBSLoaderCommonUtils", "Veículo e ar comprimido");
            VersaoDoJogo.Iniciar(cat);
            Ar.CriarPreferencias(cat);
            Faixa.CriarPreferencias(cat);
            Atalhos.Registrar("PBSLoaderCommonUtils", "Diagnostico", "Diagnóstico do SDK", "Ctrl+Alt+Shift+F2", GravarDiagnostico,
                "Grava UserData/PBSLoaderCommonUtils/sdk.txt com tudo que o SDK está lendo do jogo");
            // Teste automático: lê as teclas do jogo na partida, grava teclas.txt e fecha.
            _sairAposLer = Environment.CommandLine.Contains("--pbsloadercommonutils-teclas-sair");
            Carregador.Iniciar();
            Teste.Iniciar();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Teclado.CenaMudou();
            TeclasDoJogo.CenaMudou();
            Salvos.GravarAgora();
            Veiculo.CenaMudou();
            LeitorDoMundo.CenaMudou();
            Sdk.AvisarCena(sceneName);
        }

        public override void OnUpdate()
        {
            Sdk.Seguro(Veiculo.Atualizar, "Veiculo");
            Sdk.Seguro(Sdk.AtualizarFase, "Fase");
            Sdk.Seguro(Mapa.Atualizar, "Mapa");
            Sdk.Seguro(Passageiros.Atualizar, "Passageiros");
            Sdk.Seguro(LeitorDoMundo.Atualizar, "Mundo");
            Sdk.Seguro(Trafego.Atualizar, "Trafego");
            Sdk.Seguro(Vista.Atualizar, "Vista");
            Sdk.Seguro(Ar.Quadro, "Ar");
            Sdk.Seguro(SomAr.Quadro, "SomAr");
            Sdk.Seguro(Avisos.Atualizar, "Avisos");
            Sdk.Seguro(Faixa.Quadro, "Faixa");
            Sdk.Seguro(TeclasDoJogo.Atualizar, "TeclasDoJogo");
            Sdk.Seguro(Atalhos.Atualizar, "Atalhos");
            Sdk.Seguro(ControlesDoJogo.Quadro, "ControlesDoJogo");
            Sdk.Seguro(Conducao.Quadro, "Conducao");
            Sdk.Seguro(Midia.Sons.Quadro, "Sons");
            Sdk.Seguro(Salvos.Quadro, "Salvos");
            Sdk.Seguro(VersaoDoJogo.Quadro, "VersaoDoJogo");
            Sdk.AvisarQuadro();
            Sdk.Seguro(Carregador.Quadro, "Conteudo");
            Sdk.Seguro(Teste.Quadro, "Teste");
            if (_sairAposLer && Il2CppMep.MEPControlesBus.instance != null && _sairEm < 0) _sairEm = Time.unscaledTime + 8f;
            if (_sairEm > 0 && Time.unscaledTime > _sairEm)
            {
                Sdk.Seguro(TeclasDoJogo.Reler, "TeclasDoJogo");
                Application.Quit();
            }
        }

        public override void OnFixedUpdate()
        {
            Sdk.Seguro(Ar.Passo, "Ar");
            Sdk.AvisarPassoFisico();
            Sdk.Seguro(Carregador.PassoFisico, "Conteudo");
        }

        public override void OnLateUpdate()
        {
            Sdk.Seguro(Mouse.Aplicar, "Mouse");
            Sdk.Seguro(Teclado.Aplicar, "Teclado");
            Sdk.AvisarQuadroTardio();
            Sdk.Seguro(Carregador.QuadroTardio, "Conteudo");
        }

        public override void OnApplicationQuit() => Sdk.Seguro(Salvos.GravarAgora, "Salvos");

        public override void OnGUI() => Sdk.Seguro(Confirmacao.Desenhar, "Confirmacao");

        internal static void GravarDiagnostico()
        {
            try
            {
                var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "sdk.txt"), Diagnostico.Texto());
                MelonLogger.Msg("Diagnóstico do SDK gravado em UserData/PBSLoaderCommonUtils/sdk.txt");
            }
            catch (Exception e) { MelonLogger.Warning("[Diagnostico] " + e.Message); }
        }
    }
}
