using System.Collections.Generic;
using MelonLoader;
using PBSLoaderCommonUtils;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Avisos e falhas do veículo (Avisos) no canto inferior direito, no jeito do BeamNG: linhas
    // pequenas e translúcidas, a mais nova entra por baixo e a lista sobe. Nada pisca nem recebe clique.
    //   cor da barra   falha vermelha, aviso âmbar, informação cinza
    //   entrada        desliza da direita e acende (0,25 s)
    //   depois de 8 s  condições que continuam valendo esmaecem (falhas menos), para não ficar gritando
    //   saída          apaga e a linha encolhe (0,3 s)
    internal static class HudAvisos
    {
        internal static MelonPreferences_Entry<bool> Pref;
        internal static MelonPreferences_Entry<float> Margem;

        internal static void CriarPreferencias(MelonPreferences_Category cat)
        {
            Pref = cat.CreateEntry("AvisosNaTela", true, "Avisos do veículo na tela",
                "Avisos e falhas do veículo (ar, portas e o que outros mods informarem) no canto inferior direito");
            Margem = cat.CreateEntry("AvisosMargemInferior", 24f, "Altura dos avisos",
                "Distância do painel de condução e dos avisos até a borda de baixo da tela (em 1080p), para não cobrir o painel do jogo");
        }

        // Card: barra colorida à esquerda, ícone, título (quebra linha) e detalhe. A altura vem do texto, para
        // nada vazar do card.
        const float Largura = 400f, Maximo = 6;
        const int PadEsq = 18, PadDir = 16, PadCima = 11, PadBaixo = 12, Icone = 22, Espaco = 12;
        const float LarguraTexto = Largura - PadEsq - PadDir - Icone - Espaco;
        const float Entrada = 0.25f, Saida = 0.3f, Esmaecer = 8f;

        sealed class Linha
        {
            internal Alerta A;
            internal int Versao = -1;
            internal GameObject Go;
            internal RectTransform Conteudo;
            internal LayoutElement Tam;
            internal CanvasGroup Grupo;
            internal Image Barra;
            internal Text Icone, Titulo, Detalhe;
            internal float Nasceu, SaindoDesde = -1f, Altura, Alfa;
        }

        static GameObject _canvas;
        static RectTransform _lista;
        static readonly List<Linha> _linhas = new();

        static void Construir()
        {
            if (_canvas != null && !_canvas.WasCollected) return;
            Theme.Carregar();
            _canvas = new GameObject("PBSLoaderCommonInterface_Avisos") { layer = 5 };
            Object.DontDestroyOnLoad(_canvas);
            var cv = _canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 300; // acima da HUD do jogo, abaixo do menu lateral (500) e da tela de mods (900)
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            // Sem GraphicRaycaster: os avisos nunca roubam clique.

            _lista = Kit.Novo("Lista", _canvas.transform);
            Kit.Ancorar(_lista, new Vector2(1, 0), new Vector2(-24, Margem.Value), new Vector2(Largura, 0));
            var g = Kit.Coluna(_lista, 6, 0, TextAnchor.LowerRight);
            g.childControlHeight = true; g.childForceExpandHeight = false;
            var f = _lista.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _linhas.Clear();
        }

        // Por frame. "mostrar": a UI nova está ligada.
        internal static void Atualizar(bool mostrar)
        {
            bool ligado = mostrar && (Pref == null || Pref.Value) && Veiculo.Existe;
            if (!ligado)
            {
                if (_canvas != null && !_canvas.WasCollected && _canvas.activeSelf) _canvas.SetActive(false);
                return;
            }
            Construir();
            if (!_canvas.activeSelf) _canvas.SetActive(true);
            _lista.anchoredPosition = new Vector2(-24 - TelaAlt.LarguraOcupada, Margem.Value + HudPainel.Ocupa); // em cima do painel de condução

            float agora = Time.unscaledTime;
            var ativos = Avisos.Ativos;

            // Entradas: aviso novo (ou notificação renovada, que vem como outro objeto) ganha linha nova.
            foreach (var a in ativos)
            {
                var l = _linhas.Find(x => x.SaindoDesde < 0f && x.A.Chave == a.Chave);
                if (l == null) _linhas.Add(Criar(a, agora));
                else if (!ReferenceEquals(l.A, a)) { l.A = a; l.Versao = -1; l.Nasceu = agora; }
            }
            // Saídas: o que não está mais ativo começa a sumir.
            foreach (var l in _linhas)
                if (l.SaindoDesde < 0f && !Contem(ativos, l.A)) l.SaindoDesde = agora;

            int visiveis = 0;
            for (int i = _linhas.Count - 1; i >= 0; i--)
            {
                var l = _linhas[i];
                if (l.Go == null || l.Go.WasCollected) { _linhas.RemoveAt(i); continue; }
                if (l.Versao != l.A.Versao) Redesenhar(l);

                float ent = Suave(Mathf.Clamp01((agora - l.Nasceu) / Entrada));
                float sai = l.SaindoDesde < 0f ? 0f : Mathf.Clamp01((agora - l.SaindoDesde) / Saida);
                if (sai >= 1f) { Object.Destroy(l.Go); _linhas.RemoveAt(i); continue; }

                // As mais novas ficam; passando do máximo, as mais antigas somem da tela (continuam ativas).
                bool cabe = l.SaindoDesde >= 0f || visiveis < Maximo;
                if (l.SaindoDesde < 0f) visiveis++;
                if (l.Go.activeSelf != cabe) l.Go.SetActive(cabe);

                float idade = agora - l.A.Desde;
                float alvo = 1f;
                if (l.A.Condicao && idade > Esmaecer) alvo = l.A.Gravidade == Gravidade.Falha ? 0.8f : 0.5f;
                l.Alfa += (alvo - l.Alfa) * Mathf.Min(1f, Time.unscaledDeltaTime / 0.6f);
                l.Grupo.alpha = l.Alfa * ent * (1f - sai);
                l.Conteudo.anchoredPosition = new Vector2((1f - ent) * 40f + Suave(sai) * 24f, 0f);
                l.Tam.preferredHeight = l.Tam.minHeight = l.Altura * (1f - Suave(sai));
            }
        }

        static bool Contem(IReadOnlyList<Alerta> lista, Alerta a)
        {
            for (int i = 0; i < lista.Count; i++) if (ReferenceEquals(lista[i], a)) return true;
            return false;
        }

        static float Suave(float t) => t * t * (3f - 2f * t);

        static Linha Criar(Alerta a, float agora)
        {
            var l = new Linha { A = a, Nasceu = agora, Alfa = 1f };
            var rt = Kit.Novo("Aviso_" + a.Chave, _lista);
            rt.SetAsLastSibling(); // a mais nova embaixo
            l.Go = rt.gameObject;
            l.Tam = Kit.Tam(rt, Largura, 56);
            l.Grupo = rt.gameObject.AddComponent<CanvasGroup>();
            l.Grupo.alpha = 0f;
            l.Grupo.blocksRaycasts = false; l.Grupo.interactable = false;

            l.Conteudo = Kit.Encher(Kit.Novo("Conteudo", rt));
            Kit.Fundo(l.Conteudo, Theme.Solido, Theme.Alfa(Theme.Painel, 0.84f)).raycastTarget = false;
            l.Barra = Kit.Marca(l.Conteudo, Theme.TextoDim, 4f);
            var hl = Kit.Linha(l.Conteudo, Espaco, 0, TextAnchor.UpperLeft);
            Kit.Pad(hl, PadEsq, PadDir, PadCima, PadBaixo);
            l.Icone = Kit.Icone(l.Conteudo, Ico.Info, 20, Theme.TextoDim);
            Kit.Tam(l.Icone, Icone, 22);
            var col = Kit.Novo("Textos", l.Conteudo);
            Kit.Coluna(col, 3, 0, TextAnchor.UpperLeft);
            Kit.Tam(col, LarguraTexto, -1);
            l.Titulo = Kit.Texto(col, "", 16, Theme.Texto, true);
            l.Titulo.horizontalOverflow = HorizontalWrapMode.Wrap;
            l.Titulo.verticalOverflow = VerticalWrapMode.Overflow;
            l.Detalhe = Kit.Texto(col, "", 14, Theme.TextoDim);
            l.Detalhe.font = Theme.TextoCorrido;
            l.Detalhe.horizontalOverflow = HorizontalWrapMode.Wrap;
            l.Detalhe.verticalOverflow = VerticalWrapMode.Overflow;
            Redesenhar(l);
            return l;
        }

        static float AlturaTexto(Text t, string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            var gs = t.GetGenerationSettings(new Vector2(LarguraTexto, 0f));
            gs.horizontalOverflow = HorizontalWrapMode.Wrap;
            gs.verticalOverflow = VerticalWrapMode.Overflow;
            gs.scaleFactor = 1f; // altura em unidades do canvas (1080p), sem a escala da tela
            return Mathf.Ceil(t.cachedTextGeneratorForLayout.GetPreferredHeight(s, gs)) + 1f;
        }

        static void Redesenhar(Linha l)
        {
            var a = l.A;
            l.Versao = a.Versao;
            var cor = a.Gravidade == Gravidade.Falha ? Theme.Perigo : a.Gravidade == Gravidade.Aviso ? Theme.Aviso : Theme.TextoDim;
            l.Barra.color = cor;
            l.Icone.text = a.Gravidade == Gravidade.Info ? Ico.Info : Ico.Alerta;
            l.Icone.color = cor;
            l.Titulo.text = a.Titulo ?? "";
            l.Detalhe.text = a.Detalhe ?? "";
            bool temDetalhe = !string.IsNullOrEmpty(a.Detalhe);
            l.Detalhe.gameObject.SetActive(temDetalhe);
            // Altura pelo texto quebrado na largura do card (título e detalhe podem ter várias linhas).
            float ht = AlturaTexto(l.Titulo, l.Titulo.text), hd = temDetalhe ? AlturaTexto(l.Detalhe, l.Detalhe.text) : 0f;
            Kit.Tam(l.Titulo, LarguraTexto, ht);
            if (temDetalhe) Kit.Tam(l.Detalhe, LarguraTexto, hd);
            l.Altura = Mathf.Max(PadCima + PadBaixo + 22f, PadCima + PadBaixo + ht + (temDetalhe ? 3f + hd : 0f));
            l.Tam.preferredHeight = l.Tam.minHeight = l.Altura;
            // Piorou para falha: volta ao destaque cheio.
            if (a.Gravidade == Gravidade.Falha) l.Alfa = 1f;
        }
    }
}
