using ACBrBoleto.Core.Pool;
using FluentAssertions;
using Xunit;

namespace ACBrBoleto.Tests.Unit;

public class DiagnosticRedactionTests
{
    [Theory]
    [InlineData("ClientID=customer-id", "customer-id")]
    [InlineData("ClientSecret=super-secret", "super-secret")]
    [InlineData("Chave=123.456.789-00", "123.456.789-00")]
    [InlineData("ArquivoCRT=C:\\certs\\client.crt", "C:\\certs\\client.crt")]
    [InlineData("ArquivoKEY=C:\\certs\\client.key", "C:\\certs\\client.key")]
    public void RedactSensitiveIni_RemovesCredentialValue(string line, string secret)
    {
        var result = PoolManager.RedactSensitiveIni(line);

        result.Should().NotContain(secret);
        result.Should().EndWith("[REDACTED]");
    }

    [Fact]
    public void RedactSensitiveIni_PreservesOperationalSettings()
    {
        var input = "LogNivel=3\r\nTimeout=30\r\nClientSecret=secret\r\n";

        var result = PoolManager.RedactSensitiveIni(input);

        result.Should().Contain("LogNivel=3");
        result.Should().Contain("Timeout=30");
        result.Should().NotContain("secret");
    }
}
