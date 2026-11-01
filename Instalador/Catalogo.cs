using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PbsuLoader
{
    /// <summary>De onde vem um arquivo: o asset de um Release do GitHub (repo/tag/arquivo) ou uma URL externa direta.</summary>
    class Origem
    {
        public string Repo;      // "dono/repositorio"; null quando o arquivo vem de URL externa
        public string Tag = "v"; // prefixo da tag; o resto é a versão ("v1.2.0", "GraficosPlus-v1.0.0")
        public string Arquivo;   // nome do asset: uma .dll (vai para Mods) ou um .zip (extraído na pasta do jogo)
        public string Url;       // origem externa: download direto, sem consulta de release (só https)

        public bool Externa => string.IsNullOrEmpty(Repo);

        public static Origem De(object o) => new Origem
        {
            Repo = Json.Texto(o, "repo"),
            Tag = Json.Texto(o, "tag", "v"),
            Arquivo = Json.Texto(o, "arquivo"),
        };

        /// <summary>
        /// Origem externa do agregado da comunidade (o arquivo do PR não tem repo/tag/arquivo): o nome local do
        /// arquivo vem do fim da URL. Só https — http não entra nem para teste.
        /// </summary>
        public static Origem ExternaDe(string url)
        {
            string nome = null;
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                var ultimo = u.Segments.LastOrDefault();
                if (ultimo != null)
                {
                    try { nome = Uri.UnescapeDataString(ultimo); } catch (UriFormatException) { nome = null; }
                }
            }
            return new Origem { Url = url, Arquivo = nome };
        }
    }

    class ModDoCatalogo
    {
        public string Id, Nome, Descricao, Autor, Estado, Pagina;
        public Origem Origem;
        public bool Obrigatorio, Padrao;
        /// <summary>Veio do catálogo da comunidade (pbs-loader-catalogo): entrou sem revisão humana.</summary>
        public bool Community;
        /// <summary>Autor marcado como confiável pelos moderadores do catálogo da comunidade.</summary>
        public bool Trusted;
        public List<string> Dependencias = new List<string>();
        public List<string> VersoesDoJogo = new List<string>(); // vazio = não declarou
    }

    class VersaoDoJogo
    {
        public string Nome, Sha256;
    }

    /// <summary>
    /// catalogo.json, na raiz do pbsu-loader. Qualquer mod entra por pull request nele; as DLLs ficam nos Releases do
    /// repositório de cada mod. Lê a cópia do GitHub e, sem rede, a que veio embutida no instalador.
    /// </summary>
    /// <summary>Um pacote de scripts de ônibus/mapa (pasta scripts/ com pbsu.json) revisado e listado no catálogo.</summary>
    class ScriptConfiavel
    {
        public string Nome, Autor, Onde, Sha256;
    }

    class Catalogo
    {
        public const string Url = "https://raw.githubusercontent.com/Sebola3461/pbsu-loader/main/catalogo.json";
        public const string CommunityUrl = "https://sebola3461.github.io/pbs-loader-catalogo/community-catalog.json";

        public Origem Instalador;
        public string MelonVersao, MelonUrl, MelonSha256;
        public string Executavel = "Proton Bus Simulator.exe";
        public List<VersaoDoJogo> VersoesDoJogo = new List<VersaoDoJogo>();
        public List<ModDoCatalogo> Mods = new List<ModDoCatalogo>();
        /// <summary>Scripts de ônibus e mapa revisados: o jogo os roda sem perguntar ao jogador (ver Instalacao.GravarConfiaveis).</summary>
        public List<ScriptConfiavel> ScriptsConfiaveis = new List<ScriptConfiavel>();
        public string Fonte;

        public ModDoCatalogo Mod(string id) => Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// PBSU_CATALOGO (caminho ou URL) troca a fonte, para testar um catálogo antes do pull request;
        /// PBSU_CATALOGO_COMUNIDADE faz o mesmo com o catálogo da comunidade.
        /// </summary>
        public static Catalogo Carregar(Action<string> log)
        {
            var c = LoadBase(log);
            string community = SourceFromEnvironment("PBSU_CATALOGO_COMUNIDADE", CommunityUrl);
            if (c.Fonte == EmbeddedSource && IsUrl(community))
            {
                log("Sem rede: seguindo sem o catálogo da comunidade.");
                return c;
            }
            try
            {
                int added = c.MergeCommunity(ReadSource(community));
                log("Catálogo da comunidade: " + added + " mod(s).");
            }
            catch (Exception e)
            {
                // Sem cópia embutida: um catálogo velho mostraria mods que os moderadores já tiraram.
                log("Não consegui ler o catálogo da comunidade de " + community + " (" + e.Message + "). Seguindo só com o catálogo base.");
            }
            return c;
        }

        static Catalogo LoadBase(Action<string> log)
        {
            string fonte = SourceFromEnvironment("PBSU_CATALOGO", Url);
            try
            {
                var c = Ler(ReadSource(fonte));
                c.Fonte = fonte;
                return c;
            }
            catch (Exception e)
            {
                log("Não consegui ler o catálogo de " + fonte + " (" + e.Message + "). Usando o que veio com o instalador.");
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PbsuLoader.catalogo.json"))
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    var c = Ler(r.ReadToEnd());
                    c.Fonte = EmbeddedSource;
                    return c;
                }
            }
        }

        static string SourceFromEnvironment(string variable, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        const string EmbeddedSource = "cópia embutida";

        static bool IsUrl(string source) => source.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        static string ReadSource(string source) => IsUrl(source) ? Rede.BaixarTexto(source) : File.ReadAllText(source, Encoding.UTF8);

        /// <summary>
        /// Plugin entra como .dll; os outros tipos (ônibus, mapa, skin...) como .zip extraído na pasta do jogo.
        /// Origem externa aceita os dois formatos, mas só https. Nunca como obrigatório ou padrão. Id ou nome de
        /// arquivo já usado fica com quem chegou antes, para um mod da comunidade não sobrescrever (nem apagar ao
        /// desinstalar) o arquivo de outro mod. Devolve quantos entraram.
        /// </summary>
        public int MergeCommunity(string text)
        {
            var added = new List<ModDoCatalogo>();
            foreach (var m in Json.Lista(Json.Ler(text), "mods"))
            {
                var mod = ReadMod(m);
                if (mod == null) continue;
                bool ehDll = mod.Origem.Arquivo.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                bool ehZip = mod.Origem.Arquivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                bool plugin = string.Equals(Json.Texto(m, "tipo"), "plugin", StringComparison.Ordinal);
                if (plugin && !ehDll) continue;
                if (!plugin && !ehZip) continue;
                if (Mod(mod.Id) != null || UsesFile(mod.Origem.Arquivo)) continue;
                mod.Obrigatorio = mod.Padrao = false;
                mod.Community = true;
                mod.Trusted = Json.Bool(m, "confiavel");
                mod.Estado = mod.Trusted ? "comunidade, confiável" : "comunidade";
                Mods.Add(mod);
                added.Add(mod);
            }
            DropMissingDependencies(added);
            return added.Count(m => Mods.Contains(m));
        }

        bool UsesFile(string file) => Mods.Any(m => string.Equals(m.Origem.Arquivo, file, StringComparison.OrdinalIgnoreCase));

        void DropMissingDependencies(List<ModDoCatalogo> community)
        {
            bool removed;
            do
            {
                removed = false;
                foreach (var mod in community.Where(m => Mods.Contains(m)).ToList())
                {
                    if (mod.Dependencias.All(d => Mod(d) != null)) continue;
                    Mods.Remove(mod);
                    removed = true;
                }
            } while (removed);
        }

        public static Catalogo Ler(string texto)
        {
            var raiz = Json.Ler(texto);
            var c = new Catalogo();
            if (Json.Obj(raiz, "instalador") is Dictionary<string, object> inst) c.Instalador = Origem.De(inst);

            var ml = Json.Obj(raiz, "melonloader");
            c.MelonVersao = Json.Texto(ml, "versao");
            c.MelonUrl = Json.Texto(ml, "url");
            c.MelonSha256 = Json.Texto(ml, "sha256");

            var jogo = Json.Obj(raiz, "jogo");
            c.Executavel = Json.Texto(jogo, "executavel", c.Executavel);
            foreach (var v in Json.Lista(jogo, "versoes"))
                c.VersoesDoJogo.Add(new VersaoDoJogo { Nome = Json.Texto(v, "nome"), Sha256 = Json.Texto(v, "gameassembly_sha256") });

            foreach (var m in Json.Lista(raiz, "mods"))
                if (ReadMod(m) is ModDoCatalogo mod) c.Mods.Add(mod);

            foreach (var s in Json.Lista(raiz, "scriptsConfiaveis"))
            {
                var sha = (Json.Texto(s, "sha256") ?? "").Trim().ToLowerInvariant();
                if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) continue;
                c.ScriptsConfiaveis.Add(new ScriptConfiavel
                {
                    Nome = Json.Texto(s, "nome", "?"),
                    Autor = Json.Texto(s, "autor", ""),
                    Onde = Json.Texto(s, "onde", ""),
                    Sha256 = sha,
                });
            }
            return c;
        }

        static ModDoCatalogo ReadMod(object m)
        {
            var mod = new ModDoCatalogo
            {
                Id = Json.Texto(m, "id"),
                Nome = Json.Texto(m, "nome") ?? Json.Texto(m, "id"),
                Descricao = Json.Texto(m, "descricao", ""),
                Autor = Json.Texto(m, "autor", ""),
                Estado = Json.Texto(m, "estado", ""),
                Pagina = Json.Texto(m, "pagina"),
                Origem = Origem.De(m),
                Obrigatorio = Json.Bool(m, "obrigatorio"),
                Padrao = Json.Bool(m, "padrao"),
            };
            // Agregado da comunidade: um mod de origem externa chega com a URL na raiz, sem repo/tag/arquivo.
            // O nome local do arquivo deriva do fim da URL; a consulta de release não se aplica a ele.
            if (string.IsNullOrEmpty(mod.Origem.Repo))
            {
                string url = Json.Texto(m, "url");
                if (url == null) return null;
                mod.Origem = Origem.ExternaDe(url);
            }
            if (string.IsNullOrEmpty(mod.Id) || string.IsNullOrEmpty(mod.Origem.Arquivo))
                return null;
            if (!mod.Origem.Arquivo.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !mod.Origem.Arquivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return null; // só sabemos instalar uma DLL em Mods ou um zip na pasta do jogo
            if (!ArquivoSemPasta(mod.Origem.Arquivo)) return null;
            if (mod.Pagina == null && mod.Origem.Repo != null) mod.Pagina = "https://github.com/" + mod.Origem.Repo;
            mod.Dependencias.AddRange(Json.Lista(m, "dependencias").OfType<string>());
            mod.VersoesDoJogo.AddRange(Json.Lista(m, "versoesDoJogo").OfType<string>());
            return mod;
        }

        static bool ArquivoSemPasta(string arquivo)
            => arquivo.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !arquivo.StartsWith(".");
    }
}
