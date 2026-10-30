using System;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    // --pbsloadercommonutils-teste (com --pbsloadercommoninterface-partida): na partida, exercita o cursor e a suspensão do teclado e
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
                    Checar("portas lidas batem com o jogo", Veiculo.Portas.Todas.Count >= Veiculo.Portas.Direitas + Veiculo.Portas.Esquerdas);
                    // SDK: as outras áreas também enxergam a partida.
                    Checar($"fase dirigindo (fase {Sdk.Fase}, cena {Sdk.Cena})", Sdk.Fase == Fase.Dirigindo);
                    Checar($"rodas e eixos ({Veiculo.Rodas.Todas.Count} rodas, {Veiculo.Rodas.Eixos} eixos, alguma esterça {System.Linq.Enumerable.Any(Veiculo.Rodas.Todas, r => r.Esterca)})",
                        Veiculo.Rodas.Todas.Count >= 4 && Veiculo.Rodas.Eixos >= 2 && System.Linq.Enumerable.Any(Veiculo.Rodas.Todas, r => r.Esterca));
                    Checar($"controlador dirigido e do motor ({Avancado.VppDirigidoJogo?.name ?? "-"}, {Avancado.VppJogo?.name ?? "-"})", Avancado.VppDirigidoJogo != null && Avancado.VppJogo != null);
                    Checar($"mapa da partida (\"{Mapas.Mapa.Nome}\", {Mapas.Mapa.Origem}, {Mapas.Mapa.Paradas.Count} pontos)", Mapas.Mapa.Existe && Mapas.Mapa.Nome.Length > 0);
                    if (Mapas.Mapa.Origem == Mapas.OrigemDoMapa.Mod)
                    {
                        Checar($"mapa de mod: pasta existe (\"{Mapas.Mapa.Pasta}\")", Mapas.Mapa.Pasta.Length > 0 && Directory.Exists(Mapas.Mapa.Pasta));
                        Checar($"mapa de mod: pontos carregados ({Mapas.Mapa.Paradas.Count})", Mapas.Mapa.Paradas.Count > 0);
                    }
                    Checar($"sessão: ônibus \"{Partida.Sessao.Onibus}\"", Partida.Sessao.Onibus.Length > 0);
                    Checar($"hora do jogo ({Mundo.Tempo.Texto})", Mundo.Tempo.Existe);
                    Checar($"câmera do jogo ({Partida.Vista.Modo})", Partida.Vista.Modo != Partida.ModoDaVista.Nenhuma);
                    {
                        // Salvos: grava, esquece a memória e lê do disco.
                        Salvos.Global("teste").Gravar("contagem", 7);
                        Salvos.DoOnibus("teste").Gravar("pontos", new[] { 1.5f, 2.5f });
                        Salvos.DoOnibus("teste").Gravar("nome", "ônibus de teste");
                        Salvos.Esquecer();
                        var g = Salvos.DoOnibus("teste");
                        var pts = g.Ler<float[]>("pontos");
                        Checar($"salvos entre sessões (global {Salvos.Global("teste").Ler("contagem", 0)}, ônibus \"{g.Ler("nome", "")}\", {pts?.Length ?? 0} pontos, arquivo {File.Exists(g.Arquivo)})",
                            Salvos.Global("teste").Ler("contagem", 0) == 7 && g.Ler("nome", "") == "ônibus de teste" && pts != null && pts.Length == 2 && pts[1] == 2.5f
                            && File.Exists(g.Arquivo) && g.Ler("nao-existe", -1) == -1 && g.Ler("nome", 0) == 0);
                        var arqGlobal = Salvos.Global("teste").Arquivo;
                        Salvos.Esquecer();
                        try { File.Delete(arqGlobal); File.Delete(g.Arquivo); } catch { }
                    }
                    {
                        // Opções do jogo: grava no settings.txt, relê, vale na hora no tráfego, e volta ao que era.
                        var o = Partida.Opcoes.CarrosNoTransito;
                        int antes = o.Valor;
                        o.Valor = antes + 3;
                        int lido = o.Valor, noTrafego = Il2Cpp.MEPAI_Traffic_System_Controller.maxCars;
                        var arq = Path.Combine(Partida.Caminhos.Dados, "settings.txt");
                        bool noArquivo = File.Exists(arq) && File.ReadAllText(arq).Contains("maxCars=" + (antes + 3));
                        o.Valor = antes;
                        Checar($"opções do jogo (carros {antes} -> {lido}, tráfego {noTrafego}, no settings.txt {noArquivo}, de volta {o.Valor}; sombras {Partida.Opcoes.Sombras.Valor}, volume {Partida.Opcoes.Volume.Valor})",
                            Partida.Opcoes.Disponivel && lido == antes + 3 && noTrafego == antes + 3 && noArquivo && o.Valor == antes);
                    }
                    {
                        var acoes = ControlesDoJogo.Acoes;
                        int eixos = 0; foreach (var a in acoes) if (a.Tipo == TipoDeAcao.Eixo) eixos++;
                        var primeira = acoes.Count > 0 ? acoes[0] : null;
                        Checar($"ações dos controles do jogo ({acoes.Count}, {eixos} eixos; dispositivos: {string.Join(", ", ControlesDoJogo.Dispositivos)})",
                            acoes.Count > 10 && eixos > 0 && ControlesDoJogo.Dispositivos.Count > 0
                            && primeira != null && ControlesDoJogo.Achar(primeira.Nome) == primeira && !ControlesDoJogo.Botao("ação que não existe"));
                        ControlesDoJogo.AcaoApertada += a => _acoesApertadas++;
                    }
                    Checar($"versão do jogo conhecida ({Sdk.VersaoDoJogo}, modo seguro {Sdk.ModoSeguro})", Sdk.JogoTestado && !Sdk.ModoSeguro);
                    Dados.Publicar("teste", "x", 42);
                    Checar("dados entre mods", Dados.Ler("teste", "x", 0) == 42 && !Dados.Tem("teste", "y") && Dados.Ler("teste", "x", "texto") == "texto");
                    Ganchos.DepoisDaFisica += _ => _passosFisica++;
                    Ganchos.DepoisDaRodaVisual += _ => _rodasVisuais++;
                    Ganchos.DepoisDoSomDoMotor += _ => _sonsMotor++;
                    Ganchos.PortaPedida += p => _portasPedidas++;
                    Ar.Evento += e => { if (e == "purga") _purgou = true; };
                    Proxima();
                    break;
                case 8: // reservatório quase vazio: molas aplicam sozinhas
                    if (_frames == 1) Ar.DefinirPressao(1.5f);
                    if (_frames == 150)
                    {
                        float mao = MaoNoVpp();
                        Checar($"pressão baixa: freio de molas aplica (molas {Ar.FreioMolas:0.00}, freio de mão no VPP {mao:0.00}, freio de mão do jogo {Veiculo.Pedais.FreioDeMaoPuxado})",
                            Ar.FreioMolas > 0.9f && mao >= 0.9f);
                        Checar($"alarme com pressão baixa e elétrica ligada (elétrica {Veiculo.EletricaLigada})", Ar.Alarme == Veiculo.EletricaLigada);
                        Proxima();
                    }
                    break;
                case 9: // liga elétrica e motor (como o teste do SomDiesel)
                    if (_frames == 1 && !Veiculo.EletricaLigada) Chamar("LigaDesligaOnibus");
                    if (_frames == 60 && !Veiculo.Motor.Ligado) Chamar("LigaDesligaMotorEVP5");
                    if (_frames > 60 && (Veiculo.Motor.Ligado && Veiculo.Motor.Rpm > 300f || _frames > 900))
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
                               $"{taxa:0.00} bar/min (esperado ~{esperado:0.00}) a {Veiculo.Motor.Rpm:0} rpm, {Ar.VazaoCompressor:0} L/min, timeScale {Time.timeScale:0.00})",
                            Veiculo.Motor.Ligado && Ar.Pressao > _p0 + 0.05f && Ar.CompressorCarregando);
                        Ar.DefinirPressao(Ar.PressaoCorte - 0.15f);
                        _purgou = false; _t0 = Time.unscaledTime;
                        Proxima();
                    }
                    break;
                case 11: // governador corta e o secador purga
                    if (!_purgou && Time.unscaledTime - _t0 < 40f) break;
                    Checar($"governador corta em {Ar.PressaoCorte:0.0} bar e purga ({Ar.Pressao:0.00} bar, carregando {Ar.CompressorCarregando})",
                        _purgou && !Ar.CompressorCarregando);
                    Proxima();
                    break;
                case 12: // o freio passa pelo ar; pedais em repouso (sentido da embreagem)
                    if (_frames < 30) break;
                    Checar($"freio do VPP passa pela simulação (pedido lido {Ar.PedalLido:0.00}, efetivo {Ar.FreioEfetivo:0.00})", Ar.PedalLido >= 0f);
                    _r.Append($"pedais em repouso: acelerador {Veiculo.Pedais.Acelerador:0.00}, freio {Veiculo.Pedais.Freio:0.00}, embreagem {Veiculo.Pedais.Embreagem:0.00}\n");
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
                        // Para o print do painel de condução da PBSLoaderCommonInterface: seta, farol e parada solicitada.
                        var c = Avancado.ControlesJogo;
                        if (c != null) { c.setaEsquerdaAtivada = true; c.paradasolTahAcesa = true; }
                        // Farol e luz do salão pela API de comandos (os mesmos métodos dos botões do jogo).
                        if (Veiculo.Luzes.Farol != Farol.Baixo) Comandos.FarolBaixo();
                        _temSalao = Comandos.TemLuzInterna(1);
                        if (_temSalao && !Comandos.LuzInternaAcesa(1)) Comandos.LuzInterna(1);
                    }
                    if (_frames == 150)
                    {
                        Checar($"aviso de ar baixo ({Ar.Pressao:0.0} bar)", Tem("PBSLoaderCommonUtils/ar", Gravidade.Falha));
                        Checar($"aviso de porta aberta parado (portas: {string.Join(", ", Veiculo.Portas.Todas)})", Tem("PBSLoaderCommonUtils/portas", Gravidade.Info));
                        Checar("aviso e notificação de outro mod", Tem("Teste/retarder", Gravidade.Aviso) && Tem("Teste/salvo", Gravidade.Info));
                        _r.Append($"painel: seta {Veiculo.Luzes.Seta} (pisca {Veiculo.Luzes.PiscaAceso}), farol {Veiculo.Luzes.Farol}, parada {Veiculo.Cabine.ParadaSolicitada}, {Veiculo.Motor.Rpm:0} rpm\n");
                        Checar($"comandos: farol baixo e luz do salão 1 (tem salão {_temSalao}, internas {Veiculo.Luzes.Internas})",
                            Veiculo.Luzes.Farol == Farol.Baixo && (!_temSalao || Comandos.LuzInternaAcesa(1)));
                        _r.Append(Avisos.Resumo());
                        var png = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils", "avisos.png");
                        try { File.Delete(png); } catch { }
                        ScreenCapture.CaptureScreenshot(png);
                    }
                    if (_frames == 200)
                    {
                        _portasJogo?.AbreFechaPortaDir1();
                        Avisos.Definir("Teste", "retarder", false, Gravidade.Aviso, null);
                        var c2 = Avancado.ControlesJogo;
                        if (c2 != null) { c2.setaEsquerdaAtivada = false; c2.paradasolTahAcesa = false; }
                        if (Veiculo.Luzes.Farol == Farol.Baixo) Comandos.FarolBaixo();
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
                        _alturaAntes = Veiculo.Suspensao.AlturaM;
                    }
                    if (_frames == 30) _portasJogo?.AbreFechaPortaDir1();
                    if (_frames == 60) _avisouPorta = Tem("PBSLoaderCommonUtils/portas-sem-ar", Gravidade.Aviso); // notificação dura 4 s
                    if (_frames == 400)
                    {
                        bool abriu = false;
                        foreach (var p in Veiculo.Portas.Todas) if (p.Aberta) abriu = true;
                        Checar($"sem ar a porta não abre ({Ar.Pressao:0.0} bar, mínimo {Ar.PressaoPortas.Value:0.0}) e avisa",
                            !abriu && _avisouPorta);
                        if (Veiculo.Suspensao.TemAr)
                            Checar($"sem ar a suspensão baixa ({_alturaAntes:+0.000;-0.000} -> {Veiculo.Suspensao.AlturaM:+0.000;-0.000} m, nível {Veiculo.Suspensao.Nivel}) e avisa",
                                Veiculo.Suspensao.AlturaM < _alturaAntes - 0.01f && Tem("SuspensaoAr/sem-ar", Gravidade.Aviso));
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
                case 16: // passageiros: leva o ônibus ao ponto mais perto, abre a porta da frente e espera o embarque
                    if (_frames == 1)
                    {
                        _ponto = Mapas.Mapa.Paradas.Count > 0 && Veiculo.Raiz != null ? Mapas.Mapa.MaisPerto(Veiculo.Raiz.position) : null;
                        if (_ponto == null) { _r.Append("(mapa sem pontos: embarque não testado)\n"); Proxima(); break; }
                        _r.Append($"\n[passageiros] ponto {_ponto} a {Vector3.Distance(_ponto.Posicao, Veiculo.Raiz.position):0} m, {_ponto.Esperando} esperando\n");
                        Passageiros.Embarcou += _ => _embarcaram++;
                        var rb = Veiculo.Raiz.GetComponent<Rigidbody>();
                        var destino = _ponto.Posicao + Vector3.up * 0.3f;
                        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = destino; }
                        Veiculo.Raiz.position = destino;
                        Ar.DefinirPressao(8f);
                        _tPonto = Time.time;
                    }
                    if (_frames == 120)
                    {
                        Checar($"ônibus no ponto (atual {Linha.ParadaAtual?.ToString() ?? "-"}, próximo {Linha.ProximaParada?.ToString() ?? "-"})", Linha.ParadaAtual != null);
                        // Uma vez só: o comando alterna, e o estado da porta só muda nos quadros seguintes.
                        bool abriu = false;
                        foreach (var p in Veiculo.Portas.Todas) if (p.Entrada && !p.Aberta) { Comandos.Porta(p); abriu = true; }
                        if (!abriu && Veiculo.Portas.Todas.Count > 0 && !Veiculo.Portas.Todas[0].Aberta) Comandos.Porta(Veiculo.Portas.Todas[0]);
                    }
                    if (_frames > 120 && (_embarcaram > 0 || Time.time - _tPonto > 40f))
                    {
                        _r.Append(Linha.Resumo());
                        _r.Append($"ponto: tem ônibus {_ponto.TemOnibus}, podem entrar {_ponto.PodemEntrar}, esperando {_ponto.Esperando}; portas {string.Join(", ", Veiculo.Portas.Todas)}\n");
                        Checar($"embarque visto pelo SDK ({_embarcaram} evento(s), a bordo {Passageiros.ABordo}, catraca {Passageiros.EmbarcaramSessao}, varredura {Passageiros.Dentro.Count})",
                            _embarcaram > 0 && Passageiros.Dentro.Count > 0);
                        Proxima();
                    }
                    break;
                case 17: // comandos novos e pedidos de condução
                    Comandos17();
                    break;
                case 18: // objetos do ônibus e mídia (texturas, sons, modelos)
                    Midia18();
                    break;
                case 19: // mundo: hora, relógio, clima, chuva; trânsito e semáforos
                    Mundo19();
                    break;
                case 20: // versão não testada: modo seguro
                    if (_frames == 1) { VersaoDoJogo.ForcarDesconhecida = true; _fisicaAntes = _passosFisica; }
                    if (_frames == 60)
                    {
                        bool semGanchos = _passosFisica == _fisicaAntes;
                        Checar($"versão não testada: modo seguro (ganchos parados {semGanchos}, Avancado nulo {Avancado.Controles == null}, aviso {Tem("PBSLoaderCommonUtils/versao-do-jogo", Gravidade.Falha)})",
                            Sdk.ModoSeguro && semGanchos && Avancado.Controles == null && Avancado.Vpp == null && Tem("PBSLoaderCommonUtils/versao-do-jogo", Gravidade.Falha));
                        VersaoDoJogo.Permitir.Value = true;
                    }
                    if (_frames == 90)
                    {
                        Checar($"jogador libera a versão: sai do modo seguro (Avancado {Avancado.Controles != null})", !Sdk.ModoSeguro && Avancado.Controles != null);
                        VersaoDoJogo.Permitir.Value = false;
                        VersaoDoJogo.ForcarDesconhecida = false;
                        _fisicaAntes = _passosFisica;
                    }
                    if (_frames == 150)
                    {
                        Checar($"de volta à versão testada, ganchos voltam ({_passosFisica - _fisicaAntes} passos)", !Sdk.ModoSeguro && _passosFisica > _fisicaAntes && !Tem("PBSLoaderCommonUtils/versao-do-jogo", null));
                        Proxima();
                    }
                    break;
                case 21:
                    Checar($"ganchos dispararam (física {_passosFisica}, rodas {_rodasVisuais}, som do motor {_sonsMotor}, portas {_portasPedidas})",
                        _passosFisica > 0 && _rodasVisuais > 0 && _portasPedidas > 0);
                    _r.Append("\n[sdk.txt]\n").Append(Diagnostico.Texto());
                    TeclasDoJogo.Reler();
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
        static bool _janelaAntes, _limitadorAntes, _letreiroDefinido;
        static int _horaAntes, _minAntes, _horaAlvo;
        static bool _chuvaAntes;
        static string _climaAntes, _climaAlvo;

        static void Mundo19()
        {
            if (_frames == 1)
            {
                _r.Append("\n[mundo: escrita, trânsito e semáforos]\n");
                _horaAntes = Mundo.Tempo.Hora; _minAntes = Mundo.Tempo.Minuto; _chuvaAntes = Mundo.Clima.Chovendo; _climaAntes = Mundo.Clima.Nome;
                _horaAlvo = (_horaAntes + 5) % 24;
                var presets = Mundo.Clima.Presets;
                _climaAlvo = null;
                foreach (var p in presets) if (p != _climaAntes) { _climaAlvo = p; break; }
                _r.Append($"climas do céu: {string.Join(", ", presets)}\n");
                Mundo.Tempo.Definir(_horaAlvo, 30);
                Mundo.Tempo.Pausar(true);
                Mundo.Clima.Chover(_chuvaAntes ? Mundo.Chuva.Nenhuma : Mundo.Chuva.Media);
            }
            if (_frames == 90)
            {
                Checar($"hora definida ({_horaAntes:00}:{_minAntes:00} -> {Mundo.Tempo.Texto}, pedida {_horaAlvo:00}:30)", Mundo.Tempo.Hora == _horaAlvo && Math.Abs(Mundo.Tempo.Minuto - 30) <= 1);
                Checar($"relógio parado ({Mundo.Tempo.Parado})", Mundo.Tempo.Parado);
                // O clima foi pedido junto com a chuva; o que vale no fim é o da chuva.

                var vs = Mundo.Trafego.Veiculos;
                int carros = 0, onibus = 0;
                foreach (var v in vs) if (v.Tipo == Mundo.TipoNoTrafego.Onibus) onibus++; else carros++;
                _r.Append($"trânsito: {vs.Count} veículos ({carros} carros, {onibus} ônibus; o jogo conta {Mundo.Trafego.Carros} carros e {Mundo.Trafego.Onibus} ônibus), {Mundo.Trafego.PedestresAtivos.Count} pedestres, {Mundo.Trafego.Semaforos.Count} semáforos\n");
                for (int i = 0; i < vs.Count && i < 5; i++) _r.Append("  ").Append(vs[i]).Append('\n');
                var frente = Mundo.Trafego.MaisPertoAFrente(300f);
                _r.Append($"mais perto à frente: {(frente != null ? frente.ToString() : "-")}\n");
                if (Mundo.Trafego.Carros + Mundo.Trafego.Onibus > 0)
                    Checar($"veículos do trânsito encontrados ({vs.Count}, o jogo conta {Mundo.Trafego.Carros + Mundo.Trafego.Onibus})", vs.Count > 0);
                _r.Append("[semáforos]\n").Append(Mundo.Trafego.DiagnosticoSemaforos(6));
            }
            if (_frames == 91)
            {
                Mundo.Tempo.Pausar(false);
                Mundo.Tempo.Definir(_horaAntes, _minAntes);
            }
            // A chuva vem com a transição do céu: confere depois de uns segundos.
            if (_frames == 400)
            {
                _r.Append($"céu depois de pedir chuva: \"{Mundo.Clima.Nome}\", molhado {Mundo.Clima.Molhado:0.00}, chave de chuva do jogo {Il2Cpp.MepMobileControls.TahChovendo}\n");
                if (!_chuvaAntes) Checar($"o jogo mantém a chave de chuva ligada ({Il2Cpp.MepMobileControls.TahChovendo})", Il2Cpp.MepMobileControls.TahChovendo);
                Checar($"chuva ligada/desligada ({_chuvaAntes} -> {Mundo.Clima.Chovendo}, {Mundo.Clima.Chuva})", Mundo.Clima.Chovendo != _chuvaAntes);
                if (_climaAlvo != null) Mundo.Clima.Definir(_climaAlvo);
                if (!_chuvaAntes) Il2Cpp.MepMobileControls.TahChovendo = false;
            }
            if (_frames == 520)
            {
                if (_climaAlvo != null) Checar($"clima definido (\"{_climaAntes}\" -> \"{Mundo.Clima.Nome}\", pedido \"{_climaAlvo}\")", Mundo.Clima.Nome == _climaAlvo);
                if (_climaAntes.Length > 0) Mundo.Clima.Definir(_climaAntes);
            }
            if (_frames == 580) Proxima();
        }
        static Midia.Som3D _som;
        static Material _matTeste;
        static Texture _texOriginal;

        static void Midia18()
        {
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");
            if (_frames == 1)
            {
                _r.Append("\n[objetos e mídia]\n");
                var pasta = Veiculo.Pasta;
                Checar($"pasta do mod do ônibus (\"{pasta}\")", pasta.Length > 0 && Directory.Exists(pasta));

                var rs = Veiculo.Raiz.GetComponentsInChildren<Renderer>(true);
                var alvo = rs.Length > 0 ? rs[rs.Length / 2] : null;
                Checar($"objetos pelo nome (\"{alvo?.name}\": achou {Veiculo.Objetos.Achar(alvo?.name ?? "") != null}, {Veiculo.Objetos.Todos("").Count} objetos, {Veiculo.Objetos.Renderizadores().Count} renderers)",
                    alvo != null && Veiculo.Objetos.Achar(alvo.name) != null && Veiculo.Objetos.Todos("").Count > 10);
                var mat = alvo != null && alvo.sharedMaterial != null ? Veiculo.Objetos.NomeDoMaterial(alvo.sharedMaterial) : "";
                var mats = Veiculo.Objetos.Materiais(mat);
                _matTeste = mats.Count > 0 ? mats[0] : null;
                _texOriginal = _matTeste != null && _matTeste.HasProperty("_MainTex") ? _matTeste.GetTexture("_MainTex") : null;
                int n = mat.Length > 0 ? Veiculo.Objetos.TrocarTextura(mat, Texture2D.whiteTexture) : 0;
                Checar($"troca de textura no material \"{mat}\" ({mats.Count} material(is), {n} trocado(s))",
                    n > 0 && _matTeste != null && _matTeste.GetTexture("_MainTex") == Texture2D.whiteTexture);
                if (_texOriginal != null) Veiculo.Objetos.TrocarTextura(mat, _texOriginal);

                var tex = Midia.Carregar.Textura(Path.Combine(dir, "avisos.png"), cache: false);
                Checar($"textura de arquivo ({(tex != null ? tex.width + "x" + tex.height : "nula")})", tex != null && tex.width > 0);

                var wav = Path.Combine(dir, "teste.wav");
                GravarWav(wav, 0.6f);
                var clip = Midia.Carregar.Som(wav);
                Checar($"som de arquivo ({(clip != null ? clip.length.ToString("0.00") + " s" : "nulo")})", clip != null && clip.length > 0.5f);
                _som = Midia.Sons.Tocar(clip, Veiculo.Raiz, Vector3.up * 2f, 0.3f);

                string modelo = null; long menor = long.MaxValue;
                if (pasta.Length > 0)
                    foreach (var ext in new[] { "*.3ds", "*.c1" })
                        foreach (var f in Directory.GetFiles(pasta, ext, SearchOption.AllDirectories))
                        {
                            long t = new FileInfo(f).Length;
                            if (t > 2000 && t < menor) { menor = t; modelo = f; }
                        }
                if (modelo == null) _r.Append("(sem .3ds/.c1 na pasta do ônibus: modelo não testado)\n");
                else
                {
                    var go = Midia.Carregar.Modelo(modelo);
                    int malhas = go != null ? go.GetComponentsInChildren<MeshFilter>(true).Length : 0;
                    Checar($"modelo de arquivo ({Path.GetFileName(modelo)}, {menor / 1024} KB, {malhas} malha(s))", go != null && malhas > 0);
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
            }
            if (_frames == 10) Checar("som 3D tocando preso ao ônibus", _som != null && _som.Tocando && _som.Fonte.transform.parent == Veiculo.Raiz);
            if (_frames == 120)
            {
                Checar("som de uma vez some sozinho ao terminar", _som != null && _som.Fonte == null);
                Proxima();
            }
        }

        static void GravarWav(string arquivo, float segundos)
        {
            const int taxa = 22050;
            int n = (int)(taxa * segundos);
            using var w = new BinaryWriter(File.Create(arquivo));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(taxa); w.Write(taxa * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            for (int i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / taxa) * 8000));
        }
        static float _vAntes;

        static void Comandos17()
        {
            const string D = "teste.conducao";
            switch (_frames)
            {
                case 1:
                    {
                        _r.Append("\n[comandos e condução]\n");
                        var nomes = new System.Collections.Generic.SortedSet<string>();
                        int n = 0;
                        foreach (var comp in Veiculo.Raiz.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            n++;
                            var t = comp.GetIl2CppType().Name;
                            var l = t.ToLowerInvariant();
                            if (l.Contains("etreir") || l.Contains("tiner") || l.Contains("rolante") || l.Contains("destino") || l.Contains("placa")) nomes.Add(t + " em " + comp.name);
                        }
                        _r.Append($"componentes do ônibus: {n}; ligados a letreiro: {(nomes.Count > 0 ? string.Join("; ", nomes) : "nenhum")}\n");
                        break;
                    }
                case 30:
                    {
                        var som = Avancado.ControlesJogo?.ASsomBuzina;
                        Checar($"buzina pelo pedido (tocando {som != null && som.isPlaying})", Veiculo.Cabine.Buzinando && som != null && som.isPlaying);
                        break;
                    }
                case 60:
                    Checar("buzina desliga", !Veiculo.Cabine.Buzinando);
                    Comandos.Limpadores();
                    break;
                case 120:
                    Checar($"limpadores ligam ({Veiculo.Cabine.Limpadores})", Veiculo.Cabine.Limpadores >= 1);
                    Comandos.Limpadores();
                    _janelaAntes = Veiculo.Cabine.JanelaMotoristaAberta;
                    Comandos.JanelaMotorista();
                    _limitadorAntes = Veiculo.Ajudas.LimitadorAtivo;
                    if (Veiculo.Ajudas.TemLimitador) Comandos.Limitador();
                    break;
                case 200:
                    Checar($"limpadores desligam ({Veiculo.Cabine.Limpadores})", Veiculo.Cabine.Limpadores == 0);
                    _r.Append($"janela do motorista: {_janelaAntes} -> {Veiculo.Cabine.JanelaMotoristaAberta}\n");
                    if (Veiculo.Ajudas.TemLimitador) Checar($"limitador alterna ({_limitadorAntes} -> {Veiculo.Ajudas.LimitadorAtivo})", Veiculo.Ajudas.LimitadorAtivo != _limitadorAntes);
                    else _r.Append("(sem limitador neste ônibus)\n");
                    _letreiroDefinido = Linhas.Letreiro.Definir(2);
                    if (Veiculo.Cabine.JanelaMotoristaAberta != _janelaAntes) Comandos.JanelaMotorista();
                    if (Veiculo.Ajudas.LimitadorAtivo != _limitadorAntes) Comandos.Limitador();
                    // Para andar: portas fechadas, freio de mão solto, primeira marcha.
                    foreach (var p in Veiculo.Portas.Todas) if (p.Aberta) Comandos.Porta(p);
                    if (Veiculo.Pedais.FreioDeMaoPuxado) Comandos.FreioDeMao();
                    Ar.DefinirPressao(8f);
                    break;
                case 250:
                    if (_letreiroDefinido) Checar($"letreiro pelo teclado do jogo ({Linhas.Letreiro.Indice}; {Linhas.Letreiro.Bruto()})", Linhas.Letreiro.Indice == 2);
                    else _r.Append($"(sem teclado de itinerário neste ônibus: letreiro não trocado; {Linhas.Letreiro.Bruto()})\n");
                    break;
                case 260:
                    if (!Comandos.Marcha(1)) _r.Append("câmbio manual: marcha pelo pedido de condução\n");
                    break;
                case 380:
                    _r.Append("marcha: " + Comandos.DiagnosticoMarcha() + "\n");
                    _vAntes = Veiculo.VelocidadeKmh;
                    Checar($"marcha 1 pelo comando ({Veiculo.Cambio.MarchaTexto}, manual {Veiculo.Cambio.Manual})", Veiculo.Cambio.Marcha == 1);
                    _vAntes = Veiculo.VelocidadeKmh;
                    break;
            }
            if (_frames < 30) Conducao.Buzina(D);
            if (_frames >= 260 && _frames <= 620 && Veiculo.Cambio.Manual) { Conducao.Marcha(D, 1); if (_frames < 300) Conducao.Embreagem(D, 1f); }
            if (_frames > 380 && _frames <= 500)
            {
                Conducao.Freio(D, 0f);
                Conducao.Acelerador(D, 0.6f);
                // Direção virada só um instante (para medir o esterço), depois reta: o ônibus está encostado no ponto.
                Conducao.Direcao(D, _frames <= 400 ? 0.5f : 0f);
                if (_frames == 400)
                {
                    float ang = 0f;
                    foreach (var r in Veiculo.Rodas.Todas) ang = Mathf.Max(ang, Mathf.Abs(r.AnguloGraus));
                    Checar($"pedido de condução chega ao VPP (acelerador {Veiculo.Pedais.Acelerador:0.00}, direção {Veiculo.Pedais.Direcao:0.00}, esterço {ang:0.0}°, por {Conducao.Donos})",
                        Mathf.Abs(Veiculo.Pedais.Acelerador - 0.6f) < 0.05f && Mathf.Abs(Veiculo.Pedais.Direcao - 0.5f) < 0.05f && ang > 1f);
                }
            }
            if (_frames == 500)
                Checar($"pedido de condução move o ônibus ({_vAntes:0.0} -> {Veiculo.VelocidadeKmh:0.0} km/h, {Veiculo.Motor.Rpm:0} rpm, marcha {Veiculo.Cambio.MarchaTexto})", Veiculo.VelocidadeKmh > _vAntes + 1f);
            if (_frames > 500 && _frames <= 620) Conducao.Freio(D, 1f);
            if (_frames == 620) Checar($"freio pelo pedido para o ônibus ({Veiculo.VelocidadeKmh:0.0} km/h)", Veiculo.VelocidadeKmh < 2f);
            if (_frames == 650)
            {
                Checar($"pedido vence sozinho ao parar de chamar (controlando {Conducao.Controlando}, acelerador {Veiculo.Pedais.Acelerador:0.00})", !Conducao.Controlando && Veiculo.Pedais.Acelerador < 0.05f);
                Comandos.Marcha(0);
                _r.Append("marcha depois de soltar: " + Comandos.DiagnosticoMarcha() + "\n");
                if (!Veiculo.Pedais.FreioDeMaoPuxado) Comandos.FreioDeMao();
                Proxima();
            }
        }

        static int _passosFisica, _rodasVisuais, _sonsMotor, _portasPedidas, _embarcaram, _acoesApertadas, _fisicaAntes;
        static Mapas.Parada _ponto;
        static float _tPonto;
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
            try { var b = Avancado.VppJogo?.m_brakes; return b != null ? b.handbrakeInput : -1f; }
            catch { return -1f; }
        }

        static void Chamar(string metodo)
        {
            var c = Avancado.ControlesJogo;
            if (c == null) return;
            var m = c.GetType().GetMethod(metodo, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m == null) { _r.Append($"(sem o método {metodo})\n"); return; }
            try { m.Invoke(c, null); } catch (Exception e) { _r.Append($"({metodo} falhou: {e.InnerException?.Message ?? e.Message})\n"); }
        }

        static bool TemTeclado()
        {
            var p = TeclasDoJogo.Jogador();
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
