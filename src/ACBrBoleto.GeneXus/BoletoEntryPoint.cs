using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Services;
using ACBrBoleto.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace ACBrBoleto.GeneXus;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════════════════╗
/// ║  BoletoEntryPoint — Interface pública para GeneXus (External Object)        ║
/// ║                                                                              ║
/// ║  Arquitetura:                                                                ║
/// ║    GeneXus consulta o PostgreSQL → monta JSON → chama método da DLL         ║
/// ║    DLL chama ACBrLibBoleto nativa → retorna JSON → GeneXus persiste          ║
/// ║                                                                              ║
/// ║  Regras:                                                                     ║
/// ║    • Todos os métodos são ESTÁTICOS e SÍNCRONOS                              ║
/// ║    • Entrada e saída: strings JSON                                           ║
/// ║    • Nunca lançam exceções — erros ficam no campo "erro" do JSON             ║
/// ║    • A DLL NÃO acessa banco de dados diretamente                             ║
/// ╚══════════════════════════════════════════════════════════════════════════════╝
///
/// Mapeamento Swagger → método:
///   POST /boletos/gerar/{id}                        → GerarBoleto (registra; não gera PDF)
///   POST /boletos/consultawsdet/{id}                → ConsultarBoleto
///   POST /boletos/consultawslista/{config}          → ConsultarListaBoletos
///   POST /boletos/manutencao/vencimento             → AlterarVencimento
///   POST /boletos/manutencao/vencimento/sustar      → AlterarVencimentoSustarProtesto
///   POST /boletos/manutencao/valor                  → AlterarValor
///   POST /boletos/manutencao/baixar                 → BaixarBoleto
///   POST /boletos/manutencao/debitarconta           → DebitarEmConta
///   POST /boletos/manutencao/abatimento             → ConcederAbatimento
///   POST /boletos/manutencao/abatimento/cancelar    → CancelarAbatimento
///   POST /boletos/manutencao/desconto               → ConcederDesconto
///   POST /boletos/manutencao/desconto/cancelar      → CancelarDesconto
///   POST /boletos/manutencao/protestar              → Protestar
///   POST /boletos/manutencao/sustarprotesto         → SustarProtesto
///   POST /boletos/pdf                               → GerarPdf
///   POST /boletos/pdf/base64                        → GerarPdfBase64
/// </summary>
public static class BoletoEntryPoint
{
    private static readonly JsonSerializerOptions _j = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling              = JsonNumberHandling.AllowReadingFromString,
        Encoder                     = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters                  = { new TolerantIntConverter() }
    };

    private sealed class LogCategory { }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    public static string Inicializar(string nivelLog = "Information")
    {
        try
        {
            Bootstrapper.Inicializar(nivelLog);
            return Res.Ok("ACBrBoleto inicializado.").Ser(_j);
        }
        catch (Exception ex) { return Res.Falha(ex).Ser(_j); }
    }

    public static string Encerrar()
    {
        try
        {
            Bootstrapper.Encerrar();
            return Res.Ok("ACBrBoleto encerrado.").Ser(_j);
        }
        catch (Exception ex) { return Res.Falha(ex).Ser(_j); }
    }

    // ── Gerar / Enviar Boleto ─────────────────────────────────────────────────

    public static string GerarBoleto(
        string configJson, string tituloJson, string clienteJson, string unidadeJson)
        => Executar<GerarBoletoResponse>("GerarBoleto", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.GerarBoleto(t, cli, uni, cfg, comPix: cfg.indicadorpix > 0));

    // ── Consultas ─────────────────────────────────────────────────────────────

    public static string ConsultarBoleto(
        string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero,
        bool incluirRawJson = true)
        => Executar<ConsultaDetalheResponse>("ConsultarBoleto", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.ConsultarDetalhe(t, cli, uni, cfg, incluirRawJson));

    public static string ConsultarListaBoletos(string configJson, string unidadeJson, string filtroJson)
        => Executar<ConsultaListaResponse>("ConsultarLista", configJson, (svc, cfg) =>
        {
            var filtro = Des<FiltroWS>(filtroJson);
            var uni    = Des<Unidade>(unidadeJson);
            return svc.ConsultarLista(filtro, uni, cfg);
        });

    // ── Manutenção ────────────────────────────────────────────────────────────

    public static string AlterarVencimento(
        string configJson, string tituloJson, string clienteJson, string unidadeJson, string novaData)
        => Executar<GerarPdfResponse>("AlterarVencimento", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.AlterarVencimento(t.nossonumero, novaData, t, cli, uni, cfg));

    public static string AlterarValor(
        string configJson, string tituloJson, string clienteJson, string unidadeJson, string novoValor)
        => Executar<GerarPdfResponse>("AlterarValor", configJson, tituloJson, clienteJson, unidadeJson, (svc, t, cli, uni, cfg) =>
        {
            var val = ParsePositiveDecimal(novoValor, "novoValor");
            return svc.AlterarValor(t.nossonumero, val, t, cli, uni, cfg);
        });

    public static string BaixarBoleto(string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero, string? motivo = null)
        => Executar<ManutencaoResponse>("BaixarBoleto", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.Baixar(nossoNumero, motivo, t, cli, uni, cfg));

    public static string DebitarEmConta(string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero)
        => Executar<ManutencaoResponse>("DebitarEmConta", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.DebitarEmConta(nossoNumero, t, cli, uni, cfg));

    public static string ConcederAbatimento(string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero, string valorAbatimento, string? dataAbatimento = null)
        => Executar<ManutencaoResponse>("ConcederAbatimento", configJson, tituloJson, clienteJson, unidadeJson, (svc, t, cli, uni, cfg) =>
        {
            var val = ParsePositiveDecimal(valorAbatimento, "valorAbatimento");
            return svc.ConcederAbatimento(nossoNumero, val, dataAbatimento, t, cli, uni, cfg);
        });

    public static string CancelarAbatimento(string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero)
        => Executar<ManutencaoResponse>("CancelarAbatimento", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.CancelarAbatimento(nossoNumero, t, cli, uni, cfg));

    public static string ConcederDesconto(
        string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero, string valorDesconto,
        string? dataDesconto = null, string? tipoDesconto = null)
        => Executar<ManutencaoResponse>("ConcederDesconto", configJson, tituloJson, clienteJson, unidadeJson, (svc, t, cli, uni, cfg) =>
        {
            var val = ParsePositiveDecimal(valorDesconto, "valorDesconto");
            int? tipo = null;
            if (!string.IsNullOrWhiteSpace(tipoDesconto) && int.TryParse(tipoDesconto, out var t2)) tipo = t2;
            return svc.ConcederDesconto(nossoNumero, val, dataDesconto, tipo, t, cli, uni, cfg);
        });

    public static string CancelarDesconto(string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero)
        => Executar<ManutencaoResponse>("CancelarDesconto", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.CancelarDesconto(nossoNumero, t, cli, uni, cfg));

    public static string AlterarVencimentoSustarProtesto(
        string configJson, string tituloJson, string clienteJson, string unidadeJson, string novaData)
        => Executar<GerarPdfResponse>("AlterarVencimentoSustarProtesto", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.AlterarVencimentoSustarProtesto(t.nossonumero, novaData, t, cli, uni, cfg));

    public static string Protestar(
        string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero,
        string? dataProtesto = null, string? codigoNegativacao = null, string? diasProtesto = null)
        => Executar<ManutencaoResponse>("Protestar", configJson, tituloJson, clienteJson, unidadeJson, (svc, t, cli, uni, cfg) =>
        {
            int? codNeg = null;
            if (!string.IsNullOrWhiteSpace(codigoNegativacao) && int.TryParse(codigoNegativacao, out var c)) codNeg = c;
            int? dias = null;
            if (!string.IsNullOrWhiteSpace(diasProtesto) && int.TryParse(diasProtesto, out var d)) dias = d;
            return svc.Protestar(nossoNumero, dataProtesto, codNeg, dias, t, cli, uni, cfg);
        });

    public static string SustarProtesto(string configJson, string tituloJson, string clienteJson, string unidadeJson, string nossoNumero)
        => Executar<ManutencaoResponse>("SustarProtesto", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.SustarProtesto(nossoNumero, t, cli, uni, cfg));

    // ── PDF ───────────────────────────────────────────────────────────────────

    public static string GerarPdf(
        string configJson, string tituloJson, string clienteJson, string unidadeJson)
        => Executar<GerarPdfResponse>("GerarPdf", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.GerarPdf(t, cli, uni, cfg));

    public static string GerarPdfBase64(
        string configJson, string tituloJson, string clienteJson, string unidadeJson)
        => Executar<GerarPdfBase64Response>("GerarPdfBase64", configJson, tituloJson, clienteJson, unidadeJson,
            (svc, t, cli, uni, cfg) => svc.GerarPdfBase64(t, cli, uni, cfg));

    // ═══════════════════════════════════════════════════════════════════════════
    //  EXECUTORES CENTRAIS
    // ═══════════════════════════════════════════════════════════════════════════

    private static string Executar<TResponse>(
        string op,
        string cfgJson, string titJson, string cliJson, string uniJson,
        Func<BoletoService, Titulo, Cliente, Unidade, ConfigBoleto, TResponse> fn) where TResponse : BaseResponse, new()
    {
        Bootstrapper.EnsureInicializado();
        var log = Bootstrapper.GetLogger<LogCategory>();
        var sw  = Stopwatch.StartNew();
        log.LogInformation("[GX→] {Op}", op);
        try
        {
            var cfg = Des<ConfigBoleto>(cfgJson);
            var t   = Des<Titulo>(titJson);
            var cli = Des<Cliente>(cliJson);
            var uni = Des<Unidade>(uniJson);

            if (cfg.id <= 0) throw new ArgumentException("ConfigBoleto inválido: 'id' não informado.");
            if (string.IsNullOrWhiteSpace(t.nossonumero) && op != "GerarBoleto") 
                throw new ArgumentException("Titulo inválido: 'nossonumero' não informado.");

            var lease = Bootstrapper.GetPool()
                .AlugarAsync(cfg, CancellationToken.None)
                .GetAwaiter().GetResult();

            using var svc = new BoletoService(lease, cfg.id, log);
            var r = fn(svc, t, cli, uni, cfg);

            log.LogInformation("[GX←] {Op} | sucesso={S} | {Ms}ms", op, r.sucesso, sw.ElapsedMilliseconds);
            return r.Ser(_j);
        }
        catch (Exception ex)
        {
            if (ex is AcbrLibException) LogDiagHint(log);
            log.LogError(ex, "[GX✗] {Op} | {Ms}ms | {Msg}", op, sw.ElapsedMilliseconds, ex.Message);
            return BaseResponse.Falha<TResponse>(ex).Ser(_j);
        }
    }

    private static string Executar<TResponse>(
        string op,
        string cfgJson,
        Func<BoletoService, ConfigBoleto, TResponse> fn) where TResponse : BaseResponse, new()
    {
        Bootstrapper.EnsureInicializado();
        var log = Bootstrapper.GetLogger<LogCategory>();
        var sw  = Stopwatch.StartNew();
        log.LogInformation("[GX→] {Op}", op);
        try
        {
            var cfg   = Des<ConfigBoleto>(cfgJson);
            if (cfg.id <= 0) throw new ArgumentException("ConfigBoleto inválido: 'id' não informado.");
            var lease = Bootstrapper.GetPool()
                .AlugarAsync(cfg, CancellationToken.None)
                .GetAwaiter().GetResult();

            using var svc = new BoletoService(lease, cfg.id, log);
            var r = fn(svc, cfg);

            log.LogInformation("[GX←] {Op} | sucesso={S} | {Ms}ms", op, r.sucesso, sw.ElapsedMilliseconds);
            return r.Ser(_j);
        }
        catch (Exception ex)
        {
            if (ex is AcbrLibException) LogDiagHint(log);
            log.LogError(ex, "[GX✗] {Op} | {Ms}ms | {Msg}", op, sw.ElapsedMilliseconds, ex.Message);
            return BaseResponse.Falha<TResponse>(ex).Ser(_j);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void LogDiagHint(ILogger log) =>
        log.LogDebug("[Diag] Erro ACBrLib detectado. Verifique os arquivos INI em '{Dir}' para depurar os campos enviados.",
            Path.Combine(Path.GetTempPath(), "acbr_ini"));

    public static decimal ParsePositiveDecimal(string value, string fieldName)
    {
        if (!decimal.TryParse(value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val) || val <= 0)
            throw new ArgumentException($"{fieldName} inválido: '{value}'.");
        return val;
    }

    private static T Des<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            throw new ArgumentException($"JSON de {typeof(T).Name} está vazio ou inválido.");
        return JsonSerializer.Deserialize<T>(json, _j)
            ?? throw new ArgumentException($"JSON de {typeof(T).Name} retornou null.");
    }
}

// ── Helpers de resposta ────────────────────────────────────────────────────────

internal static class Res
{
    internal static OperacaoSimplesResponse Ok(string msg) =>
        BaseResponse.Ok<OperacaoSimplesResponse>(msg);

    internal static OperacaoSimplesResponse Falha(Exception ex) =>
        BaseResponse.Falha<OperacaoSimplesResponse>(ex);
}

internal static class ResultadoExt
{
    internal static string Ser<T>(this T r, JsonSerializerOptions opts) where T : BaseResponse =>
        JsonSerializer.Serialize(r, opts);
}
