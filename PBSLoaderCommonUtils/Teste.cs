using System;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    // --pbsloadercommonutils-teste (com --uimoderna-partida): na partida, exercita o cursor e a suspensão do teclado e
    // grava UserData/PBSLoaderCommonUtils/teste.txt. Cada etapa dura alguns frames, para o jogo ter a chance de brigar
    // (MostraEscondeMouse roda todo frame).
    internal static class Teste
    {
        static bool _ativo;
        static int _etapa = -1, _frames;
        static readonly StringBuilder _r = new();
        static int _falhas;

        internal static void Iniciar()
        {
            _ativo = Environment.CommandLine.Contains("--pbsloadercommonutils-teste");
            if (_ativo) Application.runInBackground = true; // sem foco o Unity para de rodar frames
        }

        internal static void Quadro()
        {
            if (!_ativo) return;
            if (_etapa < 0)
            {
                if (Il2CppMep.MEPControlesBus.instance == null) return;
                _etapa = 0; _frames = 0;
                _r.Append("Teste do PBSLoaderCommonUtils\n");
            }
            _frames++;
            switch (_etapa)
            {
                case 0: // espera a partida assentar: o mesmo veículo por 300 frames seguidos (o jogo pode trocar
                        // o MEPControlesBus no meio da carga)
                    if (!Veiculo.Existe || Veiculo.Geracao != _geracao) { _geracao = Veiculo.Geracao; _frames = 0; }
                    if (_frames > 300) Proxima();
                    break;
                case 1: // um painel pede o cursor solto
                    Mouse.Soltar("teste.painel");
                    if (_frames == 30) Checar("painel aberto: cursor solto e visível", Cursor.lockState == CursorLockMode.None && Cursor.visible);
                    if (_frames > 30) Proxima();
                    break;
                case 2: // câmera pede preso, painel continua aberto: solto ganha
                    Mouse.Soltar("teste.painel");
                    Mouse.Prender("teste.camera");
                    if (_frames == 30) Checar("painel + câmera: solto ganha", Cursor.lockState == CursorLockMode.None && Cursor.visible);
                    if (_frames > 30) Proxima();
                    break;
                case 3: // painel fecha (para de pedir), câmera continua: preso
                    Mouse.Prender("teste.camera");
                    if (_frames == 30) Checar("só câmera: cursor preso e escondido", Cursor.lockState == CursorLockMode.Locked && !Cursor.visible);
                    if (_frames > 30) Proxima();
                    break;
                case 4: // ninguém pede: volta ao jogo (nunca preso)
                    if (_frames == 30) Checar("ninguém pede: cursor devolvido ao jogo (solto)", Cursor.lockState == CursorLockMode.None);
                    if (_frames > 30) Proxima();
                    break;
                case 5: // painel que usa teclas suspende os controles de teclado do jogo
                    Teclado.SuspenderJogo("teste.painel");
                    if (_frames == 10) Checar("teclado do jogo suspenso", Teclado.JogoSuspenso && !TemTeclado());
                    if (_frames > 10) Proxima();
                    break;
                case 6:
                    if (_frames == 10) Checar("teclado do jogo devolvido", !Teclado.JogoSuspenso && TemTeclado());
                    if (_frames > 10) Proxima();
                    break;
                case 7: // metadados com o ônibus parado e desligado
                    if (_frames < 5) break;
                    _r.Append("\n[veículo ao entrar]\n").Append(Veiculo.Resumo());
                    Checar("veículo encontrado (VPP)", Veiculo.Existe && Veiculo.Fisica == FisicaVeiculo.Vpp);
                    Checar("portas lidas batem com o jogo", Veiculo.Portas.Count >= Veiculo.PortasDireitas + Veiculo.PortasEsquerdas);
                    Ar.Evento += e => { if (e == "purga") _purgou = true; };
                    Proxima();
                    break;
                case 8: // reservatório quase vazio: molas aplicam sozinhas
                    if (_frames == 1) Ar.DefinirPressao(1.5f);
                    if (_frames == 150)
                    {
                        float mao = MaoNoVpp();
                        Checar($"pressão baixa: freio de molas aplica (molas {Ar.FreioMolas:0.00}, freio de mão no VPP {mao:0.00}, freio de mão do jogo {Veiculo.FreioDeMao})",
                            Ar.FreioMolas > 0.9f && mao >= 0.9f);
                        Checar($"alarme com pressão baixa e elétrica ligada (elétrica {Veiculo.EletricaLigada})", Ar.Alarme == Veiculo.EletricaLigada);
                        Proxima();
                    }
                    break;
                case 9: // liga elétrica e motor (como o teste do SomDiesel)
                    if (_frames == 1 && !Veiculo.EletricaLigada) Chamar("LigaDesligaOnibus");
                    if (_frames == 60 && !Veiculo.MotorLigado) Chamar("LigaDesligaMotorEVP5");
                    if (_frames > 60 && (Veiculo.MotorLigado && Veiculo.Rpm > 300f || _frames > 900))
                    {
                        Ar.DefinirPressao(6f); // abaixo do retorno do governador: o compressor tem que carregar
                        _p0 = Ar.Pressao; _t0 = Time.unscaledTime; _tJogo0 = Time.time;
                        Proxima();
                    }
                    break;
                case 10: // 10 s de compressor, depois de 3 s para as câmaras de freio reencherem (o jogo segura o
                         // freio com o ônibus parado, e elas tinham ficado com a pressão baixa da etapa 8)
                    if (!_medindo)
                    {
                        if (Time.time - _tJogo0 < 3f) break;
                        _medindo = true; _p0 = Ar.Pressao; _t0 = Time.unscaledTime; _tJogo0 = Time.time;
                    }
                    if (Time.unscaledTime - _t0 < 10f) break;
                    {
                        float jogo = Time.time - _tJogo0, real = Time.unscaledTime - _t0;
                        float taxa = (Ar.Pressao - _p0) / Mathf.Max(0.01f, jogo) * 60f;
                        float esperado = Ar.VazaoCompressor / Ar.VolumeLitros;
                        Checar($"compressor enche com o motor ligado ({_p0:0.00} -> {Ar.Pressao:0.00} bar em {jogo:0.0} s de jogo / {real:0.0} s reais, " +
                               $"{taxa:0.00} bar/min (esperado ~{esperado:0.00}) a {Veiculo.Rpm:0} rpm, {Ar.VazaoCompressor:0} L/min, timeScale {Time.timeScale:0.00})",
                            Veiculo.MotorLigado && Ar.Pressao > _p0 + 0.05f && Ar.CompressorCarregando);
                        Ar.DefinirPressao(Ar.PressaoCorte - 0.15f);
                        _purgou = false; _t0 = Time.unscaledTime;
                        Proxima();
                    }
                    break;
                case 11: // governador corta e o secador purga
                    if (!_purgou && Time.unscaledTime - _t0 < 20f) break;
                    Checar($"governador corta em {Ar.PressaoCorte:0.0} bar e purga ({Ar.Pressao:0.00} bar, carregando {Ar.CompressorCarregando})",
                        _purgou && !Ar.CompressorCarregando);
                    Proxima();
                    break;
                case 12: // o freio passa pelo ar; pedais em repouso (sentido da embreagem)
                    if (_frames < 30) break;
                    Checar($"freio do VPP passa pela simulação (pedido lido {Ar.PedalLido:0.00}, efetivo {Ar.FreioEfetivo:0.00})", Ar.PedalLido >= 0f);
                    _r.Append($"pedais em repouso: acelerador {Veiculo.Acelerador:0.00}, freio {Veiculo.Freio:0.00}, embreagem {Veiculo.Embreagem:0.00}\n");
                    _r.Append("\n[veículo com motor ligado]\n").Append(Veiculo.Resumo()).Append(SomAr.Diagnostico()).Append('\n');
                    Proxima();
                    break;
                case 13: // avisos: ar baixo, porta aberta (pelo próprio jogo), um aviso e uma notificação de outro "mod"
                    if (_frames == 1)
                    {
                        Ar.DefinirPressao(4.4f); // baixa para o alarme, mas acima do mínimo das portas (4 bar)
                        Avisos.Notificar("Teste", "salvo", Gravidade.Info, "Ajustes salvos", "Retrovisores do 1519", 20f);
                        Avisos.Definir("Teste", "retarder", true, Gravidade.Aviso, "Retarder quente", "82 °C · reduza o nível");
                        _portasJogo = UnityEngine.Object.FindObjectOfType<Il2Cpp.MEPCtrlPortasPeds>();
                        if (_portasJogo != null) _portasJogo.AbreFechaPortaDir1(); else _r.Append("(sem MEPCtrlPortasPeds)\n");
                        // Para o print do painel de condução da UiModerna: seta, farol e parada solicitada.
                        var c = Veiculo.Controles;
                        if (c != null) { c.setaEsquerdaAtivada = true; c.paradasolTahAcesa = true; }
                        // Farol e luz do salão pela API de comandos (os mesmos métodos dos botões do jogo).
                        if (Veiculo.Farol != Farol.Baixo) Comandos.FarolBaixo();
                        _temSalao = Comandos.TemLuzInterna(1);
                        if (_temSalao && !Comandos.LuzInternaAcesa(1)) Comandos.LuzInterna(1);
                    }
                    if (_frames == 150)
                    {
                        Checar($"aviso de ar baixo ({Ar.Pressao:0.0} bar)", Tem("PBSLoaderCommonUtils/ar", Gravidade.Falha));
                        Checar($"aviso de porta aberta parado (portas: {string.Join(", ", Veiculo.Portas)})", Tem("PBSLoaderCommonUtils/portas", Gravidade.Info));
                        Checar("aviso e notificação de outro mod", Tem("Teste/retarder", Gravidade.Aviso) && Tem("Teste/salvo", Gravidade.Info));
                        _r.Append($"painel: seta {Veiculo.Seta} (pisca {Veiculo.PiscaAceso}), farol {Veiculo.Farol}, parada {Veiculo.ParadaSolicitada}, {Veiculo.Rpm:0} rpm\n");
                        Checar($"comandos: farol baixo e luz do salão 1 (tem salão {_temSalao}, internas {Veiculo.LuzesInternas})",
                            Veiculo.Farol == Farol.Baixo && (!_temSalao || Comandos.LuzInternaAcesa(1)));
                        _r.Append(Avisos.Resumo());
                        var png = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils", "avisos.png");
                        try { File.Delete(png); } catch { }
                        ScreenCapture.CaptureScreenshot(png);
                    }
                    if (_frames == 200)
                    {
                        _portasJogo?.AbreFechaPortaDir1();
                        Avisos.Definir("Teste", "retarder", false, Gravidade.Aviso, null);
                        var c2 = Veiculo.Controles;
                        if (c2 != null) { c2.setaEsquerdaAtivada = false; c2.paradasolTahAcesa = false; }
                        if (Veiculo.Farol == Farol.Baixo) Comandos.FarolBaixo();
                        if (_temSalao && Comandos.LuzInternaAcesa(1)) Comandos.LuzInterna(1);
                        Ar.DefinirPressao(8f);
                    }
                    if (_frames == 400)
                    {
                        Checar("avisos saem quando a condição acaba (ar, porta, retarder)",
                            !Tem("PBSLoaderCommonUtils/ar", null) && !Tem("PBSLoaderCommonUtils/portas", null) && !Tem("Teste/retarder", null));
                        Proxima();
                    }
                    break;
                case 14: // sem ar: a porta não abre e a suspensão a ar murcha
                    if (_frames == 1)
                    {
                        Ar.DefinirPressao(2f);
                        _alturaAntes = Veiculo.AlturaSuspensao;
                    }
                    if (_frames == 30) _portasJogo?.AbreFechaPortaDir1();
                    if (_frames == 60) _avisouPorta = Tem("PBSLoaderCommonUtils/portas-sem-ar", Gravidade.Aviso); // notificação dura 4 s
                    if (_frames == 400)
                    {
                        bool abriu = false;
                        foreach (var p in Veiculo.Portas) if (p.Aberta) abriu = true;
                        Checar($"sem ar a porta não abre ({Ar.Pressao:0.0} bar, mínimo {Ar.PressaoPortas.Value:0.0}) e avisa",
                            !abriu && _avisouPorta);
                        if (Veiculo.TemSuspensaoAr)
                            Checar($"sem ar a suspensão baixa ({_alturaAntes:+0.000;-0.000} -> {Veiculo.AlturaSuspensao:+0.000;-0.000} m, nível {Veiculo.NivelSuspensao}) e avisa",
                                Veiculo.AlturaSuspensao < _alturaAntes - 0.01f && Tem("SuspensaoAr/sem-ar", Gravidade.Aviso));
                        else _r.Append("(sem a SuspensaoAr: suspensão não testada)\n");
                        _r.Append(Avisos.Resumo());
                        if (abriu) _portasJogo?.AbreFechaPortaDir1();
                        Ar.DefinirPressao(8f);
                        Proxima();
                    }
                    break;
                case 15: // pressão na faixa do Alt: força a faixa aberta, confere o texto e grava faixa.png
                    {
                        var f = Il2Cpp.FPSDisplayNovo.instance;
                        if (f == null) { _r.Append("(sem FPSDisplayNovo: faixa não testada)\n"); Proxima(); break; }
                        Il2Cpp.FPSDisplayNovo.mostrarFPSNovoPqDoAlt = true;
                        if (f.canvasFPSNovo != null && !f.canvasFPSNovo.activeSelf) f.canvasFPSNovo.SetActive(true);
                        if (_frames == 1) Ar.DefinirPressao(7.3f);
                        if (_frames == 90)
                        {
                            string t = Faixa.TextoAtual;
                            Checar($"pressão na faixa do Alt (\"{t}\", reservatório {Ar.Pressao:0.00} bar, velocidade \"{f.txtVelocidade?.text}\")", t != null && t.EndsWith(" bar") && float.TryParse(t.Split(' ')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lido) && Mathf.Abs(lido - Ar.Pressao) < 0.15f);
                            var png = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils", "faixa.png");
                            try { File.Delete(png); } catch { }
                            ScreenCapture.CaptureScreenshot(png);
                        }
                        if (_frames == 120)
                        {
                            Il2Cpp.FPSDisplayNovo.mostrarFPSNovoPqDoAlt = false;
                            Ar.DefinirPressao(8f);
                            Proxima();
                        }
                    }
                    break;
                case 16:
                    Jogo.Reler();
                    _r.Append($"\n{Atalhos.Todos.Count} atalhos registrados\n");
                    foreach (var a in Atalhos.Todos)
                        _r.Append($"  {a.Combinacao,-22} {a.Mod}.{a.Id}{(a.Conflitos.Count > 0 ? "  CONFLITO: " + string.Join("; ", a.Conflitos) : "")}\n");
                    _r.Append(_falhas == 0 ? "\nRESULTADO: tudo certo\n" : $"\nRESULTADO: {_falhas} falha(s)\n");
                    try
                    {
                        var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, "teste.txt"), _r.ToString());
                    }
                    catch (Exception e) { MelonLogger.Warning("[Teste] " + e.Message); }
                    MelonLogger.Msg("[Teste] " + (_falhas == 0 ? "tudo certo" : _falhas + " falha(s)"));
                    _ativo = false;
                    if (Environment.CommandLine.Contains("--pbsloadercommonutils-sair")) Application.Quit();
                    break;
            }
        }

        static bool _purgou, _medindo;
        static Il2Cpp.MEPCtrlPortasPeds _portasJogo;
        static float _alturaAntes;
        static bool _avisouPorta, _temSalao;

        static bool Tem(string chave, Gravidade? g)
        {
            foreach (var a in Avisos.Ativos) if (a.Chave == chave && (g == null || a.Gravidade == g)) return true;
            return false;
        }
        static int _geracao = -1;
        static float _p0, _t0, _tJogo0;

        static float MaoNoVpp()
        {
            try { var b = Veiculo.Vpp?.m_brakes; return b != null ? b.handbrakeInput : -1f; }
            catch { return -1f; }
        }

        static void Chamar(string metodo)
        {
            var c = Veiculo.Controles;
            if (c == null) return;
            var m = c.GetType().GetMethod(metodo, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m == null) { _r.Append($"(sem o método {metodo})\n"); return; }
            try { m.Invoke(c, null); } catch (Exception e) { _r.Append($"({metodo} falhou: {e.InnerException?.Message ?? e.Message})\n"); }
        }

        static bool TemTeclado()
        {
            var p = Jogo.Jogador();
            return p != null && p.controllers.hasKeyboard;
        }

        static void Proxima() { _etapa++; _frames = 0; }

        static void Checar(string o, bool ok)
        {
            // Sem foco o Windows não prende o cursor (lockState volta None): aí o teste de "preso" não diz nada.
            bool semFoco = !ok && !Application.isFocused;
            if (!ok && !semFoco) _falhas++;
            _r.Append(ok ? "ok     " : semFoco ? "SEM FOCO (inconclusivo) " : "FALHOU ").Append(o)
              .Append($"  (lockState={Cursor.lockState} visible={Cursor.visible} foco={Application.isFocused} pedido={(Mouse.Preso ? "preso" : Mouse.Solto ? "solto" : "nenhum")})\n");
            if (!ok) _r.Append(Mouse.Diagnostico());
        }
    }
}
