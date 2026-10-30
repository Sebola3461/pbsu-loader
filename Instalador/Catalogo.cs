using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PbsuLoader
{
    /// <summary>De onde vem um arquivo: o asset de um Release do GitHub, num repositório e com um prefixo de tag.</summary>
    class Origem
    {
        public string Repo;      // "dono/repositorio"
        public string Tag = "v"; // prefixo da tag; o resto é a versão ("v1.2.0", "GraficosPlus-v1.0.0")
        public string Arquivo;   // nome do asset: uma .dll (vai para Mods) ou um .zip (extraído na pasta do jogo)

        public static Origem De(object o) => new Origem
        {
            Repo = Json.Texto(o, "repo"),
            Tag = Json.Texto(o, "tag", "v"),
            Arquivo = Json.Texto(o, "arquivo"),
        };
    }

    class ModDoCatalogo
    {
        public string Id, Nome, Descricao, Autor, Estado, Pagina;
        public Origem Origem;
        public bool Obrigatorio, Padrao;
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

        public Origem Instalador;
        public string MelonVersao, MelonUrl, MelonSha256;
        public string Executavel = "Proton Bus Simulator.exe";
        public List<VersaoDoJogo> VersoesDoJogo = new List<VersaoDoJogo>();
        public List<ModDoCatalogo> Mods = new List<ModDoCatalogo>();
        /// <summary>Scripts de ônibus e mapa revisados: o jogo os roda sem perguntar ao jogador (ver Instalacao.GravarConfiaveis).</summary>
        public List<ScriptConfiavel> ScriptsConfiaveis = new List<ScriptConfiavel>();
        public string Fonte;

        public ModDoCatalogo Mod(string id) => Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>PBSU_CATALOGO (caminho ou URL) troca a fonte, para testar um catálogo antes do pull request.</summary>
        public static Catalogo Carregar(Action<string> log)
        {
            string fonte = Environment.GetEnvironmentVariable("PBSU_CATALOGO");
            if (string.IsNullOrEmpty(fonte)) fonte = Url;
            try
            {
                string texto = fonte.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? Rede.BaixarTexto(fonte)
                    : File.ReadAllText(fonte, Encoding.UTF8);
                var c = Ler(texto);
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
                    c.Fonte = "cópia embutida";
                    return c;
                }
            }
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
                if (string.IsNullOrEmpty(mod.Id) || string.IsNullOrEmpty(mod.Origem.Repo) || string.IsNullOrEmpty(mod.Origem.Arquivo))
                    continue;
                if (!mod.Origem.Arquivo.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !mod.Origem.Arquivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue; // só sabemos instalar uma DLL em Mods ou um zip na pasta do jogo
                if (mod.Pagina == null) mod.Pagina = "https://github.com/" + mod.Origem.Repo;
                mod.Dependencias.AddRange(Json.Lista(m, "dependencias").OfType<string>());
                mod.VersoesDoJogo.AddRange(Json.Lista(m, "versoesDoJogo").OfType<string>());
                c.Mods.Add(mod);
            }

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
    }
}
