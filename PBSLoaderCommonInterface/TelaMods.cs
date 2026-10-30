using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using PBSLoaderCommonUtils;
using PBSLoaderCommonUtils.Conteudo;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PBSLoaderCommonInterface
{
    // Painel "Mods": opções e ações de todos os mods, sem atalho de teclado. Não depende de nenhum
    // Canvas do jogo (Canvas próprio, por cima de tudo), então abre igual no menu principal e na
    // partida. À esquerda, um item por mod (categoria de MelonPreferences); à direita, as ações que o
    // mod registrou (PainelMods.Acao) e as preferências dele, cada uma com o controle do seu tipo.
    // As mudanças valem na hora (os mods leem .Value); o arquivo é gravado ao fechar.
    internal static class TelaMods
    {
        const string Proprio = "PBSLoaderCommonInterface";
        // Página dos scripts que vêm com ônibus e mapas (PBSLoaderCommonUtils.Conteudo); não é uma categoria de preferências.
        const string Scripts = "PBSU.Scripts";

        static GameObject _canvas;
        static RectTransform _janela, _nav, _conteudo;
        static Text _titulo, _subtitulo;
        static ScrollRect _scroll;
        static string _atual;
        static bool _sujo;
        static readonly List<(string id, Kit.Botao botao)> _abas = new();
        static readonly List<GameObject> _cabecalhos = new();

        // Captura de tecla para entradas KeyCode (mods que ainda não usam o registro de atalhos).
        static MelonPreferences_Entry _capturando;
        // Captura de combinação para um atalho do registro comum (PBSLoaderCommonUtils).
        static Atalho _capturandoAtalho;
        static KeyCode _modificadorSozinho;

        internal static string Diagnostico()
        {
            if (_canvas == null) return "sem canvas";
            var cv = _canvas.GetComponent<Canvas>();
            var g = _canvas.transform.GetChild(0).GetComponent<CanvasGroup>();
            return $"ativo={_canvas.activeInHierarchy} ordem={cv.sortingOrder} modo={cv.renderMode} habilitado={cv.enabled} escala={_canvas.transform.localScale} "
                 + $"alfa={(g != null ? g.alpha : -1)} janela={_janela.rect} abas={_abas.Count} itens={_conteudo.childCount}";
        }

        internal static bool Aberta => _canvas != null && !_canvas.WasCollected && _canvas.activeSelf;

        internal static void Abrir(string categoria = null)
        {
            Construir();
            _canvas.SetActive(true);
            RefazerNavegacao();
            Mostrar(categoria ?? _atual ?? (_abas.Count > 0 ? _abas[0].id : null));
            Anim.Aparecer(_canvas.transform.GetChild(0).gameObject, _janela);
        }

        internal static void Fechar()
        {
            if (!Aberta) return;
            _capturando = null;
            _capturandoAtalho = null;
            _canvas.SetActive(false);
            if (_sujo) { _sujo = false; MelonPreferences.Save(); }
        }

        // ---------- montagem ----------

        static void Construir()
        {
            if (_canvas != null && !_canvas.WasCollected) return;
            Theme.Carregar();
            PainelMods.Mudou += () => { if (Aberta) { RefazerNavegacao(); Mostrar(_atual); } };

            _canvas = new GameObject("PBSLoaderCommonInterface_Mods") { layer = 5 };
            UnityEngine.Object.DontDestroyOnLoad(_canvas);
            var cv = _canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 900; // acima das telas do mod e dos Canvas do jogo
            var sc = _canvas.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<GraphicRaycaster>();

            var fundo = Kit.Encher(Kit.Novo("Fundo", _canvas.transform));
            Kit.Fundo(fundo, null, Theme.Overlay).raycastTarget = true;

            _janela = Kit.Ancorar(Kit.Novo("Janela", fundo), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 800));
            Kit.Fundo(_janela, Theme.Solido, Theme.Elevado);
            Kit.Contorno(_janela, Theme.Linha);
            Kit.FaixaTopo(_janela, Theme.Acento, 4);

            // Navegação (lista de mods)
            _nav = Kit.Novo("Navegacao", _janela);
            _nav.anchorMin = Vector2.zero; _nav.anchorMax = new Vector2(0, 1); _nav.pivot = new Vector2(0, 0.5f);
            _nav.offsetMin = new Vector2(0, 0); _nav.offsetMax = new Vector2(300, -4);
            Kit.Fundo(_nav, Theme.Solido, Theme.Painel);
            var nc = Kit.Coluna(_nav, 2);
            Kit.Pad(nc, 16, 16, 24, 16);

            // Conteúdo
            var direita = Kit.Novo("Direita", _janela);
            direita.anchorMin = Vector2.zero; direita.anchorMax = Vector2.one;
            direita.offsetMin = new Vector2(300, 0); direita.offsetMax = new Vector2(0, -4);
            var dc = Kit.Coluna(direita, 8);
            Kit.Pad(dc, 32, 24, 24, 20);

            var cab = Kit.Novo("Cabecalho", direita);
            Kit.Linha(cab, 12);
            Kit.Tam(cab, -1, 56);
            var tit = Kit.Novo("Titulos", cab);
            Kit.Coluna(tit, 2, 0, TextAnchor.MiddleLeft);
            Kit.Tam(tit, -1, 56, 1);
            Kit.Tam(Kit.Trilha(tit, "Mods", 12), -1, 16);
            _titulo = Kit.Texto(tit, "", 26, Theme.Texto, true);
            _titulo.horizontalOverflow = HorizontalWrapMode.Overflow;
            Kit.Tam(_titulo, -1, 32);
            var fechar = Kit.NovoBotao(cab, null, Ico.Fechar, Estilo.Icone, Fechar, 40);
            _ = fechar;
            _subtitulo = Kit.Texto(direita, "", 13, Theme.TextoMudo);
            _subtitulo.verticalOverflow = VerticalWrapMode.Overflow;
            Kit.Divisoria(direita);

            _conteudo = Kit.Lista(direita, out _scroll, 6);

            var rod = Kit.Novo("Rodape", direita);
            Kit.Linha(rod, 12);
            Kit.Tam(rod, -1, 44);
            var nota = Kit.Texto(rod, "As mudanças valem na hora. Alguns mods só aplicam certas opções ao recomeçar a viagem.", 12, Theme.TextoMudo);
            Kit.Tam(nota, -1, 44, 1);
            Kit.Tam(Kit.NovoBotao(rod, "Concluir", Ico.Marca, Estilo.Primario, Fechar, 44).btn, 180, 44);

            _canvas.SetActive(false);
        }

        // Mods = categorias visíveis do MelonPreferences + as que só registraram ações.
        static List<(string id, string nome, string icone)> Categorias()
        {
            var l = new List<(string id, string nome, string icone)>();
            var vistos = new HashSet<string>();
            foreach (var c in MelonPreferences.Categories)
            {
                if (c == null || c.IsHidden || !vistos.Add(c.Identifier)) continue;
                bool temAlgo = PainelMods.Mods.TryGetValue(c.Identifier, out var i0) && i0.acoes.Count > 0;
                foreach (var e in c.Entries) if (!e.IsHidden) { temAlgo = true; break; }
                if (!temAlgo) continue;
                PainelMods.Mods.TryGetValue(c.Identifier, out var info);
                if (c.Identifier == Atalhos.Categoria) { l.Add((c.Identifier, "Atalhos", Ico.Teclado)); continue; }
                var nome = info?.titulo ?? (c.Identifier == Proprio ? "Interface" : Limpar(c.DisplayName ?? c.Identifier));
                l.Add((c.Identifier, nome, info?.icone ?? (c.Identifier == Proprio ? Ico.Janela : Ico.Ferramenta)));
            }
            foreach (var kv in PainelMods.Mods)
                if (vistos.Add(kv.Key) && kv.Value.acoes.Count > 0)
                    l.Add((kv.Key, kv.Value.titulo ?? kv.Key, kv.Value.icone ?? Ico.Ferramenta));
            if (Pacotes.Todos.Count > 0) l.Add((Scripts, "Scripts de ônibus e mapa", Ico.Cadeado));
            // Os scripts de ônibus e mapa aparecem com o nome e o ícone do lugar de onde vieram.
            for (int i = 0; i < l.Count; i++)
            {
                var pac = PacoteDe(l[i].id);
                if (pac != null) l[i] = (l[i].id, pac.Nome, pac.Origem == OrigemDoPacote.Onibus ? Ico.Onibus : Ico.Casa);
            }
            // Geral (Interface, Atalhos, Scripts), depois os mods globais, depois os do ônibus e os do mapa; em cada grupo,
            // ordem alfabética.
            static int Peso(string id) => id == Proprio ? 0 : id == Atalhos.Categoria ? 1 : id == Scripts ? 2 : 3;
            l.Sort((a, b) =>
            {
                int ga = Grupo(a.id), gb = Grupo(b.id);
                if (ga != gb) return ga - gb;
                if (Peso(a.id) != Peso(b.id)) return Peso(a.id) - Peso(b.id);
                return string.Compare(a.nome, b.nome, StringComparison.CurrentCultureIgnoreCase);
            });
            return l;
        }

        // Pacote de scripts dono de uma categoria (as preferências dele são "Conteudo.<id>"; os atalhos usam o id).
        static Pacote PacoteDe(string id)
        {
            foreach (var p in Pacotes.Todos)
                if (id == p.Id || id == "Conteudo." + p.Id) return p;
            return null;
        }

        static readonly string[] NomesDosGrupos = { "Geral", "Mods", "Deste ônibus", "Deste mapa" };

        static int Grupo(string id)
        {
            if (id == Proprio || id == Atalhos.Categoria || id == Scripts) return 0;
            var p = PacoteDe(id);
            return p == null ? 1 : p.Origem == OrigemDoPacote.Onibus ? 2 : 3;
        }

        // Nome do mod no menu: título registrado, senão o DisplayName da categoria sem o parêntese.
        internal static string NomeDe(string id)
        {
            var pac = PacoteDe(id);
            if (pac != null) return pac.Nome;
            if (PainelMods.Mods.TryGetValue(id, out var info) && info.titulo != null) return info.titulo;
            if (id == Proprio) return "Interface";
            var c = MelonPreferences.GetCategory(id);
            return Limpar(c?.DisplayName ?? id);
        }

        // "AjusteEspelhos (retrovisores ajustáveis)" -> "AjusteEspelhos": o parêntese vira subtítulo.
        static string Limpar(string s)
        {
            int i = s.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }

        static void RefazerNavegacao()
        {
            foreach (var (_, b) in _abas) UnityEngine.Object.Destroy(b.go);
            _abas.Clear();
            foreach (var c in _cabecalhos) UnityEngine.Object.Destroy(c);
            _cabecalhos.Clear();
            int grupo = -1;
            foreach (var (id, nome, icone) in Categorias())
            {
                int g = Grupo(id);
                if (g != grupo)
                {
                    // Cabeçalho do grupo (o primeiro, "Geral", fica sem título para não repetir o "Mods" da janela).
                    if (grupo >= 0)
                    {
                        _cabecalhos.Add(Kit.Espaco(_nav, 10).gameObject);
                        var t = Kit.Rotulo(_nav, NomesDosGrupos[g]);
                        Kit.Tam(t, -1, 22);
                        _cabecalhos.Add(t.gameObject);
                    }
                    grupo = g;
                }
                var cat = id;
                var b = Kit.NovoBotao(_nav, nome, icone, Estilo.Fantasma, () => Mostrar(cat), 42);
                _abas.Add((id, b));
            }
        }

        static void Mostrar(string id)
        {
            if (id == null) return;
            _atual = id;
            _capturando = null;
            _capturandoAtalho = null;
            foreach (var (aid, b) in _abas) b.Selecionado(aid == id);
            for (int i = _conteudo.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_conteudo.GetChild(i).gameObject);
            if (id == Atalhos.Categoria) { MostrarAtalhos(); return; }
            if (id == Scripts) { MostrarScripts(); return; }

            var cat = MelonPreferences.GetCategory(id);
            PainelMods.Mods.TryGetValue(id, out var info);
            _titulo.text = (info?.titulo ?? (id == Proprio ? "Interface" : Limpar(cat?.DisplayName ?? id))).ToUpperInvariant();
            var dn = cat?.DisplayName ?? "";
            int p = dn.IndexOf(" (", StringComparison.Ordinal);
            _subtitulo.text = id == Proprio ? "Sons, atalhos e aparência da interface"
                : p > 0 ? dn.Substring(p + 2).TrimEnd(')') : "";
            _subtitulo.gameObject.SetActive(_subtitulo.text.Length > 0);

            var itens = new List<GameObject>();
            if (info != null && info.acoes.Count > 0)
            {
                itens.Add(Secao("Ações").gameObject);
                var s = Cartao();
                itens.Add(s.gameObject);
                bool primeira = true;
                foreach (var (rotulo, descricao, acao) in info.acoes)
                {
                    if (!primeira) Kit.Divisoria(s);
                    primeira = false;
                    var ctrl = Linha(s, rotulo, descricao);
                    var a = acao;
                    var b = Kit.NovoBotao(ctrl, "Abrir", Ico.Avancar, Estilo.Secundario, () =>
                    {
                        Fechar(); // o painel do mod aparece sem o nosso por cima
                        try { a(); } catch (Exception e) { MelonLogger.Warning($"[Mods] ação '{rotulo}' falhou: {e.Message}"); }
                    }, 40);
                    b.icone.transform.SetAsLastSibling();
                    Kit.Tam(b.btn, 140, 40);
                }
            }

            if (cat != null)
            {
                var cartao = (RectTransform)null;
                bool primeira = true;
                foreach (var e in cat.Entries)
                {
                    if (e == null || e.IsHidden || (info != null && info.ocultas.Contains(e.Identifier))) continue;
                    if (cartao == null)
                    {
                        itens.Add(Secao("Opções").gameObject);
                        cartao = Cartao();
                        itens.Add(cartao.gameObject);
                    }
                    if (!primeira) Kit.Divisoria(cartao);
                    primeira = false;
                    Controle(cartao, e, info);
                }
            }
            // Atalhos deste mod (os mesmos da página Atalhos, para achar onde se espera).
            var deste = new List<Atalho>();
            foreach (var a in Atalhos.Todos) if (a.Mod == id) deste.Add(a);
            if (deste.Count > 0)
            {
                TeclasDoJogo.Reler();
                itens.Add(Secao("Atalhos").gameObject);
                var s = Cartao();
                itens.Add(s.gameObject);
                for (int i = 0; i < deste.Count; i++)
                {
                    if (i > 0) Kit.Divisoria(s);
                    AtalhoLinha(s, deste[i]);
                }
            }
            if (itens.Count == 0)
            {
                var t = Kit.Texto(_conteudo, "Este mod não tem opções nem ações para mostrar.", 14, Theme.TextoMudo);
                Kit.Tam(t, -1, 40);
            }
            _scroll.verticalNormalizedPosition = 1f;
            Anim.Cascata(itens, 0.05f, 0.5f, 20, 36f);
        }

        static Text Secao(string titulo)
        {
            Kit.Espaco(_conteudo, 8);
            var t = Kit.Rotulo(_conteudo, titulo);
            Kit.Tam(t, -1, 22);
            return t;
        }

        static RectTransform Cartao()
        {
            var c = Kit.Painel(_conteudo, "Cartao", Theme.Cartao);
            Kit.Coluna(c, 0);
            Kit.Pad(c.GetComponent<VerticalLayoutGroup>(), 20, 20, 4, 4);
            return c;
        }

        // Linha: rótulo e descrição à esquerda, controle à direita. Devolve o slot do controle.
        static RectTransform Linha(Transform pai, string rotulo, string descricao)
        {
            var l = Kit.Novo("Linha", pai);
            var hl = Kit.Linha(l, 20);
            Kit.Pad(hl, 0, 0, 12, 12);
            var esq = Kit.Novo("Rotulos", l);
            Kit.Coluna(esq, 3, 0, TextAnchor.MiddleLeft);
            Kit.Tam(esq, -1, -1, 1);
            var t = Kit.Texto(esq, rotulo, 15, Theme.Texto);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (!string.IsNullOrWhiteSpace(descricao))
            {
                var d = Kit.Texto(esq, descricao, 12, Theme.TextoMudo);
                d.font = Theme.TextoCorrido;
                d.verticalOverflow = VerticalWrapMode.Overflow;
            }
            var ctrl = Kit.Novo("Controle", l);
            Kit.Linha(ctrl, 6, 0, TextAnchor.MiddleRight);
            Kit.Tam(ctrl, 300, 40);
            return ctrl;
        }

        // ---------- controles por tipo ----------

        static void Controle(Transform pai, MelonPreferences_Entry e, PainelMods.Info info)
        {
            var tipo = e.GetReflectedType();
            var ctrl = Linha(pai, Rotulo(e), e.Description);
            if (tipo == typeof(bool)) Chave(ctrl, e);
            else if (tipo == typeof(KeyCode)) Tecla(ctrl, e);
            else if (tipo.IsEnum) Enum(ctrl, e, tipo);
            else if (Numero(tipo))
            {
                if (info != null && info.faixas.TryGetValue(e.Identifier, out var f)) Faixa(ctrl, e, tipo, f.min, f.max, f.passo);
                else Passos(ctrl, e, tipo);
            }
            else if (tipo == typeof(string)) Texto(ctrl, e);
            else Kit.Texto(ctrl, Convert.ToString(e.BoxedValue, CultureInfo.InvariantCulture), 14, Theme.TextoDim, false, TextAnchor.MiddleRight);
        }

        // DisplayName quando o mod deu um; senão o Identifier em palavras ("TeclaPainel" -> "Tecla painel").
        static string Rotulo(MelonPreferences_Entry e)
        {
            if (!string.IsNullOrEmpty(e.DisplayName) && e.DisplayName != e.Identifier) return e.DisplayName;
            var s = e.Identifier;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) { sb.Append(' '); sb.Append(char.ToLowerInvariant(c)); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static bool Numero(Type t) => t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(long);
        static bool Inteiro(Type t) => t == typeof(int) || t == typeof(long);

        static double Ler(MelonPreferences_Entry e) => Convert.ToDouble(e.BoxedValue, CultureInfo.InvariantCulture);

        static void Gravar(MelonPreferences_Entry e, object valor)
        {
            try
            {
                e.BoxedValue = Convert.ChangeType(valor, e.GetReflectedType(), CultureInfo.InvariantCulture);
                _sujo = true;
            }
            catch (Exception ex) { MelonLogger.Warning($"[Mods] valor inválido para {e.Identifier}: {ex.Message}"); }
        }

        static string Formatar(double v, Type t) =>
            Inteiro(t) ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("0.###", CultureInfo.InvariantCulture);

        static void Chave(Transform ctrl, MelonPreferences_Entry e)
        {
            var c = Kit.NovaChave(ctrl, "", () => Gravar(e, !(bool)e.BoxedValue));
            c.rotulo.gameObject.SetActive(false);
            Kit.Tam(c.go.GetComponent<RectTransform>(), 52, 40);
            Kit.AoQuadro(c.go, () => c.Definir((bool)e.BoxedValue));
        }

        // Número com faixa conhecida: slider + valor.
        static void Faixa(Transform ctrl, MelonPreferences_Entry e, Type tipo, float min, float max, float passo)
        {
            bool sincronizando = false;
            var s = Kit.NovoSlider(ctrl, 220, v =>
            {
                if (sincronizando) return;
                double x = min + (max - min) * v;
                if (passo > 0) x = Math.Round(x / passo) * passo;
                if (Inteiro(tipo)) x = Math.Round(x);
                Gravar(e, x);
            });
            var valor = Kit.Texto(ctrl, "", 15, Theme.Texto, true, TextAnchor.MiddleRight);
            Kit.Tam(valor, 64, 32);
            Kit.AoQuadro(s.gameObject, () =>
            {
                double atual = Ler(e);
                float n = Mathf.InverseLerp(min, max, (float)atual);
                if (Mathf.Abs(s.value - n) > 0.001f) { sincronizando = true; s.value = n; sincronizando = false; }
                var txt = Formatar(atual, tipo);
                if (valor.text != txt) valor.text = txt;
            });
        }

        // Número sem faixa: − campo +. O passo acompanha a grandeza do valor (0,05 -> 0,01; 12 -> 1).
        static void Passos(Transform ctrl, MelonPreferences_Entry e, Type tipo)
        {
            double Passo()
            {
                if (Inteiro(tipo)) return 1;
                double v = Math.Abs(Ler(e));
                if (v < 1e-9) return 0.1;
                double ordem = Math.Floor(Math.Log10(v));
                return Math.Pow(10, v >= 1 ? ordem - 1 : ordem); // 12 -> 1; 2,5 -> 0,1; 0,05 -> 0,01
            }
            var menos = Kit.NovoBotao(ctrl, null, Ico.Remover, Estilo.Secundario, () => Gravar(e, Ler(e) - Passo()), 40);
            InputField campo = null;
            campo = Kit.Campo(ctrl, "", null, s =>
            {
                var t = (s ?? "").Trim().Replace(',', '.');
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) Gravar(e, v);
            }, null);
            Kit.Tam(campo, 120, 40);
            campo.textComponent.alignment = TextAnchor.MiddleCenter;
            var mais = Kit.NovoBotao(ctrl, null, Ico.Adicionar, Estilo.Secundario, () => Gravar(e, Ler(e) + Passo()), 40);
            _ = menos; _ = mais;
            Kit.AoQuadro(campo.gameObject, () =>
            {
                if (campo.isFocused) return;
                var txt = Formatar(Ler(e), tipo);
                if (campo.text != txt) campo.text = txt;
            });
        }

        static void Texto(Transform ctrl, MelonPreferences_Entry e)
        {
            var campo = Kit.Campo(ctrl, "", null, s => Gravar(e, s ?? ""), null);
            Kit.Tam(campo, 300, 40);
            Kit.AoQuadro(campo.gameObject, () =>
            {
                if (campo.isFocused) return;
                var txt = (string)e.BoxedValue ?? "";
                if (campo.text != txt) campo.text = txt;
            });
        }

        // Enum: botão que mostra o valor e avança para o próximo a cada clique.
        static void Enum(Transform ctrl, MelonPreferences_Entry e, Type tipo)
        {
            var valores = System.Enum.GetValues(tipo);
            var b = Kit.NovoBotao(ctrl, "—", Ico.SetaDir, Estilo.Secundario, () =>
            {
                int i = Array.IndexOf(valores, e.BoxedValue);
                Gravar(e, valores.GetValue((i + 1) % valores.Length));
            }, 40);
            b.maiusculas = false;
            b.icone.transform.SetAsLastSibling();
            Kit.Tam(b.btn, 240, 40);
            Kit.AoQuadro(b.go, () => b.Rotulo(Convert.ToString(e.BoxedValue)));
        }

        // KeyCode: o botão mostra a tecla; clicar espera a próxima tecla (Esc cancela).
        static void Tecla(Transform ctrl, MelonPreferences_Entry e)
        {
            Kit.Botao b = null;
            b = Kit.NovoBotao(ctrl, "—", Ico.Teclado, Estilo.Secundario, () => _capturando = e, 40);
            b.maiusculas = false;
            Kit.Tam(b.btn, 240, 40);
            Kit.AoQuadro(b.go, () =>
            {
                bool esperando = _capturando == e;
                b.Rotulo(esperando ? "Aperte uma tecla…" : NomeTecla((KeyCode)e.BoxedValue));
                b.Selecionado(esperando);
            });
        }

        static string NomeTecla(KeyCode k) => k switch
        {
            KeyCode.BackQuote => "` (crase)",
            KeyCode.None => "Nenhuma",
            _ => k.ToString(),
        };

        // ---------- scripts de ônibus e mapa ----------

        static string Estado(Pacote p) => p.Estado switch
        {
            EstadoDoPacote.Ativo => "Rodando",
            EstadoDoPacote.Inativo => "Permitido (o ônibus/mapa dele não está em jogo)",
            EstadoDoPacote.AguardandoPermissao => "Esperando a sua resposta",
            EstadoDoPacote.Recusado => "Bloqueado" + (p.Detalhe.Length > 0 ? ": " + p.Detalhe : ""),
            EstadoDoPacote.Falhou => "Desligado por erro: " + p.Detalhe,
            _ => p.Estado.ToString(),
        };

        static void MostrarScripts()
        {
            _titulo.text = "SCRIPTS DE ÔNIBUS E MAPA";
            _subtitulo.text = "Código que veio dentro de um ônibus ou mapa. Ele roda com as mesmas permissões dos mods: só permita o que vem de quem você confia. "
                            + "A permissão vale para esta versão do arquivo; uma atualização pergunta de novo.";
            _subtitulo.gameObject.SetActive(true);
            var itens = new List<GameObject>();
            foreach (var p in Pacotes.Todos)
            {
                var pac = p;
                itens.Add(Secao(p.Nome).gameObject);
                var c = Cartao();
                itens.Add(c.gameObject);
                Valor(c, "Origem", $"{(p.Origem == OrigemDoPacote.Onibus ? "Ônibus" : "Mapa")} · {p.Autor} · versão {p.Versao}");
                Kit.Divisoria(c);
                var est = Linha(c, "Estado", null);
                var t = Kit.Texto(est, "", 14, Theme.TextoDim, false, TextAnchor.MiddleRight);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                Kit.Tam(t, 300, 40);
                Kit.AoQuadro(t.gameObject, () => { var e = Estado(pac); if (t.text != e) t.text = e; t.color = pac.Ativo ? Theme.ImpactoLeve : pac.Estado == EstadoDoPacote.Falhou ? Theme.PerigoTexto : Theme.TextoDim; });
                Kit.Divisoria(c);
                Valor(c, "Arquivo", p.Sha256.Length >= 16 ? "SHA-256 " + p.Sha256.Substring(0, 16) + "…" : "—", p.Pasta);
                if (p.Estado != EstadoDoPacote.Falhou && p.Sha256.Length > 0)
                {
                    Kit.Divisoria(c);
                    var ctrl = Linha(c, "Permissão", "Permitir grava a decisão; bloquear desliga na hora e não pergunta mais para esta versão.");
                    bool permitido = Pacotes.Permissao(p) == Decisao.Permitir;
                    var b = Kit.NovoBotao(ctrl, permitido ? "Bloquear" : "Permitir", permitido ? Ico.Cadeado : Ico.Marca,
                        permitido ? Estilo.Perigo : Estilo.Primario, () =>
                        {
                            if (Pacotes.Permissao(pac) == Decisao.Permitir) Pacotes.Revogar(pac); else Pacotes.Permitir(pac);
                            Mostrar(Scripts);
                        }, 40);
                    Kit.Tam(b.btn, 160, 40);
                }
            }
            _scroll.verticalNormalizedPosition = 1f;
            Anim.Cascata(itens, 0.05f, 0.5f, 20, 36f);
        }

        static void Valor(Transform cartao, string rotulo, string valor, string descricao = null)
        {
            var ctrl = Linha(cartao, rotulo, descricao);
            var t = Kit.Texto(ctrl, valor, 14, Theme.TextoDim, false, TextAnchor.MiddleRight);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            Kit.Tam(t, 300, 40);
        }

        // ---------- atalhos (registro comum do PBSLoaderCommonUtils) ----------

        // Página "Atalhos": todos os atalhos de todos os mods, um cartão por mod, com os conflitos.
        static void MostrarAtalhos()
        {
            TeclasDoJogo.Reler(); // o jogador pode ter remapeado os controles do jogo desde a última vez
            _titulo.text = "ATALHOS";
            _subtitulo.text = "Clique num atalho e aperte a combinação nova. Esc cancela, Backspace deixa sem atalho. "
                            + "Conflitos com o jogo ou com outro mod aparecem em amarelo.";
            _subtitulo.gameObject.SetActive(true);
            var itens = new List<GameObject>();
            var mods = new List<string>();
            foreach (var a in Atalhos.Todos) if (!mods.Contains(a.Mod)) mods.Add(a.Mod);
            mods.Sort((x, y) => string.Compare(NomeDe(x), NomeDe(y), StringComparison.CurrentCultureIgnoreCase));
            foreach (var mod in mods)
            {
                itens.Add(Secao(NomeDe(mod)).gameObject);
                var s = Cartao();
                itens.Add(s.gameObject);
                bool primeira = true;
                foreach (var a in Atalhos.Todos)
                {
                    if (a.Mod != mod) continue;
                    if (!primeira) Kit.Divisoria(s);
                    primeira = false;
                    AtalhoLinha(s, a);
                }
            }
            if (itens.Count == 0)
            {
                var t = Kit.Texto(_conteudo, "Nenhum mod registrou atalhos.", 14, Theme.TextoMudo);
                Kit.Tam(t, -1, 40);
            }
            _scroll.verticalNormalizedPosition = 1f;
            Anim.Cascata(itens, 0.05f, 0.5f, 20, 36f);
        }

        // Linha de um atalho: nome, descrição e conflitos à esquerda; combinação (clique para trocar) e
        // "voltar ao padrão" à direita.
        static void AtalhoLinha(Transform cartao, Atalho a)
        {
            var desc = a.Descricao ?? "";
            if (a.SoComPainel) desc = (desc.Length > 0 ? desc + " " : "") + "Só com o painel do mod aberto.";
            if (a.DeSegurar) desc = (desc.Length > 0 ? desc + " " : "") + "Segure a tecla.";
            var ctrl = Linha(cartao, a.Nome, desc);

            var rotulos = ctrl.parent.Find("Rotulos");
            var conflito = Kit.Texto(rotulos, "", 12, Theme.Aviso);
            conflito.font = Theme.TextoCorrido;
            conflito.verticalOverflow = VerticalWrapMode.Overflow;
            Kit.AoQuadro(conflito.gameObject, () =>
            {
                var txt = a.Conflitos.Count > 0 ? "Conflito: " + string.Join("; ", a.Conflitos) : "";
                if (conflito.text != txt) conflito.text = txt;
                if (conflito.enabled != (txt.Length > 0)) conflito.enabled = txt.Length > 0;
            });

            var b = Kit.NovoBotao(ctrl, "—", Ico.Teclado, Estilo.Secundario, () =>
            {
                _capturando = null;
                _capturandoAtalho = a;
                _modificadorSozinho = KeyCode.None;
            }, 40);
            b.maiusculas = false;
            Kit.Tam(b.btn, 200, 40);
            Kit.AoQuadro(b.go, () =>
            {
                bool esperando = _capturandoAtalho == a;
                b.Rotulo(esperando ? "Aperte as teclas…" : a.Combinacao.Legivel());
                b.Selecionado(esperando);
            });
            var padrao = Kit.NovoBotao(ctrl, null, Ico.Reset, Estilo.Secundario, () =>
            {
                _capturandoAtalho = null;
                a.Definir(a.Padrao);
                _sujo = true;
            }, 40);
            padrao.visual.dica = Kit.Dica(padrao.go.GetComponent<RectTransform>(), "Voltar ao padrão (" + a.Padrao.Legivel() + ")");
        }

        static KeyCode[] _teclas;

        // Espera a combinação: tecla comum com os modificadores apertados na hora. Um modificador sozinho
        // (apertado e solto sem outra tecla) só vale para atalhos de segurar, como "Alt esquerdo".
        static void CapturarAtalho()
        {
            var a = _capturandoAtalho;
            if (Input.GetKeyDown(KeyCode.Escape)) { _capturandoAtalho = null; return; }
            if (Input.GetKeyDown(KeyCode.Backspace)) { Concluir(a, Combinacao.Nenhuma); return; }
            _teclas ??= (KeyCode[])System.Enum.GetValues(typeof(KeyCode));
            foreach (var k in _teclas)
            {
                if (k == KeyCode.None || k >= KeyCode.Mouse0) continue; // mouse e joystick não
                if (!Input.GetKeyDown(k)) continue;
                if (Combinacao.EhModificador(k)) { if (_modificadorSozinho == KeyCode.None) _modificadorSozinho = k; continue; }
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                Concluir(a, new Combinacao(k, ctrl, alt, shift));
                return;
            }
            if (_modificadorSozinho != KeyCode.None && Input.GetKeyUp(_modificadorSozinho))
            {
                if (a.DeSegurar) Concluir(a, new Combinacao(_modificadorSozinho));
                _modificadorSozinho = KeyCode.None;
            }
        }

        static void Concluir(Atalho a, Combinacao c)
        {
            a.Definir(c);
            _sujo = true;
            _capturandoAtalho = null;
            Som.Tocar(Som.Efeito.Confirmar);
        }

        // Chamado a cada frame.
        internal static void Atualizar()
        {
            // Durante a captura nenhum atalho dispara e os controles do jogo param (a tecla nova não buzina).
            bool capturando = Aberta && _capturandoAtalho != null;
            Atalhos.Pausado = capturando;
            Teclado.SuspenderJogo("PBSLoaderCommonInterface.captura", capturando);
            if (capturando) { CapturarAtalho(); return; }
            if (!Aberta || _capturando == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { _capturando = null; return; }
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (k == KeyCode.None || k == KeyCode.Mouse0 || k == KeyCode.Mouse1) continue;
                if (!Input.GetKeyDown(k)) continue;
                Gravar(_capturando, k);
                _capturando = null;
                Som.Tocar(Som.Efeito.Confirmar);
                return;
            }
        }
    }
}
