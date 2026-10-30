using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Ferramenta de desenvolvimento: grava a hierarquia de todos os Canvas da cena num .txt,
    // com tamanho, sprite, cor e texto de cada elemento. Serve para refinar o tema tela por tela.
    internal static class UiDumper
    {
        internal static string Despejar(string sufixo)
        {
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonInterface");
            Directory.CreateDirectory(dir);
            var cena = SceneManager.GetActiveScene().name;
            var arq = Path.Combine(dir, $"ui_{Seguro(cena)}_{sufixo}_{System.DateTime.Now:HHmmss}.txt");

            var sb = new StringBuilder();
            sb.AppendLine($"Cena: {cena}   Resolução: {Screen.width}x{Screen.height}");
            foreach (var cv in Object.FindObjectsOfType<Canvas>(true))
            {
                if (cv == null || !cv.isRootCanvas) continue;
                var scaler = cv.GetComponent<CanvasScaler>();
                sb.AppendLine();
                sb.AppendLine($"=== CANVAS {cv.name}  modo={cv.renderMode}  ordem={cv.sortingOrder}" +
                              (scaler != null ? $"  scaler={scaler.uiScaleMode} ref={scaler.referenceResolution}" : ""));
                Nodo(cv.transform, 0, sb);
            }
            File.WriteAllText(arq, sb.ToString(), Encoding.UTF8);
            return arq;
        }

        static void Nodo(Transform t, int nivel, StringBuilder sb)
        {
            var rt = t.TryCast<RectTransform>();
            sb.Append(' ', nivel * 2);
            sb.Append(t.gameObject.activeSelf ? "+ " : "- ").Append(t.name);
            if (rt != null) sb.Append($"  [{rt.rect.width:0}x{rt.rect.height:0}]");

            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var img = c.TryCast<Image>();
                var txt = c.TryCast<Text>();
                var raw = c.TryCast<RawImage>();
                if (img != null)
                    sb.Append($"  Image(sprite={(img.sprite != null ? img.sprite.name : "null")}, tipo={img.type}, cor={Hex(img.color)})");
                else if (txt != null)
                    sb.Append($"  Text(\"{Corta(txt.text)}\", fonte={(txt.font != null ? txt.font.name : "null")}, {txt.fontSize}px, cor={Hex(txt.color)})");
                else if (raw != null)
                    sb.Append($"  RawImage(tex={(raw.texture != null ? raw.texture.name : "null")}, cor={Hex(raw.color)})");
                else
                {
                    var n = c.GetIl2CppType().Name;
                    if (n != "RectTransform" && n != "Transform" && n != "CanvasRenderer") sb.Append("  ").Append(n);
                }
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++) Nodo(t.GetChild(i), nivel + 1, sb);
        }

        // ColorUtility.ToHtmlStringRGBA foi removido do build (stripping).
        static string Hex(Color c)
        {
            Color32 k = c;
            return $"#{k.r:X2}{k.g:X2}{k.b:X2}{k.a:X2}";
        }
        static string Corta(string s) => s == null ? "" : (s.Length > 40 ? s.Substring(0, 40) + "…" : s).Replace("\n", "\\n");
        static string Seguro(string s) { foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_'); return s; }
    }
}
