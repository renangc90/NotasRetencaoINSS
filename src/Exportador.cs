using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NotasRetencaoINSS
{
    public class ResultadoExportacao
    {
        public string Pasta;
        public int Xml;
        public int Pdf;
        public int SemXml;          // NF-e que ainda só têm o resumo
        public int PdfFaltando;     // NFS-e sem PDF (falhou, desistiu ou sem certificado)
        public int Nfe;             // NF-e não têm PDF pelo programa
        public bool SemCertificado;
        public bool DesistiuPdf;
        public string MotivoPdf;
        public bool Parado;

        public string Resumo()
        {
            var s = new StringBuilder();
            s.Append(Xml + " XML e " + Pdf + " PDF salvos em:\n" + Pasta);
            if (PdfFaltando > 0)
            {
                s.Append("\n\n" + PdfFaltando + " NFS-e ficaram sem PDF");
                if (SemCertificado) s.Append(": sem o certificado o programa não busca o PDF no ADN.");
                else if (DesistiuPdf) s.Append(". O ADN recusou o PDF " + Exportador.DesistirAposFalhas + " vezes seguidas e o programa parou de pedir. Último motivo: " + MotivoPdf + ".");
                else s.Append(". Último motivo: " + MotivoPdf + ". Tente salvar de novo mais tarde (os PDF que já vieram não são baixados outra vez).");
            }
            if (Nfe > 0) s.Append("\n\n" + Nfe + " NF-e saíram só com o XML: a SEFAZ não entrega o PDF (DANFE).");
            if (SemXml > 0) s.Append("\n\n" + SemXml + " nota(s) ficaram de fora por ainda não ter o XML completo (NF-e só com o resumo: o XML chega nas próximas buscas).");
            if (Parado) s.Append("\n\nInterrompido antes do fim.");
            return s.ToString();
        }
    }

    /// <summary>
    /// "Salvar XML e PDF": copia o XML guardado de cada nota e, para NFS-e, o PDF oficial (DANFSe)
    /// baixado do ADN — guardado em dados\pdf para não baixar de novo. Nome dos arquivos:
    /// "2026-10-21 NFS-e 850393 NOME DO PRESTADOR.xml/.pdf".
    /// </summary>
    public class Exportador
    {
        public const int DesistirAposFalhas = 3;
        public int EsperaPdfMs = 200;

        volatile bool parar;
        public void Parar() { parar = true; }

        readonly Configuracao cfg;
        readonly CertificadoInfo cert;   // null = só XML (e PDF que já estiverem guardados)
        readonly Action<string> progresso;

        public Exportador(Configuracao cfg, CertificadoInfo cert, Action<string> progresso)
        {
            this.cfg = cfg;
            this.cert = cert;
            this.progresso = progresso ?? (s => { });
        }

        /// <summary>Alguma destas NFS-e ainda não tem o PDF guardado? (Só então o certificado é necessário.)</summary>
        public static bool PrecisaBaixarPdf(IEnumerable<Nota> notas)
        {
            return notas.Any(n => n.Tipo == "nfse" && !File.Exists(Armazenamento.CaminhoPdf(n)));
        }

        public ResultadoExportacao Executar(IList<Nota> notas, string pasta)
        {
            parar = false;
            Directory.CreateDirectory(pasta);
            var res = new ResultadoExportacao { Pasta = pasta, SemCertificado = cert == null };
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool pedirPdf = cert != null;
            int falhasSeguidas = 0;

            for (int i = 0; i < notas.Count; i++)
            {
                if (parar) { res.Parado = true; break; }
                Nota n = notas[i];
                progresso("Salvando os arquivos das notas (" + (i + 1) + " de " + notas.Count + ")…");

                string xml = string.IsNullOrEmpty(n.ArquivoXml) ? null : Armazenamento.CaminhoCompleto(n.ArquivoXml);
                if (xml == null || !File.Exists(xml)) { res.SemXml++; continue; }

                string nome = NomeUnico(n, usados);
                Copiar(xml, Path.Combine(pasta, nome + ".xml"));
                res.Xml++;

                if (n.Tipo != "nfse") { res.Nfe++; continue; }

                string pdf = Armazenamento.CaminhoPdf(n);
                if (!File.Exists(pdf))
                {
                    if (!pedirPdf) { res.PdfFaltando++; continue; }
                    try
                    {
                        Armazenamento.GravarPdf(n, Adn.Danfse(cert.Certificado, cfg, n.Chave));
                        falhasSeguidas = 0;
                    }
                    catch (ErroAviso ex)
                    {
                        res.PdfFaltando++;
                        res.MotivoPdf = ex.Message;
                        if (++falhasSeguidas >= DesistirAposFalhas) { pedirPdf = false; res.DesistiuPdf = true; }
                        continue;
                    }
                    if (EsperaPdfMs > 0) Thread.Sleep(EsperaPdfMs);
                }
                Copiar(pdf, Path.Combine(pasta, nome + ".pdf"));
                res.Pdf++;
            }
            return res;
        }

        static void Copiar(string origem, string destino)
        {
            try { File.Copy(origem, destino, true); }
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
