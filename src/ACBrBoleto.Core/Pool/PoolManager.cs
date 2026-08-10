using System.Collections.Concurrent;
using ACBrBoleto.Core.Exceptions;
using ACBrBoleto.Core.Interop;
using ACBrBoleto.Core.Models;
using ACBrBoleto.Core.Services;
using Microsoft.Extensions.Logging;

namespace ACBrBoleto.Core.Pool;

// ── Lease ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Representa uma instância da ACBrLib alugada do pool.
/// Devolve automaticamente ao pool no Dispose (padrão using).
/// </summary>
public sealed class AcbrLibLease : IDisposable
{
    internal AcbrLibHandle Handle { get; }
    private readonly Action<AcbrLibLease> _devolver;
    private int _devolvido;

    internal AcbrLibLease(AcbrLibHandle handle, Action<AcbrLibLease> devolver)
    {
        Handle = handle;
        _devolver = devolver;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _devolvido, 1) == 1) return;
        _devolver(this);
    }
}

// ── PoolManager (global, singleton) ───────────────────────────────────────────────────────────────

/// <summary>
/// Pool GLOBAL de instâncias ACBrLib, dimensionado por CONCORRÊNCIA (não por configboleto).
///
/// Um único teto de handles nativos (env <c>ACBR_POOL_MAX</c>, default 16) é compartilhado por
/// todas as configurações. Cada handle é AFFINE à config pela qual foi inicializado — fica com o
/// token OAuth nativo quente — e é reusado só quando o hash de credenciais bate. Quando uma config
/// nova precisa de um handle e o teto foi atingido, o handle ocioso menos usado (LRU) é descartado
/// e um novo é criado: nunca reconfiguramos um handle vivo para outro banco (zero state-bleed).
///
/// Assim, 300 empresas / centenas de configs NÃO acumulam instâncias dormentes: o custo é limitado
/// ao número de operações simultâneas, não ao número de configs. Para escalar sob carga, basta subir
/// <c>ACBR_POOL_MAX</c> (sem redeploy).
///
/// Thread-safe. Singleton de processo.
/// </summary>
public sealed class PoolManager : IDisposable
{
    private const int DefaultMaxHandles = 16;
    private const int MaxAllowed = 256;
    private const int IdleEvictAfterMinutes = 10;
    private const int DefaultWaitSec = 30;

    private readonly ILogger<PoolManager> _logger;
    private readonly int _maxHandles;

    private readonly SemaphoreSlim _sem;          // gate de concorrência = teto de handles
    private readonly object _lock = new();
    private readonly List<AcbrLibHandle> _idle = new();   // handles ociosos, prontos para reuso
    private int _totalCount;                       // handles vivos (ociosos + alugados + reservados)
    private int _slotSeq;                           // sequência só para nomear logs de diagnóstico
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "acbr_ini");

    // último uso por configId — telemetria (não governa o ciclo de vida dos handles)
    internal readonly ConcurrentDictionary<int, DateTime> _lastUsed = new();

    private readonly Timer _evictTimer;
    private bool _disposed;

    public PoolManager(ILogger<PoolManager> logger)
        : this(logger, LerMaxHandles(logger)) { }

    // Teto explícito — usado pelos testes de concorrência, que precisam de um cap pequeno
    // e determinístico. Ler ACBR_POOL_MAX do ambiente não serve: xUnit roda coleções em
    // paralelo no mesmo processo e mexer em variável de ambiente vazaria entre testes.
    internal PoolManager(ILogger<PoolManager> logger, int maxHandles)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxHandles = maxHandles >= 1 ? Math.Min(maxHandles, MaxAllowed) : DefaultMaxHandles;
        _sem = new SemaphoreSlim(_maxHandles, _maxHandles);
        _evictTimer = new Timer(EvictIdleHandles, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        _logger.LogInformation("[PoolManager] Pool global iniciado. Teto de handles={Max} (env ACBR_POOL_MAX).", _maxHandles);
    }

    private static int LerMaxHandles(ILogger logger)
    {
        var raw = Environment.GetEnvironmentVariable("ACBR_POOL_MAX");
        if (string.IsNullOrWhiteSpace(raw)) return DefaultMaxHandles;
        if (int.TryParse(raw.Trim(), out var v) && v >= 1)
            return Math.Min(v, MaxAllowed);
        logger.LogWarning("[PoolManager] ACBR_POOL_MAX='{Raw}' inválido — usando default {Default}.", raw, DefaultMaxHandles);
        return DefaultMaxHandles;
    }

    // ── Aluguel ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna um lease de instância para a config informada. Reusa um handle ocioso já
    /// configurado para o mesmo hash de credenciais; senão cria um novo (descartando o
    /// handle ocioso LRU quando o teto global foi atingido).
    /// </summary>
    public async Task<AcbrLibLease> AlugarAsync(ConfigBoleto cfg, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidarConfig(cfg);

        var hash = cfg.ComputarHash();
        var waitSec = cfg.poolWaitTimeoutSec > 0 ? cfg.poolWaitTimeoutSec : DefaultWaitSec;
        var timeout = TimeSpan.FromSeconds(waitSec);

        if (!await _sem.WaitAsync(timeout, ct))
        {
            _logger.LogWarning("[PoolManager] Pool ESGOTADO ({Max} handles ocupados) — timeout de {Sec}s para config {Id}. " +
                               "Considere aumentar ACBR_POOL_MAX.", _maxHandles, waitSec, cfg.id);
            throw new PoolTimeoutException(cfg.id, timeout);
        }

        AcbrLibHandle? handle;
        AcbrLibHandle? descartar = null;
        bool criar = false;
        lock (_lock)
        {
            handle = TakeIdlePorHash_NoLock(hash);
            if (handle == null)
            {
                // Nenhum handle quente para esta config: vamos criar um. Se já estamos no teto,
                // descarta o ocioso LRU para abrir espaço (sempre há um ocioso aqui, pois
                // alugados ≤ permits emitidos < teto).
                if (_totalCount >= _maxHandles)
                    descartar = RemoverLruOcioso_NoLock();
                _totalCount++;        // reserva o slot antes de criar fora do lock
                criar = true;
            }
        }

        descartar?.Dispose();   // dispose nativo fora do lock

        if (criar)
        {
            try
            {
                handle = (FabricaHandle ?? CriarInstancia)(cfg, hash);
            }
            catch
            {
                lock (_lock) { _totalCount--; }
                try { _sem.Release(); } catch (ObjectDisposedException) { }
                throw;
            }
        }

        _lastUsed[cfg.id] = DateTime.UtcNow;
        _logger.LogDebug("[PoolManager] config {Id}: handle alugado ({Warm}). Livres={N}/{Max}",
            cfg.id, criar ? "novo" : "quente", _sem.CurrentCount, _maxHandles);
        return new AcbrLibLease(handle!, Devolver);
    }

    private void Devolver(AcbrLibLease lease)
    {
        var h = lease.Handle;

        // Handle inutilizável para reuso — descarta sem reenfileirar (libera o slot do teto; o
        // próximo aluguel cria um handle limpo). Dois motivos:
        //  • Faulted: estado nativo possivelmente corrompido (evita access violation 0xC0000005).
        //  • RelatorioGerado: o motor de relatório Fortes suja o reuso (PDF comprimido/multipágina
        //    na 2ª geração no mesmo handle). Não há API de reset; só descartar resolve.
        if (h.Faulted || h.RelatorioGerado)
        {
            if (h.Faulted)
                _logger.LogWarning("[PoolManager] config {Id}: handle com falha — descartado (não reutilizado).", h.ConfigId);
            else
                _logger.LogDebug("[PoolManager] config {Id}: handle descartado após geração de relatório (evita PDF corrompido no reuso).", h.ConfigId);
            try { h.Dispose(); } catch { }
            lock (_lock) { _totalCount--; }
            try { _sem.Release(); } catch (ObjectDisposedException) { }
            return;
        }

        // Limpa o estado transitório (lista de títulos) para devolver o handle neutro.
        // Se a limpeza falhar, o handle ficou faulted → descarta como acima.
        try
        {
            h.LimparLista();
        }
        catch
        {
            _logger.LogWarning("[PoolManager] config {Id}: falha ao limpar handle na devolução — descartado.", h.ConfigId);
            try { h.Dispose(); } catch { }
            lock (_lock) { _totalCount--; }
            try { _sem.Release(); } catch (ObjectDisposedException) { }
            return;
        }

        h.LastUsed = DateTime.UtcNow;

        bool poolDisposed;
        lock (_lock)
        {
            poolDisposed = _disposed;
            if (!poolDisposed)
                _idle.Add(h);
        }

        if (poolDisposed)
        {
            // Pool foi destruído enquanto o lease estava ativo — descarta o handle agora.
            h.Dispose();
            return;
        }

        try
        {
            _sem.Release();
            _logger.LogDebug("[PoolManager] config {Id}: handle devolvido ao conjunto ocioso. Livres={N}/{Max}",
                h.ConfigId, _sem.CurrentCount, _maxHandles);
        }
        catch (ObjectDisposedException)
        {
            // _sem foi destruído entre o unlock e o Release — descarta o handle.
            h.Dispose();
        }
    }

    // ── Seleção / evicção de ociosos (sob _lock) ────────────────────────────────

    private AcbrLibHandle? TakeIdlePorHash_NoLock(string hash)
    {
        for (int i = 0; i < _idle.Count; i++)
        {
            if (_idle[i].ConfigHash == hash)
            {
                var h = _idle[i];
                _idle.RemoveAt(i);
                return h;
            }
        }
        return null;
    }

    private AcbrLibHandle? RemoverLruOcioso_NoLock()
    {
        if (_idle.Count == 0) return null;
        int lru = 0;
        for (int i = 1; i < _idle.Count; i++)
            if (_idle[i].LastUsed < _idle[lru].LastUsed) lru = i;

        var h = _idle[lru];
        _idle.RemoveAt(lru);
        _totalCount--;
        _logger.LogDebug("[PoolManager] Teto atingido — descartando handle ocioso LRU da config {Id}.", h.ConfigId);
        return h;
    }

    /// <summary>Descarta os handles ociosos de um configId (ex: após rotação de credenciais).</summary>
    public void Invalidar(int configId)
    {
        var remover = new List<AcbrLibHandle>();
        lock (_lock)
        {
            for (int i = _idle.Count - 1; i >= 0; i--)
            {
                if (_idle[i].ConfigId == configId)
                {
                    remover.Add(_idle[i]);
                    _idle.RemoveAt(i);
                    _totalCount--;
                }
            }
        }
        foreach (var h in remover) { try { h.Dispose(); } catch { } }
        if (remover.Count > 0)
            _logger.LogInformation("[PoolManager] config {Id}: {N} handle(s) ocioso(s) invalidado(s).", configId, remover.Count);
    }

    // ── Criação de instância ────────────────────────────────────────────────────

    /// <summary>
    /// Seam de teste: substitui a criação do handle nativo. Nulo em produção, onde o
    /// caminho é sempre <see cref="CriarInstancia"/>. Permite exercitar lease exclusivo,
    /// afinidade por hash, teto/LRU e descarte de handle faulted sem a ACBrLibBoleto64.
    /// </summary>
    internal Func<ConfigBoleto, string, AcbrLibHandle>? FabricaHandle { get; set; }

    private static readonly object _logLock = new();

    private AcbrLibHandle CriarInstancia(ConfigBoleto cfg, string hash)
    {
        var slot = Interlocked.Increment(ref _slotSeq);
        var instanceDir = Path.Combine(_baseDir, cfg.id.ToString(), Guid.NewGuid().ToString("N"));

        var iniPath = GerarIniTemp(cfg, instanceDir, slot, out var pastaLog, out var logFile);
        var diagPath = Path.Combine(pastaLog, $"cfg{cfg.id}_{slot}_acbr_{DateTime.Today:yyyyMMdd}.log");
        var iniConteudo = File.ReadAllText(iniPath);

        var cedenteIni = StxSerializer.GerarCedenteIniMinimal(cfg);
        var cedentePath = Path.Combine(instanceDir, $"cedente_{slot}.ini");
        File.WriteAllText(cedentePath, cedenteIni);
        var cedenteConteudo = File.ReadAllText(cedentePath);

        _logger.LogInformation("[PoolManager] config {Id}#{N} criando handle. DiagLog='{Path}'", cfg.id, slot, diagPath);

        AcbrLog(diagPath, $"=== INICIALIZAR === iniPath={iniPath}\r\n{iniConteudo}");

        var handle = new AcbrLibHandle(cfg.caminhoACBrLib)
        {
            CedentePath = cedentePath,
            ConfigHash = hash,
            ConfigId = cfg.id,
            LastUsed = DateTime.UtcNow,
        };
        handle.Inicializar(iniPath);

        var urInit = TentarLerValor(() => handle.UltimoRetorno(), "UltimoRetorno-PosInit");
        var versao = TentarLerValor(() => handle.Versao(), "Versao");
        AcbrLog(diagPath, $"UltimoRetorno pós-Inicializar: '{urInit}'");
        AcbrLog(diagPath, $"Versao={versao}");

        TentarGravarValor(handle, diagPath, "Principal", "LogNivel", cfg.nivelLog.ToString());
        TentarGravarValor(handle, diagPath, "Principal", "LogPath", logFile);

        AcbrLog(diagPath, $"=== CONFIGURAR_DADOS === cedentePath={cedentePath}\r\n{cedenteConteudo}");

        try
        {
            // ConfigImportar aplica as seções globais ([BoletoWebSevice]/[BoletoCedenteWS] — VersaoDF,
            // credenciais); ConfigurarDados aplica [Cedente]/[Conta]/[Banco]. Feito UMA vez por handle,
            // sobre um handle recém-criado e limpo — nunca reaplicado sobre handle faulted.
            handle.ConfigImportar(cedentePath);
            AcbrLog(diagPath, "ConfigImportar: OK");

            handle.ConfigurarDados(cedentePath);
            AcbrLog(diagPath, "ConfigurarDados: OK");
            _logger.LogInformation("[PoolManager] config {Id}#{N} ConfigurarDados OK", cfg.id, slot);
        }
        catch (Exception ex)
        {
            AcbrLog(diagPath, $"ConfigurarDados ERRO: {ex.Message}\r\n{ex}");
            _logger.LogError(ex, "[PoolManager] config {Id}#{N} ConfigurarDados falhou", cfg.id, slot);
            try { handle.Dispose(); } catch { }
            throw;
        }

        var configExp = TentarLerValor(() => handle.ConfigExportar(), "ConfigExportar");
        AcbrLog(diagPath, $"ConfigExportar (pós-ConfigurarDados):\r\n{configExp}");

        return handle;
    }

    private string GerarIniTemp(ConfigBoleto cfg, string instanceDir, int slot, out string pastaLog, out string logFile)
    {
        pastaLog = Path.GetFullPath(cfg.pastaLog);
        var pastaOutput = Path.GetFullPath(cfg.pastaOutput);

        Directory.CreateDirectory(instanceDir);
        Directory.CreateDirectory(pastaLog);
        Directory.CreateDirectory(pastaOutput);

        logFile = Path.Combine(pastaLog, $"cfg{cfg.id}_{slot}.log");

        var conteudo = $"""
            [Principal]
            LogNivel={cfg.nivelLog}
            LogPath={logFile}
            CodificacaoResposta=0

            [BoletoDiretorioConfig]
            DirArqRemessa={pastaOutput}
            DirArqRetorno={pastaOutput}
            """;

        var path = Path.Combine(instanceDir, $"boleto_{slot}.ini");
        File.WriteAllText(path, conteudo);
        return path;
    }

    private static string TentarLerValor(Func<string> fn, string nome)
    {
        try { return fn(); }
        catch (Exception ex) { return $"[ERRO {nome}: {ex.Message}]"; }
    }

    private void TentarGravarValor(AcbrLibHandle handle, string diagPath, string sessao, string chave, string valor)
    {
        try
        {
            handle.ConfigGravarValor(sessao, chave, valor);
            AcbrLog(diagPath, $"ConfigGravarValor [{sessao}]/{chave}={valor}: OK");
        }
        catch (Exception ex)
        {
            AcbrLog(diagPath, $"ConfigGravarValor [{sessao}]/{chave} ERRO: {ex.Message}");
        }
    }

    private void AcbrLog(string diagPath, string msg)
    {
        _logger.LogDebug("[Diag] {Msg}", msg);
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {msg}";
            lock (_logLock)
                File.AppendAllText(diagPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Diag] Falha ao gravar '{Path}': {Err}", diagPath, ex.Message);
        }
    }

    // ── Validação de config ─────────────────────────────────────────────────────

    private static void ValidarConfig(ConfigBoleto cfg)
    {
        var erros = new List<string>();

        // Normaliza campos opcionais que GeneXus envia como 0/"" quando não mapeados na tabela
        if (cfg.poolWaitTimeoutSec == 0) cfg.poolWaitTimeoutSec = DefaultWaitSec;
        if (cfg.nivelLog == 0) cfg.nivelLog = 1;

        // Caminhos: o valor da config tem prioridade; em branco herda o default do container
        // (variável de ambiente). Assim "igual na maioria das configs" vira um default de
        // ambiente, e o campo só é preenchido na config quando se quer sobrescrever uma específica.
        cfg.pastaLog = ResolverCaminho(cfg.pastaLog, "ACBR_LOG_DIR", Path.Combine(Path.GetTempPath(), "acbr_boleto", "logs"));
        cfg.pastaOutput = ResolverCaminho(cfg.pastaOutput, "ACBR_OUTPUT_DIR", Path.Combine(Path.GetTempPath(), "acbr_boleto", "output"));
        // Pasta de logotipos: compartilhada por todas as configs (default de container).
        // Sem fallback: em branco, a linha DirLogo é omitida do INI e o DLL usa seu próprio lookup.
        cfg.dirLogo = ResolverCaminho(cfg.dirLogo, "ACBR_LOGO_DIR");
        cfg.caminhoACBrLib = ResolverCaminho(cfg.caminhoACBrLib, "ACBR_LIB_PATH");

        // Detecta se pastaLog/pastaOutput apontam para um arquivo existente (erro de config no GeneXus)
        if (File.Exists(cfg.pastaLog)) erros.Add($"campo 'pastaLog' aponta para um arquivo, não uma pasta: '{cfg.pastaLog}'.");
        if (File.Exists(cfg.pastaOutput)) erros.Add($"campo 'pastaOutput' aponta para um arquivo, não uma pasta: '{cfg.pastaOutput}'.");

        if (string.IsNullOrWhiteSpace(cfg.caminhoACBrLib)) erros.Add("campo 'caminhoACBrLib' obrigatório (preencha na config ou defina a env ACBR_LIB_PATH no container).");
        else if (!File.Exists(cfg.caminhoACBrLib)) erros.Add($"campo 'caminhoACBrLib': arquivo não encontrado em '{cfg.caminhoACBrLib}'.");
        if (cfg.codbanco == 0) erros.Add("campo 'codbanco' obrigatório.");
        if (string.IsNullOrWhiteSpace(cfg.agencia)) erros.Add("campo 'agencia' obrigatório.");
        if (string.IsNullOrWhiteSpace(cfg.conta)) erros.Add("campo 'conta' obrigatório.");
        if (string.IsNullOrWhiteSpace(cfg.carteira)) erros.Add("campo 'carteira' obrigatório.");
        if (string.IsNullOrWhiteSpace(cfg.ws_clientid)) erros.Add("campo 'ws_clientid' obrigatório.");
        if (string.IsNullOrWhiteSpace(cfg.ws_clientsecret)) erros.Add("campo 'ws_clientsecret' obrigatório.");
        if (cfg.ws_ambiente < 0 || cfg.ws_ambiente > 2) erros.Add($"campo 'ws_ambiente' inválido ({cfg.ws_ambiente}): use 0=Produção, 1=Homologação, 2=Sandbox.");
        if (cfg.tipocobranca <= 0) erros.Add("campo 'tipocobranca' obrigatório: banco não selecionado (ex: 6=Itaú, 5=Bradesco).");
        if (cfg.usecertificatehttp == 1)
        {
            if (string.IsNullOrWhiteSpace(cfg.arquivocrt)) erros.Add("campo 'arquivocrt' obrigatório quando usecertificatehttp=1.");
            else if (!File.Exists(cfg.arquivocrt)) erros.Add($"campo 'arquivocrt': arquivo não encontrado em '{cfg.arquivocrt}'.");
            if (string.IsNullOrWhiteSpace(cfg.arquivokey)) erros.Add("campo 'arquivokey' obrigatório quando usecertificatehttp=1.");
            else if (!File.Exists(cfg.arquivokey)) erros.Add($"campo 'arquivokey': arquivo não encontrado em '{cfg.arquivokey}'.");
        }

        if (erros.Count > 0)
            throw new ConfigInvalidaException(cfg.id, string.Join(" | ", erros));
    }

    // Resolve um caminho: usa o valor da config se preenchido; senão herda o default do
    // container via variável de ambiente; senão usa o fallback (ou "" quando obrigatório, deixando
    // a validação acusar a ausência). O caminho resolvido entra em ComputarHash (ValidarConfig roda
    // antes), então trocar a env ou preencher o campo recria o handle no próximo aluguel.
    private static string ResolverCaminho(string? doConfig, string envVar, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(doConfig)) return doConfig;
        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return fallback ?? string.Empty;
    }

    // ── Evicção por idle ────────────────────────────────────────────────────────

    // Exposto internamente para testes: permite disparar a evicção sem esperar o timer.
    internal void TriggerEviction() => EvictIdleHandles(null);

    // Exposto internamente para testes: número de handles vivos (ociosos + alugados).
    internal int ActivePoolCount
    {
        get { lock (_lock) { return _totalCount; } }
    }

    // Exposto internamente para testes: handles ociosos prontos para reuso.
    internal int IdleCount
    {
        get { lock (_lock) { return _idle.Count; } }
    }

    private void EvictIdleHandles(object? _)
    {
        if (_disposed) return;
        var cutoff = DateTime.UtcNow.AddMinutes(-IdleEvictAfterMinutes);
        var remover = new List<AcbrLibHandle>();
        lock (_lock)
        {
            for (int i = _idle.Count - 1; i >= 0; i--)
            {
                if (_idle[i].LastUsed < cutoff)
                {
                    remover.Add(_idle[i]);
                    _idle.RemoveAt(i);
                    _totalCount--;
                }
            }
        }
        foreach (var h in remover) { try { h.Dispose(); } catch { } }
        if (remover.Count > 0)
            _logger.LogInformation("[PoolManager] {N} handle(s) ocioso(s) >{Min}min evictado(s).", remover.Count, IdleEvictAfterMinutes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _evictTimer.Dispose();

        List<AcbrLibHandle> remover;
        lock (_lock)
        {
            remover = new List<AcbrLibHandle>(_idle);
            _idle.Clear();
            _totalCount = 0;
        }
        foreach (var h in remover) { try { h.Dispose(); } catch { } }
        _sem.Dispose();
    }
}
