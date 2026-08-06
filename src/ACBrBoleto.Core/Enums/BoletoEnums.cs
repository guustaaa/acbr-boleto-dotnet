namespace ACBrBoleto.Core.Enums;

// ── [BoletoBancoConfig].TipoCobranca — seleciona o banco ────────────────────
// 0=Nenhum, 1=BB, 2=Santander, 3=CaixaSIGCB, 4=CaixaSICOB, 5=Bradesco, 6=Itaú...
// Lista completa: https://www.projetoacbr.com.br/forum/topic/57991
public enum TipoBanco
{
    Nenhum = 0, BancoDoBrasil = 1, Santander = 2, CaixaSIGCB = 3, CaixaSICOB = 4,
    Bradesco = 5, Itau = 6, Mercantil = 7, Sicred = 8, Bancoob = 9, Banrisul = 10,
    BancoDoBrasilAPI = 36, BancoDoBrasilWS = 37, BancoInter = 42, BancoC6 = 40,
    Cora = 60, Sicoob = 57,
}

// ── [BoletoWebSevice].Ambiente ───────────────────────────────────────────────
// Conforme docs ACBrLib: 0=Produção, 1=Homologação, 2=Sandbox
public enum WsAmbiente      { Producao = 0, Homologacao = 1, Sandbox = 2 }

// ── [BoletoCedenteConfig].TipoChavePIX ───────────────────────────────────────
// Conforme docs ACBrLib: 0=Nenhum, 1=Email, 2=CPF, 3=CNPJ, 4=Celular, 5=Aleatória
public enum TipoChavePix    { Nenhum = 0, Email = 1, CPF = 2, CNPJ = 3, Celular = 4, EVP = 5 }

// ── configboleto.aceite → ACBrLib ────────────────────────────────────────────
public enum TipoAceite      { NaoAceite = 0, Aceite = 1 }

// ── configboleto.indicadorpix ────────────────────────────────────────────────
// [BoletoCedenteWS].IndicadorPix: 0=Não, 1=Sim
public enum IndicadorPix    { Nenhum = 0, Habilitado = 1 }

// ── titulo.CodigoDesconto — categorização DB/FEBRABAN (NÃO é campo INI) ───────
public enum CodigoDesconto { SemDesconto = 1, ValorFixo = 2, Percentual = 3 }

// ── titulo.TipoDesconto — campo TipoDesconto= do INI ACBrLib ─────────────────
// 7 = CancelamentoDesconto é interno — enviado por CancelarDesconto(), nunca exposto ao usuário.
public enum TipoDesconto
{
    NaoConcederDesconto                   = 0,  // sem desconto
    ValorFixoAteDataInformada             = 1,  // R$ fixo até dataDesconto (requer dataDesconto)
    PercentualAteDataInformada            = 2,  // % até dataDesconto (requer dataDesconto)
    ValorAntecipacaoDiaCorrido            = 3,  // R$/dia corrido — quanto mais cedo paga, maior desconto
    ValorAntecipacaoDiaUtil               = 4,  // R$/dia útil antecipado
    PercentualSobreValorNominalDiaCorrido = 5,  // %/dia corrido sobre valor nominal
    PercentualSobreValorNominalDiaUtil    = 6,  // %/dia útil sobre valor nominal
}

// ── titulo.CodigoMoraJuros ────────────────────────────────────────────────────
public enum CodigoMoraJuros { ValorDia = 1, TaxaMensal = 2, Isento = 3, ValorMensal = 4, TaxaDiaria = 5 }

// ── titulo.CodigoMulta ────────────────────────────────────────────────────────
public enum CodigoMulta     { ValorFixo = 1, Percentual = 2, Isento = 3 }

// ── [BoletoCedenteConfig].CodigoNegativacao ───────────────────────────────────
// 0=Nenhum, 1=Protestar corrido, 2=Protestar úteis, 3=Não protestar,
// 4=Negativar, 5=Não negativar, 6=Cancelamento
public enum CodigoNegativacao
{
    Nenhum = 0, ProtestarDiasCorridos = 1, ProtestarDiasUteis = 2,
    NaoProtestar = 3, Negativar = 4, NaoNegativar = 5, Cancelamento = 6
}

// ── titulo.ocorrenciamanut / operacaowsmanut ─────────────────────────────────
public enum OcorrenciaManut { SemOcorrencia = 0, AlterarVencimento = 5, AlterarValor = 8, Baixar = 2, Protestar = 9 }

// ── FiltroWS.IndicadorSituacaoBoleto ─────────────────────────────────────────
public enum SituacaoBoleto  { Nenhum = 0, Aberto = 1, Baixado = 2 }

// ── Instrucao1 / Instrucao2 — códigos FEBRABAN / CNAB ────────────────────────
// Escritos na seção [Titulo1] do INI. São instruções numéricas para o banco,
// NÃO texto livre. O texto impresso no boleto vai em Mensagem / Informativo.
// Muitos códigos são banco-específicos; os abaixo são os mais comuns (CNAB240/400).
public enum CodigoInstrucao
{
    SemInstrucao                = 0,
    BaixarAposVencimento        = 2,   // Baixar/devolver título após N dias do vencimento
    ProtestarDiasCorridos       = 9,   // Protestar após N dias corridos
    NaoProtestar                = 10,  // Não protestar
    DevolverAposDias            = 11,  // Devolver após N dias sem pagamento
    CobrarMultaDataEspecifica   = 15,  // Cobrar multa a partir de data específica (DataMoraJuros)
    CobrarMultaPercentual       = 16,  // Cobrar multa percentual (PercentualMulta)
    CobrarMultaDesdeVencimento  = 24,  // Cobrar multa imediatamente após o vencimento
    DiasUteisMultaValor         = 22,  // N dias úteis + multa em valor fixo
    DiasCorridosMultaPercentual = 73,  // N dias corridos + multa percentual
    DiasUteisMultaPercentual    = 74,  // N dias úteis + multa percentual
    NegativarDiasCorridos       = 91,  // Negativar sacado após N dias corridos
    NegativarDiasUteis          = 92,  // Negativar sacado após N dias úteis
    MensagemCNAB30Chars         = 93,  // Inserir mensagem de 30 chars no CNAB400
    MensagemCNAB40Chars         = 94,  // Inserir mensagem de 40 chars no CNAB400
}

// ── [Principal].LogNivel ──────────────────────────────────────────────────────
// 0=Nenhum, 1=Simples, 2=Normal, 3=Completo, 4=Paranoico
public enum NivelLog        { Nenhum = 0, Simples = 1, Normal = 2, Completo = 3, Paranoico = 4 }

// ── [BoletoWebSevice].Operacao ────────────────────────────────────────────────
public enum OperacaoBoleto
{
    tpInclui          = 0,   // Registrar/incluir boleto
    tpAltera          = 1,   // Alterar dados (vencimento, valor, etc.)
    tpBaixa           = 2,   // Solicitar baixa/cancelamento
    tpConsulta        = 3,   // Consultar lista de títulos por período
    tpConsultaDetalhe = 4,   // Consultar detalhe de um título específico
}
