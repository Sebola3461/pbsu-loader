using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PBSLoaderCommonInterface.Garagem
{
    // Imagem já pronta para a GPU: RGBA32 com a cadeia de mipmaps inteira, linhas de baixo para cima
    // (a ordem do Texture2D). Montada fora da thread principal.
    internal sealed class ImagemRgba
    {
        internal int w, h, mips;
        internal bool alfa;      // algum pixel com transparência de verdade
        internal byte[] dados;
        internal byte[] jpeg;    // JPEG (mesmo com extensão .png): o Unity decodifica na thread principal
    }

    // Decodificação de PNG, BMP e TGA em C# puro (sem IL2CPP: roda em qualquer thread), já reduzida
    // para no máximo "maxLado" pixels por média de blocos, com cache em disco. As skins dos mods são
    // PNGs de 4096x4096 com 16 bits por canal: o LoadImage do jogo leva centenas de ms com elas na
    // thread principal. Formatos que não estão aqui (JPG, PNG entrelaçado) devolvem null.
    internal static class Imagem
    {
        internal static string PastaCache;
        const uint Magico = 0x544D4955; // "UIMT"
        const int Versao = 1;

        internal static ImagemRgba Carregar(string caminho, int maxLado)
        {
            FileInfo fi;
            try { fi = new FileInfo(caminho); if (!fi.Exists) return null; } catch { return null; }
            string cache = null;
            // Só os tamanhos da prévia vão para o disco (as skins de 2048 do cinema ocupariam ~20 MB cada).
            if (PastaCache != null && maxLado <= 1024)
            {
                var chave = $"{fi.FullName.ToLowerInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{maxLado}|{Versao}";
                cache = Path.Combine(PastaCache, Hash(chave) + ".tex");
                var c = LerCache(cache);
                if (c != null) return c;
            }
            byte[] bytes;
            try { bytes = File.ReadAllBytes(caminho); } catch { return null; }
            ImagemRgba img = null;
            try
            {
                if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G') img = Png(bytes, maxLado);
                else if (bytes.Length > 54 && bytes[0] == 'B' && bytes[1] == 'M') img = Bmp(bytes, maxLado);
                else if (caminho.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) img = Tga(bytes, maxLado);
                else if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return new ImagemRgba { jpeg = bytes };
            }
            catch { img = null; }
            if (img != null && cache != null) GravarCache(cache, img);
            return img;
        }

        static string Hash(string s)
        {
            using var sha = SHA1.Create();
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(40);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        static ImagemRgba LerCache(string arq)
        {
            try
            {
                if (!File.Exists(arq)) return null;
                using var br = new BinaryReader(File.OpenRead(arq));
                if (br.ReadUInt32() != Magico || br.ReadInt32() != Versao) return null;
                var img = new ImagemRgba { w = br.ReadInt32(), h = br.ReadInt32(), mips = br.ReadInt32(), alfa = br.ReadBoolean() };
                int n = br.ReadInt32();
                img.dados = br.ReadBytes(n);
                return img.dados.Length == n ? img : null;
            }
            catch { return null; }
        }

        static void GravarCache(string arq, ImagemRgba img)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(arq));
                var tmp = arq + "." + Environment.CurrentManagedThreadId + ".tmp";
                using (var bw = new BinaryWriter(File.Create(tmp)))
                {
                    bw.Write(Magico); bw.Write(Versao);
                    bw.Write(img.w); bw.Write(img.h); bw.Write(img.mips); bw.Write(img.alfa);
                    bw.Write(img.dados.Length); bw.Write(img.dados);
                }
                File.Move(tmp, arq, true);
            }
            catch { }
        }

        // ---------- redução: soma os pixels de cada bloco f x f e tira a média ----------

        sealed class Redutor
        {
            readonly int _w, _h, _f;
            internal readonly int tw, th;
            readonly uint[] _soma;
            internal bool alfa;

            internal Redutor(int w, int h, int maxLado)
            {
                _w = w; _h = h;
                _f = 1;
                while (Math.Max(w, h) / _f > maxLado) _f *= 2;
                tw = Math.Max(1, w / _f); th = Math.Max(1, h / _f);
                _soma = new uint[tw * th * 4];
            }

            // Uma linha da imagem (y de cima para baixo), já em RGBA8.
            internal void Linha(int y, byte[] rgba)
            {
                int ty = y / _f;
                if (ty >= th) return;
                int b = ty * tw * 4;
                for (int x = 0, tx = 0, c = 0; x < _w; x++)
                {
                    tx = x / _f;
                    if (tx >= tw) break;
                    int o = b + tx * 4, i = x * 4;
                    _soma[o] += rgba[i]; _soma[o + 1] += rgba[i + 1]; _soma[o + 2] += rgba[i + 2]; _soma[o + 3] += rgba[i + 3];
                    if (rgba[i + 3] < 250) c = 1;
                    if (c != 0) alfa = true;
                }
            }

            // Caminho rápido (RGB/RGBA de 8 ou 16 bits): soma direto dos bytes da linha do PNG, sem
            // converter para RGBA antes. "k" = bytes por canal (o byte alto vem primeiro).
            internal void LinhaDireta(int y, byte[] s, int canais, int k)
            {
                int ty = y / _f;
                if (ty >= th) return;
                int b = ty * tw * 4, passo = canais * k;
                for (int tx = 0; tx < tw; tx++)
                {
                    uint r = 0, g = 0, bl = 0, a = 0;
                    int j = tx * _f * passo;
                    for (int i = 0; i < _f; i++, j += passo)
                    {
                        r += s[j]; g += s[j + k]; bl += s[j + 2 * k];
                        a += canais == 4 ? s[j + 3 * k] : 255u;
                    }
                    int o = b + tx * 4;
                    _soma[o] += r; _soma[o + 1] += g; _soma[o + 2] += bl; _soma[o + 3] += a;
                    if (canais == 4 && a < 250u * (uint)_f) alfa = true;
                }
            }

            internal ImagemRgba Fim()
            {
                uint n = (uint)(_f * _f);
                // mip 0 de baixo para cima (Texture2D) + cadeia de mips por média 2x2
                int mips = 1, tot = tw * th * 4;
                for (int w = tw, h = th; w > 1 || h > 1; mips++) { w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); tot += w * h * 4; }
                var d = new byte[tot];
                for (int y = 0; y < th; y++)
                {
                    int src = (th - 1 - y) * tw * 4, dst = y * tw * 4;
                    for (int i = 0; i < tw * 4; i++) d[dst + i] = (byte)(_soma[src + i] / n);
                }
                int pa = 0, wa = tw, ha = th, pb = tw * th * 4;
                for (int m = 1; m < mips; m++)
                {
                    int wb = Math.Max(1, wa / 2), hb = Math.Max(1, ha / 2);
                    for (int y = 0; y < hb; y++)
                        for (int x = 0; x < wb; x++)
                        {
                            int x0 = Math.Min(x * 2, wa - 1), x1 = Math.Min(x * 2 + 1, wa - 1);
                            int y0 = Math.Min(y * 2, ha - 1), y1 = Math.Min(y * 2 + 1, ha - 1);
                            for (int c = 0; c < 4; c++)
                                d[pb + (y * wb + x) * 4 + c] = (byte)((d[pa + (y0 * wa + x0) * 4 + c] + d[pa + (y0 * wa + x1) * 4 + c]
                                    + d[pa + (y1 * wa + x0) * 4 + c] + d[pa + (y1 * wa + x1) * 4 + c] + 2) / 4);
                        }
                    pa = pb; pb += wb * hb * 4; wa = wb; ha = hb;
                }
                return new ImagemRgba { w = tw, h = th, mips = mips, alfa = alfa, dados = d };
            }
        }

        // ---------- PNG ----------

        static uint BE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        static ImagemRgba Png(byte[] b, int maxLado)
        {
            int p = 8, w = 0, h = 0, prof = 0, tipo = 0;
            byte[] paleta = null, trns = null;
            var idat = new MemoryStream();
            while (p + 8 <= b.Length)
            {
                int len = (int)BE(b, p);
                string id = Encoding.ASCII.GetString(b, p + 4, 4);
                int d = p + 8;
                if (d + len > b.Length) break;
                switch (id)
                {
                    case "IHDR":
                        w = (int)BE(b, d); h = (int)BE(b, d + 4); prof = b[d + 8]; tipo = b[d + 9];
                        if (b[d + 12] != 0) return null; // entrelaçado: fica com o decodificador do jogo
                        break;
                    case "PLTE": paleta = new byte[len]; Buffer.BlockCopy(b, d, paleta, 0, len); break;
                    case "tRNS": trns = new byte[len]; Buffer.BlockCopy(b, d, trns, 0, len); break;
                    case "IDAT": idat.Write(b, d, len); break;
                }
                if (id == "IEND") break;
                p = d + len + 4;
            }
            if (w <= 0 || h <= 0) return null;
            int canais = tipo switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (canais == 0) return null;
            int bitsPx = canais * prof;
            int bpp = Math.Max(1, bitsPx / 8);
            int stride = (w * bitsPx + 7) / 8;

            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            var ant = new byte[stride];
            var cur = new byte[stride];
            var rgba = new byte[w * 4];
            var red = new Redutor(w, h, maxLado);
            for (int y = 0; y < h; y++)
            {
                int filtro = z.ReadByte();
                if (filtro < 0) break;
                LerTudo(z, cur, stride);
                Desfiltrar(filtro, cur, ant, bpp, stride);
                if ((tipo == 2 && trns == null || tipo == 6) && prof >= 8) red.LinhaDireta(y, cur, canais, prof / 8);
                else
                {
                    ParaRgba(cur, rgba, w, tipo, prof, paleta, trns);
                    red.Linha(y, rgba);
                }
                (ant, cur) = (cur, ant);
            }
            return red.Fim();
        }

        static void LerTudo(Stream s, byte[] buf, int n)
        {
            int lido = 0;
            while (lido < n)
            {
                int k = s.Read(buf, lido, n - lido);
                if (k <= 0) { Array.Clear(buf, lido, n - lido); return; }
                lido += k;
            }
        }

        static void Desfiltrar(int f, byte[] c, byte[] a, int bpp, int n)
        {
            switch (f)
            {
                case 1: for (int i = bpp; i < n; i++) c[i] += c[i - bpp]; break;
                case 2: for (int i = 0; i < n; i++) c[i] += a[i]; break;
                case 3:
                    for (int i = 0; i < n; i++) c[i] += (byte)(((i >= bpp ? c[i - bpp] : 0) + a[i]) >> 1);
                    break;
                case 4:
                    for (int i = 0; i < n; i++)
                    {
                        int l = i >= bpp ? c[i - bpp] : 0, u = a[i], ul = i >= bpp ? a[i - bpp] : 0;
                        int pp = l + u - ul, pa = Math.Abs(pp - l), pb = Math.Abs(pp - u), pc = Math.Abs(pp - ul);
                        c[i] += (byte)(pa <= pb && pa <= pc ? l : pb <= pc ? u : ul);
                    }
                    break;
            }
        }

        static void ParaRgba(byte[] s, byte[] o, int w, int tipo, int prof, byte[] pal, byte[] trns)
        {
            if (prof == 8 || prof == 16)
            {
                int k = prof / 8; // 16 bits: fica o byte alto
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    switch (tipo)
                    {
                        case 6: { int j = x * 4 * k; o[i] = s[j]; o[i + 1] = s[j + k]; o[i + 2] = s[j + 2 * k]; o[i + 3] = s[j + 3 * k]; break; }
                        case 2:
                            {
                                int j = x * 3 * k; o[i] = s[j]; o[i + 1] = s[j + k]; o[i + 2] = s[j + 2 * k]; o[i + 3] = 255;
                                if (trns != null && trns.Length >= 6 && k == 1 && s[j] == trns[1] && s[j + 1] == trns[3] && s[j + 2] == trns[5]) o[i + 3] = 0;
                                break;
                            }
                        case 4: { int j = x * 2 * k; o[i] = o[i + 1] = o[i + 2] = s[j]; o[i + 3] = s[j + k]; break; }
                        case 0: { int j = x * k; o[i] = o[i + 1] = o[i + 2] = s[j]; o[i + 3] = 255; break; }
                        case 3: Indice(s[x], o, i, pal, trns); break;
                    }
                }
                return;
            }
            // 1, 2 ou 4 bits (paleta ou cinza)
            int porByte = 8 / prof, mask = (1 << prof) - 1;
            for (int x = 0; x < w; x++)
            {
                int v = (s[x / porByte] >> ((porByte - 1 - x % porByte) * prof)) & mask;
                int i = x * 4;
                if (tipo == 3) Indice(v, o, i, pal, trns);
                else { byte g = (byte)(v * 255 / mask); o[i] = o[i + 1] = o[i + 2] = g; o[i + 3] = 255; }
            }
        }

        static void Indice(int v, byte[] o, int i, byte[] pal, byte[] trns)
        {
            if (pal != null && v * 3 + 2 < pal.Length) { o[i] = pal[v * 3]; o[i + 1] = pal[v * 3 + 1]; o[i + 2] = pal[v * 3 + 2]; }
            else o[i] = o[i + 1] = o[i + 2] = 0;
            o[i + 3] = trns != null && v < trns.Length ? trns[v] : (byte)255;
        }

        // ---------- BMP (8, 24 e 32 bits, sem compressão) ----------

        static ImagemRgba Bmp(byte[] b, int maxLado)
        {
            int off = BitConverter.ToInt32(b, 10), dib = BitConverter.ToInt32(b, 14);
            int w = BitConverter.ToInt32(b, 18), hh = BitConverter.ToInt32(b, 22);
            int bpp = BitConverter.ToInt16(b, 28), comp = BitConverter.ToInt32(b, 30);
            if (comp != 0 && comp != 3) return null;
            if (bpp != 8 && bpp != 24 && bpp != 32) return null;
            bool deCima = hh < 0; int h = Math.Abs(hh);
            int stride = (w * bpp / 8 + 3) & ~3;
            int pal = 14 + dib;
            var rgba = new byte[w * 4];
            var red = new Redutor(w, h, maxLado);
            for (int y = 0; y < h; y++)
            {
                int linhaArq = deCima ? y : h - 1 - y;
                int r = off + linhaArq * stride;
                if (r + w * bpp / 8 > b.Length) break;
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    if (bpp == 8) { int k = pal + b[r + x] * 4; rgba[i] = b[k + 2]; rgba[i + 1] = b[k + 1]; rgba[i + 2] = b[k]; rgba[i + 3] = 255; }
                    else
                    {
                        int k = r + x * (bpp / 8);
                        rgba[i] = b[k + 2]; rgba[i + 1] = b[k + 1]; rgba[i + 2] = b[k];
                        rgba[i + 3] = 255; // o alfa de BMP de 32 bits quase sempre é lixo
                    }
                }
                red.Linha(y, rgba);
            }
            return red.Fim();
        }

        // ---------- TGA (cor verdadeira ou cinza, com ou sem RLE) ----------

        static ImagemRgba Tga(byte[] b, int maxLado)
        {
            int idLen = b[0], tipo = b[2];
            int w = b[12] | b[13] << 8, h = b[14] | b[15] << 8, bpp = b[16];
            bool deCima = (b[17] & 0x20) != 0;
            if (tipo != 2 && tipo != 10 && tipo != 3 && tipo != 11) return null;
            int px = bpp / 8;
            if (px < 1 || px > 4) return null;
            var tudo = new byte[w * h * px];
            int p = 18 + idLen + (b[1] != 0 ? (b[5] | b[6] << 8) * ((b[7] + 7) / 8) : 0);
            if (tipo == 2 || tipo == 3) Buffer.BlockCopy(b, p, tudo, 0, Math.Min(tudo.Length, b.Length - p));
            else
            {
                int o = 0;
                while (o < tudo.Length && p < b.Length)
                {
                    int c = b[p++], n = (c & 0x7F) + 1;
                    if ((c & 0x80) != 0) { for (int i = 0; i < n && o < tudo.Length; i++, o += px) Buffer.BlockCopy(b, p, tudo, o, px); p += px; }
                    else { int k = Math.Min(n * px, tudo.Length - o); Buffer.BlockCopy(b, p, tudo, o, k); p += n * px; o += k; }
                }
            }
            var rgba = new byte[w * 4];
            var red = new Redutor(w, h, maxLado);
            for (int y = 0; y < h; y++)
            {
                int r = (deCima ? y : h - 1 - y) * w * px;
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4, k = r + x * px;
                    if (px == 1 || px == 2) { rgba[i] = rgba[i + 1] = rgba[i + 2] = tudo[k]; rgba[i + 3] = px == 2 ? tudo[k + 1] : (byte)255; }
                    else { rgba[i] = tudo[k + 2]; rgba[i + 1] = tudo[k + 1]; rgba[i + 2] = tudo[k]; rgba[i + 3] = px == 4 ? tudo[k + 3] : (byte)255; }
                }
                red.Linha(y, rgba);
            }
            return red.Fim();
        }
    }
}
