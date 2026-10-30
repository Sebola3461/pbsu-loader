using System;
using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBSLoaderCommonUtils.Midia
{
    /// <summary>
    /// Carrega texturas, sons e modelos de arquivos, pelos mesmos carregadores que o jogo usa nos mods de ônibus e mapa
    /// (então formatos, cache e o CarregamentoRapido valem igual). O que é carregado aqui não é descartado quando o
    /// jogo troca de ônibus (ele chama UnloadUnusedAssets): destrua você mesmo o que não precisar mais.
    /// Só na thread principal. Erros viram null e uma linha no log.
    /// </summary>
    public static class Carregar
    {
        /// <summary>PNG ou JPG (MepUtils.CarregaTextura). <paramref name="cache"/> usa a cache de texturas do jogo.</summary>
        public static Texture2D Textura(string caminho, bool cache = true)
        {
            if (!Existe(caminho, "textura")) return null;
            try
            {
                var t = MepUtils.CarregaTextura(caminho, cache);
                if (t != null) t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                return t;
            }
            catch (Exception e) { Sdk.Registrar(e, "Midia.Textura " + Path.GetFileName(caminho)); return null; }
        }

        /// <summary>Som (MepUtils.CarregaSom: os formatos que os mods de ônibus usam, como WAV e OGG).</summary>
        public static AudioClip Som(string caminho)
        {
            if (!Existe(caminho, "som")) return null;
            try
            {
                var c = MepUtils.CarregaSom(caminho);
                if (c != null) c.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                return c;
            }
            catch (Exception e) { Sdk.Registrar(e, "Midia.Som " + Path.GetFileName(caminho)); return null; }
        }

        /// <summary>
        /// Modelo .3ds (ou .c1, o formato protegido dos mods, aberto pelo próprio jogo), com materiais e texturas da mesma
        /// pasta, como o jogo carrega as peças de um ônibus. Volta um objeto novo na cena, na origem: posicione e
        /// pendure onde quiser.
        /// </summary>
        public static GameObject Modelo(string caminho)
        {
            if (!Existe(caminho, "modelo")) return null;
            try
            {
                return caminho.EndsWith(".c1", StringComparison.OrdinalIgnoreCase)
                    ? _3DSReader.Import3DS_C1(caminho, _3DSReader.tipoCarregamento.generico)
                    : _3DSReader.Import3DS(caminho, _3DSReader.tipoCarregamento.generico);
            }
            catch (Exception e) { Sdk.Registrar(e, "Midia.Modelo " + Path.GetFileName(caminho)); return null; }
        }

        static bool Existe(string caminho, string o)
        {
            if (!string.IsNullOrEmpty(caminho) && File.Exists(caminho)) return true;
            MelonLoader.MelonLogger.Warning($"[Midia] {o} não encontrado: {caminho}");
            return false;
        }
    }

    /// <summary>Um som tocando no mundo (criado por <see cref="Sons.Tocar"/>).</summary>
    public sealed class Som3D
    {
        public AudioSource Fonte { get; internal set; }
        /// <summary>Volume pedido, 0..1 (antes do abafamento dentro do ônibus).</summary>
        public float Volume { get; set; } = 1f;
        public float Pitch { get => Vivo ? Fonte.pitch : 1f; set { if (Vivo) Fonte.pitch = value; } }
        /// <summary>Fica mais baixo quando a câmera está dentro do ônibus (som de fora, como o motor).</summary>
        public bool AbafarDentro { get; set; }
        public bool Tocando => Vivo && Fonte.isPlaying;
        /// <summary>Para e destrói o som.</summary>
        public void Parar() { if (Vivo) Object.Destroy(Fonte.gameObject); Fonte = null; }

        internal bool Vivo => Fonte != null && !Fonte.WasCollected;
    }

    /// <summary>Sons 3D presos a um objeto (uma peça do ônibus, um ponto do mapa), no volume geral do jogo.</summary>
    public static class Sons
    {
        static readonly List<Som3D> _ativos = new();

        /// <summary>
        /// Toca <paramref name="clip"/> num objeto novo, filho de <paramref name="pai"/> na posição local dada. Sem
        /// <paramref name="loop"/>, o som se destrói sozinho quando termina. Com <paramref name="pai"/> null, fica parado
        /// no mundo em <paramref name="posicao"/>.
        /// </summary>
        public static Som3D Tocar(AudioClip clip, Transform pai, Vector3 posicao = default, float volume = 1f, bool loop = false,
            float distanciaMinM = 2f, float distanciaMaxM = 80f)
        {
            if (clip == null) return null;
            var go = new GameObject("PBSU_som_" + clip.name);
            if (pai != null) { go.transform.SetParent(pai, false); go.transform.localPosition = posicao; }
            else go.transform.position = posicao;
            var a = go.AddComponent<AudioSource>();
            a.clip = clip; a.loop = loop; a.spatialBlend = 1f; a.dopplerLevel = 0f;
            a.rolloffMode = AudioRolloffMode.Logarithmic; a.minDistance = distanciaMinM; a.maxDistance = distanciaMaxM;
            var s = new Som3D { Fonte = a, Volume = volume };
            a.volume = volume;
            a.Play();
            _ativos.Add(s);
            return s;
        }

        /// <summary>Toca um arquivo de som de uma vez (carrega, toca e esquece).</summary>
        public static Som3D TocarArquivo(string caminho, Transform pai, Vector3 posicao = default, float volume = 1f)
            => Tocar(Carregar.Som(caminho), pai, posicao, volume);

        internal static void Quadro()
        {
            bool dentro = Veiculos.Veiculo.CameraDentro;
            for (int i = _ativos.Count - 1; i >= 0; i--)
            {
                var s = _ativos[i];
                if (!s.Vivo) { _ativos.RemoveAt(i); continue; }
                if (!s.Fonte.loop && !s.Fonte.isPlaying && s.Fonte.time <= 0f) { s.Parar(); _ativos.RemoveAt(i); continue; }
                s.Fonte.volume = Mathf.Clamp01(s.Volume) * (s.AbafarDentro && dentro ? 0.55f : 1f);
            }
        }
    }
}
