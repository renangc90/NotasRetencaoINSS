using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NotasRetencaoINSS
{
    /// <summary>HTTP mínimo em 127.0.0.1 para simular ADN e SEFAZ (sem TLS — o TLS é o do Windows).</summary>
    class ServidorFalso : IDisposable
    {
        public class Pedido { public string Metodo, Caminho, Corpo; }
        public delegate void Rota(Pedido p, out int status, out string corpo);

        readonly TcpListener ouvinte = new TcpListener(IPAddress.Loopback, 0);
        readonly Rota rota;
        public readonly List<Pedido> Pedidos = new List<Pedido>();
        volatile bool ativo = true;

        public int Porta { get { return ((IPEndPoint)ouvinte.LocalEndpoint).Port; } }
        public string Url { get { return "http://127.0.0.1:" + Porta; } }

        public ServidorFalso(Rota rota)
        {
            this.rota = rota;
            ouvinte.Start();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        void Loop()
        {
            while (ativo)
            {
                TcpClient c;
                try { c = ouvinte.AcceptTcpClient(); } catch (Exception) { return; }
                try { Atender(c); } catch (Exception) { } finally { c.Close(); }
            }
        }

        void Atender(TcpClient c)
        {
            NetworkStream s = c.GetStream();
            var cabecalho = new MemoryStream();
            int b, fim = 0;
            while (fim < 4 && (b = s.ReadByte()) >= 0)
            {
                cabecalho.WriteByte((byte)b);
                fim = (b == '\r' || b == '\n') ? fim + 1 : 0;
            }
            string[] linhas = Encoding.ASCII.GetString(cabecalho.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] primeira = linhas[0].Split(' ');
            int tamanho = 0;
            foreach (string l in linhas)
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) tamanho = int.Parse(l.Substring(15).Trim());
            byte[] corpo = new byte[tamanho];
            for (int lido = 0; lido < tamanho; ) lido += s.Read(corpo, lido, tamanho - lido);

            var p = new Pedido { Metodo = primeira[0], Caminho = primeira[1], Corpo = Encoding.UTF8.GetString(corpo) };
            lock (Pedidos) Pedidos.Add(p);
            int status;
            string resposta;
            rota(p, out status, out resposta);
            byte[] r = Encoding.UTF8.GetBytes(resposta ?? "");
            byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " X\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + r.Length + "\r\nConnection: close\r\n\r\n");
            s.Write(h, 0, h.Length);
            s.Write(r, 0, r.Length);
        }

        public void Dispose()
        {
            ativo = false;
            ouvinte.Stop();
        }
    }
}
