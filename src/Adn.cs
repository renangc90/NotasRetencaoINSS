using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web.Script.Serialization;

namespace NotasRetencaoINSS
{
    public class DocDistribuido
    {
        public string Nsu;
        public string Schema;       // NF-e: resNFe/procNFe/resEvento/procEventoNFe · NFS-e: NFSE/EVENTO
        public string ChaveAcesso;
        public string Xml;
    }

    public class RetornoAdn
    {
        public string Status;
        public long UltNsu;
        public bool Acabou;
        public List<DocDistribuido> Docs = new List<DocDistribuido>();
    }

    /// <summary>
    /// NFS-e no padrão nacional: distribuição do ADN (Ambiente de Dados Nacional).
    /// Entrega as notas em que o CNPJ é prestador, tomador ou intermediário.
    /// </summary>
    public static class Adn
    {
        public const int TamanhoLote = 50;

        public static string UrlBase(Configuracao cfg)
        {
            if (!string.IsNullOrEmpty(cfg.UrlAdn)) return cfg.UrlAdn.TrimEnd('/');
            return cfg.Ambiente == 2 ? "https://adn.producaorestrita.nfse.gov.br/contribuintes" : "https://adn.nfse.gov.br/contribuintes";
        }

        public static RetornoAdn Distribuir(X509Certificate2 cert, Configuracao cfg, long ultNsu)
        {
            string url = UrlBase(cfg) + "/DFe/" + (ultNsu + 1) + "?cnpjConsulta=" + cfg.Cnpj + "&lote=true";
            RespostaHttp r = ClienteFiscal.Get(cert, url, "ADN (NFS-e Nacional)");
            if (r.Status == 401 || r.Status == 403) throw new ErroAviso(ClienteFiscal.ErroCertificado("ADN", r.Status, r.Texto));
            return LerRetorno(r.Status, r.Texto, ultNsu);
        }

        /// <summary>
        /// PDF oficial da NFS-e (DANFSe): GET {base}/danfse/{chave}, com o mesmo certificado.
        /// Endereço tirado de clientes de terceiros — ainda não conferido no ADN real.
        /// </summary>
        public static string UrlDanfse(Configuracao cfg)
        {
            if (!string.IsNullOrEmpty(cfg.UrlDanfse)) return cfg.UrlDanfse.TrimEnd('/');
            return cfg.Ambiente == 2 ? "https://adn.producaorestrita.nfse.gov.br/danfse" : "https://adn.nfse.gov.br/danfse";
        }

        /// <summary>Devolve o PDF ou lança ErroAviso com o motivo (que a tela mostra no fim).</summary>
        public static byte[] Danfse(X509Certificate2 cert, Configuracao cfg, string chave)
        {
            RespostaArquivo r = ClienteFiscal.BaixarArquivo(cert, UrlDanfse(cfg) + "/" + chave, "application/pdf", "ADN (PDF da NFS-e)");
            string texto = r.Bytes == null ? "" : Encoding.UTF8.GetString(r.Bytes, 0, Math.Min(r.Bytes.Length, 300));
            if (r.Status == 401 || r.Status == 403) throw new ErroAviso(ClienteFiscal.ErroCertificado("ADN", r.Status, texto));
            if (r.Status == 404) throw new ErroAviso("o ADN não achou o PDF da nota (HTTP 404)");
            if (r.Status >= 300) throw new ErroAviso("o ADN respondeu HTTP " + r.Status + " ao pedir o PDF");
            if (!EhPdf(r.Bytes)) throw new ErroAviso("o ADN não devolveu um PDF (veio: " + Resumir(texto) + ")");
            return r.Bytes;
        }

        public static bool EhPdf(byte[] b)
        {
            return b != null && b.Length > 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F';
        }

        static string Resumir(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim();
            if (s == "") return "resposta vazia";
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        public static string Gunzip(byte[] gz)
        {
            using (var entrada = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress))
            using (var saida = new MemoryStream())
            {
                entrada.CopyTo(saida);
                return Encoding.UTF8.GetString(saida.ToArray());
            }
        }

        static object Campo(IDictionary<string, object> d, string nome)
        {
            if (d == null) return null;
            foreach (var kv in d) if (string.Equals(kv.Key, nome, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        static string Str(object o) { return o == null ? "" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>Lê o JSON do ADN (público para os testes).</summary>
        public static RetornoAdn LerRetorno(int httpStatus, string texto, long ultNsu)
        {
            IDictionary<string, object> json = null;
            try { json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(texto) as IDictionary<string, object>; }
            catch (ArgumentException) { }
            if (json == null && httpStatus != 404)
                throw new ErroAviso("ADN respondeu HTTP " + httpStatus + ": " + (texto ?? "").Substring(0, Math.Min(200, (texto ?? "").Length)));

            var erros = Campo(json, "Erros") as IList;
            string status = Str(Campo(json, "StatusProcessamento"));
            if (status == "") status = httpStatus == 404 ? "NENHUM_DOCUMENTO_LOCALIZADO" : "HTTP " + httpStatus;

            // Fila em dia: o ADN pode mandar junto um "erro" dizendo que não há documento — é o fim normal.
            if (status == "NENHUM_DOCUMENTO_LOCALIZADO")
                return new RetornoAdn { Status = status, UltNsu = ultNsu, Acabou = true };

            var lote = Campo(json, "LoteDFe") as IList;
            if (status == "REJEICAO" || (erros != null && erros.Count > 0 && lote == null))
            {
                var motivos = new List<string>();
                if (erros != null)
                    foreach (object e in erros)
                    {
                        var d = e as IDictionary<string, object>;
                        string m = (Str(Campo(d, "Codigo")) + " - " + Str(Campo(d, "Descricao"))).Trim(' ', '-');
                        if (m != "") motivos.Add(m);
                    }
                throw new ErroAviso("ADN recusou a consulta: " + (motivos.Count > 0 ? string.Join("; ", motivos) : status));
            }
            if (lote == null && httpStatus >= 300)
                throw new ErroAviso("ADN respondeu HTTP " + httpStatus + " sem documentos. Tente novamente mais tarde.");

            var ret = new RetornoAdn { Status = status, UltNsu = ultNsu };
            int itens = 0;
            if (lote != null)
                foreach (object o in lote)
                {
                    itens++;
                    var item = o as IDictionary<string, object>;
                    long nsu;
                    if (long.TryParse(Str(Campo(item, "NSU")), out nsu) && nsu > ret.UltNsu) ret.UltNsu = nsu;
                    string arquivo = Str(Campo(item, "ArquivoXml"));
                    if (arquivo == "") continue;
                    string tipo = Str(Campo(item, "TipoDocumento"));
                    ret.Docs.Add(new DocDistribuido
                    {
                        Nsu = nsu.ToString(),
                        Schema = tipo == "" ? "NFSE" : tipo,
                        ChaveAcesso = Str(Campo(item, "ChaveAcesso")),
                        Xml = Gunzip(Convert.FromBase64String(arquivo)),
                    });
                }
            // Lote cheio = provavelmente há mais; menos que 50 = acabou.
            ret.Acabou = itens < TamanhoLote;
            return ret;
        }
    }
}
