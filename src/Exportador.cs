using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NotasRetencaoINSS
{
    public class ResultadoExportacao
    {
        public string Pasta;
        public int Xml;
        public int Pdf;
        public int SemXml;          // NF-e que ainda só têm o resumo
        public int PdfFalhou;       // XML que não deu para transformar em PDF
        public string MotivoPdf;
        public int Nfe;             // NF-e não têm PDF pelo programa
        public bool Parado;

        public string Resumo()
        {
            var s = new StringBuilder();
            s.Append(Xml + " XML e " + Pdf + " PDF salvos em:\n" + Pasta);
            if (PdfFalhou > 0) s.Append("\n\n" + PdfFalhou + " NFS-e ficaram sem PDF (o XML foi salvo). Motivo: " + MotivoPdf);
            if (Nfe > 0) s.Append("\n\n" + Nfe + " NF-e saíram só com o XML: o programa só monta o PDF de NFS-e.");
            if (SemXml > 0) s.Append("\n\n" + SemXml + " nota(s) ficaram de fora por ainda não ter o XML completo (NF-e só com o resumo: o XML chega nas próximas buscas).");
            if (Parado) s.Append("\n\nInterrompido antes do fim.");
            return s.ToString();
        }
    }

    /// <summary>
    /// "Salvar XML e PDF": copia o XML guardado de cada nota e, para NFS-e, gera o PDF a partir dele
    /// (Danfse). Nome dos arquivos: "2026-10-21 NFS-e 850393 NOME DO PRESTADOR.xml/.pdf".
    /// </summary>
    public class Exportador
    {
        volatile bool parar;
        public void Parar() { parar = true; }

        readonly Func<Nota, bool> cancelada;
        readonly Action<string> progresso;

        public Exportador(Func<Nota, bool> cancelada, Action<string> progresso)
        {
            this.cancelada = cancelada ?? (n => false);
            this.progresso = progresso ?? (s => { });
        }

        public ResultadoExportacao Executar(IList<Nota> notas, string pasta)
        {
            parar = false;
            Directory.CreateDirectory(pasta);
            var res = new ResultadoExportacao { Pasta = pasta };
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < notas.Count; i++)
            {
                if (parar) { res.Parado = true; break; }
                Nota n = notas[i];
                progresso("Salvando os arquivos das notas (" + (i + 1) + " de " + notas.Count + ")…");

                string xml = string.IsNullOrEmpty(n.ArquivoXml) ? null : Armazenamento.CaminhoCompleto(n.ArquivoXml);
                if (xml == null || !File.Exists(xml)) { res.SemXml++; continue; }

                string nome = NomeUnico(n, usados);
                Gravar(Path.Combine(pasta, nome + ".xml"), File.ReadAllBytes(xml));
                res.Xml++;

                if (n.Tipo != "nfse") { res.Nfe++; continue; }
                byte[] pdf;
                try { pdf = Danfse.GerarPdf(File.ReadAllText(xml, Encoding.UTF8), cancelada(n)); }
                catch (Exception ex)
                {
                    res.PdfFalhou++;
                    res.MotivoPdf = ex is ErroAviso ? ex.Message : "XML ilegível (" + ex.Message + ")";
                    continue;
                }
                Gravar(Path.Combine(pasta, nome + ".pdf"), pdf);
                res.Pdf++;
            }
            return res;
        }

        /// <summary>Gera o PDF de uma NFS-e na pasta de dados (dados\pdf\...) para abrir na tela. Devolve o caminho.</summary>
        public static string PdfParaAbrir(Nota n, bool cancelada)
        {
            if (n.Tipo != "nfse") throw new ErroAviso("O programa só monta o PDF de NFS-e. Para NF-e, use o XML.");
            string xml = string.IsNullOrEmpty(n.ArquivoXml) ? null : Armazenamento.CaminhoCompleto(n.ArquivoXml);
            if (xml == null || !File.Exists(xml)) throw new ErroAviso("O XML desta nota não está na pasta de dados.");
            byte[] pdf = Danfse.GerarPdf(File.ReadAllText(xml, Encoding.UTF8), cancelada);
            string caminho = Armazenamento.CaminhoPdf(n);
            Directory.CreateDirectory(Path.GetDirectoryName(caminho));
            try { File.WriteAllBytes(caminho, pdf); }
            catch (IOException)
            {
                // Aberto no leitor de PDF: grava ao lado com outro nome.
                caminho = Path.Combine(Path.GetDirectoryName(caminho), n.Chave + " " + DateTime.Now.ToString("HHmmss") + ".pdf");
                File.WriteAllBytes(caminho, pdf);
            }
            return caminho;
        }

        static void Gravar(string destino, byte[] conteudo)
        {
            try { File.WriteAllBytes(destino, conteudo); }
            catch (IOException ex) { throw new ErroAviso("Não foi possível salvar " + Path.GetFileName(destino) + " (o arquivo está aberto em outro programa?):\n" + ex.Message); }
            catch (UnauthorizedAccessException) { throw new ErroAviso("Sem permissão para gravar em " + Path.GetDirectoryName(destino) + ". Escolha outra pasta."); }
        }

        static string NomeUnico(Nota n, HashSet<string> usados)
        {
            string nome = NomeArquivo(n);
            if (!usados.Add(nome))
            {
                nome = nome + " " + n.Chave;
                usados.Add(nome);
            }
            return nome;
        }

        /// <summary>"2026-10-21 NFS-e 850393 VOLUS INSTITUICAO DE PAGAMENTO LTDA" (sem caracteres proibidos no Windows).</summary>
        public static string NomeArquivo(Nota n)
        {
            string texto = (string.IsNullOrEmpty(n.Emissao) ? "sem-data" : n.Emissao) + " " + (n.Tipo == "nfe" ? "NF-e" : "NFS-e") + " "
                           + (string.IsNullOrEmpty(n.Numero) ? n.Chave : n.Numero) + " " + (n.EmitenteNome ?? "");
            var s = new StringBuilder();
            foreach (char c in texto) s.Append(Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? ' ' : c);
            string nome = Regex.Replace(s.ToString(), @"\s+", " ").Trim().TrimEnd('.', ' ');
            return nome.Length > 120 ? nome.Substring(0, 120).TrimEnd(' ', '.') : nome;
        }
    }
}
