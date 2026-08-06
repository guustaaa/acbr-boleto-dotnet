using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Services;
using ACBrBoleto.GeneXus;
using FluentAssertions;
using Xunit;

namespace ACBrBoleto.Tests.Unit;

/// <summary>
/// Testa os helpers extraídos: ParsePositiveDecimal (BoletoEntryPoint) e AplicarRetorno (BoletoService).
/// Não requer DLL nativa.
/// </summary>
public class HelpersTests
{
    // ── ParsePositiveDecimal ──────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0.01")]
    public void ParsePositiveDecimal_EntradaInvalida_LancaArgumentException(string input)
    {
        var act = () => BoletoEntryPoint.ParsePositiveDecimal(input, "campo");
        act.Should().Throw<ArgumentException>()
           .WithMessage("*campo*");
    }

    [Theory]
    [InlineData("123.45", 123.45)]
    [InlineData("1", 1.0)]
    [InlineData("0.01", 0.01)]
    [InlineData("1000000", 1000000.0)]
    public void ParsePositiveDecimal_EntradaValida_RetornaDecimalCorreto(string input, double expected)
    {
        var result = BoletoEntryPoint.ParsePositiveDecimal(input, "campo");
        result.Should().Be((decimal)expected);
    }

    // ── NormalizarMoeda ───────────────────────────────────────────────────────
    // Saída sempre com '.' decimal, sem milhar, 2 casas — pronta para o
    // ToNumeric('.') do GeneXus em uma variável Numeric(18,2).

    [Theory]
    // locale brasileiro (vírgula decimal) — caso mais comum da ACBrLib
    [InlineData("252,50", "252.50")]
    [InlineData("252,5", "252.50")]
    [InlineData("0,00", "0.00")]
    // locale invariável (ponto decimal)
    [InlineData("252.50", "252.50")]
    [InlineData("1252", "1252.00")]
    // milhar + decimal nos dois formatos
    [InlineData("1.252,50", "1252.50")]
    [InlineData("1,252.50", "1252.50")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("1,234,567.89", "1234567.89")]
    // separador único seguido de exatamente 3 dígitos = milhar, não decimal
    // (não há como distinguir de uma fração de 3 casas; boletos usam 2 casas)
    [InlineData("1.250", "1250.00")]
    [InlineData("1,250", "1250.00")]
    // mais de 2 casas decimais (4+ dígitos = inequivocamente decimal) → arredonda p/ 2
    [InlineData("252,5000", "252.50")]
    [InlineData("10,0070", "10.01")]
    // negativos e ruído (R$, espaços)
    [InlineData("-50,00", "-50.00")]
    [InlineData("R$ 1.500,75", "1500.75")]
    [InlineData(" 99,90 ", "99.90")]
    public void NormalizarMoeda_ConverteParaFormatoGeneXus(string entrada, string esperado)
    {
        StxSerializer.NormalizarMoeda(entrada).Should().Be(esperado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    public void NormalizarMoeda_EntradaVaziaOuInvalida_RetornaVazio(string? entrada)
    {
        StxSerializer.NormalizarMoeda(entrada).Should().BeEmpty();
    }

    // ── AplicarRetorno (via BoletoService.AplicarRetorno) ────────────────────
    // AplicarRetorno é private static — testado indiretamente pelo comportamento
    // das operações de manutenção. Aqui validamos o DadosRetorno intermediário.

    [Fact]
    public void DadosRetorno_Falha_MantemCamposDeErro()
    {
        var d = new StxSerializer.DadosRetorno
        {
            Sucesso = false,
            Erro = "erro-teste",
            Mensagem = "mensagem-teste"
        };

        d.Sucesso.Should().BeFalse();
        d.Erro.Should().Be("erro-teste");
        d.Mensagem.Should().Be("mensagem-teste");
    }

    [Fact]
    public void DadosRetorno_Sucesso_NaoTemErro()
    {
        var d = new StxSerializer.DadosRetorno
        {
            Sucesso = true,
            Mensagem = "ok"
        };

        d.Sucesso.Should().BeTrue();
        d.Erro.Should().BeNullOrEmpty();
    }

    [Fact]
    public void BaseResponse_Falha_PreencheErroEMensagem()
    {
        var r = BaseResponse.Falha<ManutencaoResponse>(new InvalidOperationException("falha teste"));

        r.sucesso.Should().BeFalse();
        r.erro.Should().Contain("InvalidOperationException");
        r.mensagem.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BaseResponse_Ok_SucessoVerdadeiro()
    {
        var r = BaseResponse.Ok<ManutencaoResponse>("operação ok");

        r.sucesso.Should().BeTrue();
        r.mensagem.Should().Be("operação ok");
        r.erro.Should().BeNull();
    }
}
