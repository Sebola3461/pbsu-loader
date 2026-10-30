using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;
using PBSLoaderCommonUtils.Mapas;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Dados guardados de uma sessão para outra, em chave e valor (números, textos, listas, classes simples), num arquivo
    /// JSON por mod e por lugar. Gravar só marca a gaveta: o arquivo é escrito 1,5 s depois da última mudança, na troca de
    /// cena e ao fechar o jogo. Pegue a gaveta de novo quando o ônibus ou o mapa trocar (<see cref="Sdk.VeiculoTrocou"/>,
    /// <see cref="Sdk.MapaCarregou"/>): cada ônibus e cada mapa tem a sua.
    /// </summary>
    public sealed class Gaveta
    {
        /// <summary>"global", "onibus/&lt;nome&gt;" ou "mapa/&lt;nome&gt;".</summary>
        public string Lugar { get; }
        public string Mod { get; }
        /// <summary>false quando não há ônibus/mapa: a gaveta funciona, mas não grava nada.</summary>
        public bool Persistente { get; }

        internal readonly string Arquivo;
        readonly Dictionary<string, JsonElement> _dados = new(StringComparer.Ordinal);
        internal bool Suja;

        internal Gaveta(string lugar, string mod, string arquivo)
        {
            Lugar = lugar; Mod = mod; Arquivo = arquivo; Persistente = arquivo != null;
            if (arquivo == null || !File.Exists(arquivo)) return;
            try
            {
                var lido = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(arquivo));
                if (lido != null) foreach (var kv in lido) _dados[kv.Key] = kv.Value.Clone();
            }
            catch (Exception e) { MelonLogger.Warning($"[Salvos] {arquivo} ilegível, começando vazio: {e.Message}"); }
        }

        public bool Tem(string chave) => _dados.ContainsKey(chave);
        public IEnumerable<string> Chaves => _dados.Keys;

        /// <summary>Lê o valor, ou <paramref name="padrao"/> se não existir ou não for convertível para <typeparamref name="T"/>.</summary>
        public T Ler<T>(string chave, T padrao = default)
        {
            if (!_dados.TryGetValue(chave, out var e)) return padrao;
            try { return e.Deserialize<T>(Salvos.Opcoes); }
            catch { return padrao; }
        }

        /// <summary>Grava (ou troca) o valor. O arquivo é escrito logo depois.</summary>
        public void Gravar<T>(string chave, T valor)
        {
            _dados[chave] = JsonSerializer.SerializeToElement(valor, Salvos.Opcoes);
            Marcar();
        }

        public void Apagar(string chave) { if (_dados.Remove(chave)) Marcar(); }
        public void Limpar() { if (_dados.Count > 0) { _dados.Clear(); Marcar(); } }

        void Marcar()
        {
            if (!Persistente) return;
            Suja = true;
            Salvos.Agendar();
        }

        internal void Escrever()
        {
            if (!Suja || !Persistente) return;
            Suja = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                var tmp = Arquivo + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_dados, Salvos.OpcoesArquivo), new UTF8Encoding(false));
                File.Copy(tmp, Arquivo, true);
                File.Delete(tmp);
            }
            catch (Exception e) { Suja = true; Sdk.Registrar(e, "Salvos " + Arquivo); }
        }
    }

    /// <summary>Gavetas de dados que sobrevivem entre sessões (ver <see cref="Gaveta"/>).</summary>
    public static class Salvos
    {
        internal static readonly JsonSerializerOptions Opcoes = new() { IncludeFields = true };
        internal static readonly JsonSerializerOptions OpcoesArquivo = new() { IncludeFields = true, WriteIndented = true };

        static readonly Dictionary<string, Gaveta> _gavetas = new(StringComparer.OrdinalIgnoreCase);
        static float _escreverEm = -1f;

        /// <summary>Pasta onde ficam os arquivos (UserData/PBSLoaderCommonUtils/Salvos).</summary>
        public static string Pasta => Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils", "Salvos");

        /// <summary>Dados do mod que valem em qualquer ônibus e mapa.</summary>
        public static Gaveta Global(string mod) => Abrir("global", mod);

        /// <summary>Dados do mod para o ônibus que o jogador dirige agora (sem ônibus: gaveta que não grava).</summary>
        public static Gaveta DoOnibus(string mod) => Abrir(Veiculos.Veiculo.Existe ? "onibus/" + Limpo(Veiculos.Veiculo.Nome) : null, mod);

        /// <summary>Dados do mod para o mapa carregado agora (sem mapa: gaveta que não grava).</summary>
        public static Gaveta DoMapa(string mod) => Abrir(Mapa.Existe ? "mapa/" + Limpo(Mapa.Nome) : null, mod);

        /// <summary>Escreve agora tudo que estiver pendente (normalmente não precisa).</summary>
        public static void GravarAgora()
        {
            _escreverEm = -1f;
            foreach (var g in _gavetas.Values) g.Escrever();
        }

        internal static Gaveta Abrir(string lugar, string mod)
        {
            if (string.IsNullOrWhiteSpace(mod)) throw new ArgumentException("mod vazio");
            if (lugar == null) return new Gaveta("(sem lugar)", mod, null);
            string chave = lugar + "|" + mod;
            if (_gavetas.TryGetValue(chave, out var g)) return g;
            var arq = Path.Combine(Pasta, lugar.Replace('/', Path.DirectorySeparatorChar), Limpo(mod) + ".json");
            g = new Gaveta(lugar, mod, arq);
            _gavetas[chave] = g;
            return g;
        }

        internal static void Agendar() => _escreverEm = Time.unscaledTime + 1.5f;

        internal static void Quadro()
        {
            if (_escreverEm > 0f && Time.unscaledTime >= _escreverEm) GravarAgora();
        }

        // Para o teste automático: esquece as gavetas em memória (a próxima leitura vem do disco).
        internal static void Esquecer() { GravarAgora(); _gavetas.Clear(); }

        static string Limpo(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "_";
            var sb = new StringBuilder(s.Length);
            var proibidos = Path.GetInvalidFileNameChars();
            foreach (var c in s.Trim()) sb.Append(Array.IndexOf(proibidos, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        internal static string Resumo()
        {
            if (_gavetas.Count == 0) return "nenhuma gaveta aberta\n";
            var sb = new StringBuilder();
            foreach (var g in _gavetas.Values) sb.Append($"{g.Lugar} / {g.Mod}: {System.Linq.Enumerable.Count(g.Chaves)} chave(s){(g.Suja ? " (a gravar)" : "")}\n");
            return sb.ToString();
        }
    }
}
