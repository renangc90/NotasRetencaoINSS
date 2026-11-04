using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace NotasRetencaoINSS
{
    public class RespostaHttp
    {
        public int Status;
        public string Texto;
    }

    /// <summary>
    /// HTTPS com o certificado da prefeitura como certificado de cliente (TLS mútuo).
    /// Usa o proxy configurado no Windows (com o login do usuário), como o navegador.
    /// </summary>
    public static class ClienteFiscal
    {
        const int TimeoutMs = 60000;

        public static void ConfigurarRede()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            IWebProxy proxy = WebRequest.DefaultWebProxy;
            if (proxy != null) proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
        }

        public static RespostaHttp Get(X509Certificate2 cert, string url, string servico)
        {
            return Requisitar(cert, url, "GET", null, null, servico);
        }

        public static RespostaHttp PostSoap(X509Certificate2 cert, string url, string action, string envelope, string servico)
        {
            return Requisitar(cert, url, "POST", "application/soap+xml; charset=utf-8; action=\"" + action + "\"", envelope, servico);
        }

        static RespostaHttp Requisitar(X509Certificate2 cert, string url, string metodo, string contentType, string corpo, string servico)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = metodo;
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.KeepAlive = true;
            req.Accept = corpo == null ? "application/json" : "application/soap+xml, text/xml";
            req.UserAgent = "NotasRetencaoINSS/1.0";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (cert != null) req.ClientCertificates.Add(cert);

            try
            {
                if (corpo != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(corpo);
                    req.ContentType = contentType;
                    req.ContentLength = bytes.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse()) return Ler(resp);
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null) using (resp) return Ler(resp);
                throw new ErroAviso(MensagemDeRede(ex, url, servico));
            }
        }

        static RespostaHttp Ler(HttpWebResponse resp)
        {
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return new RespostaHttp { Status = (int)resp.StatusCode, Texto = sr.ReadToEnd() };
        }

        static string MensagemDeRede(WebException ex, string url, string servico)
        {
            string host = new Uri(url).Host;
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout:
                    return servico + " (" + host + ") demorou demais para responder. Tente novamente mais tarde.";
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "Sem conexão com " + servico + " (" + host + "). Confira a internet deste computador ou se a rede da prefeitura bloqueia esse endereço.";
                case WebExceptionStatus.TrustFailure:
                    return "Não foi possível validar o certificado HTTPS de " + host + ". Pode haver um proxy/antivírus interceptando a conexão.";
                case WebExceptionStatus.SecureChannelFailure:
                    return servico + " recusou a conexão segura com o certificado escolhido. Confira se é o e-CNPJ da prefeitura, se está na validade e, se for token, se está conectado.";
            }
            return "Falha ao falar com " + servico + " (" + host + "): " + ex.Message;
        }

        /// <summary>401/403 (subcódigos do IIS da SEFAZ: 403.7, 403.16, 403.17).</summary>
        public static string ErroCertificado(string servico, int status, string corpo)
        {
            Match m = Regex.Match(corpo ?? "", @"\b403[.,](7|16|17)\b");
            string sub = m.Success ? m.Groups[1].Value : "";
            if (status == 403 && sub == "17") return servico + " HTTP 403.17: o certificado está vencido ou ainda não vale.";
            if (status == 403 && (sub == "7" || sub == "16")) return servico + " HTTP 403." + sub + ": o certificado não foi aceito. Confira se é o e-CNPJ A1 da prefeitura.";
            if (status == 403) return servico + " HTTP 403: acesso recusado com o certificado escolhido. Confira se é o e-CNPJ da prefeitura; se persistir, o serviço pode estar fora do ar.";
            return servico + " HTTP 401: o certificado não tem autorização para esta consulta. Confira se é o e-CNPJ da prefeitura.";
        }

        public static string EnvelopeSoap(string ns, string operacao, string corpo)
        {
            string dentro = string.IsNullOrEmpty(operacao)
                ? "<nfeDadosMsg xmlns=\"" + ns + "\">" + corpo + "</nfeDadosMsg>"
                : "<" + operacao + " xmlns=\"" + ns + "\"><nfeDadosMsg>" + corpo + "</nfeDadosMsg></" + operacao + ">";
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body>"
                + dentro + "</soap12:Body></soap12:Envelope>";
        }
    }
}
