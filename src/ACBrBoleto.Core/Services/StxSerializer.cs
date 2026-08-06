using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ACBrBoleto.Core.Models;

namespace ACBrBoleto.Core.Services;

/// <summary>
/// Converte os modelos de integração para o formato INI da ACBrLibBoleto.
/// </summary>
internal static class StxSerializer
{
    // ── Geração de INI para ConfigurarDados ──────────────────────────────────

    public static string GerarCedenteIni(Unidade uni, ConfigBoleto cfg, string? fileName = null, bool incluirEndereco = true)
    {
        ValidarConfigPix(cfg);

        var sb = new StringBuilder();
        var cpfCnpjLimpo = LimparMascaras(uni.cpfcnpj);
        var tipoReal = MapearTipoPessoa(uni.tipo, cpfCnpjLimpo);

        sb.AppendLine("[Cedente]");
        sb.AppendLine($"Nome={uni.nome.Trim()}");
        sb.AppendLine($"CNPJCPF={cpfCnpjLimpo}");
        if (incluirEndereco)
        {
            sb.AppendLine($"Logradouro={uni.logradouro.Trim()}");
            sb.AppendLine($"Numero={uni.numero.Trim()}");
            sb.AppendLine($"Complemento={uni.complemento.Trim()}");
            sb.AppendLine($"Bairro={uni.bairro.Trim()}");
            sb.AppendLine($"Cidade={uni.cidade.Trim()}");
            sb.AppendLine($"UF={uni.uf.Trim()}");
            sb.AppendLine($"CEP={LimparMascaras(uni.cep)}");
        }
        sb.AppendLine($"TipoPessoa={tipoReal}");
        sb.AppendLine($"TipoInscricao={tipoReal}");
        sb.AppendLine($"CodigoCedente={cfg.codigocedente}");
        sb.AppendLine($"Carteira={cfg.carteira.Trim()}");
        sb.AppendLine($"CaracTitulo={cfg.carac_titulo}");
        sb.AppendLine($"TipoCarteira={cfg.tipo_carteira}");
        sb.AppendLine($"TipoDocumento={cfg.tipodocumento}");
        sb.AppendLine($"Modalidade={cfg.modalidade}");
        sb.AppendLine($"Convenio={cfg.convenio.Trim()}");
        sb.AppendLine($"RespEmis={cfg.resp_emissao}");
        if (!string.IsNullOrWhiteSpace(cfg.codigodetransmissao))
            sb.AppendLine($"CodTransmissao={cfg.codigodetransmissao.Trim()}");
        if (cfg.indicadorpix > 0)
        {
            sb.AppendLine($"PIX.TipoChavePix={cfg.tipochavepix}");
            sb.AppendLine($"PIX.Chave={cfg.chavepix.Trim()}");
        }
        sb.AppendLine();

        AppendContaBancoWebservice(sb, cfg, fileName);
        return sb.ToString();
    }

    public static string GerarCedenteIniMinimal(ConfigBoleto cfg, string? fileName = null)
    {
        ValidarConfigPix(cfg);

        var sb = new StringBuilder();

        sb.AppendLine("[Cedente]");
        sb.AppendLine($"CodigoCedente={cfg.codigocedente}");
        sb.AppendLine($"Carteira={cfg.carteira.Trim()}");
        sb.AppendLine($"CaracTitulo={cfg.carac_titulo}");
        sb.AppendLine($"TipoCarteira={cfg.tipo_carteira}");
        sb.AppendLine($"Modalidade={cfg.modalidade}");
        sb.AppendLine($"Convenio={cfg.convenio.Trim()}");
        if (cfg.indicadorpix > 0)
        {
            sb.AppendLine($"PIX.TipoChavePix={cfg.tipochavepix}");
            sb.AppendLine($"PIX.Chave={cfg.chavepix.Trim()}");
        }
        sb.AppendLine();

        AppendContaBancoWebservice(sb, cfg, fileName);
        return sb.ToString();
    }

    private static void AppendContaBancoWebservice(StringBuilder sb, ConfigBoleto cfg, string? fileName = null)
    {
        sb.AppendLine("[Conta]");
        sb.AppendLine($"Conta={cfg.conta.Trim()}");
        sb.AppendLine($"DigitoConta={cfg.contadig.Trim()}");
        sb.AppendLine($"Agencia={cfg.agencia.Trim()}");
        sb.AppendLine($"DigitoAgencia={cfg.agenciadig.Trim()}");
        sb.AppendLine();

        sb.AppendLine("[Banco]");
        sb.AppendLine($"Numero={cfg.codbanco}");
        sb.AppendLine($"CNAB=1");
        sb.AppendLine($"TipoCobranca={cfg.tipocobranca}");
        if (!string.IsNullOrWhiteSpace(cfg.layoutversaolote))
            sb.AppendLine($"VersaoLote={cfg.layoutversaolote.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.layoutversaoarquivo))
            sb.AppendLine($"VersaoArquivo={cfg.layoutversaoarquivo.Trim()}");
        if (!string.IsNullOrWhiteSpace(cfg.keysoftwarehouse))
            sb.AppendLine($"KeySoftwareHouse={cfg.keysoftwarehouse.Trim()}");
        sb.AppendLine();

        sb.AppendLine("[DFe]");
        sb.AppendLine("SSLHttpLib=0");
        sb.AppendLine();

        // Native section for API Credentials
        sb.AppendLine("[BoletoCedenteWS]");
        sb.AppendLine($"ClientID={cfg.ws_clientid.Trim()}");
        sb.AppendLine($"ClientSecret={cfg.ws_clientsecret.Trim()}");
        sb.AppendLine($"KeyUser={cfg.ws_keyuser.Trim()}");
        sb.AppendLine($"Scope={cfg.ws_scope.Trim()}");
        sb.AppendLine($"IndicadorPix={cfg.indicadorpix}");
        sb.AppendLine();

        // Native section for API Engine Settings (Note the hardcoded typo in "Sevice")
        sb.AppendLine("[BoletoWebSevice]");
        sb.AppendLine($"Ambiente={cfg.ws_ambiente}");
        // ACBr compara VersaoDF de forma case-sensitive (= 'V1'/'V2'). Minúsculo ('v1') faz o
        // TBoletoW_Santander anexar DV ao nosso número no código de barras, divergindo da linha
        // digitável/banco. Normaliza para maiúsculo (forma canônica do ACBr).
        if (!string.IsNullOrWhiteSpace(cfg.VersaoDF))
            sb.AppendLine($"VersaoDF={cfg.VersaoDF.Trim().ToUpperInvariant()}");
        sb.AppendLine($"UseCertificateHTTP={cfg.usecertificatehttp}");
        if (cfg.usecertificatehttp == 1)
        {
            sb.AppendLine($"ArquivoCRT={cfg.arquivocrt.Trim()}");
            sb.AppendLine($"ArquivoKEY={cfg.arquivokey.Trim()}");
            sb.AppendLine("SSLType=5");
        }
        sb.AppendLine();

        // Configuração de PDF (Fortes Report). Defaults calibrados no Windows; sob render GTK/Linux
        // o layout difere e pode precisar recalibração. Para ajustar SEM rebuild, exporte
        // ACBR_FORTES_OVERRIDE com pares chave=valor (separados por ';' ou nova linha), ex.:
        //   ACBR_FORTES_OVERRIDE="NovaEscala=120;MargemEsquerda=8;MargemSuperior=2"
        // Sobrescreve (ou adiciona) qualquer chave da seção. Lido a cada geração; muda com restart.
        sb.AppendLine("[BoletoBancoFCFortesConfig]");
        var fortes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Filtro"] = "1",                                       // 1 = PDF
            ["Layout"] = (cfg.indicadorpix > 0 ? 8 : 0).ToString(), // 8 = PadraoPIX, 0 = Padrão (sem PIX). NÃO usar 1=Carnê: coluna estreita quebra os campos Beneficiário/CNPJ/Endereço caractere a caractere
            ["MostrarPreview"] = "0",
            ["MostrarProgresso"] = "0",
            ["MostrarSetup"] = "0",
            ["AlterarEscalaPadrao"] = "1",
            ["NovaEscala"] = "94",
            ["MargemEsquerda"] = "5",
            ["MargemDireita"] = "3",
            ["MargemSuperior"] = "1",
            ["MargemInferior"] = "1",
        };
        AplicarFortesOverride(fortes);
        foreach (var kv in fortes)
            sb.AppendLine($"{kv.Key}={kv.Value}");

        if (!string.IsNullOrWhiteSpace(cfg.dirLogo))
            sb.AppendLine($"DirLogo={cfg.dirLogo.Trim()}");

        if (!string.IsNullOrWhiteSpace(cfg.pastaOutput))
        {
            var pastaOutAbs = Path.GetFullPath(cfg.pastaOutput);
            // null → unique name per boleto; "" → maintenance callers (no PDF, DLL ignores folder-only path)
            var pdfFile = fileName ?? $"boleto_{cfg.id}_{Guid.NewGuid():N}.pdf";
            sb.AppendLine($"NomeArquivo={Path.Combine(pastaOutAbs, pdfFile)}");
        }
        sb.AppendLine();

        // Seção [Arquivos] para log de requisições/respostas WS da ACBrLib
        if (!string.IsNullOrWhiteSpace(cfg.pastaLog))
        {
            var pastaLogAbs = Path.GetFullPath(cfg.pastaLog);
            sb.AppendLine("[Arquivos]");
            sb.AppendLine($"LogNivel={cfg.nivelLog}");
            sb.AppendLine($"PathGravarRegistro={pastaLogAbs}{Path.DirectorySeparatorChar}");
            sb.AppendLine($"NomeArquivoLog=ws_cfg{cfg.id}.log");
            sb.AppendLine();
        }
    }

    // Overlay opcional da seção Fortes via env ACBR_FORTES_OVERRIDE (pares chave=valor separados
    // por ';' ou nova linha). Sobrescreve defaults e permite adicionar chaves novas (ex.:
    // CalcularNomeArquivoPDFIndividual). Lido a cada geração de INI; mudança exige restart do pod.
    private static void AplicarFortesOverride(IDictionary<string, string> fortes)
    {
        var raw = Environment.GetEnvironmentVariable("ACBR_FORTES_OVERRIDE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        foreach (var par in raw.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=');
            if (i <= 0) continue;
            var chave = par[..i].Trim();
            var valor = par[(i + 1)..].Trim();
            if (chave.Length > 0) fortes[chave] = valor;
        }
    }

    // ── Geração de INI para IncluirTitulos ───────────────────────────────────

    public static string GerarTituloIni(Titulo t, Cliente cli, ConfigBoleto cfg, Unidade? uni = null)
    {
        int tipoDesc = t.TipoDesconto > 0 ? t.TipoDesconto : cfg.tipodesconto;
        int codJuros = t.CodigoMoraJuros > 0 ? t.CodigoMoraJuros : cfg.codigomorajuros;
        int codMulta = t.CodigoMulta > 0 ? t.CodigoMulta : cfg.codigomulta;
        int codNegativ = t.CodigoNegativacao > 0 ? t.CodigoNegativacao : cfg.codigonegativacao;
        int diasProt = t.DiasProtesto > 0 ? t.DiasProtesto : cfg.diasprotesto;
        var aceite = cfg.aceite == 1 ? "A" : "N";
        var especie = string.IsNullOrWhiteSpace(cfg.especiedoc) ? "DM" : cfg.especiedoc.Trim();
        var cliCpfCnpj = LimparMascaras(cli.cpfcnpj);
        var cliTipo = MapearTipoPessoa(cli.tipo, cliCpfCnpj);

        var sb = new StringBuilder();
        sb.AppendLine("[Titulo1]");
        sb.AppendLine($"NossoNumero={t.nossonumero.Trim()}");
        if (!string.IsNullOrWhiteSpace(t.nossonumerocorrespondente))
            sb.AppendLine($"NossoNumeroCorrespondente={t.nossonumerocorrespondente.Trim()}");
        sb.AppendLine($"SeuNumero={t.numerodocumento.Trim()}");
        sb.AppendLine($"NumeroDocumento={t.numerodocumento.Trim()}");
        sb.AppendLine($"Carteira={cfg.carteira.Trim()}");
        sb.AppendLine($"ValorDocumento={FormatDecimal(t.valordocumento)}");
        sb.AppendLine($"Vencimento={NormalizarData(t.datavencimento)}");
        sb.AppendLine($"DataDocumento={NormalizarData(t.datadocumento)}");
        sb.AppendLine($"DataProcessamento={NormalizarData(t.dataprocessamento)}");
        sb.AppendLine($"Especie={especie}");
        sb.AppendLine($"Aceite={aceite}");
        sb.AppendLine($"Parcela=1");
        sb.AppendLine($"TotalParcelas=1");

        sb.AppendLine($"TipoDesconto={tipoDesc}");
        sb.AppendLine($"ValorDesconto={FormatDecimal(t.valordesconto)}");
        var dataDesc = NormalizarData(t.datadesconto);
        if (!string.IsNullOrWhiteSpace(dataDesc))
            sb.AppendLine($"DataDesconto={dataDesc}");

        sb.AppendLine($"CodigoMoraJuros={codJuros}");
        sb.AppendLine($"ValorMoraJuros={FormatDecimal(t.valormorajuros)}");
        var dataMora = NormalizarData(t.datamorajuros);
        if (!string.IsNullOrWhiteSpace(dataMora))
            sb.AppendLine($"DataMoraJuros={dataMora}");

        sb.AppendLine($"CodigoMulta={codMulta}");
        sb.AppendLine($"PercentualMulta={FormatDecimal(t.percentualmulta)}");

        if (t.valorabatimento > 0)
        {
            sb.AppendLine($"ValorAbatimento={FormatDecimal(t.valorabatimento)}");
            var dataAbt = NormalizarData(t.dataabatimento);
            if (!string.IsNullOrWhiteSpace(dataAbt))
                sb.AppendLine($"DataAbatimento={dataAbt}");
        }

        if (codNegativ > 0)
        {
            sb.AppendLine($"CodigoNegativacao={codNegativ}");
            if (codNegativ is 1 or 2 && diasProt > 0)
                sb.AppendLine($"DiasDeProtesto={diasProt}");
            else if (codNegativ is 4 && diasProt > 0)
                sb.AppendLine($"DiasDeNegativacao={diasProt}");
        }
        var dataProt = NormalizarData(t.dataprotesto);
        if (!string.IsNullOrWhiteSpace(dataProt))
            sb.AppendLine($"DataProtesto={dataProt}");

        // Instrução CNAB: título sobrescreve a config quando > 0
        var instr1 = t.instrucao1 > 0 ? t.instrucao1 : cfg.instrucao1;
        var instr2 = t.instrucao2 > 0 ? t.instrucao2 : cfg.instrucao2;
        if (instr1 > 0) sb.AppendLine($"Instrucao1={instr1}");
        if (instr2 > 0) sb.AppendLine($"Instrucao2={instr2}");

        if (!string.IsNullOrWhiteSpace(cfg.localpagamento))
            sb.AppendLine($"LocalPagamento={cfg.localpagamento.Trim()}");

        // Mensagem/Informativo: cfg (padrão unidade) | titulo (específico do título)
        var mensagem = CombinarTexto(cfg.mensagem, t.mensagem);
        var informativo = CombinarTexto(cfg.informativo, t.informativo);
        if (!string.IsNullOrWhiteSpace(mensagem)) sb.AppendLine($"Mensagem={mensagem}");
        if (!string.IsNullOrWhiteSpace(informativo)) sb.AppendLine($"Informativo={informativo}");

        // Logo da empresa (cedente): replica a da unidade no título quando informada.
        // Só tem efeito na renderização do PDF (fatura/recibo PIX); ignorado pelas demais operações.
        if (!string.IsNullOrWhiteSpace(uni?.arquivologoemp))
            sb.AppendLine($"ArquivoLogoEmp={uni.arquivologoemp.Trim()}");

        if (!string.IsNullOrWhiteSpace(t.qrcodepix_emv))
            sb.AppendLine($"QrCode.emv={t.qrcodepix_emv.Replace("\r", "").Replace("\n", "")}");
        if (!string.IsNullOrWhiteSpace(t.qrcodepix_url))
            sb.AppendLine($"QrCode.url={t.qrcodepix_url.Replace("\r", "").Replace("\n", "")}");
        if (!string.IsNullOrWhiteSpace(t.qrcodepix_txid))
            sb.AppendLine($"QrCode.txid={t.qrcodepix_txid.Replace("\r", "").Replace("\n", "")}");

        sb.AppendLine($"Sacado.NomeSacado={cli.nome.Trim()}");
        sb.AppendLine($"Sacado.CNPJCPF={cliCpfCnpj}");
        sb.AppendLine($"Sacado.Pessoa={cliTipo}");
        sb.AppendLine($"Sacado.Logradouro={cli.logradouro.Trim()}");
        sb.AppendLine($"Sacado.Numero={cli.numero.Trim()}");
        sb.AppendLine($"Sacado.Complemento={cli.complemento.Trim()}");
        sb.AppendLine($"Sacado.Bairro={cli.bairro.Trim()}");
        sb.AppendLine($"Sacado.Cidade={cli.cidade.Trim()}");
        sb.AppendLine($"Sacado.UF={cli.uf.Trim()}");
        sb.AppendLine($"Sacado.CEP={LimparMascaras(cli.cep)}");
        sb.AppendLine($"Sacado.Email={cli.email.Trim()}");

        return sb.ToString();
    }

    public static string GerarTituloIniMinimal(
        string nossoNumero,
        string? carteira = null,
        string? novaData = null,
        decimal? novoValor = null,
        string? motivo = null,
        decimal? valorAbatimento = null,
        string? dataAbatimento = null,
        decimal? valorDesconto = null,
        string? dataDesconto = null,
        int? tipoDesconto = null,
        string? dataProtesto = null,
        int? codigoNegativacao = null,
        int? diasProtesto = null,
        Cliente? cli = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Titulo1]");
        sb.AppendLine($"NossoNumero={nossoNumero.Trim()}");
        if (!string.IsNullOrWhiteSpace(carteira))
            sb.AppendLine($"Carteira={carteira.Trim()}");
        sb.AppendLine(!novoValor.HasValue
            ? "ValorDocumento=0,01"
            : $"ValorDocumento={FormatDecimal(novoValor.Value)}");
        sb.AppendLine(string.IsNullOrWhiteSpace(novaData)
            ? "Vencimento=30/12/1899"
            : $"Vencimento={NormalizarData(novaData)}");
        // DLL rejeita Sacado.CNPJCPF=00000000000000 com código -10. Usar dados reais do cliente.
        if (cli is not null)
        {
            var cpfCnpj = LimparMascaras(cli.cpfcnpj);
            sb.AppendLine($"Sacado.NomeSacado={cli.nome.Trim()}");
            sb.AppendLine($"Sacado.CNPJCPF={cpfCnpj}");
            sb.AppendLine($"Sacado.Pessoa={MapearTipoPessoa(cli.tipo, cpfCnpj)}");
        }
        else
        {
            sb.AppendLine("Sacado.NomeSacado=.");
            sb.AppendLine("Sacado.CNPJCPF=00000000000000");
            sb.AppendLine("Sacado.Pessoa=1");
        }

        if (!string.IsNullOrWhiteSpace(motivo))
            sb.AppendLine($"MotivoBaixa={motivo.Trim()}");

        if (valorAbatimento.HasValue)
            sb.AppendLine($"ValorAbatimento={FormatDecimal(valorAbatimento.Value)}");

        if (!string.IsNullOrWhiteSpace(dataAbatimento))
            sb.AppendLine($"DataAbatimento={NormalizarData(dataAbatimento)}");

        if (tipoDesconto.HasValue)
            sb.AppendLine($"TipoDesconto={tipoDesconto.Value}");

        if (valorDesconto.HasValue)
            sb.AppendLine($"ValorDesconto={FormatDecimal(valorDesconto.Value)}");

        if (!string.IsNullOrWhiteSpace(dataDesconto))
            sb.AppendLine($"DataDesconto={NormalizarData(dataDesconto)}");

        if (!string.IsNullOrWhiteSpace(dataProtesto))
            sb.AppendLine($"DataProtesto={NormalizarData(dataProtesto)}");

        if (codigoNegativacao.HasValue)
            sb.AppendLine($"CodigoNegativacao={codigoNegativacao.Value}");

        if (diasProtesto.HasValue)
            sb.AppendLine($"DiasDeProtesto={diasProtesto.Value}");

        return sb.ToString();
    }

    // ── Geração de INI para ConsultarTitulosPorPeriodo ───────────────────────

    public static string GerarFiltroConsultaIni(FiltroWS filtro)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[ConsultaAPI]");

        // All fields are always written (even empty) — the DLL uses presence/absence of values
        // to determine query mode and URL construction. Omitting fields causes wrong routing.
        sb.AppendLine($"IndicadorSituacaoBoleto={filtro.IndicadorSituacaoBoleto ?? 0}");
        sb.AppendLine($"BoletoVencido={filtro.BoletoVencido?.ToString() ?? ""}");
        sb.AppendLine($"DataInicioMovimento={NormalizarData(filtro.MovimentoDtIni)}");
        sb.AppendLine($"DataInicioVencimento={NormalizarData(filtro.VencimentoDtIni)}");
        sb.AppendLine($"DataInicioRegistro={NormalizarData(filtro.RegistroDtIni)}");
        sb.AppendLine($"DataFinalMovimento={NormalizarData(filtro.MovimentoDtFim)}");
        sb.AppendLine($"DataFinalVencimento={NormalizarData(filtro.VencimentoDtFim)}");
        sb.AppendLine($"DataFinalRegistro={NormalizarData(filtro.RegistroDtFim)}");
        sb.AppendLine($"cnpjCpfPagador={LimparMascaras(filtro.CnpjPagador)}");
        sb.AppendLine($"ContaCaucao={filtro.ContaCaucao?.ToString() ?? ""}");
        sb.AppendLine($"CodigoEstadoTituloCobranca={filtro.CodigoEstadoTituloCobranca?.ToString() ?? ""}");
        sb.AppendLine($"ModalidadeCobranca={filtro.ModalidadeCobranca?.ToString() ?? ""}");
        sb.AppendLine($"Carteira={filtro.Carteira?.ToString() ?? ""}");
        sb.AppendLine($"CarteiraVariacao={filtro.CarteiraVariacao?.ToString() ?? ""}");
        sb.AppendLine($"IndiceContinuidade={filtro.IndiceContinuidade ?? ""}");
        sb.AppendLine($"NumeroProtocolo={filtro.NumeroProtocolo ?? ""}");
        sb.AppendLine($"Identificador={filtro.Identificador ?? ""}");

        return sb.ToString();
    }

    public class DadosRetorno
    {
        public bool Sucesso { get; set; } = true;
        public string Mensagem { get; set; } = string.Empty;
        public string Erro { get; set; } = string.Empty;

        public string NossoNumero { get; set; } = string.Empty;
        public string CodigoBarras { get; set; } = string.Empty;
        public string LinhaDigitavel { get; set; } = string.Empty;
        public string UrlBoleto { get; set; } = string.Empty;
        public string IdBanco { get; set; } = string.Empty;

        // ── Consulta ──────────────────────────────────────────────────────────
        public string Situacao { get; set; } = string.Empty;
        public bool Pago { get; set; }
        public string DataPagamento { get; set; } = string.Empty;
        public string DataOcorrencia { get; set; } = string.Empty;
        public string DataBaixa { get; set; } = string.Empty;
        public string ValorPago { get; set; } = string.Empty;
        public string ValorJurosMora { get; set; } = string.Empty;
        public string ValorMulta { get; set; } = string.Empty;
        public string ValorDesconto { get; set; } = string.Empty;
        public string ValorAbatimento { get; set; } = string.Empty;
        public string ValorIOF { get; set; } = string.Empty;
        public string ValorOutrasDespesas { get; set; } = string.Empty;
        public string ValorOutrosCreditos { get; set; } = string.Empty;
        public string ValorDespesaCobranca { get; set; } = string.Empty;
        public string CodTipoOcorrencia { get; set; } = string.Empty;
        public string DescricaoTipoOcorrencia { get; set; } = string.Empty;
        public List<string>? MotivosRejeicao { get; set; }
        public string RawJson { get; set; } = string.Empty;

        public string QrCodePixUrl { get; set; } = string.Empty;
        public string QrCodePixEmv { get; set; } = string.Empty;
        public string QrCodePixTxId { get; set; } = string.Empty;
        public string QrCodePixBase64 { get; set; } = string.Empty;
    }

    // ── Parse da resposta de EnviarBoleto (INI → DadosRetorno) ──────────

    public static DadosRetorno ParseRetornoEnviar(string iniRetorno, int indice = 1)
    {
        var r = new DadosRetorno();
        if (string.IsNullOrWhiteSpace(iniRetorno)) return r;

        var ini = ParseIni(iniRetorno);
        var regKey = $"REGISTRO{indice}";
        var titKey = $"TITULORETORNO{indice}";

        if (ini.TryGetValue(regKey, out var reg))
        {
            r.NossoNumero = Get(reg, "IDNossoNum") ?? Get(reg, "NossoNumero") ?? r.NossoNumero;
            r.CodigoBarras = Get(reg, "IDCodBarras") ?? Get(reg, "CodBarras") ?? r.CodigoBarras;
            r.LinhaDigitavel = Get(reg, "IDLinhaDig") ?? Get(reg, "IDLinha") ?? Get(reg, "LinhaDig") ?? r.LinhaDigitavel;
            r.UrlBoleto = Get(reg, "IDURL") ?? Get(reg, "URL") ?? r.UrlBoleto;
            r.IdBanco = Get(reg, "IDBoleto") ?? Get(reg, "ID") ?? Get(reg, "Id") ?? Get(reg, "IdBoleto") ?? Get(reg, "IdTitulo") ?? r.IdBanco;

            var codRet = Get(reg, "CodRetorno");
            var msg = Get(reg, "MsgRetorno");
            var httpCodeStr = Get(reg, "HTTPResultCode");

            if (!string.IsNullOrWhiteSpace(msg)) r.Mensagem = msg;

            bool errorByCodRet = codRet != null && codRet != "00" && codRet != "0";
            bool errorByHttp = false;

            if (!string.IsNullOrWhiteSpace(httpCodeStr) && int.TryParse(httpCodeStr, out var httpCode) && httpCode >= 400)
            {
                errorByHttp = true;
            }

            if (errorByCodRet || errorByHttp)
            {
                r.Sucesso = false;
                var baseMsg = string.IsNullOrWhiteSpace(msg) ? "Erro no envio" : msg;
                var codInfo = codRet != null ? $"CodRetorno={codRet}" : "";
                var httpInfo = errorByHttp ? $"HTTP={httpCodeStr}" : "";

                var details = string.Join(" ", new[] { codInfo, httpInfo }.Where(s => !string.IsNullOrWhiteSpace(s)));

                r.Erro = $"[BancoAPI] {details}: {baseMsg}".Trim();
                if (string.IsNullOrWhiteSpace(r.Mensagem)) r.Mensagem = baseMsg;
            }
        }

        // O banco pode devolver várias rejeições (REJEICAO1-1, REJEICAO1-2, ...).
        // A primeira costuma ser genérica ("Erro na validação de Campos"); o detalhe
        // por campo vem nas seguintes. Concatena todas para o cliente ver o motivo real.
        var rejeicoes = new List<string>();
        string? codRej = null;
        for (var n = 1; ini.TryGetValue($"REJEICAO{indice}-{n}", out var rej); n++)
        {
            codRej ??= Get(rej, "Codigo");
            var campo = Get(rej, "Campo");
            var valor = Get(rej, "Valor");
            var msgRej = Get(rej, "Mensagem");
            if (string.IsNullOrWhiteSpace(msgRej)) continue;

            var detalhe = msgRej;
            if (!string.IsNullOrWhiteSpace(campo)) detalhe = $"Campo={campo}: {detalhe}";
            if (!string.IsNullOrWhiteSpace(valor)) detalhe = $"{detalhe} (valor: {valor})";
            rejeicoes.Add(detalhe);
        }

        if (rejeicoes.Count > 0)
        {
            r.Sucesso = false;
            r.Mensagem = string.Join(" | ", rejeicoes);
            r.Erro = $"[BancoRejeicao] Cod={codRej}: {r.Mensagem}";
        }

        if (ini.TryGetValue(titKey, out var tit))
        {
            r.NossoNumero = Get(tit, "NossoNumero") ?? Get(tit, "IDNossoNum") ?? r.NossoNumero;
            r.CodigoBarras = Get(tit, "CodBarras") ?? Get(tit, "IDCodBarras") ?? r.CodigoBarras;
            r.LinhaDigitavel = Get(tit, "LinhaDig") ?? Get(tit, "IDLinhaDig") ?? r.LinhaDigitavel;
            r.UrlBoleto = Get(tit, "URL") ?? Get(tit, "IDURL") ?? r.UrlBoleto;
            r.IdBanco = Get(tit, "IDBoleto") ?? Get(tit, "ID") ?? Get(tit, "Id") ?? Get(tit, "IdBoleto") ?? Get(tit, "IdTitulo") ?? r.IdBanco;

            var emv = Get(tit, "emv") ?? Get(tit, "QrCode.emv");
            var urlPix = Get(tit, "url_Pix") ?? Get(tit, "url") ?? Get(tit, "QrCode.url");
            var txid = Get(tit, "Tx_ID") ?? Get(tit, "txid") ?? Get(tit, "QrCode.txid");

            if (!string.IsNullOrWhiteSpace(emv)) r.QrCodePixEmv = emv;
            if (!string.IsNullOrWhiteSpace(urlPix)) r.QrCodePixUrl = urlPix;
            if (!string.IsNullOrWhiteSpace(txid)) r.QrCodePixTxId = txid;
        }

        return r;
    }

    public static DadosRetorno ParseRetornoConsulta(string iniRetorno, int indice = 1)
    {
        var r = new DadosRetorno();
        if (string.IsNullOrWhiteSpace(iniRetorno)) return r;

        var ini = ParseIni(iniRetorno);
        var titKey = $"TITULORETORNO{indice}";

        // TITULORETORNO1 — detailed status section (Itaú consulta detalhe)
        if (ini.TryGetValue(titKey, out var tit))
        {
            r.Situacao = Get(tit, "SituacaoTitulo") ?? Get(tit, "SituacaoBoleto")
                          ?? Get(tit, "SituacaoGeral") ?? Get(tit, "Situacao")
                          ?? Get(tit, "StatusTitulo") ?? Get(tit, "EstadoTituloCobranca")
                          ?? r.Situacao;
            r.DataPagamento = Get(tit, "DataCredito") ?? Get(tit, "DataPagamento") ?? r.DataPagamento;
            r.DataOcorrencia = Get(tit, "DataOcorrencia") ?? r.DataOcorrencia;
            r.DataBaixa = Get(tit, "DataBaixa") ?? r.DataBaixa;

            r.ValorPago = NormalizarMoeda(Get(tit, "ValorRecebido") ?? Get(tit, "ValorPago") ?? r.ValorPago);
            r.ValorJurosMora = NormalizarMoeda(Get(tit, "ValorMoraJuros") ?? Get(tit, "ValorJurosMora") ?? Get(tit, "ValorJuros") ?? r.ValorJurosMora);
            r.ValorMulta = NormalizarMoeda(Get(tit, "ValorMulta") ?? r.ValorMulta);
            r.ValorDesconto = NormalizarMoeda(Get(tit, "ValorDesconto") ?? r.ValorDesconto);
            r.ValorAbatimento = NormalizarMoeda(Get(tit, "ValorAbatimento") ?? r.ValorAbatimento);
            r.ValorIOF = NormalizarMoeda(Get(tit, "ValorIOF") ?? r.ValorIOF);
            r.ValorOutrasDespesas = NormalizarMoeda(Get(tit, "ValorOutrasDespesas") ?? r.ValorOutrasDespesas);
            r.ValorOutrosCreditos = NormalizarMoeda(Get(tit, "ValorOutrosCreditos") ?? r.ValorOutrosCreditos);
            r.ValorDespesaCobranca = NormalizarMoeda(Get(tit, "ValorDespesaCobranca") ?? r.ValorDespesaCobranca);

            r.CodTipoOcorrencia = Get(tit, "CodTipoOcorrencia") ?? Get(tit, "CodigoOcorrencia") ?? r.CodTipoOcorrencia;
            r.DescricaoTipoOcorrencia = Get(tit, "DescricaoTipoOcorrencia") ?? Get(tit, "DescricaoOcorrencia") ?? r.DescricaoTipoOcorrencia;

            r.UrlBoleto = Get(tit, "URL") ?? r.UrlBoleto;
            r.IdBanco = Get(tit, "IDBoleto") ?? Get(tit, "ID") ?? Get(tit, "Id") ?? Get(tit, "IdBoleto") ?? Get(tit, "IdTitulo") ?? r.IdBanco;

            r.CodigoBarras = Get(tit, "CodBarras") ?? Get(tit, "IDCodBarras") ?? Get(tit, "CodigoBarras") ?? r.CodigoBarras;
            r.LinhaDigitavel = Get(tit, "LinhaDig") ?? Get(tit, "IDLinhaDig") ?? Get(tit, "LinhaDigitavel") ?? r.LinhaDigitavel;

            var emv = Get(tit, "emv") ?? Get(tit, "QrCode.emv");
            var txid = Get(tit, "Tx_ID") ?? Get(tit, "txid") ?? Get(tit, "QrCode.txid");
            if (!string.IsNullOrWhiteSpace(emv)) r.QrCodePixEmv = emv;
            if (!string.IsNullOrWhiteSpace(txid)) r.QrCodePixTxId = txid;
        }

        // REGISTRO1 — fallback for barcode/line/situacao + raw bank JSON
        if (ini.TryGetValue($"REGISTRO{indice}", out var reg))
        {
            if (string.IsNullOrWhiteSpace(r.CodigoBarras))
                r.CodigoBarras = Get(reg, "IDCodBarras") ?? Get(reg, "CodBarras") ?? r.CodigoBarras;
            if (string.IsNullOrWhiteSpace(r.LinhaDigitavel))
                r.LinhaDigitavel = Get(reg, "IDLinhaDig") ?? Get(reg, "LinhaDig") ?? r.LinhaDigitavel;
            if (string.IsNullOrWhiteSpace(r.IdBanco))
                r.IdBanco = Get(reg, "IDBoleto") ?? Get(reg, "IDNossoNum") ?? r.IdBanco;
            if (string.IsNullOrWhiteSpace(r.UrlBoleto))
                r.UrlBoleto = Get(reg, "IDURL") ?? Get(reg, "URL") ?? r.UrlBoleto;
            if (string.IsNullOrWhiteSpace(r.Situacao))
                r.Situacao = Get(reg, "SituacaoTitulo") ?? Get(reg, "SituacaoBoleto")
                                ?? Get(reg, "SituacaoGeral") ?? Get(reg, "Situacao") ?? r.Situacao;

            var rawJson = Get(reg, "JSON") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                // Store as-is — compact single line from ACBrLib, no re-serialization needed.
                // GeneXus receives it as a Character and can call FromJson() directly on it.
                r.RawJson = rawJson;
                try
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    var root = doc.RootElement;
                    // ConsultarDetalhe: data[0].dado_boleto.tx_id / qrcode_pix.url
                    if (root.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                    {
                        var first = data[0];
                        if (first.TryGetProperty("dado_boleto", out var dado))
                        {
                            if (string.IsNullOrWhiteSpace(r.QrCodePixTxId)
                                && dado.TryGetProperty("tx_id", out var txEl))
                                r.QrCodePixTxId = txEl.GetString() ?? string.Empty;

                            if (dado.TryGetProperty("qrcode_pix", out var qr))
                            {
                                if (string.IsNullOrWhiteSpace(r.QrCodePixUrl) && qr.TryGetProperty("url", out var urlEl))
                                    r.QrCodePixUrl = urlEl.GetString() ?? string.Empty;

                                if (string.IsNullOrWhiteSpace(r.QrCodePixEmv) && qr.TryGetProperty("emv", out var emvEl))
                                    r.QrCodePixEmv = emvEl.GetString() ?? string.Empty;

                                if (string.IsNullOrWhiteSpace(r.QrCodePixBase64) && qr.TryGetProperty("imagem_base64", out var b64El))
                                    r.QrCodePixBase64 = b64El.GetString() ?? string.Empty;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // Coleta motivos de rejeição: [MotivoRejeicaoN-1], [MotivoRejeicaoN-2], ...
        var motivos = new List<string>();
        for (int k = 1; k < 50; k++)
        {
            if (!ini.TryGetValue($"MotivoRejeicao{indice}-{k}", out var sec)) break;
            var msg = Get(sec, "MotivoRejeicao") ?? Get(sec, "Mensagem");
            if (!string.IsNullOrWhiteSpace(msg)) motivos.Add(msg);
        }
        if (motivos.Count > 0) r.MotivosRejeicao = motivos;

        // Derivação do flag `Pago`: situação contém PAGO / LIQUIDADO / BAIXADO_POR_LIQUIDACAO
        var sit = r.Situacao.ToUpperInvariant();
        r.Pago = sit.Contains("PAGO") || sit.Contains("LIQUIDAD") || sit.Contains("BAIXADO_POR_LIQUID");

        return r;
    }

    public static List<ConsultaListaItem> ParseListaConsulta(string iniRetorno)
    {
        if (string.IsNullOrWhiteSpace(iniRetorno)) return [];

        var ini = ParseIni(iniRetorno);
        var items = new List<ConsultaListaItem>();

        int n = 1;
        while (ini.TryGetValue($"REGISTRO{n}", out var reg))
        {
            ini.TryGetValue($"TITULORETORNO{n}", out var tit);

            // Rebuild a single-item INI so ParseRetornoConsulta handles all bank quirks
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[REGISTRO1]");
            foreach (var kv in reg) sb.AppendLine($"{kv.Key}={kv.Value}");
            if (tit != null)
            {
                sb.AppendLine("[TITULORETORNO1]");
                foreach (var kv in tit) sb.AppendLine($"{kv.Key}={kv.Value}");
            }

            var d = ParseRetornoConsulta(sb.ToString());
            items.Add(new ConsultaListaItem
            {
                nossoNumero = d.NossoNumero,
                situacao = d.Situacao,
                pago = d.Pago,
                dataPagamento = d.DataPagamento,
                valorPago = d.ValorPago,
                codigoBarras = d.CodigoBarras,
                linhaDigitavel = d.LinhaDigitavel,
            });
            n++;
        }

        return items;
    }

    // ── Parse de INI ─────────────────────────────────────────────────────────

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string conteudo)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string? sec = null;

        foreach (var rawLine in conteudo.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.StartsWith(';') || line.Length == 0) continue;

            if (line.StartsWith('[') && line.Contains(']'))
            {
                sec = line[1..line.IndexOf(']')].Trim();
                if (!result.ContainsKey(sec))
                    result[sec] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (sec is null) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..];
            var ci = val.IndexOf(";;", StringComparison.Ordinal);
            if (ci >= 0) val = val[..ci];
            val = val.Trim();

            result[sec][key] = val;
        }
        return result;
    }

    private static string? Get(Dictionary<string, string> sec, string key) =>
        sec.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    // ── Helpers de data e decimal ─────────────────────────────────────────────
    public static string NormalizarData(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        var semSeparadores = valor.Replace("/", "").Replace("-", "").Replace(".", "").Replace("T", "").Replace(":", "").Trim();
        if (semSeparadores.Length == 0 || semSeparadores.StartsWith("00000000")) return string.Empty;
        if (DateTime.TryParseExact(valor,
                new[] { "dd/MM/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "yyyy-M-d", "d/M/yyyy", "d/M/yy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            || DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt.ToString("dd/MM/yyyy");
        return valor;
    }

    public static string NormalizarDataOpt(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? string.Empty : NormalizarData(valor);

    /// <summary>
    /// Normaliza um valor monetário vindo da ACBrLib para o formato que o GeneXus
    /// converte com segurança via <c>ToNumeric('.')</c> em uma variável Numeric(18,2):
    /// separador decimal '.', sem separador de milhar, exatamente 2 casas decimais.
    ///
    /// Lida com os locales que os diferentes bancos podem produzir:
    ///   "252,50" → "252.50"   |  "252.50" → "252.50"
    ///   "1.252,50" → "1252.50" |  "1,252.50" → "1252.50"
    ///   "1.234.567,89" → "1234567.89"
    ///   "1.250" → "1250.00"  (ponto com 3 dígitos = milhar, não decimal)
    ///   "1252" → "1252.00"   |  "252,5" → "252.50"  |  "252,5000" → "252.50"
    /// Entrada vazia/nula retorna vazio (preserva o contrato de boleto em aberto).
    /// Regra de desambiguação: quando há os dois separadores, o mais à direita é o
    /// decimal e o outro é milhar. Com um único separador, ele é milhar se aparecer
    /// mais de uma vez ou se for seguido por exatamente 3 dígitos; caso contrário é decimal.
    /// </summary>
    public static string NormalizarMoeda(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

        // Mantém apenas dígitos, separadores e sinal negativo (remove R$, espaços, etc).
        var sb = new StringBuilder(valor.Length);
        foreach (var c in valor)
            if (char.IsDigit(c) || c == '.' || c == ',' || c == '-')
                sb.Append(c);

        var negativo = sb.Length > 0 && sb[0] == '-';
        var s = sb.ToString().Replace("-", "");
        if (s.Length == 0) return string.Empty;

        int lastDot = s.LastIndexOf('.');
        int lastComma = s.LastIndexOf(',');

        string inteiro, fracao;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Ambos presentes: o separador mais à direita é o decimal.
            int decPos = Math.Max(lastDot, lastComma);
            inteiro = s[..decPos];
            fracao = s[(decPos + 1)..];
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            int sepPos = Math.Max(lastDot, lastComma);
            char sep = s[sepPos];
            int digitsAfter = s.Length - sepPos - 1;
            int ocorrencias = 0;
            foreach (var c in s) if (c == sep) ocorrencias++;

            if (ocorrencias > 1 || digitsAfter == 3)
            {
                // Milhar: várias ocorrências ("1.234.567") ou um grupo de 3 dígitos ("1.250").
                inteiro = s;
                fracao = string.Empty;
            }
            else
            {
                inteiro = s[..sepPos];
                fracao = s[(sepPos + 1)..];
            }
        }
        else
        {
            inteiro = s;
            fracao = string.Empty;
        }

        // Remove qualquer separador de milhar remanescente da parte inteira.
        inteiro = inteiro.Replace(".", "").Replace(",", "");
        if (inteiro.Length == 0) inteiro = "0";

        var montado = fracao.Length == 0 ? inteiro : $"{inteiro}.{fracao}";
        if (!decimal.TryParse(montado, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
            return string.Empty;
        if (negativo) d = -d;
        return d.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string FormatDecimal(decimal v) =>
        v.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string FormatDecimalJson(decimal v) =>
        v.ToString("F2", CultureInfo.InvariantCulture);

    private static string LimparMascaras(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var c in v) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static void ValidarConfigPix(ConfigBoleto cfg)
    {
        var pixOn = cfg.indicadorpix > 0;
        var temTipo = cfg.tipochavepix > 0;
        var temChave = !string.IsNullOrWhiteSpace(cfg.chavepix) && cfg.chavepix.Trim() != "0";

        if (!pixOn && !temTipo && !temChave) return; // tudo desabilitado: ok
        if (pixOn && temTipo && temChave) return; // tudo habilitado: ok

        var problemas = new List<string>();
        if (!pixOn) problemas.Add($"indicadorpix={cfg.indicadorpix} (deve ser > 0)");
        if (!temTipo) problemas.Add($"tipochavepix={cfg.tipochavepix} (deve ser > 0)");
        if (!temChave) problemas.Add($"chavepix='{cfg.chavepix}' (deve ser uma chave PIX válida)");

        throw new ArgumentException(
            $"Configuração PIX inconsistente na configboleto id={cfg.id}: " +
            string.Join("; ", problemas) +
            ". Defina indicadorpix, tipochavepix e chavepix todos como 0/vazio (sem PIX) " +
            "ou configure os três com valores válidos.");
    }

    private static string CombinarTexto(string? base_, string? extra)
    {
        var partes = new[] { base_?.Trim(), extra?.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join("|", partes);
    }

    private static int MapearTipoPessoa(int original, string docLimpo)
    {
        // Se já for 1 (PJ) ou o documento tem 14 dígitos, força PJ (1)
        if (original == 1 || docLimpo.Length == 14) return 1;
        // Se for 0 (PF) ou o documento tem 11 dígitos, força PF (0)
        if (original == 0 || docLimpo.Length == 11) return 0;
        return original;
    }

    // ── Serialização JSON de título (para log/debug/testes) ──────────────────

    public static string SerializarTitulo(Titulo t, Cliente cli, Unidade uni, ConfigBoleto cfg)
    {
        int tipoDesc = t.TipoDesconto > 0 ? t.TipoDesconto : cfg.tipodesconto;
        int codJuros = t.CodigoMoraJuros > 0 ? t.CodigoMoraJuros : cfg.codigomorajuros;
        int codMulta = t.CodigoMulta > 0 ? t.CodigoMulta : cfg.codigomulta;
        int codNegativ = t.CodigoNegativacao > 0 ? t.CodigoNegativacao : cfg.codigonegativacao;
        var aceite = cfg.aceite == 1 ? "A" : "N";
        var especie = string.IsNullOrWhiteSpace(cfg.especiedoc) ? "DM" : cfg.especiedoc;

        var instrucoes = new[] { cfg.instrucao1, cfg.instrucao2 }
            .Where(x => x > 0).ToArray();

        var obj = new Dictionary<string, object?>
        {
            ["NossoNumero"] = t.nossonumero,
            ["SeuNumero"] = t.numerodocumento,
            ["Carteira"] = cfg.carteira,
            ["ValorDocumento"] = FormatDecimalJson(t.valordocumento),
            ["DataVencimento"] = NormalizarData(t.datavencimento),
            ["DataDocumento"] = NormalizarData(t.datadocumento),
            ["DataProcessamento"] = NormalizarData(t.dataprocessamento),
            ["Especie"] = especie,
            ["Aceite"] = aceite,
            ["Desconto"] = new { Tipo = tipoDesc, Valor = FormatDecimalJson(t.valordesconto) },
            ["Juros"] = new { Tipo = codJuros, Valor = FormatDecimalJson(t.valormorajuros) },
            ["Multa"] = new { Tipo = codMulta, Percentual = FormatDecimalJson(t.percentualmulta) },
            ["Negativacao"] = new { Tipo = codNegativ },
            ["Instrucoes"] = instrucoes,
            ["Sacado"] = new
            {
                Nome = cli.nome,
                CPFCNPJ = cli.cpfcnpj,
                Pessoa = cli.tipo == 1 ? "J" : "F",
                Logradouro = cli.logradouro,
                Numero = cli.numero,
                Complemento = cli.complemento,
                Bairro = cli.bairro,
                Cidade = cli.cidade,
                UF = cli.uf,
                CEP = cli.cep,
                Email = cli.email,
            },
            ["Cedente"] = new
            {
                Nome = uni.nome,
                CPFCNPJ = uni.cpfcnpj,
                Cidade = uni.cidade,
                UF = uni.uf,
            },
        };

        return JsonSerializer.Serialize(obj);
    }

    // ── Parse de retorno JSON do banco ───────────────────────────────────────

    public static void PreencherRetornoBanco(string? json, DadosRetorno r)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            r.UrlBoleto = GetJson(root, "urlBoleto") ?? r.UrlBoleto;
            r.IdBanco = GetJson(root, "idTitulo") ?? r.IdBanco;
            r.CodigoBarras = GetJson(root, "codigoBarras") ?? r.CodigoBarras;
            r.LinhaDigitavel = GetJson(root, "linhaDigitavel") ?? r.LinhaDigitavel;
            r.NossoNumero = GetJson(root, "nossoNumero") ?? r.NossoNumero;
        }
        catch (JsonException) { }
    }

    private static string? GetJson(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() is { Length: > 0 } s ? s : null)
            : null;
}
