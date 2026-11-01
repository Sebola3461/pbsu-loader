using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PbsuLoader
{
    /// <summary>
    /// Janela única: pasta do jogo, MelonLoader e a lista de mods do catálogo. Marcar = quero instalado; "Aplicar"
    /// instala, atualiza e remove para a pasta ficar igual à lista.
    /// </summary>
    class Janela : Form
    {
        readonly string[] _args;
        readonly string _modDoSite; // id vindo de pbsloader://mod/<id>: pré-selecionado na primeira lista
        bool _escolhidoDoSite;
        Catalogo _cat;
        string _jogo, _versaoJogo, _versaoMelon;
        Registro _reg;
        List<EstadoMod> _estados = new List<EstadoMod>();
        Lancamento _novoInstalador;
        bool _ocupado, _marcando, _aceitarPre;
        List<string> _pastasDev; // modo desenvolvedor ligado (lido na thread da janela antes de verificar)

        readonly TextBox _pasta = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        readonly Button _escolher = new Button { Text = "Escolher...", AutoSize = true };
        readonly Label _infoJogo = new Label { AutoSize = true, Padding = new Padding(0, 4, 0, 4) };
        readonly Label _infoMelon = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        readonly LinkLabel _avisoInstalador = new LinkLabel { AutoSize = true, Visible = false, Padding = new Padding(0, 4, 0, 4) };
        readonly ListView _lista = new ListView
        {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        readonly LinkLabel _detalhe = new LinkLabel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
        readonly CheckBox _beta = new CheckBox { Text = "Incluir versões de teste (pré-lançamento)", AutoSize = true };
        readonly CheckBox _dev = new CheckBox { Text = "Modo desenvolvedor", AutoSize = true };
        readonly LinkLabel _editarPastasDev = new LinkLabel { Text = "pastas...", AutoSize = true };
        readonly Button _aplicar = new Button { Text = "Aplicar", AutoSize = true };
        readonly Button _verificar = new Button { Text = "Verificar de novo", AutoSize = true };
        readonly Button _abrirMods = new Button { Text = "Abrir pasta Mods", AutoSize = true };
        readonly Button _jogar = new Button { Text = "Jogar", AutoSize = true };
        readonly Button _removerTudo = new Button { Text = "Desinstalar tudo...", AutoSize = true };
        readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

        public Janela(string[] args)
        {
            _args = args;
            _modDoSite = args.Select(Protocolo.IdDoArgumento).FirstOrDefault(id => id != null);
            Text = "PBSLoader " + Programa.Versao;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(860, 640);
            MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _lista.Columns.Add("Mod", 190);
            _lista.Columns.Add("Estado", 90);
            _lista.Columns.Add("Instalado", 80);
            _lista.Columns.Add("Disponível", 80);
            _lista.Columns.Add("Situação", 380);

            var raiz = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1 };
            raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // pasta
            raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // versão do jogo
            raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // MelonLoader
            raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // instalador novo
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 65));  // lista
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); // detalhe
            raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // botões
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 35));  // log

            var linhaPasta = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
            linhaPasta.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linhaPasta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            linhaPasta.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linhaPasta.Controls.Add(new Label { Text = "Pasta do jogo:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            linhaPasta.Controls.Add(_pasta, 1, 0);
            linhaPasta.Controls.Add(_escolher, 2, 0);

            var linhaMelon = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            linhaMelon.Controls.Add(_infoMelon);

            var botoes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            botoes.Controls.AddRange(new Control[] { _aplicar, _jogar, _abrirMods, _verificar, _removerTudo });
            var opcoes = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.Left };
            var linhaDev = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            _editarPastasDev.Padding = new Padding(0, 4, 0, 0);
            linhaDev.Controls.AddRange(new Control[] { _dev, _editarPastasDev });
            opcoes.Controls.AddRange(new Control[] { _beta, linhaDev });
            var linhaBotoes = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            linhaBotoes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linhaBotoes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            linhaBotoes.Controls.Add(opcoes, 0, 0);
            linhaBotoes.Controls.Add(botoes, 1, 0);

            raiz.Controls.Add(linhaPasta, 0, 0);
            raiz.Controls.Add(_infoJogo, 0, 1);
            raiz.Controls.Add(linhaMelon, 0, 2);
            raiz.Controls.Add(_avisoInstalador, 0, 3);
            raiz.Controls.Add(_lista, 0, 4);
            raiz.Controls.Add(_detalhe, 0, 5);
            raiz.Controls.Add(linhaBotoes, 0, 6);
            raiz.Controls.Add(_log, 0, 7);
            Controls.Add(raiz);
            AcceptButton = _aplicar;

            _beta.Checked = Config.Ler("beta") == "1";
            _dev.Checked = Local.Ativo;
            _editarPastasDev.Visible = _dev.Checked;
            _dev.CheckedChanged += (s, e) => AoTrocarDev();
            _editarPastasDev.LinkClicked += (s, e) => { if (EditarPastasDev()) Verificar(false); };
            _escolher.Click += (s, e) => EscolherPasta();
            _verificar.Click += (s, e) => Verificar(true);
            _aplicar.Click += (s, e) => Aplicar();
            _jogar.Click += (s, e) => Jogar();
            _removerTudo.Click += (s, e) => RemoverTudo();
            _abrirMods.Click += (s, e) => { if (_jogo != null) AbrirPasta(Path.Combine(_jogo, "Mods")); };
            _beta.CheckedChanged += (s, e) => { Config.Gravar("beta", _beta.Checked ? "1" : "0"); Verificar(false); };
            _lista.ItemCheck += AoMarcar;
            _lista.ItemChecked += AoMarcado;
            _lista.SelectedIndexChanged += (s, e) => MostrarDetalhe();
            _detalhe.LinkClicked += (s, e) => AbrirUrl(e.Link.LinkData as string);
            _avisoInstalador.LinkClicked += (s, e) => AtualizarInstalador();
            Shown += (s, e) => Verificar(true);
        }

        // ---------------------------------------------------------------- consulta

        void Verificar(bool releerCatalogo)
        {
            if (_ocupado) return;
            _aceitarPre = _beta.Checked;
            _pastasDev = _dev.Checked ? Local.Pastas : null;
            Rodar("Verificando...", () =>
            {
                if (releerCatalogo || _cat == null)
                {
                    _cat = Catalogo.Carregar(Log);
                    Log("Catálogo: " + _cat.Fonte + " (" + _cat.Mods.Count + " mods).");
                }
                Rede.EsquecerReleases();
                if (_jogo == null) _jogo = Jogo.Procurar(_cat, _args);
                Avaliar();
                try
                {
                    var l = _cat.Instalador != null ? Rede.UltimoLancamento(_cat.Instalador, _aceitarPre) : null;
                    var vl = Versoes.Ler(l?.Versao);
                    _novoInstalador = vl != null && vl > Versoes.Ler(Programa.Versao) ? l : null;
                }
                catch { _novoInstalador = null; }
            });
        }

        // Roda em segundo plano: o hash do GameAssembly.dll e a API do GitHub demoram.
        void Avaliar()
        {
            _estados = new List<EstadoMod>();
            _versaoJogo = _versaoMelon = null;
            _reg = null;
            if (!Jogo.Valida(_jogo, _cat)) return;
            Config.Gravar("jogo", _jogo);
            _versaoJogo = Jogo.Versao(_jogo, _cat, out _);
            _versaoMelon = Jogo.VersaoMelon(_jogo);
            _reg = Registro.Abrir(_jogo);
            _reg.AvisarIgnorados(Log);
            var builds = _pastasDev != null ? Local.Builds(_pastasDev) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in _cat.Mods)
            {
                string nome = Path.GetFileNameWithoutExtension(m.Origem.Arquivo);
                builds.TryGetValue(nome, out var local);
                builds.Remove(nome);
                _estados.Add(Instalacao.Avaliar(m, _jogo, _reg, _aceitarPre, local));
            }
            // O que sobrou são mods fora do catálogo: aparecem no fim da lista, só no modo desenvolvedor.
            foreach (var b in builds.OrderBy(b => b.Key))
                _estados.Add(Instalacao.Avaliar(Local.ForaDoCatalogo(b.Key, b.Value), _jogo, _reg, _aceitarPre, b.Value));
        }

        void AoTrocarDev()
        {
            if (_dev.Checked && Local.Pastas.Count == 0 && !EditarPastasDev())
            {
                _dev.Checked = false; // cancelou sem pasta nenhuma
                return;
            }
            Local.Ativo = _dev.Checked;
            _editarPastasDev.Visible = _dev.Checked;
            Verificar(false);
        }

        bool EditarPastasDev()
        {
            var novas = Local.EditarPastas(this, Local.Pastas);
            if (novas == null) return false;
            Local.Pastas = novas;
            return novas.Count > 0;
        }

        void Preencher()
        {
            bool valido = _cat != null && Jogo.Valida(_jogo, _cat);
            _pasta.Text = _jogo ?? "";
            if (!valido)
            {
                _infoJogo.Text = "Não achei o jogo. Clique em \"Escolher...\" e aponte o " + (_cat?.Executavel ?? "executável do jogo") + ".";
                _infoJogo.ForeColor = Color.DarkRed;
            }
            else if (_versaoJogo != null)
            {
                _infoJogo.Text = "Versão do jogo: " + _versaoJogo + " (conhecida pelo catálogo).";
                _infoJogo.ForeColor = SystemColors.ControlText;
            }
            else
            {
                _infoJogo.Text = "Versão do jogo desconhecida: o jogo foi atualizado depois do último teste dos mods. Eles podem não funcionar.";
                _infoJogo.ForeColor = Color.DarkOrange;
            }

            if (!valido) _infoMelon.Text = "MelonLoader: —";
            else if (_versaoMelon == null) _infoMelon.Text = "MelonLoader: não instalado. \"Aplicar\" instala a versão " + _cat.MelonVersao + ".";
            else if (_versaoMelon == _cat.MelonVersao) _infoMelon.Text = "MelonLoader " + _versaoMelon + " instalado.";
            else _infoMelon.Text = "MelonLoader " + _versaoMelon + " instalado, mas os mods são testados com a " + _cat.MelonVersao + ". \"Aplicar\" troca de versão.";

            if (_novoInstalador != null)
            {
                _avisoInstalador.Text = "Há uma versão nova do instalador (" + _novoInstalador.Versao + "). Clique aqui para atualizar.";
                _avisoInstalador.Visible = true;
            }
            else _avisoInstalador.Visible = false;

            _marcando = true;
            _lista.BeginUpdate();
            _lista.Items.Clear();
            bool primeiraVez = _reg == null || _reg.Mods.Count == 0;
            foreach (var e in _estados)
            {
                var it = new ListViewItem(e.Mod.Nome + (e.Mod.Obrigatorio ? " (obrigatório)" : "")) { Tag = e };
                it.SubItems.Add(e.Mod.Estado);
                it.SubItems.Add(e.Instalado?.Versao ?? (e.Presente ? "?" : ""));
                it.SubItems.Add(e.Local != null ? "local" : e.Remoto != null ? e.Remoto.Versao + (e.Remoto.PreLancamento ? " (teste)" : "") : "");
                it.SubItems.Add(TextoSituacao(e));
                it.Checked = e.Mod.Obrigatorio || e.Presente || primeiraVez && e.Mod.Padrao;
                if (e.Situacao == Situacao.Atualizacao || e.Situacao == Situacao.Diferente || e.Situacao == Situacao.LocalNovo) it.ForeColor = Color.DarkOrange;
                else if (e.Situacao == Situacao.LocalAtualizado) it.ForeColor = Color.SteelBlue;
                else if (e.Situacao == Situacao.Erro) it.ForeColor = Color.DarkRed;
                else if (e.Situacao == Situacao.SemLancamento) it.ForeColor = SystemColors.GrayText;
                _lista.Items.Add(it);
            }
            Normalizar();
            _lista.EndUpdate();
            _marcando = false;
            MostrarDetalhe();
            Habilitar();
        }

        string TextoSituacao(EstadoMod e)
        {
            string s;
            switch (e.Situacao)
            {
                case Situacao.NaoInstalado: s = e.Local != null ? "Não instalado (\"Aplicar\" copia o build local)" : "Não instalado"; break;
                case Situacao.Atualizado: s = "Atualizado"; break;
                case Situacao.Atualizacao: s = "Atualização: " + e.Instalado.Versao + " → " + e.Remoto.Versao; break;
                case Situacao.Diferente: s = e.Remoto != null ? "Diferente do release (\"Aplicar\" substitui)" : "Instalado, sem release para comparar"; break;
                case Situacao.SemLancamento: s = "Sem release publicado"; break;
                case Situacao.LocalAtualizado: s = "Build local (o mesmo que está em Mods)"; break;
                case Situacao.LocalNovo: s = "Build local diferente do que está em Mods (\"Aplicar\" copia)"; break;
                default: s = "Erro ao consultar"; break;
            }
            if (_versaoJogo != null && e.Mod.VersoesDoJogo.Count > 0 && !e.Mod.VersoesDoJogo.Contains(_versaoJogo))
                s += " · não testado na versão " + _versaoJogo + " do jogo";
            return s;
        }

        void MostrarDetalhe()
        {
            _detalhe.Links.Clear();
            if (_lista.SelectedItems.Count == 0)
            {
                _detalhe.Text = _estados.Count > 0 ? "Marque os mods que você quer no jogo e clique em \"Aplicar\". Desmarcar remove." : "";
                return;
            }
            var e = (EstadoMod)_lista.SelectedItems[0].Tag;
            string cabeca = e.Mod.Nome + (e.Mod.Autor.Length > 0 ? " — por " + e.Mod.Autor : "");
            string texto = cabeca + ". " + e.Mod.Descricao;
            if (e.Mod.Dependencias.Count > 0) texto += " Precisa de: " + string.Join(", ", e.Mod.Dependencias) + ".";
            if (e.Mod.Community) texto += e.Mod.Trusted ? " Da comunidade, de autor confiável." : " Da comunidade, sem revisão.";
            if (e.Detalhe.Length > 0) texto += " (" + e.Detalhe + ")";
            if (e.Mod.Pagina == null) { _detalhe.Text = texto; return; } // build local fora do catálogo
            const string pagina = "  Página do mod";
            _detalhe.Text = texto + pagina;
            _detalhe.Links.Add(texto.Length + 2, pagina.Length - 2, e.Mod.Pagina);
        }

        // ---------------------------------------------------------------- marcação com dependências

        void AoMarcar(object s, ItemCheckEventArgs a)
        {
            if (_marcando) return;
            var e = (EstadoMod)_lista.Items[a.Index].Tag;
            if (e.Mod.Obrigatorio && a.NewValue == CheckState.Unchecked) a.NewValue = CheckState.Checked;
        }

        void AoMarcado(object s, ItemCheckedEventArgs a)
        {
            if (_marcando) return;
            var e = (EstadoMod)a.Item.Tag;
            _marcando = true;
            try
            {
                if (!a.Item.Checked)
                {
                    var itens = _lista.Items.Cast<ListViewItem>().ToList();
                    // Um obrigatório depende dele: não dá para tirar.
                    if (itens.Any(it => ((EstadoMod)it.Tag).Mod.Obrigatorio && Depende(((EstadoMod)it.Tag).Mod, e.Mod.Id)))
                        a.Item.Checked = true;
                    else
                        foreach (var it in itens)
                            if (it.Checked && Depende(((EstadoMod)it.Tag).Mod, e.Mod.Id)) it.Checked = false;
                }
                Normalizar();
            }
            finally { _marcando = false; }
        }

        // Obrigatórios e as dependências (diretas ou não) de tudo que está marcado ficam marcados.
        // Quem chama põe _marcando, para os eventos de marcação não rodarem de novo.
        void Normalizar()
        {
            var querer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem it in _lista.Items)
            {
                var m = ((EstadoMod)it.Tag).Mod;
                if (it.Checked || m.Obrigatorio) Incluir(m, querer);
            }
            foreach (ListViewItem it in _lista.Items)
                if (!it.Checked && querer.Contains(((EstadoMod)it.Tag).Mod.Id)) it.Checked = true;
        }

        void Incluir(ModDoCatalogo m, HashSet<string> querer)
        {
            if (!querer.Add(m.Id)) return;
            foreach (var dep in m.Dependencias)
                if (_cat.Mod(dep) is ModDoCatalogo d) Incluir(d, querer);
        }

        bool Depende(ModDoCatalogo m, string id) => DependeDe(m, id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        bool DependeDe(ModDoCatalogo m, string id, HashSet<string> vistos)
        {
            if (!vistos.Add(m.Id)) return false;
            foreach (var dep in m.Dependencias)
            {
                if (string.Equals(dep, id, StringComparison.OrdinalIgnoreCase)) return true;
                var d = _cat.Mod(dep);
                if (d != null && DependeDe(d, id, vistos)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- ações

        void Aplicar()
        {
            if (!Pronto()) return;
            Instalacao.GravarConfiaveis(_cat, _jogo, Log);
            var instalar = new List<EstadoMod>();
            var remover = new List<EstadoMod>();
            var semRelease = new List<string>();
            _marcando = true;
            try { Normalizar(); }
            finally { _marcando = false; }
            foreach (ListViewItem it in _lista.Items)
            {
                var e = (EstadoMod)it.Tag;
                if (it.Checked && e.PrecisaInstalar) instalar.Add(e);
                else if (it.Checked && e.Remoto == null && e.Local == null && !e.Presente) semRelease.Add(e.Mod.Nome);
                else if (!it.Checked && e.Presente) remover.Add(e);
            }
            bool melon = _versaoMelon != _cat.MelonVersao;

            if (!melon && instalar.Count == 0 && remover.Count == 0)
            {
                MessageBox.Show(this, semRelease.Count > 0
                        ? "Nada para fazer. Sem release publicado ainda: " + string.Join(", ", semRelease) + "."
                        : "Está tudo como na lista.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var resumo = new List<string>();
            if (melon) resumo.Add(_versaoMelon == null ? "Instalar o MelonLoader " + _cat.MelonVersao
                : "Trocar o MelonLoader " + _versaoMelon + " pelo " + _cat.MelonVersao + " (a pasta MelonLoader é recriada)");
            resumo.AddRange(instalar.Select(e => (e.Situacao == Situacao.NaoInstalado ? "Instalar " : "Atualizar ") + e.Mod.Nome + " " + (e.Local != null ? "(build local)" : e.Remoto.Versao)));
            resumo.AddRange(remover.Select(e => "Remover " + e.Mod.Nome));
            if (semRelease.Count > 0) resumo.Add("(sem release, ficam de fora: " + string.Join(", ", semRelease) + ")");
            string warning = Instalacao.UnreviewedWarning(instalar);
            if (warning != null) resumo.Add("\n" + warning);
            var icon = warning != null ? MessageBoxIcon.Warning : MessageBoxIcon.Question;
            if (MessageBox.Show(this, string.Join("\n", resumo), "Confirmar", MessageBoxButtons.OKCancel, icon) != DialogResult.OK)
                return;

            Rodar("Aplicando...", () =>
            {
                try
                {
                    if (melon) Instalacao.InstalarMelon(_cat, _jogo, Log);
                    foreach (var e in instalar) Instalacao.Instalar(e, _jogo, _reg, Log);
                    foreach (var e in remover) Instalacao.Remover(e, _jogo, _reg, Log);
                    _reg.Salvar();
                    Log("Pronto.");
                }
                finally { Avaliar(); } // mesmo com erro no meio: a lista mostra o que já foi feito
            });
        }

        void RemoverTudo()
        {
            if (!Pronto()) return;
            var presentes = _estados.Where(e => e.Presente).ToList();
            string msg = "Remover " + (presentes.Count > 0 ? string.Join(", ", presentes.Select(e => e.Mod.Nome)) + " e " : "") +
                "o MelonLoader?\n\nAs configurações (pasta UserData) e mods que não vieram do catálogo ficam.";
            if (MessageBox.Show(this, msg, "Desinstalar", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Rodar("Desinstalando...", () =>
            {
                try
                {
                    foreach (var e in presentes) Instalacao.Remover(e, _jogo, _reg, Log);
                    Instalacao.RemoverMelon(_jogo, Log);
                }
                finally { Avaliar(); }
            });
        }

        void AtualizarInstalador()
        {
            if (_novoInstalador == null || _ocupado) return;
            var novo = _novoInstalador;
            Rodar("Baixando o instalador " + novo.Versao + "...", () =>
            {
                string baixado = AutoAtualizacao.Baixar(novo);
                BeginInvoke((Action)(() =>
                {
                    try { AutoAtualizacao.Trocar(baixado, _jogo ?? ""); }
                    catch (Exception ex) { Log("Não consegui trocar o instalador: " + ex.Message); }
                }));
            });
        }

        bool Pronto()
        {
            if (_ocupado || _cat == null) return false;
            if (!Jogo.Valida(_jogo, _cat))
            {
                MessageBox.Show(this, "Escolha a pasta do jogo primeiro.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (Jogo.Aberto(_cat, _jogo))
            {
                MessageBox.Show(this, "Feche o jogo antes. Com ele aberto, as DLLs dos mods estão em uso e não podem ser trocadas.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        void EscolherPasta()
        {
            if (_ocupado) return;
            using (var d = new OpenFileDialog
            {
                Title = "Onde está o jogo?",
                Filter = (_cat?.Executavel ?? "Proton Bus Simulator.exe") + "|" + (_cat?.Executavel ?? "Proton Bus Simulator.exe") + "|Programas (*.exe)|*.exe",
                InitialDirectory = Directory.Exists(_jogo ?? "") ? _jogo : "",
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string pasta = Path.GetDirectoryName(d.FileName);
                if (!File.Exists(Path.Combine(pasta, "GameAssembly.dll")))
                {
                    MessageBox.Show(this, "Essa pasta não parece ser a do jogo: falta o GameAssembly.dll ao lado do executável.", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _jogo = pasta;
                Verificar(false);
            }
        }

        void Jogar()
        {
            if (_cat == null || !Jogo.Valida(_jogo, _cat)) return;
            Process.Start(new ProcessStartInfo(Path.Combine(_jogo, _cat.Executavel)) { WorkingDirectory = _jogo, UseShellExecute = true });
        }

        // ---------------------------------------------------------------- infraestrutura

        void Rodar(string titulo, Action trabalho)
        {
            _ocupado = true;
            Habilitar();
            UseWaitCursor = true;
            Log(titulo);
            Task.Run(() =>
            {
                try { trabalho(); }
                catch (UnauthorizedAccessException ex)
                {
                    Log("Sem permissão para escrever na pasta do jogo (" + ex.Message + "). Se ele está em \"Arquivos de Programas\", abra o instalador como administrador.");
                }
                catch (Exception ex) { Log("Erro: " + ex.Message); }
            }).ContinueWith(_ =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
            {
                if (IsDisposed) return;
                _ocupado = false;
                UseWaitCursor = false;
                Preencher();
                EscolherModDoSite();
                Capturar();
            }));
            });
        }

        // --captura=arquivo.png: grava a janela depois da primeira verificação e fecha (para conferir o layout).
        void Capturar()
        {
            string arg = _args.FirstOrDefault(a => a.StartsWith("--captura="));
            if (arg == null) return;
            if (_lista.Items.Count > 0) _lista.Items[_lista.Items.Count - 1].Selected = true;
            using (var b = new Bitmap(Width, Height))
            {
                DrawToBitmap(b, new Rectangle(0, 0, Width, Height));
                b.Save(arg.Substring("--captura=".Length), System.Drawing.Imaging.ImageFormat.Png);
            }
            Close();
        }

        // O site pediu pbsloader://mod/<id>: marca o mod (com as dependências dele), mostra na lista e avisa no log.
        // Tenta a cada verificação até conseguir; depois para, para não remarcar o que o usuário desmarcar à mão.
        void EscolherModDoSite()
        {
            if (_escolhidoDoSite || _modDoSite == null || _lista.Items.Count == 0) return;
            _escolhidoDoSite = true;
            var item = _lista.Items.Cast<ListViewItem>().FirstOrDefault(it =>
                string.Equals(((EstadoMod)it.Tag).Mod.Id, _modDoSite, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                Log("O site pediu o mod \"" + _modDoSite + "\", mas ele não está no catálogo.");
                return;
            }
            _marcando = true;
            try { item.Checked = true; Normalizar(); }
            finally { _marcando = false; }
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            Log("O site pediu o mod \"" + ((EstadoMod)item.Tag).Mod.Nome + "\": marcado para instalar — clique em \"Aplicar\".");
        }

        void Habilitar()
        {
            bool valido = _cat != null && Jogo.Valida(_jogo, _cat);
            foreach (var c in new Control[] { _aplicar, _abrirMods, _jogar, _removerTudo, _lista }) c.Enabled = !_ocupado && valido;
            _escolher.Enabled = _verificar.Enabled = _beta.Enabled = _dev.Enabled = _editarPastasDev.Enabled = !_ocupado;
        }

        void Log(string linha)
        {
            if (InvokeRequired) { BeginInvoke((Action<string>)Log, linha); return; }
            _log.AppendText((_log.TextLength > 0 ? Environment.NewLine : "") + linha);
        }

        static void AbrirPasta(string pasta)
        {
            Directory.CreateDirectory(pasta);
            Process.Start(new ProcessStartInfo(pasta) { UseShellExecute = true });
        }

        static void AbrirUrl(string url)
        {
            if (url != null && url.StartsWith("https://")) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
