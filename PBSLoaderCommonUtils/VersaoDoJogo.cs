using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MelonLoader;
using MelonLoader.Utils;
using PBSLoaderCommonUtils.Interface;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    // Qual versão do jogo está rodando, pelo SHA-256 do GameAssembly.dll (o mesmo critério do instalador e do catálogo).
    // Numa versão não testada, o SDK entra em modo seguro: os ganchos não disparam, nada escreve nos componentes do jogo
    // e Avancado devolve null, porque campos e métodos podem ter mudado de lugar.
    internal static class VersaoDoJogo
    {
        // Versões testadas: nome -> SHA-256 do GameAssembly.dll. Mais versões podem vir em
        // UserData/PBSLoaderCommonUtils/versoes_testadas.txt, uma por linha: "nome=sha256".
        static readonly Dictionary<string, string> Testadas = new(StringComparer.OrdinalIgnoreCase)
        {
            ["a61a1ab69420147838c110f5c364f5b808387d218f55978a65a8f859f036bacc"] = "307",
        };

        internal static string Nome = "?";
        internal static string Sha = "";
        internal static bool Testada;
        internal static MelonPreferences_Entry<bool> Permitir;
        internal static bool ForcarDesconhecida; // teste automático

        static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");

        internal static void Iniciar(MelonPreferences_Category cat)
        {
            Permitir = cat.CreateEntry("PermitirVersaoNaoTestada", false, "Permitir versão do jogo não testada",
                "Numa versão do jogo que o SDK não conhece, os mods ficam em modo seguro (sem ganchos nem acesso ao jogo por dentro). Ligue só se souber que os mods funcionam nela.");
            try
            {
                var arq = Path.Combine(MelonEnvironment.GameRootDirectory, "GameAssembly.dll");
                Sha = ShaComCache(arq);
                var extras = Path.Combine(Dir, "versoes_testadas.txt");
                if (File.Exists(extras))
                    foreach (var l in File.ReadAllLines(extras))
                    {
                        int i = l.IndexOf('=');
                        if (i > 0) Testadas[l.Substring(i + 1).Trim()] = l.Substring(0, i).Trim();
                    }
                Testada = Testadas.TryGetValue(Sha, out var nome);
                Nome = Testada ? nome : "não testada";
            }
            catch (Exception e) { MelonLogger.Warning("[Sdk] não deu para conferir a versão do jogo: " + e.Message); Nome = "desconhecida"; }
            MelonLogger.Msg($"[Sdk] jogo versão {Nome} (GameAssembly {(Sha.Length >= 12 ? Sha.Substring(0, 12) : "?")}…){(Sdk.ModoSeguro ? ": MODO SEGURO" : "")}");
        }

        // O hash do GameAssembly (~60 MB) fica guardado com tamanho e data: só recalcula quando o arquivo muda.
        static string ShaComCache(string arquivo)
        {
            var fi = new FileInfo(arquivo);
            string chave = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            var cache = Path.Combine(Dir, "versao_do_jogo.txt");
            try
            {
                if (File.Exists(cache))
                {
                    var l = File.ReadAllText(cache).Trim().Split('|');
                    if (l.Length == 3 && l[0] + "|" + l[1] == chave) return l[2];
                }
            }
            catch { }
            string sha;
            using (var f = File.OpenRead(arquivo))
            using (var h = SHA256.Create())
                sha = Convert.ToHexString(h.ComputeHash(f)).ToLowerInvariant();
            try { Directory.CreateDirectory(Dir); File.WriteAllText(cache, chave + "|" + sha); } catch { }
            return sha;
        }

        static float _proximoAviso;

        internal static void Quadro()
        {
            if (Time.unscaledTime < _proximoAviso) return;
            _proximoAviso = Time.unscaledTime + 0.5f;
            Avisos.Definir("PBSLoaderCommonUtils", "versao-do-jogo", Sdk.ModoSeguro && Veiculos.Veiculo.Existe, Gravidade.Falha,
                "Versão do jogo não testada: mods em modo seguro",
                "Atualize o PBSLoader, ou ligue \"Permitir versão do jogo não testada\" se souber que os mods funcionam nesta versão");
        }
    }
}
