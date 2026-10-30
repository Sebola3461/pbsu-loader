using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MelonLoader;
using PBSLoaderCommonUtils.Interface;
using PBSLoaderCommonUtils.Mapas;
using PBSLoaderCommonUtils.Partida;

namespace PBSLoaderCommonUtils.Conteudo
{
    /// <summary>Pacotes de scripts que a PBSLoaderCommonUtils encontrou nesta sessão.</summary>
    public static class Pacotes
    {
        /// <summary>Todos os vistos nesta sessão (ativos ou não), na ordem em que apareceram.</summary>
        public static IReadOnlyList<Pacote> Todos => Carregador.Vistos;

        /// <summary>
        /// O jogador permite o pacote (grava a decisão para este hash). Se o ônibus/mapa dele estiver em jogo, ativa na hora.
        /// Se a pergunta dele estiver aberta, é o mesmo que responder "Permitir".
        /// </summary>
        public static void Permitir(Pacote p) => Carregador.Permitir(p);

        /// <summary>O jogador revoga a permissão (grava "nunca" para este hash) e o pacote desativa na hora.</summary>
        public static void Revogar(Pacote p) => Carregador.Revogar(p);

        /// <summary>A permissão gravada para o pacote agora.</summary>
        public static Decisao Permissao(Pacote p) => p == null || string.IsNullOrEmpty(p.Sha256) ? Decisao.Perguntar : Confianca.Consultar(p.Sha256);
    }

    // Acha a pasta scripts/ do ônibus e do mapa atuais, pede permissão, carrega a DLL (uma vez por hash na sessão)
    // e liga/desliga os scripts conforme o ônibus/mapa entra e sai de jogo.
    internal static class Carregador
    {
        internal static readonly List<Pacote> Vistos = new();
        static readonly Dictionary<string, Assembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<Script, int> _falhas = new();
        const int MaxFalhas = 5;

        static Pacote _doOnibus, _doMapa;
        static string _pastaOnibus, _pastaMapa;
        static int _geracaoOnibus = -1, _geracaoMapa = -1;

        internal static void Iniciar()
        {
            Sdk.CenaCarregou += cena =>
            {
                if (_doMapa == null || !_doMapa.Ativo) return;
                foreach (var s in _doMapa.Scripts.ToArray()) if (s is ScriptDeMapa m) Chamar(_doMapa, s, _ => m.TileCarregou(cena), "TileCarregou");
            };
        }

        internal static void Quadro()
        {
            Confirmacao.Quadro();

            // Ônibus: o pacote segue o veículo do jogador.
            if (_geracaoOnibus != Veiculo.Geracao || (!Veiculo.Existe && _doOnibus != null))
            {
                string pasta = Veiculo.Existe ? PastaDoOnibus() : null;
                if (Veiculo.Existe) _geracaoOnibus = Veiculo.Geracao;
                if (pasta != _pastaOnibus || !Veiculo.Existe)
                {
                    Trocar(ref _doOnibus, null);
                    _pastaOnibus = pasta;
                    if (pasta != null) Trocar(ref _doOnibus, Abrir(pasta, OrigemDoPacote.Onibus));
                }
                else Reativar(_doOnibus);
            }

            // Mapa: o pacote segue Mapa.Geracao.
            if (_geracaoMapa != Mapa.Geracao || (!Mapa.Existe && _doMapa != null))
            {
                string pasta = Mapa.Existe ? PastaDoMapa() : null;
                if (Mapa.Existe) _geracaoMapa = Mapa.Geracao;
                if (pasta != _pastaMapa || !Mapa.Existe)
                {
                    Trocar(ref _doMapa, null);
                    _pastaMapa = pasta;
                    if (pasta != null) Trocar(ref _doMapa, Abrir(pasta, OrigemDoPacote.Mapa));
                }
                else Reativar(_doMapa);
            }

            Cada(QuadroDe, "Quadro");
        }

        static readonly Action<Script> QuadroDe = s => s.Quadro(), PassoDe = s => s.PassoFisico(), TardioDe = s => s.QuadroTardio();
        internal static void PassoFisico() => Cada(PassoDe, "PassoFisico");
        internal static void QuadroTardio() => Cada(TardioDe, "QuadroTardio");

        // Mesmo ônibus/mapa, carregado de novo (outra geração): os scripts recomeçam com o veículo/mapa novo.
        static void Reativar(Pacote p)
        {
            if (p == null) return;
            if (p.Ativo) Desativar(p);
            if (p.Estado == EstadoDoPacote.Inativo) Ativar(p);
        }

        static void Cada(Action<Script> a, string onde)
        {
            for (int k = 0; k < 2; k++)
            {
                var p = k == 0 ? _doOnibus : _doMapa;
                if (p == null || !p.Ativo) continue;
                var lista = p.Scripts;
                for (int i = 0; i < lista.Count && p.Ativo; i++) Chamar(p, lista[i], a, onde);
            }
        }

        // Exceções seguidas no mesmo script desligam o pacote (um script quebrado não pode travar o jogo a cada quadro).
        static void Chamar(Pacote p, Script s, Action<Script> a, string onde)
        {
            try { a(s); if (_falhas.Count > 0) _falhas.Remove(s); }
            catch (Exception e)
            {
                Sdk.Registrar(e, $"Conteudo.{p.Id}.{s.GetType().Name}.{onde}");
                _falhas.TryGetValue(s, out int n);
                _falhas[s] = ++n;
                if (n >= MaxFalhas) Falhar(p, $"{s.GetType().Name}.{onde}: {e.GetType().Name}: {e.Message}");
            }
        }

        internal static void Permitir(Pacote p)
        {
            if (p == null || string.IsNullOrEmpty(p.Sha256) || p.Estado == EstadoDoPacote.Falhou) return;
            if (Confirmacao.Atual == p) { Confirmacao.Responder(Decisao.Permitir); return; }
            Confianca.Decidir(p, Decisao.Permitir);
            if (p.Ativo) return;
            p.Estado = EstadoDoPacote.Inativo; p.Detalhe = "";
            if (p == _doOnibus || p == _doMapa) Ativar(p);
        }

        internal static void Revogar(Pacote p)
        {
            if (p == null || string.IsNullOrEmpty(p.Sha256)) return;
            if (Confirmacao.Atual == p) { Confirmacao.Responder(Decisao.SempreRecusar); return; }
            Confianca.Decidir(p, Decisao.SempreRecusar);
            if (p.Ativo) Desativar(p);
            if (p.Estado != EstadoDoPacote.Falhou) { p.Estado = EstadoDoPacote.Recusado; p.Detalhe = "permissão revogada"; }
        }

        static void Falhar(Pacote p, string motivo)
        {
            Desativar(p);
            p.Estado = EstadoDoPacote.Falhou; p.Detalhe = motivo;
            MelonLogger.Warning($"[Conteudo] {p.Nome} desligado: {motivo}");
            Avisos.Notificar("PBSLoaderCommonUtils", "conteudo-" + p.Id, Gravidade.Falha, $"Scripts de \"{p.Nome}\" desligados", motivo, 15f);
        }

        static void Trocar(ref Pacote atual, Pacote novo)
        {
            if (atual != null && atual.Ativo) Desativar(atual);
            atual = novo;
            if (novo == null) return;
            if (novo.Estado == EstadoDoPacote.Falhou || novo.Estado == EstadoDoPacote.Recusado) return;
            switch (Confianca.Consultar(novo.Sha256))
            {
                case Decisao.Permitir: Ativar(novo); break;
                case Decisao.Recusar:
                case Decisao.SempreRecusar: novo.Estado = EstadoDoPacote.Recusado; novo.Detalhe = "recusado pelo jogador"; break;
                default:
                    novo.Estado = EstadoDoPacote.AguardandoPermissao;
                    var p = novo;
                    Confirmacao.Perguntar(p, d =>
                    {
                        if (d != Decisao.Permitir) { p.Estado = EstadoDoPacote.Recusado; p.Detalhe = "recusado pelo jogador"; return; }
                        // Só ativa se o ônibus/mapa dele ainda estiver em jogo.
                        if (p == _doOnibus || p == _doMapa) Ativar(p); else p.Estado = EstadoDoPacote.Inativo;
                    });
                    break;
            }
        }

        // ---------- pasta, manifesto, DLL ----------

        static Pacote Abrir(string pasta, OrigemDoPacote origem)
        {
            foreach (var v in Vistos)
                if (string.Equals(v.Pasta, pasta, StringComparison.OrdinalIgnoreCase))
                {
                    // Mesma pasta: reusa, a menos que a DLL tenha mudado no disco.
                    var sha = Sha(File.Exists(v.Dll) ? File.ReadAllBytes(v.Dll) : Array.Empty<byte>());
                    if (sha == v.Sha256) return v;
                    Vistos.Remove(v);
                    break;
                }
            var p = new Pacote { Pasta = pasta, Origem = origem, Estado = EstadoDoPacote.Inativo };
            try
            {
                var man = JsonSerializer.Deserialize<Manifesto>(File.ReadAllText(Path.Combine(pasta, "pbsu.json")),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (man == null || string.IsNullOrWhiteSpace(man.Id) || string.IsNullOrWhiteSpace(man.Dll))
                    throw new InvalidDataException("pbsu.json precisa de \"id\" e \"dll\"");
                p.Id = Limpar(man.Id); p.Nome = string.IsNullOrWhiteSpace(man.Nome) ? p.Id : man.Nome;
                p.Autor = string.IsNullOrWhiteSpace(man.Autor) ? "autor desconhecido" : man.Autor;
                p.Versao = man.Versao ?? "?"; p.SdkMinimo = man.SdkMinimo ?? "";
                p.Dll = p.Caminho(man.Dll);
                p.Bytes = File.ReadAllBytes(p.Dll);
                p.Sha256 = Sha(p.Bytes);
                if (p.SdkMinimo.Length > 0 && Version.TryParse(p.SdkMinimo, out var min) && Version.TryParse(Sdk.Versao, out var atual) && atual < min)
                {
                    p.Estado = EstadoDoPacote.Falhou;
                    p.Detalhe = $"precisa da PBSLoaderCommonUtils {p.SdkMinimo} (instalada {Sdk.Versao})";
                }
            }
            catch (Exception e)
            {
                p.Id ??= Path.GetFileName(Path.GetDirectoryName(pasta)) ?? "pacote";
                p.Nome ??= p.Id; p.Sha256 ??= "";
                p.Estado = EstadoDoPacote.Falhou; p.Detalhe = e.Message;
            }
            Vistos.Add(p);
            MelonLogger.Msg($"[Conteudo] encontrado: {p} em {pasta}");
            if (p.Estado == EstadoDoPacote.Falhou)
                Avisos.Notificar("PBSLoaderCommonUtils", "conteudo-" + p.Id, Gravidade.Falha, $"Scripts de \"{p.Nome}\" não carregaram", p.Detalhe, 15f);
            return p;
        }

        sealed class Manifesto
        {
            public string Id { get; set; }
            public string Nome { get; set; }
            public string Autor { get; set; }
            public string Versao { get; set; }
            public string SdkMinimo { get; set; }
            public string Dll { get; set; }
        }

        static void Ativar(Pacote p)
        {
            try
            {
                if (p.Scripts.Count == 0)
                {
                    if (!_assemblies.TryGetValue(p.Sha256, out var asm))
                    {
                        asm = Assembly.Load(p.Bytes);
                        _assemblies[p.Sha256] = asm;
                    }
                    var base_ = p.Origem == OrigemDoPacote.Onibus ? typeof(ScriptDeOnibus) : typeof(ScriptDeMapa);
                    Type[] tipos;
                    try { tipos = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { tipos = Array.FindAll(e.Types, t => t != null); }
                    foreach (var t in tipos)
                    {
                        if (t.IsAbstract || !base_.IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                        var s = (Script)Activator.CreateInstance(t);
                        s.Pacote = p;
                        p.Scripts.Add(s);
                    }
                    if (p.Scripts.Count == 0)
                        throw new InvalidDataException($"a DLL não tem nenhuma classe {base_.Name} com construtor sem parâmetros");
                }
                p.Estado = EstadoDoPacote.Ativo; p.Detalhe = "";
                foreach (var s in p.Scripts.ToArray())
                {
                    try { s.Ativar(); }
                    catch (Exception e) { Falhar(p, $"{s.GetType().Name}.Ativar: {e.GetType().Name}: {e.Message}"); return; }
                }
                MelonLogger.Msg($"[Conteudo] ativo: {p.Nome} ({p.Scripts.Count} script(s))");
            }
            catch (Exception e) { Falhar(p, e.InnerException?.Message ?? e.Message); }
        }

        static void Desativar(Pacote p)
        {
            if (p.Ativo)
                foreach (var s in p.Scripts.ToArray())
                {
                    try { s.Desativar(); } catch (Exception e) { Sdk.Registrar(e, $"Conteudo.{p.Id}.{s.GetType().Name}.Desativar"); }
                }
            p.DesfazerPatches();
            Avisos.Limpar(p.Id);
            if (p.Ativo) { p.Estado = EstadoDoPacote.Inativo; MelonLogger.Msg($"[Conteudo] inativo: {p.Nome}"); }
        }

        static string PastaDoOnibus()
        {
            var p = Veiculo.Pasta;
            if (p.Length == 0) return null;
            var s = Path.Combine(p, "scripts");
            return File.Exists(Path.Combine(s, "pbsu.json")) ? s : null;
        }

        static string PastaDoMapa()
        {
            if (Mapa.Origem != OrigemDoMapa.Mod || Mapa.Pasta.Length == 0) return null;
            try
            {
                var s = Path.Combine(Mapa.Pasta, "scripts");
                return File.Exists(Path.Combine(s, "pbsu.json")) ? Path.GetFullPath(s) : null;
            }
            catch { return null; }
        }

        static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

        static string Limpar(string id)
        {
            var sb = new StringBuilder();
            foreach (var c in id) sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        internal static string Resumo()
        {
            if (Vistos.Count == 0) return "nenhum pacote de scripts nesta sessão\n";
            var sb = new StringBuilder();
            foreach (var p in Vistos) sb.Append(p).Append(" sha ").Append(p.Sha256.Length >= 12 ? p.Sha256.Substring(0, 12) : "-").Append('\n');
            return sb.ToString();
        }
    }
}
