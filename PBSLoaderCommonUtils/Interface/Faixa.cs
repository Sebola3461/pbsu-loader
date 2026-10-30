using System;
using System.Globalization;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonUtils.Interface
{
    /// <summary>
    /// Pressão do reservatório na faixa do jogo que aparece embaixo, no centro, ao segurar Alt (FPSDisplayNovo: FPS e
    /// velocidade). Assim quem joga sem a PBSLoaderCommonInterface também vê o ar. É uma cópia do texto da velocidade, posta ao lado
    /// dele: herda fonte, tamanho e cor, e aparece e some junto com a faixa.
    /// </summary>
    internal static class Faixa
    {
        internal static MelonPreferences_Entry<bool> Mostrar;

        static IntPtr _dono;      // FPSDisplayNovo de onde copiamos (o jogo recria a faixa ao trocar de cena)
        static Text _numero, _unidade;
        static Color _corNumero, _corUnidade;
        static float _proxima;

        static readonly Color Vermelho = new(1f, 0.28f, 0.22f);

        internal static void CriarPreferencias(MelonPreferences_Category c)
        {
            Mostrar = c.CreateEntry("PressaoNaFaixa", true, "Pressão do ar na faixa do Alt",
                "Mostra a pressão do reservatório no meio da faixa que aparece embaixo ao segurar Alt, entre a velocidade e o FPS");
        }

        /// <summary>Texto que a faixa mostra agora (null se não está na tela). Para o teste.</summary>
        internal static string TextoAtual => _numero != null && _numero.gameObject.activeInHierarchy ? _numero.text + " " + _unidade.text : null;

        internal static void Quadro()
        {
            var f = Il2Cpp.FPSDisplayNovo.instance;
            if (f == null || f.txtVelocidade == null) return;
            bool quer = Mostrar.Value && Ar.Simulando;
            if (_numero == null || _unidade == null || _dono != f.Pointer)
            {
                if (!quer) return;
                Criar(f);
            }
            if (_numero.gameObject.activeSelf != quer)
            {
                _numero.gameObject.SetActive(quer);
                _unidade.gameObject.SetActive(quer);
            }
            if (!quer || Time.unscaledTime < _proxima) return;
            _proxima = Time.unscaledTime + 0.2f;
            _numero.text = Ar.Pressao.ToString("0.0", CultureInfo.InvariantCulture);
            _numero.color = Ar.Alarme ? Vermelho : _corNumero;
            _unidade.color = Ar.Alarme ? Vermelho : _corUnidade;
        }

        // A faixa (800 px) tem a velocidade à esquerda e o FPS à direita, cada um em dois textos: o número alinhado à
        // direita e a unidade à esquerda ("150" "km/h"). A pressão segue o mesmo par, no meio, que fica vazio.
        static void Criar(Il2Cpp.FPSDisplayNovo f)
        {
            var vel = f.txtVelocidade;
            _dono = f.Pointer;
            var pai = vel.transform.parent;
            Text kmh = null;
            for (int i = 0; i < pai.childCount; i++)
            {
                var t = pai.GetChild(i).GetComponent<Text>();
                if (t != null && t.text.Trim().ToLowerInvariant() == "km/h") { kmh = t; break; }
            }
            float y = vel.rectTransform.anchoredPosition.y;
            _numero = Copiar(vel, "PBSLoaderCommonUtils_Pressao", TextAnchor.MiddleRight, 60f, new Vector2(4f - 30f, y));
            _unidade = Copiar(kmh ?? vel, "PBSLoaderCommonUtils_PressaoUnidade", TextAnchor.MiddleLeft, 40f, new Vector2(8f + 20f, y));
            _unidade.text = "bar";
            _corNumero = vel.color;
            _corUnidade = (kmh ?? vel).color;
            _proxima = 0f;
            Despejar(f);
        }

        static Text Copiar(Text modelo, string nome, TextAnchor alinhamento, float largura, Vector2 pos)
        {
            var go = UnityEngine.Object.Instantiate(modelo.gameObject, modelo.transform.parent);
            go.name = nome;
            // A cópia leva os filhos do modelo, se houver: só o texto interessa.
            for (int i = go.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
            var t = go.GetComponent<Text>();
            t.alignment = alinhamento;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(largura, rt.sizeDelta.y);
            rt.anchoredPosition = pos;
            return t;
        }

        // Hierarquia da faixa em UserData/PBSLoaderCommonUtils/faixa.txt, para ajustar o encaixe se o jogo mudar.
        static void Despejar(Il2Cpp.FPSDisplayNovo f)
        {
            try
            {
                var sb = new StringBuilder();
                var raiz = f.canvasFPSNovo != null ? f.canvasFPSNovo.transform : f.txtVelocidade.transform.parent;
                Despejar(raiz, 0, sb);
                var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonUtils");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "faixa.txt"), sb.ToString());
            }
            catch (Exception e) { MelonLogger.Warning("[Faixa] " + e.Message); }
        }

        static void Despejar(Transform t, int nivel, StringBuilder sb)
        {
            sb.Append(' ', nivel * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inativo)");
            var rt = t.TryCast<RectTransform>();
            if (rt != null)
                sb.Append(CultureInfo.InvariantCulture, $"  pos {rt.anchoredPosition} tam {rt.rect.size} anc {rt.anchorMin}-{rt.anchorMax} piv {rt.pivot} esc {rt.localScale}");
            foreach (var c in t.GetComponents<Component>())
            {
                string tipo = c.GetIl2CppType().Name;
                if (tipo == "RectTransform" || tipo == "Transform") continue;
                sb.Append("  [").Append(tipo);
                var tx = c.TryCast<Text>();
                if (tx != null) sb.Append(CultureInfo.InvariantCulture, $" \"{tx.text}\" {tx.fontSize}px {tx.alignment} larg {tx.preferredWidth:0}");
                sb.Append(']');
            }
            sb.Append('\n');
            for (int i = 0; i < t.childCount; i++) Despejar(t.GetChild(i), nivel + 1, sb);
        }
    }
}
