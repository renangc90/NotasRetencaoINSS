using System;
using System.Collections.Generic;

namespace NotasRetencaoINSS
{
    /// <summary>Uma nota recebida (NFS-e ou NF-e). Datas em texto "aaaa-mm-dd" para o JSON ficar legível.</summary>
    public class Nota
    {
        public string Tipo { get; set; }            // "nfse" | "nfe"
        public string Chave { get; set; }
        public string Nsu { get; set; }
        public bool Completo { get; set; }          // NF-e: false enquanto só veio o resumo
        public string Numero { get; set; }
        public string Serie { get; set; }
        public string Emissao { get; set; }         // aaaa-mm-dd
        public string Competencia { get; set; }     // aaaa-mm-dd (NFS-e)
        public string EmitenteDoc { get; set; }     // prestador (NFS-e) / emitente (NF-e)
        public string EmitenteNome { get; set; }
        public string TomadorDoc { get; set; }      // tomador (NFS-e) / destinatário (NF-e)
        public string TomadorNome { get; set; }
        public decimal Valor { get; set; }          // vServ (NFS-e) / vNF (NF-e)
        public decimal? BaseInss { get; set; }      // vBCRetPrev (NF-e)
        public decimal InssRetido { get; set; }     // vRetCP (NFS-e) / vRetPrev (NF-e)
        public string CodigoServico { get; set; }
        public string Descricao { get; set; }
        public string Municipio { get; set; }
        public string Situacao { get; set; }        // autorizada | denegada | cancelada (da própria nota)
        public string ArquivoXml { get; set; }      // relativo à pasta de dados
        public string BaixadaEm { get; set; }
        public string CienciaEm { get; set; }
        public bool CienciaOk { get; set; }
        public string CienciaRetorno { get; set; }
    }

    /// <summary>Evento distribuído (cancelamento, ciência...). Guardado à parte: pode chegar antes da nota.</summary>
    public class EventoNota
    {
        public string Tipo { get; set; }
        public string Chave { get; set; }
        public string TpEvento { get; set; }
        public string Descricao { get; set; }
        public string DhEvento { get; set; }
        public string Nsu { get; set; }
        public bool Cancela { get; set; }
    }

    public class BaseNotas
    {
        public int Versao { get; set; }
        public List<Nota> Notas { get; set; }
        public List<EventoNota> Eventos { get; set; }

        public BaseNotas()
        {
            Versao = 1;
            Notas = new List<Nota>();
            Eventos = new List<EventoNota>();
        }
    }

    public class Configuracao
    {
        public string OrigemCertificado { get; set; }   // "windows" | "pfx"
        public string CertificadoThumbprint { get; set; }
        public string PfxCaminho { get; set; }
        public string PfxSenhaProtegida { get; set; }    // DPAPI do usuário do Windows, base64
        public string Cnpj { get; set; }
        public string NomeEntidade { get; set; }
        public string Uf { get; set; }
        public bool IncluirNfe { get; set; }
        public int Ambiente { get; set; }                // 1 produção, 2 homologação (só pelo arquivo)

        public string UltNsuNfse { get; set; }
        public string UltNsuNfe { get; set; }
        public string NfeLiberadaEm { get; set; }        // ISO: antes disso a SEFAZ dá 656
        public string NfeMotivoEspera { get; set; }
        public string UltimaBuscaEm { get; set; }
        public string UltimaBuscaResumo { get; set; }
        public string PastaArquivos { get; set; }        // última pasta usada em "Salvar XML e PDF"

        // Só para os testes (servidor simulado). Vazio = endereços oficiais.
        public string UrlAdn { get; set; }
        public string UrlDanfse { get; set; }
        public string UrlSefazDistribuicao { get; set; }
        public string UrlSefazEvento { get; set; }

        public Configuracao()
        {
            OrigemCertificado = "windows";
            Ambiente = 1;
            UltNsuNfse = "0";
            UltNsuNfe = "0";
        }

        /// <summary>Primeiro uso: já vem com a Prefeitura de Uberaba. O CNPJ do certificado escolhido prevalece.</summary>
        public static Configuracao Padrao()
        {
            return new Configuracao { Cnpj = "18428839000190", NomeEntidade = "MUNICIPIO DE UBERABA", Uf = "MG" };
        }
    }

    /// <summary>Erro com mensagem pronta para mostrar ao usuário.</summary>
    public class ErroAviso : Exception
    {
        public ErroAviso(string mensagem) : base(mensagem) { }
    }
}
