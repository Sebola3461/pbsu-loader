using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace PBSLoaderCommonInterface
{
    // Sons da interface: efeitos de hover/clique/voltar/lista e a música dos menus.
    // Os arquivos ficam em UserData/PBSLoaderCommonInterface/sons e podem ser trocados sem recompilar
    // (mesmo nome; .wav ou .ogg). O build removeu o DownloadHandlerAudioClip, então a decodificação
    // é nossa: WAV lido aqui, OGG pelo NVorbis que o jogo usa no rádio; o resultado vai para
    // AudioClip.Create/SetData. A música é tocada em streaming (ver Musica).
    // As fontes ignoram o volume e a pausa do AudioListener do jogo: a interface tem volumes próprios.
    internal static class Som
    {
        internal enum Efeito { Hover, Confirmar, Cancelar, Lista }

        // Efeito -> nome do arquivo (sem extensão) e ganho relativo.
        static readonly Dictionary<Efeito, (string arquivo, float ganho)> Arquivos = new()
        {
            [Efeito.Hover] = ("hover", 0.6f),
            [Efeito.Confirmar] = ("menu_confirm", 0.8f),
            [Efeito.Cancelar] = ("menu_cancel", 0.8f),
            [Efeito.Lista] = ("dropdown", 0.8f),
        };
        const string ArquivoMusica = "musica_menu";
        const float IntervaloHover = 0.05f;   // passar o mouse por uma lista não vira metralhadora
        const float FadeMusica = 1.2f;        // segundos para a música entrar ou sair

        internal static MelonPreferences_Entry<bool> PrefEfeitos, PrefMusica;
        internal static MelonPreferences_Entry<float> PrefVolumeEfeitos, PrefVolumeMusica;

        static GameObject _go;
        static AudioSource _efeitos, _fonteMusica;
        static readonly Dictionary<Efeito, AudioClip> _clips = new();
        static Streaming _musica;
        static float _ultimoHover, _nivelMusica;
        static bool _querMusica;

        internal static string Pasta => Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonInterface", "sons");

        internal static void CriarPreferencias(MelonPreferences_Category cat)
        {
            PrefEfeitos = cat.CreateEntry("SonsEfeitos", true, "Efeitos sonoros da interface (hover, clique, voltar)");
            PrefVolumeEfeitos = cat.CreateEntry("SonsVolumeEfeitos", 0.7f, "Volume dos efeitos da interface (0 a 1)");
            PrefMusica = cat.CreateEntry("SonsMusica", true, "Música nos menus e no carregamento");
            PrefVolumeMusica = cat.CreateEntry("SonsVolumeMusica", 0.45f, "Volume da música dos menus (0 a 1)");
        }

        // Os sons padrão vêm dentro da DLL e vão para a pasta só quando faltam: quem trocou um som pelo seu não perde a troca.
        static void ExtrairPadroes()
        {
            var asm = typeof(Som).Assembly;
            const string prefixo = "PBSLoaderCommonInterface.sons.";
            foreach (var nome in asm.GetManifestResourceNames())
            {
                if (!nome.StartsWith(prefixo, StringComparison.Ordinal)) continue;
                var destino = Path.Combine(Pasta, nome.Substring(prefixo.Length));
                if (File.Exists(destino)) continue;
                try
                {
                    Directory.CreateDirectory(Pasta);
                    using var s = asm.GetManifestResourceStream(nome);
                    using var f = File.Create(destino);
                    s.CopyTo(f);
                }
                catch (Exception e) { MelonLogger.Warning($"[Som] não deu para criar {destino}: {e.Message}"); }
            }
        }

        internal static void Iniciar()
        {
            if (_go != null) return;
            ExtrairPadroes();
            _go = new GameObject("PBSLoaderCommonInterface_Som");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _efeitos = NovaFonte(false);
            _fonteMusica = NovaFonte(true);
            foreach (var kv in Arquivos)
            {
                try
                {
                    var clip = CarregarEfeito(kv.Value.arquivo);
                    if (clip != null) _clips[kv.Key] = clip;
                }
                catch (Exception e) { MelonLogger.Warning($"[Som] {kv.Value.arquivo}: {e.Message}"); }
            }
            try
            {
                var ogg = Path.Combine(Pasta, ArquivoMusica + ".ogg");
                if (File.Exists(ogg)) _musica = new Streaming(ogg, _fonteMusica);
                else MelonLogger.Msg($"[Som] {ArquivoMusica}.ogg não encontrado em {Pasta}: sem música");
            }
            catch (Exception e) { MelonLogger.Warning($"[Som] música: {e.Message}"); }
            MelonLogger.Msg($"[Som] {_clips.Count} efeitos carregados{(_musica != null ? ", música pronta" : "")}");
        }

        static AudioSource NovaFonte(bool loop)
        {
            var a = _go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = 0f;           // 2D
            a.ignoreListenerVolume = true; // o volume geral do jogo não abafa a interface
            a.ignoreListenerPause = true;  // toca com o jogo pausado
            a.priority = 0;
            return a;
        }

        // ---------- efeitos (curtos: decodificados inteiros) ----------

        static AudioClip CarregarEfeito(string nome)
        {
            var wav = Path.Combine(Pasta, nome + ".wav");
            var ogg = Path.Combine(Pasta, nome + ".ogg");
            float[] dados; int canais, taxa;
            if (File.Exists(wav)) (dados, canais, taxa) = LerWav(wav);
            else if (File.Exists(ogg)) (dados, canais, taxa) = LerOgg(ogg);
            else { MelonLogger.Msg($"[Som] '{nome}' não encontrado em {Pasta}: sem esse som"); return null; }

            var clip = AudioClip.Create(nome, dados.Length / canais, canais, taxa, false);
            clip.SetData(new Il2CppStructArray<float>(dados), 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return clip;
        }

        // WAV PCM de 8, 16, 24 ou 32 bits, ou float de 32 bits.
        static (float[] dados, int canais, int taxa) LerWav(string caminho)
        {
            using var br = new BinaryReader(File.OpenRead(caminho));
            if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("não é RIFF");
            br.ReadInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("não é WAVE");
            int formato = 0, canais = 0, taxa = 0, bits = 0;
            while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
            {
                var id = new string(br.ReadChars(4));
                int tam = br.ReadInt32();
                long fim = br.BaseStream.Position + tam + (tam & 1);
                if (id == "fmt ")
                {
                    formato = br.ReadInt16(); canais = br.ReadInt16(); taxa = br.ReadInt32();
                    br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                    if (formato == -2 && tam >= 40) { br.ReadInt16(); br.ReadInt16(); br.ReadInt32(); formato = br.ReadInt16(); } // extensible
                }
                else if (id == "data")
                {
                    var bytes = br.ReadBytes(tam);
                    int bps = bits / 8, n = bytes.Length / bps;
                    var d = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        int o = i * bps;
                        d[i] = formato == 3 && bits == 32 ? BitConverter.ToSingle(bytes, o)
                            : bits == 8 ? (bytes[o] - 128) / 128f
                            : bits == 16 ? BitConverter.ToInt16(bytes, o) / 32768f
                            : bits == 24 ? ((bytes[o] | bytes[o + 1] << 8 | (sbyte)bytes[o + 2] << 16) / 8388608f)
                            : BitConverter.ToInt32(bytes, o) / 2147483648f;
                    }
                    return (d, canais, taxa);
                }
                br.BaseStream.Position = fim;
            }
            throw new InvalidDataException("sem bloco data");
        }

        static (float[] dados, int canais, int taxa) LerOgg(string caminho)
        {
            var r = new Il2CppNVorbis.VorbisReader(caminho);
            try
            {
                var tudo = new List<float>();
                var buf = new Il2CppStructArray<float>(r.Channels * 4096);
                int n;
                while ((n = r.ReadSamples(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++) tudo.Add(buf[i]);
                return (tudo.ToArray(), r.Channels, r.SampleRate);
            }
            finally { r.Dispose(); }
        }

        // ---------- música (streaming) ----------

        // Música longa em streaming: um AudioClip curto em loop serve de buffer circular, e o trecho
        // logo à frente do ponto de reprodução é preenchido com o que o NVorbis decodifica (voltando ao
        // início do arquivo no fim, para o loop). A decodificação fica na thread principal (uma thread
        // ligada ao IL2CPP disputa as travas do GC do jogo e deixava tudo mais lento), em blocos de
        // ~23 ms de áudio e com orçamento de tempo por frame: o custo se espalha em frações de ms em vez
        // de picos, e depois de uma travada de troca de cena o atraso é recuperado aos poucos (os 4 s
        // decodificados à frente seguram a reprodução enquanto isso).
        sealed class Streaming
        {
            const float SegundosBuffer = 12f, SegundosAFrente = 4f;
            const int QuadrosBloco = 1024;
            const double OrcamentoMs = 1.5;

            readonly Il2CppNVorbis.VorbisReader _leitor;
            readonly AudioSource _fonte;
            readonly AudioClip _clip;
            readonly int _canais, _quadros, _taxa;
            readonly Il2CppStructArray<float> _buf;
            long _escrito, _voltas;
            int _ultimaPos;

            internal Streaming(string caminho, AudioSource fonte)
            {
                _leitor = new Il2CppNVorbis.VorbisReader(caminho);
                _fonte = fonte;
                _canais = _leitor.Channels;
                _taxa = _leitor.SampleRate;
                _quadros = (int)(_taxa * SegundosBuffer) / QuadrosBloco * QuadrosBloco; // múltiplo do bloco: nunca divide no fim
                _buf = new Il2CppStructArray<float>(QuadrosBloco * _canais);
                _clip = AudioClip.Create("musica_menu", _quadros, _canais, _taxa, false);
                _clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                fonte.clip = _clip;
                fonte.loop = true;
            }

            long Tocado
            {
                get
                {
                    int pos = _fonte.timeSamples;
                    if (pos < _ultimaPos) _voltas++;
                    _ultimaPos = pos;
                    return _voltas * _quadros + pos;
                }
            }

            internal void Tocar()
            {
                if (_fonte.isPlaying) return;
                _voltas = 0; _ultimaPos = 0; _escrito = 0;
                Preencher(0, 40.0); // o começo sai na hora (a música entra em fade)
                _fonte.timeSamples = 0;
                _fonte.Play();
            }

            internal void Parar() { if (_fonte.isPlaying) _fonte.Stop(); }

            internal void Atualizar()
            {
                if (!_fonte.isPlaying) return;
                Preencher(Tocado, OrcamentoMs);
            }

            void Preencher(long tocado, double orcamentoMs)
            {
                if (_escrito < tocado) _escrito = tocado / QuadrosBloco * QuadrosBloco; // travou mais que o buffer
                long alvo = tocado + (long)(_taxa * SegundosAFrente);
                var ini = System.Diagnostics.Stopwatch.GetTimestamp();
                double porMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
                while (_escrito < alvo && (System.Diagnostics.Stopwatch.GetTimestamp() - ini) / porMs < orcamentoMs)
                {
                    int lidos = 0, quer = QuadrosBloco * _canais, voltas = 0;
                    while (lidos < quer)
                    {
                        int n = _leitor.ReadSamples(_buf, lidos, quer - lidos);
                        if (n > 0) { lidos += n; continue; }
                        // fim do arquivo: volta ao início (loop); arquivo que não rende nada não trava o jogo
                        if (++voltas > 2) throw new InvalidDataException("o OGG não tem áudio decodificável");
                        _leitor.DecodedTime = Il2CppSystem.TimeSpan.Zero;
                    }
                    _clip.SetData(_buf, (int)(_escrito % _quadros));
                    _escrito += QuadrosBloco;
                }
            }
        }

        // Chamado a cada frame.
        internal static void Atualizar()
        {
            if (_fonteMusica == null) return;
            // Música: entra e sai em fade; para quando chega a zero.
            float alvo = _querMusica && PrefMusica.Value && _musica != null ? 1f : 0f;
            _nivelMusica = Mathf.MoveTowards(_nivelMusica, alvo, Time.unscaledDeltaTime / FadeMusica);
            _fonteMusica.volume = _nivelMusica * _nivelMusica * Mathf.Clamp01(PrefVolumeMusica.Value); // fade em curva
            if (_musica == null) return;
            try
            {
                if (_nivelMusica > 0f) { _musica.Tocar(); _musica.Atualizar(); }
                else _musica.Parar();
            }
            catch (Exception e) { MelonLogger.Warning($"[Som] música parada: {e.Message}"); _musica = null; }
        }

        internal static void Tocar(Efeito e)
        {
            if (_efeitos == null || PrefEfeitos == null || !PrefEfeitos.Value) return;
            if (!_clips.TryGetValue(e, out var clip) || clip == null) return;
            if (e == Efeito.Hover)
            {
                if (Time.unscaledTime - _ultimoHover < IntervaloHover) return;
                _ultimoHover = Time.unscaledTime;
            }
            _efeitos.PlayOneShot(clip, Arquivos[e].ganho * Mathf.Clamp01(PrefVolumeEfeitos.Value));
        }

        // A música toca enquanto alguma tela de menu (menu, seleções, carregamento) está aberta.
        internal static void Musica(bool tocar) => _querMusica = tocar;
    }
}
