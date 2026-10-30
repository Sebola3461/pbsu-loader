using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PBSLoaderCommonInterface.Garagem
{
    // Tudo o que a prévia 3D precisa de um ônibus de mod, montado fora da thread principal: os modelos
    // da parte 1 (models.txt), as texturas já reduzidas e a lista de materiais que recebem a skin.
    internal sealed class PacoteOnibus
    {
        internal string ini, nome;
        internal readonly List<Modelo3DS> modelos = new();
        internal readonly Dictionary<string, ImagemRgba> texturas = new(StringComparer.OrdinalIgnoreCase); // por caminho
        internal readonly Dictionary<Material3DS, string> texturaDe = new();                                // material -> caminho
        internal string pastaSkins;
        // Rodas do wheels.txt: centro em repouso (posY - suspensionDistance * suspensionAnchor, a mesma
        // conta que dá o assentamento no jogo) e raio. Chave: "fl", "fr", "rl", "rr", "rl2"...
        internal readonly Dictionary<string, (float x, float y, float z, float raio)> rodas = new(StringComparer.OrdinalIgnoreCase);
        internal float? chao;     // altura do chão (base das rodas), quando o wheels.txt existe
        internal float massa = 10000f;
        internal float[] motorista, passageiro; // cabeça do motorista e câmera de passageiro (posDriverHead, posPassengerCam1)
        internal double ms;
        internal string erro;
    }

    internal static class OnibusMod
    {
        internal const int LadoTextura = 512, LadoSkin = 1024;

        // Leitor de .ini do jogo: [seção] chave=valor, comentários com ; ou #, sem diferenciar maiúsculas.
        internal static Dictionary<string, Dictionary<string, string>> LerIni(string caminho)
        {
            var r = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> sec = null;
            // Os mods vêm em UTF-8 ou em Latin-1 ("Elétrico - Area 2.png"): UTF-8 quando o arquivo é UTF-8
            // válido, senão Latin-1 (lido errado, o nome não bate com o arquivo e o slot da skin some).
            var bytes = File.ReadAllBytes(caminho);
            string texto;
            try { texto = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { texto = Encoding.Latin1.GetString(bytes); }
            if (texto.Length > 0 && texto[0] == '﻿') texto = texto.Substring(1);
            foreach (var bruta in texto.Split('\n'))
            {
                var l = bruta.Trim();
                if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
                if (l[0] == '[' && l.EndsWith("]"))
                {
                    var nome = l.Substring(1, l.Length - 2).Trim();
                    if (!r.TryGetValue(nome, out sec)) r[nome] = sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                int eq = l.IndexOf('=');
                if (eq <= 0 || sec == null) continue;
                sec[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
            }
            return r;
        }

        static string Valor(Dictionary<string, Dictionary<string, string>> ini, string sec, string chave) =>
            ini.TryGetValue(sec, out var s) && s.TryGetValue(chave, out var v) && v.Length > 0 ? v : null;

        // Carrega o ônibus descrito por "ini" (arquivo .ini dentro da pasta de mods de ônibus).
        internal static PacoteOnibus Carregar(string ini, CancellationToken cancelar)
        {
            var sw = Stopwatch.StartNew();
            var p = new PacoteOnibus { ini = ini };
            try
            {
                var cfg = LerIni(ini);
                p.nome = Valor(cfg, "mod", "name") ?? Path.GetFileNameWithoutExtension(ini);
                var raizMods = Path.GetDirectoryName(ini);
                var baseDir = Path.Combine(raizMods, Valor(cfg, "config", "baseDir") ?? Path.GetFileNameWithoutExtension(ini));
                var arqModelos = Valor(cfg, "config", "models1");
                if (arqModelos == null) { p.erro = "sem models1 no ini"; return p; }
                var modelos = LerIni(Path.Combine(baseDir, arqModelos));
                if (!modelos.TryGetValue("part1_3ds", out var parte)) { p.erro = "sem [part1_3ds]"; return p; }
                var pastaTex = Path.Combine(baseDir, Valor(cfg, "folders", "textures") ?? "textures");
                if (float.TryParse(Valor(cfg, "data", "mass1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var massa) && massa > 0)
                    p.massa = massa;
                p.motorista = Ponto(cfg, "posDriverHead");
                p.passageiro = Ponto(cfg, "posPassengerCam1");
                var arqRodas = Valor(cfg, "config", "wheels1");
                if (arqRodas != null && File.Exists(Path.Combine(baseDir, arqRodas))) LerRodas(p, Path.Combine(baseDir, arqRodas));
                p.pastaSkins = Path.Combine(baseDir, Valor(cfg, "folders", "skins") ?? "skins");

                for (int i = 1; i <= 32; i++)
                {
                    if (!parte.TryGetValue("model" + i, out var rel) || rel.Length == 0) continue;
                    cancelar.ThrowIfCancellationRequested();
                    var arq = Path.Combine(baseDir, rel.Replace('\\', Path.DirectorySeparatorChar));
                    if (!File.Exists(arq)) continue;
                    var m = Modelo3DS.Ler(arq);
                    if (m != null) p.modelos.Add(m);
                }
                if (p.modelos.Count == 0) { p.erro = "nenhum modelo legível"; return p; }

                // Texturas: procuradas na pasta do modelo, na pasta de texturas e na base do mod.
                var pedidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in p.modelos)
                    foreach (var mat in m.materiais.Values)
                    {
                        if (string.IsNullOrEmpty(mat.textura)) continue;
                        var c = Resolver(mat.textura, m.pasta, pastaTex, baseDir);
                        if (c == null) continue;
                        p.texturaDe[mat] = c;
                        pedidas.Add(c);
                    }
                var lista = new List<string>(pedidas);
                var res = new ImagemRgba[lista.Count];
                Parallel.For(0, lista.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2), CancellationToken = cancelar },
                    i => res[i] = Imagem.Carregar(lista[i], LadoTextura));
                for (int i = 0; i < lista.Count; i++) if (res[i] != null) p.texturas[lista[i]] = res[i];
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { p.erro = e.GetType().Name + ": " + e.Message; }
            p.ms = sw.Elapsed.TotalMilliseconds;
            return p;
        }

        static bool EhImagem(string a) =>
            a.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".tga", StringComparison.OrdinalIgnoreCase);

        // Skin padrão de um ônibus: o primeiro descritor .txt da pasta de skins (ou a primeira imagem),
        // o mesmo que a lista de pinturas mostra primeiro. Leitura leve (o ini e a listagem da pasta).
        internal static string SkinPadrao(string ini)
        {
            var cfg = LerIni(ini);
            var baseDir = Path.Combine(Path.GetDirectoryName(ini), Valor(cfg, "config", "baseDir") ?? Path.GetFileNameWithoutExtension(ini));
            var pasta = Path.Combine(baseDir, Valor(cfg, "folders", "skins") ?? "skins");
            if (!Directory.Exists(pasta)) return null;
            var arqs = Directory.GetFiles(pasta);
            Array.Sort(arqs, StringComparer.OrdinalIgnoreCase);
            foreach (var a in arqs) if (a.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && SlotsDaSkin(a).Count > 0) return a;
            foreach (var a in arqs) if (EhImagem(a)) return a;
            return null;
        }

        // As skins de um ônibus, como a lista de pinturas do jogo: havendo descritores .txt, só eles (as
        // imagens soltas da pasta são as partes deles: placas, vidros, adesivos); sem nenhum, as imagens.
        internal static List<string> Skins(string ini)
        {
            var r = new List<string>();
            var cfg = LerIni(ini);
            var baseDir = Path.Combine(Path.GetDirectoryName(ini), Valor(cfg, "config", "baseDir") ?? Path.GetFileNameWithoutExtension(ini));
            var pasta = Path.Combine(baseDir, Valor(cfg, "folders", "skins") ?? "skins");
            if (!Directory.Exists(pasta)) return r;
            foreach (var a in Directory.GetFiles(pasta, "*.txt"))
                if (SlotsDaSkin(a).Count > 0) r.Add(a);
            if (r.Count == 0)
                foreach (var a in Directory.GetFiles(pasta)) if (EhImagem(a)) r.Add(a);
            return r;
        }

        // Slots de uma skin: um .txt com [skin] skin001=arquivo ... skin010=arquivo (relativos à pasta da
        // skin); uma imagem usa o .txt de mesmo nome se houver, senão vale só para o slot 1.
        internal static Dictionary<int, string> SlotsDaSkin(string sel)
        {
            var r = new Dictionary<int, string>();
            var pasta = Path.GetDirectoryName(sel);
            var txt = sel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? sel : Path.ChangeExtension(sel, ".txt");
            if (File.Exists(txt))
            {
                var ini = LerIni(txt);
                if (ini.TryGetValue("skin", out var s))
                    for (int i = 1; i <= 10; i++)
                        if (s.TryGetValue("skin" + i.ToString("000"), out var a) && a.Length > 0)
                        {
                            var c = Path.Combine(pasta, a.Replace('\\', Path.DirectorySeparatorChar));
                            if (File.Exists(c)) r[i] = c;
                        }
            }
            if (r.Count == 0 && EhImagem(sel) && File.Exists(sel)) r[1] = sel;
            return r;
        }

        static float[] Ponto(Dictionary<string, Dictionary<string, string>> cfg, string sec)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var r = new float[3];
            string[] k = { "posX", "posY", "posZ" };
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(Valor(cfg, sec, k[i]), System.Globalization.NumberStyles.Float, ci, out r[i])) return null;
            return r;
        }

        // leftwheel1/rightwheel1 = eixo dianteiro (fl/fr), leftwheel2 = rl, leftwheel3 = rl2, e assim por diante.
        static void LerRodas(PacoteOnibus p, string arq)
        {
            var ini = LerIni(arq);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float F(Dictionary<string, string> s, string k, float pad) =>
                s.TryGetValue(k, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out var f) ? f : pad;
            float chao = float.MaxValue;
            int nRodas = 0;
            foreach (var (sec, _) in ini)
                if (sec.StartsWith("leftwheel", StringComparison.OrdinalIgnoreCase) || sec.StartsWith("rightwheel", StringComparison.OrdinalIgnoreCase)) nRodas++;
            float carga = p.massa / Math.Max(1, nRodas) * 9.81f; // peso sobre cada roda (N)
            foreach (var (sec, vals) in ini)
            {
                string lado = sec.StartsWith("leftwheel", StringComparison.OrdinalIgnoreCase) ? "l"
                    : sec.StartsWith("rightwheel", StringComparison.OrdinalIgnoreCase) ? "r" : null;
                if (lado == null || !int.TryParse(sec.Substring(lado == "l" ? 9 : 10), out var n) || n < 1) continue;
                string chave = n == 1 ? "f" + lado : n == 2 ? "r" + lado : "r" + lado + (n - 1);
                float raio = F(vals, "radius", 0.5f);
                float curso = F(vals, "suspensionDistance", 0);
                float y = F(vals, "posY", 0) - curso * F(vals, "suspensionAnchor", 0);
                // A mola comprime com o peso do ônibus (no Torino 1519: 2500 kg por roda / 202257 N/m = 12 cm):
                // a roda sobe para dentro da caixa, como na partida.
                float mola = F(vals, "springRate", 0);
                if (mola > 0) y += Math.Min(carga / mola, curso * 0.6f);
                p.rodas[chave] = (F(vals, "posX", 0), y, F(vals, "posZ", 0), raio);
                chao = Math.Min(chao, y - raio);
            }
            if (chao < float.MaxValue) p.chao = chao;
        }

        // "_wheel_fl_", "Roda_TD_wheel_rr__skin003_", "_wheel_rl2_" -> "fl", "rr", "rl2". Fora: volante,
        // peças das partes articuladas (_t1_, _t2_: rodas delas vêm de outros arquivos).
        static readonly System.Text.RegularExpressions.Regex ReRoda =
            new(@"_wheel_(?:suspension_)?([fr][lr]\d?)_", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static string ChaveRoda(string nome)
        {
            if (nome == null || nome.IndexOf("steering", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (nome.IndexOf("_t1_", StringComparison.OrdinalIgnoreCase) >= 0 || nome.IndexOf("_t2_", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            var m = ReRoda.Match(nome);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        // Desloca as peças de cada roda para o assentamento do wheels.txt: o vetor vem da maior peça
        // (o pneu) e vale para todas as peças daquela roda, para elas continuarem encaixadas.
        static Dictionary<Objeto3DS, (float dx, float dy, float dz)> Assentar(PacoteOnibus p)
        {
            var r = new Dictionary<Objeto3DS, (float, float, float)>();
            if (p.rodas.Count == 0) return r;
            var grupos = new Dictionary<string, List<Objeto3DS>>();
            foreach (var m in p.modelos)
                foreach (var o in m.objetos)
                {
                    var k = ChaveRoda(o.nome);
                    if (k == null || !p.rodas.ContainsKey(k)) continue;
                    if (!grupos.TryGetValue(k, out var g)) grupos[k] = g = new List<Objeto3DS>();
                    g.Add(o);
                }
            foreach (var (k, g) in grupos)
            {
                Objeto3DS maior = null; float maiorTam = -1; float cx = 0, cy = 0, cz = 0;
                foreach (var o in g)
                {
                    if (o.nome.IndexOf("suspension", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var (mn, mx) = Caixa(o.pos);
                    float tam = (mx[1] - mn[1]) * (mx[2] - mn[2]);
                    if (tam > maiorTam) { maiorTam = tam; maior = o; cx = (mn[0] + mx[0]) / 2; cy = (mn[1] + mx[1]) / 2; cz = (mn[2] + mx[2]) / 2; }
                }
                if (maior == null) continue;
                var alvo = p.rodas[k];
                // x fica (o modelo já tem a bitola e o lado certos), a não ser que a roda esteja longe demais.
                float dx = Math.Abs(cx - alvo.x) > 0.6f ? alvo.x - cx : 0f;
                float dy = alvo.y - cy, dz = Math.Abs(cz - alvo.z) > 0.05f ? alvo.z - cz : 0f;
                if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) < 0.002f) continue;
                foreach (var o in g) r[o] = (dx, dy, dz);
            }
            return r;
        }

        static bool TexturaDeChuva(string t)
        {
            var n = Path.GetFileNameWithoutExtension(t);
            return n.IndexOf("chuva", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("regen", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("rain", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("wet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static (float[] mn, float[] mx) Caixa(float[] v)
        {
            var mn = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var mx = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i + 2 < v.Length; i += 3)
                for (int k = 0; k < 3; k++) { if (v[i + k] < mn[k]) mn[k] = v[i + k]; if (v[i + k] > mx[k]) mx[k] = v[i + k]; }
            return (mn, mx);
        }

        static string Resolver(string nome, params string[] pastas)
        {
            nome = nome.Replace('\\', Path.DirectorySeparatorChar);
            foreach (var d in pastas)
            {
                if (d == null) continue;
                var c = Path.Combine(d, nome);
                if (File.Exists(c)) return c;
                c = Path.Combine(d, Path.GetFileName(nome));
                if (File.Exists(c)) return c;
            }
            return null;
        }

        // ---------- objetos -> lotes ----------

        // Como o objeto aparece na prévia. O jogo decide pelo nome do objeto: "_skin001_" (ou "SK1")
        // recebe a skin 1 e assim até a 10; "_transparent_"/"_glass_" são vidros; o que só existe em
        // algum estado do jogo (luz acesa, passageiros, chuva, gatilhos) fica de fora do showroom.
        // VidroLimpo: a camada de chuva das janelas ("_windows_wet_", textura regen/vidrochuva), por cima
        // do vidro normal; no jogo só aparece chovendo.
        internal enum Tipo { Normal, Vidro, VidroLimpo, Espelho }
        const int MaxVertLote = 40000;

        // "anim": estados alternativos das animações (volante, portas, letreiro), que no jogo só aparecem
        // durante elas; desenhados todos juntos, sobrepõem e quebram o modelo.
        // Estados das animações: "_on_" (luz acesa, porta da rampa aberta, quadro ligado) fica de fora e
        // "_off_" (o repouso, como as lentes dos faróis e as janelas "_anim1_off_") fica.
        // "wet": a camada de chuva das janelas (por cima do vidro normal), que no jogo só aparece chovendo.
        static readonly string[] Ocultos = { "_on_", "wet", "_stand_", "_sit_", "_passenger", "_teleport_", "_click_", "_btlight", "_driver_",
            "collider", "_collision", "_trigger", "_release_", "_waitdoor" }; // colisores, gatilhos e pontos de rota são invisíveis no jogo
        // Vidros com esses nomes são janelas ("_passenger_windows_front_off_", "_driver_window_off_"), não
        // passageiros nem motorista.
        static readonly string[] SoForaDeVidros = { "_passenger", "_driver_" };

        // As animações do jogo são quadro a quadro, um objeto por quadro ("_wipers1_00_" .. "_wipers1_46_")
        // ou por estado (chave "_key_stage1_", alavanca "_manualgear3_"): o jogo mostra um de cada vez.
        // Na prévia fica só a pose de repouso (quadro 00); estados alternativos ficam de fora.
        static readonly System.Text.RegularExpressions.Regex ReQuadro =
            new(@"_wipers\d*_(\d+)_", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly string[] Estados = { "_key_stage", "manualgear", "autogear" };

        // Pontos do caminho dos passageiros nas portas ("_r2y5_", "_r1b4_", "_l1a2_": porta, trecho, ponto):
        // quadradinhos que o jogo usa como rota e não desenha.
        static readonly System.Text.RegularExpressions.Regex RePonto =
            new(@"(^|_)[rl]\d+[a-z]\d+(_|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        internal static bool Oculto(string nome)
        {
            if (nome == null) return false;
            bool vidro = nome.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 || nome.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
                         || nome.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0 || nome.IndexOf("vidro", StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var t in Ocultos)
                if (nome.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 && !(vidro && Array.IndexOf(SoForaDeVidros, t) >= 0)) return true;
            foreach (var t in Estados) if (nome.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (RePonto.IsMatch(nome)) return true;
            var q = ReQuadro.Match(nome);
            if (q.Success && int.TryParse(q.Groups[1].Value, out var n) && n != 0) return true;
            return false;
        }

        internal static int SlotSkin(string nome)
        {
            if (nome == null) return 0;
            int i = nome.IndexOf("_skin", StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && i + 8 < nome.Length && int.TryParse(nome.AsSpan(i + 5, 3), out var n) && n >= 1 && n <= 10) return n;
            i = nome.IndexOf("SK", StringComparison.Ordinal);
            if (i >= 0 && i + 2 < nome.Length && char.IsDigit(nome[i + 2])) { int d = nome[i + 2] - '0'; return d == 0 ? 10 : d; }
            return 0;
        }

        static Tipo TipoDe(string nome)
        {
            bool molhado = nome.IndexOf("wet", StringComparison.OrdinalIgnoreCase) >= 0;
            if (nome.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0 || nome.IndexOf("_glass", StringComparison.OrdinalIgnoreCase) >= 0)
                return molhado ? Tipo.VidroLimpo : Tipo.Vidro;
            if (nome.IndexOf("_mirror", StringComparison.OrdinalIgnoreCase) >= 0) return Tipo.Espelho;
            return Tipo.Normal;
        }

        // Funde todas as partes que usam o mesmo material, slot de skin e tipo numa malha só (índices de
        // 32 bits): ~1000 objetos viram ~100 malhas, o que barateia a montagem e o desenho.
        internal static List<Lote> Lotes(PacoteOnibus p)
        {
            var lotes = new Dictionary<(int, string, int, Tipo), Lote>();
            var cheios = new List<Lote>();
            var desloc = Assentar(p);
            for (int mi = 0; mi < p.modelos.Count; mi++)
            {
                var m = p.modelos[mi];
                foreach (var o in m.objetos)
                {
                    if (Oculto(o.nome)) continue;
                    int skin = SlotSkin(o.nome);
                    var tipo = TipoDe(o.nome);
                    int nv = o.pos.Length / 3;
                    var mapa = new int[nv];
                    desloc.TryGetValue(o, out var d);
                    foreach (var (mat, tris) in o.partes)
                    {
                        if (string.Equals(mat, "marcador", StringComparison.OrdinalIgnoreCase)) continue; // material de marcação
                        // Textura de chuva nos vidros (regen.png, vidrochuva.png...), mesmo sem "wet" no nome.
                        m.materiais.TryGetValue(mat ?? "", out var mc);
                        if (mc?.textura != null && TexturaDeChuva(mc.textura)) continue;
                        var chave = (mi, mat ?? "", skin, tipo);
                        if (!lotes.TryGetValue(chave, out var l))
                        {
                            m.materiais.TryGetValue(mat ?? "", out var md);
                            lotes[chave] = l = new Lote { material = md, skin = skin, tipo = tipo };
                            if (md != null && p.texturaDe.TryGetValue(md, out var c)) l.textura = c;
                        }
                        // Lote cheio (~40 mil vértices): fecha e começa outro, para cada malha caber no
                        // orçamento de um frame na hora de criar.
                        if (l.pos.Count / 3 > MaxVertLote)
                        {
                            cheios.Add(l);
                            var novo = new Lote { material = l.material, skin = l.skin, tipo = l.tipo, textura = l.textura };
                            lotes[chave] = l = novo;
                        }
                        Array.Fill(mapa, -1);
                        foreach (var v in tris)
                        {
                            if (v >= nv) { l.tris.Add(0); continue; }
                            if (mapa[v] < 0)
                            {
                                mapa[v] = l.pos.Count / 3;
                                l.pos.Add(o.pos[v * 3] + d.dx); l.pos.Add(o.pos[v * 3 + 1] + d.dy); l.pos.Add(o.pos[v * 3 + 2] + d.dz);
                                l.normal.Add(o.normal[v * 3]); l.normal.Add(o.normal[v * 3 + 1]); l.normal.Add(o.normal[v * 3 + 2]);
                                if (o.uv != null && v * 2 + 1 < o.uv.Length) { l.uv.Add(o.uv[v * 2]); l.uv.Add(o.uv[v * 2 + 1]); }
                                else { l.uv.Add(0); l.uv.Add(0); }
                            }
                            l.tris.Add(mapa[v]);
                        }
                    }
                }
            }
            var r = new List<Lote>();
            foreach (var l in cheios) if (l.tris.Count > 0) r.Add(l);
            foreach (var l in lotes.Values) if (l.tris.Count > 0) r.Add(l);
            return r;
        }
    }

    internal sealed class Lote
    {
        internal Material3DS material;
        internal string textura;
        internal int skin;
        internal OnibusMod.Tipo tipo;
        internal readonly List<float> pos = new(), normal = new(), uv = new();
        internal readonly List<int> tris = new();
    }
}
