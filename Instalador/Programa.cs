using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace PbsuLoader
{
    static class Programa
    {
        public static readonly string Versao = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        [STAThread]
        static void Main(string[] args)
        {
            AutoAtualizacao.LimparVersaoAntiga(args);
            if (Array.IndexOf(args, "--silencioso") >= 0)
            {
                Environment.Exit(Silencioso.Rodar(args));
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Janela(args));
        }
    }

    /// <summary>
    /// Troca o próprio .exe: o Windows deixa renomear um executável aberto, então o atual vira .old, o novo toma o
    /// nome dele e é aberto; o novo apaga o .old ao iniciar.
    /// </summary>
    static class AutoAtualizacao
    {
        static string Atual => Process.GetCurrentProcess().MainModule.FileName;

        public static void LimparVersaoAntiga(string[] args)
        {
            if (Array.IndexOf(args, "--atualizado") < 0) return;
            string velho = Atual + ".old";
            for (int i = 0; i < 20 && File.Exists(velho); i++)
            {
                try { File.Delete(velho); }
                catch { Thread.Sleep(250); } // o processo antigo ainda está fechando
            }
        }

        public static string Baixar(Lancamento novo) => Rede.Baixar(novo.Url, novo.Sha256);

        public static void Trocar(string tmp, string pastaDoJogo)
        {
            string atual = Atual, velho = atual + ".old";
            if (File.Exists(velho)) File.Delete(velho);
            File.Move(atual, velho);
            try { File.Move(tmp, atual); }
            catch
            {
                File.Move(velho, atual);
                throw;
            }
            // Sem a barra final: "C:\Jogo\" escaparia a aspa de fechamento na linha de comando.
            Process.Start(new ProcessStartInfo(atual, "--atualizado \"" + pastaDoJogo.TrimEnd('\\') + "\"") { UseShellExecute = false });
            Application.Exit();
        }
    }
}
