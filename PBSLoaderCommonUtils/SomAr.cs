using System;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBSLoaderCommonUtils
{
    // Sons do ar comprimido, a partir de uma gravação de ar comprimido (Sons/ar_comprimido.wav, embutida).
    //   Escape  freios de serviço e de molas soltando: volume pela vazão de escape
    //   Purga   secador soltando o ar quando o governador corta: mais agudo e forte
    //   Alarme  campainha de pressão baixa (sintetizada), só dentro do veículo
    // Cada voz toca o arquivo do começo (o ataque da válvula) e cruza para um laço feito do meio da gravação.
    internal static class SomAr
    {
        sealed class Voz
        {
            internal GameObject Go;
            internal AudioSource Inicio, Laco;
            internal float G;
            internal bool Tocando;
        }

        static AudioClip _inicio, _laco, _alarme;
        static float _durInicio;
        static Voz _escape, _purga;
        static AudioSource _campainha;
        static int _geracao = -1;
        static bool _falhou;

        static void Carregar()
        {
            using var s = typeof(SomAr).Assembly.GetManifestResourceStream("PBSLoaderCommonUtils.ar_comprimido.wav");
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var x = LerWav(ms.ToArray(), out int taxa);
            int fim = x.Length;
            while (fim > taxa / 2 && Math.Abs(x[fim - 1]) < 0.003f) fim--;
            int rampa = taxa * 15 / 1000;
            var inicio = new float[fim];
            Array.Copy(x, inicio, fim);
            for (int i = 0; i < rampa && i < fim; i++) inicio[fim - 1 - i] *= (float)i / rampa;
            _durInicio = (float)fim / taxa;

            int a = Math.Min(fim / 4, (int)(0.6f * taxa)), b = fim - (int)(0.2f * taxa);
            int cruz = Math.Min((int)(0.5f * taxa), (b - a) / 3);
            int n = b - a - cruz;
            var laco = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = x[a + i];
                if (i < cruz)
                {
                    double t = (double)i / cruz * Math.PI / 2;
                    v = (float)(v * Math.Sin(t) + x[a + n + i] * Math.Cos(t));
                }
                laco[i] = v;
            }
            _inicio = Clip("PBSLoaderCommonUtils_ar_inicio", inicio, taxa);
            _laco = Clip("PBSLoaderCommonUtils_ar_laco", laco, taxa);

            // Campainha: 470 Hz com harmônicos ímpares (zumbido de buzzer), 1 s em laço sem emenda.
            const int t2 = 44100;
            var z = new float[t2];
            for (int i = 0; i < t2; i++)
            {
                double ph = 2 * Math.PI * 470.0 * i / t2;
                z[i] = (float)(0.5 * Math.Sin(ph) + 0.22 * Math.Sin(3 * ph) + 0.1 * Math.Sin(5 * ph) + 0.05 * Math.Sin(7 * ph)) * 0.6f;
            }
            _alarme = Clip("PBSLoaderCommonUtils_alarme", z, t2);
        }

        static AudioClip Clip(string nome, float[] dados, int taxa)
        {
            var c = AudioClip.Create(nome, dados.Length, 1, taxa, false);
            c.SetData(new Il2CppStructArray<float>(dados), 0);
            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(c);
            return c;
        }

        static float[] LerWav(byte[] b, out int taxa)
        {
            if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
                throw new InvalidDataException("não é WAV");
            int p = 12, formato = 0, canais = 0, bits = 0; taxa = 0;
            while (p + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, p, 4);
                int tam = BitConverter.ToInt32(b, p + 4);
                int d = p + 8;
                if (id == "fmt ")
                {
                    formato = BitConverter.ToUInt16(b, d); canais = BitConverter.ToUInt16(b, d + 2);
                    taxa = BitConverter.ToInt32(b, d + 4); bits = BitConverter.ToUInt16(b, d + 14);
                    if (formato == 0xFFFE && tam >= 26) formato = BitConverter.ToUInt16(b, d + 24);
                }
                else if (id == "data")
                {
                    tam = Math.Min(tam, b.Length - d);
                    int bpa = bits / 8, quadro = bpa * canais, n = tam / quadro;
                    var x = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float s = 0f;
                        for (int c = 0; c < canais; c++)
                        {
                            int k = d + i * quadro + c * bpa;
                            s += formato == 3 && bits == 32 ? BitConverter.ToSingle(b, k)
                               : bits == 16 ? BitConverter.ToInt16(b, k) / 32768f
                               : bits == 24 ? (b[k] | (b[k + 1] << 8) | ((sbyte)b[k + 2] << 16)) / 8388608f
                               : bits == 8 ? (b[k] - 128) / 128f
                               : BitConverter.ToInt32(b, k) / 2147483648f;
                        }
                        x[i] = s / canais;
                    }
                    return x;
                }
                p = d + tam + (tam & 1);
            }
            throw new InvalidDataException("WAV sem dados");
        }

        static void Montar()
        {
            Desmontar();
            _geracao = Veiculo.Geracao;
            var raiz = Veiculo.Raiz;
            if (raiz == null) return;
            UnityEngine.Audio.AudioMixerGroup mixer = null;
            foreach (var s in raiz.GetComponentsInChildren<AudioSource>(true))
                if (s.outputAudioMixerGroup != null) { mixer = s.outputAudioMixerGroup; break; }
            var cx = Veiculo.Caixa;
            var baixo = new Vector3(cx.center.x, cx.min.y + 0.4f, cx.center.z);
            _escape = Criar("PBSLoaderCommonUtils_ArEscape", raiz, baixo, mixer, 1.05f, 0f);
            _purga = Criar("PBSLoaderCommonUtils_ArPurga", raiz, new Vector3(cx.center.x, cx.min.y + 0.5f, cx.max.z - 1.5f), mixer, 1.2f, 0f);

            var go = new GameObject("PBSLoaderCommonUtils_Alarme");
            go.transform.SetParent(raiz, false);
            go.transform.localPosition = new Vector3(cx.center.x, cx.center.y, cx.max.z - 1f);
            _campainha = go.AddComponent<AudioSource>();
            _campainha.clip = _alarme; _campainha.loop = true; _campainha.playOnAwake = false;
            _campainha.spatialBlend = 0.6f; _campainha.minDistance = 2f; _campainha.maxDistance = 15f;
            _campainha.volume = 0f; _campainha.priority = 60;
            if (mixer != null) _campainha.outputAudioMixerGroup = mixer;
        }

        static Voz Criar(string nome, Transform pai, Vector3 local, UnityEngine.Audio.AudioMixerGroup mixer, float pitch, float passaBaixa)
        {
            var v = new Voz { Go = new GameObject(nome) };
            v.Go.transform.SetParent(pai, false);
            v.Go.transform.localPosition = local;
            v.Inicio = Fonte(v.Go, _inicio, false, mixer, pitch);
            v.Laco = Fonte(v.Go, _laco, true, mixer, pitch);
            if (passaBaixa > 0f) v.Go.AddComponent<AudioLowPassFilter>().cutoffFrequency = passaBaixa;
            return v;
        }

        static AudioSource Fonte(GameObject go, AudioClip clip, bool laco, UnityEngine.Audio.AudioMixerGroup mixer, float pitch)
        {
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = laco; s.playOnAwake = false;
            s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 4f; s.maxDistance = 120f; s.dopplerLevel = 0f;
            s.priority = 40; s.volume = 0f; s.pitch = pitch;
            if (mixer != null) s.outputAudioMixerGroup = mixer;
            return s;
        }

        static void Desmontar()
        {
            foreach (var v in new[] { _escape, _purga })
                if (v?.Go != null) try { Object.Destroy(v.Go); } catch { }
            if (_campainha != null) try { Object.Destroy(_campainha.gameObject); } catch { }
            _escape = _purga = null; _campainha = null;
        }

        internal static void Quadro()
        {
            if (_falhou) return;
            if (!Ar.Simulando) { if (_escape != null) Desmontar(); _geracao = -1; return; }
            try
            {
                if (_inicio == null) Carregar();
                if (_geracao != Veiculo.Geracao || _escape == null || _escape.Go == null || _escape.Go.WasCollected) Montar();
                if (_escape == null) return;
                float vol = Ar.VolumeSom.Value * (Veiculo.CameraDentro ? 0.55f : 1f);
                float dt = Time.deltaTime;
                // Escape: 30 L/s (um freio cheio soltando) = volume 1.
                float purga = Mathf.Min(1.4f, Mathf.Sqrt(Mathf.Max(0f, PurgaAgora()) / 3.5f));
                float esc = Mathf.Min(1.2f, Mathf.Sqrt(Mathf.Max(0f, Ar.Escape - PurgaAgora()) / 30f));
                Atualizar(_escape, esc < 0.05f ? 0f : esc * vol, dt);
                Atualizar(_purga, purga < 0.05f ? 0f : purga * vol, dt);

                bool tocar = Ar.Alarme && Veiculo.CameraDentro;
                float alvo = tocar ? Ar.VolumeAlarme.Value : 0f;
                _campainha.volume += (alvo - _campainha.volume) * Mathf.Min(1f, dt / 0.02f);
                if (tocar && !_campainha.isPlaying) _campainha.Play();
                else if (!tocar && _campainha.isPlaying && _campainha.volume < 0.002f) _campainha.Stop();
            }
            catch (Exception e)
            {
                _falhou = true;
                MelonLogger.Warning("[Ar] som desligado: " + e);
            }
        }

        // A purga é a parte do escape que veio do secador (Ar mantém o envelope; aqui só separamos as vozes).
        static float PurgaAgora() => Ar.PurgaEnvelope;

        static void Atualizar(Voz v, float alvo, float dt)
        {
            float tau = alvo > v.G ? 0.03f : 0.12f;
            v.G += (alvo - v.G) * (1f - Mathf.Exp(-dt / tau));
            if (!v.Tocando)
            {
                if (alvo <= 0.001f) return;
                v.Inicio.time = 0f; v.Inicio.Play();
                v.Laco.time = UnityEngine.Random.Range(0f, _laco.length * 0.8f); v.Laco.Play();
                v.Tocando = true;
            }
            float t = v.Inicio.isPlaying ? v.Inicio.time : _durInicio;
            float x0 = Mathf.Max(0.5f, _durInicio - 1.2f);
            float mix = Mathf.Clamp01((t - x0) / 1f);
            v.Inicio.volume = v.G * Mathf.Sqrt(1f - mix);
            v.Laco.volume = v.G * Mathf.Sqrt(mix);
            if (alvo <= 0.001f && v.G < 0.003f)
            {
                v.Inicio.Stop(); v.Laco.Stop();
                v.Inicio.volume = v.Laco.volume = 0f;
                v.G = 0f; v.Tocando = false;
            }
        }

        internal static string Diagnostico() =>
            _escape == null ? "som do ar: não montado" :
            $"som do ar: escape {_escape.G:0.00} ({_escape.Tocando}), purga {_purga.G:0.00} ({_purga.Tocando}), alarme {(_campainha != null && _campainha.isPlaying ? _campainha.volume.ToString("0.00") : "parado")}";
    }
}
