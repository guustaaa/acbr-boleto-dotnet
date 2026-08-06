using ACBrBoleto.Core.Pool;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACBrBoleto.GeneXus;

/// <summary>
/// Inicializa o container de DI do processo.
/// Singleton de processo — apenas Pool + Logger (sem banco de dados).
/// </summary>
internal static class Bootstrapper
{
    // Timestamp de compilação — muda a cada build, confirma qual DLL está em execução
    internal static readonly string BuildTimestamp = typeof(Bootstrapper).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "BuildTime")?.Value
        ?? System.IO.File.GetLastWriteTime(typeof(Bootstrapper).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss");

    private static volatile ServiceProvider? _provider;
    private static readonly object _lock = new();

    /// <summary>
    /// Garante inicialização automática na primeira chamada.
    /// Nível de log configurável via variável de ambiente ACBR_LOG_LEVEL
    /// (Debug | Information | Warning | Error). Padrão: Information.
    /// </summary>
    internal static void EnsureInicializado()
    {
        if (_provider is not null) return;
        var nivel = Environment.GetEnvironmentVariable("ACBR_LOG_LEVEL") ?? "Information";
        Inicializar(nivel);
    }

    internal static void Inicializar(string nivelLog = "Information")
    {
        if (_provider is not null) return;
        lock (_lock)
        {
            if (_provider is not null) return;

            var services = new ServiceCollection();

            services.AddLogging(b =>
            {
                b.SetMinimumLevel(ParseNivel(nivelLog));
                b.AddConsole();
                // Diretório do log gerenciado: mesmo volume dos logs nativos (ACBR_LOG_DIR,
                // ex. PVC /boletos-dir/logs). Em branco → fallback ao dir do app (efêmero).
                var logDir = Environment.GetEnvironmentVariable("ACBR_LOG_DIR");
                logDir = string.IsNullOrWhiteSpace(logDir)
                    ? Path.Combine(AppContext.BaseDirectory, "logs")
                    : Path.GetFullPath(logDir);
                b.AddProvider(new FileLogProvider(
                    Path.Combine(logDir, "acbrboleto-.log")));
            });

            // Sem IConfiguracaoEmpresaRepository — config vem do JSON do GeneXus
            services.AddSingleton<PoolManager>();

            _provider = services.BuildServiceProvider(validateScopes: true);

            var log = _provider.GetRequiredService<ILogger<object>>();
            log.LogInformation("ACBrBoleto.GeneXus v3 inicializado. LogLevel={L} DLL={Dll} Build={Ts}",
                nivelLog,
                typeof(Bootstrapper).Assembly.Location,
                BuildTimestamp);
        }
    }

    internal static PoolManager GetPool()
    {
        if (_provider is null)
            throw new InvalidOperationException(
                "Bootstrapper não inicializado. Chame BoletoEntryPoint.Inicializar() primeiro.");
        return _provider.GetRequiredService<PoolManager>();
    }

    internal static ILogger<T> GetLogger<T>() =>
        _provider?.GetRequiredService<ILogger<T>>()
        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

    internal static void Encerrar()
    {
        lock (_lock)
        {
            _provider?.Dispose();
            _provider = null;
        }
    }

    private static LogLevel ParseNivel(string s) => s.ToLowerInvariant() switch
    {
        "debug" => LogLevel.Debug,
        "information" => LogLevel.Information,
        "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        _ => LogLevel.Information
    };
}

// ── Logger de arquivo rotacionado por dia ─────────────────────────────────────

internal sealed class FileLogProvider : ILoggerProvider
{
    private readonly string _tpl;
    public FileLogProvider(string tpl)
    {
        _tpl = tpl;
        Directory.CreateDirectory(Path.GetDirectoryName(tpl) ?? ".");
    }
    public ILogger CreateLogger(string cat) => new FileLogger(_tpl, cat);
    public void Dispose() { }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _tpl;
    private readonly string _cat;
    private static readonly object _flock = new();

    public FileLogger(string tpl, string cat) { _tpl = tpl; _cat = cat; }
    public IDisposable? BeginScope<T>(T state) where T : notnull => null;
    public bool IsEnabled(LogLevel l) => l >= LogLevel.Debug;

    public void Log<T>(LogLevel level, EventId id, T state, Exception? ex, Func<T, Exception?, string> fmt)
    {
        if (!IsEnabled(level)) return;
        // Substitui a data SÓ no nome do arquivo — o diretório pode conter '-' (ex. /boletos-dir).
        var dir = Path.GetDirectoryName(_tpl) ?? ".";
        var name = Path.GetFileName(_tpl).Replace("-", $"-{DateTime.Today:yyyyMMdd}");
        var path = Path.Combine(dir, name);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-11}] {_cat} | {fmt(state, ex)}";
        if (ex != null) line += $"\n  ↳ {ex}";
        lock (_flock)
        {
            try { File.AppendAllText(path, line + Environment.NewLine); }
            catch { }
        }
    }
}
