using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NotasRetencaoINSS
{
    public class ResultadoBusca
    {
        public int NfseNovas;
        public int NfeNovas;
        public int Documentos;
        public int Ciencias;
        public int CienciasRecusadas;
        public int Ilegiveis;
        public bool Parado;
        public List<string> Avisos = new List<string>();

        public string Resumo()
        {
            var partes = new List<string>();
            partes.Add(NfseNovas + " NFS-e nova(s)");
            if (NfeNovas > 0 || Ciencias > 0) partes.Add(NfeNovas + " NF-e nova(s)");
            if (Ciencias > 0) partes.Add(Ciencias + " ciência(s) registrada(s) — o XML completo dessas NF-e chega na próxima busca, depois de 1 hora");
            if (CienciasRecusadas > 0) partes.Add(CienciasRecusadas + " ciência(s) recusada(s) pela SEFAZ");
            if (Ilegiveis > 0) partes.Add(Ilegiveis + " documento(s) ilegível(is) ignorado(s)");
            if (Parado) partes.Add("busca interrompida");
            return string.Join(" · ", partes);
        }
    }

    /// <summary>
    /// Baixa, lote a lote, tudo o que o ADN (NFS-e) e a SEFAZ (NF-e) têm para o CNPJ
    /// depois do último NSU guardado. Regra da SEFAZ: quando a fila acaba (cStat 137
    /// ou ultNSU = maxNSU), nova consulta só depois de 1 hora — antes disso dá 656 e
    /// o CNPJ fica bloqueado.
    /// </summary>
    public class Sincronizador
    {
        const int LimiteLotes = 400;
        const int SalvarACadaLotes = 10;

        public int EsperaLoteNfseMs = 300;
        public int EsperaLoteNfeMs = 1500;
        public int EsperaCienciaMs = 300;

        volatile bool parar;
        public void Parar() { parar = true; }

        readonly Configuracao cfg;
        readonly Repositorio repo;
        readonly CertificadoInfo cert;
        readonly Action<string> progresso;

        public Sincronizador(Configuracao cfg, Repositorio repo, CertificadoInfo cert, Action<string> progresso)
        {
            this.cfg = cfg;
            this.repo = repo;
            this.cert = cert;
            this.progresso = progresso ?? (s => { });
        }

        void Salvar()
        {
            Armazenamento.SalvarNotas(repo.Base);
            Armazenamento.SalvarConfiguracao(cfg);
        }

        static long Nsu(string s)
        {
            long n;
            return long.TryParse(s, out n) ? n : 0;
        }

        public ResultadoBusca Executar()
        {
            var res = new ResultadoBusca();
            parar = false;
            try
            {
                BuscarNfse(res);
                if (cfg.IncluirNfe && !parar)
                {
                    BuscarNfe(res);
                    if (!parar) DarCiencia(res);
                }
            }
            finally
            {
                res.Parado = parar;
                cfg.UltimaBuscaEm = Formatos.AgoraIso();
                cfg.UltimaBuscaResumo = res.Resumo();
                Salvar();
            }
            return res;
        }

        void BuscarNfse(ResultadoBusca res)
        {
            long ult = Nsu(cfg.UltNsuNfse);
            int semSalvar = 0;
            try
            {
                for (int lote = 1; lote <= LimiteLotes && !parar; lote++)
                {
                    progresso("NFS-e: consultando o ADN (lote " + lote + ", " + res.NfseNovas + " nova(s) até agora)…");
                    RetornoAdn r = Adn.Distribuir(cert.Certificado, cfg, ult);
                    foreach (DocDistribuido d in r.Docs)
                    {
                        res.Documentos++;
                        Lido l = null;
                        try { l = LeitorXml.LerNfse(d.Xml, d.Schema, d.ChaveAcesso, d.Nsu); }
                        catch (Exception) { res.Ilegiveis++; }
                        if (l == null) continue;
                        if (l.Nota != null && repo.GravarNota(l.Nota, d.Xml)) res.NfseNovas++;
                        if (l.Evento != null) repo.GravarEvento(l.Evento, d.Xml);
                    }
                    ult = Math.Max(ult, r.UltNsu);
                    cfg.UltNsuNfse = ult.ToString();
                    if (++semSalvar >= SalvarACadaLotes) { Salvar(); semSalvar = 0; }
                    if (r.Acabou) break;
                    Thread.Sleep(EsperaLoteNfseMs);
                }
            }
            catch (ErroAviso ex)
            {
                res.Avisos.Add("NFS-e: " + ex.Message);
            }
            Salvar();
        }

        void BuscarNfe(ResultadoBusca res)
        {
            DateTime? liberada = Formatos.LerIso(cfg.NfeLiberadaEm);
            if (liberada.HasValue && liberada.Value > DateTime.Now)
            {
                res.Avisos.Add("NF-e: " + (cfg.NfeMotivoEspera ?? "a SEFAZ pede 1 hora entre consultas") + ". Nova consulta liberada às " + liberada.Value.ToString("HH:mm") + ".");
                return;
            }

            long ult = Nsu(cfg.UltNsuNfe);
            int semSalvar = 0;
            try
            {
                for (int lote = 1; lote <= LimiteLotes && !parar; lote++)
                {
                    progresso("NF-e: consultando a SEFAZ (lote " + lote + ", " + res.NfeNovas + " nova(s) até agora)…");
                    RetornoDistribuicao r = Sefaz.DistribuirPorNsu(cert.Certificado, cfg, ult);
                    if (r.CStat == "656")
                    {
                        Esperar("a SEFAZ bloqueou consultas por excesso (656)");
                        res.Avisos.Add("NF-e: a SEFAZ bloqueou novas consultas por excesso (656). Liberado às " + DateTime.Now.AddHours(1).ToString("HH:mm") + ".");
                        break;
                    }
                    if (r.CStat != "137" && r.CStat != "138")
                        throw new ErroAviso("a SEFAZ recusou a consulta (" + r.CStat + "): " + r.XMotivo);

                    foreach (DocDistribuido d in r.Docs)
                    {
                        res.Documentos++;
                        Lido l = null;
                        try { l = LeitorXml.LerNfe(d.Xml, d.Schema, d.Nsu); }
                        catch (Exception) { res.Ilegiveis++; }
                        if (l == null) continue;
                        if (l.Nota != null && repo.GravarNota(l.Nota, d.Xml)) res.NfeNovas++;
                        if (l.Evento != null) repo.GravarEvento(l.Evento, d.Xml);
                    }
                    ult = Math.Max(ult, r.UltNsu);
                    cfg.UltNsuNfe = ult.ToString();
                    if (++semSalvar >= SalvarACadaLotes) { Salvar(); semSalvar = 0; }

                    if (r.CStat == "137" || r.Docs.Count == 0 || ult >= r.MaxNsu)
                    {
                        Esperar("sem NF-e novas na última consulta (a SEFAZ pede 1 hora de intervalo)");
                        break;
                    }
                    Thread.Sleep(EsperaLoteNfeMs);
                }
            }
            catch (ErroAviso ex)
            {
                res.Avisos.Add("NF-e: " + ex.Message);
            }
            Salvar();
        }

        void Esperar(string motivo)
        {
            cfg.NfeLiberadaEm = DateTime.Now.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss");
            cfg.NfeMotivoEspera = motivo;
        }

        /// <summary>A SEFAZ só libera o XML completo (onde está a retenção) depois da Ciência da Operação.</summary>
        void DarCiencia(ResultadoBusca res)
        {
            List<Nota> pendentes = repo.NfeAguardandoCiencia();
            try
            {
                for (int i = 0; i < pendentes.Count && !parar; i += Sefaz.EventosPorLote)
                {
                    List<Nota> lote = pendentes.Skip(i).Take(Sefaz.EventosPorLote).ToList();
                    progresso("NF-e: registrando a ciência para liberar o XML completo (" + Math.Min(i + lote.Count, pendentes.Count) + " de " + pendentes.Count + ")…");
                    List<RetornoCiencia> rets = Sefaz.EnviarCiencia(cert.Certificado, cfg, lote.Select(n => n.Chave).ToList());
                    foreach (RetornoCiencia r in rets)
                    {
                        Nota n = repo.Buscar("nfe", r.Chave);
                        if (n == null) continue;
                        n.CienciaEm = Formatos.AgoraIso();
                        n.CienciaOk = r.Ok;
                        n.CienciaRetorno = r.CStat + " - " + r.XMotivo;
                        if (r.Ok) res.Ciencias++; else res.CienciasRecusadas++;
                    }
                    Salvar();
                    Thread.Sleep(EsperaCienciaMs);
                }
            }
            catch (ErroAviso ex)
            {
                // Erro de comunicação: as notas ficam sem retorno e são tentadas de novo na próxima busca.
                res.Avisos.Add("NF-e (ciência): " + ex.Message);
            }
        }
    }
}
