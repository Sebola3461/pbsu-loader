using System;
using System.IO;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;

namespace PBSLoaderCommonUtils.Conteudo
{
    /// <summary>
    /// Base dos scripts que vêm junto com um ônibus ou mapa. Não herde desta: herde de <see cref="ScriptDeOnibus"/>
    /// ou <see cref="ScriptDeMapa"/>. A PBSLoaderCommonUtils cria uma instância de cada classe concreta da DLL do
    /// pacote (precisa de construtor sem parâmetros) e chama os métodos abaixo na thread principal.
    /// Se você se inscrever em eventos do SDK em <see cref="Ativar"/>, saia deles em <see cref="Desativar"/>.
    /// </summary>
    public abstract class Script
    {
        /// <summary>O pacote (pasta, configuração, Harmony, log) deste script.</summary>
        public Pacote Pacote { get; internal set; }

        /// <summary>O ônibus/mapa do pacote entrou em jogo. Também ao voltar para ele na mesma sessão.</summary>
        public virtual void Ativar() { }
        /// <summary>O ônibus/mapa saiu de jogo. Os patches do <see cref="Pacote.Harmony"/> são desfeitos logo depois.</summary>
        public virtual void Desativar() { }
        /// <summary>Todo quadro, enquanto ativo.</summary>
        public virtual void Quadro() { }
        /// <summary>Todo passo de física, enquanto ativo.</summary>
        public virtual void PassoFisico() { }
        /// <summary>Todo quadro, no LateUpdate, enquanto ativo.</summary>
        public virtual void QuadroTardio() { }
    }

    /// <summary>Script de um ônibus: ativo enquanto aquele ônibus é o veículo do jogador.</summary>
    public abstract class ScriptDeOnibus : Script { }

    /// <summary>Script de um mapa: ativo enquanto aquele mapa está carregado.</summary>
    public abstract class ScriptDeMapa : Script
    {
        /// <summary>Uma cena do mapa (tile) terminou de carregar.</summary>
        public virtual void TileCarregou(string cena) { }
    }

    public enum OrigemDoPacote { Onibus, Mapa }

    public enum EstadoDoPacote { AguardandoPermissao, Recusado, Ativo, Inativo, Falhou }

    /// <summary>Um pacote de scripts (pasta <c>scripts/</c> de um ônibus ou mapa, com <c>pbsu.json</c> e a DLL).</summary>
    public sealed class Pacote
    {
        public string Id { get; internal set; }
        public string Nome { get; internal set; }
        public string Autor { get; internal set; }
        public string Versao { get; internal set; }
        public string SdkMinimo { get; internal set; }
        public OrigemDoPacote Origem { get; internal set; }
        /// <summary>Pasta <c>scripts/</c> do pacote.</summary>
        public string Pasta { get; internal set; }
        /// <summary>SHA-256 da DLL (em hexadecimal minúsculo). A permissão do jogador vale para este hash.</summary>
        public string Sha256 { get; internal set; }
        public EstadoDoPacote Estado { get; internal set; }
        /// <summary>Motivo da falha ou da recusa.</summary>
        public string Detalhe { get; internal set; } = "";
        public bool Ativo => Estado == EstadoDoPacote.Ativo;

        internal string Dll;
        internal byte[] Bytes;
        internal System.Collections.Generic.List<Script> Scripts = new();
        /// <summary>Quantas classes de script da DLL viraram instâncias.</summary>
        public int QuantosScripts => Scripts.Count;

        /// <summary>Caminho de um arquivo do pacote. Não sai da pasta do pacote.</summary>
        public string Caminho(string relativo)
        {
            var raiz = Path.GetFullPath(Pasta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var c = Path.GetFullPath(Path.Combine(raiz, relativo));
            if (!c.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("fora da pasta do pacote: " + relativo);
            return c;
        }

        public string LerTexto(string relativo) => File.ReadAllText(Caminho(relativo));
        /// <summary>Textura da pasta do pacote (ver <see cref="Midia.Carregar.Textura"/>).</summary>
        public UnityEngine.Texture2D Textura(string relativo) => Midia.Carregar.Textura(Caminho(relativo));
        /// <summary>Som da pasta do pacote (ver <see cref="Midia.Carregar.Som"/>).</summary>
        public UnityEngine.AudioClip Som(string relativo) => Midia.Carregar.Som(Caminho(relativo));
        /// <summary>Modelo .3ds/.c1 da pasta do pacote (ver <see cref="Midia.Carregar.Modelo"/>).</summary>
        public UnityEngine.GameObject Modelo(string relativo) => Midia.Carregar.Modelo(Caminho(relativo));
        public byte[] LerBytes(string relativo) => File.ReadAllBytes(Caminho(relativo));
        public bool Existe(string relativo) { try { return File.Exists(Caminho(relativo)); } catch { return false; } }

        MelonPreferences_Category _config;
        /// <summary>Preferências do pacote, em UserData/PBSLoaderCommonUtils/Conteudo/&lt;id&gt;.cfg.</summary>
        public MelonPreferences_Category Config
        {
            get
            {
                if (_config != null) return _config;
                var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils", "Conteudo");
                Directory.CreateDirectory(dir);
                _config = MelonPreferences.CreateCategory("Conteudo." + Id, Nome);
                _config.SetFilePath(Path.Combine(dir, Id + ".cfg"), autoload: true, printmsg: false);
                return _config;
            }
        }

        /// <summary>
        /// Dados do pacote guardados entre sessões, separados pelo ônibus (ou mapa) em que ele está (ver <see cref="Gaveta"/>).
        /// </summary>
        public Gaveta Salvos => Origem == OrigemDoPacote.Onibus
            ? PBSLoaderCommonUtils.Salvos.DoOnibus("Conteudo." + Id)
            : PBSLoaderCommonUtils.Salvos.DoMapa("Conteudo." + Id);

        HarmonyLib.Harmony _harmony;
        /// <summary>Harmony do pacote. Tudo que for aplicado por ele é desfeito quando o pacote desativa.</summary>
        public HarmonyLib.Harmony Harmony => _harmony ??= new HarmonyLib.Harmony("pbsu.conteudo." + Id);

        MelonLogger.Instance _log;
        /// <summary>Log com o nome do pacote no MelonLoader/Latest.log.</summary>
        public MelonLogger.Instance Log => _log ??= new MelonLogger.Instance(Id);

        /// <summary>
        /// Registra um atalho do pacote (aparece na tela de atalhos da PBSLoaderCommonInterface como os dos mods). A ação só roda
        /// enquanto o pacote está ativo. Ver <see cref="Entrada.Atalhos.Registrar"/>.
        /// </summary>
        public Entrada.Atalho Atalho(string id, string nome, string padrao, Action acao, string descricao = null)
            => Entrada.Atalhos.Registrar(Id, id, nome, padrao, acao == null ? null : () => { if (Ativo) acao(); }, descricao);

        internal void DesfazerPatches()
        {
            try { _harmony?.UnpatchSelf(); } catch (Exception e) { Sdk.Registrar(e, "Conteudo." + Id + ".Unpatch"); }
        }

        public override string ToString() => $"{Nome} {Versao} ({Id}, {Origem}, {Estado}{(Detalhe.Length > 0 ? ": " + Detalhe : "")})";
    }
}
