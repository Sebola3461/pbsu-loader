using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PBSLoaderCommonInterface.Garagem
{
    internal sealed class Material3DS
    {
        internal string nome, textura;
        internal float r = 0.8f, g = 0.8f, b = 0.8f, transparencia; // transparência 0..1
    }

    internal sealed class Objeto3DS
    {
        internal string nome;
        internal float[] pos;      // xyz já no espaço do Unity
        internal float[] uv;       // pode ser null
        internal float[] normal;
        internal List<(string material, int[] tris)> partes = new();
    }

    internal sealed class Modelo3DS
    {
        internal readonly List<Objeto3DS> objetos = new();
        internal readonly Dictionary<string, Material3DS> materiais = new(StringComparer.OrdinalIgnoreCase);
        internal string pasta;

        // Modelos .c1 (criptografados pelo autor do ônibus) são abertos pelo próprio jogo, em memória, na
        // thread principal (ver Previa3D.DecifrarC1). O mod não tem chave nem decifrador próprio e nunca
        // grava o modelo aberto em disco.
        internal static Func<byte[], byte[]> DecifradorC1;

        // Lê um .3ds (ou .3ds.protonbus.c1). Mesmo sistema de eixos do leitor do jogo: y e z trocados
        // (o 3DS é z para cima) e os triângulos invertidos (c, b, a) para manter a face para fora.
        internal static Modelo3DS Ler(string caminho)
        {
            var bytes = File.ReadAllBytes(caminho);
            if (caminho.EndsWith(".c1", StringComparison.OrdinalIgnoreCase)) bytes = DecifradorC1?.Invoke(bytes);
            else if (caminho.EndsWith(".c2", StringComparison.OrdinalIgnoreCase)) return null; // formato ainda não suportado
            var m = new Modelo3DS { pasta = Path.GetDirectoryName(caminho) };
            if (bytes == null || bytes.Length < 6 || BitConverter.ToUInt16(bytes, 0) != 0x4D4D) return null;
            m.Blocos(bytes, 6, (int)Math.Min(bytes.Length, BitConverter.ToUInt32(bytes, 2)), null);
            foreach (var o in m.objetos) Normais(o);
            return m;
        }

        static string Cadeia(byte[] b, ref int p, int fim)
        {
            int ini = p;
            while (p < fim && b[p] != 0) p++;
            var s = Encoding.Latin1.GetString(b, ini, p - ini);
            p++;
            return s;
        }

        // Percorre os blocos [id u16][tamanho u32] entre p e fim.
        void Blocos(byte[] b, int p, int fim, object ctx)
        {
            while (p + 6 <= fim)
            {
                int id = BitConverter.ToUInt16(b, p);
                int len = (int)BitConverter.ToUInt32(b, p + 2);
                if (len < 6 || p + len > fim) break;
                int d = p + 6, f = p + len;
                switch (id)
                {
                    case 0x3D3D: Blocos(b, d, f, null); break;                       // editor
                    case 0x4000:                                                       // objeto
                        {
                            var o = new Objeto3DS();
                            int q = d;
                            o.nome = Cadeia(b, ref q, f);
                            Blocos(b, q, f, o);
                            if (o.pos != null && o.partes.Count > 0) objetos.Add(o);
                            break;
                        }
                    case 0x4100: Blocos(b, d, f, ctx); break;                          // malha
                    case 0x4110 when ctx is Objeto3DS o:                               // vértices
                        {
                            int n = BitConverter.ToUInt16(b, d);
                            var v = new float[n * 3];
                            for (int i = 0, q = d + 2; i < n && q + 12 <= f; i++, q += 12)
                            {
                                v[i * 3] = BitConverter.ToSingle(b, q);
                                v[i * 3 + 1] = BitConverter.ToSingle(b, q + 8);
                                v[i * 3 + 2] = BitConverter.ToSingle(b, q + 4);
                            }
                            o.pos = v;
                            break;
                        }
                    case 0x4140 when ctx is Objeto3DS o:                               // UV
                        {
                            int n = BitConverter.ToUInt16(b, d);
                            var v = new float[n * 2];
                            for (int i = 0, q = d + 2; i < n && q + 8 <= f; i++, q += 8)
                            {
                                v[i * 2] = BitConverter.ToSingle(b, q);
                                v[i * 2 + 1] = BitConverter.ToSingle(b, q + 4);
                            }
                            o.uv = v;
                            break;
                        }
                    case 0x4120 when ctx is Objeto3DS o:                               // faces
                        {
                            int n = BitConverter.ToUInt16(b, d);
                            var faces = new int[n * 3];
                            int q = d + 2;
                            for (int i = 0; i < n && q + 8 <= f; i++, q += 8)
                            {
                                faces[i * 3] = BitConverter.ToUInt16(b, q + 4);
                                faces[i * 3 + 1] = BitConverter.ToUInt16(b, q + 2);
                                faces[i * 3 + 2] = BitConverter.ToUInt16(b, q);
                            }
                            // Grupos de material (0x4130) vêm como sub-blocos depois da lista de faces.
                            var usadas = new bool[n];
                            int s = q;
                            while (s + 6 <= f)
                            {
                                int sid = BitConverter.ToUInt16(b, s);
                                int slen = (int)BitConverter.ToUInt32(b, s + 2);
                                if (slen < 6 || s + slen > f) break;
                                if (sid == 0x4130)
                                {
                                    int r = s + 6;
                                    var mat = Cadeia(b, ref r, s + slen);
                                    int k = BitConverter.ToUInt16(b, r); r += 2;
                                    var tris = new List<int>(k * 3);
                                    for (int i = 0; i < k && r + 2 <= s + slen; i++, r += 2)
                                    {
                                        int fi = BitConverter.ToUInt16(b, r);
                                        if (fi >= n || usadas[fi]) continue;
                                        usadas[fi] = true;
                                        tris.Add(faces[fi * 3]); tris.Add(faces[fi * 3 + 1]); tris.Add(faces[fi * 3 + 2]);
                                    }
                                    if (tris.Count > 0) o.partes.Add((mat, tris.ToArray()));
                                }
                                s += slen;
                            }
                            var resto = new List<int>();
                            for (int i = 0; i < n; i++)
                                if (!usadas[i]) { resto.Add(faces[i * 3]); resto.Add(faces[i * 3 + 1]); resto.Add(faces[i * 3 + 2]); }
                            if (resto.Count > 0) o.partes.Add((null, resto.ToArray()));
                            break;
                        }
                    case 0xAFFF:                                                       // material
                        {
                            var mat = new Material3DS();
                            Blocos(b, d, f, mat);
                            if (mat.nome != null) materiais[mat.nome] = mat;
                            break;
                        }
                    case 0xA000 when ctx is Material3DS mat: { int q = d; mat.nome = Cadeia(b, ref q, f); break; }
                    case 0xA020 when ctx is Material3DS mat: Cor(b, d, f, mat); break;
                    case 0xA050 when ctx is Material3DS mat:
                        if (BitConverter.ToUInt16(b, d) == 0x0030) mat.transparencia = BitConverter.ToInt16(b, d + 6) / 100f;
                        else if (BitConverter.ToUInt16(b, d) == 0x0031) mat.transparencia = BitConverter.ToSingle(b, d + 6);
                        break;
                    case 0xA200 when ctx is Material3DS mat: Blocos(b, d, f, mat); break;   // mapa de textura
                    case 0xA300 when ctx is Material3DS mat:
                        { int q = d; var t = Cadeia(b, ref q, f); if (mat.textura == null && t.Length > 0) mat.textura = t; break; }
                }
                p = f;
            }
        }

        static void Cor(byte[] b, int p, int fim, Material3DS m)
        {
            if (p + 6 > fim) return;
            int id = BitConverter.ToUInt16(b, p);
            if (id == 0x0011 && p + 9 <= fim) { m.r = b[p + 6] / 255f; m.g = b[p + 7] / 255f; m.b = b[p + 8] / 255f; }
            else if (id == 0x0010 && p + 18 <= fim) { m.r = BitConverter.ToSingle(b, p + 6); m.g = BitConverter.ToSingle(b, p + 10); m.b = BitConverter.ToSingle(b, p + 14); }
        }

        // Normais por vértice, médias das faces ponderadas pela área (o mesmo que RecalculateNormals).
        static void Normais(Objeto3DS o)
        {
            var v = o.pos;
            var n = new float[v.Length];
            foreach (var (_, t) in o.partes)
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = t[i] * 3, bb = t[i + 1] * 3, c = t[i + 2] * 3;
                    if (a + 2 >= v.Length || bb + 2 >= v.Length || c + 2 >= v.Length) continue;
                    float ux = v[bb] - v[a], uy = v[bb + 1] - v[a + 1], uz = v[bb + 2] - v[a + 2];
                    float wx = v[c] - v[a], wy = v[c + 1] - v[a + 1], wz = v[c + 2] - v[a + 2];
                    float nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
                    n[a] += nx; n[a + 1] += ny; n[a + 2] += nz;
                    n[bb] += nx; n[bb + 1] += ny; n[bb + 2] += nz;
                    n[c] += nx; n[c + 1] += ny; n[c + 2] += nz;
                }
            for (int i = 0; i + 2 < n.Length; i += 3)
            {
                float l = MathF.Sqrt(n[i] * n[i] + n[i + 1] * n[i + 1] + n[i + 2] * n[i + 2]);
                if (l > 1e-12f) { n[i] /= l; n[i + 1] /= l; n[i + 2] /= l; } else n[i + 1] = 1f;
            }
            o.normal = n;
        }
    }
}
