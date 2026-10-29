using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PbsuLoader
{
    enum Situacao { NaoInstalado, Atualizado, Atualizacao, Diferente, SemLancamento, Erro }

    /// <summary>Um mod do catálogo cruzado com o último release e com o que está na pasta do jogo.</summary>
    class EstadoMod
    {
        public ModDoCatalogo Mod;
        public Lancamento Remoto;
        public Registro.Item Instalado;
        public Situacao Situacao;
        public string Detalhe = "";

        public bool Presente => Instalado != null || Situacao == Situacao.Diferente;
        public bool PrecisaBaixar => Remoto != null && (Situacao == Situacao.NaoInstalado || Situacao == Situacao.Atualizacao || Situacao == Situacao.Diferente);
    }

    /// <summary>
    /// O que o instalador pôs no jogo, em UserData/pbsu-loader/instalado.json. Só apaga o que estiver aqui (ou a DLL com
    /// o nome do catálogo em Mods), nunca outros arquivos.
    /// </summary>
    class Registro
    {
        public class Item
        {
            public string Versao, Sha256;                                     // do asset baixado
            public Dictionary<string, string> Arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // caminho relativo -> SHA-256
        }

        public Dictionary<string, Item> Mods = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        readonly string _arquivo;

        Registro(string jogo) { _arquivo = Path.Combine(jogo, "UserData", "pbsu-loader", "instalado.json"); }

        public static Registro Abrir(string jogo)
        {
            var r = new Registro(jogo);
            if (!File.Exists(r._arquivo)) return r;
            var raiz = Json.Ler(File.ReadAllText(r._arquivo, Encoding.UTF8));
            if (Json.Obj(raiz, "mods") is Dictionary<string, object> mods)
                foreach (var p in mods)
                {
                    var it = new Item { Versao = Json.Texto(p.Value, "versao"), Sha256 = Json.Texto(p.Value, "sha256") };
                    if (Json.Obj(p.Value, "arquivos") is Dictionary<string, object> arqs)
                        foreach (var a in arqs) it.Arquivos[a.Key] = a.Value as string;
                    r.Mods[p.Key] = it;
                }
            return r;
        }

        public void Salvar()
        {
            var mods = new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Mods)
                mods[p.Key] = new SortedDictionary<string, object>
                {
                    ["versao"] = p.Value.Versao,
                    ["sha256"] = p.Value.Sha256,
                    ["arquivos"] = new SortedDictionary<string, object>(p.Value.Arquivos.ToDictionary(a => a.Key, a => (object)a.Value), StringComparer.OrdinalIgnoreCase),
                };
            Directory.CreateDirectory(Path.GetDirectoryName(_arquivo));
            File.WriteAllText(_arquivo, Json.Escrever(new SortedDictionary<string, object> { ["formato"] = 1, ["mods"] = mods }) + "\n", new UTF8Encoding(false));
        }
    }

    static class Instalacao
    {
        static bool EhDll(Origem o) => o.Arquivo.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        static string Absoluto(string jogo, string relativo) => Path.Combine(jogo, relativo.Replace('/', Path.DirectorySeparatorChar));

        public static EstadoMod Avaliar(ModDoCatalogo mod, string jogo, Registro reg, bool aceitarPre)
        {
            var e = new EstadoMod { Mod = mod };
            reg.Mods.TryGetValue(mod.Id, out e.Instalado);
            try { e.Remoto = Rede.UltimoLancamento(mod.Origem, aceitarPre); }
            catch (Exception ex) { e.Detalhe = "não consegui consultar o GitHub: " + ex.Message; }

            if (e.Instalado != null)
            {
                var faltando = e.Instalado.Arquivos.Keys.Where(a => !File.Exists(Absoluto(jogo, a))).ToList();
                var alterados = e.Instalado.Arquivos.Where(a => File.Exists(Absoluto(jogo, a.Key)) && a.Value != null
                    && !string.Equals(Rede.Sha256(Absoluto(jogo, a.Key)), a.Value, StringComparison.OrdinalIgnoreCase)).Select(a => a.Key).ToList();
                if (faltando.Count > 0) { e.Situacao = Situacao.Diferente; e.Detalhe = "faltam arquivos: " + string.Join(", ", faltando); }
                else if (alterados.Count > 0) { e.Situacao = Situacao.Diferente; e.Detalhe = "arquivo trocado fora do instalador: " + string.Join(", ", alterados); }
                else if (e.Remoto == null) e.Situacao = e.Detalhe.Length > 0 ? Situacao.Erro : Situacao.SemLancamento;
                else if (e.Remoto.Sha256 != null ? string.Equals(e.Remoto.Sha256, e.Instalado.Sha256, StringComparison.OrdinalIgnoreCase) : e.Remoto.Versao == e.Instalado.Versao)
                    e.Situacao = Situacao.Atualizado;
                else e.Situacao = Situacao.Atualizacao;
                return e;
            }

            // Sem registro: pode ter sido copiada à mão para Mods.
            string dll = Absoluto(jogo, "Mods/" + mod.Origem.Arquivo);
            if (EhDll(mod.Origem) && File.Exists(dll))
            {
                string sha = Rede.Sha256(dll);
                if (e.Remoto?.Sha256 != null && string.Equals(sha, e.Remoto.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    e.Situacao = Situacao.Atualizado;
                    e.Instalado = new Registro.Item { Versao = e.Remoto.Versao, Sha256 = sha };
                    e.Instalado.Arquivos["Mods/" + mod.Origem.Arquivo] = sha;
                    reg.Mods[mod.Id] = e.Instalado; // adota: vai para o registro no próximo Salvar
                }
                else
                {
                    e.Situacao = Situacao.Diferente;
                    e.Detalhe = "Mods/" + mod.Origem.Arquivo + " não veio do instalador (compilada à mão ou de outra fonte)";
                }
                return e;
            }

            if (e.Remoto == null) e.Situacao = e.Detalhe.Length > 0 ? Situacao.Erro : Situacao.SemLancamento;
            else e.Situacao = Situacao.NaoInstalado;
            if (e.Situacao == Situacao.SemLancamento) e.Detalhe = "nenhum release com " + mod.Origem.Arquivo + " em " + mod.Origem.Repo;
            return e;
        }

        public static void Instalar(EstadoMod e, string jogo, Registro reg, Action<string> log)
        {
            var o = e.Mod.Origem;
            log("Baixando " + e.Mod.Nome + " " + e.Remoto.Versao + "...");
            string tmp = Rede.Baixar(e.Remoto.Url, e.Remoto.Sha256);
            try
            {
                var novo = new Registro.Item { Versao = e.Remoto.Versao, Sha256 = Rede.Sha256(tmp) };
                if (EhDll(o))
                {
                    string rel = "Mods/" + o.Arquivo;
                    Directory.CreateDirectory(Path.Combine(jogo, "Mods"));
                    File.Copy(tmp, Absoluto(jogo, rel), true);
                    novo.Arquivos[rel] = novo.Sha256;
                }
                else
                {
                    string raiz = Path.GetFullPath(jogo).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    using (var z = ZipFile.OpenRead(tmp))
                        foreach (var entrada in z.Entries)
                        {
                            if (entrada.FullName.EndsWith("/")) continue;
                            string destino = Path.GetFullPath(Path.Combine(raiz, entrada.FullName));
                            if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("o zip tenta escrever fora da pasta do jogo: " + entrada.FullName);
                            Directory.CreateDirectory(Path.GetDirectoryName(destino));
                            entrada.ExtractToFile(destino, true);
                            novo.Arquivos[destino.Substring(raiz.Length).Replace('\\', '/')] = Rede.Sha256(destino);
                        }
                }
                // Arquivos da versão antiga que a nova não tem mais.
                if (e.Instalado != null)
                    foreach (var velho in e.Instalado.Arquivos.Keys.Where(a => !novo.Arquivos.ContainsKey(a) && !DeOutroMod(reg, e.Mod.Id, a)))
                        ApagarSeExistir(Absoluto(jogo, velho));
                reg.Mods[e.Mod.Id] = novo;
                reg.Salvar();
                log("  " + e.Mod.Nome + " " + novo.Versao + " instalado.");
            }
            finally { ApagarSeExistir(tmp); }
        }

        public static void Remover(EstadoMod e, string jogo, Registro reg, Action<string> log)
        {
            var arquivos = e.Instalado?.Arquivos.Keys.ToList() ?? new List<string>();
            if (arquivos.Count == 0 && EhDll(e.Mod.Origem)) arquivos.Add("Mods/" + e.Mod.Origem.Arquivo);
            foreach (var a in arquivos.Where(a => !DeOutroMod(reg, e.Mod.Id, a))) ApagarSeExistir(Absoluto(jogo, a));
            reg.Mods.Remove(e.Mod.Id);
            reg.Salvar();
            log("  " + e.Mod.Nome + " removido.");
        }

        // Dois zips podem trazer o mesmo arquivo (uma lib em UserLibs): ele fica enquanto outro mod o registrar.
        static bool DeOutroMod(Registro reg, string id, string relativo)
            => reg.Mods.Any(p => !string.Equals(p.Key, id, StringComparison.OrdinalIgnoreCase) && p.Value.Arquivos.ContainsKey(relativo));

        public static void InstalarMelon(Catalogo c, string jogo, Action<string> log)
        {
            string atual = Jogo.VersaoMelon(jogo);
            log("Baixando MelonLoader " + c.MelonVersao + "...");
            string tmp = Rede.Baixar(c.MelonUrl, c.MelonSha256);
            try
            {
                // Versão diferente: tira a anterior inteira. O MelonLoader recria o resto (Il2CppAssemblies, logs) ao abrir.
                if (atual != null && atual != c.MelonVersao)
                {
                    log("  Removendo o MelonLoader " + atual + ".");
                    RemoverMelonArquivos(jogo);
                }
                string raiz = Path.GetFullPath(jogo).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                using (var z = ZipFile.OpenRead(tmp))
                    foreach (var entrada in z.Entries)
                    {
                        string destino = Path.GetFullPath(Path.Combine(raiz, entrada.FullName));
                        if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) continue; // caminho que sai da pasta do jogo
                        if (entrada.FullName.EndsWith("/")) { Directory.CreateDirectory(destino); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destino));
                        entrada.ExtractToFile(destino, true);
                    }
                foreach (var d in new[] { "Mods", "Plugins", "UserData", "UserLibs" }) Directory.CreateDirectory(Path.Combine(jogo, d));
                log("  MelonLoader " + c.MelonVersao + " instalado. Na primeira abertura do jogo ele leva alguns minutos preparando os arquivos.");
            }
            finally { ApagarSeExistir(tmp); }
        }

        public static void RemoverMelon(string jogo, Action<string> log)
        {
            RemoverMelonArquivos(jogo);
            log("MelonLoader removido. As pastas Mods e UserData ficaram.");
        }

        static void RemoverMelonArquivos(string jogo)
        {
            ApagarSeExistir(Path.Combine(jogo, "version.dll"));
            string pasta = Path.Combine(jogo, "MelonLoader");
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        }

        static void ApagarSeExistir(string caminho)
        {
            if (File.Exists(caminho)) File.Delete(caminho);
        }
    }
}
