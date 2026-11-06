using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace NotasRetencaoINSS
{
    /// <summary>
    /// Desenha as mesmas páginas do PDF num bitmap (Arial tem as medidas da Helvetica),
    /// para conferir o visual nas capturas sem precisar de um leitor de PDF.
    /// </summary>
    class DesenhoBitmap : IDesenho, IDisposable
    {
        const float Escala = 1.6f;   // pixels por ponto
        public readonly List<Bitmap> Paginas = new List<Bitmap>();
        Graphics g;

        public void NovaPagina()
        {
            if (g != null) g.Dispose();
            var bmp = new Bitmap((int)(DocumentoPdf.Largura * Escala), (int)(DocumentoPdf.Altura * Escala));
            Paginas.Add(bmp);
            g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public void Texto(float x, float y, string texto, float tamanho, bool negrito)
        {
            using (var f = new Font("Arial", tamanho * Escala, negrito ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                g.DrawString(texto, f, Brushes.Black, x * Escala, y * Escala, StringFormat.GenericTypographic);
        }

        public void Linha(float x1, float y1, float x2, float y2, float espessura)
        {
            using (var p = new Pen(Color.Black, espessura * Escala))
                g.DrawLine(p, x1 * Escala, y1 * Escala, x2 * Escala, y2 * Escala);
        }

        public void Retangulo(float x, float y, float largura, float altura, float espessura, float fundo)
        {
            if (fundo < 1f)
                using (var b = new SolidBrush(Color.FromArgb((int)(fundo * 255), (int)(fundo * 255), (int)(fundo * 255))))
                    g.FillRectangle(b, x * Escala, y * Escala, largura * Escala, altura * Escala);
            if (espessura > 0f)
                using (var p = new Pen(Color.Black, espessura * Escala))
                    g.DrawRectangle(p, x * Escala, y * Escala, largura * Escala, altura * Escala);
        }

        public void MarcaDagua(string texto)
        {
            var estado = g.Save();
            float w = DocumentoPdf.Largura * Escala, h = DocumentoPdf.Altura * Escala;
            g.TranslateTransform(w / 2, h / 2);
            g.RotateTransform(-(float)(Math.Atan2(DocumentoPdf.Altura, DocumentoPdf.Largura) * 180 / Math.PI));
            using (var f = new Font("Arial", 96 * Escala, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.FromArgb(209, 209, 209)))
            {
                SizeF t = g.MeasureString(texto, f);
                g.DrawString(texto, f, b, -t.Width / 2, -t.Height / 2);
            }
            g.Restore(estado);
        }

        public void Dispose()
        {
            if (g != null) g.Dispose();
            foreach (Bitmap b in Paginas) b.Dispose();
        }
    }
}
