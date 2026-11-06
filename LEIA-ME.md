# Notas com Retenção de INSS

Programa de Windows (WinForms, C# 5, .NET Framework 4.8) que baixa as NFS-e do ADN e, como opção, as NF-e da SEFAZ emitidas para o CNPJ da prefeitura. Ele lista as notas com INSS retido (`vRetCP` na NFS-e Nacional, `vRetPrev` na NF-e) e com IR retido (`vRetIRRF` / `vIRRF`), sugere a natureza de rendimento da EFD-Reinf com o código de receita do Anexo I e exporta para Excel. É independente do SST: a lógica foi passada do módulo de NF de lá para C#, mas nada do SST é usado nem alterado.

## Compilar e testar

```
compilar.bat            -> dist\NotasRetencaoINSS.exe
compilar.bat testes     -> também testes\bin\Testes.exe
testes\bin\Testes.exe   -> roda a bateria (servidor ADN/SEFAZ simulado em 127.0.0.1)
testes\bin\Testes.exe capturas <pasta>   -> PNG das telas com dados de exemplo
```

Usa o `csc.exe` que já vem no Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`), que só aceita C# 5: nada de `$""`, `?.` ou `=>` em membros.

## Arquivos

| Arquivo | O que faz |
|---|---|
| `src/Adn.cs` | NFS-e: `GET /contribuintes/DFe/{NSU}?cnpjConsulta=&lote=true` (lotes de 50, XML em gzip+base64) |
| `src/Sefaz.cs` | NF-e: Distribuição DF-e (distNSU) e Ciência da Operação 210210 assinada com `SignedXml` (RSA-SHA1, C14N), até 20 notas por lote |
| `src/Sincronizador.cs` | Laço por NSU. Regra de 1 hora da SEFAZ (cStat 137 / ultNSU = maxNSU) e bloqueio do 656 |
| `src/LeitorXml.cs` | Leitura dos XML pelo nome local dos elementos. Eventos de cancelamento: NFS-e 101101/105102/105104/305101, NF-e 110111/110112. CSLL/PIS/COFINS da NFS-e em `CsrfNfse` (regra abaixo) |
| `src/Armazenamento.cs` | `dados\config.json`, `dados\notas.json` (gravação atômica com .bak) e `dados\xml\...`. Regras para juntar resumo e nota completa. `notas.json` versão 1 é completado com o IR relendo os XML guardados. Natureza escolhida vale como sugestão para o mesmo prestador |
| `src/TabelaReinf.cs` | Anexo I do Manual da EFD-Reinf 2.1.2.1 (PDF na raiz): grupo 17 do R-4020 inteiro (50 naturezas, colunas Sim/Não da classificação 85) e a 10002 do R-4010. Linhas geradas do texto do PDF (`pdftotext -raw`), não digitadas. Sugestão de natureza pelo item da LC 116 (`cTribNac`) |
| `src/Certificados.cs` | Repositório do Windows (A1 importado ou A3) ou .pfx. CNPJ pelo SAN 2.16.76.1.3.3 ou pelo CN |
| `src/ClienteFiscal.cs` | `HttpWebRequest` com TLS 1.2 e certificado de cliente. Usa o proxy do Windows com as credenciais do usuário |
| `src/Planilha.cs` | .xlsx feito à mão (ZipArchive), sem depender de Excel. Abas Notas, Por prestador e EFD-Reinf (retenções por prestador × natureza) |
| `src/Exportador.cs` | Botão "Salvar XML e PDF": copia o XML guardado e baixa o PDF da NFS-e (DANFSe) em `GET {adn}/danfse/{chave}`, guardado em `dados\pdf\...`. Para de pedir PDF depois de 3 falhas seguidas |

## Status

- Testado só com servidor simulado e certificado autoassinado: 184 verificações. A distribuição de NFS-e (DFe) já respondeu certo no ADN real. **A SEFAZ e o PDF do ADN ainda não foram conferidos de verdade**: o endereço `/danfse/{chave}` veio de clientes de terceiros.
- Homologação: `"Ambiente": 2` no `dados\config.json`. As chaves `UrlAdn`, `UrlDanfse`, `UrlSefazDistribuicao` e `UrlSefazEvento` servem só para os testes.
- `"ClassTrib"` no `dados\config.json` (padrão `"85"`, administração pública direta, autarquia e fundação) escolhe a coluna "Sim" do Anexo I (IR 6256-05). Qualquer outro valor usa a coluna "Não" (IR 6256-06).
- **CSLL/PIS/COFINS da NFS-e são uma estimativa somada**: no leiaute original `vRetCSLL` é só a CSLL e PIS/COFINS foram retidos quando `tpRetPisCofins = 1`; na NT 007/2026 (`tpRetPisCofins` 0 e 3 a 9) `vRetCSLL` passa a ser o total dos três. As fontes divergem sobre o significado do código 1 depois da NT 007, então pode haver dupla contagem nesse caso. O IR (`vRetIRRF`) não tem essa ambiguidade.
- A sugestão de natureza só cobre itens sem ambiguidade (11.02, 7.10, 17.05, 17.12, 16.01, 10.xx menos 10.08); o resto vai para 17099 e a pessoa confere na tela.
