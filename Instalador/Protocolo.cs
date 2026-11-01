using System;
using System.Linq;
using Microsoft.Win32;

namespace PbsuLoader
{
    /// <summary>
    /// O site oferece "Instalar pelo loader" com pbsloader://mod/&lt;id&gt;: o Windows abre o instalador com o mod
    /// marcado (e as dependências dele). O registro do protocolo fica no HKCU — sem administração — e é refeito a
    /// cada abertura da janela, para sobreviver a pasta trocada ou reinstalação.
    /// </summary>
    static class Protocolo
    {
        public const string Esquema = "pbsloader";

        public static void Registrar()
        {
            try
            {
                using (var chave = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Esquema))
                {
                    chave.SetValue("", "URL:PBSLoader");
                    chave.SetValue("URL Protocol", "");
                    using (var comando = chave.CreateSubKey(@"shell\open\command"))
                        comando.SetValue("", "\"" + Programa.Executavel + "\" \"%1\"");
                }
            }
            catch
            {
                // Sem o registro, o botão do site só não abre o instalador: ele continua funcionando normal.
            }
        }

        /// <summary>
        /// Extrai o id de "pbsloader://mod/&lt;id&gt;" (aceita "mod/" opcional e barra final). Devolve null quando
        /// o argumento não é do protocolo ou o id não é um id válido de catálogo.
        /// </summary>
        public static string IdDoArgumento(string arg)
        {
            if (arg == null || !arg.StartsWith(Esquema + "://", StringComparison.OrdinalIgnoreCase)) return null;
            string resto = arg.Substring(Esquema.Length + 3);
            // O prefixo "mod/" sai antes da barra final: assim "pbsloader://mod/" não vira o id "mod".
            if (resto.StartsWith("mod/", StringComparison.OrdinalIgnoreCase)) resto = resto.Substring(4);
            resto = resto.TrimEnd('/');
            if (resto.Length == 0) return null;
            try { resto = Uri.UnescapeDataString(resto); }
            catch (UriFormatException) { return null; }
            return IdValido(resto) ? resto : null;
        }

        internal static bool IdValido(string id)
            => id.Length > 0 && id.Length <= 64 && id.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
    }
}
