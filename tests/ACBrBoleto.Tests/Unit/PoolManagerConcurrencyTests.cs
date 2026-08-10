using System.Collections.Concurrent;
using ACBrBoleto.Core.Exceptions;
using ACBrBoleto.Core.Interop;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Pool;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ACBrBoleto.Tests.Unit;

/// <summary>
/// Política de concorrência do pool: duas empresas emitindo ao mesmo tempo nunca
/// compartilham um handle, e nenhum handle é reconfigurado de um banco para outro.
///
/// Roda sem a ACBrLibBoleto64: <see cref="PoolManager.FabricaHandle"/> troca a criação do
/// handle nativo por um handle sem DLL. Cobre a política do pool, não o interop.
/// </summary>
public class PoolManagerConcurrencyTests
{
    // ── Lease exclusivo ───────────────────────────────────────────────────────

    [Fact]
    public async Task LeasesSimultaneos_MesmaConfig_NuncaCompartilhamHandle()
    {
        using var ctx = new PoolContext(maxHandles: 4);
        var cfg = ctx.Config(id: 1);

        var leases = new List<AcbrLibLease>();
        for (int i = 0; i < 4; i++)
            leases.Add(await ctx.Pool.AlugarAsync(cfg, CancellationToken.None));

        // Mesmo com hash idêntico, cada lease ativo detém um handle distinto: o pool só
        // reusa um handle DEPOIS que ele volta ao conjunto ocioso.
        ctx.HandlesDe(leases).Should().OnlyHaveUniqueItems();
        ctx.Pool.ActivePoolCount.Should().Be(4);

        foreach (var l in leases) l.Dispose();
    }

    [Fact]
    public async Task AluguelParalelo_SobPressao_NuncaEntregaOMesmoHandleDuasVezes()
    {
        const int max = 8;
        const int operacoes = 200;

        using var ctx = new PoolContext(maxHandles: max);
        var cfgs = Enumerable.Range(1, 4).Select(i => ctx.Config(id: i)).ToArray();

        var emUso = new ConcurrentDictionary<AcbrLibHandle, byte>();
        var violacoes = 0;

        await Task.WhenAll(Enumerable.Range(0, operacoes).Select(async i =>
        {
            var cfg = cfgs[i % cfgs.Length];
            using var lease = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
            var handle = ctx.HandleDe(lease);

            // Se outra thread já registrou este handle, o pool entregou o mesmo handle
            // nativo para dois leases ativos — exatamente o bug que o pool existe para evitar.
            if (!emUso.TryAdd(handle, 0)) Interlocked.Increment(ref violacoes);
            await Task.Yield();
            emUso.TryRemove(handle, out _);
        }));

        violacoes.Should().Be(0);
        ctx.Pool.ActivePoolCount.Should().BeLessThanOrEqualTo(max);
    }

    // ── Isolamento por configuração ───────────────────────────────────────────

    [Fact]
    public async Task HandleOcioso_DeOutraConfig_NaoEhReutilizado()
    {
        using var ctx = new PoolContext(maxHandles: 4);

        var lease1 = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handleEmpresa1 = ctx.HandleDe(lease1);
        lease1.Dispose();                       // volta ao conjunto ocioso, quente para a config 1

        var lease2 = await ctx.Pool.AlugarAsync(ctx.Config(id: 2), CancellationToken.None);
        var handleEmpresa2 = ctx.HandleDe(lease2);

        // Credenciais diferentes ⇒ hash diferente ⇒ handle novo. Reconfigurar o handle
        // quente da empresa 1 para a empresa 2 vazaria token OAuth e dados de cedente.
        handleEmpresa2.Should().NotBeSameAs(handleEmpresa1);
        handleEmpresa2.ConfigHash.Should().NotBe(handleEmpresa1.ConfigHash);

        lease2.Dispose();
    }

    [Fact]
    public async Task HandleOcioso_MesmaConfig_EhReutilizadoQuente()
    {
        using var ctx = new PoolContext(maxHandles: 4);
        var cfg = ctx.Config(id: 1);

        var lease1 = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        var primeiro = ctx.HandleDe(lease1);
        lease1.Dispose();

        var lease2 = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);

        // Mesmo hash ⇒ reuso do handle quente (token OAuth nativo ainda válido),
        // sem pagar uma nova inicialização.
        ctx.HandleDe(lease2).Should().BeSameAs(primeiro);
        ctx.Criados.Should().Be(1);

        lease2.Dispose();
    }

    [Fact]
    public async Task CredencialRotacionada_ForcaHandleNovo()
    {
        using var ctx = new PoolContext(maxHandles: 4);

        var antes = ctx.Config(id: 1);
        var lease1 = await ctx.Pool.AlugarAsync(antes, CancellationToken.None);
        var handleAntigo = ctx.HandleDe(lease1);
        lease1.Dispose();

        var depois = ctx.Config(id: 1);
        depois.ws_clientsecret = "secret-rotacionado";

        var lease2 = await ctx.Pool.AlugarAsync(depois, CancellationToken.None);

        ctx.HandleDe(lease2).Should().NotBeSameAs(handleAntigo);
        ctx.Criados.Should().Be(2);

        lease2.Dispose();
    }

    // ── Teto global e evicção LRU ─────────────────────────────────────────────

    [Fact]
    public async Task ConfigNova_ComTetoAtingido_DescartaOOciosoLru()
    {
        using var ctx = new PoolContext(maxHandles: 2);

        // Dois handles ociosos: config 1 é o mais antigo (LRU).
        var l1 = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handle1 = ctx.HandleDe(l1);
        l1.Dispose();

        var l2 = await ctx.Pool.AlugarAsync(ctx.Config(id: 2), CancellationToken.None);
        var handle2 = ctx.HandleDe(l2);
        l2.Dispose();

        handle1.LastUsed = DateTime.UtcNow.AddMinutes(-5);   // envelhece o handle da config 1
        handle2.LastUsed = DateTime.UtcNow;

        var l3 = await ctx.Pool.AlugarAsync(ctx.Config(id: 3), CancellationToken.None);

        // Uma config nova no teto descarta o ocioso menos usado — nunca reconfigura um vivo.
        handle1.Disposed.Should().BeTrue();
        handle2.Disposed.Should().BeFalse();
        ctx.Pool.ActivePoolCount.Should().Be(2);

        l3.Dispose();
    }

    [Fact]
    public async Task TotalDeHandles_NuncaUltrapassaOTeto()
    {
        const int max = 3;
        using var ctx = new PoolContext(maxHandles: max);

        // 12 configs distintas contra um teto de 3: o custo acompanha a concorrência,
        // não o número de configurações. É o que permite centenas de empresas sem
        // acumular instâncias nativas dormentes.
        for (int i = 1; i <= 12; i++)
        {
            using var lease = await ctx.Pool.AlugarAsync(ctx.Config(id: i), CancellationToken.None);
            ctx.Pool.ActivePoolCount.Should().BeLessThanOrEqualTo(max);
        }

        ctx.Pool.ActivePoolCount.Should().BeLessThanOrEqualTo(max);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PoolEsgotado_LancaPoolTimeoutException()
    {
        using var ctx = new PoolContext(maxHandles: 1);
        var cfg = ctx.Config(id: 1);
        cfg.poolWaitTimeoutSec = 1;

        using var ocupado = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);

        var act = async () => await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);

        (await act.Should().ThrowAsync<PoolTimeoutException>())
            .Which.ConfigId.Should().Be(cfg.id);
    }

    [Fact]
    public async Task PoolEsgotado_LiberaAssimQueOLeaseAnteriorVolta()
    {
        using var ctx = new PoolContext(maxHandles: 1);
        var cfg = ctx.Config(id: 1);
        cfg.poolWaitTimeoutSec = 10;

        var primeiro = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);

        var segundoPedido = ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        segundoPedido.IsCompleted.Should().BeFalse("o único handle está alugado");

        primeiro.Dispose();

        using var segundo = await segundoPedido.WaitAsync(TimeSpan.FromSeconds(5));
        ctx.Criados.Should().Be(1, "o handle devolvido é reusado, não recriado");
    }

    [Fact]
    public async Task TokenCancelado_AbortaAEsperaSemVazarPermit()
    {
        using var ctx = new PoolContext(maxHandles: 1);
        var cfg = ctx.Config(id: 1);
        cfg.poolWaitTimeoutSec = 30;

        using var ocupado = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await ctx.Pool.AlugarAsync(cfg, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // O cancelamento não pode consumir o permit: devolver o lease ativo deve
        // liberar o pool normalmente.
        ocupado.Dispose();
        using var depois = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
    }

    // ── Descarte de handle inutilizável ───────────────────────────────────────

    [Fact]
    public async Task HandleFaulted_EhDescartadoEmVezDeVoltarAoPool()
    {
        using var ctx = new PoolContext(maxHandles: 2);
        var cfg = ctx.Config(id: 1);

        var lease = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        var handle = ctx.HandleDe(lease);
        handle.MarcarFaulted();      // operação nativa com estado falhou
        lease.Dispose();

        // Reusar um handle faulted dispara access violation (0xC0000005) na DLL Delphi.
        handle.Disposed.Should().BeTrue();
        ctx.Pool.IdleCount.Should().Be(0);
        ctx.Pool.ActivePoolCount.Should().Be(0);

        using var novo = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        ctx.HandleDe(novo).Should().NotBeSameAs(handle);
    }

    [Fact]
    public async Task HandleComRelatorioGerado_EhDescartado()
    {
        using var ctx = new PoolContext(maxHandles: 2);

        var lease = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handle = ctx.HandleDe(lease);
        handle.MarcarRelatorioGerado();
        lease.Dispose();

        // O motor de relatório Fortes acumula estado entre gerações no mesmo handle:
        // a 2ª geração sai comprimida/multipágina. Sem API de reset, só descartar resolve.
        handle.Disposed.Should().BeTrue();
        ctx.Pool.IdleCount.Should().Be(0);
    }

    [Fact]
    public async Task FalhaAoLimparNaDevolucao_DescartaOHandle()
    {
        using var ctx = new PoolContext(maxHandles: 2);

        var lease = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handle = ctx.HandleDe(lease);
        handle.FalharAoLimpar = true;
        lease.Dispose();

        // Se nem a limpeza da lista de títulos passa, o handle não é confiável para reuso.
        handle.Disposed.Should().BeTrue();
        ctx.Pool.IdleCount.Should().Be(0);
        ctx.Pool.ActivePoolCount.Should().Be(0);
    }

    [Fact]
    public async Task DescarteDeHandleFaulted_LiberaOPermitDoTeto()
    {
        using var ctx = new PoolContext(maxHandles: 1);
        var cfg = ctx.Config(id: 1);
        cfg.poolWaitTimeoutSec = 5;

        var lease = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        ctx.HandleDe(lease).MarcarFaulted();
        lease.Dispose();

        // O caminho de descarte precisa liberar o semáforo tanto quanto o caminho feliz;
        // se vazasse um permit aqui, o pool travaria após N falhas.
        using var depois = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        ctx.Criados.Should().Be(2);
    }

    // ── Dispose idempotente do lease ──────────────────────────────────────────

    [Fact]
    public async Task DisposeDuplicadoDoLease_DevolveOHandleUmaVezSo()
    {
        using var ctx = new PoolContext(maxHandles: 2);
        var cfg = ctx.Config(id: 1);

        var lease = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        lease.Dispose();
        lease.Dispose();        // idempotente: um using aninhado a um try/finally não pode duplicar

        ctx.Pool.IdleCount.Should().Be(1, "o handle não pode ser enfileirado duas vezes");
        ctx.Pool.ActivePoolCount.Should().Be(1);

        // Um permit duplicado deixaria o teto ser furado; aqui ainda cabem exatamente 2.
        var a = await ctx.Pool.AlugarAsync(cfg, CancellationToken.None);
        var b = await ctx.Pool.AlugarAsync(ctx.Config(id: 2), CancellationToken.None);
        ctx.Pool.ActivePoolCount.Should().Be(2);

        a.Dispose();
        b.Dispose();
    }

    [Fact]
    public async Task PoolDescartado_ComLeaseAtivo_DescartaOHandleNaDevolucao()
    {
        var ctx = new PoolContext(maxHandles: 2);
        var lease = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handle = ctx.HandleDe(lease);

        ctx.Pool.Dispose();      // shutdown enquanto uma operação ainda está em voo
        var act = () => lease.Dispose();

        act.Should().NotThrow();
        handle.Disposed.Should().BeTrue();
    }

    // ── Evicção por ociosidade ────────────────────────────────────────────────

    [Fact]
    public async Task HandleOciosoAntigo_EhEvictado()
    {
        using var ctx = new PoolContext(maxHandles: 4);

        var lease = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handle = ctx.HandleDe(lease);
        lease.Dispose();

        handle.LastUsed = DateTime.UtcNow.AddMinutes(-30);   // além do corte de 10 min
        ctx.Pool.TriggerEviction();

        handle.Disposed.Should().BeTrue();
        ctx.Pool.IdleCount.Should().Be(0);
        ctx.Pool.ActivePoolCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleOciosoRecente_NaoEhEvictado()
    {
        using var ctx = new PoolContext(maxHandles: 4);

        var lease = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handle = ctx.HandleDe(lease);
        lease.Dispose();

        handle.LastUsed = DateTime.UtcNow.AddMinutes(-2);
        ctx.Pool.TriggerEviction();

        handle.Disposed.Should().BeFalse();
        ctx.Pool.IdleCount.Should().Be(1);
    }

    [Fact]
    public async Task Invalidar_DescartaOsOciososDaConfigEPreservaAsOutras()
    {
        using var ctx = new PoolContext(maxHandles: 4);

        var l1 = await ctx.Pool.AlugarAsync(ctx.Config(id: 1), CancellationToken.None);
        var handle1 = ctx.HandleDe(l1);
        l1.Dispose();

        var l2 = await ctx.Pool.AlugarAsync(ctx.Config(id: 2), CancellationToken.None);
        var handle2 = ctx.HandleDe(l2);
        l2.Dispose();

        ctx.Pool.Invalidar(1);      // ex.: rotação de credenciais da empresa 1

        handle1.Disposed.Should().BeTrue();
        handle2.Disposed.Should().BeFalse();
        ctx.Pool.IdleCount.Should().Be(1);
    }

    // ── Contexto de teste ─────────────────────────────────────────────────────

    /// <summary>
    /// Pool com teto explícito e fábrica de handles sem DLL nativa. Mantém a lista de
    /// handles criados, na ordem, para as asserções de identidade.
    /// </summary>
    private sealed class PoolContext : IDisposable
    {
        private readonly List<AcbrLibHandle> _criados = new();
        private readonly object _gate = new();
        private readonly string _dllFalsa;

        public PoolManager Pool { get; }

        public PoolContext(int maxHandles)
        {
            // ValidarConfig exige que caminhoACBrLib exista em disco. O arquivo nunca é
            // carregado: a fábrica abaixo substitui a criação do handle nativo.
            _dllFalsa = Path.Combine(Path.GetTempPath(), $"acbr_fake_{Guid.NewGuid():N}.dll");
            File.WriteAllText(_dllFalsa, string.Empty);

            Pool = new PoolManager(NullLogger<PoolManager>.Instance, maxHandles)
            {
                FabricaHandle = (cfg, hash) =>
                {
                    var h = new AcbrLibHandle(semNativo: true)
                    {
                        ConfigHash = hash,
                        ConfigId = cfg.id,
                        LastUsed = DateTime.UtcNow,
                    };
                    lock (_gate) _criados.Add(h);
                    return h;
                }
            };
        }

        public int Criados { get { lock (_gate) return _criados.Count; } }

        public AcbrLibHandle HandleDe(AcbrLibLease lease) => lease.Handle;

        public IEnumerable<AcbrLibHandle> HandlesDe(IEnumerable<AcbrLibLease> leases) =>
            leases.Select(HandleDe);

        /// <summary>Config válida e distinta por <paramref name="id"/> (hash distinto).</summary>
        public ConfigBoleto Config(int id) => new()
        {
            id = id,
            codbanco = 341,
            agencia = "1234",
            agenciadig = "5",
            conta = "12345",
            contadig = "6",
            carteira = "109",
            ws_clientid = $"cid-{id}",
            ws_clientsecret = $"secret-{id}",
            ws_scope = "boleto",
            ws_ambiente = 1,
            tipocobranca = 6,
            caminhoACBrLib = _dllFalsa,
        };

        public void Dispose()
        {
            Pool.Dispose();
            try { File.Delete(_dllFalsa); } catch { }
        }
    }
}
