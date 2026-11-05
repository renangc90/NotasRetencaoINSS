using System;
using System.Collections.Generic;
using System.Linq;

namespace NotasRetencaoINSS
{
    /// <summary>Natureza de rendimento da EFD-Reinf e seus códigos de receita (DCTFWeb).</summary>
    public class NaturezaRendimento
    {
        public string Codigo { get; private set; }
        public string Descricao { get; private set; }
        public string Evento { get; private set; }      // R-4020 (pago a PJ) ou R-4010 (pago a PF)
        /// <summary>"17031 – Vigilância" (encurtado) para a lista da tela.</summary>
        public string Texto { get; private set; }

        // Ordem: IR, CSLL, COFINS, PIS/Pasep, Agregado. "" = a tabela não traz código.
        readonly string[] adm85, outros;

        public NaturezaRendimento(string codigo, string descricao, string evento, string[] adm85, string[] outros)
        {
            Codigo = codigo;
            Descricao = descricao;
            Evento = evento;
            this.adm85 = adm85;
            this.outros = outros;
            Texto = codigo + " – " + (descricao.Length > 70 ? descricao.Substring(0, 68).TrimEnd(' ', ',') + "…" : descricao);
        }

        /// <summary>
        /// Código de receita (6 dígitos) do tributo: 0 IR, 1 CSLL, 2 COFINS, 3 PIS, 4 Agregado.
        /// classTrib85 = declarante da administração pública direta, autarquia ou fundação (coluna "Sim" do Anexo I).
        /// </summary>
        public string CodigoReceita(int tributo, bool classTrib85)
        {
            string[] c = classTrib85 ? adm85 : outros;
            return tributo < c.Length ? c[tributo] : "";
        }
    }

    /// <summary>
    /// Anexo I do Manual da EFD-Reinf (versão 2.1.2.1) — natureza de rendimento × código de receita.
    /// Traz o grupo 17 do R-4020 (pagamentos de órgãos públicos a pessoa jurídica, IN RFB 1.234/2012) e a
    /// natureza 10002 do R-4010 (trabalho sem vínculo, pessoa física). As linhas do grupo 17 foram geradas
    /// do texto do PDF (anexo na raiz do projeto), não digitadas.
    /// </summary>
    public static class TabelaReinf
    {
        public const string Fonte = "Anexo I do Manual da EFD-Reinf, versão 2.1.2.1";
        public const int IR = 0, CSLL = 1, COFINS = 2, PIS = 3, AGREGADO = 4;

        /// <summary>Pessoa física prestadora (CPF): R-4010, IRPF 0588-07.</summary>
        public const string NaturezaPessoaFisica = "10002";
        public const string NaturezaMercadorias = "17009";
        public const string NaturezaDemaisServicos = "17099";

        public static readonly List<NaturezaRendimento> Naturezas;
        static readonly Dictionary<string, NaturezaRendimento> porCodigo;

        /// <summary>
        /// Sugestão pelo item da lista de serviços (LC 116) — os 4 primeiros dígitos do cTribNac.
        /// Só os casos sem ambiguidade; o resto fica em "17099 Demais serviços" para a pessoa conferir.
        /// </summary>
        static readonly Dictionary<string, string> PorServico = new Dictionary<string, string>
        {
            { "1102", "17031" },   // 11.02 Vigilância, segurança ou monitoramento
            { "0710", "17032" },   // 7.10 Limpeza, manutenção e conservação de imóveis, vias e logradouros
            { "1705", "17033" },   // 17.05 Fornecimento de mão de obra
            { "1712", "17035" },   // 17.12 Administração em geral, inclusive de bens e negócios de terceiros
            { "1601", "17023" },   // 16.01 Transporte coletivo municipal de passageiros
        };

        static TabelaReinf()
        {
            Naturezas = new List<NaturezaRendimento>
            {
                new NaturezaRendimento(NaturezaPessoaFisica, "Rendimento decorrente do trabalho sem vínculo empregatício (pessoa física)", "R-4010",
                    new[] { "058807", "", "", "", "" }, new[] { "058807", "", "", "", "" }),
            Pj("17001", "Alimentação",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17002", "Energia elétrica",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17003", "Serviços prestados com emprego de materiais",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17004", "Construção Civil por empreitada com emprego de materiais",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17005", "Serviços hospitalares de que trata o art. 30 da Instrução Normativa RFB nº 1.234, de 11 de janeiro de 2012",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17006", "Transporte de cargas, exceto os relacionados na natureza de rendimento \"17017\"",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17007", "Serviços de auxílio diagnóstico e terapia, patologia clínica, imagenologia, anatomia patológica e citopatológica, medicina nuclear e análises e patologias clínicas, exames por métodos gráficos, procedimentos endoscópicos, radioterapia, quimioterapia, diálise e oxigenoterapia hiperbárica de que trata o art. 31 e parágrafo único da Instrução Normativa RFB nº 1.234, de 2012",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17008", "Produtos farmacêuticos, de perfumaria, de toucador ou de higiene pessoal adquiridos de produtor, importador, distribuidor ou varejista, exceto os relacionados nas naturezas de rendimentos de “17019\" a \"17022\"",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17009", "Mercadorias e bens em geral",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17010", "Gasolina, inclusive de aviação, óleo diesel, gás liquefeito de petróleo (GLP), combustíveis derivados de petróleo ou de gás natural, querosene de aviação (QAV), e demais produtos derivados de petróleo, adquiridos de refinarias de petróleo, de demais produtores, de importadores, de distribuidor ou varejista",
               "625605 622805 624305 623005 906005", "625606 622806 624306 623006 906006"),
            Pj("17011", "Álcool etílico hidratado, inclusive para fins carburantes, adquirido diretamente de produtor, importador ou do distribuidor",
               "625605 622805 624305 623005 906005", "625606 622806 624306 623006 906006"),
            Pj("17012", "Biodiesel adquirido de produtor ou importador",
               "625605 622805 624305 623005 906005", "625606 622806 624306 623006 906006"),
            Pj("17013", "Gasolina, exceto gasolina de aviação, óleo diesel e gás liquefeito de petróleo (GLP), derivados de petróleo ou de gás natural e querosene de aviação adquiridos de distribuidores e comerciantes varejistas",
               "625605 622805 - - 873905", "625606 622806 - - 873906"),
            Pj("17014", "Álcool etílico hidratado nacional, inclusive para fins carburantes adquirido de comerciante varejista",
               "625605 622805 - - 873905", "625606 622806 - - 873906"),
            Pj("17015", "Biodiesel adquirido de distribuidores e comerciantes varejistas",
               "625605 622805 - - 873905", "625606 622806 - - 873906"),
            Pj("17016", "Biodiesel adquirido de produtor detentor regular do selo “Combustível Social”, fabricado a partir de mamona ou fruto, caroço ou amêndoa de palma produzidos nas regiões norte e nordeste e no semiárido, por agricultor familiar enquadrado no Programa Nacional de Fortalecimento da Agricultura Familiar (Pronaf)",
               "625605 622805 - - 873905", "625606 622806 - - 873906"),
            Pj("17017", "Transporte internacional de cargas efetuado por empresas nacionais",
               "625605 622805 - - 876705", "625606 622806 - - 876706"),
            Pj("17018", "Estaleiros navais brasileiros nas atividades de Construção, conservação, modernização, conversão e reparo de embarcações pré-registradas ou registradas no REB",
               "625605 622805 - - 876705", "625606 622806 - - 876706"),
            Pj("17019", "Produtos de perfumaria, de toucador e de higiene pessoal a que se refere o § 1º do art. 22 da Instrução Normativa RFB nº 1.234, de 2012, adquiridos de distribuidores e de comerciantes varejistas",
               "625605 622805 - - 876705", "625606 622806 - - 876706"),
            Pj("17020", "Produtos a que se refere o § 2º do art. 22 da Instrução Normativa RFB nº 1.234, de 2012",
               "625605 622805 - - 876705", "625606 622806 - - 876706"),
            Pj("17021", "Produtos de que tratam as alíneas “c” a “k” do inciso I do art. 5º da Instrução Normativa RFB nº 1.234, de 2012",
               "625605 622805 - - 876705", "625606 622806 - - 876706"),
            Pj("17022", "Outros produtos ou serviços beneficiados com isenção, não incidência ou Alíquotas zero da Cofins e da Contribuição para o PIS/Pasep, observado o disposto no § 5º do art. 2º da Instrução Normativa RFB nº 1.234, de 2012",
               "625605 622805 - - 876705", "625606 622806 - - 876706"),
            Pj("17023", "Passagens aéreas, rodoviárias e demais serviços de transporte de passageiros, inclusive, tarifa de embarque, exceto transporte internacional de passageiros, efetuado por empresas nacionais",
               "625605 622805 624305 623005 617505", "625606 622806 624306 623006 617506"),
            Pj("17024", "Transporte internacional de passageiros efetuado por empresas nacionais",
               "625605 622805 - - 885005", "625606 622806 - - 885006"),
            Pj("17025", "Serviços prestados por associações profissionais ou assemelhadas e cooperativas",
               "328006 622805 624305 623005 886305", "328006 622806 624306 623006 886306"),
            Pj("17026", "Serviços prestados por bancos comerciais, bancos de investimento, bancos de desenvolvimento, caixas econômicas, sociedades de crédito, financiamento e investimento, sociedades de crédito imobiliário, e câmbio, distribuidoras de títulos e valores mobiliários, empresas de arrendamento mercantil, cooperativas de crédito, empresas de seguros privados e de capitalização e entidades abertas de previdência complementar.",
               "625605 622805 624305 623005 618805", "625606 622806 624306 623006 618806"),
            Pj("17027", "Seguro Saúde",
               "625605 622805 624305 623005 618805", "625606 622806 624306 623006 618806"),
            Pj("17028", "Serviços de abastecimento de água",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17029", "Telefone",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17030", "Correio e telégrafos",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17031", "Vigilância",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17032", "Limpeza",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17033", "Locação de mão de obra",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17034", "Intermediação de negócios",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17035", "Administração, locação ou cessão de bens imóveis, móveis e direitos de qualquer natureza",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17036", "Factoring",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17037", "Plano de saúde humano, veterinário ou odontológico com valores fixos por servidor, por empregado ou por animal",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17038", "Pagamento efetuado a sociedade cooperativa pelo fornecimento de bens, conforme art. 24, da IN 1234/12.",
               "- 622805 624305 623005 886305", "- 622806 624306 623006 886306"),
            Pj("17040", "Serviços prestados por associações profissionais ou assemelhadas e cooperativas que envolver parcela de serviços fornecidos por terceiros não cooperados ou não associados, contratados ou conveniados, para cumprimento de contratos – Serviços prestados com emprego de materiais, inclusive o de que trata a alínea “C” do Inciso II do art. 27 da IN 1.1234.",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17041", "Serviços prestados por associações profissionais ou assemelhadas e cooperativas que envolver parcela de serviços fornecidos por terceiros não cooperados ou não associados, contratados ou conveniados, para cumprimento de contratos - Demais serviços",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17042", "Pagamentos efetuados às associações e às cooperativas de médicos e de odontólogos, relativamente às importâncias recebidas a título de comissão, taxa de administração ou de adesão ao plano",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17043", "Pagamento efetuado a sociedade cooperativa de produção, em relação aos atos decorrentes da comercialização ou de industrialização, pelas cooperativas agropecuárias e de pesca, de produtos adquiridos de não associados, agricultores, pecuaristas ou pescadores, para completar lotes destinados ao cumprimento de contratos ou para suprir capacidade ociosa de suas instalações industriais, conforme § 1º do art. 25, da IN 1234/12.",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17044", "Pagamento referente a aluguel de imóvel quando efetuado à entidade aberta de previdência complementar sem fins lucrativos, de que trata o art. 34, § 2º da IN 1.234/2012.",
               "- 622805 624305 623005 -", "- 622806 624306 623006 -"),
            Pj("17045", "Serviços prestados por cooperativas de radiotáxi, bem como àquelas cujos cooperados se dediquem a serviços relacionados a atividades culturais e demais cooperativas de serviços, conforme art. 5º–A, da IN RFB 1.234/2012.",
               "328006 622805 - - -", "328006 622806 - - -"),
            Pj("17046", "Pagamento efetuado na aquisição de bem imóvel, quando o vendedor for pessoa jurídica que exerce a atividade de compra e venda de imóveis, ou quando se tratar de imóveis adquiridos de entidades abertas de previdência complementar com fins lucrativos, conforme art. 23, inc I, da IN RFB 1234/2012.",
               "625605 622805 624305 623005 614705", "625606 622806 624306 623006 614706"),
            Pj("17047", "Pagamento efetuado na aquisição de bem imóvel adquirido pertencente ao ativo não circulante da empresa vendedora, conforme art. 23, inc II da IN RFB 1234/2012.",
               "625605 622805 - - 876705", "625606 622806 - - 876706"),
            Pj("17048", "Pagamento efetuado na aquisição de bem imóvel adquirido de entidade aberta de previdência complementar sem fins lucrativos, conforme art. 23, inc III, da IN RFB 1234/2012.",
               "- 622805 624305 623005 -", "- 622806 624306 623006 -"),
            Pj("17049", "Propaganda e Publicidade, em desconformidade ao art 16 da IN RFB 1234/2012, referente ao § 4º do citado artigo.",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            Pj("17050", "Propaganda e Publicidade, em conformidade ao art 16 da IN RFB 1234/2012, referente ao § 4º do citado artigo.",
               "- 622805 624305 623005 886305", "- 622806 624306 623006 886306"),
            Pj("17099", "Demais serviços",
               "625605 622805 624305 623005 619005", "625606 622806 624306 623006 619006"),
            };
            porCodigo = Naturezas.ToDictionary(n => n.Codigo);
        }

        static NaturezaRendimento Pj(string codigo, string descricao, string adm85, string outros)
        {
            return new NaturezaRendimento(codigo, descricao, "R-4020", Codigos(adm85), Codigos(outros));
        }

        static string[] Codigos(string s)
        {
            return s.Split(' ').Select(c => c == "-" ? "" : c).ToArray();
        }

        public static NaturezaRendimento Buscar(string codigo)
        {
            NaturezaRendimento n;
            return codigo != null && porCodigo.TryGetValue(codigo, out n) ? n : null;
        }

        /// <summary>"625605" → "6256-05" (como aparece na DCTFWeb).</summary>
        public static string Formatar(string seisDigitos)
        {
            if (string.IsNullOrEmpty(seisDigitos) || seisDigitos.Length != 6) return seisDigitos ?? "";
            return seisDigitos.Substring(0, 4) + "-" + seisDigitos.Substring(4);
        }

        /// <summary>Código de receita do IR da natureza, já formatado ("" se a tabela não traz).</summary>
        public static string CodigoIr(string natureza, bool classTrib85)
        {
            NaturezaRendimento n = Buscar(natureza);
            return n == null ? "" : Formatar(n.CodigoReceita(IR, classTrib85));
        }

        /// <summary>"CSLL 6228-05 · COFINS 6243-05 · PIS 6230-05" (só os que a tabela traz).</summary>
        public static string CodigosCsrf(string natureza, bool classTrib85)
        {
            NaturezaRendimento n = Buscar(natureza);
            if (n == null) return "";
            var partes = new List<string>();
            string[] nomes = { "CSLL", "COFINS", "PIS" };
            int[] tributos = { CSLL, COFINS, PIS };
            for (int i = 0; i < tributos.Length; i++)
            {
                string c = n.CodigoReceita(tributos[i], classTrib85);
                if (c != "") partes.Add(nomes[i] + " " + Formatar(c));
            }
            return string.Join(" · ", partes);
        }

        /// <summary>Natureza sugerida só pelos dados da nota (a tela também considera a escolha feita para o mesmo prestador).</summary>
        public static string Sugerir(Nota n)
        {
            if (n.Tipo == "nfe") return NaturezaMercadorias;
            if (Formatos.SoDigitos(n.EmitenteDoc).Length == 11) return NaturezaPessoaFisica;
            string c = Formatos.SoDigitos(n.CodigoServico);
            if (c.Length >= 4)
            {
                string item = c.Substring(0, 4);
                string natureza;
                if (PorServico.TryGetValue(item, out natureza)) return natureza;
                // 10.xx agenciamento, corretagem ou intermediação (menos 10.08, agência de publicidade)
                if (item.StartsWith("10") && item != "1008") return "17034";
            }
            return NaturezaDemaisServicos;
        }
    }
}
