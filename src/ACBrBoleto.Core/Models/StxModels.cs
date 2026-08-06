using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACBrBoleto.Core.Enums;
using ACBrBoleto.Core.Exceptions;

namespace ACBrBoleto.Core.Models;

/// <summary>
/// Converte o campo aceite que pode chegar como int (0/1) ou string ("A"/"N")
/// dependendo do tipo do atributo na transaction GeneXus.
/// </summary>
internal sealed class AceiteConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
        if (reader.TokenType == JsonTokenType.True) return 1;
        if (reader.TokenType == JsonTokenType.False) return 0;
        var s = reader.GetString() ?? "";
        return s.Trim().ToUpperInvariant() switch { "A" or "S" or "1" or "TRUE" => 1, _ => 0 };
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

// Aceita int puro, string numérica ("10") ou string vazia/inválida ("") → 0.
// GeneXus serializa campos int como "" quando o SDT ainda os declara como string.
public sealed class TolerantIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
        var s = reader.GetString() ?? "";
        return int.TryParse(s.Trim(), out var v) ? v : 0;
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

// ═══════════════════════════════════════════════════════════════════
//  INPUT MODELS
//  Campos idênticos às colunas do PostgreSQL (snake_case preservado)
//  O GeneXus serializa as rows diretamente para esses modelos.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Espelha a tabela <c>configboleto</c> do PostgreSQL.
/// Contém tudo que a ACBrLib precisa para autenticar e configurar
/// a comunicação com o banco (credenciais OAuth2, carteira, etc.).
/// </summary>
public sealed class ConfigBoleto
{
    // ── Identificação ────────────────────────────────────────────────
    public int id { get; set; }
    public int id_unidade { get; set; }
    public int tipocobranca { get; set; }   // Código nativo ACBrLib: ex. 6=Itaú API, 36=BB API, 42=Inter API. NÃO é flag genérico 1=API/2=Remessa.

    // ── Dados bancários ──────────────────────────────────────────────
    public int codbanco { get; set; }   // ex: 341=Itaú, 237=Bradesco
    public string agencia { get; set; } = string.Empty;
    public string agenciadig { get; set; } = string.Empty;
    public string conta { get; set; } = string.Empty;
    public string contadig { get; set; } = string.Empty;
    public string codigocedente { get; set; } = string.Empty;
    public string convenio { get; set; } = string.Empty;
    public string carteira { get; set; } = string.Empty;
    public string modalidade { get; set; } = string.Empty;
    public string especiedoc { get; set; } = "DM";
    public string especiemod { get; set; } = string.Empty;
    [JsonConverter(typeof(AceiteConverter))]
    public int aceite { get; set; }   // 0=N, 1=A
    public string localpagamento { get; set; } = string.Empty;
    public int layoutremessa { get; set; }
    public string layoutdocumento { get; set; } = string.Empty;
    // Código numérico FEBRABAN/CNAB (ver enum CodigoInstrucao). 0 = sem instrução.
    public int instrucao1 { get; set; }
    public int instrucao2 { get; set; }
    // Texto livre impresso no boleto. Separar linhas com "|".
    // Combina automaticamente com Titulo.mensagem/informativo (cfg vai primeiro).
    public string mensagem { get; set; } = string.Empty;
    public string informativo { get; set; } = string.Empty;
    public int tipopagamento { get; set; }
    public int tipoocorrenciaremessa { get; set; }

    // ── PIX ──────────────────────────────────────────────────────────
    public int tipochavepix { get; set; }
    public string chavepix { get; set; } = string.Empty;
    public int indicadorpix { get; set; }   // 0=Nenhum, 1=Estático, 2=Dinâmico

    // ── WebService / OAuth2 ──────────────────────────────────────────
    public string ws_clientid { get; set; } = string.Empty;
    public string ws_clientsecret { get; set; } = string.Empty;
    public string ws_keyuser { get; set; } = string.Empty;
    public string ws_scope { get; set; } = string.Empty;
    public int ws_ambiente { get; set; }   // 1=Producao, 2=Homologacao

    // ── Certificado HTTP (mTLS) ──────────────────────────────────────
    public string arquivocrt { get; set; } = string.Empty;
    public string arquivokey { get; set; } = string.Empty;
    public int usecertificatehttp { get; set; }

    // ── Campos extras CNAB / ACBrLib ────────────────────────────────
    public int tipo_distribuicao { get; set; }
    public int carac_titulo { get; set; }
    public int resp_emissao { get; set; }
    public int tipo_carteira { get; set; }
    public string layoutversaolote { get; set; } = string.Empty;
    public string layoutversaoarquivo { get; set; } = string.Empty;
    public string cip { get; set; } = string.Empty;
    public string densidadegravacao { get; set; } = string.Empty;
    public string codigodetransmissao { get; set; } = string.Empty;
    public string prefixremessa { get; set; } = string.Empty;
    public string operacaocedente { get; set; } = string.Empty;
    public int tipodocumento { get; set; }
    public string VersaoDF { get; set; } = string.Empty;
    // Banco do Brasil API: CNPJ/identificador da software house para o header x-bb-portal-devx-cnpj-parceiro.
    public string? keysoftwarehouse { get; set; }

    // ── Códigos padrão (usados quando o título não sobrescreve) ─────
    public int codigodesconto { get; set; }   // 1-3: categorização DB/FEBRABAN (não vai para o INI)
    public int tipodesconto { get; set; }   // 0-6: TipoDesconto= no INI ACBrLib
    public int codigomorajuros { get; set; }
    public int codigomulta { get; set; }
    public int codigonegativacao { get; set; }   // 0-6: política de protesto/negativação
    public int diasprotesto { get; set; }   // dias após vencimento (obrigatório para codNeg 1, 2, 4)

    // ── Caminho da DLL nativa no servidor ────────────────────────────
    /// <summary>
    /// Caminho absoluto da ACBrLibBoleto64.dll no servidor de aplicação.
    /// Enviado pelo GeneXus junto com a config para evitar hard-code na DLL.
    /// </summary>
    public string caminhoACBrLib { get; set; } = string.Empty;

    // ── Configurações de pool/log (opcionais, com defaults) ──────────
    // O tamanho do pool NÃO é mais por-config: é um teto global do processo definido pela
    // variável de ambiente ACBR_POOL_MAX (default 16). Ver PoolManager.
    public int poolWaitTimeoutSec { get; set; } = 30;
    public int nivelLog { get; set; } = 1;  // 1=Erros
    public string pastaLog { get; set; } = "logs";
    public string pastaOutput { get; set; } = "output";
    public string dirLogo { get; set; } = string.Empty;

    // ── Hash para detecção de mudança de config no pool ──────────────
    /// <summary>
    /// Hash SHA256 do config INTEIRO. Se QUALQUER campo mudar entre chamadas (edição via CRUD do
    /// GeneXus), o hash muda e o pool recria o handle com a nova config no próximo aluguel —
    /// inclusive campos de seções globais init-only (VersaoDF, ws_keyuser, indicadorpix, config de
    /// PDF) que o ConfigurarDados por operação NÃO reaplica. Serializa o objeto todo para não exigir
    /// manutenção da lista de campos quando a tabela ganha colunas novas. O custo (alguns centenas
    /// de bytes por aluguel) é desprezível; quando nada muda, o hash bate e reusa o handle quente.
    /// </summary>
    public string ComputarHash()
    {
        var json = JsonSerializer.Serialize(this);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..16];
    }
}

/// <summary>
/// Espelha a tabela <c>titulo</c> do PostgreSQL.
/// Contém os dados financeiros do título a ser registrado.
/// </summary>
public sealed class Titulo
{
    public int id { get; set; }
    public int id_unidade { get; set; }
    public int id_cliente { get; set; }
    public int id_configboleto { get; set; }
    public string datavencimento { get; set; } = string.Empty;  // "yyyy-MM-dd" ou "dd/MM/yyyy"
    public string datadocumento { get; set; } = string.Empty;
    public string numerodocumento { get; set; } = string.Empty;
    public string dataprocessamento { get; set; } = string.Empty;
    public string nossonumero { get; set; } = string.Empty;
    // Banco Inter: UUID retornado pelo banco no registro; necessário para consultas e baixa subsequentes.
    public string? nossonumerocorrespondente { get; set; }
    public decimal valordocumento { get; set; }
    public decimal valorabatimento { get; set; }
    public decimal valormorajuros { get; set; }
    public decimal valordesconto { get; set; }
    public string? datamorajuros { get; set; }
    public string? datadesconto { get; set; }
    public string? dataabatimento { get; set; }
    public string? dataprotesto { get; set; }
    public decimal percentualmulta { get; set; }
    public int tipo { get; set; }
    public string? links { get; set; }
    public string? qrcodepix_url { get; set; }
    public string? qrcodepix_txid { get; set; }
    public string? qrcodepix_emv { get; set; }
    public int ocorrenciamanut { get; set; }
    public int operacaowsmanut { get; set; }

    // Sobrescrevem os defaults da configboleto quando > 0
    public int CodigoDesconto { get; set; }   // 1-3: categorização DB/FEBRABAN (não vai para o INI)
    public int TipoDesconto { get; set; }   // 0-6: TipoDesconto= no INI ACBrLib (0 = usa config)
    public int CodigoMoraJuros { get; set; }
    public int CodigoMulta { get; set; }
    public int CodigoNegativacao { get; set; }   // sobrescreve cfg quando > 0
    public int DiasProtesto { get; set; }   // sobrescreve cfg quando > 0

    // Instrução CNAB por título (0 = usa o da configboleto)
    public int instrucao1 { get; set; }
    public int instrucao2 { get; set; }

    // Texto livre impresso no boleto — ACRESCENTA ao mensagem/informativo da config.
    // Ex: "Parcela 1/2 | NF 12345" → aparece após as linhas padrão da unidade.
    public string? mensagem { get; set; }
    public string? informativo { get; set; }
}

/// <summary>
/// Espelha a tabela <c>cliente</c> do PostgreSQL.
/// Contém os dados do sacado (devedor).
/// </summary>
public sealed class Cliente
{
    public int id { get; set; }
    public int id_empresa { get; set; }
    public string nome { get; set; } = string.Empty;
    public string cpfcnpj { get; set; } = string.Empty;
    public string logradouro { get; set; } = string.Empty;
    public string numero { get; set; } = string.Empty;
    public string complemento { get; set; } = string.Empty;
    public string cep { get; set; } = string.Empty;
    public string bairro { get; set; } = string.Empty;
    public string cidade { get; set; } = string.Empty;
    public string uf { get; set; } = string.Empty;
    public int tipo { get; set; }  // 0=PFisica, 1=PJuridica
    public string email { get; set; } = string.Empty;
    public string telefone { get; set; } = string.Empty;
    public string mensagem { get; set; } = string.Empty;
}

/// <summary>
/// Espelha a tabela <c>unidade</c> do PostgreSQL.
/// Contém os dados da unidade emissora (cedente).
/// </summary>
public sealed class Unidade
{
    public int id { get; set; }
    public int id_empresa { get; set; }
    public string nome { get; set; } = string.Empty;
    public string fantasia { get; set; } = string.Empty;
    public string cpfcnpj { get; set; } = string.Empty;
    public int tipo { get; set; }
    public string logradouro { get; set; } = string.Empty;
    public string numero { get; set; } = string.Empty;
    public string cep { get; set; } = string.Empty;
    public string bairro { get; set; } = string.Empty;
    public string cidade { get; set; } = string.Empty;
    public string uf { get; set; } = string.Empty;
    public string complemento { get; set; } = string.Empty;
    public string telefone { get; set; } = string.Empty;
    // Logo da empresa (cedente) impressa no PDF — vai para [Titulo1] ArquivoLogoEmp.
    // Caminho completo de arquivo. FPDF aceita só PNG/JPG (não BMP). Vazio = sem logo.
    public string arquivologoemp { get; set; } = string.Empty;
}

/// <summary>
/// Filtro para consulta em lista (FiltroWS do swagger).
/// </summary>
public sealed class FiltroWS
{
    public int? IndicadorSituacaoBoleto { get; set; }  // 0=Nenhum,1=Aberto,2=Baixado
    public int? BoletoVencido { get; set; }  // 0=Nenhum,1=Não,2=Sim
    public string? VencimentoDtIni { get; set; }
    public string? VencimentoDtFim { get; set; }
    public string? RegistroDtIni { get; set; }
    public string? RegistroDtFim { get; set; }
    public string? MovimentoDtIni { get; set; }
    public string? MovimentoDtFim { get; set; }
    public string? CnpjPagador { get; set; }
    public int? ContaCaucao { get; set; }
    public int? CodigoEstadoTituloCobranca { get; set; }
    public int? ModalidadeCobranca { get; set; }
    public int? Carteira { get; set; }
    public int? CarteiraVariacao { get; set; }
    public string? IndiceContinuidade { get; set; }
    public string? NumeroProtocolo { get; set; }
    public string? Identificador { get; set; }
    // Client-side post-filter: when non-empty, returned list is filtered to only these nossoNumeros
    public List<string>? NossoNumeros { get; set; }
}

// ═══════════════════════════════════════════════════════════════════
//  OUTPUT / RESULT MODELS
// ═══════════════════════════════════════════════════════════════════

public abstract class BaseResponse
{
    public bool sucesso { get; set; }
    public string mensagem { get; set; } = string.Empty;
    public string? erro { get; set; }

    public static T Ok<T>(string msg = "OK") where T : BaseResponse, new() =>
        new T { sucesso = true, mensagem = msg };

    public static T Falha<T>(string msg, Exception? ex = null) where T : BaseResponse, new() =>
        new T
        {
            sucesso = false,
            mensagem = msg,
            erro = ex is null ? msg : $"[{ex.GetType().Name}] {ex.Message}"
        };

    public static T Falha<T>(Exception ex) where T : BaseResponse, new()
    {
        var (label, mensagem) = ex switch
        {
            AcbrLibException e => ($"ACBrLib op={e.Operacao ?? "?"} cod={e.CodigoRetorno}", e.Message),
            ConfigInvalidaException => ("Config", ex.Message),
            PoolTimeoutException => ("Pool", ex.Message),
            JsonException => ("EntradaJSON", $"Erro ao ler JSON de entrada: {ex.Message}"),
            ArgumentException => ("EntradaInvalida", ex.Message),
            _ => (ex.GetType().Name, ex.Message),
        };
        return new T { sucesso = false, mensagem = mensagem, erro = $"[{label}] {mensagem}" };
    }
}

public class OperacaoSimplesResponse : BaseResponse { }

public class PixDados
{
    public string url { get; set; } = string.Empty;
    public string emv { get; set; } = string.Empty;
    public string txid { get; set; } = string.Empty;
    public string base64 { get; set; } = string.Empty;
}

public class GerarBoletoResponse : BaseResponse
{
    public string nossoNumero { get; set; } = string.Empty;
    public string codigoBarras { get; set; } = string.Empty;
    public string linhaDigitavel { get; set; } = string.Empty;
    public string urlBoleto { get; set; } = string.Empty;
    public string idBanco { get; set; } = string.Empty;
    public PixDados? pix { get; set; }
}

public class GerarPdfResponse : BaseResponse
{
    public string nossoNumero { get; set; } = string.Empty;
    public string pdfPath { get; set; } = string.Empty;
    public string codigoBarras { get; set; } = string.Empty;
    public string linhaDigitavel { get; set; } = string.Empty;
    public PixDados? pix { get; set; }
    public string rawJson { get; set; } = string.Empty;
}

public class GerarPdfBase64Response : BaseResponse
{
    public string nossoNumero { get; set; } = string.Empty;
    public string pdfBase64 { get; set; } = string.Empty;
}

public class ConsultaDetalheResponse : BaseResponse
{
    public string nossoNumero { get; set; } = string.Empty;
    public string situacao { get; set; } = string.Empty;

    // Status normalizado — true quando situacao indica liquidação confirmada
    public bool pago { get; set; }

    // Valores recebidos / encargos (strings em formato invariante "250.00")
    public string valorPago { get; set; } = string.Empty;   // = ValorRecebido do banco
    public string valorJurosMora { get; set; } = string.Empty;
    public string valorMulta { get; set; } = string.Empty;
    public string valorDesconto { get; set; } = string.Empty;
    public string valorAbatimento { get; set; } = string.Empty;
    public string valorIOF { get; set; } = string.Empty;
    public string valorOutrasDespesas { get; set; } = string.Empty;
    public string valorOutrosCreditos { get; set; } = string.Empty;
    public string valorDespesaCobranca { get; set; } = string.Empty;

    // Datas do ciclo do título
    public string dataPagamento { get; set; } = string.Empty;   // = DataCredito do banco
    public string dataOcorrencia { get; set; } = string.Empty;
    public string dataBaixa { get; set; } = string.Empty;

    // Códigos da ocorrência
    public string codTipoOcorrencia { get; set; } = string.Empty;
    public string descricaoTipoOcorrencia { get; set; } = string.Empty;

    // Motivos de rejeição (quando situacao=Rejeitado) — uma string por bloco [MotivoRejeicaoN-K]
    public List<string>? motivosRejeicao { get; set; }

    public string codigoBarras { get; set; } = string.Empty;
    public string linhaDigitavel { get; set; } = string.Empty;
    public string urlBoleto { get; set; } = string.Empty;
    public string idBanco { get; set; } = string.Empty;
    public PixDados? pix { get; set; }

    // Raw JSON string exactly as returned by the bank (JSON= field from REGISTRO1).
    // GeneXus: LongVarChar — parse with JSONObject.FromString() when bank-specific fields are needed.
    public string rawJson { get; set; } = string.Empty;
}

public class ConsultaListaItem
{
    public string nossoNumero { get; set; } = string.Empty;
    public string situacao { get; set; } = string.Empty;
    public bool pago { get; set; }
    public string dataPagamento { get; set; } = string.Empty;
    public string valorPago { get; set; } = string.Empty;
    public string codigoBarras { get; set; } = string.Empty;
    public string linhaDigitavel { get; set; } = string.Empty;
}

public class ConsultaListaResponse : BaseResponse
{
    public List<ConsultaListaItem> itens { get; set; } = [];
}

public class ManutencaoResponse : BaseResponse
{
    public string nossoNumero { get; set; } = string.Empty;
    // Preenchidos quando a resposta da operação traz o boleto (pode vir vazio em alguns bancos/ops).
    public string codigoBarras { get; set; } = string.Empty;
    public string linhaDigitavel { get; set; } = string.Empty;
    public PixDados? pix { get; set; }
}

