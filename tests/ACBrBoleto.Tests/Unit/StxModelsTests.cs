using System.Text.Json;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Services;
using FluentAssertions;
using Xunit;

namespace ACBrBoleto.Tests.Unit;

public class StxModelsTests
{
    // ── BaseResponse / OperacaoSimplesResponse ───────────────────────────────

    [Fact]
    public void BaseResponse_Ok_TemSucessoTrue()
    {
        var r = BaseResponse.Ok<OperacaoSimplesResponse>("tudo bem");
        r.sucesso.Should().BeTrue();
        r.mensagem.Should().Be("tudo bem");
        r.erro.Should().BeNull();
    }

    [Fact]
    public void BaseResponse_Falha_TemSucessoFalse()
    {
        var r = BaseResponse.Falha<OperacaoSimplesResponse>("falhou");
        r.sucesso.Should().BeFalse();
        r.erro.Should().Be("falhou");
    }

    [Fact]
    public void BaseResponse_Falha_ComException_TemTipoNoErro()
    {
        var r = BaseResponse.Falha<OperacaoSimplesResponse>(new InvalidOperationException("ops"));
        r.sucesso.Should().BeFalse();
        r.erro.Should().Contain("InvalidOperationException");
        r.erro.Should().Contain("ops");
    }

    // ── ConfigBoleto.ComputarHash ─────────────────────────────────────────────

    [Fact]
    public void ConfigBoleto_HashIgual_QuandoCamposIguais()
    {
        var c1 = CriarConfig();
        var c2 = CriarConfig();
        c1.ComputarHash().Should().Be(c2.ComputarHash());
    }

    [Fact]
    public void ConfigBoleto_HashDiferente_QuandoClientSecretMuda()
    {
        var c1 = CriarConfig();
        var c2 = CriarConfig();
        c2.ws_clientsecret = "NOVO_SECRET";
        c1.ComputarHash().Should().NotBe(c2.ComputarHash());
    }

    [Fact]
    public void ConfigBoleto_HashDiferente_QuandoCaminhoMuda()
    {
        var c1 = CriarConfig();
        var c2 = CriarConfig();
        c2.caminhoACBrLib = @"C:\outro\path\acbr.dll";
        c1.ComputarHash().Should().NotBe(c2.ComputarHash());
    }

    [Fact]
    public void ConfigBoleto_HashTemDezesseisCars()
    {
        var h = CriarConfig().ComputarHash();
        h.Should().HaveLength(16);
    }

    // ── StxSerializer.NormalizarData ──────────────────────────────────────────

    [Theory]
    [InlineData("2025-12-31",           "31/12/2025")]
    [InlineData("2025-12-31T00:00:00",  "31/12/2025")]
    [InlineData("31/12/2025",           "31/12/2025")]
    public void NormalizarData_ConverteFormatos(string entrada, string esperado)
    {
        StxSerializer.NormalizarData(entrada).Should().Be(esperado);
    }

    [Fact]
    public void NormalizarData_Nula_RetornaVazio()
    {
        StxSerializer.NormalizarData(null).Should().Be(string.Empty);
    }

    // ── StxSerializer.GerarTituloIni ──────────────────────────────────────────

    [Fact]
    public void GerarTituloIni_ContemCamposEssenciais()
    {
        var ini = StxSerializer.GerarTituloIni(CriarTitulo(), CriarCliente(), CriarConfig());

        ini.Should().Contain("[Titulo1]");
        ini.Should().Contain("NossoNumero=");
        ini.Should().Contain("ValorDocumento=");
        ini.Should().Contain("Vencimento=");
        ini.Should().Contain("Sacado.NomeSacado=");
    }

    [Fact]
    public void GerarTituloIni_CodigoMultaTituloPrecedeConfig()
    {
        var cfg = CriarConfig();
        cfg.codigomulta = 3; // Isento na config

        var t = CriarTitulo();
        t.CodigoMulta = 2;   // Percentual no título — deve prevalecer

        var ini = StxSerializer.GerarTituloIni(t, CriarCliente(), cfg);
        ini.Should().Contain("CodigoMulta=2");
    }

    [Fact]
    public void GerarTituloIni_ValorDocumento_FormatoVirgulaBrasileiro()
    {
        var t = CriarTitulo();
        t.valordocumento = 1500.50m;

        var ini = StxSerializer.GerarTituloIni(t, CriarCliente(), CriarConfig());
        ini.Should().Contain("1500,50");
    }

    [Fact]
    public void GerarTituloIni_AceiteConfig1_GeraLetraA()
    {
        var cfg = CriarConfig();
        cfg.aceite = 1;
        var ini = StxSerializer.GerarTituloIni(CriarTitulo(), CriarCliente(), cfg);
        ini.Should().Contain("Aceite=A");
    }

    [Fact]
    public void GerarTituloIni_AceiteConfig0_GeraLetraN()
    {
        var cfg = CriarConfig();
        cfg.aceite = 0;
        var ini = StxSerializer.GerarTituloIni(CriarTitulo(), CriarCliente(), cfg);
        ini.Should().Contain("Aceite=N");
    }

    [Fact]
    public void GerarTituloIni_InstrucoesConfig_IncluiNoIni()
    {
        var cfg = CriarConfig();
        cfg.instrucao1 = 10;  // NaoProtestar
        cfg.instrucao2 = 16;  // CobrarMultaPercentual

        var ini = StxSerializer.GerarTituloIni(CriarTitulo(), CriarCliente(), cfg);
        ini.Should().Contain("Instrucao1=10");
        ini.Should().Contain("Instrucao2=16");
    }

    // ── StxSerializer.ParseRetornoEnviar ─────────────────────────────────

    [Fact]
    public void ParseRetornoEnviar_ParseiaRegistro1()
    {
        var retIni = """
            [REGISTRO1]
            IDNossoNum=00001
            IDCodBarras=34191234560000000000010000000011
            IDLinha=34191.23456 0...
            IDURL=https://banco.com/boleto/abc
            CodRetorno=00
            MsgRetorno=OK
            """;

        var r = StxSerializer.ParseRetornoEnviar(retIni);

        r.UrlBoleto.Should().Be("https://banco.com/boleto/abc");
        r.CodigoBarras.Should().Be("34191234560000000000010000000011");
        r.NossoNumero.Should().Be("00001");
        r.Sucesso.Should().BeTrue();
    }

    [Fact]
    public void ParseRetornoEnviar_CodRetornoNaoZero_MarcaFalha()
    {
        var retIni = """
            [REGISTRO1]
            IDNossoNum=00001
            CodRetorno=01
            MsgRetorno=Título não encontrado
            """;

        var r = StxSerializer.ParseRetornoEnviar(retIni);

        r.Sucesso.Should().BeFalse();
        r.Mensagem.Should().Be("Título não encontrado");
    }

    [Fact]
    public void ParseRetornoEnviar_RejeicoesMultiplas_ConcatenaDetalhes()
    {
        // Itaú API v2: REJEICAO1-1 traz a mensagem genérica, REJEICAO1-2 o detalhe do campo.
        var retIni = """
            [REGISTRO1]
            CodRetorno=400
            HTTPResultCode=400
            MsgRetorno=Erro na validação de Campos

            [REJEICAO1-1]
            Campo=
            Codigo=400
            Mensagem=Erro na validação de Campos
            Valor=

            [REJEICAO1-2]
            Campo=
            Codigo=
            Mensagem=Ultrapassado o tamanho máximo do campo texto_seu_numero. O tamanho máximo deste campo é 10 caracteres
            Valor=000066594001
            """;

        var r = StxSerializer.ParseRetornoEnviar(retIni);

        r.Sucesso.Should().BeFalse();
        r.Mensagem.Should().Contain("Erro na validação de Campos");
        r.Mensagem.Should().Contain("tamanho máximo deste campo é 10 caracteres");
        r.Mensagem.Should().Contain("000066594001");
        r.Erro.Should().StartWith("[BancoRejeicao] Cod=400:");
    }

    [Fact]
    public void ParseRetornoEnviar_IniVazio_RetornaDefault()
    {
        var r = StxSerializer.ParseRetornoEnviar(null!);
        r.Sucesso.Should().BeTrue(); // Parse de string vazia não altera o default (true)
    }

    // ── Desserialização de JSON GeneXus → modelos ────────────────────────────

    [Fact]
    public void ConfigBoleto_Desserializa_DeJsonSnakeCase()
    {
        var json = """{ "id": 1, "ws_clientid": "ABC", "indicadorpix": 2 }""";
        var obj  = JsonSerializer.Deserialize<ConfigBoleto>(json);
        obj.Should().NotBeNull();
        obj!.id.Should().Be(1);
        obj.ws_clientid.Should().Be("ABC");
        obj.indicadorpix.Should().Be(2);
    }

    // ── PIX EMV — preservação no INI do título e no cedente ──────────────────

    private const string SampleEmv =
        "00020101021226770014BR.GOV.BCB.PIX2555api.itau/pix/qr/v2/" +
        "65f8f5d0-987b-49d2-8236-1916623810c2" +
        "5204000053039865802BR5925ROCHATOOLS COMERCIO DE EQ" +
        "6015SAO BERNARDO DO62070503***6304FF35";

    [Fact]
    public void GerarTituloIni_EmvCompleto_NaoEhTruncado()
    {
        var t = CriarTitulo();
        t.qrcodepix_emv = SampleEmv;

        var ini = StxSerializer.GerarTituloIni(t, CriarCliente(), CriarConfigPix());

        ini.Should().Contain($"QrCode.emv={SampleEmv}");
    }

    [Fact]
    public void GerarTituloIni_EmvSemQuebrasDeLinha_UmaLinhaApenas()
    {
        var t = CriarTitulo();
        t.qrcodepix_emv = SampleEmv + "\r\n"; // simula valor com quebra de linha

        var ini = StxSerializer.GerarTituloIni(t, CriarCliente(), CriarConfigPix());

        // Deve estar em uma única linha — sem quebra embutida no valor
        ini.Should().Contain($"QrCode.emv={SampleEmv}");
        // Garante que não há CRLF no meio do valor EMV
        var linha = ini.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("QrCode.emv="));
        linha.Should().NotBeNull();
        linha!.TrimEnd('\r').Should().Be($"QrCode.emv={SampleEmv}");
    }

    [Fact]
    public void GerarCedenteIni_ComPix_UsaLayout8NoFortes()
    {
        var ini = StxSerializer.GerarCedenteIni(CriarUnidade(), CriarConfigPix(), "boleto.pdf");

        ini.Should().Contain("Layout=8");
        ini.Should().Contain("[BoletoBancoFCFortesConfig]");
    }

    [Fact]
    public void GerarCedenteIni_SemPix_UsaLayout0NoFortes()
    {
        var ini = StxSerializer.GerarCedenteIni(CriarUnidade(), CriarConfig(), "boleto.pdf");

        ini.Should().Contain("Layout=0");
    }

    // ── Helpers de Criação ───────────────────────────────────────────────────

    internal static ConfigBoleto CriarConfigPix() => new()
    {
        id             = 1,
        codbanco       = 341,
        agencia        = "1690",
        conta          = "20579",
        ws_clientid    = "CLIENT_ID",
        ws_clientsecret= "CLIENT_SECRET",
        caminhoACBrLib = @"C:\ACBrLib\ACBrLibBoleto64.dll",
        indicadorpix   = 1,
        tipochavepix   = 5, // Aleatória
        chavepix       = "65f8f5d0-987b-49d2-8236-1916623810c2",
        pastaOutput    = @"C:\temp\boletos",
    };

    internal static ConfigBoleto CriarConfig() => new()
    {
        id             = 1,
        codbanco       = 341,
        agencia        = "1690",
        conta          = "20579",
        ws_clientid    = "CLIENT_ID",
        ws_clientsecret= "CLIENT_SECRET",
        caminhoACBrLib = @"C:\ACBrLib\ACBrLibBoleto64.dll",
    };

    internal static Titulo CriarTitulo() => new()
    {
        id             = 10,
        nossonumero    = "00001",
        valordocumento = 250.00m,
        datavencimento = "2025-12-31",
    };

    internal static Cliente CriarCliente() => new()
    {
        id      = 2,
        nome    = "João Silva",
        cpfcnpj = "12345678901",
        cep     = "01234-567",
    };

    internal static Unidade CriarUnidade() => new()
    {
        id      = 3,
        nome    = "Empresa Teste LTDA",
        cpfcnpj = "12.345.678/0001-99",
        cidade  = "São Paulo",
        uf      = "SP",
    };
}
