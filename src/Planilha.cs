using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace NotasRetencaoINSS
{
    /// <summary>Gera .xlsx de verdade (sem Excel instalado): aba das notas e aba somada por prestador.</summary>
    public static class Planilha
    {
        // Estilos (styles.xml): 0 normal, 1 cabeçalho, 2 dinheiro, 3 data, 4 total dinheiro, 5 total texto, 6 título
        const int SCabecalho = 1, SDinheiro = 2, SData = 3, STotalDinheiro = 4, STotalTexto = 5, STitulo = 6;

        class Celula
        {
            public string Texto;
            public decimal? Numero;
            public DateTime? Data;
            public string Formula;
            public int Estilo;
        }

        static Celula T(string s, int estilo = 0) { return new Celula { Texto = s ?? "", Estilo = estilo }; }
        static Celula N(decimal? v, int estilo = SDinheiro) { return v.HasValue ? new Celula { Numero = v, Estilo = estilo } : T(""); }
        static Celula D(string iso)
        {
            DateTime? d = Formatos.ParaData(iso);
            return d.HasValue ? new Celula { Data = d, Estilo = SData } : T("");
        }

        public static void Gerar(string caminho, string titulo, IList<Nota> notas, Func<Nota, bool> cancelada)
        {
            var linhasNotas = new List<List<Celula>>();
            linhasNotas.Add(new List<Celula> { T(titulo, STitulo) });
            linhasNotas.Add(new List<Celula>());
            string[] cab = { "Tipo", "Número", "Série", "Emissão", "Competência", "Prestador / Emitente", "CNPJ/CPF", "Município",
                             "Código do serviço", "Descrição", "Valor da nota", "Base INSS (NF-e)", "INSS retido", "Situação", "Chave de acesso" };
            linhasNotas.Add(cab.Select(c => T(c, SCabecalho)).ToList());
            int primeira = linhasNotas.Count + 1;
            foreach (Nota n in notas)
            {
                bool canc = cancelada(n);
                linhasNotas.Add(new List<Celula>
                {
                    T(n.Tipo == "nfse" ? "NFS-e" : "NF-e"), T(n.Numero), T(n.Serie), D(n.Emissao), D(n.Competencia),
                    T(n.EmitenteNome), T(Formatos.CnpjCpf(n.EmitenteDoc)), T(n.Municipio), T(n.CodigoServico), T(n.Descricao),
                    N(n.Valor), N(n.BaseInss), N(n.InssRetido), T(canc ? "Cancelada" : SituacaoTexto(n)), T(n.Chave),
                });
            }
            int ultima = linhasNotas.Count;
            var ativas = notas.Where(n => !cancelada(n)).ToList();
            linhasNotas.Add(new List<Celula>
            {
                T("Total (sem canceladas)", STotalTexto), T(""), T(""), T(""), T(""), T(""), T(""), T(""), T(""), T(""),
                new Celula { Formula = ultima >= primeira ? "SUMIF(N" + primeira + ":N" + ultima + ",\"<>Cancelada\",K" + primeira + ":K" + ultima + ")" : null, Numero = ativas.Sum(n => n.Valor), Estilo = STotalDinheiro },
                T(""),
                new Celula { Formula = ultima >= primeira ? "SUMIF(N" + primeira + ":N" + ultima + ",\"<>Cancelada\",M" + primeira + ":M" + ultima + ")" : null, Numero = ativas.Sum(n => n.InssRetido), Estilo = STotalDinheiro },
            });

            var linhasPrest = new List<List<Celula>>();
            linhasPrest.Add(new List<Celula> { T(titulo + " — somado por prestador (sem canceladas)", STitulo) });
            linhasPrest.Add(new List<Celula>());
            linhasPrest.Add(new[] { "CNPJ/CPF", "Prestador / Emitente", "Notas", "Valor das notas", "INSS retido" }.Select(c => T(c, SCabecalho)).ToList());
            foreach (var g in ativas.GroupBy(n => n.EmitenteDoc ?? "").OrderBy(g => g.First().EmitenteNome))
            {
                linhasPrest.Add(new List<Celula>
                {
                    T(Formatos.CnpjCpf(g.Key)), T(g.First().EmitenteNome), new Celula { Numero = g.Count() },
                    N(g.Sum(n => n.Valor)), N(g.Sum(n => n.InssRetido)),
                });
            }
            linhasPrest.Add(new List<Celula>
            {
                T("Total", STotalTexto), T(""), new Celula { Numero = ativas.Count, Estilo = STotalTexto },
                N(ativas.Sum(n => n.Valor), STotalDinheiro), N(ativas.Sum(n => n.InssRetido), STotalDinheiro),
            });

            double[] largNotas = { 8, 11, 7, 11, 12, 42, 20, 20, 14, 45, 15, 15, 14, 12, 50 };
            double[] largPrest = { 20, 50, 8, 18, 16 };

            if (File.Exists(caminho)) File.Delete(caminho);
            using (var zip = ZipFile.Open(caminho, ZipArchiveMode.Create))
            {
                Escrever(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                    + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                    + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                    + "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                    + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                Escrever(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Escrever(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                    + "<sheets><sheet name=\"Notas\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Por prestador\" sheetId=\"2\" r:id=\"rId2\"/></sheets>"
                    + "<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">Notas!$A$3:$O$" + Math.Max(3, ultima) + "</definedName></definedNames>"
                    + "<calcPr fullCalcOnLoad=\"1\"/></workbook>");
                Escrever(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                    + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>"
                    + "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Escrever(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                    + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\"/></numFmts>"
                    + "<fonts count=\"3\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"13\"/><name val=\"Calibri\"/></font></fonts>"
                    + "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCE6F1\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>"
                    + "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top style=\"thin\"/><bottom/><diagonal/></border></borders>"
                    + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                    + "<cellXfs count=\"7\">"
                    + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                    + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>"
                    + "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
                    + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
                    + "<xf numFmtId=\"4\" fontId=\"1\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyBorder=\"1\"/>"
                    + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\"/>"
                    + "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                    + "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
                Escrever(zip, "xl/worksheets/sheet1.xml", Aba(linhasNotas, largNotas, "A3:O" + Math.Max(3, ultima)));
                Escrever(zip, "xl/worksheets/sheet2.xml", Aba(linhasPrest, largPrest, null));
            }
        }

        public static string SituacaoTexto(Nota n)
        {
            if (n.Situacao == "denegada") return "Denegada";
            if (n.Tipo == "nfe" && !n.Completo) return "Só resumo";
            return "Autorizada";
        }

        static void Escrever(ZipArchive zip, string nome, string conteudo)
        {
            ZipArchiveEntry e = zip.CreateEntry(nome, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(conteudo);
        }

        static string Coluna(int i)
        {
            string s = "";
            for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
            return s;
        }

        static string Aba(List<List<Celula>> linhas, double[] larguras, string autoFiltro)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"3\" topLeftCell=\"A4\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sb.Append("<cols>");
            for (int i = 0; i < larguras.Length; i++)
                sb.Append("<col min=\"").Append(i + 1).Append("\" max=\"").Append(i + 1).Append("\" width=\"").Append(larguras[i].ToString(CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
            sb.Append("</cols><sheetData>");
            for (int l = 0; l < linhas.Count; l++)
            {
                sb.Append("<row r=\"").Append(l + 1).Append("\">");
                for (int c = 0; c < linhas[l].Count; c++)
                {
                    Celula cel = linhas[l][c];
                    string r = Coluna(c) + (l + 1);
                    string s = cel.Estilo > 0 ? " s=\"" + cel.Estilo + "\"" : "";
                    if (cel.Formula != null || cel.Numero.HasValue)
                    {
                        sb.Append("<c r=\"").Append(r).Append("\"").Append(s).Append(">");
                        if (cel.Formula != null) sb.Append("<f>").Append(Formatos.EscaparXml(cel.Formula)).Append("</f>");
                        if (cel.Numero.HasValue) sb.Append("<v>").Append(cel.Numero.Value.ToString(CultureInfo.InvariantCulture)).Append("</v>");
                        sb.Append("</c>");
                    }
                    else if (cel.Data.HasValue)
                        sb.Append("<c r=\"").Append(r).Append("\"").Append(s).Append("><v>").Append(cel.Data.Value.ToOADate().ToString(CultureInfo.InvariantCulture)).Append("</v></c>");
                    else if (cel.Texto != "")
                        sb.Append("<c r=\"").Append(r).Append("\" t=\"inlineStr\"").Append(s).Append("><is><t xml:space=\"preserve\">").Append(Formatos.EscaparXml(LimparXml(cel.Texto))).Append("</t></is></c>");
                    else if (cel.Estilo > 0)
                        sb.Append("<c r=\"").Append(r).Append("\"").Append(s).Append("/>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (autoFiltro != null) sb.Append("<autoFilter ref=\"").Append(autoFiltro).Append("\"/>");
            sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>");
            return sb.ToString();
        }

        /// <summary>Caracteres de controle quebram o XML da planilha.</summary>
        static string LimparXml(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) if (ch >= 0x20 || ch == '\t' || ch == '\n' || ch == '\r') sb.Append(ch);
            return sb.ToString();
        }
    }
}
