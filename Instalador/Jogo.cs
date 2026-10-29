using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PbsuLoader
{
    /// <summary>Acha a pasta do jogo e diz o que tem nela (versão do jogo, MelonLoader).</summary>
    static class Jogo
    {
        public static bool Valida(string pasta, Catalogo c)
            => !string.IsNullOrEmpty(pasta) && File.Exists(Path.Combine(pasta, c.Executavel)) && File.Exists(Path.Combine(pasta, "GameAssembly.dll"));

        /// <summary>Lugares prováveis, em ordem: argumento, pasta do instalador, a última usada, Steam, programas instalados.</summary>
        public static string Procurar(Catalogo c, string[] args)
        {
            var candidatos = new List<string>();
            candidatos.AddRange(args.Where(a => !a.StartsWith("--")));
            string aqui = AppDomain.CurrentDomain.BaseDirectory;
            candidatos.Add(aqui);
            candidatos.Add(Path.GetDirectoryName(aqui.TrimEnd('\\')));
            candidatos.Add(Config.Ler("jogo"));
            candidatos.AddRange(PastasSteam(c));
            candidatos.AddRange(ProgramasInstalados());
            foreach (var p in candidatos)
            {
                try { if (Valida(p, c)) return Path.GetFullPath(p); }
                catch { }
            }
            return null;
        }

        static IEnumerable<string> PastasSteam(Catalogo c)
        {
            var bibliotecas = new List<string>();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k?.GetValue("SteamPath") is string steam)
                    {
                        bibliotecas.Add(steam);
                        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                        if (File.Exists(vdf))
                            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                                bibliotecas.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                }
            }
            catch { }
            foreach (var b in bibliotecas)
            {
                string comum = Path.Combine(b, "steamapps", "common");
                if (!Directory.Exists(comum)) continue;
                foreach (var d in Directory.GetDirectories(comum))
                    if (File.Exists(Path.Combine(d, c.Executavel))) yield return d;
            }
        }

        static IEnumerable<string> ProgramasInstalados()
        {
            var achados = new List<string>();
            foreach (var raiz in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (var caminho in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    try
                    {
                        using (var k = raiz.OpenSubKey(caminho))
                        {
                            if (k == null) continue;
                            foreach (var nome in k.GetSubKeyNames())
                                using (var p = k.OpenSubKey(nome))
                                    if ((p?.GetValue("DisplayName") as string ?? "").IndexOf("Proton Bus", StringComparison.OrdinalIgnoreCase) >= 0
                                        && p.GetValue("InstallLocation") is string local && local.Length > 0)
                                        achados.Add(local);
                        }
                    }
                    catch { }
                }
            return achados;
        }

        /// <summary>Nome da versão do jogo (pelo hash do GameAssembly.dll) ou null se o catálogo não a conhece.</summary>
        public static string Versao(string pasta, Catalogo c, out string sha)
        {
            sha = Rede.Sha256(Path.Combine(pasta, "GameAssembly.dll"));
            string s = sha;
            return c.VersoesDoJogo.FirstOrDefault(v => string.Equals(v.Sha256, s, StringComparison.OrdinalIgnoreCase))?.Nome;
        }

        /// <summary>O jogo desta pasta está aberto? Um jogo aberto em outra pasta não trava as DLLs desta.</summary>
        public static bool Aberto(Catalogo c, string pasta)
        {
            string exe = Path.GetFullPath(Path.Combine(pasta, c.Executavel));
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(c.Executavel)))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(p.MainModule.FileName), exe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { return true; } // sem acesso ao processo: na dúvida, aberto
            }
            return false;
        }

        /// <summary>Versão do MelonLoader instalado ("0.7.3"), ou null se não houver.</summary>
        public static string VersaoMelon(string pasta)
        {
            string dll = Path.Combine(pasta, "MelonLoader", "net6", "MelonLoader.dll");
            if (!File.Exists(dll)) dll = Path.Combine(pasta, "MelonLoader", "MelonLoader.dll");
            if (!File.Exists(dll) || !File.Exists(Path.Combine(pasta, "version.dll"))) return null;
            var v = FileVersionInfo.GetVersionInfo(dll);
            string pv = v.ProductVersion ?? v.FileVersion ?? "?";
            int mais = pv.IndexOf('+');
            return mais > 0 ? pv.Substring(0, mais) : pv;
        }
    }

    /// <summary>Preferências do instalador em %APPDATA%\pbsu-loader\config.txt (chave=valor).</summary>
    static class Config
    {
        static string Arquivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pbsu-loader", "config.txt");

        static Dictionary<string, string> Tudo()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(Arquivo))
                    foreach (var l in File.ReadAllLines(Arquivo))
                    {
                        int i = l.IndexOf('=');
                        if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        public static string Ler(string chave) => Tudo().TryGetValue(chave, out var v) ? v : null;

        public static void Gravar(string chave, string valor)
        {
            try
            {
                var d = Tudo();
                d[chave] = valor;
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                File.WriteAllLines(Arquivo, d.Select(p => p.Key + "=" + p.Value));
            }
            catch { }
        }
    }
}
