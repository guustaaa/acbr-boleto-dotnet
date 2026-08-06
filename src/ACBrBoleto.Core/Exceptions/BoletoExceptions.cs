namespace ACBrBoleto.Core.Exceptions;

/// <summary>Exceção base do domínio.</summary>
public class AcbrBoletoException : Exception
{
    public string? Operacao    { get; }
    public int?    ConfigId    { get; }

    public AcbrBoletoException(string message, string? operacao = null, int? configId = null)
        : base(message) { Operacao = operacao; ConfigId = configId; }

    public AcbrBoletoException(string message, Exception inner, string? operacao = null, int? configId = null)
        : base(message, inner) { Operacao = operacao; ConfigId = configId; }
}

/// <summary>Erro retornado diretamente pela ACBrLibBoleto nativa.</summary>
public sealed class AcbrLibException : AcbrBoletoException
{
    public int CodigoRetorno { get; }

    public AcbrLibException(string msgLib, int codigoRetorno, string operacao, int? configId = null)
        : base($"[{operacao}] código {codigoRetorno}: {msgLib}", operacao, configId)
        => CodigoRetorno = codigoRetorno;
}

/// <summary>JSON de entrada inválido ou com campos obrigatórios ausentes.</summary>
public sealed class InputInvalidoException : AcbrBoletoException
{
    public InputInvalidoException(string campo, string detalhe)
        : base($"Campo '{campo}' inválido: {detalhe}") { }
}

/// <summary>Timeout aguardando instância disponível no pool.</summary>
public sealed class PoolTimeoutException : AcbrBoletoException
{
    public PoolTimeoutException(int configId, TimeSpan timeout)
        : base($"Timeout de {timeout.TotalSeconds:F0}s aguardando instância ACBrLib para config {configId}.",
               configId: configId) { }
}

/// <summary>ConfigBoleto inválida — campos obrigatórios ausentes.</summary>
public sealed class ConfigInvalidaException : AcbrBoletoException
{
    public ConfigInvalidaException(int configId, string detalhe)
        : base($"ConfigBoleto id={configId} inválida: {detalhe}", configId: configId) { }
}
