using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace PBSLoaderCommonUtils.Conteudo
{
    public enum Decisao { Perguntar, Permitir, Recusar, SempreRecusar }

    // Decisões do jogador por SHA-256 da DLL (%LOCALAPPDATA%/PBSLoaderCommonUtils/confianca.json). "Recusar" vale só
    // para esta sessão; "Permitir" e "Sempre recusar" ficam gravados. Um hash novo (pacote atualizado) pergunta
    // de novo. Hashes em confiaveis.txt (um por linha, gravado pelo PBSU Loader a partir do catálogo) já vêm permitidos.
    // Os dois arquivos ficam fora da pasta do jogo: um ônibus ou mapa extraído nela poderia trazer um "permitir" pronto.
    internal static class Confianca
    {
        static Dictionary<string, string> _gravadas;
        static readonly HashSet<string> _recusadasNaSessao = new(StringComparer.OrdinalIgnoreCase);
        static HashSet<string> _catalogo;

        static readonly string Dir = PastaDoUsuario();
        static string DirAntigo => Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");

        static string PastaDoUsuario()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local) ? null : Path.Combine(local, "PBSLoaderCommonUtils");
        }

        internal static Decisao Consultar(string sha)
        {
            Carregar();
            if (_catalogo.Contains(sha)) return Decisao.Permitir;
            if (_recusadasNaSessao.Contains(sha)) return Decisao.Recusar;
            if (_gravadas.TryGetValue(sha, out var d))
                return d == "permitir" ? Decisao.Permitir : d == "sempre-recusar" ? Decisao.SempreRecusar : Decisao.Perguntar;
            return Decisao.Perguntar;
        }

        internal static void Decidir(Pacote p, Decisao d)
        {
            Carregar();
            if (d == Decisao.Recusar) { _recusadasNaSessao.Add(p.Sha256); return; }
            if (d == Decisao.Permitir) _gravadas[p.Sha256] = "permitir";
            else if (d == Decisao.SempreRecusar) _gravadas[p.Sha256] = "sempre-recusar";
            else return;
            if (Dir == null) { MelonLogger.Warning("[Conteudo] sem pasta de dados do usuário: a decisão vale só nesta sessão"); return; }
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, "confianca.json"),
                    JsonSerializer.Serialize(_gravadas, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { MelonLogger.Warning("[Conteudo] não deu para gravar confianca.json: " + e.Message); }
            MelonLogger.Msg($"[Conteudo] {p.Nome} ({p.Sha256.Substring(0, 12)}…): {d}");
        }

        static void Carregar()
        {
            if (_gravadas != null) return;
            _gravadas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _catalogo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Dir != null)
            {
                foreach (var kv in LerDecisoes(Path.Combine(Dir, "confianca.json"))) _gravadas[kv.Key] = kv.Value;
                try
                {
                    var c = Path.Combine(Dir, "confiaveis.txt");
                    if (File.Exists(c))
                        foreach (var l in File.ReadAllLines(c)) { var t = l.Trim(); if (t.Length == 64) _catalogo.Add(t); }
                }
                catch (Exception e) { MelonLogger.Warning("[Conteudo] confiaveis.txt ilegível: " + e.Message); }
            }
            // O arquivo antigo, na pasta do jogo, não é aproveitado: qualquer decisão ali pode ter vindo junto com um pacote.
            var antigas = LerDecisoes(Path.Combine(DirAntigo, "confianca.json"));
            if (antigas.Count > 0)
                MelonLogger.Msg($"[Conteudo] {antigas.Count} decisão(ões) de UserData/PBSLoaderCommonUtils/confianca.json ignoradas: esses pacotes vão perguntar de novo");
        }

        static Dictionary<string, string> LerDecisoes(string arquivo)
        {
            try
            {
                if (File.Exists(arquivo))
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(arquivo)) ?? new();
            }
            catch (Exception e) { MelonLogger.Warning($"[Conteudo] {arquivo} ilegível, perguntando de novo: " + e.Message); }
            return new();
        }
    }

    /// <summary>
    /// Pergunta ao jogador se um pacote de scripts pode rodar. Desenhada pela própria PBSLoaderCommonUtils (IMGUI),
    /// uma pergunta por vez. Enquanto aberta, o cursor fica solto e o teclado do jogo suspenso.
    /// </summary>
    public static class Confirmacao
    {
        sealed class Pedido { public Pacote P; public Action<Decisao> Resposta; }
        static readonly Queue<Pedido> _fila = new();
        static Pedido _atual;
        static GUIStyle _titulo, _texto, _caixa;

        /// <summary>Há uma pergunta na tela.</summary>
        public static bool Aberta => _atual != null;
        /// <summary>O pacote perguntado agora (para quem quiser desenhar a própria janela e responder com <see cref="Responder"/>).</summary>
        public static Pacote Atual => _atual?.P;
        /// <summary>Outro mod (a PBSLoaderCommonInterface) desenha a pergunta: a lib não desenha a dela.</summary>
        public static bool DesenhadaPorOutroMod { get; set; }

        internal static void Perguntar(Pacote p, Action<Decisao> resposta) => _fila.Enqueue(new Pedido { P = p, Resposta = resposta });

        /// <summary>Responde a pergunta atual.</summary>
        public static void Responder(Decisao d)
        {
            var a = _atual;
            if (a == null) return;
            _atual = null;
            Confianca.Decidir(a.P, d);
            try { a.Resposta(d); } catch (Exception e) { Sdk.Registrar(e, "Confirmacao"); }
        }

        internal static void Quadro()
        {
            if (_atual == null && _fila.Count > 0) _atual = _fila.Dequeue();
            if (_atual == null) return;
            Entrada.Mouse.Soltar("PBSLoaderCommonUtils.Confirmacao");
            Entrada.Teclado.SuspenderJogo("PBSLoaderCommonUtils.Confirmacao");
        }

        internal static void Desenhar()
        {
            var a = _atual;
            if (a == null || DesenhadaPorOutroMod) return;
            var p = a.P;
            _caixa ??= new GUIStyle(GUI.skin.box);
            _titulo ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true };
            _texto ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            float w = Mathf.Min(620f, Screen.width - 40f), h = 300f;
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, "", _caixa); GUI.Box(r, "", _caixa); // duas vezes: o fundo padrão é transparente demais
            float x = r.x + 20f, y = r.y + 16f, cw = w - 40f;
            GUI.Label(new Rect(x, y, cw, 30f), $"Rodar os scripts de \"{p.Nome}\"?", _titulo); y += 38f;
            GUI.Label(new Rect(x, y, cw, 96f),
                $"Este {(p.Origem == OrigemDoPacote.Onibus ? "ônibus" : "mapa")} traz código próprio (de {p.Autor}, versão {p.Versao}). " +
                "Ele roda com as mesmas permissões dos seus mods: só permita se você confia em quem fez.", _texto); y += 100f;
            GUI.Label(new Rect(x, y, cw, 44f), $"SHA-256 {p.Sha256}\n{p.Pasta}", _texto); y += 62f;
            float bw = (cw - 20f) / 3f;
            if (GUI.Button(new Rect(x, y, bw, 36f), "Permitir")) Responder(Decisao.Permitir);
            if (GUI.Button(new Rect(x + bw + 10f, y, bw, 36f), "Agora não")) Responder(Decisao.Recusar);
            if (GUI.Button(new Rect(x + 2 * (bw + 10f), y, bw, 36f), "Nunca")) Responder(Decisao.SempreRecusar);
        }
    }
}
