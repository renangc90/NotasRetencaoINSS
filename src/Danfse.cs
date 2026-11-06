using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace NotasRetencaoINSS
{
    /// <summary>
    /// PDF da NFS-e montado a partir do XML guardado. A API de DANFSe do ADN foi descontinuada pela
    /// NT 008/2026 (agosto de 2026): desde então cada sistema gera a representação a partir do XML.
    /// Este leiaute é simplificado (sem QR Code); o rodapé indica a consulta pública para conferir.
    /// </summary>
    public static class Danfse
    {
        public const string ConsultaPublica = "https://www.nfse.gov.br/ConsultaPublica";

        public static byte[] GerarPdf(string xml, bool cancelada)
        {
            var pdf = new DocumentoPdf();
            Desenhar(pdf, xml, cancelada);
            return pdf.Gerar();
        }

        public static void Desenhar(IDesenho d, string xml, bool cancelada)
        {
            XmlDocument doc = LeitorXml.Carregar(xml);
            XmlElement inf = LeitorXml.No(doc, "infNFSe");
            if (inf == null) throw new ErroAviso("O XML não é de uma NFS-e do padrão nacional.");
            XmlElement infDps = LeitorXml.No(inf, "infDPS");
            XmlElement emit = LeitorXml.Filho(inf, "emit");
            XmlElement prest = LeitorXml.Filho(infDps, "prest");
            XmlElement toma = LeitorXml.Filho(infDps, "toma");
            XmlElement interm = LeitorXml.Filho(infDps, "interm");
            XmlElement serv = LeitorXml.Filho(infDps, "serv");
            XmlElement valNfse = LeitorXml.Filho(inf, "valores");
            XmlElement valDps = LeitorXml.Filho(infDps, "valores");
            XmlElement tribMun = LeitorXml.No(valDps, "tribMun");
            XmlElement tribFed = LeitorXml.No(valDps, "tribFed");
            XmlElement pisCofins = LeitorXml.No(tribFed, "piscofins");

            string chave = Regex.Match(inf.GetAttribute("Id"), @"\d{50}").Value;
            string numero = F(inf, "nNFSe");

            var f = new Folha(d, numero, chave, cancelada);
            f.Cabecalho();

            f.Linha(new Campo("Chave de acesso", Agrupar(chave), 1f, true));
            f.Linha(new Campo("Número da NFS-e", numero, 0.16f, true),
                    new Campo("Competência", Data(F(infDps, "dCompet")), 0.14f),
                    new Campo("Data e hora da NFS-e", DataHora(F(inf, "dhProc")), 0.22f),
                    new Campo("Número da DPS", F(infDps, "nDPS"), 0.16f),
                    new Campo("Série da DPS", F(infDps, "serie"), 0.12f),
                    new Campo("Data e hora da DPS", DataHora(F(infDps, "dhEmi")), 0.20f));
            f.Linha(new Campo("Município de emissão", F(inf, "xLocEmi"), 0.30f),
                    new Campo("Local da prestação", F(inf, "xLocPrestacao"), 0.30f),
                    new Campo("Município de incidência do ISSQN", F(inf, "xLocIncid"), 0.25f),
                    new Campo("Situação", cancelada ? "CANCELADA" : "Autorizada", 0.15f, true));

            // Prestador: o da DPS; o que faltar vem do emitente (normalmente são o mesmo).
            f.Secao("PRESTADOR DO SERVIÇO");
            string prestDoc = Doc(prest);
            bool prestEhEmit = prestDoc == "" || prestDoc == Doc(emit);
            XmlElement dadosPrest = prestEhEmit ? emit : prest;
            f.Linha(new Campo("Nome / razão social", Primeiro(F(prest, "xNome"), prestEhEmit ? F(emit, "xNome") : ""), 0.55f, true),
                    new Campo("CNPJ / CPF", Formatos.CnpjCpf(prestDoc != "" ? prestDoc : Doc(emit)), 0.25f),
                    new Campo("Inscrição municipal", Primeiro(F(prest, "IM"), prestEhEmit ? F(emit, "IM") : ""), 0.20f));
            f.Linha(new Campo("Endereço", Endereco(dadosPrest), 0.55f),
                    new Campo("CEP", Cep(F(dadosPrest, "CEP")), 0.13f),
                    new Campo("UF", F(dadosPrest, "UF"), 0.07f),
                    new Campo("Simples Nacional", Simples(F(LeitorXml.Filho(prest, "regTrib"), "opSimpNac")), 0.25f));
            f.Linha(new Campo("Telefone", Fone(Primeiro(F(prest, "fone"), F(emit, "fone"))), 0.30f),
                    new Campo("E-mail", Primeiro(F(prest, "email"), F(emit, "email")), 0.70f));

            f.Secao("TOMADOR DO SERVIÇO");
            if (toma == null) f.Linha(new Campo("", "Tomador não identificado na NFS-e", 1f));
            else
            {
                f.Linha(new Campo("Nome / razão social", F(toma, "xNome"), 0.55f, true),
                        new Campo("CNPJ / CPF / NIF", Primeiro(Formatos.CnpjCpf(Doc(toma)), F(toma, "NIF")), 0.25f),
                        new Campo("Inscrição municipal", F(toma, "IM"), 0.20f));
                f.Linha(new Campo("Endereço", Endereco(toma), 0.62f),
                        new Campo("CEP", Cep(F(toma, "CEP")), 0.13f),
                        new Campo("Telefone", Fone(F(toma, "fone")), 0.25f));
                f.Linha(new Campo("E-mail", F(toma, "email"), 1f));
            }

            if (interm != null)
            {
                f.Secao("INTERMEDIÁRIO DO SERVIÇO");
                f.Linha(new Campo("Nome / razão social", F(interm, "xNome"), 0.55f),
                        new Campo("CNPJ / CPF / NIF", Primeiro(Formatos.CnpjCpf(Doc(interm)), F(interm, "NIF")), 0.25f),
                        new Campo("Inscrição municipal", F(interm, "IM"), 0.20f));
            }

            f.Secao("SERVIÇO PRESTADO");
            string cTribNac = F(serv, "cTribNac");
            string xTribNac = F(inf, "xTribNac");
            f.TextoLongo("Código de tributação nacional", CodigoServico(cTribNac) + (xTribNac != "" ? " – " + xTribNac : ""));
            f.Linha(new Campo("Código de tributação municipal", F(serv, "cTribMun"), 0.34f),
                    new Campo("Código NBS", F(serv, "cNBS"), 0.33f),
                    new Campo("Local da prestação", F(inf, "xLocPrestacao"), 0.33f));
            f.TextoLongo("Descrição do serviço", F(serv, "xDescServ"));

            f.Secao("VALORES E ISSQN");
            f.Linha(new Campo("Valor do serviço", Dinheiro(F(valDps, "vServ")), 0.25f, true),
                    new Campo("Desconto incondicionado", Dinheiro(F(valDps, "vDescIncond")), 0.25f),
                    new Campo("Desconto condicionado", Dinheiro(F(valDps, "vDescCond")), 0.25f),
                    new Campo("Deduções / reduções", Dinheiro(Primeiro(F(valNfse, "vCalcDR"), F(valDps, "vDR"))), 0.25f));
            f.Linha(new Campo("Base de cálculo do ISSQN", Dinheiro(F(valNfse, "vBC")), 0.20f),
                    new Campo("Alíquota", Percentual(Primeiro(F(valNfse, "pAliqAplic"), F(tribMun, "pAliq"))), 0.12f),
                    new Campo("ISSQN apurado", Dinheiro(F(valNfse, "vISSQN")), 0.18f),
                    new Campo("Retenção do ISSQN", RetencaoIss(F(tribMun, "tpRetISSQN")), 0.25f),
                    new Campo("Tributação do ISSQN", TributacaoIss(F(tribMun, "tribISSQN")), 0.25f));

            f.Secao("RETENÇÕES FEDERAIS");
            f.Linha(new Campo("IRRF retido", Dinheiro(F(tribFed, "vRetIRRF")), 0.17f, true),
                    new Campo("INSS retido (CP)", Dinheiro(F(tribFed, "vRetCP")), 0.17f, true),
                    new Campo("CSLL retida", Dinheiro(F(tribFed, "vRetCSLL")), 0.16f),
                    new Campo("PIS", Dinheiro(F(pisCofins, "vPis")), 0.15f),
                    new Campo("COFINS", Dinheiro(F(pisCofins, "vCofins")), 0.15f),
                    new Campo("Retenção PIS/COFINS", RetencaoPisCofins(F(pisCofins, "tpRetPisCofins")), 0.20f));

            f.Secao("TOTAIS");
            f.Linha(new Campo("Valor do serviço", Dinheiro(F(valDps, "vServ")), 0.34f),
                    new Campo("Total de retenções", Dinheiro(F(valNfse, "vTotalRet")), 0.33f),
                    new Campo("Valor líquido da NFS-e", Dinheiro(F(valNfse, "vLiq")), 0.33f, true));

            string infComp = F(infDps, "xInfComp");
            if (infComp != "")
            {
                f.Secao("INFORMAÇÕES COMPLEMENTARES");
                f.TextoLongo("", infComp);
            }
            f.Fim();
        }

        // ------------------------------------------------------------ leitura e formatação

        /// <summary>Texto do primeiro descendente com esse nome (vazio se não houver).</summary>
        static string F(XmlNode pai, string nome)
        {
            return LeitorXml.Texto(pai, nome);
        }

        static string Doc(XmlNode grupo)
        {
            XmlElement e = LeitorXml.Filho(grupo, "CNPJ") ?? LeitorXml.Filho(grupo, "CPF");
            return e == null ? "" : Formatos.SoDigitos(e.InnerText);
        }

        static string Primeiro(string a, string b) { return string.IsNullOrEmpty(a) ? (b ?? "") : a; }

        static string Endereco(XmlNode grupo)
        {
            var partes = new List<string>();
            string lgr = F(grupo, "xLgr"), nro = F(grupo, "nro");
            if (lgr != "") partes.Add(nro != "" ? lgr + ", " + nro : lgr);
            foreach (string campo in new[] { "xCpl", "xBairro" })
            {
                string v = F(grupo, campo);
                if (v != "") partes.Add(v);
            }
            return string.Join(" – ", partes);
        }

        static string Agrupar(string chave)
        {
            return Regex.Replace(chave ?? "", ".{4}(?!$)", "$0 ");
        }

        static string Data(string iso)
        {
            Match m = Regex.Match(iso ?? "", @"^(\d{4})-(\d{2})-(\d{2})");
            return m.Success ? m.Groups[3].Value + "/" + m.Groups[2].Value + "/" + m.Groups[1].Value : iso ?? "";
        }

        static string DataHora(string iso)
        {
            Match m = Regex.Match(iso ?? "", @"^(\d{4})-(\d{2})-(\d{2})T(\d{2}:\d{2}:\d{2})");
            return m.Success ? m.Groups[3].Value + "/" + m.Groups[2].Value + "/" + m.Groups[1].Value + " " + m.Groups[4].Value : Data(iso);
        }

        static string Dinheiro(string valor)
        {
            if (string.IsNullOrEmpty(valor)) return "-";
            return Formatos.Dinheiro(Formatos.Numero(valor));
        }

        static string Percentual(string valor)
        {
            if (string.IsNullOrEmpty(valor)) return "-";
            return Formatos.Numero(valor).ToString("0.00##", Formatos.PtBr) + "%";
        }

        static string Cep(string cep)
        {
            string d = Formatos.SoDigitos(cep);
            return d.Length == 8 ? d.Substring(0, 5) + "-" + d.Substring(5) : d;
        }

        static string Fone(string fone)
        {
            string d = Formatos.SoDigitos(fone);
            if (d.Length == 10) return "(" + d.Substring(0, 2) + ") " + d.Substring(2, 4) + "-" + d.Substring(6);
            if (d.Length == 11) return "(" + d.Substring(0, 2) + ") " + d.Substring(2, 5) + "-" + d.Substring(7);
            return d;
        }

        /// <summary>"070201" → "07.02.01".</summary>
        static string CodigoServico(string c)
        {
            string d = Formatos.SoDigitos(c);
            return d.Length == 6 ? d.Substring(0, 2) + "." + d.Substring(2, 2) + "." + d.Substring(4) : d;
        }

        static string Simples(string op)
        {
            switch (op)
            {
                case "1": return "Não optante";
                case "2": return "Optante – MEI";
                case "3": return "Optante – ME/EPP";
            }
            return "-";
        }

        static string RetencaoIss(string tp)
        {
            switch (tp)
            {
                case "1": return "Não retido";
                case "2": return "Retido pelo tomador";
                case "3": return "Retido pelo intermediário";
            }
            return "-";
        }

        static string TributacaoIss(string trib)
        {
            switch (trib)
            {
                case "1": return "Operação tributável";
                case "2": return "Imunidade";
                case "3": return "Exportação de serviço";
                case "4": return "Não incidência";
            }
            return "-";
        }

        /// <summary>O significado dos códigos mudou com a NT 007/2026; mostra o código junto quando não é um dos originais.</summary>
        static string RetencaoPisCofins(string tp)
        {
            switch (tp)
            {
                case "": return "-";
                case "1": return "Retidos (1)";
                case "2": return "Não retidos (2)";
                case "0": return "Não retidos (0)";
                case "3": return "PIS/COFINS/CSLL retidos (3)";
            }
            return "Código " + tp;
        }

        // ------------------------------------------------------------ diagramação

        class Campo
        {
            public readonly string Rotulo, Valor;
            public readonly float Proporcao;
            public readonly bool Destaque;
            public Campo(string rotulo, string valor, float proporcao, bool destaque = false)
            {
                Rotulo = rotulo;
                Valor = string.IsNullOrEmpty(valor) ? "-" : valor;
                Proporcao = proporcao;
                Destaque = destaque;
            }
        }

        /// <summary>Página com cursor vertical, quebra automática e rodapé.</summary>
        class Folha
        {
            const float Margem = 28f, AlturaCampo = 23f, AlturaSecao = 13f, AlturaRodape = 34f;
            static readonly float LarguraUtil = DocumentoPdf.Largura - 2 * Margem;
            static readonly float Limite = DocumentoPdf.Altura - Margem - AlturaRodape;

            readonly IDesenho d;
            readonly string numero, chave;
            readonly bool cancelada;
            float y;
            int pagina;

            public Folha(IDesenho d, string numero, string chave, bool cancelada)
            {
                this.d = d;
                this.numero = numero;
                this.chave = chave;
                this.cancelada = cancelada;
            }

            void ComecarPagina()
            {
                d.NovaPagina();
                pagina++;
                if (cancelada) d.MarcaDagua("CANCELADA");
                y = Margem;
            }

            public void Cabecalho()
            {
                ComecarPagina();
                d.Retangulo(Margem, y, LarguraUtil, 46f, 1f, 1f);
                d.Texto(Margem + 8, y + 7, "DANFSe", 20f, true);
                d.Texto(Margem + 8, y + 31, "Documento Auxiliar da Nota Fiscal de Serviço Eletrônica", 8.5f, false);
                string n = "NFS-e nº " + (string.IsNullOrEmpty(numero) ? "-" : numero);
                d.Texto(Margem + LarguraUtil - 8 - Fonte.Largura(n, 13f, true), y + 9, n, 13f, true);
                string sub = "Gerado a partir do XML oficial da NFS-e";
                d.Texto(Margem + LarguraUtil - 8 - Fonte.Largura(sub, 7f, false), y + 30, sub, 7f, false);
                y += 52f;
            }

            void Continuacao()
            {
                Rodape();
                ComecarPagina();
                string t = "DANFSe – NFS-e nº " + numero + " (continuação)";
                d.Texto(Margem, y, t, 9f, true);
                d.Texto(Margem, y + 12, "Chave de acesso " + Agrupar(chave), 7f, false);
                y += 26f;
            }

            void Garantir(float altura)
            {
                if (y + altura > Limite) Continuacao();
            }

            public void Secao(string titulo)
            {
                Garantir(AlturaSecao + AlturaCampo);
                y += 4f;
                d.Retangulo(Margem, y, LarguraUtil, AlturaSecao, 0.6f, 0.88f);
                d.Texto(Margem + 4, y + 3, titulo, 7.5f, true);
                y += AlturaSecao;
            }

            public void Linha(params Campo[] campos)
            {
                Garantir(AlturaCampo);
                float x = Margem;
                for (int i = 0; i < campos.Length; i++)
                {
                    Campo c = campos[i];
                    float w = i == campos.Length - 1 ? Margem + LarguraUtil - x : LarguraUtil * c.Proporcao;
                    d.Retangulo(x, y, w, AlturaCampo, 0.5f, 1f);
                    if (c.Rotulo != "") d.Texto(x + 3, y + 2.5f, c.Rotulo, 6f, false);
                    float tam = c.Destaque ? 9f : 8.5f;
                    d.Texto(x + 3, y + 11f, Caber(c.Valor, w - 6, tam, c.Destaque), tam, c.Destaque);
                    x += w;
                }
                y += AlturaCampo;
            }

            /// <summary>Texto que pode ocupar várias linhas (e páginas), dentro de uma caixa.</summary>
            public void TextoLongo(string rotulo, string texto)
            {
                const float tam = 8.5f, entrelinha = 11f;
                List<string> linhas = Fonte.Quebrar(string.IsNullOrEmpty(texto) ? "-" : texto, LarguraUtil - 8, tam, false);
                int i = 0;
                bool primeira = true;
                while (i < linhas.Count)
                {
                    Garantir(rotulo != "" && primeira ? 12f + entrelinha : entrelinha + 6f);
                    float topo = y;
                    float yTexto = y + 3f;
                    if (rotulo != "" && primeira) { d.Texto(Margem + 3, y + 2.5f, rotulo, 6f, false); yTexto = y + 11f; }
                    while (i < linhas.Count && yTexto + entrelinha <= Limite)
                    {
                        d.Texto(Margem + 4, yTexto, linhas[i], tam, false);
                        yTexto += entrelinha;
                        i++;
                    }
                    float altura = yTexto - topo + 3f;
                    d.Retangulo(Margem, topo, LarguraUtil, altura, 0.5f, 1f);
                    y = topo + altura;
                    primeira = false;
                    if (i < linhas.Count) Continuacao();
                }
            }

            public void Fim()
            {
                Rodape();
            }

            void Rodape()
            {
                float yr = DocumentoPdf.Altura - Margem - AlturaRodape + 6f;
                d.Linha(Margem, yr, Margem + LarguraUtil, yr, 0.5f);
                d.Texto(Margem, yr + 4, "Gerado a partir do XML oficial da NFS-e (a API de DANFSe do Ambiente Nacional foi descontinuada pela NT 008/2026).", 6.5f, false);
                d.Texto(Margem, yr + 13, "Confira a autenticidade em " + ConsultaPublica + " com a chave de acesso.", 6.5f, false);
                string p = "Página " + pagina;
                d.Texto(Margem + LarguraUtil - Fonte.Largura(p, 6.5f, false), yr + 13, p, 6.5f, false);
            }

            static string Caber(string texto, float largura, float tamanho, bool negrito)
            {
                if (Fonte.Largura(texto, tamanho, negrito) <= largura) return texto;
                int n = texto.Length;
                while (n > 1 && Fonte.Largura(texto.Substring(0, n) + "…", tamanho, negrito) > largura) n--;
                return texto.Substring(0, n).TrimEnd() + "…";
            }
        }
    }
}
