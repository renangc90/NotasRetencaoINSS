using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NotasRetencaoINSS
{
    /// <summary>Certificado, CNPJ, UF e a opção de NF-e. Abre sozinha no primeiro uso.</summary>
    public class TelaConfiguracao : Form
    {
        readonly Configuracao cfg;
        readonly RadioButton rbWindows = new RadioButton { Text = "Instalado neste computador (A1 importado ou token/cartão A3)", AutoSize = true };
        readonly RadioButton rbPfx = new RadioButton { Text = "Arquivo do certificado A1 (.pfx)", AutoSize = true };
        readonly ComboBox cbCertificados = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 560 };
        readonly Button btAtualizar = new Button { Text = "Atualizar lista", AutoSize = true };
        readonly TextBox txPfx = new TextBox { Width = 470 };
        readonly Button btProcurar = new Button { Text = "Procurar…", AutoSize = true };
        readonly TextBox txSenha = new TextBox { Width = 200, UseSystemPasswordChar = true };
        readonly TextBox txCnpj = new TextBox { Width = 160 };
        readonly Label lbNome = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        readonly ComboBox cbUf = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        readonly CheckBox ckNfe = new CheckBox { Text = "Incluir NF-e (notas de mercadoria)", AutoSize = true };
        string nomeEntidade;

        public TelaConfiguracao(Configuracao cfg)
        {
            this.cfg = cfg;
            Text = "Configurações";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var raiz = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            Controls.Add(raiz);

            raiz.Controls.Add(Titulo("Certificado digital da prefeitura"));
            raiz.Controls.Add(rbWindows);
            raiz.Controls.Add(Linha(20, cbCertificados, btAtualizar));
            raiz.Controls.Add(rbPfx);
            raiz.Controls.Add(Linha(20, txPfx, btProcurar));
            raiz.Controls.Add(Linha(20, Rotulo("Senha:"), txSenha));

            raiz.Controls.Add(Titulo("Prefeitura"));
            raiz.Controls.Add(Linha(0, Rotulo("CNPJ consultado:"), txCnpj, lbNome));
            raiz.Controls.Add(Linha(0, Rotulo("UF:"), cbUf, Rotulo("(usada só nas NF-e)", true)));

            raiz.Controls.Add(Titulo("NF-e"));
            raiz.Controls.Add(ckNfe);
            raiz.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(640, 0),
                Margin = new Padding(20, 2, 3, 3),
                ForeColor = SystemColors.GrayText,
                Text = "Para ler a retenção de uma NF-e, a SEFAZ exige registrar a \"Ciência da Operação\" em cada nota, em nome da prefeitura. "
                     + "É um evento oficial que só diz que a prefeitura sabe da nota (não confirma nem recusa a compra) e fica registrado na SEFAZ. "
                     + "Marque só com o OK de quem cuida das NF-e da prefeitura.\n"
                     + "A SEFAZ só entrega as NF-e dos últimos 3 meses, e o XML completo chega na busca seguinte (a SEFAZ pede 1 hora entre consultas).",
            });

            var btSalvar = new Button { Text = "Salvar", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
            var btCancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            var botoes = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 660, Height = 36, Margin = new Padding(0, 14, 0, 0) };
            botoes.Controls.Add(btCancelar);
            botoes.Controls.Add(btSalvar);
            raiz.Controls.Add(botoes);
            AcceptButton = btSalvar;
            CancelButton = btCancelar;

            cbUf.Items.AddRange(Sefaz.UfCodigo.Keys.OrderBy(k => k).Cast<object>().ToArray());
            rbWindows.CheckedChanged += (s, e) => AjustarOrigem();
            rbPfx.CheckedChanged += (s, e) => AjustarOrigem();
            btAtualizar.Click += (s, e) => CarregarCertificados(null);
            btProcurar.Click += (s, e) => ProcurarPfx();
            cbCertificados.SelectedIndexChanged += (s, e) => UsarCertificado(cbCertificados.SelectedItem as CertificadoInfo);
            btSalvar.Click += (s, e) => Salvar();

            nomeEntidade = cfg.NomeEntidade;
            txCnpj.Text = Formatos.CnpjCpf(cfg.Cnpj);
            lbNome.Text = cfg.NomeEntidade ?? "";
            if (cfg.Uf != null) cbUf.SelectedItem = cfg.Uf;
            ckNfe.Checked = cfg.IncluirNfe;
            txPfx.Text = cfg.PfxCaminho ?? "";
            if (cfg.OrigemCertificado == "pfx") rbPfx.Checked = true; else rbWindows.Checked = true;
            CarregarCertificados(cfg.CertificadoThumbprint);
            AjustarOrigem();
        }

        static Label Titulo(string texto)
        {
            return new Label { Text = texto, AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Margin = new Padding(0, 12, 3, 4) };
        }

        static Label Rotulo(string texto, bool cinza = false)
        {
            return new Label { Text = texto, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3), ForeColor = cinza ? SystemColors.GrayText : SystemColors.ControlText };
        }

        static FlowLayoutPanel Linha(int recuo, params Control[] controles)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(recuo, 0, 0, 0) };
            p.Controls.AddRange(controles);
            return p;
        }

        void AjustarOrigem()
        {
            cbCertificados.Enabled = btAtualizar.Enabled = rbWindows.Checked;
            txPfx.Enabled = btProcurar.Enabled = txSenha.Enabled = rbPfx.Checked;
        }

        void CarregarCertificados(string thumbprint)
        {
            Cursor = Cursors.WaitCursor;
            List<CertificadoInfo> lista = Certificados.ListarInstalados();
            Cursor = Cursors.Default;
            cbCertificados.Items.Clear();
            foreach (CertificadoInfo c in lista) cbCertificados.Items.Add(c);
            if (lista.Count == 0) return;
            CertificadoInfo escolhido = lista.FirstOrDefault(c => string.Equals(c.Certificado.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
            // Primeiro uso: sugere o e-CNPJ válido da prefeitura configurada; senão, o e-CNPJ válido mais novo.
            if (escolhido == null && string.IsNullOrEmpty(thumbprint))
            {
                string raiz = Formatos.SoDigitos(cfg.Cnpj);
                raiz = raiz.Length >= 8 ? raiz.Substring(0, 8) : "?";
                escolhido = lista.FirstOrDefault(c => c.Cnpj.StartsWith(raiz) && !c.Vencido) ?? lista.FirstOrDefault(c => c.Cnpj != "" && !c.Vencido);
            }
            if (escolhido != null) cbCertificados.SelectedItem = escolhido;
        }

        void UsarCertificado(CertificadoInfo c)
        {
            if (c == null || string.IsNullOrEmpty(c.Cnpj)) return;
            txCnpj.Text = Formatos.CnpjCpf(c.Cnpj);
            nomeEntidade = c.Nome;
            lbNome.Text = c.Nome;
        }

        void ProcurarPfx()
        {
            using (var dlg = new OpenFileDialog { Filter = "Certificado A1 (*.pfx;*.p12)|*.pfx;*.p12|Todos os arquivos|*.*", Title = "Escolha o certificado da prefeitura" })
                if (dlg.ShowDialog(this) == DialogResult.OK) txPfx.Text = dlg.FileName;
        }

        void Salvar()
        {
            try
            {
                CertificadoInfo cert;
                if (rbWindows.Checked)
                {
                    cert = cbCertificados.SelectedItem as CertificadoInfo;
                    if (cert == null) throw new ErroAviso("Escolha o certificado na lista. Se ele não aparece, use a opção do arquivo .pfx.");
                }
                else
                {
                    if (txPfx.Text.Trim() == "") throw new ErroAviso("Escolha o arquivo .pfx do certificado.");
                    string senha = txSenha.Text;
                    if (senha == "" && cfg.PfxCaminho == txPfx.Text.Trim()) senha = Armazenamento.Desproteger(cfg.PfxSenhaProtegida);
                    cert = Certificados.AbrirPfx(txPfx.Text.Trim(), senha);
                    cfg.PfxSenhaProtegida = Armazenamento.Proteger(senha);
                    UsarCertificado(cert);
                }

                string cnpj = Formatos.SoDigitos(txCnpj.Text);
                if (cnpj.Length != 14) throw new ErroAviso("Informe o CNPJ da prefeitura (14 dígitos).");
                if (cert.Vencido && MessageBox.Show(this, "Este certificado venceu em " + cert.ValidoAte.ToString("dd/MM/yyyy") + " e não vai funcionar. Salvar assim mesmo?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                if (ckNfe.Checked && cbUf.SelectedItem == null) throw new ErroAviso("Para incluir NF-e, escolha a UF da prefeitura.");
                if (!string.IsNullOrEmpty(cert.Cnpj) && cert.Cnpj.Substring(0, 8) != cnpj.Substring(0, 8)
                    && MessageBox.Show(this, "O CNPJ informado não é o do certificado (" + Formatos.CnpjCpf(cert.Cnpj) + "). O ADN e a SEFAZ costumam recusar. Salvar assim mesmo?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                // Trocou de CNPJ: o NSU guardado é de outra fila, começa do zero.
                if (cfg.Cnpj != null && cfg.Cnpj != cnpj)
                {
                    cfg.UltNsuNfse = "0";
                    cfg.UltNsuNfe = "0";
                    cfg.NfeLiberadaEm = null;
                }
                cfg.OrigemCertificado = rbWindows.Checked ? "windows" : "pfx";
                cfg.CertificadoThumbprint = rbWindows.Checked ? cert.Certificado.Thumbprint : null;
                cfg.PfxCaminho = rbPfx.Checked ? txPfx.Text.Trim() : null;
                if (rbWindows.Checked) cfg.PfxSenhaProtegida = null;
                cfg.Cnpj = cnpj;
                cfg.NomeEntidade = nomeEntidade;
                cfg.Uf = cbUf.SelectedItem as string;
                cfg.IncluirNfe = ckNfe.Checked;
                Armazenamento.SalvarConfiguracao(cfg);
                DialogResult = DialogResult.OK;
            }
            catch (ErroAviso ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
