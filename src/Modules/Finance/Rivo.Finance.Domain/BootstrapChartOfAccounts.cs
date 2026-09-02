namespace Rivo.Finance.Domain;

/// <summary>
/// Bootstrap do plano de contas para ambiente de desenvolvimento/validação.
///
/// <para>
/// Este conjunto <strong>não pretende ser o PGC oficial angolano</strong> nem a
/// fonte primária legal. O projecto ressalva que o conteúdo do plano é carregado
/// e não inventado. Este seed tem apenas dois fins legítimos: (1) permitir a
/// plataforma funcionar em desenvolvimento e testes, e (2) dar uma estrutura
/// consistente para a lógica de diário, regra de posting e exportação SAF-T.
/// </para>
/// </summary>
public static class BootstrapChartOfAccounts
{
    public static IReadOnlyList<LedgerAccount> Load()
    {
        var raizAtivos = LedgerAccount.Open("1", "Ativos", AccountCategory.GR, null);
        var raizPassivos = LedgerAccount.Open("2", "Passivos", AccountCategory.GR, null);
        var raizCapital = LedgerAccount.Open("3", "Capital Próprio", AccountCategory.GR, null);
        var raizReceitas = LedgerAccount.Open("4", "Receitas", AccountCategory.GR, null);
        var raizCustos = LedgerAccount.Open("5", "Custos", AccountCategory.GR, null);
        var raizDespesas = LedgerAccount.Open("6", "Despesas", AccountCategory.GR, null);

        var disponibilidades = LedgerAccount.Open("10", "Disponibilidades", AccountCategory.GA, raizAtivos);
        var clientes = LedgerAccount.Open("11", "Clientes e Outros Débitos", AccountCategory.GA, raizAtivos);
        var inventarios = LedgerAccount.Open("12", "Inventários", AccountCategory.GA, raizAtivos);
        var imobilizado = LedgerAccount.Open("13", "Activos Fixos", AccountCategory.GA, raizAtivos);

        var fornecedores = LedgerAccount.Open("21", "Fornecedores", AccountCategory.GA, raizPassivos);
        var financiamentos = LedgerAccount.Open("22", "Financiamentos", AccountCategory.GA, raizPassivos);
        var impostos = LedgerAccount.Open("23", "Impostos e Contribuições", AccountCategory.GA, raizPassivos);

        var capital = LedgerAccount.Open("31", "Capital Social e Reservas", AccountCategory.GA, raizCapital);

        var vendas = LedgerAccount.Open("41", "Vendas", AccountCategory.GA, raizReceitas);
        var outrasReceitas = LedgerAccount.Open("42", "Outras Receitas", AccountCategory.GA, raizReceitas);

        var compras = LedgerAccount.Open("51", "Compras", AccountCategory.GA, raizCustos);
        var custosOperacionais = LedgerAccount.Open("52", "Custos Operacionais", AccountCategory.GA, raizCustos);

        var despesasAdm = LedgerAccount.Open("61", "Despesas Administrativas", AccountCategory.GA, raizDespesas);
        var despesasComerciais = LedgerAccount.Open("62", "Despesas Comerciais", AccountCategory.GA, raizDespesas);
        var despesasFinanceiras = LedgerAccount.Open("63", "Despesas Financeiras", AccountCategory.GA, raizDespesas);

        var caixa = LedgerAccount.Open("1010", "Caixa", AccountCategory.GM, disponibilidades);
        var bancos = LedgerAccount.Open("1020", "Bancos", AccountCategory.GM, disponibilidades);
        var clientesCc = LedgerAccount.Open("1110", "Clientes em Conta Corrente", AccountCategory.GM, clientes);
        var adiantamentos = LedgerAccount.Open("1120", "Adiantamentos a Fornecedores", AccountCategory.GM, clientes);
        var mercadorias = LedgerAccount.Open("1210", "Mercadorias", AccountCategory.GM, inventarios);
        var imobilizadoBruto = LedgerAccount.Open("1310", "Edifícios e Instalações", AccountCategory.GM, imobilizado);

        var fornsNacionais = LedgerAccount.Open("2110", "Fornecedores Nacionais", AccountCategory.GM, fornecedores);
        var encargos = LedgerAccount.Open("2210", "Empréstimos Bancários", AccountCategory.GM, financiamentos);
        var ivaPagar = LedgerAccount.Open("2310", "IVA a Pagar", AccountCategory.GM, impostos);
        var irpsRetido = LedgerAccount.Open("2320", "IRPS a Recolher", AccountCategory.GM, impostos);

        var capitalSocial = LedgerAccount.Open("3110", "Capital Social", AccountCategory.GM, capital);
        var reservas = LedgerAccount.Open("3120", "Reservas", AccountCategory.GM, capital);

        var vendasMercadorias = LedgerAccount.Open("4110", "Vendas de Mercadorias", AccountCategory.GM, vendas);
        var servicos = LedgerAccount.Open("4120", "Prestação de Serviços", AccountCategory.GM, vendas);
        var juros = LedgerAccount.Open("4210", "Juros e Rendimentos Financeiros", AccountCategory.GM, outrasReceitas);

        var comprasMercadorias = LedgerAccount.Open("5110", "Compras de Mercadorias", AccountCategory.GM, compras);
        var custosFornecimentos = LedgerAccount.Open("5210", "Fornecimentos e Serviços", AccountCategory.GM, custosOperacionais);

        var despesasPessoal = LedgerAccount.Open("6110", "Despesas de Pessoal", AccountCategory.GM, despesasAdm);
        var alugueres = LedgerAccount.Open("6120", "Alugueres e Arrendamentos", AccountCategory.GM, despesasAdm);
        var marketing = LedgerAccount.Open("6210", "Marketing e Publicidade", AccountCategory.GM, despesasComerciais);
        var jurosPagos = LedgerAccount.Open("6310", "Juros Pagos", AccountCategory.GM, despesasFinanceiras);

        return
        [
            raizAtivos,
            raizPassivos,
            raizCapital,
            raizReceitas,
            raizCustos,
            raizDespesas,
            disponibilidades,
            clientes,
            inventarios,
            imobilizado,
            fornecedores,
            financiamentos,
            impostos,
            capital,
            vendas,
            outrasReceitas,
            compras,
            custosOperacionais,
            despesasAdm,
            despesasComerciais,
            despesasFinanceiras,
            caixa,
            bancos,
            clientesCc,
            adiantamentos,
            mercadorias,
            imobilizadoBruto,
            fornsNacionais,
            encargos,
            ivaPagar,
            irpsRetido,
            capitalSocial,
            reservas,
            vendasMercadorias,
            servicos,
            juros,
            comprasMercadorias,
            custosFornecimentos,
            despesasPessoal,
            alugueres,
            marketing,
            jurosPagos,
        ];
    }
}
