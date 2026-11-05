using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NotasRetencaoINSS
{
    /// <summary>
    /// Tudo fica numa pasta "dados" ao lado do programa (config.json, notas.json e os XML),
    /// para dar para copiar o programa com o histórico. Se ali não der para gravar,
    /// usa %LOCALAPPDATA%\NotasRetencaoINSS.
    /// </summary>
    public static class Armazenamento
    {
        static string pasta;

        public static string Pasta
        {
            get
            {
                if (pasta == null) pasta = EscolherPasta();
                return pasta;
            }
            set { pasta = value; Directory.CreateDirectory(value); }
        }

        static string EscolherPasta()
        {
            string aoLado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dados");
            try
            {
                Directory.CreateDirectory(aoLado);
                string teste = Path.Combine(aoLado, ".teste");
                File.WriteAllText(teste, "ok");
                File.Delete(teste);
                return aoLado;
            }
            catch (Exception)
            {
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotasRetencaoINSS");
                Directory.CreateDirectory(local);
                return local;
            }
        }

        /// <summary>Rodando de dentro do .zip o Windows usa uma pasta temporária — os dados se perderiam.</summary>
        public static bool RodandoDePastaTemporaria()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + "\\";
            string temp = Path.GetTempPath().TrimEnd('\\') + "\\";
            return baseDir.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }

        static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        /// <summary>Grava num .tmp e troca, para não corromper o arquivo se faltar luz no meio.</summary>
        static void GravarAtomico(string caminho, string conteudo)
        {
            string tmp = caminho + ".tmp";
            File.WriteAllText(tmp, conteudo, new UTF8Encoding(false));
            if (File.Exists(caminho)) File.Replace(tmp, caminho, caminho + ".bak");
            else File.Move(tmp, caminho);
        }

        static T Ler<T>(string caminho) where T : new()
        {
            if (!File.Exists(caminho))
            {
                // Sobrou só o backup (troca interrompida): usa ele.
                if (File.Exists(caminho + ".bak")) caminho = caminho + ".bak";
                else return new T();
            }
            return Json().Deserialize<T>(File.ReadAllText(caminho, Encoding.UTF8));
        }

        // ------------------------------------------------------------ configuração

        public static Configuracao LerConfiguracao()
        {
            string caminho = Path.Combine(Pasta, "config.json");
            if (!File.Exists(caminho) && !File.Exists(caminho + ".bak")) return Configuracao.Padrao();
            return Ler<Configuracao>(caminho);
        }

        public static void SalvarConfiguracao(Configuracao cfg)
        {
            GravarAtomico(Path.Combine(Pasta, "config.json"), Json().Serialize(cfg));
        }

        public static string Proteger(string senha)
        {
            if (string.IsNullOrEmpty(senha)) return "";
            byte[] p = ProtectedData.Protect(Encoding.UTF8.GetBytes(senha), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(p);
        }

        public static string Desproteger(string protegida)
        {
            if (string.IsNullOrEmpty(protegida)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protegida), null, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                throw new ErroAviso("A senha do certificado foi salva por outro usuário do Windows. Abra Configurações e digite a senha de novo.");
            }
        }

        // ------------------------------------------------------------ notas

        public static BaseNotas LerNotas()
        {
            BaseNotas b = Ler<BaseNotas>(Path.Combine(Pasta, "notas.json"));
            if (b.Notas == null) b.Notas = new List<Nota>();
            if (b.Eventos == null) b.Eventos = new List<EventoNota>();
            if (b.Versao < BaseNotas.VersaoAtual)
            {
                CompletarDoXml(b);
                b.Versao = BaseNotas.VersaoAtual;
                if (b.Notas.Count > 0) SalvarNotas(b);
            }
            return b;
        }

        /// <summary>Versão 1 → 2: IR, CSLL/PIS/COFINS e Simples saem dos XML já guardados, sem baixar de novo.</summary>
        static void CompletarDoXml(BaseNotas b)
        {
            foreach (Nota n in b.Notas)
            {
                if (string.IsNullOrEmpty(n.ArquivoXml)) continue;
                string caminho = CaminhoCompleto(n.ArquivoXml);
                if (!File.Exists(caminho)) continue;
                try
                {
                    string xml = File.ReadAllText(caminho, Encoding.UTF8);
                    Lido l = n.Tipo == "nfse" ? LeitorXml.LerNfse(xml, "NFSE", n.Chave, n.Nsu) : LeitorXml.LerNfe(xml, "procNFe", n.Nsu);
                    if (l == null || l.Nota == null) continue;
                    n.IrRetido = l.Nota.IrRetido;
                    n.CsrfRetida = l.Nota.CsrfRetida;
                    n.SimplesNacional = l.Nota.SimplesNacional;
                }
                catch (Exception) { }   // XML ilegível: a nota fica sem IR, como antes
            }
        }

        public static void SalvarNotas(BaseNotas b)
        {
            GravarAtomico(Path.Combine(Pasta, "notas.json"), Json().Serialize(b));
        }

        public static string CaminhoCompleto(string relativo)
        {
            return Path.Combine(Pasta, relativo);
        }

        /// <summary>pdf\nfse\2026-10\chave.pdf — o PDF baixado do ADN fica guardado para não baixar de novo.</summary>
        public static string CaminhoPdf(Nota n)
        {
            string mes = string.IsNullOrEmpty(n.Emissao) || n.Emissao.Length < 7 ? "sem-data" : n.Emissao.Substring(0, 7);
            return Path.Combine(Pasta, "pdf", n.Tipo, mes, n.Chave + ".pdf");
        }

        public static void GravarPdf(Nota n, byte[] pdf)
        {
            string caminho = CaminhoPdf(n);
            Directory.CreateDirectory(Path.GetDirectoryName(caminho));
            string tmp = caminho + ".tmp";
            File.WriteAllBytes(tmp, pdf);
            if (File.Exists(caminho)) File.Delete(caminho);
            File.Move(tmp, caminho);
        }

        /// <summary>xml\nfse\2026-10\chave.xml — devolve o caminho relativo.</summary>
        public static string GravarXml(string tipo, string pastaMes, string nomeArquivo, string xml)
        {
            string relativo = Path.Combine("xml", tipo, string.IsNullOrEmpty(pastaMes) ? "sem-data" : pastaMes, nomeArquivo);
            string completo = Path.Combine(Pasta, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(completo));
            File.WriteAllText(completo, xml, new UTF8Encoding(false));
            return relativo;
        }
    }

    /// <summary>As notas em memória, com as regras de juntar o que chega da distribuição.</summary>
    public class Repositorio
    {
        public BaseNotas Base { get; private set; }
        readonly Dictionary<string, Nota> porChave = new Dictionary<string, Nota>();
        readonly HashSet<string> chavesCanceladas = new HashSet<string>();
        readonly HashSet<string> eventosVistos = new HashSet<string>();
        // Última natureza escolhida na tela para cada prestador: vira a sugestão das outras notas dele.
        readonly Dictionary<string, string> naturezaPorPrestador = new Dictionary<string, string>();

        public Repositorio(BaseNotas b)
        {
            Base = b;
            foreach (Nota n in b.Notas) porChave[n.Tipo + ":" + n.Chave] = n;
            foreach (Nota n in b.Notas.Where(x => !string.IsNullOrEmpty(x.Natureza)).OrderBy(x => x.Emissao ?? ""))
                naturezaPorPrestador[ChavePrestador(n)] = n.Natureza;
            foreach (EventoNota e in b.Eventos)
            {
                eventosVistos.Add(IdEvento(e));
                if (e.Cancela) chavesCanceladas.Add(e.Tipo + ":" + e.Chave);
            }
        }

        static string IdEvento(EventoNota e)
        {
            return e.Tipo + ":" + e.Chave + ":" + e.TpEvento + ":" + e.Nsu;
        }

        public Nota Buscar(string tipo, string chave)
        {
            Nota n;
            return porChave.TryGetValue(tipo + ":" + chave, out n) ? n : null;
        }

        public bool Cancelada(Nota n)
        {
            return n.Situacao == "cancelada" || chavesCanceladas.Contains(n.Tipo + ":" + n.Chave);
        }

        static string ChavePrestador(Nota n)
        {
            return n.Tipo + ":" + (n.EmitenteDoc ?? "");
        }

        /// <summary>Natureza de rendimento da nota: a escolhida nela, senão a do mesmo prestador, senão a sugerida pela nota.</summary>
        public string Natureza(Nota n)
        {
            if (!string.IsNullOrEmpty(n.Natureza)) return n.Natureza;
            string doPrestador;
            if (!string.IsNullOrEmpty(n.EmitenteDoc) && naturezaPorPrestador.TryGetValue(ChavePrestador(n), out doPrestador)) return doPrestador;
            return TabelaReinf.Sugerir(n);
        }

        public bool NaturezaEscolhida(Nota n)
        {
            return !string.IsNullOrEmpty(n.Natureza);
        }

        public void DefinirNatureza(Nota n, string codigo)
        {
            n.Natureza = codigo;
            if (!string.IsNullOrEmpty(n.EmitenteDoc)) naturezaPorPrestador[ChavePrestador(n)] = codigo;
        }

        /// <summary>
        /// Resumo nunca sobrescreve nota completa (só pode avisar que ela foi cancelada);
        /// nota completa substitui o resumo, guardando o que já se sabia da ciência.
        /// Devolve true quando a nota é nova.
        /// </summary>
        public bool GravarNota(Nota nova, string xml)
        {
            if (string.IsNullOrEmpty(nova.Chave)) return false;
            Nota atual = Buscar(nova.Tipo, nova.Chave);
            if (atual != null && atual.Completo && !nova.Completo)
            {
                if (nova.Situacao == "cancelada") atual.Situacao = "cancelada";
                return false;
            }

            if (nova.Completo && !string.IsNullOrEmpty(xml))
            {
                string mes = string.IsNullOrEmpty(nova.Emissao) ? null : nova.Emissao.Substring(0, 7);
                nova.ArquivoXml = Armazenamento.GravarXml(nova.Tipo, mes, nova.Chave + ".xml", xml);
            }
            nova.BaixadaEm = Formatos.AgoraIso();

            if (atual == null)
            {
                Base.Notas.Add(nova);
                porChave[nova.Tipo + ":" + nova.Chave] = nova;
                return true;
            }

            nova.CienciaEm = atual.CienciaEm;
            nova.CienciaOk = atual.CienciaOk;
            nova.CienciaRetorno = atual.CienciaRetorno;
            nova.Natureza = atual.Natureza;
            if (atual.Situacao == "cancelada") nova.Situacao = "cancelada";
            Base.Notas[Base.Notas.IndexOf(atual)] = nova;
            porChave[nova.Tipo + ":" + nova.Chave] = nova;
            return false;
        }

        public void GravarEvento(EventoNota e, string xml)
        {
            if (string.IsNullOrEmpty(e.Chave) || !eventosVistos.Add(IdEvento(e))) return;
            Base.Eventos.Add(e);
            if (e.Cancela) chavesCanceladas.Add(e.Tipo + ":" + e.Chave);
            if (!string.IsNullOrEmpty(xml))
                Armazenamento.GravarXml(e.Tipo, "eventos", e.Chave + "_" + e.TpEvento + "_" + e.Nsu + ".xml", xml);
        }

        /// <summary>NF-e que só têm o resumo e ainda não receberam a ciência (ou falharam por erro de comunicação).</summary>
        public List<Nota> NfeAguardandoCiencia()
        {
            return Base.Notas.Where(n => n.Tipo == "nfe" && !n.Completo && !n.CienciaOk && !Cancelada(n)
                                         && string.IsNullOrEmpty(n.CienciaRetorno)).ToList();
        }

        public int NfeSoResumo()
        {
            return Base.Notas.Count(n => n.Tipo == "nfe" && !n.Completo && !Cancelada(n));
        }
    }
}
