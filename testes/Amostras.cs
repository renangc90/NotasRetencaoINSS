using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NotasRetencaoINSS
{
    /// <summary>XML de exemplo nos leiautes oficiais (CNPJs fictícios).</summary>
    static class Amostras
    {
        public const string CnpjPrefeitura = "11222333000181";
        public const string CnpjPrestador = "12345678000190";

        /// <param name="tribFedXml">conteúdo do grupo tribFed; null = vRetCP informado + vRetIRRF 15,00</param>
        public static string Nfse(string chave, string numero, string tomador, decimal vServ, decimal? vRetCP, string dhProc = "2026-10-15T09:12:00-03:00",
                                  string tribFedXml = null, string cTribNac = "070201", string opSimpNac = "1")
        {
            string tribFed = tribFedXml != null ? "<tribFed>" + tribFedXml + "</tribFed>"
                : vRetCP.HasValue ? "<tribFed><vRetCP>" + vRetCP.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</vRetCP><vRetIRRF>15.00</vRetIRRF></tribFed>" : "";
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><NFSe versao=\"1.00\" xmlns=\"http://www.sped.fazenda.gov.br/nfse\"><infNFSe Id=\"NFS" + chave + "\">"
                + "<xLocEmi>Uberlândia</xLocEmi><xLocPrestacao>Uberlândia</xLocPrestacao><nNFSe>" + numero + "</nNFSe><cLocIncid>3170206</cLocIncid><xTribNac>Limpeza</xTribNac>"
                + "<verAplic>1.0</verAplic><ambGer>2</ambGer><tpEmis>1</tpEmis><procEmi>1</procEmi><cStat>100</cStat><dhProc>" + dhProc + "</dhProc><nDFSe>1</nDFSe>"
                + "<emit><CNPJ>" + CnpjPrestador + "</CNPJ><IM>123</IM><xNome>LIMPA TUDO SERVIÇOS LTDA</xNome><enderNac><xLgr>Rua A</xLgr></enderNac></emit>"
                + "<valores><vBC>" + vServ.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</vBC><vLiq>9000.00</vLiq></valores>"
                + "<DPS versao=\"1.00\"><infDPS Id=\"DPS317020621234567800019000900000000000055\"><tpAmb>1</tpAmb><dhEmi>2026-10-15T09:10:00-03:00</dhEmi><serie>900</serie><nDPS>55</nDPS>"
                + "<dCompet>2026-09-30</dCompet><tpEmit>1</tpEmit><cLocEmi>3170206</cLocEmi><prest><CNPJ>" + CnpjPrestador + "</CNPJ><regTrib><opSimpNac>" + opSimpNac + "</opSimpNac></regTrib></prest>"
                + "<toma><CNPJ>" + tomador + "</CNPJ><xNome>MUNICIPIO DE TESTE</xNome><end><endNac><cMun>3170206</cMun></endNac></end></toma>"
                + "<serv><locPrest><cLocPrestacao>3170206</cLocPrestacao></locPrest><cServ><cTribNac>" + cTribNac + "</cTribNac><xDescServ>Limpeza predial - setembro</xDescServ></cServ></serv>"
                + "<valores><vServPrest><vServ>" + vServ.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</vServ></vServPrest><trib><tribMun><tribISSQN>1</tribISSQN><tpRetISSQN>1</tpRetISSQN></tribMun>"
                + tribFed + "<totTrib><indTotTrib>0</indTotTrib></totTrib></trib></valores></infDPS></DPS></infNFSe></NFSe>";
        }

        public static string EventoCancelamentoNfse(string chave)
        {
            return "<evento versao=\"1.00\" xmlns=\"http://www.sped.fazenda.gov.br/nfse\"><infEvento Id=\"EVT" + chave + "101101001\"><verAplic>1</verAplic><ambGer>2</ambGer><nSeqEvento>1</nSeqEvento>"
                + "<dhProc>2026-10-20T10:00:00-03:00</dhProc><nDFe>9</nDFe><pedRegEvento versao=\"1.00\"><infPedReg Id=\"PRE" + chave + "101101001\"><tpAmb>1</tpAmb><verAplic>1</verAplic>"
                + "<dhEvento>2026-10-20T09:59:00-03:00</dhEvento><CNPJAutor>" + CnpjPrestador + "</CNPJAutor><chNFSe>" + chave + "</chNFSe><nPedRegEvento>1</nPedRegEvento>"
                + "<e101101><xDesc>Cancelamento de NFS-e</xDesc><cMotivo>1</cMotivo><xMotivo>Erro na emissao</xMotivo></e101101></infPedReg></pedRegEvento></infEvento></evento>";
        }

        public static string ChaveNfe(string numero)
        {
            // cUF(2) AAMM(4) CNPJ(14) mod(2) serie(3) nNF(9) tpEmis(1) cNF(8) cDV(1)
            return "31" + "2610" + CnpjPrestador + "55" + "001" + numero.PadLeft(9, '0') + "1" + "12345678" + "0";
        }

        public static string ResNfe(string chave, decimal vNF, string cSit = "1")
        {
            return "<resNFe xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"1.01\"><chNFe>" + chave + "</chNFe><CNPJ>" + CnpjPrestador + "</CNPJ><xNome>FORNECEDORA LTDA</xNome><IE>123</IE>"
                + "<dhEmi>2026-10-10T08:00:00-03:00</dhEmi><tpNF>1</tpNF><vNF>" + vNF.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</vNF><digVal>abc=</digVal>"
                + "<dhRecbto>2026-10-10T08:00:05-03:00</dhRecbto><nProt>131260000000001</nProt><cSitNFe>" + cSit + "</cSitNFe></resNFe>";
        }

        /// <param name="retTribExtra">mais campos do retTrib (vIRRF, vRetCSLL...)</param>
        public static string ProcNfe(string chave, string numero, decimal vNF, decimal? vRetPrev, string retTribExtra = "", string crt = "3")
        {
            string ret = vRetPrev.HasValue || retTribExtra != ""
                ? "<retTrib><vRetPIS>0.00</vRetPIS>" + retTribExtra + (vRetPrev.HasValue ? "<vBCRetPrev>" + vNF.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</vBCRetPrev><vRetPrev>" + vRetPrev.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</vRetPrev>" : "") + "</retTrib>"
                : "";
            return "<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"4.00\"><NFe><infNFe Id=\"NFe" + chave + "\" versao=\"4.00\"><ide><cUF>31</cUF><nNF>" + numero + "</nNF><serie>1</serie>"
                + "<dhEmi>2026-10-10T08:00:00-03:00</dhEmi></ide><emit><CNPJ>" + CnpjPrestador + "</CNPJ><xNome>FORNECEDORA LTDA</xNome><enderEmit><xMun>Uberlândia</xMun></enderEmit><CRT>" + crt + "</CRT></emit>"
                + "<dest><CNPJ>" + CnpjPrefeitura + "</CNPJ><xNome>MUNICIPIO DE TESTE</xNome><enderDest><xMun>Teste</xMun></enderDest></dest>"
                + "<det nItem=\"1\"><prod><xProd>Manutenção de ar condicionado</xProd></prod></det><det nItem=\"2\"><prod><xProd>Gás refrigerante</xProd></prod></det>"
                + "<total><ICMSTot><vNF>" + vNF.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</vNF></ICMSTot>" + ret + "</total></infNFe></NFe>"
                + "<protNFe versao=\"4.00\"><infProt><chNFe>" + chave + "</chNFe><cStat>100</cStat><xMotivo>Autorizado</xMotivo></infProt></protNFe></nfeProc>";
        }

        public static string ResEventoCancelamentoNfe(string chave)
        {
            return "<resEvento xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"1.01\"><cOrgao>31</cOrgao><CNPJ>" + CnpjPrestador + "</CNPJ><chNFe>" + chave + "</chNFe>"
                + "<dhEvento>2026-10-11T10:00:00-03:00</dhEvento><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento><xEvento>Cancelamento</xEvento><dhRecbto>2026-10-11T10:00:01-03:00</dhRecbto><nProt>1</nProt></resEvento>";
        }

        public static string Gzip64(string xml)
        {
            var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
            {
                byte[] b = Encoding.UTF8.GetBytes(xml);
                gz.Write(b, 0, b.Length);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        public static string ChaveNfse(int i)
        {
            return ("3170206" + "2" + CnpjPrestador + i.ToString().PadLeft(13, '0') + "2610" + "12345678" + "9").PadRight(50, '0').Substring(0, 50);
        }

        public static string SoapDist(string cStat, string xMotivo, long ult, long max, string docZips)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?><soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"><soap:Body><nfeDistDFeInteresseResponse xmlns=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe\"><nfeDistDFeInteresseResult>"
                + "<retDistDFeInt xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"1.01\"><tpAmb>1</tpAmb><verAplic>1.7</verAplic><cStat>" + cStat + "</cStat><xMotivo>" + xMotivo + "</xMotivo>"
                + "<dhResp>2026-11-04T10:00:00-03:00</dhResp><ultNSU>" + ult.ToString("D15") + "</ultNSU><maxNSU>" + max.ToString("D15") + "</maxNSU>"
                + (docZips == "" ? "" : "<loteDistDFeInt>" + docZips + "</loteDistDFeInt>") + "</retDistDFeInt></nfeDistDFeInteresseResult></nfeDistDFeInteresseResponse></soap:Body></soap:Envelope>";
        }

        public static string DocZip(long nsu, string schema, string xml)
        {
            return "<docZip NSU=\"" + nsu.ToString("D15") + "\" schema=\"" + schema + "\">" + Gzip64(xml) + "</docZip>";
        }
    }
}
