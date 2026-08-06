using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using ACBrBoleto.Core.Enums;
using ACBrBoleto.Core.Exceptions;

namespace ACBrBoleto.Core.Interop;

/// <summary>
/// Encapsula uma instância da ACBrLibBoleto64.dll nativa (Delphi/Lazarus, Cdecl).
/// Carregada dinamicamente pelo caminho passado em ConfigBoleto.caminhoACBrLib.
///
/// Cada instância representa um handle INDEPENDENTE da lib — existe um por slot no pool.
/// Não é thread-safe: usar sob lock do pool (SemaphoreSlim).
///
/// Nomes e assinaturas baseados nos demos oficiais:
///   Imports/Dinamico/ST/ACBrBoleto.Delegates.cs  (CallingConvention.Cdecnref int bufferSize)
///   Imports/Estatico/ACBrBoleto.cs
/// </summary>
internal sealed class AcbrLibHandle : IDisposable
{
    // ── Tamanhos de buffer ────────────────────────────────────────────
    private const int SmallBuf = 4_096;
    private const int LargeBuf = 65_536;

    // ── NativeLibrary (cross-platform: Windows + Linux) ───────────────

    // ── Convenção de chamada (Cdecl — conforme demos oficiais ACBr) ───
    private const CallingConvention CC = CallingConvention.Cdecl;

    // A ACBrLib em modo UTF8 (CodificacaoResposta=0) retorna texto UTF-8.
    // Usar CharSet.Ansi causa truncamento em `StringBuilder` interop.
    // Deixaremos o P/Invoke com CharSet.Ansi e decodificaremos os bytes UTF-8 no wrapper.
    private const CharSet CS = CharSet.Ansi;

    // ── Delegates — um por função exportada ──────────────────────────
    // Ciclo de vida
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_Inicializar(ref IntPtr handle, string eArqConfig, string eChaveCrypt);
    [UnmanagedFunctionPointer(CC)] delegate int D_Finalizar(IntPtr handle);

    // Informações
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_Nome(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_Versao(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_UltimoRetorno(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_OpenSSLInfo(IntPtr handle, StringBuilder sb, ref int len);

    // Configuração (arquivo INI)
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConfigLer(IntPtr handle, string eArqConfig);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConfigGravar(IntPtr handle, string eArqConfig);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConfigImportar(IntPtr handle, string eArqConfig);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConfigExportar(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConfigLerValor(IntPtr handle, string sessao, string chave, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConfigGravarValor(IntPtr handle, string sessao, string chave, string valor);

    // Cedente + banco (aceita path OU conteúdo do INI)
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConfigurarDados(IntPtr handle, string eArquivoIni);

    // Títulos
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_IncluirTitulos(IntPtr handle, string eArquivoIni, string eTpSaida);
    [UnmanagedFunctionPointer(CC)] delegate int D_LimparLista(IntPtr handle);
    [UnmanagedFunctionPointer(CC)] delegate int D_TotalTitulosLista(IntPtr handle);

    // Impressão / PDF
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_Imprimir(IntPtr handle, string eNomeImpressora);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ImprimirBoleto(IntPtr handle, int eIndice, string eNomeImpressora);
    [UnmanagedFunctionPointer(CC)] delegate int D_GerarPDF(IntPtr handle);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_SalvarPDF(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC)] delegate int D_GerarPDFBoleto(IntPtr handle, int eIndice);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_SalvarPDFBoleto(IntPtr handle, int eIndice, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC)] delegate int D_GerarHTML(IntPtr handle);

    // CNAB
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_GerarRemessa(IntPtr handle, string eDir, int eNumArquivo, string eNomeArq);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_GerarRemessaStream(IntPtr handle, int eNumArquivo, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ObterRetorno(IntPtr handle, string eDir, string eNomeArq, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_LerRetorno(IntPtr handle, string eDir, string eNomeArq);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_LerRetornoStream(IntPtr handle, string aRetornoBase64, StringBuilder sb, ref int len);

    // E-mail
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_EnviarEmail(IntPtr handle, string ePara, string eAssunto, string eMensagem, string eCC);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_EnviarEmailBoleto(IntPtr handle, int eIndice, string ePara, string eAssunto, string eMensagem, string eCC);

    // Diretório / arquivo
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_SetDiretorioArquivo(IntPtr handle, string eDir, string eArq);

    // Utilitários
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ListaBancos(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ListaCaractTitulo(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ListaOcorrencias(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ListaOcorrenciasEX(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_CodigosMoraAceitos(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_SelecionaBanco(IntPtr handle, string eCodBanco);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_TamNossoNumero(IntPtr handle, string eCarteira, string eNossoNumero, string eConvenio);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_MontarNossoNumero(IntPtr handle, int eIndice, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_RetornaLinhaDigitavel(IntPtr handle, int eIndice, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_RetornaCodigoBarras(IntPtr handle, int eIndice, StringBuilder sb, ref int len);

    // WebService (operação única — código define a operação)
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_EnviarBoleto(IntPtr handle, int eCodigoOperacao, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_ConsultarTitulosPorPeriodo(IntPtr handle, string eArquivoIni, StringBuilder sb, ref int len);

    // Token
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_GerarToken(IntPtr handle, StringBuilder sb, ref int len);
    [UnmanagedFunctionPointer(CC, CharSet = CS)] delegate int D_InformarToken(IntPtr handle, string eToken, double eDataValidade);

    // ── Instâncias dos delegates ──────────────────────────────────────
    private D_Inicializar _inicializar = null!;
    private D_Finalizar _finalizar = null!;
    private D_Nome _nome = null!;
    private D_Versao _versao = null!;
    private D_UltimoRetorno _ultimoRetorno = null!;
    private D_OpenSSLInfo _openSSLInfo = null!;
    private D_ConfigLer _configLer = null!;
    private D_ConfigGravar _configGravar = null!;
    private D_ConfigImportar _configImportar = null!;
    private D_ConfigExportar _configExportar = null!;
    private D_ConfigLerValor _configLerValor = null!;
    private D_ConfigGravarValor _configGravarValor = null!;
    private D_ConfigurarDados _configurarDados = null!;
    private D_IncluirTitulos _incluirTitulos = null!;
    private D_LimparLista _limparLista = null!;
    private D_TotalTitulosLista _totalTitulosLista = null!;
    private D_Imprimir _imprimir = null!;
    private D_ImprimirBoleto _imprimirBoleto = null!;
    private D_GerarPDF _gerarPDF = null!;
    private D_SalvarPDF _salvarPDF = null!;
    private D_GerarPDFBoleto _gerarPDFBoleto = null!;
    private D_SalvarPDFBoleto _salvarPDFBoleto = null!;
    private D_GerarHTML _gerarHTML = null!;
    private D_GerarRemessa _gerarRemessa = null!;
    private D_GerarRemessaStream _gerarRemessaStream = null!;
    private D_ObterRetorno _obterRetorno = null!;
    private D_LerRetorno _lerRetorno = null!;
    private D_LerRetornoStream _lerRetornoStream = null!;
    private D_EnviarEmail _enviarEmail = null!;
    private D_EnviarEmailBoleto _enviarEmailBoleto = null!;
    private D_SetDiretorioArquivo _setDiretorioArquivo = null!;
    private D_ListaBancos _listaBancos = null!;
    private D_ListaCaractTitulo _listaCaractTitulo = null!;
    private D_ListaOcorrencias _listaOcorrencias = null!;
    private D_ListaOcorrenciasEX _listaOcorrenciasEX = null!;
    private D_CodigosMoraAceitos _codigosMoraAceitos = null!;
    private D_SelecionaBanco _selecionaBanco = null!;
    private D_TamNossoNumero _tamNossoNumero = null!;
    private D_MontarNossoNumero _montarNossoNumero = null!;
    private D_RetornaLinhaDigitavel _retornaLinhaDigitavel = null!;
    private D_RetornaCodigoBarras _retornaCodigoBarras = null!;
    private D_EnviarBoleto _enviarBoleto = null!;
    private D_ConsultarTitulosPorPeriodo _consultarPeriodo = null!;
    private D_GerarToken _gerarToken = null!;
    private D_InformarToken _informarToken = null!;

    // ── Estado ────────────────────────────────────────────────────────
    private IntPtr _hLib;
    private IntPtr _libHandle;
    private bool _inicializado;
    private bool _disposed;

    // Caminho do cedente_{slot}.ini gravado pelo pool — reutilizado antes de cada IncluirTitulos.
    internal string CedentePath { get; set; } = string.Empty;

    // ── Metadados do pool ─────────────────────────────────────────────
    // O handle é AFFINE a uma configuração: fica vinculado ao hash/ id de credenciais
    // pelos quais foi inicializado (token OAuth nativo continua quente). O pool global
    // reusa um handle ocioso só quando o hash bate; nunca reconfigura para outro banco.
    internal string ConfigHash { get; set; } = string.Empty;
    internal int ConfigId { get; set; }
    internal DateTime LastUsed { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True quando uma operação NATIVA com estado (WS/config/lista/títulos) falhou.
    /// A DLL Delphi pode deixar o handle em estado corrompido após tal falha; reusá-lo
    /// dispara access violation (0xC0000005). O pool DEVE descartar um handle faulted e
    /// criar um novo em vez de devolvê-lo ao conjunto ocioso. Ver PoolManager.Devolver.
    /// </summary>
    internal bool Faulted { get; private set; }

    /// <summary>
    /// True após gerar relatório/PDF/HTML/impressão. O motor de relatório (Fortes) da DLL acumula
    /// estado entre gerações no MESMO handle — a 2ª geração sai "comprimida"/multipágina. Não há
    /// API de reset; o pool descarta o handle após uma geração para que a próxima use estado limpo
    /// (igual ao caso de handle novo, que funciona). Ver PoolManager.Devolver.
    /// </summary>
    internal bool RelatorioGerado { get; private set; }

    // ── Construtor ────────────────────────────────────────────────────

    static AcbrLibHandle()
    {
        // Required for Encoding.GetEncoding(1252) in .NET Core / .NET 5+
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // Em Linux a ACBrLib é build LCL/GTK2: dlclose+dlopen no mesmo processo re-inicializa o GTK e
    // aborta (SIGABRT no 2º GerarPdf). Por isso o módulo é carregado UMA vez por processo e nunca
    // liberado — o GTK inicializa uma só vez. Em Windows mantemos load/Free por handle, que é o que
    // garante PDF limpo no reuso (quirk Fortes). Ver Dispose.
    private static readonly ConcurrentDictionary<string, IntPtr> _modulosLinux = new();

    public AcbrLibHandle(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
            throw new ArgumentException("Caminho da ACBrLibBoleto não informado.", nameof(dllPath));

        try
        {
            _hLib = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? NativeLibrary.Load(dllPath)
                : _modulosLinux.GetOrAdd(dllPath, NativeLibrary.Load);
        }
        catch (Exception ex)
        {
            // ex.Message traz o motivo real do loader (ex.: "libgtk-x11-2.0.so.0: cannot open
            // shared object file") — em Linux a falha quase sempre é dependência nativa ausente,
            // não o caminho. Surfaça isso na resposta p/ não exigir um `ldd` no pod a cada vez.
            throw new InvalidOperationException(
                $"Falha ao carregar '{dllPath}': {ex.Message} " +
                "(verifique arquitetura 64 bits e dependências nativas — rode 'ldd' no .so).", ex);
        }

        BindDelegates();
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────

    public void Inicializar(string iniPath)
    {
        Check(_inicializar(ref _libHandle, iniPath, string.Empty), "Boleto_Inicializar");
        _inicializado = true;
    }

    public void Finalizar()
    {
        if (!_inicializado) return;
        _finalizar(_libHandle);
        _inicializado = false;
    }

    // ── Informações ───────────────────────────────────────────────────

    public string Nome()
    {
        int Fn(StringBuilder sb, ref int l) => _nome(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_Nome");
    }

    public string Versao()
    {
        int Fn(StringBuilder sb, ref int l) => _versao(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_Versao");
    }

    public string UltimoRetorno()
    {
        int Fn(StringBuilder sb, ref int l) => _ultimoRetorno(_libHandle, sb, ref l);
        return LerInterno(Fn);
    }

    public string OpenSSLInfo()
    {
        int Fn(StringBuilder sb, ref int l) => _openSSLInfo(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_OpenSSLInfo");
    }

    // ── Configuração ──────────────────────────────────────────────────

    public void ConfigLer(string iniPath) => Check(_configLer(_libHandle, iniPath), "Boleto_ConfigLer");
    public void ConfigGravar(string iniPath) => Check(_configGravar(_libHandle, iniPath), "Boleto_ConfigGravar");
    public void ConfigImportar(string conteudo) => Stateful(() => Check(_configImportar(_libHandle, conteudo), "Boleto_ConfigImportar"));

    public string ConfigExportar()
    {
        int Fn(StringBuilder sb, ref int l) => _configExportar(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_ConfigExportar");
    }

    public string ConfigLerValor(string sessao, string chave)
    {
        int Fn(StringBuilder sb, ref int l) => _configLerValor(_libHandle, sessao, chave, sb, ref l);
        return Ler(Fn, "Boleto_ConfigLerValor");
    }

    public void ConfigGravarValor(string sessao, string chave, string valor) =>
        Check(_configGravarValor(_libHandle, sessao, chave, valor), "Boleto_ConfigGravarValor");

    // DLL inline mode for ConfigurarDados returns -10/Access violation after
    // ConsultarTitulosPorPeriodo failures corrupt handle state (confirmed 2026-05-28).
    // When receiving inline INI content, write to a temp file — same fix as IncluirTitulos.
    public void ConfigurarDados(string iniConteudo) => Stateful(() =>
    {
        if (iniConteudo.TrimStart('\r', '\n', ' ').StartsWith("["))
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"acbr_ced_{Guid.NewGuid():N}.ini");
            try
            {
                File.WriteAllText(tmp, iniConteudo);
                Check(_configurarDados(_libHandle, tmp), "Boleto_ConfigurarDados");
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
        else
        {
            Check(_configurarDados(_libHandle, ToUTF8(iniConteudo)), "Boleto_ConfigurarDados");
        }
    });

    // ── Títulos ───────────────────────────────────────────────────────

    // DLL v1.2.1.446 confirmado (diagnóstico 2026-05-26): IncluirTitulos exige arquivo em disco —
    // não aceita conteúdo inline independente do encoding. Manter arquivo temp.
    public void IncluirTitulos(string iniConteudo, string tpSaida = "") => Stateful(() =>
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"acbr_titulo_{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(tmp, iniConteudo);
            Check(_incluirTitulos(_libHandle, tmp, tpSaida), "Boleto_IncluirTitulos");
        }
        finally { try { File.Delete(tmp); } catch { } }
    });

    public void LimparLista() =>
        Stateful(() => Check(_limparLista(_libHandle), "Boleto_LimparLista"));

    /// <summary>Retorna o número de títulos na lista atual (retorno = contagem).</summary>
    public int TotalTitulosLista() => _totalTitulosLista(_libHandle);

    // ── Impressão / PDF ───────────────────────────────────────────────

    public void Imprimir(string impressora = "")
    {
        RelatorioGerado = true;
        Check(_imprimir(_libHandle, impressora), "Boleto_Imprimir");
    }

    public void ImprimirBoleto(int indice, string impressora = "")
    {
        RelatorioGerado = true;
        Check(_imprimirBoleto(_libHandle, indice, impressora), "Boleto_ImprimirBoleto");
    }

    public void GerarPDF()
    {
        RelatorioGerado = true;
        Check(_gerarPDF(_libHandle), "Boleto_GerarPDF");
    }

    /// <summary>Retorna PDF de todos os títulos em Base64.</summary>
    public string SalvarPDF()
    {
        RelatorioGerado = true;
        int Fn(StringBuilder sb, ref int l) => _salvarPDF(_libHandle, sb, ref l);
        return LerBase64(Fn, "Boleto_SalvarPDF", LargeBuf);
    }

    public void GerarPDFBoleto(int indice)
    {
        RelatorioGerado = true;
        Check(_gerarPDFBoleto(_libHandle, indice), "Boleto_GerarPDFBoleto");
    }

    /// <summary>Retorna PDF do título no índice <paramref name="indice"/> em Base64.</summary>
    public string SalvarPDFBoleto(int indice)
    {
        RelatorioGerado = true;
        int Fn(StringBuilder sb, ref int l) => _salvarPDFBoleto(_libHandle, indice, sb, ref l);
        return Ler(Fn, "Boleto_SalvarPDFBoleto", LargeBuf).Replace("\r", "").Replace("\n", "");
    }

    public void GerarHTML()
    {
        RelatorioGerado = true;
        Check(_gerarHTML(_libHandle), "Boleto_GerarHTML");
    }

    // ── CNAB ──────────────────────────────────────────────────────────

    public void GerarRemessa(string dir, int numArquivo, string nomeArq) =>
        Check(_gerarRemessa(_libHandle, dir, numArquivo, nomeArq), "Boleto_GerarRemessa");

    /// <summary>Retorna o arquivo remessa em Base64.</summary>
    public string GerarRemessaStream(int numArquivo)
    {
        int Fn(StringBuilder sb, ref int l) => _gerarRemessaStream(_libHandle, numArquivo, sb, ref l);
        return LerBase64(Fn, "Boleto_GerarRemessaStream", LargeBuf);
    }

    public void LerRetorno(string dir, string nomeArq) =>
        Check(_lerRetorno(_libHandle, dir, nomeArq), "Boleto_LerRetorno");

    /// <summary>Lê retorno de arquivo Base64 e retorna INI com resultado.</summary>
    public string LerRetornoStream(string retornoBase64)
    {
        int Fn(StringBuilder sb, ref int l) => _lerRetornoStream(_libHandle, retornoBase64, sb, ref l);
        return Ler(Fn, "Boleto_LerRetornoStream", LargeBuf);
    }

    /// <summary>Lê retorno de arquivo e retorna INI com resultado.</summary>
    public string ObterRetorno(string dir, string nomeArq)
    {
        int Fn(StringBuilder sb, ref int l) => _obterRetorno(_libHandle, dir, nomeArq, sb, ref l);
        return Ler(Fn, "Boleto_ObterRetorno", LargeBuf);
    }

    // ── E-mail ────────────────────────────────────────────────────────

    public void EnviarEmail(string para, string assunto, string mensagem, string cc) =>
        Check(_enviarEmail(_libHandle, para, assunto, mensagem, cc), "Boleto_EnviarEmail");

    public void EnviarEmailBoleto(int indice, string para, string assunto, string mensagem, string cc) =>
        Check(_enviarEmailBoleto(_libHandle, indice, para, assunto, mensagem, cc), "Boleto_EnviarEmailBoleto");

    // ── Diretório ─────────────────────────────────────────────────────

    public void SetDiretorioArquivo(string dir, string arq = "") =>
        Check(_setDiretorioArquivo(_libHandle, dir, arq), "Boleto_SetDiretorioArquivo");

    // ── Utilitários ───────────────────────────────────────────────────

    public string[] ListaBancos()
    {
        int Fn(StringBuilder sb, ref int l) => _listaBancos(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_ListaBancos").Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    public string[] ListaCaractTitulo()
    {
        int Fn(StringBuilder sb, ref int l) => _listaCaractTitulo(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_ListaCaractTitulo").Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    public string[] ListaOcorrencias()
    {
        int Fn(StringBuilder sb, ref int l) => _listaOcorrencias(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_ListaOcorrencias").Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    public string[] ListaOcorrenciasEX()
    {
        int Fn(StringBuilder sb, ref int l) => _listaOcorrenciasEX(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_ListaOcorrenciasEX").Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    public string CodigosMoraAceitos()
    {
        int Fn(StringBuilder sb, ref int l) => _codigosMoraAceitos(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_CodigosMoraAceitos");
    }

    public void SelecionaBanco(string codBanco) =>
        Check(_selecionaBanco(_libHandle, codBanco), "Boleto_SelecionaBanco");

    /// <summary>Retorna o tamanho do nosso número (retorno = tamanho).</summary>
    public int TamNossoNumero(string carteira, string nossoNumero, string convenio) =>
        _tamNossoNumero(_libHandle, carteira, nossoNumero, convenio);

    public string MontarNossoNumero(int indice)
    {
        int Fn(StringBuilder sb, ref int l) => _montarNossoNumero(_libHandle, indice, sb, ref l);
        return Ler(Fn, "Boleto_MontarNossoNumero");
    }

    /// <summary>Índice 1-based do título na lista.</summary>
    public string RetornaLinhaDigitavel(int indice)
    {
        int Fn(StringBuilder sb, ref int l) => _retornaLinhaDigitavel(_libHandle, indice, sb, ref l);
        return Ler(Fn, "Boleto_RetornaLinhaDigitavel");
    }

    /// <summary>Índice 1-based do título na lista.</summary>
    public string RetornaCodigoBarras(int indice)
    {
        int Fn(StringBuilder sb, ref int l) => _retornaCodigoBarras(_libHandle, indice, sb, ref l);
        return Ler(Fn, "Boleto_RetornaCodigoBarras");
    }

    // ── WebService ────────────────────────────────────────────────────

    /// <summary>
    /// Executa operação bancária via API.
    /// O título deve estar incluído via IncluirTitulos antes de chamar.
    /// Retorna INI com seções [REGISTRO1], [TITULORETORNO1], [Sacado1].
    /// </summary>
    public string EnviarBoleto(OperacaoBoleto operacao)
    {
        var op = (int)operacao;
        int Fn(StringBuilder sb, ref int l) => _enviarBoleto(_libHandle, op, sb, ref l);
        return Stateful(() => Ler(Fn, "Boleto_EnviarBoleto", LargeBuf));
    }

    /// <summary>
    /// Consulta lista de boletos por período.
    /// <paramref name="iniConteudo"/> deve conter seção [ConsultaAPI] com os filtros.
    /// Retorna INI com os resultados.
    /// </summary>
    public string ConsultarTitulosPorPeriodo(string iniConteudo) => Stateful(() =>
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"acbr_consulta_{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(tmp, iniConteudo);
            int Fn(StringBuilder sb, ref int l) => _consultarPeriodo(_libHandle, tmp, sb, ref l);
            return Ler(Fn, "Boleto_ConsultarTitulosPorPeriodo", LargeBuf);
        }
        finally { try { File.Delete(tmp); } catch { } }
    });

    // ── Token ─────────────────────────────────────────────────────────

    public string GerarToken()
    {
        int Fn(StringBuilder sb, ref int l) => _gerarToken(_libHandle, sb, ref l);
        return Ler(Fn, "Boleto_GerarToken");
    }

    public void InformarToken(string token, DateTime dataValidade) =>
        Check(_informarToken(_libHandle, token, dataValidade.ToOADate()), "Boleto_InformarToken");

    // ── Internos ──────────────────────────────────────────────────────

    private delegate int RefIntFunc(StringBuilder sb, ref int len);

    // Hack retirado do ACBrLibBase.cs oficial: CharSet.Ansi marshals strings como Windows-1252.
    // Para enviar conteúdo INI inline (ConfigurarDados), re-encodamos: UTF-8 bytes → Default(1252).
    // IncluirTitulos NÃO aceita inline (confirmado com DLL v1.2.1.446) — continua usando arquivo temp.
    private static string ToUTF8(string value) =>
        string.IsNullOrEmpty(value) ? value : Encoding.Default.GetString(Encoding.UTF8.GetBytes(value));

    private static string DecodeUtf8(string? val)
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        // On Windows, CharSet.Ansi interprets the DLL's UTF-8 bytes as Windows-1252,
        // so we re-encode through 1252 to recover the real UTF-8 string.
        // On Linux, CharSet.Ansi uses the process locale (UTF-8), so the string is already correct.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return val;
        var bytes = Encoding.GetEncoding(1252).GetBytes(val);
        return Encoding.UTF8.GetString(bytes);
    }

    // Sem verificação de retorno — usado APENAS por UltimoRetorno para evitar recursão em Check()
    private string LerInterno(RefIntFunc fn, int tamanhoInicial = SmallBuf)
    {
        int len = tamanhoInicial;
        var sb = new StringBuilder(len);
        fn(sb, ref len);
        if (len > tamanhoInicial)
        {
            sb.Capacity = len;
            fn(sb, ref len);
        }
        return DecodeUtf8(sb.ToString().TrimEnd('\0'));
    }

    // Verifica o código de retorno — qualquer valor negativo lança AcbrLibException via Check()
    private string Ler(RefIntFunc fn, string op, int tamanhoInicial = SmallBuf)
    {
        int len = tamanhoInicial;
        var sb = new StringBuilder(len);
        int ret = fn(sb, ref len);
        Check(ret, op);
        if (len > tamanhoInicial)
        {
            sb.Capacity = len;
            ret = fn(sb, ref len);
            Check(ret, op);
        }
        return DecodeUtf8(sb.ToString().TrimEnd('\0'));
    }

    // Para Base64 e dados binários codificados, não fazemos o re-encode UTF8
    private string LerBase64(RefIntFunc fn, string op, int tamanhoInicial = SmallBuf)
    {
        int len = tamanhoInicial;
        var sb = new StringBuilder(len);
        int ret = fn(sb, ref len);
        Check(ret, op);
        if (len > tamanhoInicial)
        {
            sb.Capacity = len;
            ret = fn(sb, ref len);
            Check(ret, op);
        }
        return sb.ToString().TrimEnd('\0');
    }

    private void Check(int ret, string op)
    {
        if (ret >= 0) return;
        var msg = UltimoRetorno();
        throw new AcbrLibException(msg, ret, op);
    }

    // Operações NATIVAS com estado (WS, config, lista, títulos) podem deixar o handle Delphi
    // corrompido quando falham — uma chamada seguinte desreferencia esse estado e dispara
    // 0xC0000005 (access violation). Marcamos o handle como faulted em qualquer falha para que
    // o pool o descarte (em vez de reusá-lo) e nos recusamos a emitir novas chamadas nativas
    // sobre um handle faulted, convertendo um access violation em exceção gerenciável.
    private void Stateful(Action native)
    {
        if (Faulted) throw new InvalidOperationException("Handle ACBrLib em estado inválido (faulted): não pode ser reutilizado.");
        try { native(); }
        catch { Faulted = true; throw; }
    }

    private T Stateful<T>(Func<T> native)
    {
        if (Faulted) throw new InvalidOperationException("Handle ACBrLib em estado inválido (faulted): não pode ser reutilizado.");
        try { return native(); }
        catch { Faulted = true; throw; }
    }

    private void BindDelegates()
    {
        _inicializar = Bind<D_Inicializar>("Boleto_Inicializar");
        _finalizar = Bind<D_Finalizar>("Boleto_Finalizar");
        _nome = Bind<D_Nome>("Boleto_Nome");
        _versao = Bind<D_Versao>("Boleto_Versao");
        _ultimoRetorno = Bind<D_UltimoRetorno>("Boleto_UltimoRetorno");
        _openSSLInfo = Bind<D_OpenSSLInfo>("Boleto_OpenSSLInfo");
        _configLer = Bind<D_ConfigLer>("Boleto_ConfigLer");
        _configGravar = Bind<D_ConfigGravar>("Boleto_ConfigGravar");
        _configImportar = Bind<D_ConfigImportar>("Boleto_ConfigImportar");
        _configExportar = Bind<D_ConfigExportar>("Boleto_ConfigExportar");
        _configLerValor = Bind<D_ConfigLerValor>("Boleto_ConfigLerValor");
        _configGravarValor = Bind<D_ConfigGravarValor>("Boleto_ConfigGravarValor");
        _configurarDados = Bind<D_ConfigurarDados>("Boleto_ConfigurarDados");
        _incluirTitulos = Bind<D_IncluirTitulos>("Boleto_IncluirTitulos");
        _limparLista = Bind<D_LimparLista>("Boleto_LimparLista");
        _totalTitulosLista = Bind<D_TotalTitulosLista>("Boleto_TotalTitulosLista");
        _imprimir = Bind<D_Imprimir>("Boleto_Imprimir");
        _imprimirBoleto = Bind<D_ImprimirBoleto>("Boleto_ImprimirBoleto");
        _gerarPDF = Bind<D_GerarPDF>("Boleto_GerarPDF");
        _salvarPDF = Bind<D_SalvarPDF>("Boleto_SalvarPDF");
        _gerarPDFBoleto = Bind<D_GerarPDFBoleto>("Boleto_GerarPDFBoleto");
        _salvarPDFBoleto = Bind<D_SalvarPDFBoleto>("Boleto_SalvarPDFBoleto");
        _gerarHTML = Bind<D_GerarHTML>("Boleto_GerarHTML");
        _gerarRemessa = Bind<D_GerarRemessa>("Boleto_GerarRemessa");
        _gerarRemessaStream = Bind<D_GerarRemessaStream>("Boleto_GerarRemessaStream");
        _obterRetorno = Bind<D_ObterRetorno>("Boleto_ObterRetorno");
        _lerRetorno = Bind<D_LerRetorno>("Boleto_LerRetorno");
        _lerRetornoStream = Bind<D_LerRetornoStream>("Boleto_LerRetornoStream");
        _enviarEmail = Bind<D_EnviarEmail>("Boleto_EnviarEmail");
        _enviarEmailBoleto = Bind<D_EnviarEmailBoleto>("Boleto_EnviarEmailBoleto");
        _setDiretorioArquivo = Bind<D_SetDiretorioArquivo>("Boleto_SetDiretorioArquivo");
        _listaBancos = Bind<D_ListaBancos>("Boleto_ListaBancos");
        _listaCaractTitulo = Bind<D_ListaCaractTitulo>("Boleto_ListaCaractTitulo");
        _listaOcorrencias = Bind<D_ListaOcorrencias>("Boleto_ListaOcorrencias");
        _listaOcorrenciasEX = Bind<D_ListaOcorrenciasEX>("Boleto_ListaOcorrenciasEX");
        _codigosMoraAceitos = Bind<D_CodigosMoraAceitos>("Boleto_CodigosMoraAceitos");
        _selecionaBanco = Bind<D_SelecionaBanco>("Boleto_SelecionaBanco");
        _tamNossoNumero = Bind<D_TamNossoNumero>("Boleto_TamNossoNumero");
        _montarNossoNumero = Bind<D_MontarNossoNumero>("Boleto_MontarNossoNumero");
        _retornaLinhaDigitavel = Bind<D_RetornaLinhaDigitavel>("Boleto_RetornaLinhaDigitavel");
        _retornaCodigoBarras = Bind<D_RetornaCodigoBarras>("Boleto_RetornaCodigoBarras");
        _enviarBoleto = Bind<D_EnviarBoleto>("Boleto_EnviarBoleto");
        _consultarPeriodo = Bind<D_ConsultarTitulosPorPeriodo>("Boleto_ConsultarTitulosPorPeriodo");
        _gerarToken = Bind<D_GerarToken>("Boleto_GerarToken");
        _informarToken = Bind<D_InformarToken>("Boleto_InformarToken");
    }

    private TDelegate Bind<TDelegate>(string fn) where TDelegate : Delegate
    {
        if (!NativeLibrary.TryGetExport(_hLib, fn, out var ptr))
            throw new EntryPointNotFoundException(
                $"Função '{fn}' não encontrada em ACBrLibBoleto. Verifique a versão da DLL.");
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
    }

    // ── IDisposable ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Finalizar();
        // Só liberamos o módulo no Windows. Em Linux o módulo GTK2 permanece mapeado pelo processo
        // (ver _modulosLinux): dar Free aqui faria o próximo handle re-inicializar o GTK e abortar.
        if (_hLib != IntPtr.Zero)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                NativeLibrary.Free(_hLib);
            _hLib = IntPtr.Zero;
        }
    }
}
