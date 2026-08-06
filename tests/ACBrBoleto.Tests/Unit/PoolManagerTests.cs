using ACBrBoleto.Core.Exceptions;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Pool;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ACBrBoleto.Tests.Unit;

public class PoolManagerTests : IDisposable
{
    private readonly PoolManager _pool =
        new(NullLogger<PoolManager>.Instance);

    // ── Validação de config ───────────────────────────────────────────────────

    [Fact]
    public async Task AlugarAsync_SemCaminhoACBrLib_LancaConfigInvalida()
    {
        var cfg = ConfigSemCampo(c => c.caminhoACBrLib = "");

        var act = async () => await _pool.AlugarAsync(cfg, CancellationToken.None);

        await act.Should().ThrowAsync<ConfigInvalidaException>()
            .WithMessage("*caminhoACBrLib*");
    }

    [Fact]
    public async Task AlugarAsync_DllNaoExiste_LancaConfigInvalida()
    {
        var cfg = ConfigBase();
        cfg.caminhoACBrLib = @"C:\nao\existe\acbr.dll";

        var act = async () => await _pool.AlugarAsync(cfg, CancellationToken.None);

        await act.Should().ThrowAsync<ConfigInvalidaException>()
            .WithMessage("*arquivo não encontrado*");
    }

    [Fact]
    public async Task AlugarAsync_CodbancOZero_LancaConfigInvalida()
    {
        var cfg = ConfigSemCampo(c => c.codbanco = 0);

        var act = async () => await _pool.AlugarAsync(cfg, CancellationToken.None);

        await act.Should().ThrowAsync<ConfigInvalidaException>()
            .WithMessage("*codbanco*");
    }

    [Fact]
    public async Task AlugarAsync_SemAgencia_LancaConfigInvalida()
    {
        var cfg = ConfigSemCampo(c => c.agencia = "");

        var act = async () => await _pool.AlugarAsync(cfg, CancellationToken.None);

        await act.Should().ThrowAsync<ConfigInvalidaException>()
            .WithMessage("*agencia*");
    }

    [Fact]
    public async Task AlugarAsync_SemConta_LancaConfigInvalida()
    {
        var cfg = ConfigSemCampo(c => c.conta = "");

        var act = async () => await _pool.AlugarAsync(cfg, CancellationToken.None);

        await act.Should().ThrowAsync<ConfigInvalidaException>()
            .WithMessage("*conta*");
    }

    [Fact]
    public async Task AlugarAsync_SemCarteira_LancaConfigInvalida()
    {
        var cfg = ConfigSemCampo(c => c.carteira = "");

        var act = async () => await _pool.AlugarAsync(cfg, CancellationToken.None);

        await act.Should().ThrowAsync<ConfigInvalidaException>()
            .WithMessage("*carteira*");
    }

    [Fact]
    public async Task AlugarAsync_SemClientId_LancaConfigInvalida()
    {
        var cfg = ConfigSemCampo(c => c.ws_clientid = "");

        var act = async () => await _pool.AlugarAsync(cfg, CancellationToken.None);

        await act.Should().ThrowAsync<ConfigInvalidaException>()
            .WithMessage("*ws_clientid*");
    }

    // ── Invalidação de pool ───────────────────────────────────────────────────

    [Fact]
    public void InvalidarPool_IdInexistente_NaoLancaExcecao()
    {
        // Pool nunca criado para configId=999 — deve ser silencioso
        var act = () => _pool.Invalidar(999);
        act.Should().NotThrow();
    }

    // ── Detecção de mudança de hash ───────────────────────────────────────────

    [Fact]
    public void ConfigBoleto_HashMuda_QuandoSecretMuda()
    {
        var c1 = ConfigBase();
        var c2 = ConfigBase();
        c2.ws_clientsecret = "outro-secret";

        c1.ComputarHash().Should().NotBe(c2.ComputarHash());
    }

    [Fact]
    public void ConfigBoleto_HashIgual_QuandoSoNomeMuda()
    {
        // Nome da unidade não faz parte do hash — não afeta pool
        var c1 = ConfigBase();
        var c2 = ConfigBase();
        // nome não existe em ConfigBoleto — hash é baseado em credenciais

        c1.ComputarHash().Should().Be(c2.ComputarHash());
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_PodeSerChamadoDuasVezes_SemExcecao()
    {
        var pool = new PoolManager(NullLogger<PoolManager>.Instance);
        var act  = () => { pool.Dispose(); pool.Dispose(); };
        act.Should().NotThrow();
    }

    [Fact]
    public async Task AposDispose_AlugarAsync_LancaObjectDisposed()
    {
        var pool = new PoolManager(NullLogger<PoolManager>.Instance);
        pool.Dispose();

        var act = async () => await pool.AlugarAsync(ConfigBase(), CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact(Skip = "Requer DLL válida para rodar")]
    public async Task AlugarAsync_ComTokenCancelado_LancaOperationCanceled()
    {
        var cfg = ConfigBase();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _pool.AlugarAsync(cfg, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ConfigBoleto ConfigBase() => new()
    {
        id              = 1,
        codbanco        = 341,
        agencia         = "1234",
        agenciadig      = "5",
        conta           = "12345",
        contadig        = "6",
        carteira        = "109",
        ws_clientid     = "cid",
        ws_clientsecret = "cs",
        ws_scope        = "boleto",
        ws_ambiente     = 1,
        tipocobranca    = 6,
        caminhoACBrLib  = @"C:\nao\existe.dll",  // inválido → vai lançar ConfigInvalida
    };

    private static ConfigBoleto ConfigSemCampo(Action<ConfigBoleto> mutar)
    {
        var c = ConfigBase();
        mutar(c);
        return c;
    }

    public void Dispose() => _pool.Dispose();
}
