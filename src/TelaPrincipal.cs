using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NotasRetencaoINSS
{
    public class TelaPrincipal : Form
    {
        Configuracao cfg;
        Repositorio repo;
        Sincronizador sincronizando;
        Exportador exportando;
        List<Nota> filtradas = new List<Nota>();

        bool Ocupado { get { return sincronizando != null || exportando != null; } }

        readonly Label lbEntidade = new Label { AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Margin = new Padding(3, 6, 12, 3) };
        readonly Button btConfig = new Button { Text = "Configurações", AutoSize = true };
        readonly DateTimePicker dtDe = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        readonly DateTimePicker dtAte = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        readonly TextBox txBusca = new TextBox { Width = 230 };
        readonly RadioButton rbComInss = new RadioButton { Text = "Só com INSS retido", AutoSize = true, Checked = true };
        readonly RadioButton rbComIr = new RadioButton { Text = "Só com IR retido", AutoSize = true };
        readonly RadioButton rbTodas = new RadioButton { Text = "Todas as notas recebidas", AutoSize = true };
        readonly CheckBox ckCanceladas = new CheckBox { Text = "Mostrar canceladas", AutoSize = true };
        readonly Button btBaixar = new Button { Text = "Baixar notas novas", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        readonly Button btParar = new Button { Text = "Parar", AutoSize = true, Visible = false };
        readonly Button btExcel = new Button { Text = "Exportar para Excel", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        readonly Button btArquivos = new Button { Text = "Salvar XML e PDF", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };
        readonly ToolTip dica = new ToolTip();
        readonly DataGridView grade = new DataGridView();
        readonly Label lbTotais = new Label { AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Margin = new Padding(3, 4, 3, 0) };
        readonly Label lbStatus = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(1400, 0), Margin = new Padding(3, 2, 3, 4) };
        readonly System.Windows.Forms.Timer adiarFiltro = new System.Windows.Forms.Timer { Interval = 300 };
        readonly Font fonteSugerida = new Font("Segoe UI", 9F, FontStyle.Italic);
        bool atualizandoNaturezas;

        public TelaPrincipal()
        {
            Text = "Notas com Retenção de INSS";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(1320, 700);
            MinimumSize = new Size(900, 480);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            var topo = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(8, 8, 8, 0) };
            topo.Controls.Add(lbEntidade);
            topo.Controls.Add(btConfig);

            var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8, 4, 8, 0) };
            filtros.Controls.AddRange(new Control[] { Rotulo("Emissão de"), dtDe, Rotulo("até"), dtAte, Rotulo("   Buscar:"), txBusca, Espaco(), rbComInss, rbComIr, rbTodas, ckCanceladas });

            var acoes = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(8, 2, 8, 6) };
            acoes.Controls.AddRange(new Control[] { btBaixar, btParar, btExcel, btArquivos });
            dica.SetToolTip(btArquivos, "Salva numa pasta o XML e o PDF das notas da lista.\nPara salvar só algumas, selecione as linhas antes (Ctrl ou Shift + clique).");

            var rodape = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 0, 8, 4) };
            rodape.Controls.Add(lbTotais);
            rodape.Controls.Add(lbStatus);

            ConfigurarGrade();
            Controls.Add(grade);
            Controls.Add(rodape);
            Controls.Add(acoes);
            Controls.Add(filtros);
            Controls.Add(topo);

            // Padrão: mês passado inteiro (a EFD-Reinf é mensal e vence no dia 15 do mês seguinte).
            DateTime inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dtDe.Value = inicioMes.AddMonths(-1);
            dtAte.Value = inicioMes.AddDays(-1);

            EventHandler filtrar = (s, e) => Filtrar();
            dtDe.ValueChanged += filtrar;
            dtAte.ValueChanged += filtrar;
            EventHandler opcao = (s, e) => { if (((RadioButton)s).Checked) Filtrar(); };
            rbComInss.CheckedChanged += opcao;
            rbComIr.CheckedChanged += opcao;
            rbTodas.CheckedChanged += opcao;
            ckCanceladas.CheckedChanged += filtrar;
            txBusca.TextChanged += (s, e) => { adiarFiltro.Stop(); adiarFiltro.Start(); };
            adiarFiltro.Tick += (s, e) => { adiarFiltro.Stop(); Filtrar(); };
            btConfig.Click += (s, e) => AbrirConfiguracao();
            btBaixar.Click += (s, e) => Baixar();
            btParar.Click += (s, e) =>
            {
                if (sincronizando != null) sincronizando.Parar();
                else if (exportando != null) exportando.Parar();
                else return;
                btParar.Enabled = false;
                btParar.Text = "Parando…";
            };
            btExcel.Click += (s, e) => Exportar();
            btArquivos.Click += (s, e) => SalvarArquivos();
            grade.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || (e.ColumnIndex >= 0 && grade.Columns[e.ColumnIndex].Name == "Natureza")) return;
                AbrirNota(e.RowIndex);
            };
            grade.CellMouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                Rectangle r = grade.GetCellDisplayRectangle(e.ColumnIndex < 0 ? 0 : e.ColumnIndex, e.RowIndex, false);
                MenuDaNota(e.RowIndex, new Point(r.Left + e.X, r.Top + e.Y));
            };
            grade.SelectionChanged += (s, e) => AtualizarBotaoArquivos();
            FormClosing += (s, e) =>
            {
                if (!Ocupado) return;
                string oQue = sincronizando != null ? "A busca" : "A gravação dos arquivos";
                if (MessageBox.Show(this, oQue + " ainda está em andamento. Parar e fechar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
                else if (sincronizando != null) sincronizando.Parar();
                else exportando.Parar();
            };
            Shown += (s, e) => Iniciar();
        }

        static Label Rotulo(string t) { return new Label { Text = t, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }; }
        static Control Espaco() { return new Label { Width = 16 }; }

        void ConfigurarGrade()
        {
            grade.Dock = DockStyle.Fill;
            grade.ReadOnly = false;   // só a coluna Natureza é editável (as outras são ReadOnly em Coluna())
            grade.AllowUserToAddRows = grade.AllowUserToDeleteRows = grade.AllowUserToResizeRows = false;
            grade.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grade.MultiSelect = true;
            // Sem seleção = "Salvar XML e PDF" pega a lista toda; a grade marcaria a 1ª linha sozinha.
            grade.DataBindingComplete += (s, e) => { grade.ClearSelection(); AtualizarBotaoArquivos(); };
            grade.RowHeadersVisible = false;
            grade.BackgroundColor = SystemColors.Window;
            grade.BorderStyle = BorderStyle.None;
            grade.AutoGenerateColumns = false;
            grade.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 248, 251);
            grade.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grade.EnableHeadersVisualStyles = false;
            grade.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            grade.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(220, 230, 241);
            grade.ShowCellToolTips = true;

            Coluna("Tipo", "Tipo", 50, null);
            Coluna("Numero", "Número", 70, null);
            Coluna("Emissao", "Emissão", 80, "dd/MM/yyyy");
            Coluna("Competencia", "Competência", 85, "dd/MM/yyyy");
            DataGridViewTextBoxColumn prestador = Coluna("Prestador", "Prestador / Emitente", 300, null);
            prestador.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            prestador.MinimumWidth = 200;
            Coluna("Documento", "CNPJ/CPF", 130, null);
            Coluna("Valor", "Valor da nota", 100, "N2").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            Coluna("Inss", "INSS retido", 90, "N2").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            Coluna("Ir", "IR retido", 85, "N2").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grade.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Natureza", DataPropertyName = "Natureza", HeaderText = "Natureza do rendimento", Width = 210,
                DataSource = TabelaReinf.Naturezas, ValueMember = "Codigo", DisplayMember = "Texto",
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing, FlatStyle = FlatStyle.Flat, DropDownWidth = 560,
                MaxDropDownItems = 20, SortMode = DataGridViewColumnSortMode.Automatic,
            });
            Coluna("CodReceita", "Cód. receita IR", 100, null);
            Coluna("Situacao", "Situação", 85, null);
            grade.Columns["Inss"].DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            grade.Columns["Ir"].DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            grade.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var linha = ((DataRowView)grade.Rows[e.RowIndex].DataBoundItem).Row;
                if ((string)linha["Situacao"] == "Cancelada") e.CellStyle.ForeColor = Color.Gray;
                else if (grade.Columns[e.ColumnIndex].Name == "Natureza" && !(bool)linha["Escolhida"])
                {
                    e.CellStyle.ForeColor = Color.DimGray;
                    e.CellStyle.Font = fonteSugerida;
                }
            };
            grade.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || grade.Columns[e.ColumnIndex].Name != "Natureza") return;
                var linha = ((DataRowView)grade.Rows[e.RowIndex].DataBoundItem).Row;
                NaturezaRendimento n = TabelaReinf.Buscar(linha["Natureza"] as string);
                e.ToolTipText = (n == null ? "" : n.Codigo + " – " + n.Descricao + " (" + n.Evento + ")\n")
                    + ((bool)linha["Escolhida"] ? "Escolhida por você." : "Sugestão do programa: clique para conferir ou trocar.");
            };
            // Um clique na Natureza já abre a lista; a escolha vale na hora.
            grade.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || grade.Columns[e.ColumnIndex].Name != "Natureza" || Ocupado) return;
                grade.CurrentCell = grade.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (grade.BeginEdit(true))
                {
                    var cb = grade.EditingControl as ComboBox;
                    if (cb != null) cb.DroppedDown = true;
                }
            };
            grade.CellBeginEdit += (s, e) => { if (Ocupado) e.Cancel = true; };
            grade.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grade.IsCurrentCellDirty && grade.CurrentCell is DataGridViewComboBoxCell) grade.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grade.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && grade.Columns[e.ColumnIndex].Name == "Natureza") NaturezaEscolhida(e.RowIndex);
            };
            grade.DataError += (s, e) => { e.ThrowException = false; };
            // Escolher na lista a mesma natureza que já estava sugerida também conta como conferida.
            grade.EditingControlShowing += (s, e) =>
            {
                var cb = e.Control as ComboBox;
                if (cb == null) return;
                cb.SelectionChangeCommitted -= ConfirmarNatureza;
                cb.SelectionChangeCommitted += ConfirmarNatureza;
            };
        }

        void ConfirmarNatureza(object sender, EventArgs e)
        {
            int linha = grade.CurrentCell == null ? -1 : grade.CurrentCell.RowIndex;
            if (linha < 0) return;
            BeginInvoke((Action)(() =>
            {
                if (linha >= grade.Rows.Count || grade.DataSource == null) return;
                grade.EndEdit();
                var row = ((DataRowView)grade.Rows[linha].DataBoundItem).Row;
                if (!(bool)row["Escolhida"]) NaturezaEscolhida(linha);
            }));
        }

        /// <summary>A pessoa escolheu a natureza de uma nota: grava, e as notas do mesmo prestador sem escolha passam a sugerir a mesma.</summary>
        void NaturezaEscolhida(int linhaGrade)
        {
            if (atualizandoNaturezas) return;
            var row = ((DataRowView)grade.Rows[linhaGrade].DataBoundItem).Row;
            string codigo = row["Natureza"] as string;
            if (TabelaReinf.Buscar(codigo) == null) return;
            Nota escolhida = filtradas[(int)row["Indice"]];
            repo.DefinirNatureza(escolhida, codigo);

            atualizandoNaturezas = true;
            try
            {
                foreach (DataRow r in ((DataTable)grade.DataSource).Rows)
                {
                    Nota n = filtradas[(int)r["Indice"]];
                    if (n != escolhida && (n.EmitenteDoc != escolhida.EmitenteDoc || n.Tipo != escolhida.Tipo || repo.NaturezaEscolhida(n))) continue;
                    string nat = repo.Natureza(n);
                    if (!Equals(r["Natureza"], nat)) r["Natureza"] = nat;
                    r["Escolhida"] = repo.NaturezaEscolhida(n);
                    r["CodReceita"] = CodigoReceitaIr(n, nat);
                }
            }
            finally { atualizandoNaturezas = false; }

            try { Armazenamento.SalvarNotas(repo.Base); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar a natureza escolhida:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        string CodigoReceitaIr(Nota n, string natureza)
        {
            if (n.IrRetido <= 0) return "";
            string c = TabelaReinf.CodigoIr(natureza, cfg.ClassTrib85);
            return c != "" ? c : "sem código";
        }

        DataGridViewTextBoxColumn Coluna(string campo, string titulo, int largura, string formato)
        {
            var c = new DataGridViewTextBoxColumn { Name = campo, DataPropertyName = campo, HeaderText = titulo, Width = largura, SortMode = DataGridViewColumnSortMode.Automatic, ReadOnly = true };
            if (formato != null) { c.DefaultCellStyle.Format = formato; c.DefaultCellStyle.FormatProvider = Formatos.PtBr; }
            grade.Columns.Add(c);
            return c;
        }

        void Iniciar()
        {
            if (Armazenamento.RodandoDePastaTemporaria())
                MessageBox.Show(this, "O programa está rodando de dentro do arquivo .zip. Extraia a pasta (botão direito → Extrair tudo) e abra o programa de lá, senão as notas baixadas se perdem.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try
            {
                cfg = Armazenamento.LerConfiguracao();
                repo = new Repositorio(Armazenamento.LerNotas());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível ler os dados em " + Armazenamento.Pasta + ":\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            AtualizarCabecalho();
            Filtrar();
            if (string.IsNullOrEmpty(cfg.CertificadoThumbprint) && string.IsNullOrEmpty(cfg.PfxCaminho)) AbrirConfiguracao();
        }

        void AtualizarCabecalho()
        {
            lbEntidade.Text = string.IsNullOrEmpty(cfg.Cnpj)
                ? "Nenhum certificado configurado"
                : (cfg.NomeEntidade ?? "Prefeitura") + " · CNPJ " + Formatos.CnpjCpf(cfg.Cnpj) + (cfg.Ambiente == 2 ? " · HOMOLOGAÇÃO" : "");
            string s = cfg.UltimaBuscaEm == null ? "Nenhuma busca feita ainda. Clique em \"Baixar notas novas\"." :
                "Última busca: " + Formatos.LerIso(cfg.UltimaBuscaEm).GetValueOrDefault().ToString("dd/MM/yyyy HH:mm") + " — " + cfg.UltimaBuscaResumo;
            int soResumo = repo == null ? 0 : repo.NfeSoResumo();
            if (soResumo > 0) s += "\n" + soResumo + " NF-e ainda só com o resumo (sem o XML completo não dá para saber a retenção); o XML chega nas próximas buscas.";
            lbStatus.Text = s;
        }

        bool Recebida(Nota n)
        {
            if (string.IsNullOrEmpty(cfg.Cnpj)) return false;
            // Resumo de NF-e não traz destinatário: quem recebe pela distribuição é o destinatário.
            if (string.IsNullOrEmpty(n.TomadorDoc)) return n.Tipo == "nfe";
            return n.TomadorDoc.Length == 14 && n.TomadorDoc.Substring(0, 8) == cfg.Cnpj.Substring(0, 8);
        }

        void Filtrar()
        {
            if (repo == null) return;
            string de = dtDe.Value.ToString("yyyy-MM-dd"), ate = dtAte.Value.ToString("yyyy-MM-dd");
            string busca = txBusca.Text.Trim().ToLowerInvariant();
            string buscaDigitos = Formatos.SoDigitos(busca);
            filtradas = repo.Base.Notas.Where(n =>
                    Recebida(n)
                    && n.Emissao != null && string.CompareOrdinal(n.Emissao, de) >= 0 && string.CompareOrdinal(n.Emissao, ate) <= 0
                    && (!rbComInss.Checked || n.InssRetido > 0)
                    && (!rbComIr.Checked || n.IrRetido > 0)
                    && (ckCanceladas.Checked || !repo.Cancelada(n))
                    && (busca == "" || (n.EmitenteNome ?? "").ToLowerInvariant().Contains(busca)
                        || (buscaDigitos != "" && ((n.EmitenteDoc ?? "").Contains(buscaDigitos) || n.Numero == buscaDigitos || (n.Chave ?? "").Contains(buscaDigitos)))))
                .OrderBy(n => n.Emissao).ThenBy(n => n.EmitenteNome).ToList();

            var t = new DataTable();
            t.Columns.Add("Indice", typeof(int));
            t.Columns.Add("Tipo", typeof(string));
            t.Columns.Add("Numero", typeof(string));
            t.Columns.Add("Emissao", typeof(DateTime));
            t.Columns.Add("Competencia", typeof(DateTime));
            t.Columns.Add("Prestador", typeof(string));
            t.Columns.Add("Documento", typeof(string));
            t.Columns.Add("Valor", typeof(decimal));
            t.Columns.Add("Inss", typeof(decimal));
            t.Columns.Add("Ir", typeof(decimal));
            t.Columns.Add("Natureza", typeof(string));
            t.Columns.Add("Escolhida", typeof(bool));
            t.Columns.Add("CodReceita", typeof(string));
            t.Columns.Add("Situacao", typeof(string));
            for (int i = 0; i < filtradas.Count; i++)
            {
                Nota n = filtradas[i];
                DateTime? comp = Formatos.ParaData(n.Competencia);
                string nat = repo.Natureza(n);
                t.Rows.Add(i, n.Tipo == "nfse" ? "NFS-e" : "NF-e", n.Numero, Formatos.ParaData(n.Emissao).Value,
                    comp.HasValue ? (object)comp.Value : DBNull.Value, n.EmitenteNome, Formatos.CnpjCpf(n.EmitenteDoc),
                    n.Valor, n.InssRetido, n.IrRetido, nat, repo.NaturezaEscolhida(n), CodigoReceitaIr(n, nat),
                    repo.Cancelada(n) ? "Cancelada" : Planilha.SituacaoTexto(n));
            }
            grade.DataSource = t;

            var ativas = filtradas.Where(n => !repo.Cancelada(n)).ToList();
            lbTotais.Text = ativas.Count + (ativas.Count == 1 ? " nota" : " notas") + " · Valor " + Formatos.Dinheiro(ativas.Sum(n => n.Valor))
                + " · INSS retido " + Formatos.Dinheiro(ativas.Sum(n => n.InssRetido))
                + " · IR retido " + Formatos.Dinheiro(ativas.Sum(n => n.IrRetido))
                + (filtradas.Count > ativas.Count ? "   (canceladas fora da soma)" : "");
            btExcel.Enabled = filtradas.Count > 0 && !Ocupado;
            AtualizarBotaoArquivos();
        }

        /// <summary>Linhas selecionadas (na ordem da tela) ou, sem seleção, a lista toda.</summary>
        List<Nota> NotasParaSalvar()
        {
            if (grade.SelectedRows.Count == 0) return filtradas.ToList();
            return grade.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => NotaDaLinha(r.Index)).ToList();
        }

        void AtualizarBotaoArquivos()
        {
            int sel = grade.SelectedRows.Count;
            btArquivos.Text = sel > 0 ? "Salvar XML e PDF (" + sel + (sel == 1 ? " selecionada)" : " selecionadas)") : "Salvar XML e PDF";
            btArquivos.Enabled = filtradas.Count > 0 && !Ocupado;
        }

        Nota NotaDaLinha(int linha)
        {
            var row = ((DataRowView)grade.Rows[linha].DataBoundItem).Row;
            return filtradas[(int)row["Indice"]];
        }

        /// <summary>Dois cliques: NFS-e abre o PDF (gerado do XML); NF-e mostra o XML na pasta.</summary>
        void AbrirNota(int linha)
        {
            Nota n = NotaDaLinha(linha);
            if (n.Tipo != "nfse") { AbrirXml(linha); return; }
            if (string.IsNullOrEmpty(n.ArquivoXml)) { AbrirXml(linha); return; }
            string caminho;
            try { caminho = Exportador.PdfParaAbrir(n, repo.Cancelada(n)); }
            catch (Exception ex)
            {
                string motivo = ex is ErroAviso ? ex.Message : "Não foi possível montar o PDF desta nota: " + ex.Message;
                if (MessageBox.Show(this, motivo + "\n\nAbrir a consulta pública da NFS-e no navegador? (a chave de acesso fica copiada para colar)", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) ConsultaPublica(linha);
                return;
            }
            try { Process.Start(caminho); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "O PDF foi gerado, mas não abriu (falta um leitor de PDF?):\n" + caminho + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void AbrirXml(int linha)
        {
            Nota n = NotaDaLinha(linha);
            if (string.IsNullOrEmpty(n.ArquivoXml))
            {
                MessageBox.Show(this, "Esta NF-e ainda só tem o resumo. O XML completo chega nas próximas buscas.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string caminho = Armazenamento.CaminhoCompleto(n.ArquivoXml);
            if (!File.Exists(caminho)) { MessageBox.Show(this, "Arquivo não encontrado:\n" + caminho, Text); return; }
            try { Process.Start("explorer.exe", "/select,\"" + caminho + "\""); } catch (Exception) { }
        }

        void CopiarChave(int linha)
        {
            Nota n = NotaDaLinha(linha);
            if (string.IsNullOrEmpty(n.Chave)) return;
            try { Clipboard.SetText(n.Chave); lbStatus.Text = "Chave de acesso copiada: " + n.Chave; }
            catch (Exception) { MessageBox.Show(this, "Chave de acesso:\n" + n.Chave, Text); }
        }

        /// <summary>Portal nacional da NFS-e: lá dá para ver e baixar o DANFSe oficial informando a chave.</summary>
        void ConsultaPublica(int linha)
        {
            CopiarChave(linha);
            try { Process.Start(Danfse.ConsultaPublica); }
            catch (Exception) { MessageBox.Show(this, "Abra no navegador: " + Danfse.ConsultaPublica, Text); }
        }

        /// <summary>Botão direito numa nota.</summary>
        void MenuDaNota(int linha, Point ondeNaGrade)
        {
            Nota n = NotaDaLinha(linha);
            bool nfse = n.Tipo == "nfse";
            var menu = new ContextMenuStrip();
            var pdf = menu.Items.Add("Abrir a nota (PDF)", null, (s, e) => AbrirNota(linha));
            pdf.Enabled = nfse && !string.IsNullOrEmpty(n.ArquivoXml);
            pdf.Font = new Font(pdf.Font, FontStyle.Bold);
            menu.Items.Add("Mostrar o XML na pasta", null, (s, e) => AbrirXml(linha)).Enabled = !string.IsNullOrEmpty(n.ArquivoXml);
            menu.Items.Add("Copiar a chave de acesso", null, (s, e) => CopiarChave(linha)).Enabled = !string.IsNullOrEmpty(n.Chave);
            if (nfse) menu.Items.Add("Consultar no portal nacional da NFS-e (DANFSe oficial)", null, (s, e) => ConsultaPublica(linha));
            menu.Closed += (s, e) => BeginInvoke((Action)(() => menu.Dispose()));
            menu.Show(grade, ondeNaGrade);
        }

        void AbrirConfiguracao()
        {
            if (Ocupado) return;
            using (var t = new TelaConfiguracao(cfg))
                if (t.ShowDialog(this) == DialogResult.OK) { AtualizarCabecalho(); Filtrar(); }
        }

        void Baixar()
        {
            if (string.IsNullOrEmpty(cfg.Cnpj)) { AbrirConfiguracao(); return; }
            CertificadoInfo cert;
            try { cert = Certificados.Carregar(cfg); }
            catch (ErroAviso ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            sincronizando = new Sincronizador(cfg, repo, cert, msg => BeginInvoke((Action)(() => lbStatus.Text = msg)));
            btBaixar.Enabled = btConfig.Enabled = btExcel.Enabled = btArquivos.Enabled = false;
            btParar.Visible = btParar.Enabled = true;
            btParar.Text = "Parar";
            Cursor = Cursors.AppStarting;
            lbStatus.Text = "Iniciando a busca…";

            var trabalho = new Thread(() =>
            {
                ResultadoBusca res = null;
                Exception erro = null;
                try { res = sincronizando.Executar(); }
                catch (Exception ex) { erro = ex; }
                BeginInvoke((Action)(() => TerminouBusca(res, erro)));
            }) { IsBackground = true };
            trabalho.Start();
        }

        void TerminouBusca(ResultadoBusca res, Exception erro)
        {
            sincronizando = null;
            btBaixar.Enabled = btConfig.Enabled = true;
            btParar.Visible = false;
            Cursor = Cursors.Default;
            AtualizarCabecalho();
            Filtrar();
            if (erro != null)
                MessageBox.Show(this, "A busca parou por um erro inesperado:\n" + erro.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (res.Avisos.Count > 0)
                MessageBox.Show(this, res.Resumo() + "\n\n" + string.Join("\n\n", res.Avisos), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void SalvarArquivos()
        {
            if (Ocupado) return;
            List<Nota> notas = NotasParaSalvar();
            if (notas.Count == 0) return;

            string pasta;
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Onde salvar o XML e o PDF " + (notas.Count == 1 ? "da nota selecionada" : "das " + notas.Count + " notas") + "?",
                ShowNewFolderButton = true,
                SelectedPath = !string.IsNullOrEmpty(cfg.PastaArquivos) && Directory.Exists(cfg.PastaArquivos)
                    ? cfg.PastaArquivos : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                pasta = dlg.SelectedPath;
            }

            cfg.PastaArquivos = pasta;
            try { Armazenamento.SalvarConfiguracao(cfg); } catch (Exception) { }

            exportando = new Exportador(repo.Cancelada, msg => BeginInvoke((Action)(() => lbStatus.Text = msg)));
            btBaixar.Enabled = btConfig.Enabled = btExcel.Enabled = btArquivos.Enabled = false;
            btParar.Visible = btParar.Enabled = true;
            btParar.Text = "Parar";
            Cursor = Cursors.AppStarting;
            lbStatus.Text = "Salvando os arquivos das notas…";

            Exportador e = exportando;
            var trabalho = new Thread(() =>
            {
                ResultadoExportacao res = null;
                Exception erro = null;
                try { res = e.Executar(notas, pasta); }
                catch (Exception ex) { erro = ex; }
                BeginInvoke((Action)(() => TerminouSalvar(res, erro, pasta)));
            }) { IsBackground = true };
            trabalho.Start();
        }

        void TerminouSalvar(ResultadoExportacao res, Exception erro, string pasta)
        {
            exportando = null;
            btBaixar.Enabled = btConfig.Enabled = true;
            btParar.Visible = false;
            Cursor = Cursors.Default;
            AtualizarCabecalho();
            Filtrar();
            if (erro != null)
            {
                MessageBox.Show(this, erro is ErroAviso ? erro.Message : "A gravação parou por um erro inesperado:\n" + erro.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool tudo = res.PdfFalhou == 0 && res.SemXml == 0 && !res.Parado;
            if (MessageBox.Show(this, res.Resumo() + "\n\nAbrir a pasta agora?", Text, MessageBoxButtons.YesNo,
                    tudo ? MessageBoxIcon.Information : MessageBoxIcon.Warning) == DialogResult.Yes)
                try { Process.Start("explorer.exe", "\"" + pasta + "\""); } catch (Exception) { }
        }

        void Exportar()
        {
            string nome = (rbComIr.Checked ? "Notas IR " : rbComInss.Checked ? "Notas INSS " : "Notas ") + dtDe.Value.ToString("dd-MM-yyyy") + " a " + dtAte.Value.ToString("dd-MM-yyyy") + ".xlsx";
            using (var dlg = new SaveFileDialog { Filter = "Planilha do Excel (*.xlsx)|*.xlsx", FileName = nome, InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string titulo = (rbComInss.Checked ? "Notas com retenção de INSS" : rbComIr.Checked ? "Notas com retenção de IR" : "Notas recebidas")
                        + " — " + (cfg.NomeEntidade ?? "") + " — CNPJ " + Formatos.CnpjCpf(cfg.Cnpj)
                        + " — emissão de " + dtDe.Value.ToString("dd/MM/yyyy") + " a " + dtAte.Value.ToString("dd/MM/yyyy");
                    Planilha.Gerar(dlg.FileName, titulo, filtradas, repo.Cancelada, repo.Natureza, cfg.ClassTrib85);
                    if (MessageBox.Show(this, "Planilha salva. Abrir agora?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Process.Start(dlg.FileName);
                }
                catch (IOException ex)
                {
                    MessageBox.Show(this, "Não foi possível salvar (o arquivo está aberto no Excel?):\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
