using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Checagem de layout da UI nova, medida na resolução real (não no papel):
    //  - texto cortado (precisa de mais altura do que tem) ou passando da largura sem quebrar linha;
    //  - elemento visível saindo da tela;
    //  - alvo de clique menor que 32 unidades de canvas.
    internal static class Auditoria
    {
        const float AlvoMinimo = 32f;

        internal static List<string> Verificar(RectTransform raiz)
        {
            var problemas = new List<string>();
            if (raiz == null || !raiz.gameObject.activeInHierarchy) return problemas;
            // Mede depois do layout assentar; senão elementos recém-ativados/alterados aparecem
            // com o tamanho padrão de um RectTransform novo (100x100) e geram falsos positivos.
            Canvas.ForceUpdateCanvases();

            float sw = Screen.width, sh = Screen.height;
            float escalaCanvas = Mathf.Max(0.0001f, raiz.lossyScale.x); // px de tela por unidade de canvas

            foreach (var rt in raiz.GetComponentsInChildren<RectTransform>(false))
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                var r = rt.rect;
                if (r.width <= 0.5f || r.height <= 0.5f) continue;

                var p = rt.position;
                var s = rt.lossyScale;
                float xMin = p.x + r.xMin * s.x, xMax = p.x + r.xMax * s.x;
                float yMin = p.y + r.yMin * s.y, yMax = p.y + r.yMax * s.y;
                bool temGrafico = rt.GetComponent<Graphic>() != null;

                if (temGrafico && (xMin < -2 || yMin < -2 || xMax > sw + 2 || yMax > sh + 2))
                    problemas.Add($"fora da tela: {Caminho(rt, raiz)} (x {xMin:0}..{xMax:0}, y {yMin:0}..{yMax:0}, tela {sw}x{sh})");

                var t = rt.GetComponent<Text>();
                if (t != null && t.enabled && !string.IsNullOrEmpty(t.text) && t.font != Theme.FonteIcones)
                {
                    if (t.horizontalOverflow == HorizontalWrapMode.Overflow && t.preferredWidth > r.width + 2)
                        problemas.Add($"texto passa da largura: {Caminho(rt, raiz)} \"{Corta(t.text)}\" precisa {t.preferredWidth:0}, tem {r.width:0}");
                    else if (t.verticalOverflow == VerticalWrapMode.Truncate && t.preferredHeight > r.height + 2)
                        problemas.Add($"texto cortado: {Caminho(rt, raiz)} \"{Corta(t.text)}\" precisa {t.preferredHeight:0} de altura, tem {r.height:0}");
                }

                var sel = rt.GetComponent<Selectable>();
                if (sel != null && sel.enabled)
                {
                    float w = (xMax - xMin) / escalaCanvas, h = (yMax - yMin) / escalaCanvas;
                    if (w < AlvoMinimo || h < AlvoMinimo)
                    {
                        var lbl = rt.GetComponentInChildren<Text>();
                        problemas.Add($"alvo de clique pequeno: {Caminho(rt, raiz)} \"{(lbl != null ? Corta(lbl.text) : "")}\" ({w:0}x{h:0})");
                    }
                }
            }
            return problemas;
        }

        internal static string Gravar(string tela, List<string> problemas)
        {
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PBSLoaderCommonInterface");
            Directory.CreateDirectory(dir);
            var arq = Path.Combine(dir, $"auditoria_{tela}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"Auditoria de layout - {tela} - {System.DateTime.Now:HH:mm:ss} - tela {Screen.width}x{Screen.height}");
            foreach (var p in problemas) sb.AppendLine(p);
            File.AppendAllText(arq, sb.ToString() + "\n", Encoding.UTF8);
            return arq;
        }

        static string Caminho(Transform t, Transform raiz)
        {
            var partes = new List<string>();
            for (var x = t; x != null && x != raiz; x = x.parent) partes.Add(x.name);
            partes.Reverse();
            return string.Join("/", partes);
        }

        static string Corta(string s) => (s.Length > 30 ? s.Substring(0, 30) + "…" : s).Replace("\n", " ");
    }
}
