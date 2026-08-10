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
        // Pasta montada com as primitivas da plataforma: a lib roda em Windows e Linux,
        // e o INI sai com o separador e o caminho absoluto do host.
        var pastaOut = Path.Combine(Path.GetTempPath(), "acbr_out_test");
        var cfg = new ConfigBoleto
        {
            id = 1,
            codbanco = 341,
            tipocobranca = 6,
            pastaOutput = pastaOut,
            VersaoDF = "V2"
        };
        var uni = new Unidade { nome = "Empresa Teste", cpfcnpj = "00000000000000" };

        var ini = StxSerializer.GerarCedenteIni(uni, cfg);

        // Verifica se a seção de PDF foi gerada corretamente
        ini.Should().Contain("[BoletoBancoFCFortesConfig]");
        ini.Should().Contain("Filtro=1");
        // Sem fileName explícito, gera nome único: boleto_{id}_{guid}.pdf
        ini.Should().Contain($"NomeArquivo={Path.Combine(Path.GetFullPath(pastaOut), "boleto_1_")}");
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

        // Deve conter a chave mas com valor vazio, não 0000-00-00.
        // O INI é montado com AppendLine, então a quebra é a do host.
        ini.Should().Contain($"DataProcessamento={Environment.NewLine}");
    }
}
