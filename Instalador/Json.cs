using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PbsuLoader
{
    /// <summary>
    /// JSON mínimo: lê para Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool e null, e escreve
    /// de volta com indentação. Basta para o catálogo, a API do GitHub e o registro do que foi instalado.
    /// </summary>
    static class Json
    {
        public static object Ler(string texto)
        {
            int i = 0;
            var v = Valor(texto, ref i);
            Espacos(texto, ref i);
            if (i != texto.Length) throw Erro(texto, i, "texto sobrando depois do JSON");
            return v;
        }

        public static string Escrever(object v)
        {
            var sb = new StringBuilder();
            Escrever(sb, v, 0);
            return sb.ToString();
        }

        // Atalhos para ler campos sem encher o código de casts.
        static object Campo(object v, string campo)
            => v is Dictionary<string, object> d && d.TryGetValue(campo, out var x) ? x : null;

        public static Dictionary<string, object> Obj(object v, string campo) => Campo(v, campo) as Dictionary<string, object>;

        public static List<object> Lista(object v, string campo) => Campo(v, campo) as List<object> ?? new List<object>();

        public static string Texto(object v, string campo, string padrao = null) => Campo(v, campo) as string ?? padrao;

        public static bool Bool(object v, string campo, bool padrao = false) => Campo(v, campo) is bool b ? b : padrao;

        static object Valor(string t, ref int i)
        {
            Espacos(t, ref i);
            if (i >= t.Length) throw Erro(t, i, "fim inesperado");
            char c = t[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                i++;
                Espacos(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return d; }
                while (true)
                {
                    Espacos(t, ref i);
                    string k = Cadeia(t, ref i);
                    Espacos(t, ref i);
                    Esperar(t, ref i, ':');
                    d[k] = Valor(t, ref i);
                    Espacos(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    Esperar(t, ref i, '}');
                    return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Espacos(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Valor(t, ref i));
                    Espacos(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    Esperar(t, ref i, ']');
                    return l;
                }
            }
            if (c == '"') return Cadeia(t, ref i);
            if (Palavra(t, ref i, "true")) return true;
            if (Palavra(t, ref i, "false")) return false;
            if (Palavra(t, ref i, "null")) return null;
            int ini = i;
            while (i < t.Length && "+-0123456789.eE".IndexOf(t[i]) >= 0) i++;
            if (i == ini) throw Erro(t, i, "valor inválido");
            return double.Parse(t.Substring(ini, i - ini), CultureInfo.InvariantCulture);
        }

        static string Cadeia(string t, ref int i)
        {
            Esperar(t, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= t.Length) throw Erro(t, i, "texto sem fim");
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) throw Erro(t, i, "escape sem fim");
                char e = t[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > t.Length) throw Erro(t, i, "escape \\u incompleto");
                        sb.Append((char)int.Parse(t.Substring(i, 4), NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }

        static bool Palavra(string t, ref int i, string p)
        {
            if (string.CompareOrdinal(t, i, p, 0, p.Length) != 0) return false;
            i += p.Length;
            return true;
        }

        static void Esperar(string t, ref int i, char c)
        {
            if (i >= t.Length || t[i] != c) throw Erro(t, i, "esperava '" + c + "'");
            i++;
        }

        static void Espacos(string t, ref int i)
        {
            while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
        }

        static FormatException Erro(string t, int i, string msg)
        {
            int linha = 1;
            for (int k = 0; k < i && k < t.Length; k++) if (t[k] == '\n') linha++;
            return new FormatException("JSON inválido na linha " + linha + ": " + msg);
        }

        static void Escrever(StringBuilder sb, object v, int nivel)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: Cadeia(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary d:
                    {
                        if (d.Count == 0) { sb.Append("{}"); break; }
                        sb.Append("{\n");
                        bool primeiro = true;
                        foreach (DictionaryEntry e in d)
                        {
                            if (!primeiro) sb.Append(",\n");
                            primeiro = false;
                            sb.Append(' ', (nivel + 1) * 2);
                            Cadeia(sb, (string)e.Key);
                            sb.Append(": ");
                            Escrever(sb, e.Value, nivel + 1);
                        }
                        sb.Append('\n').Append(' ', nivel * 2).Append('}');
                        break;
                    }
                case IEnumerable l:
                    {
                        sb.Append('[');
                        bool primeiro = true;
                        foreach (var x in l)
                        {
                            sb.Append(primeiro ? "\n" : ",\n");
                            primeiro = false;
                            sb.Append(' ', (nivel + 1) * 2);
                            Escrever(sb, x, nivel + 1);
                        }
                        if (!primeiro) sb.Append('\n').Append(' ', nivel * 2);
                        sb.Append(']');
                        break;
                    }
                default: Cadeia(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            }
        }

        static void Cadeia(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
