using System;
using System.Text.Json;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Services;
using FluentAssertions;
using Xunit;

namespace ACBrBoleto.Tests.Unit;

public class PdfGenerationTests
{
    [Fact]
    public void StxSerializer_GeraIniComConfiguracaoFortes()
    {
        var cfg = new ConfigBoleto
        {
            id = 1,
            codbanco = 341,
            tipocobranca = 6,
            pastaOutput = @"C:\TEMP\OUTPUT",
            VersaoDF = "V2"
        };
        var uni = new Unidade { nome = "Empresa Teste", cpfcnpj = "00000000000000" };

        var ini = StxSerializer.GerarCedenteIni(uni, cfg);

        // Verifica se a seção de PDF foi gerada corretamente
        ini.Should().Contain("[BoletoBancoFCFortesConfig]");
        ini.Should().Contain("Filtro=1");
        // Sem fileName explícito, gera nome único: boleto_{id}_{guid}.pdf
        ini.Should().Contain(@"NomeArquivo=C:\TEMP\OUTPUT\boleto_1_");
        ini.Should().Contain(".pdf");
    }

    [Fact]
    public void StxSerializer_NormalizarData_TrataDataZeradaPostgres()
    {
        // 0000-00-00 (vinda do PG/GeneXus) deve virar "" (vazio) para a ACBrLib não dar erro
        StxSerializer.NormalizarData("0000-00-00").Should().BeEmpty();
        StxSerializer.NormalizarData("0000-00-00T00:00:00").Should().BeEmpty();
        StxSerializer.NormalizarData("  /  /    ").Should().BeEmpty();
    }

    [Fact]
    public void StxSerializer_GerarTituloIni_UsaDataNormalizada()
    {
        var t = new Titulo { dataprocessamento = "0000-00-00" };
        var cli = new Cliente { nome = "Sacado" };
        var cfg = new ConfigBoleto();

        var ini = StxSerializer.GerarTituloIni(t, cli, cfg);

        // Deve conter a chave mas com valor vazio, não 0000-00-00
        ini.Should().Contain("DataProcessamento=\r\n");
    }
}
