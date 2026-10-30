using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PbsuLoader
{
    /// <summary>
    /// Modo desenvolvedor: o build local de cada mod (bin\Release ou bin\Debug dos projetos nas pastas de dev) vale
    /// no lugar do release. Mods fora do catálogo também aparecem, com o nome da DLL. Não precisa de tag nem release.
    /// </summary>
    static class Local
    {
        public static bool Ativo
        {
            get => Config.Ler("dev") == "1";
            set => Config.Gravar("dev", value ? "1" : "0");
        }

        public static List<string> Pastas
        {
            get => (Config.Ler("dev_pastas") ?? "").Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            set => Config.Gravar("dev_pastas", string.Join(";", value));
        }

        static readonly HashSet<string> Ignorar = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", "dist" };

        /// <summary>Nome do assembly (sem .dll) -> DLL mais nova entre os builds dos projetos de biblioteca nas pastas.</summary>
        public static Dictionary<string, string> Builds(IEnumerable<string> pastas)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pasta in pastas)
            {
                if (!Directory.Exists(pasta)) continue;
                foreach (var csproj in Projetos(pasta, 0))
                {
                    string texto;
                    try { texto = File.ReadAllText(csproj); } catch { continue; }
                    if (Regex.IsMatch(texto, @"<OutputType>\s*(Win)?Exe\s*</OutputType>", RegexOptions.IgnoreCase)) continue; // ferramentas, o próprio instalador
                    var m = Regex.Match(texto, @"<AssemblyName>\s*([^<\s]+)\s*</AssemblyName>");
                    string nome = m.Success ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(csproj);
                    string bin = Path.Combine(Path.GetDirectoryName(csproj), "bin");
                    if (!Directory.Exists(bin)) continue;
                    string dll = Directory.GetFiles(bin, nome + ".dll", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if (dll == null) continue;
                    if (!r.TryGetValue(nome, out var outra) || File.GetLastWriteTimeUtc(dll) > File.GetLastWriteTimeUtc(outra)) r[nome] = dll;
                }
            }
            return r;
        }

        static IEnumerable<string> Projetos(string pasta, int nivel)
        {
            string[] arquivos, subpastas;
            try { arquivos = Directory.GetFiles(pasta, "*.csproj"); subpastas = Directory.GetDirectories(pasta); }
            catch { yield break; }
            foreach (var a in arquivos) yield return a;
            if (nivel >= 3) yield break;
            foreach (var d in subpastas)
                if (!Ignorar.Contains(Path.GetFileName(d)))
                    foreach (var p in Projetos(d, nivel + 1)) yield return p;
        }

        /// <summary>Mod sintético para um build local que não está no catálogo.</summary>
        public static ModDoCatalogo ForaDoCatalogo(string nome, string dll) => new ModDoCatalogo
        {
            Id = nome,
            Nome = nome,
            Estado = "local",
            Descricao = "Build local, fora do catálogo.",
            Autor = "",
            Origem = new Origem { Arquivo = nome + ".dll" },
        };

        /// <summary>Lista de pastas editável. Devolve null se cancelar.</summary>
        public static List<string> EditarPastas(IWin32Window dono, List<string> atuais)
        {
            using (var f = new Form
            {
                Text = "Pastas de desenvolvimento",
                Font = new Font("Segoe UI", 9f),
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(560, 300),
                MinimumSize = new Size(420, 240),
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MinimizeBox = false,
                MaximizeBox = false,
            })
            {
                var lista = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                lista.Items.AddRange(atuais.Cast<object>().ToArray());
                var explicacao = new Label
                {
                    Dock = DockStyle.Top, AutoSize = false, Height = 40,
                    Text = "Pastas com os seus projetos (.csproj). O instalador procura o build de cada um em bin\\Release ou bin\\Debug, até 3 níveis abaixo.",
                };
                var adicionar = new Button { Text = "Adicionar...", AutoSize = true };
                var remover = new Button { Text = "Remover", AutoSize = true };
                var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
                var cancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
                var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
                botoes.Controls.AddRange(new Control[] { cancelar, ok, remover, adicionar });
                var corpo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
                corpo.Controls.Add(lista);
                corpo.Controls.Add(explicacao);
                corpo.Controls.Add(botoes);
                f.Controls.Add(corpo);
                f.AcceptButton = ok;
                f.CancelButton = cancelar;

                adicionar.Click += (s, e) =>
                {
                    using (var d = new FolderBrowserDialog { Description = "Pasta com os projetos dos mods" })
                        if (d.ShowDialog(f) == DialogResult.OK && !lista.Items.Contains(d.SelectedPath)) lista.Items.Add(d.SelectedPath);
                };
                remover.Click += (s, e) => { if (lista.SelectedIndex >= 0) lista.Items.RemoveAt(lista.SelectedIndex); };

                if (f.ShowDialog(dono) != DialogResult.OK) return null;
                return lista.Items.Cast<string>().ToList();
            }
        }
    }
}
