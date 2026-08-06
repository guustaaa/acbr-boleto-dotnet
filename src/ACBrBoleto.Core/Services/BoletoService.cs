using System.Diagnostics;
using ACBrBoleto.Core.Enums;
using ACBrBoleto.Core.Interop;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Pool;
using Microsoft.Extensions.Logging;

namespace ACBrBoleto.Core.Services;

/// <summary>
/// Executa as operações ACBrLib usando o handle emprestado do pool.
/// Uma instância por operação — descartada no final (devolve o lease).
/// </summary>
public sealed class BoletoService : IDisposable
{
    private readonly AcbrLibLease _lease;
    private readonly int _configId;
    private readonly ILogger _logger;
    private bool _disposed;

    public BoletoService(AcbrLibLease lease, int configId, ILogger logger)
    {
        _lease = lease;
        _configId = configId;
        _logger = logger;
    }

    // ── Gerar / Enviar boleto ────────────────────────────────────────────────

    // Registra o boleto no banco. NÃO gera PDF — o handle fica quente para reuso. O documento é
    // gerado sob demanda, separadamente, via GerarPdf/GerarPdfBase64 (decisão da tela).
    public GerarBoletoResponse GerarBoleto(
        Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg, bool comPix)
    {
        return Executar<GerarBoletoResponse>("GerarBoleto", t.nossonumero, () =>
        {
            var h = _lease.Handle;

            var cedenteIni = StxSerializer.GerarCedenteIni(uni, cfg, "");
            var tituloIni = StxSerializer.GerarTituloIni(t, cli, cfg);

            _logger.LogDebug("[cfg={Id}] INI Cedente:\n{Ini}", _configId, cedenteIni);
            _logger.LogDebug("[cfg={Id}] INI Titulo:\n{Ini}", _configId, tituloIni);

            ConfigurarCedente(h, _configId, cedenteIni);
            h.LimparLista();
            h.IncluirTitulos(tituloIni);

            var retIni = h.EnviarBoleto(OperacaoBoleto.tpInclui);
            _logger.LogDebug("[cfg={Id}] Retorno EnviarBoleto:\n{Ret}", _configId, retIni);

            var dadosRetorno = StxSerializer.ParseRetornoEnviar(retIni);

            var r = BaseResponse.Ok<GerarBoletoResponse>("Boleto registrado com sucesso.");
            r.sucesso = dadosRetorno.Sucesso;
            if (!r.sucesso)
            {
                r.erro = dadosRetorno.Erro;
                r.mensagem = dadosRetorno.Mensagem;
            }

            r.nossoNumero = !string.IsNullOrWhiteSpace(dadosRetorno.NossoNumero) ? dadosRetorno.NossoNumero : t.nossonumero;
            r.codigoBarras = dadosRetorno.CodigoBarras;
            r.linhaDigitavel = dadosRetorno.LinhaDigitavel;
            r.urlBoleto = dadosRetorno.UrlBoleto;
            r.idBanco = dadosRetorno.IdBanco;

            if (r.sucesso && (!string.IsNullOrWhiteSpace(dadosRetorno.QrCodePixEmv) || !string.IsNullOrWhiteSpace(dadosRetorno.QrCodePixUrl)))
            {
                r.pix = new PixDados
                {
                    emv = dadosRetorno.QrCodePixEmv,
                    url = dadosRetorno.QrCodePixUrl,
                    txid = dadosRetorno.QrCodePixTxId
                };
            }

            return r;
        });
    }

    // ── Consulta detalhada ───────────────────────────────────────────────────

    public ConsultaDetalheResponse ConsultarDetalhe(Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg, bool incluirRawJson = true)
    {
        return Executar<ConsultaDetalheResponse>("ConsultarDetalhe", t.nossonumero, () =>
        {
            var h = _lease.Handle;
            var cedenteIni = StxSerializer.GerarCedenteIni(uni, cfg, "");
            var tituloIni = StxSerializer.GerarTituloIni(t, cli, cfg);
            _logger.LogDebug("[cfg={Id}] INI Cedente (Consulta):\n{Ini}", _configId, cedenteIni);
            _logger.LogDebug("[cfg={Id}] INI ConsultarDetalhe:\n{Ini}", _configId, tituloIni);
            ConfigurarCedente(h, _configId, cedenteIni);
            h.LimparLista();
            h.IncluirTitulos(tituloIni);
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpConsultaDetalhe);
            _logger.LogDebug("[cfg={Id}] Retorno ConsultarDetalhe:\n{Ret}", _configId, retIni);

            var r = BaseResponse.Ok<ConsultaDetalheResponse>("Consulta realizada com sucesso.");
            r.nossoNumero = t.nossonumero;

            var d = StxSerializer.ParseRetornoConsulta(retIni);
            r.situacao = d.Situacao;
            r.pago = d.Pago;
            r.dataPagamento = d.DataPagamento;
            r.dataOcorrencia = d.DataOcorrencia;
            r.dataBaixa = d.DataBaixa;
            r.valorPago = d.ValorPago;
            // Itaú boletoscash (e outros) não retornam o valor recebido quando o boleto é liquidado
            // via PIX/BoleCode. Quando o banco confirma a liquidação (pago) mas omite o valor, o
            // boleto não-parcial foi quitado integralmente: usamos o valor do documento para a
            // conciliação a jusante. Pagamentos parciais reportados pelo banco (>0) ficam intactos.
            if (r.pago && EhValorZeroOuVazio(r.valorPago))
                r.valorPago = t.valordocumento.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            r.valorJurosMora = d.ValorJurosMora;
            r.valorMulta = d.ValorMulta;
            r.valorDesconto = d.ValorDesconto;
            r.valorAbatimento = d.ValorAbatimento;
            r.valorIOF = d.ValorIOF;
            r.valorOutrasDespesas = d.ValorOutrasDespesas;
            r.valorOutrosCreditos = d.ValorOutrosCreditos;
            r.valorDespesaCobranca = d.ValorDespesaCobranca;
            r.codTipoOcorrencia = d.CodTipoOcorrencia;
            r.descricaoTipoOcorrencia = d.DescricaoTipoOcorrencia;
            r.motivosRejeicao = d.MotivosRejeicao;
            r.urlBoleto = d.UrlBoleto;
            r.idBanco = d.IdBanco;
            r.rawJson = incluirRawJson ? d.RawJson : string.Empty;

            // Barcode / linha: use dedicated functions (more reliable than INI key parsing)
            try { r.codigoBarras = h.RetornaCodigoBarras(1); } catch { }
            try { r.linhaDigitavel = h.RetornaLinhaDigitavel(1); } catch { }
            // Fall back to INI-parsed values if direct calls returned nothing
            if (string.IsNullOrWhiteSpace(r.codigoBarras)) r.codigoBarras = d.CodigoBarras;
            if (string.IsNullOrWhiteSpace(r.linhaDigitavel)) r.linhaDigitavel = d.LinhaDigitavel;

            if (!string.IsNullOrWhiteSpace(d.QrCodePixEmv) || !string.IsNullOrWhiteSpace(d.QrCodePixTxId) || !string.IsNullOrWhiteSpace(d.QrCodePixUrl))
            {
                r.pix = new PixDados
                {
                    url = d.QrCodePixUrl,
                    emv = d.QrCodePixEmv,
                    txid = d.QrCodePixTxId
                };
            }

            return r;
        });
    }

    // ── Consulta em lista ────────────────────────────────────────────────────

    public ConsultaListaResponse ConsultarLista(FiltroWS filtro, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ConsultaListaResponse>("ConsultarLista", "filtro", () =>
        {
            var h = _lease.Handle;
            var cedenteIni = StxSerializer.GerarCedenteIni(uni, cfg, "");
            _logger.LogDebug("[cfg={Id}] INI Cedente (ConsultarLista):\n{Ini}", _configId, cedenteIni);
            ConfigurarCedente(h, _configId, cedenteIni);
            var filtroIni = StxSerializer.GerarFiltroConsultaIni(filtro);
            _logger.LogDebug("[cfg={Id}] INI Filtro (ConsultarLista):\n{Ini}", _configId, filtroIni);
            var retIni = h.ConsultarTitulosPorPeriodo(filtroIni);

            // DLL returns code 0 even when bank HTTP call fails (e.g. HTTP 400).
            // The error response is written into UltimoRetorno as an INI with HTTPResultCode=4xx.
            var ultimoRetorno = h.UltimoRetorno();
            if (!string.IsNullOrWhiteSpace(ultimoRetorno))
            {
                _logger.LogWarning("[cfg={Id}] ConsultarLista UltimoRetorno: {Ret}", _configId, ultimoRetorno);
                var httpMatch = System.Text.RegularExpressions.Regex.Match(ultimoRetorno, @"HTTPResultCode=(\d+)");
                if (httpMatch.Success && int.TryParse(httpMatch.Groups[1].Value, out var httpCode) && httpCode >= 400)
                {
                    var msgMatch = System.Text.RegularExpressions.Regex.Match(ultimoRetorno, @"Mensagem=(.+)");
                    var bankMsg = msgMatch.Success ? msgMatch.Groups[1].Value.Trim() : string.Empty;
                    return BaseResponse.Falha<ConsultaListaResponse>(
                        string.IsNullOrWhiteSpace(bankMsg) ? $"Banco retornou HTTP {httpCode}" : $"[HTTP {httpCode}] {bankMsg}");
                }
            }

            var r = BaseResponse.Ok<ConsultaListaResponse>("Consulta de lista realizada com sucesso.");
            r.itens = StxSerializer.ParseListaConsulta(retIni);

            if (filtro.NossoNumeros?.Count > 0)
            {
                var allow = new HashSet<string>(filtro.NossoNumeros, StringComparer.OrdinalIgnoreCase);
                r.itens = r.itens.Where(i => allow.Contains(i.nossoNumero.Trim())).ToList();
            }

            return r;
        });
    }

    // ── Alteração (manutenção via WS) ────────────────────────────────────────

    public GerarPdfResponse AlterarVencimento(
        string nossoNumero, string novaData,
        Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<GerarPdfResponse>("AlterarVencimento", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            t.datavencimento = novaData;
            var tituloIni = StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=7\r\n";
            _logger.LogDebug("[cfg={Id}] INI Titulo (AlterarVencimento):\n{Ini}", _configId, tituloIni);
            h.LimparLista();
            h.IncluirTitulos(tituloIni);
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);
            _logger.LogDebug("[cfg={Id}] Retorno EnviarBoleto(tpAltera):\n{Ret}", _configId, retIni);

            var dadosRetorno = StxSerializer.ParseRetornoEnviar(retIni);

            var r = BaseResponse.Ok<GerarPdfResponse>("Vencimento alterado com sucesso.");
            r.sucesso = dadosRetorno.Sucesso;
            if (!r.sucesso)
            {
                r.erro = dadosRetorno.Erro;
                r.mensagem = dadosRetorno.Mensagem;
            }
            r.nossoNumero = nossoNumero;

            if (r.sucesso)
            {
                AtualizarCamposPosManutencao(h, r, nossoNumero, t, cli, uni, cfg);
            }

            return r;
        });
    }

    public GerarPdfResponse AlterarValor(
        string nossoNumero, decimal novoValor,
        Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<GerarPdfResponse>("AlterarValor", nossoNumero, () =>
        {
            var h = _lease.Handle;
            var cedenteIni = StxSerializer.GerarCedenteIni(uni, cfg, "");
            _logger.LogDebug("[cfg={Id}] INI Cedente (AlterarValor):\n{Ini}", _configId, cedenteIni);
            ConfigurarCedente(h, _configId, cedenteIni);
            t.valordocumento = novoValor;
            var novoValorFmt = novoValor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
            var tituloIni = StxSerializer.GerarTituloIni(t, cli, cfg) +
                           $"OcorrenciaOriginal.TipoOcorrencia=38\r\n" +
                           $"OcorrenciaOriginal.ValorDocumento={novoValorFmt}\r\n";
            _logger.LogDebug("[cfg={Id}] INI Titulo (AlterarValor):\n{Ini}", _configId, tituloIni);
            h.LimparLista();
            h.IncluirTitulos(tituloIni);
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);
            _logger.LogDebug("[cfg={Id}] Retorno EnviarBoleto(tpAltera):\n{Ret}", _configId, retIni);

            var dadosRetorno = StxSerializer.ParseRetornoEnviar(retIni);

            var r = BaseResponse.Ok<GerarPdfResponse>("Valor alterado com sucesso.");
            r.sucesso = dadosRetorno.Sucesso;
            if (!r.sucesso)
            {
                r.erro = dadosRetorno.Erro;
                r.mensagem = dadosRetorno.Mensagem;
            }
            r.nossoNumero = nossoNumero;

            if (r.sucesso)
            {
                AtualizarCamposPosManutencao(h, r, nossoNumero, t, cli, uni, cfg);
            }

            return r;
        });
    }

    public ManutencaoResponse Baixar(string nossoNumero, string? motivo, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("Baixar", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            var baixaIni = StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=1\r\n";
            if (!string.IsNullOrWhiteSpace(motivo)) baixaIni += $"MotivoBaixa={motivo.Trim()}\r\n";
            h.LimparLista();
            h.IncluirTitulos(baixaIni);
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpBaixa);

            return AplicarRetorno(nossoNumero, "Baixa solicitada com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    public ManutencaoResponse DebitarEmConta(string nossoNumero, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("DebitarEmConta", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            h.LimparLista();
            h.IncluirTitulos(StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=2\r\n");
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpBaixa);

            return AplicarRetorno(nossoNumero, "Débito em conta solicitado com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    public ManutencaoResponse ConcederAbatimento(string nossoNumero, decimal valorAbatimento, string? dataAbatimento, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("ConcederAbatimento", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            t.valorabatimento = valorAbatimento;
            if (!string.IsNullOrWhiteSpace(dataAbatimento)) t.dataabatimento = dataAbatimento;
            h.LimparLista();
            h.IncluirTitulos(StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=3\r\n");
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);

            return AplicarRetorno(nossoNumero, "Abatimento concedido com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    public ManutencaoResponse CancelarAbatimento(string nossoNumero, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("CancelarAbatimento", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            // ValorAbatimento=0,00 tells the bank to cancel. GerarTituloIni skips it when 0, so append explicitly.
            var cancelAbt = StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=4\r\nValorAbatimento=0,00\r\n";
            h.LimparLista();
            h.IncluirTitulos(cancelAbt);
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);

            return AplicarRetorno(nossoNumero, "Abatimento cancelado com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    public ManutencaoResponse ConcederDesconto(string nossoNumero, decimal valorDesconto, string? dataDesconto, int? tipoDesconto, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("ConcederDesconto", nossoNumero, () =>
        {
            var h = _lease.Handle;
            var cedenteIniDesc = StxSerializer.GerarCedenteIni(uni, cfg, "");
            _logger.LogDebug("[cfg={Id}] INI Cedente (ConcederDesconto):\n{Ini}", _configId, cedenteIniDesc);
            ConfigurarCedente(h, _configId, cedenteIniDesc);
            t.valordesconto = valorDesconto;
            if (!string.IsNullOrWhiteSpace(dataDesconto)) t.datadesconto = dataDesconto;
            if (tipoDesconto.HasValue) t.TipoDesconto = tipoDesconto.Value;
            // Tipos antecipatórios (3-6 / códigos 91-94 no Itaú) exigem exatamente 1 item em descontos[].
            // A DLL gera esse item a partir de DataDesconto. Se não foi informado, usa o vencimento como referência.
            var tipoFinal = t.TipoDesconto > 0 ? t.TipoDesconto : cfg.tipodesconto;
            if (tipoFinal is >= 3 and <= 6 && string.IsNullOrWhiteSpace(StxSerializer.NormalizarData(t.datadesconto)))
                t.datadesconto = t.datavencimento;
            var descontoIni = StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=5\r\n";
            _logger.LogDebug("[cfg={Id}] INI Titulo (ConcederDesconto):\n{Ini}", _configId, descontoIni);
            h.LimparLista();
            h.IncluirTitulos(descontoIni);
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);
            _logger.LogDebug("[cfg={Id}] Retorno EnviarBoleto(ConcederDesconto):\n{Ret}", _configId, retIni);

            return AplicarRetorno(nossoNumero, "Desconto concedido com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    public ManutencaoResponse CancelarDesconto(string nossoNumero, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("CancelarDesconto", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            // TipoDesconto=7 = CancelamentoDesconto per ACBrLib INI docs
            t.TipoDesconto = 7;
            t.valordesconto = 0m;
            h.LimparLista();
            h.IncluirTitulos(StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=6\r\n");
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);

            return AplicarRetorno(nossoNumero, "Desconto cancelado com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    public GerarPdfResponse AlterarVencimentoSustarProtesto(
        string nossoNumero, string novaData,
        Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<GerarPdfResponse>("AlterarVencimentoSustarProtesto", nossoNumero, () =>
        {
            var h = _lease.Handle;

            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            t.datavencimento = novaData;
            // DiasDeProtesto=0 signals sustar — GerarTituloIni skips it when 0, so append explicitly.
            h.LimparLista();
            h.IncluirTitulos(StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=8\r\nDiasDeProtesto=0\r\n");
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);
            _logger.LogDebug("[cfg={Id}] Retorno EnviarBoleto(tpAltera):\n{Ret}", _configId, retIni);

            var d = StxSerializer.ParseRetornoEnviar(retIni);
            var r = BaseResponse.Ok<GerarPdfResponse>("Vencimento alterado e protesto sustado com sucesso.");
            r.sucesso = d.Sucesso;
            if (!r.sucesso) { r.erro = d.Erro; r.mensagem = d.Mensagem; }
            r.nossoNumero = nossoNumero;

            if (r.sucesso)
            {
                AtualizarCamposPosManutencao(h, r, nossoNumero, t, cli, uni, cfg);
            }

            return r;
        });
    }

    public ManutencaoResponse Protestar(string nossoNumero, string? dataProtesto, int? codigoNegativacao, int? diasProtesto, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("Protestar", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            if (codigoNegativacao.HasValue) t.CodigoNegativacao = codigoNegativacao.Value;
            if (diasProtesto.HasValue) t.DiasProtesto = diasProtesto.Value;
            if (!string.IsNullOrWhiteSpace(dataProtesto)) t.dataprotesto = dataProtesto;
            h.LimparLista();
            h.IncluirTitulos(StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=9\r\n");
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);

            return AplicarRetorno(nossoNumero, "Protesto solicitado com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    public ManutencaoResponse SustarProtesto(string nossoNumero, Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<ManutencaoResponse>("SustarProtesto", nossoNumero, () =>
        {
            var h = _lease.Handle;
            ConfigurarCedente(h, _configId, StxSerializer.GerarCedenteIni(uni, cfg, ""));
            // DiasDeProtesto=0 signals sustar — GerarTituloIni skips it when 0, so append explicitly.
            h.LimparLista();
            h.IncluirTitulos(StxSerializer.GerarTituloIni(t, cli, cfg) + "OcorrenciaOriginal.TipoOcorrencia=10\r\nDiasDeProtesto=0\r\n");
            var retIni = h.EnviarBoleto(OperacaoBoleto.tpAltera);

            return AplicarRetorno(nossoNumero, "Sustar protesto solicitado com sucesso.", StxSerializer.ParseRetornoEnviar(retIni));
        });
    }

    // ── PDF avulso ───────────────────────────────────────────────────────────

    public GerarPdfResponse GerarPdf(Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<GerarPdfResponse>("GerarPdf", t.nossonumero, () =>
        {
            var h = _lease.Handle;
            var fileName = $"boleto_{t.nossonumero.Trim()}_{Guid.NewGuid().ToString("N")[..8]}.pdf";
            // Sem endereço do beneficiário: o motor FPDF concatena Nome+CNPJ+Logradouro na linha
            // "Beneficiário/CNPJ/CPF/Endereço" da ficha (célula fixa que corta a palavra). Só no PDF.
            var cedenteIni = StxSerializer.GerarCedenteIni(uni, cfg, fileName, incluirEndereco: false);
            var tituloIni = StxSerializer.GerarTituloIni(t, cli, cfg, uni);

            _logger.LogDebug("[cfg={Id}] INI Cedente (PDF):\n{Ini}", _configId, cedenteIni);
            _logger.LogDebug("[cfg={Id}] INI Titulo (PDF):\n{Ini}", _configId, tituloIni);

            ConfigurarCedente(h, _configId, cedenteIni);
            h.LimparLista();
            h.IncluirTitulos(tituloIni);

            var r = BaseResponse.Ok<GerarPdfResponse>("PDF gerado com sucesso.");
            r.nossoNumero = t.nossonumero;

            try { h.GerarPDFBoleto(1); }
            catch { h.GerarPDF(); }
            r.pdfPath = ResolverPdfPath(h, cfg, fileName);
            return r;
        });
    }

    public GerarPdfBase64Response GerarPdfBase64(Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        return Executar<GerarPdfBase64Response>("GerarPdfBase64", t.nossonumero, () =>
        {
            var h = _lease.Handle;
            var fileName = $"boleto_b64_{t.nossonumero.Trim()}_{Guid.NewGuid().ToString("N")[..8]}.pdf";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            // Pass full temp path so NomeArquivo in the INI points to %TEMP%
            // Path.Combine(pastaOutAbs, tempPath) → tempPath (rooted, wins on Windows)
            // incluirEndereco:false → mesma razão de GerarPdf (linha da ficha corta o endereço).
            var cedenteIni = StxSerializer.GerarCedenteIni(uni, cfg, tempPath, incluirEndereco: false);
            var tituloIni = StxSerializer.GerarTituloIni(t, cli, cfg, uni);

            ConfigurarCedente(h, _configId, cedenteIni);
            h.LimparLista();
            h.IncluirTitulos(tituloIni);

            var r = BaseResponse.Ok<GerarPdfBase64Response>("PDF gerado com sucesso.");
            r.nossoNumero = t.nossonumero;

            try { h.GerarPDFBoleto(1); }
            catch { h.GerarPDF(); }

            var path = ResolverPdfPath(h, cfg, Path.GetFileName(tempPath));
            if (File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                r.pdfBase64 = Convert.ToBase64String(bytes);
                try { File.Delete(path); } catch { }
            }
            else
            {
                r.sucesso = false;
                r.mensagem = "PDF Base64 não gerado: arquivo temporário não encontrado após chamada à ACBrLib.";
                r.erro = "[ACBrLib op=GerarPDFBoleto cod=0] Arquivo PDF não foi criado no caminho esperado.";
            }

            return r;
        });
    }

    // ── Helpers internos ─────────────────────────────────────────────────────

    // Após uma alteração (vencimento/valor/sustar), consulta o título para devolver os campos
    // ATUALIZADOS (código de barras / linha digitável / pix) — que mudam com a alteração e, em
    // bancos API, vêm do banco. NÃO gera PDF: o documento é gerado depois, sob demanda, via GerarPdf.
    private void AtualizarCamposPosManutencao(
        AcbrLibHandle h, GerarPdfResponse r, string nossoNumero,
        Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        h.LimparLista();
        h.IncluirTitulos(StxSerializer.GerarTituloIni(t, cli, cfg));
        var retCons = h.EnviarBoleto(OperacaoBoleto.tpConsultaDetalhe);
        _logger.LogDebug("[cfg={Id}] Retorno EnviarBoleto(tpConsultaDetalhe):\n{Ret}", _configId, retCons);

        var consDado = StxSerializer.ParseRetornoConsulta(retCons);

        r.codigoBarras = consDado.CodigoBarras;
        r.linhaDigitavel = consDado.LinhaDigitavel;
        r.rawJson = consDado.RawJson;

        if (!string.IsNullOrWhiteSpace(consDado.QrCodePixEmv) || !string.IsNullOrWhiteSpace(consDado.QrCodePixUrl))
        {
            r.pix = new PixDados
            {
                emv = consDado.QrCodePixEmv,
                url = consDado.QrCodePixUrl,
                txid = consDado.QrCodePixTxId,
                base64 = consDado.QrCodePixBase64
            };
            t.qrcodepix_emv = consDado.QrCodePixEmv;
            t.qrcodepix_url = consDado.QrCodePixUrl;
            t.qrcodepix_txid = consDado.QrCodePixTxId;
        }

        if (string.IsNullOrWhiteSpace(r.codigoBarras))
        {
            try { r.codigoBarras = h.RetornaCodigoBarras(1); } catch { }
        }
        if (string.IsNullOrWhiteSpace(r.linhaDigitavel))
        {
            try { r.linhaDigitavel = h.RetornaLinhaDigitavel(1); } catch { }
        }
    }

    private static bool EhValorZeroOuVazio(string valor) =>
        string.IsNullOrWhiteSpace(valor) ||
        (decimal.TryParse(valor, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v == 0m);

    private static ManutencaoResponse AplicarRetorno(string nossoNumero, string msgSucesso, StxSerializer.DadosRetorno d)
    {
        var r = BaseResponse.Ok<ManutencaoResponse>(msgSucesso);
        r.sucesso = d.Sucesso;
        if (!r.sucesso) { r.erro = d.Erro; r.mensagem = d.Mensagem; }
        r.nossoNumero = nossoNumero;

        // Surface o que a própria resposta da operação já trouxe (sem chamada extra ao banco).
        // Pode vir vazio em operações que o banco não ecoa o boleto — o lado GeneXus só deve
        // gravar quando não-vazio para não apagar um valor bom.
        r.codigoBarras = d.CodigoBarras;
        r.linhaDigitavel = d.LinhaDigitavel;
        if (!string.IsNullOrWhiteSpace(d.QrCodePixEmv) || !string.IsNullOrWhiteSpace(d.QrCodePixUrl))
        {
            r.pix = new PixDados
            {
                emv = d.QrCodePixEmv,
                url = d.QrCodePixUrl,
                txid = d.QrCodePixTxId
            };
        }
        return r;
    }

    private static void ConfigurarCedente(AcbrLibHandle h, int configId, string cedenteIni)
    {
        // Pool init already applies ConfigImportar (global settings). Per-operation we only
        // need to re-set [Cedente]/[Conta]/[Banco] via ConfigurarDados with inline content.
        // Calling ConfigImportar again on a reused handle after a failed EnviarBoleto causes -10.
        h.ConfigurarDados(cedenteIni);
    }

    // ConfigurarDados (por operação) NÃO aplica a seção global [BoletoBancoFCFortesConfig]: a DLL
    // grava o PDF no NomeArquivo definido na inicialização do pool (boleto_{cfg.id}_{guid}.pdf),
    // ignorando o nome por operação. NÃO podemos MUTAR a config para corrigir isso —
    // ConfigGravarValor reinicia o cedente (perde o Nome → "Nome do cedente não informado") e
    // arrisca a config de PDF (dimensões). Em vez disso lemos de volta (read-only) o caminho que a
    // DLL realmente usou, para que o pdfPath retornado bata com o arquivo gerado.
    private string ResolverPdfPath(AcbrLibHandle h, ConfigBoleto cfg, string fileNameFallback)
    {
        try
        {
            var nomeArq = h.ConfigLerValor("BoletoBancoFCFortesConfig", "NomeArquivo");
            if (!string.IsNullOrWhiteSpace(nomeArq) && File.Exists(nomeArq))
                return nomeArq;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[cfg={Id}] Falha ao ler NomeArquivo do PDF: {Msg}", _configId, ex.Message);
        }
        return Path.Combine(Path.GetFullPath(cfg.pastaOutput), fileNameFallback);
    }

    // ── Executor central ─────────────────────────────────────────────────────

    private T Executar<T>(string op, string ctx, Func<T> fn) where T : BaseResponse, new()
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("[cfg={Id}] → {Op} | {Ctx}", _configId, op, ctx);
        try
        {
            var r = fn();
            _logger.LogInformation("[cfg={Id}] ✓ {Op} | {Ctx} | {Ms}ms",
                _configId, op, ctx, sw.ElapsedMilliseconds);
            return r;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[cfg={Id}] ✗ {Op} | {Ctx} | {Ms}ms | {Msg}",
                _configId, op, ctx, sw.ElapsedMilliseconds, ex.Message);
            return BaseResponse.Falha<T>(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lease.Dispose();
    }
}