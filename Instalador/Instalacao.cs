using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PbsuLoader
{
    enum Situacao { NaoInstalado, Atualizado, Atualizacao, Diferente, SemLancamento, Erro, LocalAtualizado, LocalNovo }

    /// <summary>Um mod do catálogo cruzado com o último release e com o que está na pasta do jogo.</summary>
    class EstadoMod
    {
        public ModDoCatalogo Mod;
        public Lancamento Remoto;
        public Registro.Item Instalado;
        public Situacao Situacao;
        public string Detalhe = "";
        public string Local, LocalSha; // modo desenvolvedor: build local que vale no lugar do release
        public bool ArquivoNoJogo;     // a DLL do mod está em Mods (com ou sem registro)

        public bool Presente => Instalado != null || Situacao == Situacao.Diferente || ArquivoNoJogo;
        public bool PrecisaBaixar => Local == null && Remoto != null && (Situacao == Situacao.NaoInstalado || Situacao == Situacao.Atualizacao || Situacao == Situacao.Diferente);
        public bool PrecisaCopiar => Local != null && (Situacao == Situacao.NaoInstalado || Situacao == Situacao.LocalNovo);
        public bool PrecisaInstalar => PrecisaBaixar || PrecisaCopiar;
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
        /// <summary>Caminhos do arquivo inválidos ou fora da pasta do jogo: ficaram de fora e somem no próximo Salvar.</summary>
        public List<string> Ignorados = new List<string>();
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
                    // O arquivo fica dentro da pasta do jogo, onde qualquer zip consegue escrever: o que ele lista vai
                    // ser apagado ao remover, então só vale caminho que não sai dela.
                    if (Json.Obj(p.Value, "arquivos") is Dictionary<string, object> arqs)
                        foreach (var a in arqs)
                        {
                            if (Instalacao.TentarAbsoluto(jogo, a.Key, out _, out var chave)) it.Arquivos[chave] = a.Value as string;
                            else r.Ignorados.Add(a.Key);
                        }
                    r.Mods[p.Key] = it;
                }
            return r;
        }

        public void AvisarIgnorados(Action<string> log)
        {
            foreach (var a in Ignorados) log("instalado.json lista um caminho inválido ou fora da pasta do jogo, ignorado: " + a);
            Ignorados.Clear();
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

        static string Absoluto(string jogo, string relativo)
        {
            if (!TentarAbsoluto(jogo, relativo, out var caminho)) throw new InvalidDataException("caminho inválido ou fora da pasta do jogo: " + relativo);
            return caminho;
        }

        internal static bool TentarAbsoluto(string jogo, string relativo, out string caminho) => TentarAbsoluto(jogo, relativo, out caminho, out _);

        /// <summary>
        /// Caminho absoluto de <paramref name="relativo"/> dentro da pasta do jogo e a forma normalizada dele
        /// ("Mods/X.dll"), que é a chave do registro; false se ele sair da pasta ou não for um caminho válido.
        /// </summary>
        internal static bool TentarAbsoluto(string jogo, string relativo, out string caminho, out string chave)
        {
            caminho = chave = null;
            if (string.IsNullOrWhiteSpace(relativo)) return false;
            try
            {
                // IsPathRooted também pega "C:x" e "\x": o Combine não os prende à pasta do jogo ("C:x" vai para a pasta atual do drive).
                if (Path.IsPathRooted(relativo)) return false;
                string raiz = Path.GetFullPath(jogo).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string c = Path.GetFullPath(Path.Combine(raiz, relativo.Replace('/', Path.DirectorySeparatorChar)));
                if (!c.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) return false;
                caminho = c;
                chave = c.Substring(raiz.Length).Replace(Path.DirectorySeparatorChar, '/');
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>
        /// Aviso para mods da comunidade sem autor confiável que vão entrar pela primeira vez, ou null. Recebe a lista
        /// já com as dependências marcadas sozinhas, então o aviso também vale para elas.
        /// </summary>
        public static string UnreviewedWarning(IEnumerable<EstadoMod> toInstall)
        {
            var names = toInstall.Where(e => e.Mod.Community && !e.Mod.Trusted && !e.Presente).Select(e => e.Mod.Nome).ToList();
            if (names.Count == 0) return null;
            return "Atenção: " + string.Join(", ", names) + (names.Count == 1 ? " é um mod" : " são mods") +
                " da comunidade que ninguém revisou. Um plugin roda com as mesmas permissões que você no computador. " +
                "Só continue se confia no autor.";
        }

        /// <param name="local">Build local do mod (modo desenvolvedor), ou null para usar o release.</param>
        public static EstadoMod Avaliar(ModDoCatalogo mod, string jogo, Registro reg, bool aceitarPre, string local = null)
        {
            var e = new EstadoMod { Mod = mod };
            reg.Mods.TryGetValue(mod.Id, out e.Instalado);
            if (!string.IsNullOrEmpty(mod.Origem.Repo))
            {
                try { e.Remoto = Rede.UltimoLancamento(mod.Origem, aceitarPre); }
                catch (Exception ex) { e.Detalhe = "não consegui consultar o GitHub: " + ex.Message; }
            }

            if (local != null && EhDll(mod.Origem))
            {
                // O build local manda: compara com o que está em Mods (o dotnet build já costuma copiar para lá).
                e.Local = local;
                e.LocalSha = Rede.Sha256(local);
                e.Detalhe = local + ", compilado em " + File.GetLastWriteTime(local).ToString("dd/MM HH:mm");
                string noJogo = Absoluto(jogo, "Mods/" + mod.Origem.Arquivo);
                e.ArquivoNoJogo = File.Exists(noJogo);
                if (!e.ArquivoNoJogo) e.Situacao = Situacao.NaoInstalado;
                else e.Situacao = string.Equals(Rede.Sha256(noJogo), e.LocalSha, StringComparison.OrdinalIgnoreCase) ? Situacao.LocalAtualizado : Situacao.LocalNovo;
                return e;
            }

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
            if (e.Local != null)
            {
                string rel = "Mods/" + o.Arquivo;
                Directory.CreateDirectory(Path.Combine(jogo, "Mods"));
                File.Copy(e.Local, Absoluto(jogo, rel), true);
                var local = new Registro.Item { Versao = "local", Sha256 = e.LocalSha };
                local.Arquivos[rel] = e.LocalSha;
                reg.Mods[e.Mod.Id] = local;
                reg.Salvar();
                log("  " + e.Mod.Nome + " copiado do build local (" + e.Local + ").");
                return;
            }
            if (e.Mod.Community && e.Remoto.Sha256 == null)
                throw new InvalidOperationException(e.Mod.Nome + " " + e.Remoto.Versao + " está sem sha256 no GitHub; o autor precisa publicar o release de novo.");
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
                    using (var z = ZipFile.OpenRead(tmp))
                        foreach (var entrada in z.Entries)
                        {
                            if (entrada.FullName.EndsWith("/")) continue;
                            if (!TentarAbsoluto(jogo, entrada.FullName, out var destino, out var chave))
                                throw new InvalidDataException("o zip tem um caminho inválido ou fora da pasta do jogo: " + entrada.FullName);
                            Directory.CreateDirectory(Path.GetDirectoryName(destino));
                            entrada.ExtractToFile(destino, true);
                            novo.Arquivos[chave] = Rede.Sha256(destino);
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

        /// <summary>
        /// Pasta da confiança nos scripts, a mesma que a PBSLoaderCommonUtils lê (Conteudo/Confianca.cs). Fica no perfil
        /// do usuário e não na pasta do jogo, onde um ônibus ou mapa extraído poderia trazer o arquivo pronto. Null se o
        /// Windows não informar a pasta.
        /// </summary>
        internal static string PastaDeConfianca = PastaDeConfiancaPadrao();

        static string PastaDeConfiancaPadrao()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local) ? null : Path.Combine(local, "PBSLoaderCommonUtils");
        }

        /// <summary>
        /// Grava os hashes dos scripts de ônibus/mapa revisados (scriptsConfiaveis do catálogo) em
        /// <see cref="PastaDeConfianca"/>\confiaveis.txt: a PBSLoaderCommonUtils roda esses sem perguntar. O arquivo é só do
        /// instalador; as decisões do jogador ficam em confianca.json e não são tocadas.
        /// </summary>
        public static void GravarConfiaveis(Catalogo c, string jogo, Action<string> log)
        {
            try
            {
                string copiaAntigaNaPastaDoJogo = Path.Combine(jogo, "UserData", "PBSLoaderCommonUtils", "confiaveis.txt");
                ApagarSeExistir(copiaAntigaNaPastaDoJogo);
                if (PastaDeConfianca == null) { log("Sem pasta de dados do usuário: os scripts revisados vão perguntar antes de rodar."); return; }
                var arq = Path.Combine(PastaDeConfianca, "confiaveis.txt");
                var sb = new StringBuilder();
                sb.Append("# Scripts de ônibus e mapa revisados no catálogo do PBSLoader. Este arquivo é reescrito pelo instalador.\n");
                foreach (var s in c.ScriptsConfiaveis)
                {
                    sb.Append("# ").Append(s.Nome);
                    if (s.Autor.Length > 0) sb.Append(" — ").Append(s.Autor);
                    if (s.Onde.Length > 0) sb.Append(" (").Append(s.Onde).Append(')');
                    sb.Append('\n').Append(s.Sha256).Append('\n');
                }
                string novo = sb.ToString();
                if (File.Exists(arq) && File.ReadAllText(arq, Encoding.UTF8) == novo) return;
                Directory.CreateDirectory(Path.GetDirectoryName(arq));
                File.WriteAllText(arq, novo, new UTF8Encoding(false));
                log(c.ScriptsConfiaveis.Count + " script(s) de ônibus/mapa revisados no catálogo.");
            }
            catch (Exception e) { log("Não consegui gravar a lista de scripts revisados: " + e.Message); }
        }

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
                using (var z = ZipFile.OpenRead(tmp))
                    foreach (var entrada in z.Entries)
                    {
                        if (!TentarAbsoluto(jogo, entrada.FullName, out var destino)) continue;
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
