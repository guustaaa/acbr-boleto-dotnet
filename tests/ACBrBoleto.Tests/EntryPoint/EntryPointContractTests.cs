using System.Text.Json;
using System.Text.Json.Serialization;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Tests.Unit;
using FluentAssertions;
using Xunit;

namespace ACBrBoleto.Tests.EntryPoint;

/// <summary>
/// Testa o contrato JSON do EntryPoint sem precisar da ACBrLibBoleto.dll
/// nem de banco de dados. Valida serialização, deserialização, campos
/// obrigatórios e respostas de erro.
/// </summary>
public class EntryPointContractTests
{
    private static readonly JsonSerializerOptions _j = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Serialização dos DTOs Normalizados ───────────────────────────────────

    [Fact]
    public void GerarBoletoResponse_SerializaCorretamente()
    {
        var r = new GerarBoletoResponse
        {
            sucesso = true,
            mensagem = "ok",
            nossoNumero = "001",
            codigoBarras = "34191...",
            linhaDigitavel = "34191.23456...",
        };

        var json = JsonSerializer.Serialize(r, _j);

        json.Should().Contain("\"sucesso\"");
        json.Should().Contain("\"mensagem\"");
        json.Should().Contain("\"nossoNumero\"");
        json.Should().Contain("\"codigoBarras\"");
    }

    [Fact]
    public void GerarBoletoResponse_CamposNulos_NaoSaoIncluidos()
    {
        var r = BaseResponse.Ok<GerarBoletoResponse>("apenas sucesso");
        var json = JsonSerializer.Serialize(r, _j);

        json.Should().NotContain("pdfBase64");
        json.Should().NotContain("pix");
        json.Should().NotContain("erro");
    }

    [Fact]
    public void OperacaoSimplesResponse_Erro_ContemTipoExcecao()
    {
        var r = BaseResponse.Falha<OperacaoSimplesResponse>(new InvalidOperationException("credencial inválida"));
        r.erro.Should().Contain("InvalidOperationException");
        r.erro.Should().Contain("credencial inválida");
        r.sucesso.Should().BeFalse();
    }

    // ── ConfigBoleto deserializa os campos de configboleto ───────────────────

    [Fact]
    public void ConfigJson_CamposObrigatorios_Deserializa()
    {
        var json = MontarConfigJson(id: 1, banco: 341);
        var cfg = JsonSerializer.Deserialize<ConfigBoleto>(json, _j)!;

        cfg.id.Should().Be(1);
        cfg.codbanco.Should().Be(341);
        cfg.agencia.Should().Be("1234");
        cfg.ws_clientid.Should().Be("my-client-id");
        cfg.caminhoACBrLib.Should().Be(@"C:\ACBrLib\ACBrLibBoleto64.dll");
    }

    // ── Titulo deserializa campos financeiros ────────────────────────────────

    [Fact]
    public void TituloJson_CamposFinanceiros_Deserializa()
    {
        var json = """
            {
                "id": 42,
                "id_configboleto": 1,
                "nossonumero": "00042",
                "valordocumento": 1500.75,
                "datavencimento": "2025-12-31",
                "percentualmulta": 2.0,
                "CodigoMulta": 2,
                "CodigoMoraJuros": 2,
                "CodigoNegativacao": 1
            }
            """;

        var t = JsonSerializer.Deserialize<Titulo>(json, _j)!;

        t.id.Should().Be(42);
        t.nossonumero.Should().Be("00042");
        t.valordocumento.Should().Be(1500.75m);
        t.CodigoMulta.Should().Be(2);
    }

    // ── Cliente deserializa sacado ────────────────────────────────────────────

    [Fact]
    public void ClienteJson_Deserializa()
    {
        var json = """
            {
                "id": 99,
                "id_empresa": 1,
                "nome": "Maria Souza",
                "cpfcnpj": "987.654.321-00",
                "logradouro": "Av. Brasil",
                "numero": "500",
                "cidade": "Rio de Janeiro",
                "uf": "RJ",
                "cep": "20040-020",
                "email": "maria@teste.com",
                "tipo": 0
            }
            """;

        var cli = JsonSerializer.Deserialize<Cliente>(json, _j)!;

        cli.nome.Should().Be("Maria Souza");
        cli.cpfcnpj.Should().Be("987.654.321-00");
        cli.email.Should().Be("maria@teste.com");
        cli.tipo.Should().Be(0);
    }

    // ── FiltroWS para consulta em lista ──────────────────────────────────────

    [Fact]
    public void FiltroWS_TodosOsCampos_Deserializa()
    {
        var json = """
            {
                "VencimentoDtIni": "2025-01-01",
                "VencimentoDtFim": "2025-12-31",
                "RegistroDtIni":   "2025-01-01",
                "IndicadorSituacaoBoleto": 1,
                "BoletoVencido": 2,
                "Carteira": 109
            }
            """;

        var f = JsonSerializer.Deserialize<FiltroWS>(json, _j)!;

        f.VencimentoDtIni.Should().Be("2025-01-01");
        f.IndicadorSituacaoBoleto.Should().Be(1);   // Aberto
        f.BoletoVencido.Should().Be(2);             // Sim
        f.Carteira.Should().Be(109);
    }

    // ── Resposta de consulta detalhada ────────────────────────────────────────

    [Fact]
    public void ConsultaDetalheResponse_ComPagamento_TemCamposSituacao()
    {
        var r = new ConsultaDetalheResponse
        {
            sucesso = true,
            nossoNumero = "001",
            situacao = "PAGO",
            dataPagamento = "15/06/2025",
            valorPago = "250.00",
        };

        var json = JsonSerializer.Serialize(r, _j);
        json.Should().Contain("PAGO");
        json.Should().Contain("15/06/2025");
        json.Should().Contain("250.00");
    }

    // ── Resposta com PIX ──────────────────────────────────────────────────────

    [Fact]
    public void GerarBoletoResponse_ComPix_TemObjetoPix()
    {
        var r = new GerarBoletoResponse
        {
            sucesso = true,
            nossoNumero = "001",
            pix = new PixDados
            {
                url = "https://banco.com/pix/001",
                emv = "00020126580014br.gov.bcb.pix...",
                txid = "TX123456789012345678901234",
            }
        };

        var json = JsonSerializer.Serialize(r, _j);
        json.Should().Contain("\"pix\"");
        json.Should().Contain("00020126");
        json.Should().Contain("TX123456");
    }

    // ── Resposta de lista (itens) ─────────────────────────────────────────────

    [Fact]
    public void ConsultaListaResponse_CampoItens_SerializaLista()
    {
        var r = new ConsultaListaResponse
        {
            sucesso = true,
            itens =
            [
                new ConsultaListaItem { nossoNumero = "001", situacao = "ABERTO" },
                new ConsultaListaItem { nossoNumero = "002", situacao = "PAGO", pago = true },
            ]
        };

        var json = JsonSerializer.Serialize(r, _j);
        json.Should().Contain("\"itens\"");
        json.Should().Contain("\"nossoNumero\":\"001\"");
        json.Should().Contain("\"situacao\":\"PAGO\"");
    }

    // ── Round-trip config → hash → reserialização ────────────────────────────

    [Fact]
    public void ConfigBoleto_RoundTrip_MantemHash()
    {
        var cfg = StxModelsTests.CriarConfig();
        var hashOrig = cfg.ComputarHash();
        var json = JsonSerializer.Serialize(cfg, _j);
        var cfg2 = JsonSerializer.Deserialize<ConfigBoleto>(json, _j)!;
        cfg2.ComputarHash().Should().Be(hashOrig);
    }

    // ── JSON completo de chamada GerarBoleto (smoke test de contrato) ─────────

    [Fact]
    public void GerarBoleto_Contrato_TodosOsJsonsSaoValidos()
    {
        var configJson = MontarConfigJson();
        var tituloJson = JsonSerializer.Serialize(StxModelsTests.CriarTitulo(), _j);
        var clienteJson = JsonSerializer.Serialize(StxModelsTests.CriarCliente(), _j);
        var unidadeJson = JsonSerializer.Serialize(StxModelsTests.CriarUnidade(), _j);

        var cfg = JsonSerializer.Deserialize<ConfigBoleto>(configJson, _j);
        var tit = JsonSerializer.Deserialize<Titulo>(tituloJson, _j);
        var cli = JsonSerializer.Deserialize<Cliente>(clienteJson, _j);
        var uni = JsonSerializer.Deserialize<Unidade>(unidadeJson, _j);

        cfg.Should().NotBeNull();
        tit.Should().NotBeNull();
        cli.Should().NotBeNull();
        uni.Should().NotBeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MontarConfigJson(
        int id = 1, int banco = 341,
        bool incluirPool = true)
    {
        var pool = incluirPool ? "\"tamanhoPool\": 2," : string.Empty;
        return $$"""
            {
                "id": {{id}},
                "id_unidade": 1,
                "tipocobranca": 1,
                "codbanco": {{banco}},
                "agencia": "1234",
                "agenciadig": "5",
                "conta": "12345",
                "contadig": "6",
                "carteira": "109",
                "convenio": "",
                "codigocedente": "",
                "ws_clientid": "my-client-id",
                "ws_clientsecret": "my-client-secret",
                "ws_scope": "boleto.read boleto.write",
                "ws_ambiente": 1,
                "indicadorpix": 0,
                "instrucao1": 10,
                "instrucao2": 0,
                "codigomulta": 2,
                "codigomorajuros": 2,
                "codigodesconto": 1,
                "codigonegativacao": 1,
                {{pool}}
                "caminhoACBrLib": "C:\\ACBrLib\\ACBrLibBoleto64.dll"
            }
            """;
    }
}
