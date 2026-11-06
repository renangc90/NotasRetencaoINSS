using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace NotasRetencaoINSS
{
    static class Testes
    {
        static int ok, falhas;

        static void Checar(bool cond, string nome)
        {
            if (cond) ok++; else { falhas++; Console.WriteLine("FALHOU: " + nome); }
        }

        static void Igual(object esperado, object obtido, string nome)
        {
            Checar(Equals(esperado, obtido), nome + " (esperado " + esperado + ", veio " + obtido + ")");
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "capturas") return Capturas.Executar(args[1]);
            return Executar();
        }

        public static int Executar()
        {
            string pasta = Path.Combine(Path.GetTempPath(), "NotasRetencaoINSS-testes-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Armazenamento.Pasta = pasta;
            WebRequest.DefaultWebProxy = null;
            System.Threading.Thread.CurrentThread.CurrentCulture = Formatos.PtBr;

            Rodar("Configuração de fábrica", () =>
            {
                Configuracao c = Armazenamento.LerConfiguracao();
                Igual("18428839000190", c.Cnpj, "CNPJ de Uberaba");
                Igual("MG", c.Uf, "UF");
                Checar(string.IsNullOrEmpty(c.CertificadoThumbprint), "sem certificado escolhido (abre Configurações)");
            });
            Rodar("Leitura NFS-e", LeituraNfse);
            Rodar("Leitura NF-e", LeituraNfe);
            Rodar("Retorno ADN", RetornoAdn);
            Rodar("Certificado e assinatura", CertificadoEAssinatura);
            Rodar("Repositório", Repositorio);
            Rodar("Busca completa (servidor simulado)", BuscaCompleta);
            Rodar("Bloqueio 656", Bloqueio656);
            Rodar("Planilha", PlanilhaXlsx);
            Rodar("PDF da NFS-e (gerado do XML)", PdfDaNfse);
            Rodar("Salvar XML e PDF", SalvarArquivos);
            Rodar("Leitura de IR e CSLL/PIS/COFINS", LeituraIr);
            Rodar("Anexo I da EFD-Reinf", AnexoReinf);
            Rodar("Natureza de rendimento", NaturezaNoRepositorio);
            Rodar("Atualização do notas.json antigo", AtualizacaoVersao1);

            Console.WriteLine();
            Console.WriteLine(ok + " verificações ok, " + falhas + " falha(s). Dados de teste em " + pasta);
            return falhas == 0 ? 0 : 1;
        }

        static void Rodar(string nome, Action teste)
        {
            Console.WriteLine("- " + nome);
            try { teste(); }
            catch (Exception ex) { falhas++; Console.WriteLine("FALHOU (exceção): " + nome + ": " + ex); }
        }

        static void LeituraNfse()
        {
            string chave = Amostras.ChaveNfse(1);
            Nota n = LeitorXml.LerNfse(Amostras.Nfse(chave, "123", Amostras.CnpjPrefeitura, 10000m, 1100m), "NFSE", chave, "7").Nota;
            Igual(1100m, n.InssRetido, "vRetCP");
            Igual(10000m, n.Valor, "vServ");
            Igual("123", n.Numero, "nNFSe");
            Igual("2026-10-15", n.Emissao, "emissão = dhProc");
            Igual("2026-09-30", n.Competencia, "dCompet");
            Igual(Amostras.CnpjPrestador, n.EmitenteDoc, "prestador");
            Igual("LIMPA TUDO SERVIÇOS LTDA", n.EmitenteNome, "nome do prestador vem do emit");
            Igual(Amostras.CnpjPrefeitura, n.TomadorDoc, "tomador (sem pegar CNPJ de dentro do endereço)");
            Igual("070201", n.CodigoServico, "cTribNac");
            Igual("Uberlândia", n.Municipio, "município");

            Nota sem = LeitorXml.LerNfse(Amostras.Nfse(chave, "124", Amostras.CnpjPrefeitura, 500m, null), "NFSE", "", "8").Nota;
            Igual(0m, sem.InssRetido, "sem tribFed = 0");
            Igual(chave, sem.Chave, "chave pelo Id quando o ADN não manda");

            EventoNota e = LeitorXml.LerNfse(Amostras.EventoCancelamentoNfse(chave), "EVENTO", chave, "9").Evento;
            Igual("101101", e.TpEvento, "tipo do evento");
            Checar(e.Cancela, "101101 cancela");
            Igual("Cancelamento de NFS-e", e.Descricao, "xDesc");
        }

        static void LeituraNfe()
        {
            string chave = Amostras.ChaveNfe("4567");
            Nota r = LeitorXml.LerNfe(Amostras.ResNfe(chave, 2500m), "resNFe", "1").Nota;
            Checar(!r.Completo, "resumo não é completo");
            Igual("4567", r.Numero, "número pela chave");
            Igual("1", r.Serie, "série pela chave");
            Igual(2500m, r.Valor, "vNF do resumo");

            Nota c = LeitorXml.LerNfe(Amostras.ProcNfe(chave, "4567", 2500m, 275m), "procNFe", "2").Nota;
            Checar(c.Completo, "procNFe completo");
            Igual(275m, c.InssRetido, "vRetPrev");
            Igual((decimal?)2500m, c.BaseInss, "vBCRetPrev");
            Igual(Amostras.CnpjPrefeitura, c.TomadorDoc, "destinatário");
            Igual("Manutenção de ar condicionado (e mais 1 item)", c.Descricao, "descrição dos itens");

            Nota sem = LeitorXml.LerNfe(Amostras.ProcNfe(chave, "4567", 2500m, null), "procNFe", "3").Nota;
            Igual(0m, sem.InssRetido, "sem retTrib = 0");
            Checar(!sem.BaseInss.HasValue, "sem base");

            EventoNota e = LeitorXml.LerNfe(Amostras.ResEventoCancelamentoNfe(chave), "resEvento", "4").Evento;
            Checar(e.Cancela && e.Chave == chave, "cancelamento da NF-e");

            RetornoDistribuicao d = Sefaz.LerRetornoDistribuicao(Amostras.SoapDist("138", "Documento localizado", 2, 9,
                Amostras.DocZip(1, "resNFe_v1.01.xsd", Amostras.ResNfe(chave, 1m)) + Amostras.DocZip(2, "procNFe_v4.00.xsd", Amostras.ProcNfe(chave, "4567", 1m, 1m))));
            Igual("138", d.CStat, "cStat");
            Igual(2L, d.UltNsu, "ultNSU");
            Igual(9L, d.MaxNsu, "maxNSU");
            Igual(2, d.Docs.Count, "docZips");
            Igual("procNFe", d.Docs[1].Schema, "schema sem versão");
            Igual("2", d.Docs[1].Nsu, "NSU sem zeros");
        }

        static string JsonLote(string status, IEnumerable<string> itens)
        {
            return "{\"StatusProcessamento\":\"" + status + "\",\"LoteDFe\":[" + string.Join(",", itens) + "],\"Alertas\":null,\"Erros\":null}";
        }

        static string ItemAdn(long nsu, string chave, string tipo, string xml)
        {
            return "{\"NSU\":" + nsu + ",\"ChaveAcesso\":\"" + chave + "\",\"TipoDocumento\":\"" + tipo + "\",\"ArquivoXml\":\"" + Amostras.Gzip64(xml) + "\",\"DataHoraGeracao\":\"2026-10-15T09:12:00\"}";
        }

        static void RetornoAdn()
        {
            string chave = Amostras.ChaveNfse(1);
            RetornoAdn r = Adn.LerRetorno(200, JsonLote("DOCUMENTOS_LOCALIZADOS", new[] { ItemAdn(11, chave, "NFSE", Amostras.Nfse(chave, "1", Amostras.CnpjPrefeitura, 1m, 1m)) }), 10);
            Igual(1, r.Docs.Count, "1 doc");
            Igual(11L, r.UltNsu, "ultNSU = maior NSU");
            Checar(r.Acabou, "lote menor que 50 = acabou");
            Checar(r.Docs[0].Xml.Contains("vRetCP"), "gunzip");

            RetornoAdn vazio = Adn.LerRetorno(404, "{\"StatusProcessamento\":\"NENHUM_DOCUMENTO_LOCALIZADO\",\"Erros\":[{\"Codigo\":\"E2220\",\"Descricao\":\"Nenhum documento\"}]}", 10);
            Checar(vazio.Acabou && vazio.UltNsu == 10, "nenhum documento não é erro");
            Checar(Adn.LerRetorno(404, "", 10).Acabou, "404 sem corpo");

            string msg = "";
            try { Adn.LerRetorno(400, "{\"StatusProcessamento\":\"REJEICAO\",\"Erros\":[{\"Codigo\":\"E1\",\"Descricao\":\"CNPJ inválido\"}]}", 0); }
            catch (ErroAviso ex) { msg = ex.Message; }
            Checar(msg.Contains("CNPJ inválido"), "rejeição vira aviso com o motivo");
        }

        // ---------------------------------------------------------------- certificado

        static X509Certificate2 certTeste;

        /// <summary>e-CNPJ de teste: CN sem o CNPJ, CNPJ só no SAN (OID 2.16.76.1.3.3), recarregado de um .pfx.</summary>
        static X509Certificate2 CertificadoTeste()
        {
            if (certTeste != null) return certTeste;
            using (RSA rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                var req = new CertificateRequest("CN=MUNICIPIO DE TESTE, O=ICP-Brasil", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                byte[] cnpj = Encoding.ASCII.GetBytes(Amostras.CnpjPrefeitura);
                var san = new List<byte> { 0x30, 0x1B, 0xA0, 0x19, 0x06, 0x05, 0x60, 0x4C, 0x01, 0x03, 0x03, 0xA0, 0x10, 0x04, 0x0E };
                san.AddRange(cnpj);
                req.CertificateExtensions.Add(new X509Extension(new Oid("2.5.29.17"), san.ToArray(), false));
                using (X509Certificate2 c = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1)))
                {
                    string pfx = Path.Combine(Armazenamento.Pasta, "teste.pfx");
                    File.WriteAllBytes(pfx, c.Export(X509ContentType.Pfx, "senha123"));
                    certTeste = Certificados.AbrirPfx(pfx, "senha123").Certificado;
                }
            }
            return certTeste;
        }

        static void CertificadoEAssinatura()
        {
            X509Certificate2 c = CertificadoTeste();
            Igual(Amostras.CnpjPrefeitura, Certificados.CnpjDoCertificado(c), "CNPJ pelo SAN");
            Igual("MUNICIPIO DE TESTE", Certificados.NomeDoCertificado(c), "nome");

            string erro = "";
            try { Certificados.AbrirPfx(Path.Combine(Armazenamento.Pasta, "teste.pfx"), "errada"); }
            catch (ErroAviso ex) { erro = ex.Message; }
            Checar(erro.Length > 0, "senha errada vira aviso: " + erro);

            string protegida = Armazenamento.Proteger("senha123");
            Checar(protegida != "senha123" && Armazenamento.Desproteger(protegida) == "senha123", "DPAPI ida e volta");

            var cfg = new Configuracao { Cnpj = Amostras.CnpjPrefeitura };
            string chave = Amostras.ChaveNfe("1");
            XmlElement evento = Sefaz.MontarCiencia(c, cfg, chave, "2026-11-04T10:00:00-03:00");
            Checar(VerificarAssinaturas("<envEvento xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"1.00\"><idLote>1</idLote>" + evento.OuterXml + "</envEvento>", c) == 1,
                "assinatura confere dentro do envEvento");
            string adulterado = evento.OuterXml.Replace("<tpAmb>1</tpAmb>", "<tpAmb>2</tpAmb>");
            Checar(VerificarAssinaturas("<envEvento xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"1.00\"><idLote>1</idLote>" + adulterado + "</envEvento>", c) == 0,
                "assinatura não confere com o XML adulterado");
            string x = evento.OuterXml;
            Checar(x.Contains("xmldsig#rsa-sha1") && x.Contains("xmldsig#sha1") && x.Contains("REC-xml-c14n-20010315") && x.Contains("enveloped-signature"), "algoritmos do leiaute (RSA-SHA1/C14N)");
            Checar(x.Contains("URI=\"#ID210210" + chave + "01\""), "Reference para o Id do infEvento");
            Checar(x.Contains("<cOrgao>91</cOrgao>") && x.Contains("<descEvento>Ciencia da Operacao</descEvento>"), "campos do evento");
        }

        /// <summary>Verifica cada Signature como a SEFAZ faria: o envEvento como documento próprio.</summary>
        static int VerificarAssinaturas(string envEvento, X509Certificate2 cert)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(envEvento);
            int validas = 0;
            foreach (XmlElement sig in doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl))
            {
                var sx = new SignedXml((XmlElement)sig.ParentNode);
                sx.LoadXml(sig);
                if (sx.CheckSignature(cert, true)) validas++;
            }
            return validas;
        }

        // ---------------------------------------------------------------- repositório

        static void Repositorio()
        {
            var repo = new Repositorio(new BaseNotas());
            string chave = Amostras.ChaveNfe("77");
            Checar(repo.GravarNota(LeitorXml.LerNfe(Amostras.ResNfe(chave, 10m), "resNFe", "1").Nota, Amostras.ResNfe(chave, 10m)), "resumo entra como novo");
            Igual(1, repo.NfeAguardandoCiencia().Count, "aguardando ciência");
            repo.Buscar("nfe", chave).CienciaOk = true;
            Igual(0, repo.NfeAguardandoCiencia().Count, "com ciência sai da fila");

            string proc = Amostras.ProcNfe(chave, "77", 10m, 1.1m);
            Checar(!repo.GravarNota(LeitorXml.LerNfe(proc, "procNFe", "5").Nota, proc), "completa substitui (não conta como nova)");
            Nota n = repo.Buscar("nfe", chave);
            Checar(n.Completo && n.CienciaOk && n.InssRetido == 1.1m, "completa manteve a ciência");
            Checar(File.Exists(Armazenamento.CaminhoCompleto(n.ArquivoXml)) && n.ArquivoXml.Contains("2026-10"), "XML gravado na pasta do mês");

            repo.GravarNota(LeitorXml.LerNfe(Amostras.ResNfe(chave, 10m), "resNFe", "6").Nota, null);
            Checar(repo.Buscar("nfe", chave).Completo, "resumo atrasado não estraga a completa");

            string chaveNfse = Amostras.ChaveNfse(5);
            repo.GravarEvento(LeitorXml.LerNfse(Amostras.EventoCancelamentoNfse(chaveNfse), "EVENTO", chaveNfse, "10").Evento, Amostras.EventoCancelamentoNfse(chaveNfse));
            repo.GravarNota(LeitorXml.LerNfse(Amostras.Nfse(chaveNfse, "5", Amostras.CnpjPrefeitura, 1m, 1m), "NFSE", chaveNfse, "11").Nota, Amostras.Nfse(chaveNfse, "5", Amostras.CnpjPrefeitura, 1m, 1m));
            Checar(repo.Cancelada(repo.Buscar("nfse", chaveNfse)), "cancelamento que chegou antes da nota vale");

            Armazenamento.SalvarNotas(repo.Base);
            var relido = new Repositorio(Armazenamento.LerNotas());
            Checar(relido.Cancelada(relido.Buscar("nfse", chaveNfse)) && relido.Buscar("nfe", chave).InssRetido == 1.1m, "salvar e reler");
            File.Delete(Path.Combine(Armazenamento.Pasta, "notas.json"));
        }

        // ---------------------------------------------------------------- busca completa

        static void BuscaCompleta()
        {
            string cancelada = Amostras.ChaveNfse(3);
            string chA = Amostras.ChaveNfe("1001"), chB = Amostras.ChaveNfe("1002"), chC = Amostras.ChaveNfe("1003"), chD = Amostras.ChaveNfe("1004");
            int consultasNfe = 0;
            bool liberarCompleto = false;

            ServidorFalso.Rota rota = (ServidorFalso.Pedido p, out int status, out string corpo) =>
            {
                status = 200;
                corpo = "";
                if (p.Caminho.StartsWith("/adn/DFe/1?"))
                {
                    // Lote cheio (50): 47 notas p/ a prefeitura, 1 em que ela é prestadora, 1 sem INSS, 1 cancelamento.
                    var itens = new List<string>();
                    for (int i = 1; i <= 47; i++)
                        itens.Add(ItemAdn(i, Amostras.ChaveNfse(i), "NFSE", Amostras.Nfse(Amostras.ChaveNfse(i), i.ToString(), Amostras.CnpjPrefeitura, 1000m, 110m)));
                    itens.Add(ItemAdn(48, Amostras.ChaveNfse(48), "NFSE", Amostras.Nfse(Amostras.ChaveNfse(48), "48", "99888777000166", 1000m, 110m)));
                    itens.Add(ItemAdn(49, Amostras.ChaveNfse(49), "NFSE", Amostras.Nfse(Amostras.ChaveNfse(49), "49", Amostras.CnpjPrefeitura, 800m, null)));
                    itens.Add(ItemAdn(50, cancelada, "EVENTO", Amostras.EventoCancelamentoNfse(cancelada)));
                    corpo = JsonLote("DOCUMENTOS_LOCALIZADOS", itens);
                }
                else if (p.Caminho.StartsWith("/adn/DFe/51?"))
                    corpo = JsonLote("DOCUMENTOS_LOCALIZADOS", new[] { ItemAdn(51, Amostras.ChaveNfse(51), "NFSE", Amostras.Nfse(Amostras.ChaveNfse(51), "51", Amostras.CnpjPrefeitura, 2000m, 220m, "2026-11-02T08:00:00-03:00")) });
                else if (p.Caminho.StartsWith("/adn/DFe/"))
                {
                    status = 404;
                    corpo = "{\"StatusProcessamento\":\"NENHUM_DOCUMENTO_LOCALIZADO\"}";
                }
                else if (p.Caminho == "/dist")
                {
                    consultasNfe++;
                    if (liberarCompleto)
                        corpo = Amostras.SoapDist("138", "Documento localizado", 6, 6, Amostras.DocZip(6, "procNFe_v4.00.xsd", Amostras.ProcNfe(chA, "1001", 5000m, 550m)));
                    else if (p.Corpo.Contains("<ultNSU>000000000000000</ultNSU>"))
                        corpo = Amostras.SoapDist("138", "Documento localizado", 3, 5,
                            Amostras.DocZip(1, "resNFe_v1.01.xsd", Amostras.ResNfe(chA, 5000m)) + Amostras.DocZip(2, "resNFe_v1.01.xsd", Amostras.ResNfe(chB, 300m))
                            + Amostras.DocZip(3, "procNFe_v4.00.xsd", Amostras.ProcNfe(chC, "1003", 1200m, 132m)));
                    else
                        corpo = Amostras.SoapDist("138", "Documento localizado", 5, 5,
                            Amostras.DocZip(4, "resEvento_v1.01.xsd", Amostras.ResEventoCancelamentoNfe(chB)) + Amostras.DocZip(5, "procNFe_v4.00.xsd", Amostras.ProcNfe(chD, "1004", 700m, null)));
                }
                else if (p.Caminho == "/evento")
                {
                    var rets = new StringBuilder();
                    foreach (string ch in new[] { chA, chB, chC, chD })
                        if (p.Corpo.Contains("<chNFe>" + ch + "</chNFe>"))
                            rets.Append("<retEvento versao=\"1.00\"><infEvento><tpAmb>1</tpAmb><cOrgao>91</cOrgao><cStat>135</cStat><xMotivo>Evento registrado e vinculado a NF-e</xMotivo><chNFe>" + ch + "</chNFe></infEvento></retEvento>");
                    corpo = "<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"><soap:Body><nfeRecepcaoEventoNFResult xmlns=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4\"><retEnvEvento xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"1.00\"><idLote>1</idLote><tpAmb>1</tpAmb><cOrgao>91</cOrgao><cStat>128</cStat><xMotivo>Lote de evento processado</xMotivo>"
                        + rets + "</retEnvEvento></nfeRecepcaoEventoNFResult></soap:Body></soap:Envelope>";
                }
                else status = 500;
            };

            using (var srv = new ServidorFalso(rota))
            {
                var cfg = new Configuracao
                {
                    Cnpj = Amostras.CnpjPrefeitura, Uf = "MG", IncluirNfe = true, NomeEntidade = "MUNICIPIO DE TESTE",
                    UrlAdn = srv.Url + "/adn", UrlSefazDistribuicao = srv.Url + "/dist", UrlSefazEvento = srv.Url + "/evento",
                };
                var repo = new Repositorio(new BaseNotas());
                var cert = new CertificadoInfo { Certificado = CertificadoTeste(), Cnpj = Amostras.CnpjPrefeitura, Nome = "MUNICIPIO DE TESTE" };
                var mensagens = new List<string>();
                Sincronizador s = NovoSinc(cfg, repo, cert, mensagens);

                ResultadoBusca r = s.Executar();
                Igual(0, r.Avisos.Count, "sem avisos na 1ª busca: " + string.Join(" | ", r.Avisos));
                Igual(50, r.NfseNovas, "NFS-e novas (2 lotes)");
                Igual("51", cfg.UltNsuNfse, "NSU do ADN guardado");
                Igual(4, r.NfeNovas, "NF-e novas");
                Igual("5", cfg.UltNsuNfe, "NSU da SEFAZ guardado");
                Igual(2, consultasNfe, "2 consultas à SEFAZ (parou no maxNSU)");
                Checar(Formatos.LerIso(cfg.NfeLiberadaEm) > DateTime.Now.AddMinutes(59), "regra de 1 hora registrada");
                Igual(1, r.Ciencias, "ciência só da NF-e só com resumo e não cancelada");
                Checar(repo.Cancelada(repo.Buscar("nfse", cancelada)), "NFS-e 3 cancelada pelo evento");
                Checar(repo.Cancelada(repo.Buscar("nfe", chB)), "NF-e B cancelada");
                Checar(repo.Buscar("nfe", chA).CienciaOk, "ciência da A registrada");
                Igual(132m, repo.Buscar("nfe", chC).InssRetido, "INSS da NF-e C");

                ServidorFalso.Pedido evento = srv.Pedidos.Last(p => p.Caminho == "/evento");
                string env = LeitorXml.No(LeitorXml.Carregar(evento.Corpo), "envEvento").OuterXml;
                Igual(1, VerificarAssinaturas(env, cert.Certificado), "assinatura do evento enviado confere");
                Checar(evento.Corpo.Contains("<cUFAutor>") == false && srv.Pedidos.Any(p => p.Caminho == "/dist" && p.Corpo.Contains("<cUFAutor>31</cUFAutor>")), "cUFAutor de MG");

                Checar(File.Exists(Path.Combine(Armazenamento.Pasta, "notas.json")) && File.Exists(Path.Combine(Armazenamento.Pasta, "config.json")), "gravou os arquivos");
                var relida = new Repositorio(Armazenamento.LerNotas());
                Igual(repo.Base.Notas.Count, relida.Base.Notas.Count, "notas relidas do disco");
                Igual("51", Armazenamento.LerConfiguracao().UltNsuNfse, "config relida do disco");

                // 2ª busca logo depois: ADN vazio, SEFAZ não é consultada (1 hora), ciência não repete.
                int antes = consultasNfe, eventosAntes = srv.Pedidos.Count(p => p.Caminho == "/evento");
                r = NovoSinc(cfg, repo, cert, mensagens).Executar();
                Igual(0, r.NfseNovas, "nada novo no ADN");
                Igual(antes, consultasNfe, "SEFAZ respeitou 1 hora");
                Checar(r.Avisos.Any(a => a.Contains("liberada às")), "avisou o horário");
                Igual(eventosAntes, srv.Pedidos.Count(p => p.Caminho == "/evento"), "não repetiu ciência");

                // Depois da hora: chega o XML completo da A, com a retenção.
                cfg.NfeLiberadaEm = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss");
                liberarCompleto = true;
                r = NovoSinc(cfg, repo, cert, mensagens).Executar();
                Nota notaA = repo.Buscar("nfe", chA);
                Checar(notaA.Completo && notaA.InssRetido == 550m && notaA.CienciaOk, "XML completo da A chegou com INSS");
                Igual(0, repo.NfeSoResumo(), "nenhuma NF-e só com resumo");
                Checar(mensagens.Any(m => m.StartsWith("NFS-e")) && mensagens.Any(m => m.StartsWith("NF-e")), "progresso informado");

                // Filtro da tela: só recebidas, com INSS, sem canceladas, no período de outubro.
                var tela = repo.Base.Notas.Where(n => n.InssRetido > 0 && !repo.Cancelada(n) && n.Emissao.StartsWith("2026-10")
                                                       && (string.IsNullOrEmpty(n.TomadorDoc) || n.TomadorDoc.StartsWith(Amostras.CnpjPrefeitura.Substring(0, 8)))).ToList();
                Igual(46 + 2, tela.Count, "notas de outubro com INSS (46 NFS-e + 2 NF-e)");
            }
            File.Delete(Path.Combine(Armazenamento.Pasta, "notas.json"));
        }

        static Sincronizador NovoSinc(Configuracao cfg, Repositorio repo, CertificadoInfo cert, List<string> mensagens)
        {
            return new Sincronizador(cfg, repo, cert, m => { lock (mensagens) mensagens.Add(m); }) { EsperaLoteNfseMs = 0, EsperaLoteNfeMs = 0, EsperaCienciaMs = 0 };
        }

        static void Bloqueio656()
        {
            ServidorFalso.Rota rota = (ServidorFalso.Pedido p, out int status, out string corpo) =>
            {
                status = p.Caminho.StartsWith("/adn") ? 404 : 200;
                corpo = p.Caminho.StartsWith("/adn") ? "" : Amostras.SoapDist("656", "Rejeicao: Consumo Indevido", 0, 0, "");
            };
            using (var srv = new ServidorFalso(rota))
            {
                var cfg = new Configuracao { Cnpj = Amostras.CnpjPrefeitura, Uf = "MG", IncluirNfe = true, UrlAdn = srv.Url + "/adn", UrlSefazDistribuicao = srv.Url + "/dist", UrlSefazEvento = srv.Url + "/evento" };
                var cert = new CertificadoInfo { Certificado = CertificadoTeste() };
                ResultadoBusca r = NovoSinc(cfg, new Repositorio(new BaseNotas()), cert, new List<string>()).Executar();
                Checar(r.Avisos.Any(a => a.Contains("656")), "aviso do 656");
                Checar(Formatos.LerIso(cfg.NfeLiberadaEm) > DateTime.Now.AddMinutes(59), "bloqueio de 1 hora guardado");
                Igual("0", cfg.UltNsuNfe, "NSU não andou");
            }
            File.Delete(Path.Combine(Armazenamento.Pasta, "notas.json"));
        }

        // ---------------------------------------------------------------- PDF da NFS-e e "Salvar XML e PDF"

        static readonly Encoding WinAnsi = Encoding.GetEncoding(1252);

        /// <summary>Confere a estrutura do PDF: cabeçalho, fim, e cada entrada do xref apontando para "N 0 obj".</summary>
        static void ChecarPdf(byte[] pdf, string nome)
        {
            string s = WinAnsi.GetString(pdf);
            Checar(s.StartsWith("%PDF-1.4") && s.TrimEnd().EndsWith("%%EOF"), nome + ": cabeçalho e fim");
            int sx = s.LastIndexOf("startxref", StringComparison.Ordinal);
            long xref = long.Parse(s.Substring(sx + 9).Trim().Split('\n')[0].Trim());
            Checar(s.Substring((int)xref, 4) == "xref", nome + ": startxref aponta para o xref");
            string[] linhas = s.Substring((int)xref).Split('\n');
            int total = int.Parse(linhas[1].Split(' ')[1]);
            bool ok = true;
            for (int i = 1; i < total; i++)
            {
                long off = long.Parse(linhas[2 + i].Substring(0, 10));
                if (!s.Substring((int)off).StartsWith(i + " 0 obj")) ok = false;
            }
            Checar(ok, nome + ": todas as posições do xref conferem");
            foreach (Match m in Regex.Matches(s, @"<< /Length (\d+) >>\nstream\n"))
            {
                int ini = m.Index + m.Length, tam = int.Parse(m.Groups[1].Value);
                if (s.Substring(ini + tam, 10) != "\nendstream") ok = false;
            }
            Checar(ok, nome + ": /Length de cada conteúdo confere");
        }

        static int PaginasPdf(byte[] pdf)
        {
            return Regex.Matches(WinAnsi.GetString(pdf), @"/Type /Page ").Count;
        }

        static void PdfDaNfse()
        {
            string ch = Amostras.ChaveNfse(901);
            string xml = Amostras.NfseCompleta(ch, "431", "Vigilância armada 24h no Paço Municipal (contrato 45/2025) - outubro/2026");
            byte[] pdf = Danfse.GerarPdf(xml, false);
            ChecarPdf(pdf, "NFS-e completa");
            string s = WinAnsi.GetString(pdf);
            Igual(1, PaginasPdf(pdf), "uma página");
            Checar(s.Contains("(DANFSe)") && s.Contains("(NFS-e nº 431)"), "título e número");
            Checar(s.Contains("(VIGILÂNCIA SEGURA EIRELI)") && s.Contains("(12.345.678/0001-90)"), "prestador com acento (WinAnsi)");
            Checar(s.Contains("(MUNICIPIO DE TESTE)") && s.Contains("(11.222.333/0001-81)"), "tomador");
            Checar(s.Contains("(11.02.01 – Vigilância, segurança ou monitoramento de bens, pessoas e semoventes.)"), "código e descrição da tributação nacional");
            Checar(s.Contains("\\(contrato 45/2025\\)"), "parênteses escapados no texto do PDF");
            Checar(s.Contains("(R$ 12.000,00)") && s.Contains("(R$ 60,00)") && s.Contains("(R$ 1.320,00)") && s.Contains("(R$ 10.020,00)"), "valor, IR, INSS e líquido");
            Checar(s.Contains("(5,00%)") && s.Contains("(Retido pelo tomador)") && s.Contains("(Não retidos \\(2\\))"), "ISSQN e PIS/COFINS");
            Checar(s.Contains("(21/10/2026 14:32:10)") && s.Contains("(01/10/2026)"), "datas da NFS-e e competência");
            Checar(s.Contains("(" + Regex.Replace(ch, ".{4}(?!$)", "$0 ") + ")"), "chave de acesso agrupada");
            Checar(s.Contains("(Contrato 45/2025 - Empenho 2026/1234)"), "informações complementares");
            Checar(s.Contains(Danfse.ConsultaPublica) && s.Contains("NT 008/2026"), "rodapé com a consulta pública");
            Checar(!s.Contains("(CANCELADA)"), "autorizada sem marca d'água");

            byte[] canc = Danfse.GerarPdf(xml, true);
            Checar(WinAnsi.GetString(canc).Contains("(CANCELADA) Tj") && WinAnsi.GetString(canc).Contains("(CANCELADA)"), "cancelada com marca d'água e situação");

            string longa = string.Join(" ", Enumerable.Repeat("Serviço de vigilância patrimonial com postos diurnos e noturnos, incluindo supervisão e rondas motorizadas.", 90));
            byte[] varias = Danfse.GerarPdf(Amostras.NfseCompleta(ch, "432", longa), false);
            ChecarPdf(varias, "descrição longa");
            Checar(PaginasPdf(varias) >= 2, "descrição longa quebra em mais páginas (" + PaginasPdf(varias) + ")");
            Checar(WinAnsi.GetString(varias).Contains("nº 432 \\(continuação\\)"), "página seguinte com cabeçalho de continuação");

            string erro = "";
            try { Danfse.GerarPdf(Amostras.ProcNfe(Amostras.ChaveNfe("1"), "1", 10m, null), false); }
            catch (ErroAviso e) { erro = e.Message; }
            Checar(erro.Contains("não é de uma NFS-e"), "XML de NF-e não vira DANFSe");

            Checar(Fonte.Quebrar("palavra " + new string('x', 400), 200f, 8.5f, false).All(l => Fonte.Largura(l, 8.5f, false) <= 200f), "quebra de linha respeita a largura");
        }

        static void SalvarArquivos()
        {
            var repo = new Repositorio(new BaseNotas());
            var nfse = new List<Nota>();
            for (int i = 1; i <= 3; i++)
            {
                string ch = Amostras.ChaveNfse(300 + i);
                string xml = Amostras.Nfse(ch, (700 + i).ToString(), Amostras.CnpjPrefeitura, 100m * i, 11m * i);
                repo.GravarNota(LeitorXml.LerNfse(xml, "NFSE", ch, i.ToString()).Nota, xml);
                nfse.Add(repo.Buscar("nfse", ch));
            }
            nfse[0].EmitenteNome = "LIMPA/TUDO: SERVIÇOS* \"LTDA\".";
            // Mesmo dia, número e prestador da 3ª: o nome do arquivo repetiria.
            string chDup = Amostras.ChaveNfse(399);
            string xmlDup = Amostras.Nfse(chDup, "703", Amostras.CnpjPrefeitura, 1m, 1m);
            repo.GravarNota(LeitorXml.LerNfse(xmlDup, "NFSE", chDup, "9").Nota, xmlDup);
            Nota dup = repo.Buscar("nfse", chDup);
            dup.EmitenteNome = nfse[2].EmitenteNome;
            repo.GravarEvento(new EventoNota { Tipo = "nfse", Chave = nfse[1].Chave, TpEvento = "101101", Nsu = "50", Cancela = true }, null);

            string chNfe = Amostras.ChaveNfe("3001");
            string proc = Amostras.ProcNfe(chNfe, "3001", 500m, 55m);
            repo.GravarNota(LeitorXml.LerNfe(proc, "procNFe", "1").Nota, proc);
            string chRes = Amostras.ChaveNfe("3002");
            repo.GravarNota(LeitorXml.LerNfe(Amostras.ResNfe(chRes, 80m), "resNFe", "2").Nota, null);

            var todas = new List<Nota>(nfse) { dup, repo.Buscar("nfe", chNfe), repo.Buscar("nfe", chRes) };
            string destino = Path.Combine(Armazenamento.Pasta, "salvos 1");
            var msgs = new List<string>();
            ResultadoExportacao r = new Exportador(repo.Cancelada, m => msgs.Add(m)).Executar(todas, destino);
            Igual(5, r.Xml, "XML: 4 NFS-e + 1 NF-e completa");
            Igual(4, r.Pdf, "PDF de todas as NFS-e, gerado do XML (sem internet)");
            Igual(0, r.PdfFalhou, "nenhum PDF falhou");
            Igual(1, r.Nfe, "NF-e só com XML");
            Igual(1, r.SemXml, "NF-e só com resumo ficou de fora");
            Igual(todas.Count, msgs.Count, "progresso por nota");

            string nome1 = Exportador.NomeArquivo(nfse[0]);
            Igual("2026-10-15 NFS-e 701 LIMPA TUDO SERVIÇOS LTDA", nome1, "nome sem caracteres proibidos");
            Checar(File.Exists(Path.Combine(destino, nome1 + ".xml")) && File.Exists(Path.Combine(destino, nome1 + ".pdf")), "XML e PDF com o mesmo nome");
            Igual(File.ReadAllText(Armazenamento.CaminhoCompleto(nfse[0].ArquivoXml)), File.ReadAllText(Path.Combine(destino, nome1 + ".xml")), "XML copiado igual ao guardado");
            byte[] pdf1 = File.ReadAllBytes(Path.Combine(destino, nome1 + ".pdf"));
            ChecarPdf(pdf1, "PDF salvo");
            Checar(WinAnsi.GetString(pdf1).Contains("(NFS-e nº 701)"), "PDF é o da nota certa");
            Checar(WinAnsi.GetString(File.ReadAllBytes(Path.Combine(destino, Exportador.NomeArquivo(nfse[1]) + ".pdf"))).Contains("(CANCELADA) Tj"), "nota cancelada sai com marca d'água");
            string nome3 = Exportador.NomeArquivo(nfse[2]);
            Checar(File.Exists(Path.Combine(destino, nome3 + ".pdf")) && File.Exists(Path.Combine(destino, nome3 + " " + chDup + ".pdf")), "nome repetido ganha a chave");
            Igual(9, Directory.GetFiles(destino).Length, "arquivos na pasta (5 XML + 4 PDF)");

            // XML corrompido: o XML vai, o PDF não, e o resumo explica.
            File.WriteAllText(Armazenamento.CaminhoCompleto(dup.ArquivoXml), "<NFSe><infNFSe>quebrado");
            r = new Exportador(repo.Cancelada, null).Executar(new[] { nfse[0], dup }, Path.Combine(Armazenamento.Pasta, "salvos 2"));
            Checar(r.Xml == 2 && r.Pdf == 1 && r.PdfFalhou == 1 && r.Resumo().Contains("ficaram sem PDF"), "XML ilegível: sem PDF, com motivo");

            // Abrir na tela: gera em dados\pdf; NF-e não tem PDF.
            string aberto = Exportador.PdfParaAbrir(nfse[0], false);
            Checar(File.Exists(aberto) && aberto.Contains(Path.Combine("pdf", "nfse", "2026-10")), "PDF para abrir fica em dados\\pdf");
            ChecarPdf(File.ReadAllBytes(aberto), "PDF para abrir");
            string erro = "";
            try { Exportador.PdfParaAbrir(repo.Buscar("nfe", chNfe), false); }
            catch (ErroAviso e) { erro = e.Message; }
            Checar(erro.Contains("só monta o PDF de NFS-e"), "NF-e não tem PDF: " + erro);
            using (File.Open(aberto, FileMode.Open, FileAccess.Read, FileShare.Read))
                Checar(File.Exists(Exportador.PdfParaAbrir(nfse[0], false)), "PDF aberto no leitor: grava com outro nome");

            // Parar no meio: termina a nota atual e para.
            Exportador ex = null;
            ex = new Exportador(repo.Cancelada, m => ex.Parar());
            r = ex.Executar(nfse, Path.Combine(Armazenamento.Pasta, "salvos 3"));
            Checar(r.Parado && r.Xml == 1, "Parar interrompe depois da nota atual");
            Checar(r.Resumo().Contains("Interrompido"), "resumo avisa a interrupção");
            File.Delete(Path.Combine(Armazenamento.Pasta, "notas.json"));
        }

        // ---------------------------------------------------------------- planilha

        static void PlanilhaXlsx()
        {
            var repo = new Repositorio(new BaseNotas());
            for (int i = 1; i <= 3; i++)
            {
                string ch = Amostras.ChaveNfse(100 + i);
                repo.GravarNota(LeitorXml.LerNfse(Amostras.Nfse(ch, i.ToString(), Amostras.CnpjPrefeitura, 1000m * i, 110m * i), "NFSE", ch, i.ToString()).Nota, null);
            }
            string chC = Amostras.ChaveNfse(103);
            repo.GravarEvento(new EventoNota { Tipo = "nfse", Chave = chC, TpEvento = "101101", Nsu = "9", Cancela = true }, null);
            Nota esquisita = repo.Base.Notas[0];
            esquisita.EmitenteNome = "A & B <Serviços> \"Ltda\"\u0001";

            string arq = Path.Combine(Armazenamento.Pasta, "teste.xlsx");
            Planilha.Gerar(arq, "Notas com retenção de INSS — teste", repo.Base.Notas, repo.Cancelada, repo.Natureza, true);
            using (ZipArchive z = ZipFile.OpenRead(arq))
            {
                foreach (ZipArchiveEntry e in z.Entries)
                    using (var sr = new StreamReader(e.Open()))
                    {
                        var d = new XmlDocument();
                        d.LoadXml(sr.ReadToEnd());
                    }
                Checar(z.Entries.Count == 8, "8 partes no xlsx (3 abas)");
                string s1 = new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml").Open()).ReadToEnd();
                string s2 = new StreamReader(z.GetEntry("xl/worksheets/sheet2.xml").Open()).ReadToEnd();
                string s3 = new StreamReader(z.GetEntry("xl/worksheets/sheet3.xml").Open()).ReadToEnd();
                string wb = new StreamReader(z.GetEntry("xl/workbook.xml").Open()).ReadToEnd();
                Checar(s1.Contains("SUMIF(U4:U6,&quot;&lt;&gt;Cancelada&quot;,M4:M6)"), "total do INSS com SUMIF sem canceladas (Situação na coluna U)");
                Checar(s1.Contains("SUMIF(U4:U6,&quot;&lt;&gt;Cancelada&quot;,N4:N6)"), "total do IR com SUMIF");
                Checar(s1.Contains("<v>330</v>") || s1.Contains("<v>330.00</v>"), "INSS somado sem a cancelada (110+220)");
                Checar(s1.Contains("<v>30</v>") || s1.Contains("<v>30.00</v>"), "IR somado sem a cancelada (15+15)");
                Checar(s1.Contains("Cancelada"), "situação cancelada na linha");
                Checar(s1.Contains("A &amp; B &lt;Serviços&gt;"), "texto escapado");
                Checar(s1.Contains(">17099<") && s1.Contains(">Demais serviços<") && s1.Contains(">6256-05<"), "natureza, descrição e código de receita na aba Notas");
                Checar(s1.Contains("<autoFilter ref=\"A3:V6\"/>") && wb.Contains("Notas!$A$3:$V$6"), "filtro cobre todas as colunas");
                Checar(s2.Contains("12.345.678/0001-90") && (s2.Contains("<v>3000</v>") || s2.Contains("<v>3000.00</v>")), "aba por prestador somada");
                Checar(wb.Contains("name=\"EFD-Reinf\""), "aba EFD-Reinf");
                Checar(s3.Contains(">R-4020<") && s3.Contains(">17099<") && s3.Contains(">6256-05<") && (s3.Contains("<v>30</v>") || s3.Contains("<v>30.00</v>")),
                    "EFD-Reinf: prestador × natureza com IR somado e código");
                Checar(s3.Contains("data do pagamento não vem na nota"), "aviso da data de pagamento");
            }
        }

        // ---------------------------------------------------------------- IR, natureza de rendimento e Anexo I

        static void LeituraIr()
        {
            // NFS-e: IR, CSLL/PIS/COFINS pelo leiaute original (tpRetPisCofins 1 = PIS/COFINS retidos) e Simples.
            string ch = Amostras.ChaveNfse(601);
            string tribFed = "<piscofins><CST>01</CST><vBCPisCofins>1000.00</vBCPisCofins><pAliqPis>0.65</pAliqPis><pAliqCofins>3.00</pAliqCofins>"
                           + "<vPis>6.50</vPis><vCofins>30.00</vCofins><tpRetPisCofins>1</tpRetPisCofins></piscofins><vRetCP>110.00</vRetCP><vRetIRRF>48.00</vRetIRRF><vRetCSLL>10.00</vRetCSLL>";
            Nota n = LeitorXml.LerNfse(Amostras.Nfse(ch, "601", Amostras.CnpjPrefeitura, 1000m, null, tribFedXml: tribFed, cTribNac: "110201", opSimpNac: "3"), "NFSE", ch, "1").Nota;
            Igual(48m, n.IrRetido, "vRetIRRF");
            Igual(110m, n.InssRetido, "vRetCP continua");
            Igual(46.5m, n.CsrfRetida, "CSLL + PIS + COFINS retidos (tpRetPisCofins 1)");
            Igual("Sim", n.SimplesNacional, "opSimpNac 3 = Simples");
            Igual("110201", n.CodigoServico, "cTribNac");

            Func<string, decimal> csrf = x => LeitorXml.CsrfNfse(LeitorXml.Carregar("<tribFed>" + x + "</tribFed>").DocumentElement);
            Igual(10m, csrf("<piscofins><vPis>6.50</vPis><vCofins>30.00</vCofins><tpRetPisCofins>2</tpRetPisCofins></piscofins><vRetCSLL>10.00</vRetCSLL>"),
                "PIS/COFINS não retidos (2): só a CSLL");
            Igual(10m, csrf("<vRetCSLL>10.00</vRetCSLL>"), "sem grupo piscofins: só vRetCSLL");
            Igual(46.5m, csrf("<piscofins><vPis>6.50</vPis><vCofins>30.00</vCofins><tpRetPisCofins>3</tpRetPisCofins></piscofins><vRetCSLL>46.50</vRetCSLL>"),
                "NT 007 (3): vRetCSLL já é o total, sem somar PIS/COFINS de novo");
            Igual(0m, csrf("<piscofins><vPis>6.50</vPis><vCofins>30.00</vCofins><tpRetPisCofins>0</tpRetPisCofins></piscofins>"), "NT 007 (0): nada retido");
            Igual(0m, LeitorXml.CsrfNfse(null), "sem tribFed");

            Nota semIr = LeitorXml.LerNfse(Amostras.Nfse(ch, "602", Amostras.CnpjPrefeitura, 1000m, null), "NFSE", ch, "2").Nota;
            Checar(semIr.IrRetido == 0m && semIr.CsrfRetida == 0m && semIr.SimplesNacional == "Não", "nota sem tributos federais");

            // NF-e: retTrib.
            string chNfe = Amostras.ChaveNfe("6001");
            Nota nfe = LeitorXml.LerNfe(Amostras.ProcNfe(chNfe, "6001", 2000m, null, "<vRetCOFINS>60.00</vRetCOFINS><vRetCSLL>20.00</vRetCSLL><vBCIRRF>2000.00</vBCIRRF><vIRRF>24.00</vIRRF>", "4"), "procNFe", "1").Nota;
            Igual(24m, nfe.IrRetido, "NF-e vIRRF");
            Igual(80m, nfe.CsrfRetida, "NF-e vRetPIS + vRetCOFINS + vRetCSLL");
            Igual("MEI", nfe.SimplesNacional, "NF-e CRT 4 = MEI");
        }

        static void AnexoReinf()
        {
            Igual(51, TabelaReinf.Naturezas.Count, "50 naturezas do grupo 17 + 10002");
            Checar(TabelaReinf.Naturezas.Where(x => x.Codigo.StartsWith("17")).All(x => x.Evento == "R-4020"), "grupo 17 é do R-4020");
            Igual("R-4010", TabelaReinf.Buscar("10002").Evento, "10002 é do R-4010");
            Checar(TabelaReinf.Naturezas.All(x => x.Texto.Length <= 80 && x.Texto.StartsWith(x.Codigo + " – ")), "texto curto para a lista");

            // Códigos conferidos linha a linha no PDF (Manual da EFD-Reinf 2.1.2.1, Anexo I).
            Igual("6256-05", TabelaReinf.CodigoIr("17031", true), "Vigilância, IR, administração pública");
            Igual("6256-06", TabelaReinf.CodigoIr("17031", false), "Vigilância, IR, demais entidades");
            Igual("3280-06", TabelaReinf.CodigoIr("17025", true), "cooperativas: IR 3280-06");
            Igual("", TabelaReinf.CodigoIr("17050", true), "publicidade em conformidade: sem código de IR");
            Igual("0588-07", TabelaReinf.CodigoIr("10002", true), "pessoa física: IRPF 0588-07");
            Igual("", TabelaReinf.CodigoIr("99999", true), "natureza inexistente");
            Igual("619005", TabelaReinf.Buscar("17031").CodigoReceita(TabelaReinf.AGREGADO, true), "agregado da vigilância 6190-05");
            Igual("614705", TabelaReinf.Buscar("17001").CodigoReceita(TabelaReinf.AGREGADO, true), "agregado da alimentação 6147-05");
            Igual("906005", TabelaReinf.Buscar("17010").CodigoReceita(TabelaReinf.AGREGADO, true), "agregado dos combustíveis 9060-05");
            Igual("CSLL 6228-05 · COFINS 6243-05 · PIS 6230-05", TabelaReinf.CodigosCsrf("17099", true), "CSLL/COFINS/PIS de demais serviços");
            Igual("CSLL 6228-05", TabelaReinf.CodigosCsrf("17013", true), "combustível de varejista: só CSLL");
            Igual("6256-05", TabelaReinf.Formatar("625605"), "formato da DCTFWeb");

            Func<string, string, string, string> sugerir = (tipo, doc, servico) =>
                TabelaReinf.Sugerir(new Nota { Tipo = tipo, EmitenteDoc = doc, CodigoServico = servico });
            Igual("17031", sugerir("nfse", Amostras.CnpjPrestador, "110201"), "11.02 → Vigilância");
            Igual("17032", sugerir("nfse", Amostras.CnpjPrestador, "071001"), "7.10 → Limpeza");
            Igual("17033", sugerir("nfse", Amostras.CnpjPrestador, "170501"), "17.05 → Locação de mão de obra");
            Igual("17034", sugerir("nfse", Amostras.CnpjPrestador, "100501"), "10.05 → Intermediação");
            Igual("17099", sugerir("nfse", Amostras.CnpjPrestador, "100801"), "10.08 (publicidade) fica em Demais serviços");
            Igual("17099", sugerir("nfse", Amostras.CnpjPrestador, "070201"), "construção civil fica em Demais serviços");
            Igual("17099", sugerir("nfse", Amostras.CnpjPrestador, ""), "sem código de serviço");
            Igual("10002", sugerir("nfse", "12345678909", "170501"), "prestador pessoa física → 10002");
            Igual("17009", sugerir("nfe", Amostras.CnpjPrestador, ""), "NF-e → Mercadorias");
        }

        static void NaturezaNoRepositorio()
        {
            var repo = new Repositorio(new BaseNotas());
            var notas = new List<Nota>();
            for (int i = 1; i <= 3; i++)
            {
                string ch = Amostras.ChaveNfse(700 + i);
                string xml = Amostras.Nfse(ch, (700 + i).ToString(), Amostras.CnpjPrefeitura, 100m, 11m);
                repo.GravarNota(LeitorXml.LerNfse(xml, "NFSE", ch, i.ToString()).Nota, xml);
                notas.Add(repo.Buscar("nfse", ch));
            }
            notas[2].EmitenteDoc = "99888777000166";
            Checar(notas.All(n => repo.Natureza(n) == "17099" && !repo.NaturezaEscolhida(n)), "sem escolha: sugestão pelo serviço");

            repo.DefinirNatureza(notas[0], "17032");
            Checar(repo.NaturezaEscolhida(notas[0]) && repo.Natureza(notas[0]) == "17032", "escolha gravada na nota");
            Checar(repo.Natureza(notas[1]) == "17032" && !repo.NaturezaEscolhida(notas[1]), "mesmo prestador passa a sugerir a escolha");
            Igual("17099", repo.Natureza(notas[2]), "outro prestador não muda");

            repo.DefinirNatureza(notas[1], "17033");
            Igual("17032", repo.Natureza(notas[0]), "escolha da própria nota prevalece");

            Armazenamento.SalvarNotas(repo.Base);
            var relido = new Repositorio(Armazenamento.LerNotas());
            Igual("17032", relido.Natureza(relido.Buscar("nfse", notas[0].Chave)), "natureza escolhida relida do disco");

            // Resumo de NF-e com natureza escolhida, depois chega a completa: a escolha fica.
            string chNfe = Amostras.ChaveNfe("7001");
            repo.GravarNota(LeitorXml.LerNfe(Amostras.ResNfe(chNfe, 10m), "resNFe", "1").Nota, null);
            repo.DefinirNatureza(repo.Buscar("nfe", chNfe), "17010");
            string proc = Amostras.ProcNfe(chNfe, "7001", 10m, null);
            repo.GravarNota(LeitorXml.LerNfe(proc, "procNFe", "2").Nota, proc);
            Igual("17010", repo.Buscar("nfe", chNfe).Natureza, "natureza mantida quando a NF-e completa substitui o resumo");
            File.Delete(Path.Combine(Armazenamento.Pasta, "notas.json"));
        }

        /// <summary>notas.json da versão 1 (sem IR): ao abrir, o IR vem dos XML já guardados.</summary>
        static void AtualizacaoVersao1()
        {
            string ch = Amostras.ChaveNfse(801);
            string xml = Amostras.Nfse(ch, "801", Amostras.CnpjPrefeitura, 1000m, null, tribFedXml: "<vRetIRRF>48.00</vRetIRRF><vRetCSLL>10.00</vRetCSLL>", opSimpNac: "2");
            Nota n = LeitorXml.LerNfse(xml, "NFSE", ch, "1").Nota;
            n.ArquivoXml = Armazenamento.GravarXml("nfse", "2026-10", ch + ".xml", xml);
            n.IrRetido = 0m;
            n.CsrfRetida = 0m;
            n.SimplesNacional = null;
            var antiga = new BaseNotas { Versao = 1 };
            antiga.Notas.Add(n);
            antiga.Notas.Add(new Nota { Tipo = "nfe", Chave = Amostras.ChaveNfe("8002"), Completo = false });   // só resumo: sem XML
            Armazenamento.SalvarNotas(antiga);

            BaseNotas b = Armazenamento.LerNotas();
            Igual(BaseNotas.VersaoAtual, b.Versao, "versão atualizada");
            Igual(48m, b.Notas[0].IrRetido, "IR lido do XML guardado");
            Igual(10m, b.Notas[0].CsrfRetida, "CSLL lida do XML guardado");
            Igual("MEI", b.Notas[0].SimplesNacional, "Simples lido do XML guardado");
            Checar(File.ReadAllText(Path.Combine(Armazenamento.Pasta, "notas.json")).Contains("\"Versao\":2"), "arquivo regravado na versão 2");
            File.Delete(Path.Combine(Armazenamento.Pasta, "notas.json"));
        }
    }
}
