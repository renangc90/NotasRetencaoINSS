using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NotasRetencaoINSS
{
    /// <summary>
    /// Onde se desenha uma página A4 em pontos (1/72 pol.), com y contado do topo.
    /// O PDF é uma implementação; os testes têm outra que desenha num bitmap para conferir o visual.
    /// </summary>
    public interface IDesenho
    {
        void NovaPagina();
        /// <summary>Texto com o topo da linha em y.</summary>
        void Texto(float x, float y, string texto, float tamanho, bool negrito);
        void Linha(float x1, float y1, float x2, float y2, float espessura);
        /// <summary>fundo: 1 = sem fundo; 0,9 = cinza claro.</summary>
        void Retangulo(float x, float y, float largura, float altura, float espessura, float fundo);
        void MarcaDagua(string texto);
    }

    /// <summary>Medidas da Helvetica (iguais às da Arial), para quebrar o texto do mesmo jeito em qualquer desenho.</summary>
    public static class Fonte
    {
        // Larguras (milésimos do corpo) dos caracteres 32 a 126 — AFM padrão da Adobe.
        static readonly short[] Normal =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
        };
        static readonly short[] Negrito =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
        };

        public static float Largura(string texto, float tamanho, bool negrito)
        {
            if (string.IsNullOrEmpty(texto)) return 0f;
            short[] t = negrito ? Negrito : Normal;
            int soma = 0;
            foreach (char c0 in texto)
            {
                char c = SemAcento(c0);
                soma += c >= 32 && c <= 126 ? t[c - 32] : 556;
            }
            return soma * tamanho / 1000f;
        }

        static char SemAcento(char c)
        {
            if (c < 128) return c;
            string d = c.ToString().Normalize(NormalizationForm.FormD);
            return d.Length > 0 && d[0] < 128 ? d[0] : c;
        }

        /// <summary>Quebra em linhas que cabem na largura (palavra longa demais é cortada).</summary>
        public static List<string> Quebrar(string texto, float largura, float tamanho, bool negrito)
        {
            var linhas = new List<string>();
            foreach (string paragrafo in (texto ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string atual = "";
                foreach (string palavra0 in paragrafo.Split(' '))
                {
                    string palavra = palavra0;
                    string tentativa = atual == "" ? palavra : atual + " " + palavra;
                    if (Largura(tentativa, tamanho, negrito) <= largura) { atual = tentativa; continue; }
                    if (atual != "") linhas.Add(atual);
                    while (Largura(palavra, tamanho, negrito) > largura && palavra.Length > 1)
                    {
                        int n = palavra.Length - 1;
                        while (n > 1 && Largura(palavra.Substring(0, n), tamanho, negrito) > largura) n--;
                        linhas.Add(palavra.Substring(0, n));
                        palavra = palavra.Substring(n);
                    }
                    atual = palavra;
                }
                linhas.Add(atual);
            }
            return linhas;
        }
    }

    /// <summary>
    /// PDF 1.4 mínimo, sem biblioteca: páginas A4, Helvetica/Helvetica-Bold (fontes padrão, não embutidas)
    /// com WinAnsiEncoding (acentos do português), linhas, retângulos e marca d'água.
    /// </summary>
    public class DocumentoPdf : IDesenho
    {
        public const float Largura = 595.28f, Altura = 841.89f;
        static readonly Encoding WinAnsi = Encoding.GetEncoding(1252);

        readonly List<StringBuilder> paginas = new List<StringBuilder>();
        StringBuilder atual;

        public int Paginas { get { return paginas.Count; } }

        static string N(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        StringBuilder Pagina()
        {
            if (atual == null) NovaPagina();
            return atual;
        }

        public void NovaPagina()
        {
            atual = new StringBuilder();
            paginas.Add(atual);
        }

        public void Texto(float x, float y, string texto, float tamanho, bool negrito)
        {
            if (string.IsNullOrEmpty(texto)) return;
            float linhaBase = Altura - y - tamanho * 0.8f;
            Pagina().Append("BT /").Append(negrito ? "F2 " : "F1 ").Append(N(tamanho)).Append(" Tf ")
                .Append(N(x)).Append(' ').Append(N(linhaBase)).Append(" Td (").Append(Escapar(texto)).Append(") Tj ET\n");
        }

        public void Linha(float x1, float y1, float x2, float y2, float espessura)
        {
            Pagina().Append(N(espessura)).Append(" w ").Append(N(x1)).Append(' ').Append(N(Altura - y1)).Append(" m ")
                .Append(N(x2)).Append(' ').Append(N(Altura - y2)).Append(" l S\n");
        }

        public void Retangulo(float x, float y, float largura, float altura, float espessura, float fundo)
        {
            string r = N(x) + " " + N(Altura - y - altura) + " " + N(largura) + " " + N(altura) + " re ";
            StringBuilder p = Pagina();
            if (fundo < 1f) p.Append("q ").Append(N(fundo)).Append(" g ").Append(r).Append("f Q\n");
            if (espessura > 0f) p.Append(N(espessura)).Append(" w ").Append(r).Append("S\n");
        }

        public void MarcaDagua(string texto)
        {
            const float tamanho = 96f;
            float w = Fonte.Largura(texto, tamanho, true);
            double a = Math.Atan2(Altura, Largura);
            float cos = (float)Math.Cos(a), sen = (float)Math.Sin(a);
            // Centro da página, texto inclinado na diagonal.
            float x = Largura / 2 - (w / 2) * cos + (tamanho / 3) * sen;
            float y = Altura / 2 - (w / 2) * sen - (tamanho / 3) * cos;
            Pagina().Append("q 0.82 g BT /F2 ").Append(N(tamanho)).Append(" Tf ")
                .Append(N(cos)).Append(' ').Append(N(sen)).Append(' ').Append(N(-sen)).Append(' ').Append(N(cos)).Append(' ')
                .Append(N(x)).Append(' ').Append(N(y)).Append(" Tm (").Append(Escapar(texto)).Append(") Tj ET Q\n");
        }

        /// <summary>Texto de PDF: \ ( ) escapados; o que não existe no WinAnsi vira "?" na hora de gravar.</summary>
        static string Escapar(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '\\' || c == '(' || c == ')') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public byte[] Gerar()
        {
            if (paginas.Count == 0) NovaPagina();
            var ms = new MemoryStream();
            var offsets = new List<long>();
            Action<string> escrever = s => { byte[] b = WinAnsi.GetBytes(s); ms.Write(b, 0, b.Length); };
            Action<int, string> objeto = (num, corpo) =>
            {
                while (offsets.Count < num) offsets.Add(0);
                offsets[num - 1] = ms.Position;
                escrever(num + " 0 obj\n" + corpo + "\nendobj\n");
            };

            escrever("%PDF-1.4\n%âãÏÓ\n");
            int n = paginas.Count;
            // 1 catálogo, 2 páginas, 3 e 4 fontes, depois (página, conteúdo) para cada página.
            var kids = new StringBuilder();
            for (int i = 0; i < n; i++) kids.Append(5 + i * 2).Append(" 0 R ");
            objeto(1, "<< /Type /Catalog /Pages 2 0 R >>");
            objeto(2, "<< /Type /Pages /Kids [" + kids.ToString().Trim() + "] /Count " + n + " >>");
            objeto(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            objeto(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            for (int i = 0; i < n; i++)
            {
                int pag = 5 + i * 2, cont = pag + 1;
                objeto(pag, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(Largura) + " " + N(Altura) + "] "
                    + "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents " + cont + " 0 R >>");
                byte[] conteudo = WinAnsi.GetBytes(paginas[i].ToString());
                while (offsets.Count < cont) offsets.Add(0);
                offsets[cont - 1] = ms.Position;
                escrever(cont + " 0 obj\n<< /Length " + conteudo.Length + " >>\nstream\n");
                ms.Write(conteudo, 0, conteudo.Length);
                escrever("\nendstream\nendobj\n");
            }

            long xref = ms.Position;
            var x = new StringBuilder();
            x.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (long o in offsets) x.Append(o.ToString("D10")).Append(" 00000 n \n");
            x.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            escrever(x.ToString());
            return ms.ToArray();
        }
    }
}
