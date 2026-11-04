using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace NotasRetencaoINSS
{
    public class Lido
    {
        public Nota Nota;
        public EventoNota Evento;
    }

    /// <summary>Lê NFS-e Nacional (ADN) e NF-e (Distribuição DF-e). Busca por nome local, ignorando prefixo/namespace.</summary>
    public static class LeitorXml
    {
        static readonly Dictionary<string, string> EventosNfse = new Dictionary<string, string>
        {
            { "101101", "Cancelamento" },
            { "105102", "Cancelamento por substituição" },
            { "101103", "Solicitação de análise fiscal para cancelamento" },
            { "105104", "Cancelamento deferido por análise fiscal" },
            { "105105", "Cancelamento indeferido por análise fiscal" },
            { "305101", "Cancelamento por ofício" },
            { "202201", "Confirmação do prestador" },
            { "203202", "Confirmação do tomador" },
            { "204203", "Confirmação do intermediário" },
            { "205204", "Confirmação tácita" },
            { "202205", "Rejeição do prestador" },
            { "203206", "Rejeição do tomador" },
            { "204207", "Rejeição do intermediário" },
        };
        static readonly HashSet<string> CancelamentosNfse = new HashSet<string> { "101101", "105102", "105104", "305101" };

        static readonly Dictionary<string, string> EventosNfe = new Dictionary<string, string>
        {
            { "110110", "Carta de Correção" },
            { "110111", "Cancelamento" },
            { "110112", "Cancelamento por substituição" },
            { "210200", "Confirmação da Operação" },
            { "210210", "Ciência da Operação" },
            { "210220", "Desconhecimento da Operação" },
            { "210240", "Operação não Realizada" },
        };
        static readonly HashSet<string> CancelamentosNfe = new HashSet<string> { "110111", "110112" };

        public static XmlDocument Carregar(string xml)
        {
            var doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            var cfg = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var r = XmlReader.Create(new StringReader(xml), cfg)) doc.Load(r);
            return doc;
        }

        public static XmlElement No(XmlNode pai, string nome)
        {
            if (pai == null) return null;
            return pai.SelectSingleNode(".//*[local-name()='" + nome + "']") as XmlElement;
        }

        /// <summary>Filho direto (evita pegar, por ex., o CNPJ do endereço de outro grupo).</summary>
        public static XmlElement Filho(XmlNode pai, string nome)
        {
            if (pai == null) return null;
            return pai.SelectSingleNode("*[local-name()='" + nome + "']") as XmlElement;
        }

        public static string Texto(XmlNode pai, string nome)
        {
            XmlElement e = No(pai, nome);
            return e == null ? "" : e.InnerText.Trim();
        }

        static string TextoFilho(XmlNode pai, string nome)
        {
            XmlElement e = Filho(pai, nome);
            return e == null ? "" : e.InnerText.Trim();
        }

        static string Documento(XmlNode grupo)
        {
            string d = TextoFilho(grupo, "CNPJ");
            if (d == "") d = TextoFilho(grupo, "CPF");
            return Formatos.SoDigitos(d);
        }

        // ------------------------------------------------------------ NFS-e Nacional

        public static Lido LerNfse(string xml, string tipoDocumento, string chaveAcesso, string nsu)
        {
            XmlDocument doc = Carregar(xml);
            XmlElement inf = No(doc, "infNFSe");
            bool evento = (tipoDocumento ?? "").ToUpperInvariant().Contains("EVENTO") || (inf == null && No(doc, "infEvento") != null);
            if (evento) return new Lido { Evento = LerEventoNfse(doc, chaveAcesso, nsu) };
            if (inf == null) return null;

            XmlElement infDps = No(inf, "infDPS");
            XmlElement emit = Filho(inf, "emit");
            XmlElement prest = Filho(infDps, "prest");
            XmlElement toma = Filho(infDps, "toma");

            string chave = Formatos.SoDigitos(chaveAcesso);
            if (chave == "")
            {
                Match m = Regex.Match(inf.GetAttribute("Id"), @"(\d{50})");
                chave = m.Success ? m.Groups[1].Value : "";
            }

            // Prestador: o da DPS (a NFS-e pode ter sido emitida pelo tomador ou intermediário).
            string prestDoc = Documento(prest);
            string emitDoc = Documento(emit);
            string prestNome = TextoFilho(prest, "xNome");
            if (prestNome == "" && (prestDoc == "" || prestDoc == emitDoc)) prestNome = TextoFilho(emit, "xNome");
            if (prestDoc == "") prestDoc = emitDoc;

            string dhProc = TextoFilho(inf, "dhProc");
            var n = new Nota
            {
                Tipo = "nfse",
                Chave = chave,
                Nsu = nsu,
                Completo = true,
                Numero = TextoFilho(inf, "nNFSe"),
                Serie = TextoFilho(infDps, "serie"),
                Emissao = Formatos.DataIso(dhProc != "" ? dhProc : TextoFilho(infDps, "dhEmi")),
                Competencia = Formatos.DataIso(TextoFilho(infDps, "dCompet")),
                EmitenteDoc = prestDoc,
                EmitenteNome = prestNome,
                TomadorDoc = Documento(toma),
                TomadorNome = TextoFilho(toma, "xNome"),
                Valor = Formatos.Numero(Texto(No(infDps, "vServPrest"), "vServ")),
                InssRetido = Formatos.Numero(Texto(No(infDps, "tribFed"), "vRetCP")),
                CodigoServico = Texto(No(infDps, "cServ"), "cTribNac"),
                Descricao = Texto(No(infDps, "cServ"), "xDescServ"),
                Municipio = TextoFilho(inf, "xLocPrestacao") != "" ? TextoFilho(inf, "xLocPrestacao") : TextoFilho(inf, "xLocEmi"),
                Situacao = "autorizada",
            };
            if (n.Valor == 0m) n.Valor = Formatos.Numero(Texto(Filho(inf, "valores"), "vLiq"));
            return new Lido { Nota = n };
        }

        static EventoNota LerEventoNfse(XmlDocument doc, string chaveAcesso, string nsu)
        {
            string tp = "";
            foreach (XmlElement e in doc.SelectNodes("//*"))
            {
                Match m = Regex.Match(e.LocalName, @"^e(\d{6})$");
                if (m.Success) { tp = m.Groups[1].Value; break; }
            }
            string descricao = Texto(doc, "xDesc");
            if (descricao == "" && EventosNfse.ContainsKey(tp)) descricao = EventosNfse[tp];
            string dh = Texto(doc, "dhEvento");
            if (dh == "") dh = Texto(doc, "dhProc");
            string chave = Formatos.SoDigitos(chaveAcesso);
            if (chave == "") chave = Formatos.SoDigitos(Texto(doc, "chNFSe"));
            return new EventoNota
            {
                Tipo = "nfse",
                Chave = chave,
                TpEvento = tp,
                Descricao = descricao,
                DhEvento = dh,
                Nsu = nsu,
                Cancela = CancelamentosNfse.Contains(tp),
            };
        }

        // ------------------------------------------------------------ NF-e

        /// <summary>schema = nome antes do "_v" (resNFe, procNFe, resEvento, procEventoNFe).</summary>
        public static Lido LerNfe(string xml, string schema, string nsu)
        {
            XmlDocument doc = Carregar(xml);
            if (schema == "resEvento" || schema == "procEventoNFe") return new Lido { Evento = LerEventoNfe(doc, nsu) };
            if (schema == "resNFe") return new Lido { Nota = LerResumoNfe(doc, nsu) };
            if (No(doc, "infNFe") != null) return new Lido { Nota = LerNfeCompleta(doc, nsu) };
            return null;
        }

        static Nota LerResumoNfe(XmlDocument doc, string nsu)
        {
            XmlElement res = No(doc, "resNFe");
            string chave = Formatos.SoDigitos(Texto(res, "chNFe"));
            string cSit = Texto(res, "cSitNFe");
            return new Nota
            {
                Tipo = "nfe",
                Chave = chave,
                Nsu = nsu,
                Completo = false,
                Numero = chave.Length == 44 ? long.Parse(chave.Substring(25, 9)).ToString() : "",
                Serie = chave.Length == 44 ? int.Parse(chave.Substring(22, 3)).ToString() : "",
                Emissao = Formatos.DataIso(Texto(res, "dhEmi")),
                EmitenteDoc = Documento(res),
                EmitenteNome = TextoFilho(res, "xNome"),
                Valor = Formatos.Numero(Texto(res, "vNF")),
                Situacao = cSit == "3" ? "cancelada" : cSit == "2" ? "denegada" : "autorizada",
            };
        }

        static Nota LerNfeCompleta(XmlDocument doc, string nsu)
        {
            XmlElement inf = No(doc, "infNFe");
            XmlElement ide = Filho(inf, "ide");
            XmlElement emit = Filho(inf, "emit");
            XmlElement dest = Filho(inf, "dest");
            XmlElement total = Filho(inf, "total");
            XmlElement retTrib = Filho(total, "retTrib");
            XmlElement prot = No(doc, "infProt");

            string chave = Formatos.SoDigitos(Texto(prot, "chNFe"));
            if (chave.Length != 44)
            {
                Match m = Regex.Match(inf.GetAttribute("Id"), @"(\d{44})");
                chave = m.Success ? m.Groups[1].Value : "";
            }
            string cStatProt = Texto(prot, "cStat");

            XmlNodeList produtos = inf.SelectNodes("*[local-name()='det']");
            string descricao = produtos.Count > 0 ? Texto(produtos[0], "xProd") : "";
            if (produtos.Count > 1) descricao += " (e mais " + (produtos.Count - 1) + " ite" + (produtos.Count == 2 ? "m" : "ns") + ")";

            string dhEmi = TextoFilho(ide, "dhEmi");
            if (dhEmi == "") dhEmi = TextoFilho(ide, "dEmi");
            string vBcPrev = TextoFilho(retTrib, "vBCRetPrev");

            return new Nota
            {
                Tipo = "nfe",
                Chave = chave,
                Nsu = nsu,
                Completo = true,
                Numero = TextoFilho(ide, "nNF"),
                Serie = TextoFilho(ide, "serie"),
                Emissao = Formatos.DataIso(dhEmi),
                EmitenteDoc = Documento(emit),
                EmitenteNome = TextoFilho(emit, "xNome"),
                TomadorDoc = Documento(dest),
                TomadorNome = TextoFilho(dest, "xNome"),
                Valor = Formatos.Numero(Texto(Filho(total, "ICMSTot"), "vNF")),
                BaseInss = vBcPrev == "" ? (decimal?)null : Formatos.Numero(vBcPrev),
                InssRetido = Formatos.Numero(TextoFilho(retTrib, "vRetPrev")),
                Descricao = descricao,
                Municipio = Texto(Filho(emit, "enderEmit"), "xMun"),
                Situacao = (cStatProt == "110" || cStatProt == "301" || cStatProt == "302" || cStatProt == "303") ? "denegada" : "autorizada",
            };
        }

        static EventoNota LerEventoNfe(XmlDocument doc, string nsu)
        {
            string tp = Texto(doc, "tpEvento");
            string descricao = Texto(doc, "xEvento");
            if (descricao == "") descricao = Texto(doc, "descEvento");
            if (descricao == "" && EventosNfe.ContainsKey(tp)) descricao = EventosNfe[tp];
            return new EventoNota
            {
                Tipo = "nfe",
                Chave = Formatos.SoDigitos(Texto(doc, "chNFe")),
                TpEvento = tp,
                Descricao = descricao,
                DhEvento = Texto(doc, "dhEvento"),
                Nsu = nsu,
                Cancela = CancelamentosNfe.Contains(tp),
            };
        }
    }
}
