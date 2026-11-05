using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NotasRetencaoINSS
{
    /// <summary>Abre as telas com dados de exemplo e salva PNG, para conferir o visual sem clicar.</summary>
    static class Capturas
    {
        public static int Executar(string saida)
        {
            Directory.CreateDirectory(saida);
            Armazenamento.Pasta = Path.Combine(saida, "dados");
            System.Threading.Thread.CurrentThread.CurrentCulture = Formatos.PtBr;

            var cfg = new Configuracao
            {
                Cnpj = Amostras.CnpjPrefeitura, NomeEntidade = "MUNICIPIO DE TESTE", Uf = "MG", IncluirNfe = true, CertificadoThumbprint = "DEMO",
                UltimaBuscaEm = "2026-11-04T09:40:00", UltimaBuscaResumo = "12 NFS-e nova(s) · 3 NF-e nova(s)",
            };
            Armazenamento.SalvarConfiguracao(cfg);

            var repo = new Repositorio(new BaseNotas());
            string[] nomes = { "LIMPA TUDO SERVIÇOS LTDA", "VIGILÂNCIA SEGURA EIRELI", "CONSTRUTORA HORIZONTE S/A", "COPA ALIMENTAÇÃO COLETIVA LTDA" };
            for (int i = 1; i <= 14; i++)
            {
                string ch = Amostras.ChaveNfse(200 + i);
                string xml = Amostras.Nfse(ch, (1000 + i).ToString(), Amostras.CnpjPrefeitura, 1500m * i, i % 5 == 0 ? (decimal?)null : 165m * i, "2026-10-" + (i * 2).ToString("00") + "T10:00:00-03:00");
                Nota n = LeitorXml.LerNfse(xml, "NFSE", ch, i.ToString()).Nota;
                n.EmitenteNome = nomes[i % nomes.Length];
                n.EmitenteDoc = "1234567800" + (1000 + i % nomes.Length * 7).ToString().PadLeft(4, '0');
                repo.GravarNota(n, xml);
            }
            // Natureza escolhida numa nota da vigilância: as outras do mesmo prestador passam a sugerir a mesma (em cinza).
            Nota vigilancia = repo.Base.Notas.First(x => x.EmitenteNome.StartsWith("VIGIL"));
            repo.DefinirNatureza(vigilancia, "17031");
            string cancelada = Amostras.ChaveNfse(203);
            repo.GravarEvento(new EventoNota { Tipo = "nfse", Chave = cancelada, TpEvento = "101101", Nsu = "99", Cancela = true }, null);
            string chNfe = Amostras.ChaveNfe("5521");
            repo.GravarNota(LeitorXml.LerNfe(Amostras.ProcNfe(chNfe, "5521", 3200m, 352m), "procNFe", "1").Nota, Amostras.ProcNfe(chNfe, "5521", 3200m, 352m));
            string chRes = Amostras.ChaveNfe("5522");
            repo.GravarNota(LeitorXml.LerNfe(Amostras.ResNfe(chRes, 800m), "resNFe", "2").Nota, null);
            Armazenamento.SalvarNotas(repo.Base);

            Application.EnableVisualStyles();
            var principal = new TelaPrincipal();
            var timer = new Timer { Interval = 1200 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Salvar(principal, Path.Combine(saida, "tela-principal.png"));
                using (var conf = new TelaConfiguracao(Armazenamento.LerConfiguracao()))
                {
                    conf.Shown += (s2, e2) =>
                    {
                        Application.DoEvents();
                        Salvar(conf, Path.Combine(saida, "tela-configuracao.png"));
                        conf.Close();
                    };
                    conf.ShowDialog(principal);
                }
                principal.Close();
            };
            principal.Shown += (s, e) => timer.Start();
            Application.Run(principal);
            Console.WriteLine("Capturas em " + saida);
            return 0;
        }

        static void Salvar(Form f, string caminho)
        {
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(caminho, ImageFormat.Png);
            }
        }
    }
}
