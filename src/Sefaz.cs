using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace NotasRetencaoINSS
{
    public class RetornoDistribuicao
    {
        public string CStat;
        public string XMotivo;
        public long UltNsu;
        public long MaxNsu;
        public List<DocDistribuido> Docs = new List<DocDistribuido>();
    }

    public class RetornoCiencia
    {
        public string Chave;
        public string CStat;
        public string XMotivo;
        public bool Ok { get { return CStat == "135" || CStat == "136" || CStat == "573"; } }  // 573 = já registrada antes
    }

    /// <summary>NF-e no Ambiente Nacional: Distribuição DF-e e Ciência da Operação (Recepção de Evento).</summary>
    public static class Sefaz
    {
        public static readonly Dictionary<string, int> UfCodigo = new Dictionary<string, int>
        {
            { "AC", 12 }, { "AL", 27 }, { "AM", 13 }, { "AP", 16 }, { "BA", 29 }, { "CE", 23 }, { "DF", 53 },
            { "ES", 32 }, { "GO", 52 }, { "MA", 21 }, { "MG", 31 }, { "MS", 50 }, { "MT", 51 }, { "PA", 15 },
            { "PB", 25 }, { "PE", 26 }, { "PI", 22 }, { "PR", 41 }, { "RJ", 33 }, { "RN", 24 }, { "RO", 11 },
            { "RR", 14 }, { "RS", 43 }, { "SC", 42 }, { "SE", 28 }, { "SP", 35 }, { "TO", 17 },
        };

        public const int EventosPorLote = 20;
        const string NsNfe = "http://www.portalfiscal.inf.br/nfe";

        static string UrlDistribuicao(Configuracao cfg)
        {
            if (!string.IsNullOrEmpty(cfg.UrlSefazDistribuicao)) return cfg.UrlSefazDistribuicao;
            return cfg.Ambiente == 2
                ? "https://hom1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx"
                : "https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx";
        }

        static string UrlEvento(Configuracao cfg)
        {
            if (!string.IsNullOrEmpty(cfg.UrlSefazEvento)) return cfg.UrlSefazEvento;
            return cfg.Ambiente == 2
                ? "https://hom1.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
                : "https://www.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        }

        static int TpAmb(Configuracao cfg) { return cfg.Ambiente == 2 ? 2 : 1; }

        static string Postar(X509Certificate2 cert, string url, string action, string envelope)
        {
            RespostaHttp r = ClienteFiscal.PostSoap(cert, url, action, envelope, "SEFAZ");
            if (r.Status == 401 || r.Status == 403) throw new ErroAviso(ClienteFiscal.ErroCertificado("SEFAZ", r.Status, r.Texto));
            if (r.Texto == null || !r.Texto.Contains("cStat"))
            {
                if (r.Status < 200 || r.Status >= 300) throw new ErroAviso("SEFAZ respondeu HTTP " + r.Status + ". Tente novamente mais tarde.");
                throw new ErroAviso("A SEFAZ respondeu sem resultado fiscal. O serviço pode estar fora do ar; tente mais tarde.");
            }
            return r.Texto;
        }

        // ------------------------------------------------------------ distribuição

        public static RetornoDistribuicao DistribuirPorNsu(X509Certificate2 cert, Configuracao cfg, long ultNsu)
        {
            int cUf;
            if (cfg.Uf == null || !UfCodigo.TryGetValue(cfg.Uf, out cUf)) throw new ErroAviso("Informe a UF da prefeitura em Configurações (a SEFAZ exige para NF-e).");
            const string ns = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe";
            string msg = "<distDFeInt xmlns=\"" + NsNfe + "\" versao=\"1.01\"><tpAmb>" + TpAmb(cfg) + "</tpAmb><cUFAutor>" + cUf + "</cUFAutor><CNPJ>" + cfg.Cnpj
                + "</CNPJ><distNSU><ultNSU>" + ultNsu.ToString("D15") + "</ultNSU></distNSU></distDFeInt>";
            string resposta = Postar(cert, UrlDistribuicao(cfg), ns + "/nfeDistDFeInteresse", ClienteFiscal.EnvelopeSoap(ns, "nfeDistDFeInteresse", msg));
            return LerRetornoDistribuicao(resposta);
        }

        /// <summary>Lê o retDistDFeInt (público para os testes).</summary>
        public static RetornoDistribuicao LerRetornoDistribuicao(string resposta)
        {
            XmlDocument doc = LeitorXml.Carregar(resposta);
            XmlElement ret = LeitorXml.No(doc, "retDistDFeInt");
            if (ret == null) throw new ErroAviso("Resposta da SEFAZ sem retDistDFeInt.");
            var r = new RetornoDistribuicao
            {
                CStat = LeitorXml.Texto(ret, "cStat"),
                XMotivo = LeitorXml.Texto(ret, "xMotivo"),
            };
            long n;
            if (long.TryParse(LeitorXml.Texto(ret, "ultNSU"), out n)) r.UltNsu = n;
            if (long.TryParse(LeitorXml.Texto(ret, "maxNSU"), out n)) r.MaxNsu = n;
            foreach (XmlElement z in ret.SelectNodes(".//*[local-name()='docZip']"))
            {
                string schema = z.GetAttribute("schema");
                int p = schema.IndexOf('_');
                r.Docs.Add(new DocDistribuido
                {
                    Nsu = z.GetAttribute("NSU").TrimStart('0'),
                    Schema = p > 0 ? schema.Substring(0, p) : schema,
                    Xml = Adn.Gunzip(Convert.FromBase64String(z.InnerText.Trim())),
                });
            }
            return r;
        }

        // ------------------------------------------------------------ ciência da operação

        /// <summary>Evento 210210 assinado (XMLDSig enveloped, RSA-SHA1, C14N), como pede o leiaute da NF-e.</summary>
        public static XmlElement MontarCiencia(X509Certificate2 cert, Configuracao cfg, string chave, string dhEvento)
        {
            string id = "ID210210" + chave + "01";
            string xml = "<evento xmlns=\"" + NsNfe + "\" versao=\"1.00\"><infEvento Id=\"" + id + "\"><cOrgao>91</cOrgao><tpAmb>" + TpAmb(cfg)
                + "</tpAmb><CNPJ>" + cfg.Cnpj + "</CNPJ><chNFe>" + chave + "</chNFe><dhEvento>" + dhEvento
                + "</dhEvento><tpEvento>210210</tpEvento><nSeqEvento>1</nSeqEvento><verEvento>1.00</verEvento><detEvento versao=\"1.00\"><descEvento>Ciencia da Operacao</descEvento></detEvento></infEvento></evento>";
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(xml);

            var sx = new SignedXml(doc) { SigningKey = ChavePrivada(cert) };
            sx.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
            sx.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
            var reference = new Reference("#" + id) { DigestMethod = SignedXml.XmlDsigSHA1Url };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigC14NTransform());
            sx.AddReference(reference);
            var ki = new KeyInfo();
            ki.AddClause(new KeyInfoX509Data(cert));
            sx.KeyInfo = ki;
            sx.ComputeSignature();
            doc.DocumentElement.AppendChild(doc.ImportNode(sx.GetXml(), true));
            return doc.DocumentElement;
        }

        static AsymmetricAlgorithm ChavePrivada(X509Certificate2 cert)
        {
            RSA rsa;
            try { rsa = cert.GetRSAPrivateKey(); }
            catch (CryptographicException ex) { throw new ErroAviso("Não foi possível usar a chave do certificado para assinar: " + ex.Message.Trim()); }
            if (rsa == null) throw new ErroAviso("O certificado escolhido não tem chave RSA para assinar.");
            return rsa;
        }

        /// <summary>Até 20 chaves por lote. Devolve o retorno de cada nota.</summary>
        public static List<RetornoCiencia> EnviarCiencia(X509Certificate2 cert, Configuracao cfg, IList<string> chaves)
        {
            if (chaves.Count == 0 || chaves.Count > EventosPorLote) throw new ArgumentException("Lote de ciência deve ter de 1 a 20 notas.");
            string dh = Formatos.AgoraBrasilia().ToString("yyyy-MM-ddTHH:mm:ss") + "-03:00";
            var sb = new StringBuilder();
            sb.Append("<envEvento xmlns=\"").Append(NsNfe).Append("\" versao=\"1.00\"><idLote>").Append(DateTime.Now.ToString("yyMMddHHmmssfff")).Append("</idLote>");
            foreach (string chave in chaves) sb.Append(MontarCiencia(cert, cfg, chave, dh).OuterXml);
            sb.Append("</envEvento>");

            const string ns = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";
            string resposta = Postar(cert, UrlEvento(cfg), ns + "/nfeRecepcaoEvento", ClienteFiscal.EnvelopeSoap(ns, "", sb.ToString()));
            return LerRetornoCiencia(resposta, chaves);
        }

        /// <summary>retEnvEvento: 128 = lote processado, cada retEvento traz o cStat da nota (público para os testes).</summary>
        public static List<RetornoCiencia> LerRetornoCiencia(string resposta, IList<string> chaves)
        {
            XmlDocument doc = LeitorXml.Carregar(resposta);
            XmlElement ret = LeitorXml.No(doc, "retEnvEvento");
            string cStatLote = LeitorXml.Texto(ret, "cStat");
            string motivoLote = LeitorXml.Texto(ret, "xMotivo");
            var porChave = new Dictionary<string, RetornoCiencia>();
            if (ret != null)
                foreach (XmlElement e in ret.SelectNodes(".//*[local-name()='retEvento']"))
                {
                    var r = new RetornoCiencia
                    {
                        Chave = Formatos.SoDigitos(LeitorXml.Texto(e, "chNFe")),
                        CStat = LeitorXml.Texto(e, "cStat"),
                        XMotivo = LeitorXml.Texto(e, "xMotivo"),
                    };
                    porChave[r.Chave] = r;
                }
            var lista = new List<RetornoCiencia>();
            foreach (string c in chaves)
            {
                RetornoCiencia r;
                if (!porChave.TryGetValue(c, out r))
                    r = new RetornoCiencia { Chave = c, CStat = cStatLote, XMotivo = motivoLote == "" ? "Lote recusado" : motivoLote };
                lista.Add(r);
            }
            return lista;
        }
    }
}
