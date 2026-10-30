using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace PBSLoaderCommonInterface.Garagem
{
    // Fundo do menu principal: cinematográficas dos ônibus instalados num estúdio escuro (no clima das
    // telas de carregamento do BeamNG). Renderizado ao vivo pelo mesmo palco da prévia 3D, então não há
    // vídeo para guardar e aparecem os ônibus e as skins do jogador. Cada ônibus ganha dois planos de
    // câmera lentos; a troca de ônibus passa pelo preto, com o próximo já preparado durante o plano.
    internal static partial class Previa3D
    {
        internal static MelonPreferences_Entry<bool> PrefCinema;
        internal static void CriarPreferenciasCinema(MelonPreferences_Category cat) =>
            PrefCinema = cat.CreateEntry("CinemaMenu", true, "Fundo do menu com cinematográficas dos ônibus em 3D");

        const int CamadaPiso = 30; // o piso numa camada própria: a luz de teto só ilumina o chão

        static bool _modoCinema, _modoDefinido;
        static float _cinemaAte, _escuro = 1f;
        static GameObject _luzesVitrine, _estudio;
        static Transform _reflexo;
        static Montado _reflexoDe;
        static RenderTexture _rtCinema;

        internal static void PedirCinema() => _cinemaAte = Time.unscaledTime + 0.2f;
        internal static Texture SaidaCinema => _rtCinema;
        internal static float Escuro => _escuro;            // 0 = imagem, 1 = preto (corte entre ônibus)
        internal static bool CinemaAtivo => _cinemaComecou;  // já mostrou algum ônibus
        // Sem cinema possível (desligado, ou nenhum ônibus de mod legível): o menu usa a foto do jogo.
        internal static bool CinemaIndisponivel => (PrefCinema != null && !PrefCinema.Value) || (_fila != null && _fila.Count == 0);

        // ---------- estúdio ----------

        static void CriarEstudio()
        {
            _estudio = new GameObject("Estudio") { layer = Camada };
            _estudio.transform.SetParent(_palco.transform, false);

            // Piso de placas escuras, semitransparente sobre o reflexo do ônibus.
            var piso = GameObject.CreatePrimitive(PrimitiveType.Quad);
            piso.name = "Piso";
            piso.layer = CamadaPiso;
            UnityEngine.Object.Destroy(piso.GetComponent<Collider>());
            piso.transform.SetParent(_estudio.transform, false);
            piso.transform.localRotation = Quaternion.Euler(90, 0, 0);
            const float lado = 160f, placa = 1.8f;
            piso.transform.localScale = new Vector3(lado, lado, 1f);
            var mr = piso.GetComponent<MeshRenderer>();
            var mat = new Material(_transparente) { color = new Color(0.62f, 0.63f, 0.68f, 0.86f), mainTexture = TexturaPiso() };
            mat.mainTextureScale = new Vector2(lado / placa, lado / placa);
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // O ônibus espelhado sob o piso (o reflexo clássico de showroom).
            _reflexo = new GameObject("Reflexo") { layer = Camada }.transform;
            _reflexo.SetParent(_estudio.transform, false);
            _reflexo.localScale = new Vector3(1f, -1f, 1f);

            // Luz de teto: uma poça de luz no chão em volta do ônibus (só o piso a recebe).
            var teto = new GameObject("Teto") { layer = Camada };
            teto.transform.SetParent(_estudio.transform, false);
            teto.transform.localPosition = new Vector3(0, 18f, 0);
            teto.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var spot = teto.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 95f; spot.range = 48f; spot.intensity = 2.6f;
            spot.color = new Color(0.92f, 0.95f, 1f);
            spot.cullingMask = 1 << CamadaPiso;
            spot.shadows = LightShadows.None;
            spot.renderMode = LightRenderMode.ForcePixel;

            // No ônibus: chave de cima (como um softbox), recorte frio por trás e um preenchimento baixo.
            Luz(_estudio.transform, "Chave", new Vector3(58, -25, 0), new Color(1f, 0.97f, 0.93f), 1.0f, true);
            Luz(_estudio.transform, "Recorte", new Vector3(15, 165, 0), new Color(0.7f, 0.8f, 1f), 0.8f, false);
            Luz(_estudio.transform, "Preenchimento", new Vector3(8, 60, 0), new Color(0.75f, 0.8f, 0.9f), 0.45f, false);
            Luz(_estudio.transform, "Lateral", new Vector3(12, -120, 0), new Color(0.85f, 0.88f, 0.95f), 0.35f, false);
            _estudio.SetActive(false);
        }

        static Texture2D TexturaPiso()
        {
            const int n = 256, junta = 3;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "PBSLoaderCommonInterface_Piso", hideFlags = HideFlags.DontUnloadUnusedAsset };
            var px = new Il2CppStructArray<Color32>(n * n);
            var rnd = new System.Random(7);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool j = x < junta || y < junta;
                    int v = j ? 70 : 34 + rnd.Next(6); // placa escura com granulado leve, junta mais clara
                    px[y * n + x] = new Color32((byte)v, (byte)v, (byte)(v + 3), 255);
                }
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
            t.Apply(true, true);
            return t;
        }

        static void Modo(bool cinema)
        {
            _modoCinema = cinema; _modoDefinido = true;
            _luzesVitrine.SetActive(!cinema);
            _estudio.SetActive(cinema);
            _sombra.gameObject.SetActive(!cinema);
            _cam.backgroundColor = cinema ? Color.black : new Color(0, 0, 0, 0);
            _cam.cullingMask = cinema ? (1 << Camada) | (1 << CamadaPiso) : 1 << Camada;
            _cam.farClipPlane = cinema ? 400f : 200f;
            if (cinema)
            {
                _pivo.localRotation = Quaternion.identity;
                AjustarRtCinema();
            }
            else
            {
                LimparReflexo();
                _cam.fieldOfView = 24f;
                _cam.targetTexture = _rt;
                if (_rt != null) _cam.aspect = _rt.width / (float)_rt.height;
                if (_atual != null && _atual.raiz != null) Enquadrar(_atual);
                _fase = Fase.Inicio; // ao voltar ao menu, recomeça com um corte limpo
                _escuro = 1f;
            }
        }

        static void AjustarRtCinema()
        {
            int w = Mathf.Min(Screen.width, 2560), h = Mathf.Min(Screen.height, 1440);
            if (_rtCinema == null || _rtCinema.width != w || _rtCinema.height != h)
            {
                if (_rtCinema != null) { _cam.targetTexture = null; _rtCinema.Release(); UnityEngine.Object.Destroy(_rtCinema); }
                _rtCinema = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4, name = "PBSLoaderCommonInterface_Cinema" };
                _rtCinema.Create();
            }
            _cam.targetTexture = _rtCinema;
            _cam.aspect = w / (float)h;
        }

        // A cópia espelhada acompanha o ônibus mostrado (malhas e materiais são compartilhados).
        static void AtualizarReflexo()
        {
            if (_reflexoDe == _atual) return;
            LimparReflexo();
            if (_atual == null || _atual.raiz == null) return;
            var copia = UnityEngine.Object.Instantiate(_atual.raiz, _reflexo, false);
            copia.transform.localPosition = _atual.raiz.transform.localPosition;
            copia.SetActive(true);
            _reflexoDe = _atual;
        }

        static void LimparReflexo()
        {
            for (int i = _reflexo.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_reflexo.GetChild(i).gameObject);
            _reflexoDe = null;
        }

        // ---------- roteiro ----------

        enum Fase { Inicio, Carregando, Plano, Saindo }
        static Fase _fase;
        static List<string> _fila;
        static int _indice = -1;
        static string _cIni, _cSkin, _proxSkin;
        static float _faseDesde;
        static int _planoNum;
        static Plano _plano;
        static bool _cinemaComecou, _preCarregou;
        static readonly System.Random _rnd = new();
        static int _forcado;

        struct Plano
        {
            internal Vector3 de, ate, olharDe, olharAte;
            internal float fov, duracao;
            internal bool arco;                  // arco em volta do ônibus (a0..a1 graus, raio, altura)
            internal float a0, a1, raio, altura;
        }

        static void Cinema()
        {
            if (Time.unscaledTime - _ultimaRt > 1f) { _ultimaRt = Time.unscaledTime; AjustarRtCinema(); }
            if (_fila == null) MontarFila();
            if (_fila.Count == 0) return;
            float agora = Time.unscaledTime, dt = Anim.Dt;
            switch (_fase)
            {
                case Fase.Inicio:
                    Proximo();
                    break;
                case Fase.Carregando:
                    _escuro = Mathf.MoveTowards(_escuro, 1f, dt / 0.4f);
                    PedirOnibus(_cIni, _cSkin);
                    if (Falhou(_cIni) || agora - _faseDesde > 15f) { Proximo(); break; }
                    if (Pronto(_cIni) && _escuro >= 1f && _atual?.raiz != null)
                    {
                        _pivo.localRotation = Quaternion.identity;
                        NovoPlano(0);
                        _fase = Fase.Plano;
                        _cinemaComecou = true;
                    }
                    break;
                case Fase.Plano:
                    _escuro = Mathf.MoveTowards(_escuro, 0f, dt / 0.9f);
                    if (Filmar(agora - _faseDesde) >= 1f)
                    {
                        if (_planoNum == 0) NovoPlano(1); // corte seco para o segundo plano
                        else { _fase = Fase.Saindo; _faseDesde = agora - _plano.duracao; }
                    }
                    if (_planoNum == 1 && !_preCarregou)
                    {
                        // O próximo ônibus (e a skin dele) já vai sendo preparado durante este plano.
                        _preCarregou = true;
                        var prox = _fila[(_indice + 1) % _fila.Count];
                        _proxSkin = SkinAleatoria(prox);
                        PreCarregarOnibus(prox);
                        PreCarregarSkin(_proxSkin);
                    }
                    break;
                case Fase.Saindo:
                    Filmar(agora - _faseDesde); // a câmera segue andando enquanto escurece
                    _escuro = Mathf.MoveTowards(_escuro, 1f, dt / 0.55f);
                    if (_escuro >= 1f) Proximo();
                    break;
            }
            AtualizarReflexo();
        }

        static float _ultimaRt;

        static void PedirOnibus(string ini, string skin)
        {
            if (!string.Equals(ini, _iniPedido, StringComparison.OrdinalIgnoreCase) || !string.Equals(skin, _skinPedida, StringComparison.OrdinalIgnoreCase))
            {
                _iniPedido = ini; _skinPedida = skin;
                _pedidoEm = -10f; // sem a pausa de "rolar a lista"
            }
        }

        static void MontarFila()
        {
            _fila = new List<string>();
            try
            {
                var pasta = Il2Cpp.ProtonPaths.pastaModsBuses;
                if (!string.IsNullOrEmpty(pasta) && Directory.Exists(pasta)) _fila.AddRange(Directory.GetFiles(pasta, "*.ini"));
            }
            catch { }
            for (int i = _fila.Count - 1; i > 0; i--) { int j = _rnd.Next(i + 1); (_fila[i], _fila[j]) = (_fila[j], _fila[i]); }
            // Só no tour de inspeção: filtra os ônibus (PBSLOADERCOMMONINTERFACE_CINE_ONIBUS=1519|Manaus).
            var filtro = Inspecao.Ativa ? Environment.GetEnvironmentVariable("PBSLOADERCOMMONINTERFACE_CINE_ONIBUS") : null;
            if (!string.IsNullOrEmpty(filtro))
            {
                var partes = filtro.Split('|');
                var f = _fila.FindAll(x => Array.Exists(partes, t => Path.GetFileName(x).Contains(t, StringComparison.OrdinalIgnoreCase)));
                f.Sort((a, b) => Array.FindIndex(partes, t => Path.GetFileName(a).Contains(t, StringComparison.OrdinalIgnoreCase))
                                 .CompareTo(Array.FindIndex(partes, t => Path.GetFileName(b).Contains(t, StringComparison.OrdinalIgnoreCase))));
                _fila = f;
            }
            if (_fila.Count == 0) MelonLogger.Msg("[Cinema] nenhum ônibus de mod: o menu fica com a foto");
        }

        static void Proximo()
        {
            // Pula os que já falharam (todos falharam: para, o menu fica com a foto).
            for (int k = 0; k < _fila.Count; k++)
            {
                _indice = (_indice + 1) % _fila.Count;
                if (!Falhou(_fila[_indice])) break;
            }
            if (Falhou(_fila[_indice])) { _fila.Clear(); return; }
            var ini = _fila[_indice];
            _cSkin = ini == _cIni ? _cSkin : (_proxSkin != null && _preCarregou ? _proxSkin : SkinAleatoria(ini));
            _cIni = ini;
            _preCarregou = false; _proxSkin = null;
            _fase = Fase.Carregando;
            _faseDesde = Time.unscaledTime;
        }

        static string SkinAleatoria(string ini)
        {
            try
            {
                var l = OnibusMod.Skins(ini);
                return l.Count > 0 ? l[_rnd.Next(l.Count)] : null;
            }
            catch { return null; }
        }

        // ---------- câmera ----------

        static void NovoPlano(int n)
        {
            _planoNum = n;
            _faseDesde = Time.unscaledTime;
            var t = _atual.tamanho;
            float L = Mathf.Max(6f, t.z), W = Mathf.Max(2f, t.x), H = Mathf.Max(2f, _atual.altura);
            float s = _rnd.Next(2) == 0 ? 1f : -1f; // lado do ônibus
            // Primeiro plano: frente/3-4 (apresenta o ônibus); segundo: lateral, traseira, alto ou interno.
            bool interno = _atual.motorista != null || _atual.passageiro != null;
            // Com posições internas, o segundo plano é interno em metade das vezes (corredor mais que motorista).
            int tipo = n == 0 ? _rnd.Next(3)
                : interno && _rnd.Next(2) == 0 ? new[] { 6, 7, 7, 8 }[_rnd.Next(4)]
                : 3 + _rnd.Next(3);
            // Só no tour de inspeção: sequência de planos forçada (PBSLOADERCOMMONINTERFACE_CINE_PLANOS=7,8,6).
            var forcados = Inspecao.Ativa ? Environment.GetEnvironmentVariable("PBSLOADERCOMMONINTERFACE_CINE_PLANOS") : null;
            if (!string.IsNullOrEmpty(forcados))
            {
                var l = forcados.Split(',');
                if (int.TryParse(l[_forcado++ % l.Length], out var f)) tipo = f;
            }
            if (tipo == 6 && _atual.motorista == null) tipo = 7;
            _cam.nearClipPlane = tipo >= 6 ? 0.05f : 0.3f;
            Plano p;
            switch (tipo)
            {
                case 0: // 3/4 da frente, arco lento (a pose clássica)
                    p = Arco(25f * s, 55f * s, L * 0.62f + 5.5f, 1.9f, new Vector3(0, H * 0.42f, L * 0.05f), 30f, 9f);
                    break;
                case 1: // frente baixa, aproximando
                    p = new Plano
                    {
                        de = new Vector3(s * W * 1.4f, 0.8f, L / 2 + 9f), ate = new Vector3(s * W * 0.6f, 1.1f, L / 2 + 5.5f),
                        olharDe = new Vector3(0, H * 0.42f, L / 2 - 1.5f), olharAte = new Vector3(0, H * 0.46f, L / 2 - 2.5f), fov = 30f, duracao = 8f,
                    };
                    break;
                case 2: // lateral próxima, da frente para o meio
                    p = new Plano
                    {
                        de = new Vector3(s * (W / 2 + 4.8f), 1.2f, L * 0.55f), ate = new Vector3(s * (W / 2 + 4.2f), 1.4f, L * 0.05f),
                        olharDe = new Vector3(0, H * 0.45f, L * 0.22f), olharAte = new Vector3(0, H * 0.45f, -L * 0.15f), fov = 34f, duracao = 8f,
                    };
                    break;
                case 3: // alto, 3/4 traseira
                    p = Arco(200f * s, 235f * s, L * 0.7f + 6f, H * 2.3f, new Vector3(0, H * 0.3f, 0), 30f, 8.5f);
                    break;
                case 4: // traseira baixa, cruzando
                    p = new Plano
                    {
                        de = new Vector3(s * W * 1.7f, 0.65f, -L / 2 - 7.5f), ate = new Vector3(-s * W * 0.3f, 0.9f, -L / 2 - 6f),
                        olharDe = new Vector3(0, H * 0.5f, -L / 2 + 1f), olharAte = new Vector3(0, H * 0.5f, -L / 2 + 1.5f), fov = 30f, duracao = 7.5f,
                    };
                    break;
                case 6: // do banco do motorista: olhar passeando pelo para-brisa e o painel
                    {
                        var c = Local(_atual.motorista) + new Vector3(0.05f, 0.02f, -0.12f);
                        p = new Plano
                        {
                            de = c, ate = c + new Vector3(0.12f, 0.02f, 0.05f),
                            olharDe = c + new Vector3(-0.9f, -0.35f, 3f), olharAte = c + new Vector3(1.2f, -0.25f, 3f), fov = 58f, duracao = 7.5f,
                        };
                        break;
                    }
                case 7: // corredor de trás para a frente, na altura dos olhos, atravessando o ônibus
                case 8: // corredor da frente para trás
                    {
                        // Altura dos olhos de um passageiro em pé: a câmera de passageiro do .ini, senão a do
                        // motorista (o piso do salão fica perto da altura do banco dele).
                        var c = Local(_atual.passageiro ?? _atual.motorista);
                        float y = Mathf.Clamp(c.y - 0.1f, 1.5f, H - 0.35f);
                        float tras = -L / 2 + 1.2f, frente = L / 2 - 2.8f; // o fundo e logo atrás da catraca/motorista
                        bool praFrente = tipo == 7;
                        float z0 = praFrente ? tras : frente, z1 = praFrente ? Mathf.Lerp(tras, frente, 0.7f) : Mathf.Lerp(frente, tras, 0.7f);
                        float olhar = praFrente ? 6f : -6f;
                        p = new Plano
                        {
                            de = new Vector3(0.12f, y, z0), ate = new Vector3(-0.08f, y + 0.04f, z1),
                            olharDe = new Vector3(0.25f, y - 0.3f, z0 + olhar), olharAte = new Vector3(-0.15f, y - 0.25f, z1 + olhar),
                            fov = 58f, duracao = Mathf.Clamp(L * 0.75f, 8f, 13f), // o travelling atravessa o ônibus devagar
                        };
                        break;
                    }
                default: // lateral de longe, deslizando de trás para a frente
                    p = new Plano
                    {
                        de = new Vector3(s * (W / 2 + L * 0.55f + 3f), 1.3f, -L * 0.25f), ate = new Vector3(s * (W / 2 + L * 0.55f + 3f), 1.5f, L * 0.2f),
                        olharDe = new Vector3(0, H * 0.4f, -L * 0.1f), olharAte = new Vector3(0, H * 0.4f, L * 0.1f), fov = 28f, duracao = 8f,
                    };
                    break;
            }
            _plano = p;
            Filmar(0f);
        }

        // Ponto do .ini (coordenadas do modelo) no espaço do palco (o ônibus centrado, rodas no chão).
        static Vector3 Local(float[] p) => new Vector3(p[0], p[1], p[2]) - _atual.origem;

        // Arco em volta do ônibus (ângulos a partir da frente, em graus), sempre olhando o centro.
        static Plano Arco(float a0, float a1, float raio, float altura, Vector3 olhar, float fov, float dur)
        {
            Vector3 P(float a) => new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * raio, altura, Mathf.Cos(a * Mathf.Deg2Rad) * raio);
            return new Plano { de = P(a0), ate = P(a1), olharDe = olhar, olharAte = olhar, fov = fov, duracao = dur, arco = true, a0 = a0, a1 = a1, raio = raio, altura = altura };
        }

        // Posiciona a câmera no plano atual; devolve o progresso (0..1, passa de 1 na saída).
        static float Filmar(float decorrido)
        {
            var p = _plano;
            float t = decorrido / p.duracao;
            float tc = Mathf.Clamp01(t);
            float e = Mathf.Lerp(tc, tc * tc * (3f - 2f * tc), 0.6f); // movimento contínuo, com saída e chegada macias
            if (t > 1f) e = 1f + (t - 1f) * 0.6f;                    // na saída, segue no mesmo ritmo
            Vector3 pos;
            if (p.arco)
            {
                float a = Mathf.LerpUnclamped(p.a0, p.a1, e) * Mathf.Deg2Rad;
                pos = new Vector3(Mathf.Sin(a) * p.raio, p.altura, Mathf.Cos(a) * p.raio);
            }
            else pos = Vector3.LerpUnclamped(p.de, p.ate, e);
            var olhar = Vector3.LerpUnclamped(p.olharDe, p.olharAte, e);
            var origem = _palco.transform.position;
            _cam.fieldOfView = p.fov;
            _cam.transform.position = origem + pos;
            _cam.transform.LookAt(origem + olhar);
            // O ônibus fica no centro da área livre, à direita da coluna do menu (~24% da largura).
            float hfov = 2f * Mathf.Atan(Mathf.Tan(p.fov * 0.5f * Mathf.Deg2Rad) * _cam.aspect) * Mathf.Rad2Deg;
            if (_cam.nearClipPlane > 0.1f) _cam.transform.Rotate(0, -hfov * 0.12f, 0, Space.World);
            return t;
        }
    }
}
