using System;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using PBSLoaderCommonUtils.Conteudo;
using UnityEngine;

namespace PBSLoaderCommonInterface
{
    // Teste automático da API de interface (--pbsloadercommoninterface-teste-sdk): painéis, cartões de HUD, a pergunta de
    // permissão dos scripts de conteúdo e a página de scripts no menu Mods. Grava UserData/PBSLoaderCommonInterface/
    // teste_sdk.txt e prints sdk_*.png; com --pbsloadercommoninterface-sair fecha o jogo no fim. Para a parte dos scripts,
    // o ônibus precisa ter o ScriptDeOnibusExemplo em scripts/ (Exemplos, -p:PastaDeTeste=...).
    internal static class TesteSdk
    {
        static bool _ativo;
        static int _etapa = -1, _frames, _falhas, _geracao = -1;
        static readonly StringBuilder _r = new();
        static Painel _painel;
        static Widget _widget;
        static float _valor;
        static bool _chave;
        static Pacote _pacote;

        static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonInterface");

        internal static void Iniciar() => _ativo = Environment.CommandLine.Contains("--pbsloadercommoninterface-teste-sdk");

        static void Checar(string o, bool ok)
        {
            if (!ok) _falhas++;
            _r.Append(ok ? "ok     " : "FALHOU ").Append(o).Append('\n');
        }

        static void Print(string nome)
        {
            var arq = Path.Combine(Dir, "sdk_" + nome + ".png");
            try { File.Delete(arq); } catch { }
            ScreenCapture.CaptureScreenshot(arq);
            _r.Append("print ").Append(arq).Append('\n');
        }

        static void Proxima() { _etapa++; _frames = 0; }

        internal static void Quadro()
        {
            if (!_ativo) return;
            if (_etapa < 0)
            {
                if (!PBSLoaderCommonUtils.Veiculos.Veiculo.Existe) return;
                _etapa = 0; _frames = 0;
                _r.Append("Teste da API de interface (PBSLoaderCommonInterface)\n");
            }
            _frames++;
            switch (_etapa)
            {
                case 0: // o mesmo veículo por 300 quadros
                    if (PBSLoaderCommonUtils.Veiculos.Veiculo.Geracao != _geracao) { _geracao = PBSLoaderCommonUtils.Veiculos.Veiculo.Geracao; _frames = 0; }
                    if (_frames > 300) Proxima();
                    break;

                case 1: // painel declarativo com todos os tipos de item
                    if (_frames == 1)
                    {
                        _painel = Paineis.Criar("PBSLoaderCommonInterface", "TesteSdk", "Teste do SDK", "Ctrl+Alt+Shift+F8")
                            .Secao("Leituras")
                            .Valor("Velocidade", () => $"{PBSLoaderCommonUtils.Veiculos.Veiculo.VelocidadeKmh:0} km/h", "do SDK")
                            .Barra("Rotação", () => PBSLoaderCommonUtils.Veiculos.Veiculo.Motor.Rpm / Mathf.Max(1f, PBSLoaderCommonUtils.Veiculos.Veiculo.Motor.RpmMax))
                            .Texto(() => "Texto corrido recalculado a cada quadro: " + Time.frameCount)
                            .Secao("Controles")
                            .Chave("Chave de teste", () => _chave, v => _chave = v)
                            .Faixa("Faixa de teste", () => _valor, v => _valor = v, 0f, 10f, 0.5f)
                            .Botao("Botão de teste", () => _chave = !_chave, "Alternar");
                        _painel.Abrir();
                    }
                    if (_frames == 60)
                    {
                        var j = _painel.Janela;
                        int linhas = j != null ? j.GetComponentsInChildren<UnityEngine.UI.LayoutElement>(true).Length : 0;
                        Checar($"painel aberto ({_painel.Itens.Count} itens, janela ativa {j != null && j.activeInHierarchy}, {linhas} elementos)",
                            j != null && j.activeInHierarchy && _painel.Itens.Count == 8);
                        Checar("painel solta o cursor", PBSLoaderCommonUtils.Entrada.Mouse.Solto);
                        Checar("painel aparece como ação no menu Mods", PainelMods.Mods.TryGetValue("PBSLoaderCommonInterface", out var i) && i.acoes.Any(a => a.rotulo == "Teste do SDK"));
                        Print("painel");
                    }
                    if (_frames == 70)
                    {
                        _painel.Fechar();
                        Proxima();
                    }
                    break;

                case 2: // cartão de HUD
                    if (_frames == 1) _widget = Hud.Adicionar("PBSLoaderCommonInterface", "teste", "Teste do SDK", () => $"{Time.frameCount % 1000}", 0);
                    if (_frames == 60)
                    {
                        Checar($"cartão de HUD na tela (\"{_widget.TextoValor?.text}\")", _widget.Raiz != null && _widget.Raiz.gameObject.activeInHierarchy && (_widget.TextoValor?.text.Length ?? 0) > 0);
                        Print("hud");
                    }
                    if (_frames == 70) { _widget.Remover(); Proxima(); }
                    break;

                case 3: // pergunta de permissão do script do ônibus
                    if (_frames == 1) _pacote = Pacotes.Todos.FirstOrDefault(p => p.Origem == OrigemDoPacote.Onibus);
                    if (_pacote == null)
                    {
                        if (_frames < 300) break;
                        _r.Append("(o ônibus não tem scripts/: permissão não testada)\n");
                        _etapa = 7; _frames = 0;
                        break;
                    }
                    if (_frames < 60) break;
                    if (_frames == 60)
                    {
                        _r.Append($"pacote: {_pacote}, permissão gravada {Pacotes.Permissao(_pacote)}\n");
                        if (_pacote.Estado == EstadoDoPacote.AguardandoPermissao)
                        {
                            Checar("pergunta de permissão no visual da interface", Confirmacao.DesenhadaPorOutroMod && Confirmacao.Atual == _pacote && PermissaoScripts.Diagnostico().Contains(_pacote.Nome));
                            Print("permissao");
                        }
                        else _r.Append("(já havia decisão gravada para este pacote: sem pergunta)\n");
                    }
                    if (_frames == 70)
                    {
                        Pacotes.Permitir(_pacote);
                        Proxima();
                    }
                    break;

                case 4:
                    if (_frames == 60)
                    {
                        Checar($"permitido, o script do ônibus roda ({_pacote}, {_pacote.QuantosScripts} script(s))", _pacote.Ativo);
                        Checar("atalho do pacote registrado", PBSLoaderCommonUtils.Entrada.Atalhos.Achar(_pacote.Id, "Alerta") != null);
                        TelaMods.Abrir("PBSU.Scripts");
                    }
                    if (_frames == 120) { Print("scripts"); }
                    if (_frames == 130) { TelaMods.Fechar(); Proxima(); }
                    break;

                case 5: // bloquear desliga na hora
                    if (_frames == 1) Pacotes.Revogar(_pacote);
                    if (_frames == 30)
                    {
                        Checar($"bloquear desliga o pacote ({_pacote})", !_pacote.Ativo && _pacote.Estado == EstadoDoPacote.Recusado && Pacotes.Permissao(_pacote) == Decisao.SempreRecusar);
                        Pacotes.Permitir(_pacote); // devolve como estava para o próximo teste
                    }
                    if (_frames == 60)
                    {
                        Checar($"permitir de novo reativa ({_pacote})", _pacote.Ativo);
                        Proxima();
                    }
                    break;

                case 6:
                    Proxima();
                    break;

                case 7:
                    _r.Append("\n[painéis]\n").Append(Paineis.Diagnostico());
                    _r.Append("[cartões]\n").Append(Hud.Diagnostico());
                    _r.Append(_falhas == 0 ? "\nRESULTADO: tudo certo\n" : $"\nRESULTADO: {_falhas} falha(s)\n");
                    try
                    {
                        Directory.CreateDirectory(Dir);
                        File.WriteAllText(Path.Combine(Dir, "teste_sdk.txt"), _r.ToString());
                    }
                    catch (Exception e) { MelonLogger.Warning("[TesteSdk] " + e.Message); }
                    MelonLogger.Msg("[TesteSdk] " + (_falhas == 0 ? "tudo certo" : _falhas + " falha(s)"));
                    _ativo = false;
                    if (Environment.CommandLine.Contains("--pbsloadercommoninterface-sair")) Application.Quit();
                    break;
            }
        }
    }
}
