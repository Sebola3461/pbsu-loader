using PBSLoaderCommonUtils.Conteudo;
using UnityEngine;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // A pergunta "rodar os scripts deste ônibus/mapa?" (PBSLoaderCommonUtils.Conteudo.Confirmacao), no visual da interface.
    // A CommonUtils só desenha a janela simples dela quando ninguém assume (DesenhadaPorOutroMod).
    internal static class PermissaoScripts
    {
        static GameObject _canvas, _modal;
        static Pacote _mostrado;

        internal static void Iniciar() => Confirmacao.DesenhadaPorOutroMod = true;

        internal static void Atualizar()
        {
            var p = Confirmacao.Atual;
            if (p == _mostrado) return;
            if (_modal != null && !_modal.WasCollected) Object.Destroy(_modal);
            _modal = null;
            _mostrado = p;
            if (p == null) { if (_canvas != null && !_canvas.WasCollected) _canvas.SetActive(false); return; }
            Construir();
            _canvas.SetActive(true);

            var card = Kit.Modal(_canvas.transform, 640, out _modal);
            Kit.Trilha(card, p.Origem == OrigemDoPacote.Onibus ? "Scripts deste ônibus" : "Scripts deste mapa", 12);
            var t = Kit.Texto(card, $"RODAR \"{p.Nome.ToUpperInvariant()}\"?", 24, Theme.Texto, true);
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            Kit.Paragrafo(card, $"Este {(p.Origem == OrigemDoPacote.Onibus ? "ônibus" : "mapa")} traz código próprio, de {p.Autor} (versão {p.Versao}). "
                + "Ele roda com as mesmas permissões dos seus mods: só permita se você confia em quem fez.");
            var sha = Kit.Texto(card, "SHA-256 " + p.Sha256, 11, Theme.TextoMudo);
            sha.horizontalOverflow = HorizontalWrapMode.Wrap; sha.verticalOverflow = VerticalWrapMode.Overflow;
            var pasta = Kit.Texto(card, p.Pasta, 11, Theme.TextoMudo);
            pasta.horizontalOverflow = HorizontalWrapMode.Wrap; pasta.verticalOverflow = VerticalWrapMode.Overflow;

            var botoes = Kit.Novo("Botoes", card);
            Kit.Linha(botoes, 10, 0, TextAnchor.MiddleRight);
            Kit.Tam(botoes, -1, 44);
            Kit.Tam(Kit.NovoBotao(botoes, "Nunca", Ico.Cadeado, Estilo.Fantasma, () => Confirmacao.Responder(Decisao.SempreRecusar), 44).btn, 150, 44);
            Kit.Tam(Kit.NovoBotao(botoes, "Agora não", Ico.Fechar, Estilo.Secundario, () => Confirmacao.Responder(Decisao.Recusar), 44).btn, 160, 44);
            Kit.Tam(Kit.NovoBotao(botoes, "Permitir", Ico.Marca, Estilo.Primario, () => Confirmacao.Responder(Decisao.Permitir), 44).btn, 170, 44);
            Anim.Aparecer(_modal, card);
        }

        static void Construir()
        {
            if (_canvas != null && !_canvas.WasCollected) return;
            Theme.Carregar();
            _canvas = new GameObject("PBSLoaderCommonInterface_Permissao") { layer = 5 };
            Object.DontDestroyOnLoad(_canvas);
            var cv = _canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 950; // acima do menu Mods
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<GraphicRaycaster>();
        }

        internal static string Diagnostico() => _mostrado != null ? $"perguntando sobre {_mostrado.Nome}" : "sem pergunta aberta";
    }
}
