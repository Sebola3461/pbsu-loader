using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(PBSLoaderCommonUtils.PBSLoaderCommonUtilsMod), "PBSLoaderCommonUtils", "1.2.0", "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]
[assembly: MelonPriority(-1000)] // antes dos mods que dependem dela

namespace PBSLoaderCommonUtils
{
    // Base comum dos mods:
    //   Atalhos   registro único de teclas, conflitos com o jogo e entre mods
    //   Mouse     um dono só para o cursor
    //   Teclado   suspende os controles de teclado do jogo quando um painel precisa das teclas
    //   Veiculo   metadados do veículo do jogador (velocidade, pedais, luzes, portas...)
    //   Ar        compressor, reservatório, freio a ar e freio de molas (atua no freio do VPP)
    //   Avisos    avisos e falhas do veículo, de qualquer mod (a UiModerna mostra)
    //   Faixa     pressão do ar na faixa do Alt do jogo (FPS e velocidade), para quem não usa a UiModerna
    public class PBSLoaderCommonUtilsMod : MelonMod
    {
        static bool _sairAposLer;
        static float _sairEm = -1;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("PBSLoaderCommonUtils", "Veículo e ar comprimido");
            Ar.CriarPreferencias(cat);
            Faixa.CriarPreferencias(cat);
            Atalhos.Registrar("PBSLoaderCommonUtils", "Diagnostico", "Diagnóstico do veículo", "Ctrl+Alt+Shift+F2", GravarDiagnostico,
                "Grava UserData/PBSLoaderCommonUtils/veiculo.txt com todos os dados do veículo e do ar");
            // Teste automático: lê as teclas do jogo na partida, grava teclas.txt e fecha.
            _sairAposLer = Environment.CommandLine.Contains("--pbsloadercommonutils-teclas-sair");
            Teste.Iniciar();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Teclado.CenaMudou();
            Jogo.CenaMudou();
            Veiculo.CenaMudou();
        }

        public override void OnUpdate()
        {
            Seguro(Veiculo.Atualizar, "Veiculo");
            Seguro(Ar.Quadro, "Ar");
            Seguro(SomAr.Quadro, "SomAr");
            Seguro(Avisos.Atualizar, "Avisos");
            Seguro(Faixa.Quadro, "Faixa");
            Seguro(Jogo.Atualizar, "Jogo");
            Seguro(Atalhos.Atualizar, "Atalhos");
            Seguro(Teste.Quadro, "Teste");
            if (_sairAposLer && Il2CppMep.MEPControlesBus.instance != null && _sairEm < 0) _sairEm = Time.unscaledTime + 8f;
            if (_sairEm > 0 && Time.unscaledTime > _sairEm)
            {
                Seguro(Jogo.Reler, "Jogo");
                Application.Quit();
            }
        }

        public override void OnFixedUpdate() => Seguro(Ar.Passo, "Ar");

        public override void OnLateUpdate()
        {
            Seguro(Mouse.Aplicar, "Mouse");
            Seguro(Teclado.Aplicar, "Teclado");
        }

        internal static void GravarDiagnostico()
        {
            try
            {
                var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "veiculo.txt"), Veiculo.Resumo() + Avisos.Resumo() + SomAr.Diagnostico() + "\n" + Mouse.Diagnostico());
                MelonLogger.Msg("Diagnóstico do veículo gravado em UserData/PBSLoaderCommonUtils/veiculo.txt");
            }
            catch (Exception e) { MelonLogger.Warning("[Diagnostico] " + e.Message); }
        }

        // Erros repetidos (todo frame) aparecem no máximo a cada 10 s por lugar; a pilha só na primeira vez.
        static readonly Dictionary<string, float> _ultimoErro = new();
        static readonly HashSet<string> _comPilha = new();

        static void Seguro(Action a, string onde)
        {
            try { a(); }
            catch (Exception e)
            {
                float agora = Time.realtimeSinceStartup;
                if (_ultimoErro.TryGetValue(onde, out var t) && agora - t < 10f) return;
                _ultimoErro[onde] = agora;
                MelonLogger.Warning(_comPilha.Add(onde) ? $"[{onde}] {e}" : $"[{onde}] {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
