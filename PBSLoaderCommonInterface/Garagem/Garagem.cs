using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface.Garagem
{
    // Prévia 3D dos ônibus nas telas de veículo e pintura: o ônibus gira no centro da tela, já com a
    // skin. Para não pesar na troca de ônibus/skin:
    //  - ler o modelo (o .c1 é aberto pelo jogo na thread principal), decodificar e reduzir as texturas e fundir as malhas roda numa
    //    thread .NET pura (OnibusMod/Imagem/Modelo3DS), com cache em disco das texturas reduzidas;
    //  - na thread principal só se criam malhas, texturas e materiais, com orçamento de tempo por frame;
    //  - os últimos ônibus montados ficam em memória (voltar a um deles é instantâneo), e o palco
    //    sobrevive à troca de cena (o ônibus escolhido já está pronto na tela de pintura);
    //  - pedidos seguidos (rolar a lista) só disparam a carga do último, depois de uma pausa curta.
    // Enquanto não está pronto (ou para ônibus nativos, que não têm arquivos), a tela mostra a imagem.
    internal static partial class Previa3D
    {
        internal static MelonPreferences_Entry<bool> Pref;
        internal static void CriarPreferencias(MelonPreferences_Category cat) =>
            Pref = cat.CreateEntry("Previa3D", true, "Prévia 3D girando nas telas de veículo e pintura");

        const int Camada = 31;
        // Orçamento de montagem por frame; maior com a tela escura do cinema (ninguém vê o frame).
        static double OrcamentoMs => _modoCinema && _escuro > 0.9f ? 20.0 : 5.0;
        const int MaxEmMemoria = 3;

        static bool _iniciado;
        static GameObject _palco;
        static Transform _pivo;
        static Camera _cam;
        static RenderTexture _rt;
        static Transform _sombra;
        static Shader _padrao, _transparente;

        // ---------- pedido da tela ----------
        static string _iniPedido, _skinPedida;
        static float _pedidoEm, _visivelAte;
        static Montado _atual;
        static readonly List<Montado> _memoria = new();
        static Task<Preparado> _tarefa;
        static string _tarefaIni;
        static CancellationTokenSource _cancelar;
        static Montagem _montando;

        // Skins: uma skin é um conjunto de texturas por slot (skin001..skin010, descrito no .txt dela).
        // Cada arquivo é decodificado fora da thread principal (os da skin pedida, os da padrão do ônibus
        // que está carregando, em paralelo com ele, e os das vizinhas na lista de pinturas), vira textura
        // no frame e fica num cache curto. Os decodificados também vão para o cache em disco.
        static readonly Dictionary<string, Task<ImagemRgba>> _decodificando = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Texture2D> _skins = new(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> _ordemSkins = new();
        static readonly HashSet<string> _skinsFalhas = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Dictionary<int, string>> _slots = new(StringComparer.OrdinalIgnoreCase);
        const int MaxSkins = 24;
        static readonly Dictionary<string, float> _usadaEm = new(StringComparer.OrdinalIgnoreCase); // último uso de cada textura de skin

        internal static string UltimoOnibus;

        // Ônibus sem prévia possível (modelo ilegível etc.): não tenta de novo nesta sessão.
        static readonly HashSet<string> _falhas = new(StringComparer.OrdinalIgnoreCase);
        internal static bool Falhou(string ini) => ini != null && _falhas.Contains(ini);

        sealed class Preparado
        {
            internal PacoteOnibus pacote; internal List<Lote> lotes; internal float[] min, max; internal string skinPadrao;
            internal float Chao => pacote.chao ?? min[1]; // base das rodas (wheels.txt) ou o ponto mais baixo
            internal Vector3 Min => new(min[0], min[1], min[2]);
            internal Vector3 Max => new(max[0], max[1], max[2]);
        }

        sealed class Montado
        {
            internal string ini, pastaSkins, skinPadrao;
            internal GameObject raiz;
            internal readonly List<(Material m, int slot)> materiaisSkin = new();
            internal readonly List<UnityEngine.Object> donos = new();
            internal string skinAplicada;
            internal bool skinHd; // aplicada no tamanho do cinema
            internal readonly Dictionary<Material, (Texture tex, Color cor)> original = new(); // slots que a skin não define
            internal Vector3 centro, tamanho, origem; internal float raio, altura;
            internal float[] motorista, passageiro; // posições internas do .ini (coordenadas do modelo)
            internal float usadoEm;
        }

        sealed class Montagem
        {
            internal Preparado prep;
            internal Montado m;
            internal int i;
            internal readonly Dictionary<string, Texture2D> tex = new(StringComparer.OrdinalIgnoreCase);
        }

        // ---------- API das telas ----------

        // Chamado a cada frame pela tela visível: qual ônibus (ini) e skin (arquivo, ou null = padrão), e
        // a área da tela onde a prévia aparece (para o arraste e a resolução da imagem).
        static RectTransform _area;

        internal static void Pedir(string ini, string skin, RectTransform area)
        {
            if (Pref != null && !Pref.Value) return;
            _visivelAte = Time.unscaledTime + 0.15f;
            _area = area;
            if (!string.Equals(ini, _iniPedido, StringComparison.OrdinalIgnoreCase) || !string.Equals(skin, _skinPedida, StringComparison.OrdinalIgnoreCase))
            {
                _iniPedido = ini; _skinPedida = skin; _pedidoEm = Time.unscaledTime;
            }
        }

        // A prévia deste ônibus está pronta para aparecer: montada e já com a skin (não surge sem pintura
        // para ela entrar depois). Trocar de skin depois disso acontece no lugar.
        internal static bool Pronto(string ini) =>
            _atual != null && ini != null && string.Equals(_atual.ini, ini, StringComparison.OrdinalIgnoreCase) && _montando == null
            && (_atual.materiaisSkin.Count == 0 || _atual.skinAplicada != null || (_skinPedida ?? _atual.skinPadrao) == null);

        // Carregando a prévia deste ônibus (para a tela mostrar o indicador em vez da imagem).
        internal static bool Carregando(string ini) => ini != null && !_falhas.Contains(ini) && !Pronto(ini);

        // Arquivos de cada slot de uma skin escolhida. Um .txt descreve os slots ([skin] skin001=...);
        // uma imagem usa o .txt de mesmo nome, se houver, ou vale só para o slot 1.
        static Dictionary<int, string> Slots(string sel)
        {
            if (_slots.TryGetValue(sel, out var r)) return r;
            r = OnibusMod.SlotsDaSkin(sel);
            _slots[sel] = r;
            return r;
        }

        // Tamanho de cada arquivo de skin: o slot 1 (a carroceria) maior que as partes; no cinema, que
        // mostra o ônibus em close na tela cheia, o dobro (2048/1024) da prévia das telas de seleção.
        static int LadoDe(int slot) => slot == 1 ? (_modoCinema ? 2048 : OnibusMod.LadoSkin) : (_modoCinema ? 1024 : OnibusMod.LadoTextura);

        // O cache de skins é por arquivo e tamanho ("2048|caminho"): a do cinema e a da prévia convivem.
        static string Chave(string arq, int lado) => lado + "|" + arq;
        static (int lado, string arq) DaChave(string k) { int i = k.IndexOf('|'); return (int.Parse(k.Substring(0, i)), k.Substring(i + 1)); }

        // Começa a decodificar uma skin que deve ser pedida em breve (vizinhas na lista, próximo do cinema).
        internal static void PreCarregarSkin(string sel)
        {
            if (sel == null || _decodificando.Count >= 3) return; // não ocupa todos os núcleos
            Dictionary<int, string> slots;
            try { slots = Slots(sel); } catch { return; }
            foreach (var (slot, arq) in slots)
            {
                int lado = LadoDe(slot);
                var k = Chave(arq, lado);
                if (!_skins.ContainsKey(k) && !_decodificando.ContainsKey(k) && !_skinsFalhas.Contains(k))
                    _decodificando[k] = Task.Run(() => Imagem.Carregar(arq, lado));
            }
        }

        // Textura de um arquivo de skin, se pronta; senão começa (ou segue) a decodificação e devolve null.
        static Texture2D Skin(string arq, int lado)
        {
            var k = Chave(arq, lado);
            if (_skins.TryGetValue(k, out var t))
            {
                if (t != null && !t.WasCollected) { _usadaEm[k] = Time.unscaledTime; return t; }
                _skins.Remove(k); _ordemSkins.Remove(k); // descarregada por fora: decodifica de novo
            }
            if (_skinsFalhas.Contains(k)) return null;
            if (!_decodificando.TryGetValue(k, out var tarefa))
            {
                _decodificando[k] = Task.Run(() => Imagem.Carregar(arq, lado));
                return null;
            }
            if (!tarefa.IsCompleted) return null;
            // Uma conversão para textura por frame (cada uma sobe até ~20 MB para a GPU).
            if (_quadroConversao == Time.frameCount) return null;
            _quadroConversao = Time.frameCount;
            _decodificando.Remove(k);
            var img = tarefa.IsCompletedSuccessfully ? tarefa.Result : null;
            t = img != null ? Textura(img, Path.GetFileName(arq)) : null;
            if (t == null) { _skinsFalhas.Add(k); return null; }
            t.anisoLevel = 8; // a lateral do ônibus vista de lado continua nítida
            _skins[k] = t;
            _ordemSkins.Add(k);
            _usadaEm[k] = Time.unscaledTime;
            Podar();
            return t;
        }

        // Cache curto (LRU): descarta as texturas usadas há mais tempo, nunca as de skins aplicadas nem as
        // usadas nos últimos segundos (a skin que ainda está sendo montada, slot a slot, e as pré-carregadas).
        // Descartar essas fazia a skin em montagem perder partes e nunca terminar.
        static void Podar()
        {
            while (_ordemSkins.Count > MaxSkins)
            {
                string velha = null; float t0 = float.MaxValue;
                foreach (var c in _ordemSkins)
                {
                    if (_emUso.Contains(c)) continue;
                    float u = _usadaEm.TryGetValue(c, out var x) ? x : 0f;
                    if (Time.unscaledTime - u < 8f) continue;
                    if (u < t0) { t0 = u; velha = c; }
                }
                if (velha == null) return; // tudo em uso ou recente: espera
                if (_skins.TryGetValue(velha, out var v) && v != null) UnityEngine.Object.Destroy(v);
                _skins.Remove(velha);
                _ordemSkins.Remove(velha);
                _usadaEm.Remove(velha);
            }
        }

        static bool Falha(string arq, int lado) => _skinsFalhas.Contains(Chave(arq, lado));

        static readonly HashSet<string> _emUso = new(StringComparer.OrdinalIgnoreCase);
        static float _logSkin;
        static int _quadroConversao = -1;

        // Pré-carregados que terminaram viram textura, um por frame (subir uma textura leva poucos ms).
        static void Colher()
        {
            foreach (var kv in _decodificando)
                if (kv.Value.IsCompleted) { var (lado, arq) = DaChave(kv.Key); Skin(arq, lado); return; }
        }

        internal static Texture Saida => _rt;

        // Caminho do .ini de um item da lista de ônibus ("MOD nome.ini"); null para os nativos.
        internal static string IniDoItem(string texto)
        {
            if (string.IsNullOrEmpty(texto) || !texto.StartsWith("MOD ", StringComparison.OrdinalIgnoreCase)) return null;
            string pasta;
            try { pasta = Il2Cpp.ProtonPaths.pastaModsBuses; } catch { return null; }
            if (string.IsNullOrEmpty(pasta)) return null;
            var arq = texto.Substring(4).Trim();
            if (!arq.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) arq += ".ini";
            var c = Path.Combine(pasta, arq);
            return File.Exists(c) ? c : null;
        }

        // .ini a partir do que o jogo guarda como ônibus escolhido (caminho, nome do arquivo ou nome sem extensão).
        internal static string IniDoArquivo(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && File.Exists(s)) return s;
            return IniDoItem("MOD " + Path.GetFileName(s));
        }

        // ---------- por frame ----------

        // Por frame (loop do mod). Sem tela pedindo, a câmera fica desligada e nada roda.
        internal static void Atualizar()
        {
            AtenderC1(); // antes de tudo: quem pediu está esperando, mesmo com a prévia escondida
            var areaPrevia = _area != null && !_area.WasCollected ? _area : null;
            bool vitrine = Time.unscaledTime < _visivelAte && (Pref == null || Pref.Value);
            bool cinema = !vitrine && Time.unscaledTime < _cinemaAte && (PrefCinema == null || PrefCinema.Value);
            bool visivel = vitrine || cinema;
            if (_cam != null) _cam.enabled = visivel && _atual != null && _atual.raiz != null && _atual.raiz.activeSelf;
            if (!visivel)
            {
                // Luzes do palco apagadas fora da vitrine: na câmera deferred do jogo o cullingMask de uma
                // camada só não é respeitado e elas iluminavam a cidade inteira (imagem chapada, lavada).
                if (_modoDefinido)
                {
                    _modoDefinido = false;
                    if (_luzesVitrine != null) _luzesVitrine.SetActive(false);
                    if (_estudio != null) _estudio.SetActive(false);
                }
                return;
            }
            if (!_iniciado) Iniciar();
            if (cinema != _modoCinema || !_modoDefinido) Modo(cinema);
            if (cinema) { try { Cinema(); } catch (Exception e) { MelonLogger.Warning("[Cinema] " + e); _cinemaAte = 0; } }
            try
            {
                using (Desempenho.Medir("·3D programar")) Programar();
                using (Desempenho.Medir("·3D montar")) Montar();
                using (Desempenho.Medir("·3D skin")) AplicarSkin();
                using (Desempenho.Medir("·3D colher")) Colher();
                if (!cinema)
                {
                    Girar(areaPrevia);
                    using (Desempenho.Medir("·3D resolução")) if (areaPrevia != null) Resolucao(areaPrevia);
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Garagem] " + e); _iniPedido = null; }
        }

        static void Iniciar()
        {
            _iniciado = true;
            Imagem.PastaCache = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonInterface", "cache3d");
            Modelo3DS.DecifradorC1 = DecifrarC1;
            _padrao = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            _transparente = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? _padrao;
            CriarPalco();
        }

        // Modelos .c1: a thread que prepara o ônibus pede ao jogo (MepC1Enc.Decrypt, o mesmo que ele usa
        // para carregar o ônibus) e espera. O IL2CPP só pode ser chamado na thread principal, então o
        // pedido vai para uma fila atendida a cada frame. O resultado fica só em memória.
        static readonly ConcurrentQueue<(byte[] dados, TaskCompletionSource<byte[]> fim)> _filaC1 = new();

        static byte[] DecifrarC1(byte[] dados)
        {
            var fim = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _filaC1.Enqueue((dados, fim));
            return fim.Task.Wait(20000) ? fim.Task.Result : null;
        }

        static void AtenderC1()
        {
            while (_filaC1.TryDequeue(out var p))
            {
                try
                {
                    var r = Il2CppMepC1.MepC1Enc.Decrypt(new Il2CppStructArray<byte>(p.dados));
                    if (r == null) { p.fim.TrySetResult(null); continue; }
                    var b = new byte[r.Length];
                    for (int i = 0; i < b.Length; i++) b[i] = r[i];
                    p.fim.TrySetResult(b);
                }
                catch (Exception e)
                {
                    MelonLogger.Msg("[Garagem] modelo .c1 não abriu: " + e.Message);
                    p.fim.TrySetResult(null);
                }
            }
        }

        // Decide o que carregar: o ônibus pedido (depois de 0,12 s sem trocar), da memória se já montado.
        static void Programar()
        {
            if (_iniPedido == null || _falhas.Contains(_iniPedido) || Time.unscaledTime - _pedidoEm < 0.12f) return;
            if (_atual != null && string.Equals(_atual.ini, _iniPedido, StringComparison.OrdinalIgnoreCase)) return;
            if (_montando != null && string.Equals(_montando.m.ini, _iniPedido, StringComparison.OrdinalIgnoreCase)) return;

            var mem = _memoria.Find(x => string.Equals(x.ini, _iniPedido, StringComparison.OrdinalIgnoreCase));
            if (mem != null) { Trocar(mem); return; }

            if (_tarefa != null && string.Equals(_tarefaIni, _iniPedido, StringComparison.OrdinalIgnoreCase))
            {
                if (!_tarefa.IsCompleted) return;
                var prep = _tarefa.IsCompletedSuccessfully ? _tarefa.Result : null;
                var falha = _tarefa.Exception?.InnerException?.Message;
                _tarefa = null;
                if (prep == null || prep.lotes == null || prep.lotes.Count == 0)
                {
                    var erro = prep?.pacote?.erro ?? falha ?? "sem malhas visíveis";
                    MelonLogger.Msg($"[Garagem] {Path.GetFileName(_tarefaIni)}: sem prévia 3D ({erro})");
                    _falhas.Add(_tarefaIni); // fica na imagem
                    return;
                }
                MelonLogger.Msg($"[Garagem] {Path.GetFileName(_tarefaIni)}: preparado em {prep.pacote.ms:0} ms, {prep.lotes.Count} malhas");
                _montando = new Montagem { prep = prep, m = new Montado { ini = _tarefaIni, pastaSkins = prep.pacote.pastaSkins, skinPadrao = prep.skinPadrao } };
                return;
            }
            // Já preparado de antemão (o próximo ônibus do cinema)?
            if (_preparando.TryGetValue(_iniPedido, out var pronto))
            {
                _preparando.Remove(_iniPedido);
                _tarefaIni = _iniPedido;
                _tarefa = pronto;
                return;
            }
            // Pedido novo: cancela o anterior (o resultado dele é descartado) e começa este.
            _cancelar?.Cancel();
            _cancelar = new CancellationTokenSource();
            var tok = _cancelar.Token;
            var ini = _iniPedido;
            _tarefaIni = ini;
            _tarefa = Task.Run(() => Preparar(ini, tok), tok);
            // A skin decodifica junto com o ônibus, em outras threads.
            try
            {
                var sel = _skinPedida ?? OnibusMod.SkinPadrao(ini);
                if (sel != null) foreach (var (slot, arq) in Slots(sel)) Skin(arq, LadoDe(slot));
            }
            catch { }
        }

        // Prepara um ônibus fora da thread principal sem mostrá-lo (o pedido dele depois só monta).
        static readonly Dictionary<string, Task<Preparado>> _preparando = new(StringComparer.OrdinalIgnoreCase);

        internal static void PreCarregarOnibus(string ini)
        {
            if (ini == null || _falhas.Contains(ini) || _preparando.ContainsKey(ini)) return;
            if (_memoria.Exists(x => string.Equals(x.ini, ini, StringComparison.OrdinalIgnoreCase))) return;
            if (_preparando.Count >= 2) return;
            _preparando[ini] = Task.Run(() => Preparar(ini, CancellationToken.None));
        }

        static Preparado Preparar(string ini, CancellationToken tok)
        {
            var p = OnibusMod.Carregar(ini, tok);
            tok.ThrowIfCancellationRequested();
            var r = new Preparado { pacote = p };
            if (p.erro != null && p.modelos.Count == 0) return r;
            r.lotes = OnibusMod.Lotes(p);
            // Esta função roda fora da thread principal: nada de Unity aqui (nem Vector3.Min, que no
            // interop chama o IL2CPP e derruba o GC do jogo numa thread que ele não conhece).
            var mn = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var mx = new[] { float.MinValue, float.MinValue, float.MinValue };
            foreach (var l in r.lotes)
                for (int i = 0; i + 2 < l.pos.Count; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        float v = l.pos[i + k];
                        if (v < mn[k]) mn[k] = v;
                        if (v > mx[k]) mx[k] = v;
                    }
            r.min = mn; r.max = mx;
            try { r.skinPadrao = OnibusMod.SkinPadrao(ini); } catch { }
            return r;
        }

        // Monta (thread principal) o ônibus preparado, alguns lotes por frame.
        static void Montar()
        {
            var mt = _montando;
            if (mt == null) return;
            if (!string.Equals(mt.m.ini, _iniPedido, StringComparison.OrdinalIgnoreCase) && _iniPedido != null)
            {
                Descartar(mt.m); _montando = null; return; // o jogador já foi para outro ônibus
            }
            var sw = Stopwatch.StartNew();
            var m = mt.m;
            if (m.raiz == null)
            {
                m.raiz = new GameObject("Onibus") { layer = Camada };
                m.raiz.transform.SetParent(_pivo, false);
                m.raiz.SetActive(false);
                var c = (mt.prep.Min + mt.prep.Max) * 0.5f;
                m.centro = c;
                m.raio = (mt.prep.Max - mt.prep.Min).magnitude * 0.5f;
                m.altura = mt.prep.max[1] - mt.prep.Chao;
                m.tamanho = mt.prep.Max - mt.prep.Min;
                m.origem = new Vector3(c.x, mt.prep.Chao, c.z);
                m.motorista = mt.prep.pacote.motorista;
                m.passageiro = mt.prep.pacote.passageiro;
                m.raiz.transform.localPosition = new Vector3(-c.x, -mt.prep.Chao, -c.z); // pivô no centro, rodas no chão
            }
            var lotes = mt.prep.lotes;
            while (mt.i < lotes.Count && sw.Elapsed.TotalMilliseconds < OrcamentoMs)
            {
                // Lote que ainda precisa criar a textura dele: só no começo de um frame (subir a textura e
                // criar a malha no mesmo frame passava do orçamento).
                var l = lotes[mt.i];
                if (sw.Elapsed.TotalMilliseconds > 0.5 && l.textura != null && !mt.tex.ContainsKey(l.textura)) break;
                CriarLote(mt, l);
                mt.i++;
            }
            if (mt.i < lotes.Count) return;
            _montando = null;
            _memoria.Add(m);
            while (_memoria.Count > MaxEmMemoria)
            {
                Montado velho = null;
                foreach (var x in _memoria) if (x != m && (velho == null || x.usadoEm < velho.usadoEm)) velho = x;
                if (velho == null) break;
                _memoria.Remove(velho);
                Descartar(velho);
            }
            Trocar(m);
        }

        static void Trocar(Montado m)
        {
            if (_atual != null && _atual != m && _atual.raiz != null) _atual.raiz.SetActive(false);
            _atual = m;
            m.usadoEm = Time.unscaledTime;
            m.raiz.SetActive(true);
            Enquadrar(m);
            _girando = 0f; _velocidade = 140f; // entra com um giro rápido que desacelera
            if (_modoCinema) _pivo.localRotation = Quaternion.identity;
            _surgiu = Time.unscaledTime;
        }

        // Arrays do IL2CPP preenchidos por cópia de memória (o cabeçalho do array tem 4 ponteiros).
        static unsafe void Copiar(float[] dados, IntPtr destino, long bytes)
        {
            fixed (float* src = dados)
                Buffer.MemoryCopy(src, (byte*)destino + 4 * IntPtr.Size, bytes, Math.Min(bytes, (long)dados.Length * 4));
        }

        static Il2CppStructArray<Vector3> CopiaV3(float[] dados, int n)
        {
            var a = new Il2CppStructArray<Vector3>(n);
            Copiar(dados, a.Pointer, (long)n * 12);
            return a;
        }

        static Il2CppStructArray<Vector2> CopiaV2(float[] dados, int n)
        {
            var a = new Il2CppStructArray<Vector2>(n);
            Copiar(dados, a.Pointer, (long)n * 8);
            return a;
        }

        static unsafe Il2CppStructArray<int> CopiaInt(int[] dados)
        {
            var a = new Il2CppStructArray<int>(dados.Length);
            fixed (int* src = dados)
                Buffer.MemoryCopy(src, (byte*)a.Pointer + 4 * IntPtr.Size, (long)dados.Length * 4, (long)dados.Length * 4);
            return a;
        }

        static void CriarLote(Montagem mt, Lote l)
        {
            var m = mt.m;
            int nv = l.pos.Count / 3;
            var mesh = new Mesh { name = "lote", hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (nv > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = CopiaV3(l.pos.ToArray(), nv);
            mesh.normals = CopiaV3(l.normal.ToArray(), nv);
            mesh.uv = CopiaV2(l.uv.ToArray(), nv);
            mesh.triangles = CopiaInt(l.tris.ToArray());
            mesh.UploadMeshData(true);
            m.donos.Add(mesh);

            var go = new GameObject("lote") { layer = Camada };
            go.transform.SetParent(m.raiz.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Texture2D tex = null;
            if (l.textura != null && !mt.tex.TryGetValue(l.textura, out tex))
            {
                mt.prep.pacote.texturas.TryGetValue(l.textura, out var img);
                tex = img != null ? Textura(img, Path.GetFileName(l.textura)) : null;
                mt.tex[l.textura] = tex;
                if (tex != null) m.donos.Add(tex);
            }
            Material mat;
            var cor = l.material != null ? new Color(l.material.r, l.material.g, l.material.b) : new Color(0.7f, 0.7f, 0.7f);
            switch (l.tipo)
            {
                case OnibusMod.Tipo.Vidro:
                case OnibusMod.Tipo.VidroLimpo:
                    mat = new Material(_transparente);
                    // A camada de chuva quase some (seco): sobre o vidro normal, duas camadas translúcidas no
                    // mesmo plano brigavam; sozinha, ainda dá o reflexo de um vidro.
                    if (l.tipo == OnibusMod.Tipo.VidroLimpo) { tex = null; mat.color = new Color(0.12f, 0.15f, 0.18f, 0.1f); }
                    // Vidro com textura (riscos, sujeira): mais transparente e levemente escuro, senão fica
                    // leitoso visto de dentro contra o fundo escuro.
                    else mat.color = tex != null ? new Color(0.7f, 0.75f, 0.8f, 0.3f) : new Color(0.12f, 0.15f, 0.18f, 0.45f);
                    break;
                case OnibusMod.Tipo.Espelho:
                    mat = new Material(_padrao) { color = new Color(0.25f, 0.27f, 0.3f) };
                    mat.SetFloat("_Glossiness", 0.95f);
                    tex = null;
                    break;
                default:
                    // Sem textura: a cor-base do material, menos o magenta de marcação dos slots de skin
                    // (que a skin cobre no jogo), que vira branco.
                    bool marcacao = cor.r > 0.8f && cor.g < 0.3f && cor.b > 0.8f;
                    mat = new Material(_padrao) { color = tex != null || marcacao ? Color.white : cor };
                    mat.SetFloat("_Glossiness", l.skin > 0 ? 0.55f : 0.25f);
                    mat.SetFloat("_Metallic", 0f);
                    break;
            }
            if (tex != null) mat.mainTexture = tex;
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m.donos.Add(mat);
            r.sharedMaterial = mat;
            if (l.skin > 0) { m.materiaisSkin.Add((mat, l.skin)); m.original[mat] = (tex, mat.color); }
        }

        static unsafe Texture2D Textura(ImagemRgba img, string nome)
        {
            if (img.jpeg != null)
            {
                var j = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = nome, hideFlags = HideFlags.DontUnloadUnusedAsset };
                if (!ImageConversion.LoadImage(j, img.jpeg, true)) { UnityEngine.Object.Destroy(j); return null; }
                j.anisoLevel = 4;
                return j;
            }
            // DontUnloadUnusedAsset: o jogo chama Resources.UnloadUnusedAssets ao trocar de ônibus, e isso
            // destruiria as texturas em cache que ainda não estão em nenhum material (skins pré-carregadas,
            // ônibus guardados).
            var t = new Texture2D(img.w, img.h, TextureFormat.RGBA32, img.mips > 1, false) { name = nome, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (t.mipmapCount != img.mips) { UnityEngine.Object.Destroy(t); return null; }
            fixed (byte* p = img.dados)
                if (!t.LoadRawTextureDataImpl((IntPtr)p, img.dados.Length)) { UnityEngine.Object.Destroy(t); return null; }
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.Apply(false, true); // sobe para a GPU e libera a cópia na memória
            return t;
        }

        static void Descartar(Montado m)
        {
            if (m == null) return;
            if (m.raiz != null) UnityEngine.Object.Destroy(m.raiz);
            foreach (var o in m.donos) if (o != null) UnityEngine.Object.Destroy(o);
            m.donos.Clear();
        }

        // ---------- skin ----------

        static void AplicarSkin()
        {
            var m = _atual;
            if (m == null || m.materiaisSkin.Count == 0) return;
            var sel = _skinPedida ?? m.skinPadrao;
            // A mesma skin no tamanho do modo atual já aplicada: nada a fazer (no cinema ela é refeita em 2048).
            if (sel == null || string.Equals(m.skinAplicada, sel, StringComparison.OrdinalIgnoreCase) && m.skinHd == _modoCinema) return;
            Dictionary<int, string> slots;
            try { slots = Slots(sel); } catch { m.skinAplicada = sel; return; }
            // Espera todos os arquivos da skin (aplicar aos poucos deixaria o ônibus meio pintado).
            var tex = new Dictionary<int, Texture2D>();
            bool falta = false;
            foreach (var (slot, arq) in slots)
            {
                var t = Skin(arq, LadoDe(slot));
                if (t != null) tex[slot] = t;
                else if (!Falha(arq, LadoDe(slot))) falta = true;
            }
            if (falta)
            {
                if (Time.unscaledTime > _logSkin)
                {
                    _logSkin = Time.unscaledTime + 3f;
                    var esperando = new List<string>();
                    foreach (var (slot, arq) in slots)
                    {
                        var k = Chave(arq, LadoDe(slot));
                        if (!_skins.ContainsKey(k) && !_skinsFalhas.Contains(k)) esperando.Add($"{slot}:{Path.GetFileName(arq)}{(_decodificando.TryGetValue(k, out var tk) ? "(" + tk.Status + ")" : "(sem tarefa)")}");
                    }
                    MelonLogger.Msg($"[Garagem] skin {Path.GetFileName(sel)} esperando {string.Join(", ", esperando)}");
                }
                return;
            }
            bool outra = m.skinAplicada != null && !string.Equals(m.skinAplicada, sel, StringComparison.OrdinalIgnoreCase);
            if (outra) SkinTrocadaEm = Time.unscaledTime; // troca no lugar: a tela pulsa (não quando só ganha resolução)
            m.skinAplicada = sel;
            m.skinHd = _modoCinema;
            _emUso.Clear();
            foreach (var x in _memoria)
                if (x.skinAplicada != null)
                    foreach (var (slot, a) in Slots(x.skinAplicada))
                        _emUso.Add(Chave(a, slot == 1 ? (x.skinHd ? 2048 : OnibusMod.LadoSkin) : (x.skinHd ? 1024 : OnibusMod.LadoTextura)));
            foreach (var (mat, slot) in m.materiaisSkin)
            {
                if (mat == null) continue;
                if (tex.TryGetValue(slot, out var t)) { mat.mainTexture = t; mat.color = Color.white; }
                else if (m.original.TryGetValue(mat, out var orig)) { mat.mainTexture = orig.tex; mat.color = orig.cor; }
            }
        }

        // ---------- palco ----------

        static void CriarPalco()
        {
            _palco = new GameObject("PBSLoaderCommonInterface_Garagem") { layer = Camada };
            UnityEngine.Object.DontDestroyOnLoad(_palco);
            _palco.transform.position = new Vector3(0, -3000f, 0); // longe de qualquer cenário

            _pivo = new GameObject("Pivo").transform;
            _pivo.SetParent(_palco.transform, false);

            var camGo = new GameObject("Camera") { layer = Camada };
            camGo.transform.SetParent(_palco.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.cullingMask = 1 << Camada;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0, 0, 0, 0);
            _cam.fieldOfView = 24f;
            _cam.nearClipPlane = 0.3f; _cam.farClipPlane = 200f;
            _cam.renderingPath = RenderingPath.Forward;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.useOcclusionCulling = false;
            _cam.depth = -100;
            _cam.enabled = false;

            // Luz de estúdio: principal quente, preenchimento frio do outro lado e contraluz.
            // Só a principal é por pixel: no forward cada luz por pixel a mais é outra passada desenhando o
            // ônibus inteiro (~1 milhão de triângulos). As outras entram como luz "não importante",
            // somadas na mesma passada.
            _luzesVitrine = new GameObject("LuzesVitrine") { layer = Camada };
            _luzesVitrine.transform.SetParent(_palco.transform, false);
            Luz(_luzesVitrine.transform, "Principal", new Vector3(32, -38, 0), new Color(1f, 0.96f, 0.9f), 1.15f, true);
            Luz(_luzesVitrine.transform, "Preenchimento", new Vector3(18, 140, 0), new Color(0.78f, 0.86f, 1f), 0.5f, false);
            Luz(_luzesVitrine.transform, "Contraluz", new Vector3(8, 200, 0), new Color(1f, 1f, 1f), 0.55f, false);
            Luz(_luzesVitrine.transform, "Baixo", new Vector3(-30, 60, 0), new Color(0.9f, 0.9f, 1f), 0.2f, false);
            CriarEstudio();

            // Sombra de contato: elipse escura e suave sob o ônibus (gira junto).
            var s = GameObject.CreatePrimitive(PrimitiveType.Quad);
            s.layer = Camada;
            UnityEngine.Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(_pivo, false);
            s.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var mr = s.GetComponent<MeshRenderer>();
            var ms = new Material(_transparente) { color = new Color(0, 0, 0, 0.75f), mainTexture = TexturaSombra() };
            mr.sharedMaterial = ms;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            _sombra = s.transform;
        }

        static Light Luz(Transform pai, string nome, Vector3 rot, Color cor, float intens, bool porPixel)
        {
            var go = new GameObject(nome) { layer = Camada };
            go.transform.SetParent(pai, false);
            go.transform.rotation = Quaternion.Euler(rot);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = cor; l.intensity = intens;
            l.cullingMask = 1 << Camada;
            l.shadows = LightShadows.None;
            l.renderMode = porPixel ? LightRenderMode.ForcePixel : LightRenderMode.ForceVertex;
            return l;
        }

        static Texture2D TexturaSombra()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Il2CppStructArray<Color32>(n * n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(255 * Mathf.Clamp01(1f - Mathf.SmoothStep(0.55f, 1f, d)));
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply(false, true);
            return t;
        }

        static void Enquadrar(Montado m)
        {
            float r = Mathf.Max(1f, m.raio);
            float dist = r / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
            var alvo = _palco.transform.position + new Vector3(0, m.altura * 0.42f, 0);
            _cam.transform.position = alvo + Quaternion.Euler(11f, 0, 0) * new Vector3(0, 0, -dist);
            _cam.transform.LookAt(alvo);
            // Sombra do tamanho da pegada do ônibus (largura x comprimento, com folga).
            var b = m.raio * 2f;
            _sombra.localScale = new Vector3(Mathf.Max(3.2f, b * 0.22f), b * 1.02f, 1f);
            _sombra.localPosition = new Vector3(0, 0.01f, 0);
        }

        // ---------- giro ----------

        static float _girando, _velocidade = 140f, _surgiu;
        static bool _arrastando;
        static float _ultimoMouse, _soltouEm = -10f;
        const float GiroLento = 9f;

        static void Girar(RectTransform area)
        {
            if (_atual == null) return;
            float dt = Anim.Dt;
            bool dentro = area != null && RectTransformUtility.RectangleContainsScreenPoint(area, Input.mousePosition, null);
            if (Input.GetMouseButtonDown(0) && dentro) { _arrastando = true; _ultimoMouse = Input.mousePosition.x; }
            if (_arrastando)
            {
                if (!Input.GetMouseButton(0)) { _arrastando = false; _soltouEm = Time.unscaledTime; }
                else
                {
                    float dx = Input.mousePosition.x - _ultimoMouse;
                    _ultimoMouse = Input.mousePosition.x;
                    _velocidade = -dx * 0.35f / Mathf.Max(dt, 1e-4f) * 0.5f + _velocidade * 0.5f;
                    _girando -= dx * 0.35f;
                }
            }
            else
            {
                // Inércia do arraste (ou do giro de entrada) voltando ao giro lento.
                float alvo = Time.unscaledTime - _soltouEm < 2.5f ? 0f : GiroLento;
                _velocidade = Mathf.Lerp(_velocidade, alvo, 1f - Mathf.Exp(-dt / 0.6f));
                _girando += _velocidade * dt;
            }
            // Pose de 3/4 da frente (30°) mais o giro.
            _pivo.localRotation = Quaternion.Euler(0, 150f + _girando, 0);
        }

        internal static float SurgiuHa => Time.unscaledTime - _surgiu;
        internal static float SkinTrocadaEm = -10f;

        static void Resolucao(RectTransform area)
        {
            var r = area.rect;
            var cv = area.GetComponentInParent<Canvas>();
            float esc = cv != null ? cv.rootCanvas.scaleFactor : 1f; // não a escala da área: ela é animada
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width * esc), 64, 2048);
            int h = Mathf.Clamp(Mathf.RoundToInt(r.height * esc), 64, 2048);
            if (_rt != null && Mathf.Abs(_rt.width - w) < 8 && Mathf.Abs(_rt.height - h) < 8) return;
            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); UnityEngine.Object.Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4, name = "PBSLoaderCommonInterface_Garagem" };
            _rt.Create();
            _cam.targetTexture = _rt;
            _cam.aspect = w / (float)h;
        }
    }
}
