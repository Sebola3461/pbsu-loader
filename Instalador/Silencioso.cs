using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PbsuLoader
{
    /// <summary>
    /// PbsuLoader.exe --silencioso [pasta do jogo] [--beta] [--dev]: sem janela, deixa o MelonLoader na versão do catálogo,
    /// instala os obrigatórios e atualiza o que já está instalado. Não instala nem remove mods opcionais.
    /// Log em UserData/pbsu-loader/instalador.log. Código de saída 0 = ok, 1 = erro, 2 = jogo não achado ou aberto.
    /// </summary>
    static class Silencioso
    {
        public static int Rodar(string[] args)
        {
            var log = new StringBuilder();
            Action<string> L = s => { log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + s); Console.WriteLine(s); };
            string jogo = null;
            int codigo = 0;
            try
            {
                var cat = Catalogo.Carregar(L);
                L("Catálogo: " + cat.Fonte);
                jogo = Jogo.Procurar(cat, args);
                if (jogo == null) { L("Jogo não encontrado."); return 2; }
                if (Jogo.Aberto(cat, jogo)) { L("O jogo está aberto; nada foi feito."); return 2; }
                L("Jogo: " + jogo + " (versão " + (Jogo.Versao(jogo, cat, out var sha) ?? "desconhecida, GameAssembly " + sha) + ")");

                if (Jogo.VersaoMelon(jogo) != cat.MelonVersao) Instalacao.InstalarMelon(cat, jogo, L);
                else L("MelonLoader " + cat.MelonVersao + " ok.");

                bool beta = args.Contains("--beta");
                // --dev: os builds locais das pastas de desenvolvimento valem no lugar dos releases.
                var builds = args.Contains("--dev") ? Local.Builds(Local.Pastas) : new Dictionary<string, string>();
                var reg = Registro.Abrir(jogo);
                foreach (var m in cat.Mods)
                {
                    // Um mod com erro não impede os outros de atualizar.
                    try
                    {
                        builds.TryGetValue(Path.GetFileNameWithoutExtension(m.Origem.Arquivo), out var local);
                        var e = Instalacao.Avaliar(m, jogo, reg, beta, local);
                        L(m.Id + ": " + e.Situacao + (e.Detalhe.Length > 0 ? " (" + e.Detalhe + ")" : ""));
                        bool quer = m.Obrigatorio || e.Instalado != null;
                        if (e.Local != null) { if ((quer || e.Presente) && e.PrecisaCopiar) Instalacao.Instalar(e, jogo, reg, L); }
                        else if (quer && e.PrecisaBaixar && e.Situacao != Situacao.Diferente) Instalacao.Instalar(e, jogo, reg, L);
                        else if (m.Obrigatorio && e.Situacao == Situacao.Diferente && e.Remoto != null) Instalacao.Instalar(e, jogo, reg, L);
                        else if (e.Situacao == Situacao.Erro) codigo = 1;
                    }
                    catch (Exception ex)
                    {
                        L(m.Id + ": erro: " + ex.Message);
                        codigo = 1;
                    }
                }
                reg.Salvar();
                Instalacao.GravarConfiaveis(cat, jogo, L);
                L("Pronto.");
            }
            catch (Exception ex)
            {
                L("Erro: " + ex);
                codigo = 1;
            }
            finally
            {
                try
                {
                    if (jogo != null)
                    {
                        string arq = Path.Combine(jogo, "UserData", "pbsu-loader", "instalador.log");
                        Directory.CreateDirectory(Path.GetDirectoryName(arq));
                        File.WriteAllText(arq, log.ToString(), new UTF8Encoding(false));
                    }
                }
                catch { }
            }
            return codigo;
        }
    }
}
