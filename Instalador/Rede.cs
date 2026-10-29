using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PbsuLoader
{
    /// <summary>Um asset de um Release do GitHub.</summary>
    class Lancamento
    {
        public string Tag, Versao, Url, Sha256, Pagina;
        public bool PreLancamento;
        public long Tamanho;
    }

    static class Rede
    {
        static Rede()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        static WebClient Cliente()
        {
            var w = new WebClient { Encoding = Encoding.UTF8 };
            w.Headers[HttpRequestHeader.UserAgent] = "pbsu-loader/" + Programa.Versao;
            return w;
        }

        public static string BaixarTexto(string url)
        {
            using (var w = Cliente())
            {
                if (url.StartsWith("https://api.github.com/")) w.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                return w.DownloadString(url);
            }
        }

        /// <summary>Baixa para um arquivo temporário e confere o SHA-256 quando ele é conhecido.</summary>
        public static string Baixar(string url, string sha256Esperado)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "pbsu-loader-" + Guid.NewGuid().ToString("N"));
            using (var w = Cliente()) w.DownloadFile(url, tmp);
            if (!string.IsNullOrEmpty(sha256Esperado))
            {
                string obtido = Sha256(tmp);
                if (!string.Equals(obtido, sha256Esperado, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tmp);
                    throw new InvalidDataException("o arquivo baixado de " + url + " não confere (SHA-256 " + obtido + ", esperado " + sha256Esperado + ")");
                }
            }
            return tmp;
        }

        public static string Sha256(string caminho)
        {
            using (var s = File.OpenRead(caminho))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        // Uma consulta por repositório: o limite da API sem login é de 60 por hora.
        static readonly Dictionary<string, List<object>> _releases = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        public static void EsquecerReleases() { lock (_releases) _releases.Clear(); }

        static List<object> Releases(string repo)
        {
            lock (_releases)
            {
                if (_releases.TryGetValue(repo, out var l)) return l;
                l = Json.Ler(BaixarTexto("https://api.github.com/repos/" + repo + "/releases?per_page=50")) as List<object> ?? new List<object>();
                _releases[repo] = l;
                return l;
            }
        }

        /// <summary>O release mais novo do repositório com a tag no prefixo e o asset pedido, ou null.</summary>
        public static Lancamento UltimoLancamento(Origem o, bool aceitarPre)
        {
            Lancamento melhor = null;
            Version vMelhor = null;
            foreach (var r in Releases(o.Repo))
            {
                if (Json.Bool(r, "draft")) continue;
                bool pre = Json.Bool(r, "prerelease");
                if (pre && !aceitarPre) continue;
                string tag = Json.Texto(r, "tag_name", "");
                if (!tag.StartsWith(o.Tag, StringComparison.OrdinalIgnoreCase)) continue;
                var asset = Json.Lista(r, "assets").FirstOrDefault(a => string.Equals(Json.Texto(a, "name"), o.Arquivo, StringComparison.OrdinalIgnoreCase));
                if (asset == null) continue;

                string versao = tag.Substring(o.Tag.Length);
                var v = Versoes.Ler(versao);
                if (melhor != null && (v == null || vMelhor != null && v <= vMelhor)) continue;

                string digest = Json.Texto(asset, "digest", "");
                melhor = new Lancamento
                {
                    Tag = tag,
                    Versao = versao,
                    Url = Json.Texto(asset, "browser_download_url"),
                    Sha256 = digest.StartsWith("sha256:") ? digest.Substring(7) : null,
                    Pagina = Json.Texto(r, "html_url"),
                    PreLancamento = pre,
                    Tamanho = asset is Dictionary<string, object> d && d.TryGetValue("size", out var t) && t is double n ? (long)n : 0,
                };
                vMelhor = v;
            }
            return melhor;
        }
    }

    static class Versoes
    {
        /// <summary>"1.2", "1.2.0-beta" ou "v1.2.0" viram Version; texto sem número vira null.</summary>
        public static Version Ler(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.TrimStart('v', 'V');
            int fim = 0;
            while (fim < s.Length && (char.IsDigit(s[fim]) || s[fim] == '.')) fim++;
            s = s.Substring(0, fim).Trim('.');
            if (s.Length == 0) return null;
            if (!s.Contains('.')) s += ".0";
            return Version.TryParse(s, out var v) ? v : null;
        }
    }
}
