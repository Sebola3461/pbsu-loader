using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace PBSLoaderCommonInterface
{
    // Sistema de design da UI nova, no estilo dos menus de jogos de rali/simulação
    // (DiRT Rally 2.0, Assetto Corsa EVO), que seguem a linguagem gráfica das transmissões de
    // automobilismo: superfícies chapadas e retas sobre a foto, um único acento vermelho,
    // seleção por inversão (fundo branco, texto escuro), títulos em DIN condensada em negrito
    // itálico e números grandes e retos para os dados.
    // Nada vem de asset bundle: fontes do Windows, sprites e texturas desenhados na hora.
    internal static class Theme
    {
        internal static MelonPreferences_Entry<string> PrefFonteDisplay, PrefFonteCorpo;

        // ---------- cores ----------
        // O jogo renderiza em espaço de cor LINEAR: a transparência é misturada antes da correção
        // de gama, então 4% de branco sobre preto aparece como um cinza de ~23%, e o mesmo token
        // translúcido dá cinzas diferentes conforme o que está atrás. Por isso superfícies, linhas
        // e textos são cores opacas; transparência só em véus de hover (Veu) e no escurecimento.
        internal static readonly Color Pagina = Hex("#101116FF");
        internal static readonly Color PaginaFundo = Hex("#0B0C10FF");
        internal static readonly Color Painel = Hex("#16171CFF");          // colunas, barras, navegação
        internal static readonly Color PainelSobreJogo = Painel;           // mesmo tom sobre a cena 3D
        internal static readonly Color Cartao = Hex("#1F2127FF");          // bloco dentro de um painel
        internal static readonly Color Elevado = Hex("#1B1C22FF");         // modais
        internal static readonly Color Menu = Hex("#1F2127FF");            // listas suspensas
        internal static readonly Color Barra = Hex("#08090CFF");           // faixa do rodapé
        internal static readonly Color Campo = Hex("#0E0F13FF");
        internal static readonly Color Linha = Hex("#2B2D34FF");
        internal static readonly Color LinhaForte = Hex("#464951FF");
        internal static readonly Color Texto = Color.white;
        internal static readonly Color TextoDim = Hex("#B9BBC1FF");       // 9,3:1 no painel
        internal static readonly Color TextoMudo = Hex("#8B8E96FF");      // 5,5:1 no painel; 4,9:1 no cartão
        internal static readonly Color TextoInverso = Hex("#111216FF");   // texto sobre o branco da seleção
        internal static readonly Color TextoInversoMudo = Hex("#5C5E66FF");
        // Vermelho de corrida. Acento: preenchimentos com texto branco (contraste 5,4:1).
        // AcentoVivo: marcas e textos vermelhos sobre o fundo escuro (4,8:1 no painel).
        internal static readonly Color Acento = Hex("#D6002FFF");
        internal static readonly Color AcentoHover = Hex("#E3083AFF");
        internal static readonly Color AcentoPress = Hex("#A80026FF");
        internal static readonly Color AcentoVivo = Hex("#FF4565FF");
        internal static readonly Color Dado = Hex("#FFB81CFF");            // âmbar dos valores e do visor de letreiro
        internal static readonly Color Perigo = Hex("#FF4D4DFF");
        internal static readonly Color PerigoTexto = Hex("#FF8080FF");
        internal static readonly Color PerigoFundo = Hex("#C62828FF");     // hover do botão destrutivo (5,6:1 com branco)
        internal static readonly Color Aviso = Hex("#FFB81CFF");
        internal static readonly Color Overlay = new Color(0f, 0f, 0f, 0.9f);
        // Impacto no desempenho (legenda do jogo: verde leve, amarelo médio, vermelho pesado).
        internal static readonly Color ImpactoLeve = Hex("#3DDC84FF");
        internal static readonly Color ImpactoMedio = Hex("#FFB81CFF");
        internal static readonly Color ImpactoPesado = Hex("#FF4565FF");

        // Véu branco para hover/pressionado: alfa que, misturado em espaço linear sobre um fundo
        // escuro (~#17), clareia a cor percebida em "p" (0..1), como um alfa comum faria em sRGB.
        internal static Color Veu(float p)
        {
            const float b = 0.09f;
            float bl = Mathf.Pow(b, 2.2f);
            float a = (Mathf.Pow(Mathf.Min(1f, b + p), 2.2f) - bl) / (1f - bl);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }

        // Cor opaca entre "base" e "cor" (mistura feita em sRGB, como o designer espera).
        internal static Color Tingir(Color baseOpaca, Color cor, float p) { var c = Color.Lerp(baseOpaca, cor, p); c.a = 1f; return c; }
        internal static Color Clarear(Color baseOpaca, float p) => Tingir(baseOpaca, Color.white, p);

        // Nomes usados pelas telas
        internal static Color Fundo => Pagina;
        internal static Color Controle => Campo;
        internal static Color ControleHover => Clarear(Campo, 0.1f);
        internal static Color ControlePress => LinhaForte;
        internal static Color Texto2 => TextoDim;
        internal static Color Texto3 => TextoMudo;
        internal static Color Destaque => AcentoVivo;

        // ---------- tipografia ----------
        // Display: títulos, botões, rótulos e números (DIN condensada; no Windows, a Bahnschrift).
        // TextoCorrido: parágrafos e textos longos.
        internal static Font Display, TextoCorrido, FonteIcones;
        internal static char Espaco = '\u200A'; // espaço fino usado para espaçar letras dos rótulos

        // Escala tipográfica (px de canvas 1920x1080)
        internal const int TamRotulo = 11, TamMeta = 13, TamCorpo = 14, TamValor = 16, TamSubtitulo = 18, TamTitulo = 26, TamDisplay = 34;

        // ---------- forma ----------
        // Tudo reto: o retângulo sólido e o contorno de 1 px servem a painéis, botões e campos.
        internal static Sprite Solido, Borda;
        internal static Texture2D GradienteLateral, GradienteBase, Vinheta, Listras;

        static bool _carregado;

        internal static void CriarPreferencias(MelonPreferences_Category cat)
        {
            PrefFonteDisplay = cat.CreateEntry("FonteDisplay", "Bahnschrift SemiBold Condensed;Bahnschrift Condensed;Bahnschrift;Segoe UI",
                "Títulos, botões, rótulos e números (fontes instaladas para todos os usuários)");
            PrefFonteCorpo = cat.CreateEntry("FonteCorpo", "Bahnschrift;Segoe UI", "Parágrafos e textos longos");
        }

        internal static void Carregar()
        {
            if (_carregado) return;
            _carregado = true;

            Display = FonteOuNull(PrefFonteDisplay.Value.Split(';'));
            TextoCorrido = FonteOuNull(PrefFonteCorpo.Value.Split(';')) ?? Display;
            FonteIcones = FonteOuNull(new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" });
            // Espaçamento de letras: o uGUI não tem; usamos o espaço mais fino que a fonte tiver.
            if (Display != null)
                Espaco = Display.HasCharacter('\u200A') ? '\u200A' : Display.HasCharacter('\u2009') ? '\u2009' : '\0';

            Solido = Retangulo("PBSLoaderCommonInterface_Solido");
            Borda = Contorno("PBSLoaderCommonInterface_Borda");

            GradienteLateral = Gradiente("PBSLoaderCommonInterface_GradL", 256, 1, x => Mathf.Lerp(0.92f, 0.15f, Mathf.SmoothStep(0f, 1f, x)));
            GradienteBase = Gradiente("PBSLoaderCommonInterface_GradB", 1, 256, y => Mathf.Lerp(0.85f, 0f, Mathf.SmoothStep(0f, 1f, y)));
            Vinheta = TexturaVinheta("PBSLoaderCommonInterface_Vinheta", 256);
            Listras = TexturaListras("PBSLoaderCommonInterface_Listras");
        }

        // Rótulo: maiúsculas com espaço fino entre as letras (o "contextual subtitle" do DiRT).
        internal static string Rotular(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            s = s.ToUpperInvariant();
            if (Espaco == '\0') return s;
            var sb = new System.Text.StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if (i < s.Length - 1 && s[i] != ' ' && s[i + 1] != ' ') sb.Append(Espaco);
            }
            return sb.ToString();
        }

        // ---------- fontes ----------

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void CreateDynamicFontFn(System.IntPtr self, System.IntPtr nomes, int tamanho);

        // Font.CreateDynamicFontFromOSFont depende do construtor privado Font(string[], int), que o
        // IL2CPP removeu deste build. A parte nativa continua no UnityPlayer.dll: alocamos o objeto
        // sem construir e chamamos a icall Internal_CreateDynamicFont, como o construtor faria.
        static Font FonteOuNull(string[] nomes)
        {
            try
            {
                var fn = IL2CPP.ResolveICall<CreateDynamicFontFn>("UnityEngine.Font::Internal_CreateDynamicFont");
                var obj = IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<Font>.NativeClassPtr);
                fn(obj, new Il2CppStringArray(nomes).Pointer, 32);
                return new Font(obj) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"Fonte '{string.Join(";", nomes)}' indisponível: {e.Message}");
                return null;
            }
        }

        // ---------- sprites ----------

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate System.IntPtr CreateSpriteFn(System.IntPtr textura, ref Rect rect, ref Vector2 pivo, float ppu,
            uint extrude, int meshType, ref Vector4 borda, [MarshalAs(UnmanagedType.U1)] bool fisica);

        static CreateSpriteFn _createSprite;

        // Todas as sobrecargas de Sprite.Create foram removidas do build; a icall nativa continua lá.
        static Sprite CriarSprite(Texture2D tex, Rect rect, Vector2 pivo, float ppu, Vector4 borda)
        {
            _createSprite ??= IL2CPP.ResolveICall<CreateSpriteFn>("UnityEngine.Sprite::CreateSprite_Injected");
            var ptr = _createSprite(tex.Pointer, ref rect, ref pivo, ppu, 0, (int)SpriteMeshType.FullRect, ref borda, false);
            if (ptr == System.IntPtr.Zero) throw new System.InvalidOperationException("CreateSprite_Injected retornou null");
            var s = new Sprite(ptr);
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return s;
        }

        static Sprite Nomear(Sprite s, string nome) { s.name = nome; return s; }

        static Sprite Retangulo(string nome)
        {
            var tex = NovaTextura(nome, 4, 4, FilterMode.Point);
            var px = new Il2CppStructArray<Color32>(16);
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply(false, true);
            return Nomear(CriarSprite(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, new Vector4(1, 1, 1, 1)), nome);
        }

        static Sprite Contorno(string nome)
        {
            const int t = 8;
            var tex = NovaTextura(nome, t, t, FilterMode.Point);
            var px = new Il2CppStructArray<Color32>(t * t);
            for (int y = 0; y < t; y++)
                for (int x = 0; x < t; x++)
                    px[y * t + x] = new Color32(255, 255, 255, (byte)(x == 0 || y == 0 || x == t - 1 || y == t - 1 ? 255 : 0));
            tex.SetPixels32(px); tex.Apply(false, true);
            return Nomear(CriarSprite(tex, new Rect(0, 0, t, t), new Vector2(0.5f, 0.5f), 100f, new Vector4(2, 2, 2, 2)), nome);
        }

        // ---------- texturas ----------

        static Texture2D Gradiente(string nome, int w, int h, System.Func<float, float> alfa)
        {
            var tex = NovaTextura(nome, w, h);
            var px = new Il2CppStructArray<Color32>(w * h);
            Color32 b = PaginaFundo;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = w > 1 ? x / (float)(w - 1) : y / (float)(h - 1);
                    px[y * w + x] = new Color32(b.r, b.g, b.b, (byte)(alfa(t) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }

        // Vinheta radial: transparente no centro, escurece nas bordas.
        static Texture2D TexturaVinheta(string nome, int t)
        {
            var tex = NovaTextura(nome, t, t);
            var px = new Il2CppStructArray<Color32>(t * t);
            for (int y = 0; y < t; y++)
                for (int x = 0; x < t; x++)
                {
                    float dx = (x + 0.5f) / t * 2f - 1f, dy = (y + 0.5f) / t * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, d)) * 0.9f;
                    px[y * t + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }

        // Riscas diagonais finas (as "speed lines" de fundo do Assetto Corsa EVO): uma linha
        // de 1 px a 45° num ladrilho de 24 px, repetido. A intensidade vem da cor do RawImage.
        static Texture2D TexturaListras(string nome)
        {
            const int t = 24;
            var tex = NovaTextura(nome, t, t);
            tex.wrapMode = TextureWrapMode.Repeat;
            var px = new Il2CppStructArray<Color32>(t * t);
            for (int y = 0; y < t; y++)
                for (int x = 0; x < t; x++)
                {
                    int d = (x + y) % t;
                    float a = d == 0 ? 1f : d == 1 || d == t - 1 ? 0.25f : 0f;
                    px[y * t + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }

        static Texture2D NovaTextura(string nome, int w, int h, FilterMode filtro = FilterMode.Bilinear) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = nome,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filtro,
            hideFlags = HideFlags.DontUnloadUnusedAsset
        };

        // ---------- util ----------

        internal static Color Alfa(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static Color Hex(string s)
        {
            s = s.Trim().TrimStart('#');
            byte P(int i) => System.Convert.ToByte(s.Substring(i, 2), 16);
            return new Color32(P(0), P(2), P(4), s.Length == 8 ? P(6) : (byte)255);
        }
    }

    // Ícones da fonte Segoe Fluent Icons / Segoe MDL2 Assets (mesmos códigos nas duas).
    internal static class Ico
    {
        internal const string Play = "", Voltar = "", Avancar = "", SetaEsq = "", SetaDir = "",
            Config = "", Busca = "", Pasta = "", Download = "", Atualizar = "", Ajuda = "",
            Info = "", Pessoas = "", Contato = "", Fechar = "", Excluir = "", Adicionar = "",
            Globo = "", Desligar = "", Onibus = "", Pintura = "", Alerta = "", Link = "",
            Som = "", Teclado = "", Biblioteca = "", Salvar = "", Abrir = "", Coracao = "",
            Ferramenta = "", Video = "", Reset = "", Estrela = "",
            Casa = "", Brilho = "", Console = "", Camera = "", Lampada = "", Nuvem = "",
            Janela = "", Girar = "", Cadeado = "", Abaixo = "", Carro = "", Foto360 = "",
            Remover = "", Marca = "", Gpu = "", Relogio = "", Folha = "", Letreiro = "", Banco = "";
    }
}
