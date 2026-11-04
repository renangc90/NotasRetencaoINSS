using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace NotasRetencaoINSS
{
    public class CertificadoInfo
    {
        public X509Certificate2 Certificado;
        public string Nome;
        public string Cnpj;
        public DateTime ValidoAte;
        public string Local;   // "Usuário" | "Computador" | "Arquivo"

        public bool Vencido { get { return ValidoAte < DateTime.Now; } }

        public override string ToString()
        {
            string cnpj = string.IsNullOrEmpty(Cnpj) ? "sem CNPJ" : "CNPJ " + Formatos.CnpjCpf(Cnpj);
            return Nome + " — " + cnpj + " — " + (Vencido ? "VENCIDO em " : "vale até ") + ValidoAte.ToString("dd/MM/yyyy");
        }
    }

    public static class Certificados
    {
        // OID 2.16.76.1.3.3 (CNPJ no e-CNPJ ICP-Brasil), em DER: 06 05 60 4C 01 03 03
        static readonly byte[] OidCnpj = { 0x06, 0x05, 0x60, 0x4C, 0x01, 0x03, 0x03 };

        /// <summary>CNPJ do e-CNPJ: no SAN (OID 2.16.76.1.3.3) ou, de reserva, no CN "NOME:12345678000199".</summary>
        public static string CnpjDoCertificado(X509Certificate2 c)
        {
            foreach (X509Extension ext in c.Extensions)
            {
                if (ext.Oid == null || ext.Oid.Value != "2.5.29.17") continue;
                byte[] raw = ext.RawData;
                for (int i = 0; i + OidCnpj.Length < raw.Length; i++)
                {
                    bool igual = true;
                    for (int j = 0; j < OidCnpj.Length && igual; j++) igual = raw[i + j] == OidCnpj[j];
                    if (!igual) continue;
                    // O valor vem logo depois do OID, envelopado em [0] + string: pega a 1ª sequência de 14 dígitos.
                    int fim = Math.Min(raw.Length, i + OidCnpj.Length + 40);
                    string trecho = Encoding.ASCII.GetString(raw, i + OidCnpj.Length, fim - i - OidCnpj.Length);
                    Match m = Regex.Match(trecho, @"\d{14}");
                    if (m.Success) return m.Value;
                }
            }
            Match cn = Regex.Match(c.GetNameInfo(X509NameType.SimpleName, false) ?? "", @":(\d{14})\s*$");
            return cn.Success ? cn.Groups[1].Value : "";
        }

        public static string NomeDoCertificado(X509Certificate2 c)
        {
            string nome = c.GetNameInfo(X509NameType.SimpleName, false) ?? c.Subject;
            int p = nome.LastIndexOf(':');
            return p > 0 ? nome.Substring(0, p).Trim() : nome.Trim();
        }

        static CertificadoInfo Info(X509Certificate2 c, string local)
        {
            return new CertificadoInfo { Certificado = c, Nome = NomeDoCertificado(c), Cnpj = CnpjDoCertificado(c), ValidoAte = c.NotAfter, Local = local };
        }

        /// <summary>Certificados com chave privada instalados no Windows (A1 importado ou token/cartão A3). e-CNPJ primeiro.</summary>
        public static List<CertificadoInfo> ListarInstalados()
        {
            var lista = new List<CertificadoInfo>();
            var vistos = new HashSet<string>();
            foreach (StoreLocation loc in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            {
                var store = new X509Store(StoreName.My, loc);
                try
                {
                    store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    foreach (X509Certificate2 c in store.Certificates)
                    {
                        if (!c.HasPrivateKey || !vistos.Add(c.Thumbprint)) continue;
                        lista.Add(Info(c, loc == StoreLocation.CurrentUser ? "Usuário" : "Computador"));
                    }
                }
                catch (CryptographicException) { }
                finally { store.Close(); }
            }
            lista.Sort((a, b) =>
            {
                int pa = string.IsNullOrEmpty(a.Cnpj) ? 1 : 0, pb = string.IsNullOrEmpty(b.Cnpj) ? 1 : 0;
                if (pa != pb) return pa - pb;
                if (a.Vencido != b.Vencido) return a.Vencido ? 1 : -1;
                return b.ValidoAte.CompareTo(a.ValidoAte);
            });
            return lista;
        }

        public static CertificadoInfo AbrirPfx(string caminho, string senha)
        {
            try
            {
                var c = new X509Certificate2(caminho, senha, X509KeyStorageFlags.UserKeySet);
                if (!c.HasPrivateKey) throw new ErroAviso("O arquivo não tem a chave privada do certificado.");
                return Info(c, "Arquivo");
            }
            catch (CryptographicException ex)
            {
                if (ex.Message.IndexOf("senha", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new ErroAviso("Senha do certificado incorreta.");
                throw new ErroAviso("Não foi possível abrir o certificado: " + ex.Message.Trim());
            }
        }

        /// <summary>O certificado escolhido na configuração.</summary>
        public static CertificadoInfo Carregar(Configuracao cfg)
        {
            if (cfg.OrigemCertificado == "pfx")
            {
                if (string.IsNullOrEmpty(cfg.PfxCaminho) || !System.IO.File.Exists(cfg.PfxCaminho))
                    throw new ErroAviso("Arquivo do certificado não encontrado. Abra Configurações e escolha o certificado de novo.");
                return AbrirPfx(cfg.PfxCaminho, Armazenamento.Desproteger(cfg.PfxSenhaProtegida));
            }
            if (string.IsNullOrEmpty(cfg.CertificadoThumbprint))
                throw new ErroAviso("Nenhum certificado escolhido. Abra Configurações.");
            foreach (CertificadoInfo c in ListarInstalados())
                if (string.Equals(c.Certificado.Thumbprint, cfg.CertificadoThumbprint, StringComparison.OrdinalIgnoreCase)) return c;
            throw new ErroAviso("O certificado escolhido não está mais instalado neste computador (ou o token não está conectado). Abra Configurações.");
        }
    }
}
