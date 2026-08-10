using ACBrBoleto.Core.Pool;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ACBrBoleto.Tests.Unit;

/// <summary>
/// Ciclo de vida do pool em estados degenerados: pool vazio, Dispose repetido, evicção
/// depois do Dispose. A evicção com handles de verdade está em
/// <see cref="PoolManagerConcurrencyTests"/>.
/// </summary>
public class PoolManagerEvictionTests : IDisposable
{
    private readonly PoolManager _pool = new(NullLogger<PoolManager>.Instance);

    // ── Estados degenerados ───────────────────────────────────────────────────

    [Fact]
    public void TriggerEviction_PoolVazio_NaoLancaExcecao()
    {
        var act = () => _pool.TriggerEviction();
        act.Should().NotThrow();
    }

    [Fact]
    public void TriggerEviction_PoolVazio_NaoAlteraContagens()
    {
        _pool.TriggerEviction();

        _pool.ActivePoolCount.Should().Be(0);
        _pool.IdleCount.Should().Be(0);
    }

    [Fact]
    public void Invalidar_ConfigInexistente_EhSilencioso()
    {
        var act = () => _pool.Invalidar(999);

        act.Should().NotThrow();
        _pool.ActivePoolCount.Should().Be(0);
    }

    [Fact]
    public void PoolNovo_ComecaVazio()
    {
        _pool.ActivePoolCount.Should().Be(0);
        _pool.IdleCount.Should().Be(0);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_ComTimerAtivo_NaoLancaExcecao()
    {
        var pool = new PoolManager(NullLogger<PoolManager>.Instance);

        var act = () => pool.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_DuasVezes_NaoLancaExcecao()
    {
        var pool = new PoolManager(NullLogger<PoolManager>.Instance);

        var act = () => { pool.Dispose(); pool.Dispose(); };

        act.Should().NotThrow();
    }

    [Fact]
    public void TriggerEviction_AposDispose_EhNoOpSilencioso()
    {
        var pool = new PoolManager(NullLogger<PoolManager>.Instance);
        pool.Dispose();

        // O timer de evicção pode disparar concorrente ao shutdown do processo.
        var act = () => pool.TriggerEviction();

        act.Should().NotThrow();
    }

    [Fact]
    public void Invalidar_AposDispose_NaoLancaExcecao()
    {
        var pool = new PoolManager(NullLogger<PoolManager>.Instance);
        pool.Dispose();

        var act = () => pool.Invalidar(1);

        act.Should().NotThrow();
    }

    public void Dispose() => _pool.Dispose();
}
