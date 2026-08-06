# ACBrBoleto.GeneXus — Guia de Integração

> Edição pública: exemplos usam dados fictícios. Credenciais bancárias,
> certificados e identificadores reais nunca devem ser versionados.

Biblioteca .NET 8 que substitui as chamadas REST à STX API por chamadas diretas
à **ACBrLibBoleto** (Delphi/Lazarus), eliminando a dependência de servidor externo.

---

## Arquitetura

```
┌─────────────────────────────────────────────────────────────────────┐
│  GeneXus                                                            │
│                                                                     │
│  1. Consulta PostgreSQL:                                            │
│     configboleto + titulo + cliente + unidade                       │
│                                                                     │
│  2. Serializa cada row como JSON                                    │
│                                                                     │
│  3. Chama External Object (esta DLL):                               │
│     BoletoEntryPoint.GerarBoleto(cfg, tit, cli, uni)                │
│                                                                     │
│  4. Deserializa JSON de retorno                                     │
│                                                                     │
│  5. Persiste resultado: titulo.links, titulo.qrcodepix_*,           │
│     boletos.link, boletos.datageracao                               │
└───────────────────────┬─────────────────────────────────────────────┘
                        │ JSON in/out
┌───────────────────────▼─────────────────────────────────────────────┐
│  ACBrBoleto.GeneXus.dll  (.NET 8)                                   │
│                                                                     │
│  • Deserializa JSONs das tabelas do PostgreSQL                      │
│  • Gerencia pool de instâncias ACBrLib por config                   │
│  • Detecta mudança de credenciais via hash automático               │
│  • Chama ACBrLibBoleto nativa (Pascal/Delphi)                       │
│  • Serializa resposta de volta para GeneXus                         │
└───────────────────────┬─────────────────────────────────────────────┘
                        │ P/Invoke (StdCall)
┌───────────────────────▼─────────────────────────────────────────────┐
│  ACBrLibBoleto64.dll  (nativa — baixar separado)                    │
│  Comunicação OAuth2 / mTLS com a API do banco                       │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Pré-requisitos

| Item | Versão mínima |
|------|---------------|
| .NET Runtime | 8.0 (x64) |
| ACBrLibBoleto64.dll | Última estável |
| GeneXus | 18+ (com suporte a External Objects .NET) |

### Baixar a ACBrLibBoleto64.dll

1. Acesse https://www.projetoacbr.com.br/acbrlib/
2. Baixe **ACBrLib Binários — Windows 64 bits**
3. Copie `ACBrLibBoleto64.dll` para a pasta da aplicação GeneXus

---

## Estrutura da Solução

```
ACBrBoleto.sln
├── src/
│   ├── ACBrBoleto.Core/         ← Modelos, Interop P/Invoke, Pool, Serializer
│   └── ACBrBoleto.GeneXus/      ← EntryPoint estático + Bootstrapper
└── tests/
    └── ACBrBoleto.Tests/        ← xUnit (sem DLL nativa, sem banco)
```

---

## Configuração do External Object no GeneXus

### 1. Registrar a DLL

No GeneXus, criar um **External Object** apontando para `ACBrBoleto.GeneXus.dll`:

```
External Object: BoletoEntryPoint
Assembly: ACBrBoleto.GeneXus
Class: ACBrBoleto.GeneXus.BoletoEntryPoint
```

### 2. Declarar os métodos

Todos os métodos retornam `VarChar(max)` (JSON). A maioria das operações recebe o
**quad de entrada** padrão — os quatro JSONs serializados do PostgreSQL:

```
configJson  : VarChar(max) [IN]   // configboleto + caminhoACBrLib
tituloJson  : VarChar(max) [IN]   // titulo
clienteJson : VarChar(max) [IN]   // cliente (sacado)
unidadeJson : VarChar(max) [IN]   // unidade (cedente)
```

#### Ciclo de vida (sem quad)

```
Method: Inicializar
  Parameter: nivelLog : VarChar(20) [IN]   // "Information" (default)
  Return: VarChar(max)

Method: Encerrar
  Return: VarChar(max)
```

#### Geração e PDF (somente o quad)

```
Method: GerarBoleto       (configJson, tituloJson, clienteJson, unidadeJson)  // registra; NÃO gera PDF
Method: GerarPdf          (configJson, tituloJson, clienteJson, unidadeJson)
Method: GerarPdfBase64    (configJson, tituloJson, clienteJson, unidadeJson)
```

#### Consultas

```
Method: ConsultarBoleto       (quad, nossoNumero : VarChar(50), incluirRawJson : Boolean [opcional, default true])
Method: ConsultarListaBoletos (configJson, unidadeJson, filtroJson : VarChar(max))
```

#### Manutenção (quad + parâmetros específicos)

```
Method: AlterarVencimento              (quad, novaData : VarChar(20))      // "yyyy-MM-dd"
Method: AlterarValor                   (quad, novoValor : VarChar(20))     // "250.50"
Method: BaixarBoleto                   (quad, nossoNumero : VarChar(50), motivo : VarChar(50) [opcional])  // "ACERTOS"
Method: DebitarEmConta                 (quad, nossoNumero : VarChar(50))
Method: ConcederAbatimento             (quad, nossoNumero, valorAbatimento : VarChar(20), dataAbatimento : VarChar(20) [opcional])
Method: CancelarAbatimento             (quad, nossoNumero : VarChar(50))
Method: ConcederDesconto               (quad, nossoNumero, valorDesconto : VarChar(20), dataDesconto [opcional], tipoDesconto [opcional])
Method: CancelarDesconto               (quad, nossoNumero : VarChar(50))
Method: AlterarVencimentoSustarProtesto(quad, novaData : VarChar(20))
Method: Protestar                      (quad, nossoNumero, dataProtesto [opcional], codigoNegativacao [opcional], diasProtesto [opcional])
Method: SustarProtesto                 (quad, nossoNumero : VarChar(50))
```

> **Não existe `InvalidarPool`.** O pool se invalida sozinho quando o hash das credenciais
> em `configJson` muda (`ConfigBoleto.ComputarHash()`) — basta enviar o config atualizado
> na próxima chamada que o pool antigo é descartado em background.

---

## Uso no GeneXus

### Inicializar (uma vez, no startup)

```gx
// ──────────────────────────────────────────────────
// Chamar no início do processo ou no Main do servidor
// ──────────────────────────────────────────────────
&retJson = BoletoEntryPoint.Inicializar("Information")
&ret     = new()                          // SDT de resultado
FromJson(&retJson, &ret)
If not &ret.sucesso
    Msg("Erro ao inicializar ACBrBoleto: " + &ret.erro)
EndIf
```

### Montar os JSONs a partir do banco

```gx
// ── configboleto (JOIN com unidade para pegar id_unidade) ──
For each configboleto
    Where configboleto.id = &idConfigBoleto
    // Adicionar campo extra: caminhoACBrLib
    &cfgObj             = new SDTConfigBoleto()
    &cfgObj.id          = configboleto.id
    &cfgObj.codbanco    = configboleto.codbanco
    &cfgObj.agencia     = configboleto.agencia
    &cfgObj.agenciadig  = configboleto.agenciadig
    &cfgObj.conta       = configboleto.conta
    &cfgObj.contadig    = configboleto.contadig
    &cfgObj.carteira    = configboleto.carteira
    &cfgObj.convenio    = configboleto.convenio
    &cfgObj.ws_clientid     = configboleto.ws_clientid
    &cfgObj.ws_clientsecret = configboleto.ws_clientsecret
    &cfgObj.ws_scope        = configboleto.ws_scope
    &cfgObj.ws_ambiente     = configboleto.ws_ambiente
    &cfgObj.indicadorpix    = configboleto.indicadorpix
    &cfgObj.chavepix        = configboleto.chavepix
    &cfgObj.instrucao1      = configboleto.instrucao1
    &cfgObj.instrucao2      = configboleto.instrucao2
    &cfgObj.codigomulta     = configboleto.codigomulta
    &cfgObj.codigomorajuros = configboleto.codigomorajuros
    &cfgObj.codigodesconto  = configboleto.codigodesconto
    &cfgObj.codigonegativacao = configboleto.codigonegativacao
    &cfgObj.usecertificatehttp = configboleto.usecertificatehttp
    &cfgObj.arquivocrt      = configboleto.arquivocrt
    &cfgObj.arquivokey      = configboleto.arquivokey
    &cfgObj.tamanhoPool     = 2                               // ajustar conforme carga
    &cfgObj.nivelLog        = 1                               // 1=Erros, 3=Info, 4=Debug
    &cfgObj.pastaLog        = "C:\Logs\ACBrBoleto"
    &cfgObj.pastaOutput     = "C:\Output\ACBrBoleto"
    &cfgObj.caminhoACBrLib  = "C:\ACBrLib\ACBrLibBoleto64.dll"
    ToJson(&cfgObj, &configJson)
EndFor

// ── titulo ──────────────────────────────────────────────────
For each titulo
    Where titulo.id = &idTitulo
    &titObj                    = new SDTTitulo()
    &titObj.id                 = titulo.id
    &titObj.id_configboleto    = titulo.id_configboleto
    &titObj.nossonumero        = titulo.nossonumero
    &titObj.numerodocumento    = titulo.numerodocumento
    &titObj.valordocumento     = titulo.valordocumento
    &titObj.datavencimento     = titulo.datavencimento.ToString("yyyy-MM-dd")
    &titObj.datadocumento      = titulo.datadocumento.ToString("yyyy-MM-dd")
    &titObj.dataprocessamento  = titulo.dataprocessamento.ToString("yyyy-MM-dd")
    &titObj.percentualmulta    = titulo.percentualmulta
    &titObj.CodigoMulta        = titulo.codigomulta
    &titObj.CodigoMoraJuros    = titulo.codigomorajuros
    &titObj.CodigoDesconto     = titulo.codigodesconto
    &titObj.CodigoNegativacao  = titulo.codigonegativacao
    ToJson(&titObj, &tituloJson)
EndFor

// ── cliente ─────────────────────────────────────────────────
For each cliente
    Where cliente.id = titulo.id_cliente
    ToJson(cliente, &clienteJson)
EndFor

// ── unidade ─────────────────────────────────────────────────
For each unidade
    Where unidade.id = titulo.id_unidade
    ToJson(unidade, &unidadeJson)
EndFor
```

### Gerar boleto

```gx
&retJson = BoletoEntryPoint.GerarBoleto(&configJson, &tituloJson, &clienteJson, &unidadeJson)
&ret     = new SDTResultado()
FromJson(&retJson, &ret)

If &ret.sucesso
    // Persistir resultado no banco
    For each titulo
        Where titulo.nossonumero = &ret.nossoNumero
            titulo.links         = &ret.urlBoleto
            titulo.qrcodepix_url = &ret.qrcodepix_url
            titulo.qrcodepix_emv = &ret.qrcodepix_emv
            titulo.qrcodepix_txid= &ret.qrcodepix_txid
        EndFor.Commit

    // Criar registro em boletos
    New boletos
        boletos.id_titulo    = titulo.id
        boletos.datageracao  = Today()
        boletos.link         = &ret.urlBoleto
    EndNew.Commit

    Msg("Boleto gerado: " + &ret.linhaDigitavel)
Else
    Msg("Erro: " + &ret.erro)
EndIf
```

### Consultar boleto

```gx
// A consulta precisa do quad completo para montar o INI do cedente antes de chamar o WS.
&retJson = BoletoEntryPoint.ConsultarBoleto(&configJson, &tituloJson, &clienteJson, &unidadeJson, titulo.nossonumero)
&ret     = new SDTResultado()
FromJson(&retJson, &ret)

If &ret.sucesso
    // &ret.situacao    → "ABERTO" | "PAGO" | "BAIXADO"
    // &ret.valorPago   → valor pago normalizado p/ ponto decimal ("250.00") — pronto p/ ToNumeric('.')
    // &ret.dataPagamento → "dd/MM/yyyy"
    Msg("Situação: " + &ret.situacao)
EndIf
```

### Alterar vencimento

```gx
&novaData = &novoVencimento.ToString("yyyy-MM-dd")
&retJson  = BoletoEntryPoint.AlterarVencimento(&configJson, &tituloJson, &clienteJson, &unidadeJson, &novaData)
```

### Baixar boleto

```gx
&retJson = BoletoEntryPoint.BaixarBoleto(&configJson, &tituloJson, &clienteJson, &unidadeJson, titulo.nossonumero, "ACERTOS")
```

### Consultar lista com filtro

> **Status:** atualmente **não usado em produção.** A consulta em lista por período
> apresenta problemas em aberto, então hoje consultamos boleto a boleto em loop com
> `ConsultarBoleto`. Mantido aqui para referência da assinatura.

```gx
&filtroObj                          = new SDTFiltroWS()
&filtroObj.VencimentoDtIni          = &dtInicio.ToString("yyyy-MM-dd")
&filtroObj.VencimentoDtFim          = &dtFim.ToString("yyyy-MM-dd")
&filtroObj.IndicadorSituacaoBoleto  = 1   // 1=Aberto

ToJson(&filtroObj, &filtroJson)

&retJson = BoletoEntryPoint.ConsultarListaBoletos(&configJson, &unidadeJson, &filtroJson)
&ret     = new SDTResultado()
FromJson(&retJson, &ret)

// &ret.itens → JSON array de ConsultaListaItem com campos camelCase:
//   nossoNumero, situacao, pago, dataPagamento, valorPago, codigoBarras, linhaDigitavel
//   (valorPago normalizado p/ ponto decimal; iterar com FromJson em SDT de lista)
```

### Gerar PDF em Base64

```gx
&retJson = BoletoEntryPoint.GerarPdfBase64(&configJson, &tituloJson, &clienteJson, &unidadeJson)
&ret     = new SDTResultado()
FromJson(&retJson, &ret)

If &ret.sucesso
    // &ret.pdfBase64 → gravar em arquivo ou devolver ao frontend
    &pdfFile.FromBase64(&ret.pdfBase64)
    &pdfFile.Save("C:\Output\boleto_" + titulo.nossonumero + ".pdf")
EndIf
```

---

## SDTs GeneXus necessários

Criar os seguintes SDTs baseados nos campos JSON:

### SDTResultado (retorno de qualquer operação)
```
sucesso          : Boolean
mensagem         : VarChar(500)
nossoNumero      : VarChar(50)
codigoBarras     : VarChar(200)
linhaDigitavel   : VarChar(200)
urlBoleto        : VarChar(500)
idBanco          : VarChar(100)
qrcodepix_url    : VarChar(500)
qrcodepix_emv    : VarChar(max)
qrcodepix_txid   : VarChar(100)
pdfBase64        : VarChar(max)
situacao         : VarChar(50)
dataPagamento    : VarChar(20)
valorPago        : VarChar(20)
itens            : VarChar(max)   // JSON array
erro             : VarChar(max)
```

### SDTFiltroWS (para ConsultarListaBoletos)
```
VencimentoDtIni          : VarChar(10)   // "yyyy-MM-dd"
VencimentoDtFim          : VarChar(10)
RegistroDtIni            : VarChar(10)
RegistroDtFim            : VarChar(10)
MovimentoDtIni           : VarChar(10)
MovimentoDtFim           : VarChar(10)
IndicadorSituacaoBoleto  : Numeric(1)    // 0=Nenhum, 1=Aberto, 2=Baixado
BoletoVencido            : Numeric(1)    // 0=Nenhum, 1=Não, 2=Sim
Carteira                 : Numeric(5)
```

---

## Pool de instâncias — como funciona

A DLL mantém um pool de instâncias da ACBrLibBoleto em memória, uma por
`configboleto.id`. Isso significa:

- **Primeira chamada** para um `configId` → cria o pool (demora ~1-2s)
- **Chamadas seguintes** → reutiliza instância já configurada (< 5ms overhead)
- **Mudança de credenciais** → automática: o pool detecta a mudança do hash de
  `configJson` (`ConfigBoleto.ComputarHash()`) e descarta o pool antigo em background.
  Basta enviar o config atualizado na próxima chamada — não há método manual de invalidação.
- **Multi-empresa** → cada `configboleto.id` tem seu próprio pool isolado
- **Concorrência** → `tamanhoPool` instâncias paralelas por config (padrão: 2)

### Recomendações de tamanhoPool

| Carga | tamanhoPool |
|-------|-------------|
| Baixa (< 10 boletos/min) | 1 |
| Média (10-100 boletos/min) | 2–3 |
| Alta (> 100 boletos/min) | 4–6 |

---

## Campos de retorno → mapeamento para tabelas

| Campo retornado | Tabela.Coluna PostgreSQL |
|----------------|--------------------------|
| `nossoNumero`   | `titulo.nossonumero` |
| `urlBoleto`     | `titulo.links` e `boletos.link` |
| `qrcodepix_url` | `titulo.qrcodepix_url` |
| `qrcodepix_emv` | `titulo.qrcodepix_emv` |
| `qrcodepix_txid`| `titulo.qrcodepix_txid` |
| `pdfBase64`     | Decodificar e salvar em disco |
| `situacao`      | Lógica de negócio GeneXus |

---

## Logs

Os logs são escritos em `logs/acbrboleto-YYYYMMDD.log` (relativo ao diretório
da DLL). Cada linha tem o formato:

```
2025-06-15 14:23:01.123 [Information] ACBrBoleto.GeneXus.BoletoEntryPoint |
    [GX→] GerarBoleto
2025-06-15 14:23:01.891 [Information] ACBrBoleto.GeneXus.BoletoEntryPoint |
    [GX←] GerarBoleto | sucesso=True | 768ms
```

Para debug de problemas com o banco, aumentar `nivelLog` para `3` (Informações)
ou `4` (Debug) no campo do configJson.

---

## Executar os testes

```bash
# Não requer ACBrLibBoleto.dll nem PostgreSQL
dotnet test tests/ACBrBoleto.Tests/
```
