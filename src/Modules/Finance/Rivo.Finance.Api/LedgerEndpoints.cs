using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Api;

/// <summary>
/// Contabilidade &amp; Fecho e Planeamento — os dois contextos internos que
/// faltavam a `finance`.
///
/// <para>
/// Ficheiro próprio, ao lado de <see cref="FinanceModuleEndpoints"/> (AR) e
/// <see cref="PayablesEndpoints"/> (AP e Tesouraria), pela mesma razão: são
/// contextos distintos, e um ficheiro único deixaria de se conseguir ler.
/// </para>
/// </summary>
public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedger(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/finance");

        // ---- Plano de contas ----
        group.MapGet("/ledger/accounts", ListAccountsAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapPost("/ledger/accounts", OpenAccountAsync)
            .RequireAuthorization(FinancePermissions.LedgerWrite);

        group.MapPost("/ledger/accounts/{accountId:guid}/deactivation", DeactivateAccountAsync)
            .RequireAuthorization(FinancePermissions.LedgerWrite);

        // ---- Diários ----
        group.MapGet("/ledger/journals", ListJournalsAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapPost("/ledger/journals", OpenJournalAsync)
            .RequireAuthorization(FinancePermissions.LedgerWrite);

        // ---- Lançamentos ----
        group.MapGet("/ledger/entries", ListEntriesAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapGet("/ledger/entries/{entryId:guid}", GetEntryAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapPost("/ledger/entries", PostEntryAsync)
            .RequireAuthorization(FinancePermissions.LedgerWrite);

        group.MapPost("/ledger/entries/{entryId:guid}/void", VoidEntryAsync)
            .RequireAuthorization(FinancePermissions.LedgerWrite);

        // ---- Períodos e fecho ----
        group.MapGet("/ledger/periods", ListPeriodsAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapPost("/ledger/periods", OpenPeriodAsync)
            .RequireAuthorization(FinancePermissions.LedgerWrite);

        // **Fechar e reabrir são a mesma permissão, e mais restrita que
        // lançar.** Reabrir faz números já dados por definitivos voltarem a
        // mexer-se — é do mesmo calibre que abrir uma série de documento.
        group.MapPost("/ledger/periods/{fiscalYear:int}/{number:int}/closure", ClosePeriodAsync)
            .RequireAuthorization(FinancePermissions.LedgerClose);

        group.MapPost("/ledger/periods/{fiscalYear:int}/{number:int}/reopening", ReopenPeriodAsync)
            .RequireAuthorization(FinancePermissions.LedgerClose);

        // ---- Balancete ----
        group.MapGet("/ledger/trial-balance", TrialBalanceAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        // ---- Regras de postagem ----
        //
        // Definir uma regra decide como **todos** os documentos futuros lançam.
        // Fica com quem fecha períodos, não com quem lança um a um.
        group.MapGet("/ledger/posting-rules", ListPostingRulesAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapPost("/ledger/posting-rules", DefinePostingRuleAsync)
            .RequireAuthorization(FinancePermissions.LedgerClose);

        group.MapPost("/ledger/posting-rules/{ruleId:guid}/deactivation", DeactivatePostingRuleAsync)
            .RequireAuthorization(FinancePermissions.LedgerClose);

        // ---- Versões do plano de contas ----
        group.MapGet("/ledger/chart-versions", ListChartVersionsAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapPost("/ledger/chart-versions", CreateChartVersionAsync)
            .RequireAuthorization(FinancePermissions.LedgerClose);

        // ---- Regras contabilísticas ----
        group.MapGet("/ledger/accounting-rules", ListAccountingRulesAsync)
            .RequireAuthorization(FinancePermissions.LedgerRead);

        group.MapPost("/ledger/accounting-rules", CreateAccountingRuleAsync)
            .RequireAuthorization(FinancePermissions.LedgerClose);

        group.MapPost("/ledger/accounting-rules/{ruleId:guid}/deactivation", DeactivateAccountingRuleAsync)
            .RequireAuthorization(FinancePermissions.LedgerClose);

        // ---- Planeamento ----
        group.MapGet("/planning/cost-centres", ListCostCentresAsync)
            .RequireAuthorization(FinancePermissions.PlanningRead);

        group.MapPost("/planning/cost-centres", OpenCostCentreAsync)
            .RequireAuthorization(FinancePermissions.PlanningWrite);

        group.MapGet("/planning/budgets", ListBudgetsAsync)
            .RequireAuthorization(FinancePermissions.PlanningRead);

        group.MapPost("/planning/budgets", DraftBudgetAsync)
            .RequireAuthorization(FinancePermissions.PlanningWrite);

        group.MapPost("/planning/budgets/{budgetId:guid}/revision", ReviseBudgetAsync)
            .RequireAuthorization(FinancePermissions.PlanningWrite);

        // **Permissão própria, e é BR-8 na forma do catálogo:** quem elabora o
        // orçamento não é quem lhe dá força. Senão bastava subir o tecto para o
        // próprio pedido passar a caber.
        group.MapPost("/planning/budgets/{budgetId:guid}/approval", ApproveBudgetAsync)
            .RequireAuthorization(FinancePermissions.BudgetsApprove);

        group.MapGet("/planning/cost-forecasts", ListForecastsAsync)
            .RequireAuthorization(FinancePermissions.PlanningRead);

        group.MapPost("/planning/cost-forecasts", RecordForecastAsync)
            .RequireAuthorization(FinancePermissions.PlanningWrite);

        return endpoints;
    }

    // ---- Plano de contas ----

    private static async Task<IResult> ListAccountsAsync(
        ListLedgerAccounts list,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> OpenAccountAsync(
        LedgerAccountRequest request,
        OpenLedgerAccount open,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AccountCategory>(request.Category, ignoreCase: true, out var categoria))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["category"] =
                [
                    "A categoria é uma das seis do SAF-T AO: GR, GA, GM (contabilidade geral) " +
                    "ou AR, AA, AM (analítica).",
                ],
            });
        }

        var result = await open.ExecuteAsync(
            request.Code, request.Name, categoria, request.ParentCode,
            BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            OpenLedgerAccountOutcome.Opened => Results.Created(
                $"/finance/ledger/accounts?code={result.Code}",
                new { accountId = result.AccountId, code = result.Code }),

            OpenLedgerAccountOutcome.Duplicate =>
                Results.Conflict(new { erro = result.Error }),

            OpenLedgerAccountOutcome.ParentNotFound =>
                Results.NotFound(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["conta"] = [result.Error!],
            }),
        };
    }

    private static async Task<IResult> DeactivateAccountAsync(
        Guid accountId,
        DeactivateLedgerAccount deactivate,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await deactivate.ExecuteAsync(
            accountId, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            DeactivateAccountOutcome.Done => Results.NoContent(),

            DeactivateAccountOutcome.NotFound =>
                Results.NotFound(new { erro = "Conta não encontrada." }),

            _ => Results.Conflict(new
            {
                erro = "A conta tem contas penduradas. Desactive-as primeiro, ou a árvore " +
                       "fica com um buraco no meio.",
            }),
        };
    }

    // ---- Diários ----

    private static async Task<IResult> ListJournalsAsync(
        ListJournals list,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> OpenJournalAsync(
        JournalRequest request,
        OpenJournal open,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await open.ExecuteAsync(
            request.Code, request.Name, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            OpenJournalOutcome.Opened => Results.Created(
                $"/finance/ledger/journals?code={result.Code}",
                new { journalId = result.JournalId, code = result.Code }),

            OpenJournalOutcome.Duplicate => Results.Conflict(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["diario"] = [result.Error!],
            }),
        };
    }

    // ---- Lançamentos ----

    private static async Task<IResult> ListEntriesAsync(
        ListJournalEntries list,
        Guid? journalId,
        int? fiscalYear,
        int? period,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(journalId, fiscalYear, period, cancellationToken));

    private static async Task<IResult> GetEntryAsync(
        Guid entryId,
        GetJournalEntry get,
        CancellationToken cancellationToken)
    {
        var lancamento = await get.ExecuteAsync(entryId, cancellationToken);

        return lancamento is null
            ? Results.NotFound(new { erro = "Lançamento não encontrado." })
            : Results.Ok(lancamento);
    }

    private static async Task<IResult> PostEntryAsync(
        PostEntryRequest request,
        PostJournalEntry post,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TransactionType>(request.Type ?? "N", ignoreCase: true, out var tipo))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["type"] =
                [
                    "O tipo é um dos quatro do SAF-T AO: N (normal), R (regularização), " +
                    "A (apuramento de resultados) ou J (ajustamento).",
                ],
            });
        }

        var linhas = new List<JournalLineInput>();

        foreach (var linha in request.Lines ?? [])
        {
            if (!Enum.TryParse<EntrySide>(linha.Side, ignoreCase: true, out var lado))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["lines"] = ["O lado de cada linha é `Debit` ou `Credit`."],
                });
            }

            linhas.Add(new JournalLineInput(
                linha.AccountCode, lado, linha.Amount, linha.Description,
                linha.CostCentreId, linha.SourceDocumentId));
        }

        var data = request.TransactionDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await post.ExecuteAsync(
            request.JournalCode,
            request.ArchivalNumber,
            data,
            request.FiscalYear ?? data.Year,
            request.Period ?? data.Month,
            request.Description,
            tipo,
            SourceIdOf(http),
            linhas,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            PostEntryOutcome.Posted => Results.Created(
                $"/finance/ledger/entries/{result.EntryId}",
                new { entryId = result.EntryId, transactionId = result.TransactionId }),

            PostEntryOutcome.JournalNotFound =>
                Results.NotFound(new { erro = "Diário não encontrado." }),

            PostEntryOutcome.AccountNotFound =>
                Results.NotFound(new { erro = result.Error }),

            // 409 e não 400: o lançamento está bem formado, e noutro período
            // entrava sem objecção. É o estado dos livros que impede.
            PostEntryOutcome.PeriodClosed or PostEntryOutcome.DuplicateTransaction =>
                Results.Conflict(new { erro = result.Error }),

            // 400 com chave própria: a partida dobrada é a invariante central
            // da contabilidade, e quem a viola merece ouvir isso.
            PostEntryOutcome.Unbalanced => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["partidaDobrada"] = [result.Error!] }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["lancamento"] = [result.Error!],
            }),
        };
    }

    private static async Task<IResult> VoidEntryAsync(
        Guid entryId,
        VoidEntryRequest request,
        VoidJournalEntry voidEntry,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await voidEntry.ExecuteAsync(
            entryId, request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            VoidEntryOutcome.Voided => Results.NoContent(),

            VoidEntryOutcome.NotFound =>
                Results.NotFound(new { erro = "Lançamento não encontrado." }),

            VoidEntryOutcome.PeriodClosed =>
                Results.Conflict(new { erro = result.Error }),

            _ => Results.Conflict(new { erro = result.Error }),
        };
    }

    // ---- Períodos ----

    private static async Task<IResult> ListPeriodsAsync(
        ManageAccountingPeriods periods,
        int fiscalYear,
        CancellationToken cancellationToken) =>
        Results.Ok(await periods.ListAsync(fiscalYear, cancellationToken));

    private static async Task<IResult> OpenPeriodAsync(
        OpenPeriodRequest request,
        ManageAccountingPeriods periods,
        CancellationToken cancellationToken)
    {
        var result = await periods.OpenAsync(request.FiscalYear, request.Number, cancellationToken);

        return result.Succeeded
            ? Results.Created($"/finance/ledger/periods?fiscalYear={request.FiscalYear}",
                new { periodId = result.PeriodId })
            : Results.Conflict(new { erro = result.Error });
    }

    private static async Task<IResult> ClosePeriodAsync(
        int fiscalYear,
        int number,
        ClosePeriodRequest request,
        ManageAccountingPeriods periods,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await periods.CloseAsync(
            fiscalYear, number, request.ClosedByEmployeeId,
            BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            ClosePeriodOutcome.Done => Results.NoContent(),
            ClosePeriodOutcome.NotFound => Results.NotFound(new { erro = "Período não encontrado." }),
            _ => Results.Conflict(new { erro = result.Error }),
        };
    }

    private static async Task<IResult> ReopenPeriodAsync(
        int fiscalYear,
        int number,
        ReopenPeriodRequest request,
        ManageAccountingPeriods periods,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await periods.ReopenAsync(
            fiscalYear, number, request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            ClosePeriodOutcome.Done => Results.NoContent(),
            ClosePeriodOutcome.NotFound => Results.NotFound(new { erro = "Período não encontrado." }),
            _ => Results.Conflict(new { erro = result.Error }),
        };
    }

    private static async Task<IResult> TrialBalanceAsync(
        GetTrialBalance balance,
        int fiscalYear,
        int? period,
        CancellationToken cancellationToken) =>
        Results.Ok(await balance.ExecuteAsync(fiscalYear, period, cancellationToken));

    // ---- Regras de postagem ----

    private static async Task<IResult> ListPostingRulesAsync(
        ListPostingRules list,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> DefinePostingRuleAsync(
        PostingRuleRequest request,
        DefinePostingRule define,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PostingEvent>(request.Event, ignoreCase: true, out var acontecimento))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["event"] =
                [
                    "O acontecimento é um de: " +
                    string.Join(", ", Enum.GetNames<PostingEvent>()) + ".",
                ],
            });
        }

        var linhas = new List<PostingRuleLineInput>();

        foreach (var linha in request.Lines ?? [])
        {
            if (!Enum.TryParse<EntrySide>(linha.Side, ignoreCase: true, out var lado))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["lines"] = ["O lado de cada linha é `Debit` ou `Credit`."],
                });
            }

            if (!Enum.TryParse<PostingAmount>(linha.Amount, ignoreCase: true, out var parcela))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["lines"] = ["A parcela de cada linha é `Net`, `Tax` ou `Gross`."],
                });
            }

            linhas.Add(new PostingRuleLineInput(
                linha.AccountCode, lado, parcela, linha.Description));
        }

        var result = await define.ExecuteAsync(
            acontecimento, request.JournalCode, request.Description, linhas,
            BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            DefinePostingRuleOutcome.Defined => Results.Created(
                $"/finance/ledger/posting-rules?event={acontecimento}",
                new { ruleId = result.RuleId }),

            DefinePostingRuleOutcome.Duplicate =>
                Results.Conflict(new { erro = result.Error }),

            DefinePostingRuleOutcome.JournalNotFound =>
                Results.NotFound(new { erro = "Diário não encontrado." }),

            DefinePostingRuleOutcome.AccountNotFound =>
                Results.NotFound(new { erro = result.Error }),

            // Chave própria: a regra não equilibra enquanto expressão, e isso é
            // diferente de um campo mal preenchido.
            DefinePostingRuleOutcome.Unbalanced => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["equilibrio"] = [result.Error!] }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["regra"] = [result.Error!],
            }),
        };
    }

    private static async Task<IResult> DeactivatePostingRuleAsync(
        Guid ruleId,
        DeactivatePostingRule deactivate,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var feito = await deactivate.ExecuteAsync(ruleId, BuildAuditContext(http), cancellationToken);

        return feito
            ? Results.NoContent()
            : Results.NotFound(new { erro = "Regra de postagem não encontrada." });
    }

    // ---- Versões do plano de contas ----

    private static async Task<IResult> ListChartVersionsAsync(
        ListChartOfAccountsVersions list,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> CreateChartVersionAsync(
        CreateChartVersionRequest request,
        CreateChartOfAccountsVersion create,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await create.ExecuteAsync(
            request.Jurisdiction,
            request.Name,
            request.Version,
            request.Source,
            request.EffectiveFrom,
            request.EffectiveTo,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            CreateChartOfAccountsVersionOutcome.Created => Results.Created(
                $"/finance/ledger/chart-versions?jurisdiction={Uri.EscapeDataString(request.Jurisdiction)}&name={Uri.EscapeDataString(request.Name)}&version={Uri.EscapeDataString(request.Version)}",
                new { chartVersionId = result.ChartVersionId }),

            CreateChartOfAccountsVersionOutcome.Duplicate =>
                Results.Conflict(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["versao"] = [result.Error!],
            }),
        };
    }

    // ---- Regras contabilísticas ----

    private static async Task<IResult> ListAccountingRulesAsync(
        ListAccountingRules list,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> CreateAccountingRuleAsync(
        CreateAccountingRuleRequest request,
        CreateAccountingRule create,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var linhas = new List<AccountingRuleLineInput>();

        foreach (var linha in request.Lines ?? [])
        {
            if (!Enum.TryParse<EntrySide>(linha.Side, ignoreCase: true, out var lado))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["lines"] = ["O lado de cada linha é `Debit` ou `Credit`."],
                });
            }

            if (!Enum.TryParse<PostingAmount>(linha.Amount, ignoreCase: true, out var parcela))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["lines"] = ["A parcela de cada linha é `Net`, `Tax` ou `Gross`."],
                });
            }

            linhas.Add(new AccountingRuleLineInput(
                linha.AccountCode, lado, parcela, linha.Description));
        }

        var result = await create.ExecuteAsync(
            request.Code,
            request.Name,
            request.SourceType,
            request.Source,
            request.EffectiveFrom,
            request.EffectiveTo,
            linhas,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            CreateAccountingRuleOutcome.Created => Results.Created(
                $"/finance/ledger/accounting-rules?code={Uri.EscapeDataString(request.Code)}",
                new { ruleId = result.RuleId }),

            CreateAccountingRuleOutcome.AccountNotFound =>
                Results.NotFound(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["regra"] = [result.Error!],
            }),
        };
    }

    private static async Task<IResult> DeactivateAccountingRuleAsync(
        Guid ruleId,
        DeactivateAccountingRule deactivate,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var feito = await deactivate.ExecuteAsync(ruleId, BuildAuditContext(http), cancellationToken);

        return feito
            ? Results.NoContent()
            : Results.NotFound(new { erro = "Regra contabilística não encontrada." });
    }

    // ---- Planeamento ----

    private static async Task<IResult> ListCostCentresAsync(
        ListCostCentres list,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> OpenCostCentreAsync(
        CostCentreRequest request,
        OpenCostCentre open,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await open.ExecuteAsync(
            request.Code, request.Name, request.DepartmentId, request.ResponsibleEmployeeId,
            BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            OpenCostCentreOutcome.Opened => Results.Created(
                $"/finance/planning/cost-centres/{result.CostCentreId}",
                new { costCentreId = result.CostCentreId }),

            OpenCostCentreOutcome.Duplicate => Results.Conflict(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["centroDeCusto"] = [result.Error!],
            }),
        };
    }

    private static async Task<IResult> ListBudgetsAsync(
        ListBudgets list,
        Guid? costCentreId,
        int? fiscalYear,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(costCentreId, fiscalYear, cancellationToken));

    private static async Task<IResult> DraftBudgetAsync(
        BudgetRequest request,
        DraftBudget draft,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await draft.ExecuteAsync(
            request.CostCentreId,
            request.FiscalYear,
            request.Currency ?? "AOA",
            request.MonthlyCeilings ?? new Dictionary<int, decimal>(),
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            DraftBudgetOutcome.Drafted => Results.Created(
                $"/finance/planning/budgets/{result.BudgetId}",
                new { budgetId = result.BudgetId, estado = "Draft" }),

            DraftBudgetOutcome.CostCentreNotFound =>
                Results.NotFound(new { erro = "Centro de custo não encontrado." }),

            DraftBudgetOutcome.Duplicate => Results.Conflict(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["orcamento"] = [result.Error!],
            }),
        };
    }

    private static async Task<IResult> ReviseBudgetAsync(
        Guid budgetId,
        BudgetRevisionRequest request,
        ReviseBudget revise,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await revise.ExecuteAsync(
            budgetId,
            request.MonthlyCeilings ?? new Dictionary<int, decimal>(),
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            ReviseBudgetOutcome.Revised => Results.NoContent(),
            ReviseBudgetOutcome.NotFound => Results.NotFound(new { erro = "Orçamento não encontrado." }),

            // 409: um orçamento aprovado não se altera. Subir o tecto depois de
            // aprovado esvaziaria a aprovação, e com ela BR-8.
            ReviseBudgetOutcome.NotDraft => Results.Conflict(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["orcamento"] = [result.Error!],
            }),
        };
    }

    private static async Task<IResult> ApproveBudgetAsync(
        Guid budgetId,
        ApproveBudgetRequest request,
        ApproveBudget approve,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await approve.ExecuteAsync(
            budgetId, request.ApprovedByEmployeeId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            ApproveBudgetOutcome.Approved => Results.NoContent(),
            ApproveBudgetOutcome.NotFound => Results.NotFound(new { erro = "Orçamento não encontrado." }),
            _ => Results.Conflict(new { erro = result.Error }),
        };
    }

    private static async Task<IResult> ListForecastsAsync(
        ListCostForecasts list,
        Guid? departmentId,
        int? fiscalYear,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(departmentId, fiscalYear, cancellationToken));

    private static async Task<IResult> RecordForecastAsync(
        CostForecastRequest request,
        RecordCostForecast record,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await record.ExecuteAsync(
            request.DepartmentId,
            request.FiscalYear,
            request.Month,
            request.Currency ?? "AOA",
            request.OperationalCosts,
            request.FixedCosts,
            request.Submit ?? false,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            RecordForecastOutcome.Recorded => Results.Created(
                $"/finance/planning/cost-forecasts?departmentId={request.DepartmentId}",
                new { forecastId = result.ForecastId }),

            RecordForecastOutcome.Duplicate => Results.Conflict(new { erro = result.Error }),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["previsao"] = [result.Error!],
            }),
        };
    }

    // ---- contexto ----

    /// <summary>
    /// <c>SourceID</c> do SAF-T: quem lançou. O e-mail da conta, que é o que
    /// identifica um utilizador de forma legível a quem audita.
    /// </summary>
    private static string SourceIdOf(HttpContext http) =>
        http.User.FindFirstValue(JwtRegisteredClaimNames.Email)
        ?? http.User.FindFirstValue(ClaimTypes.Email)
        ?? http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? "desconhecido";

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var sub = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            Guid.TryParse(sub, out var actorId) ? actorId : null,
            http.Connection.RemoteIpAddress?.ToString(),
            http.TraceIdentifier);
    }
}

public sealed record LedgerAccountRequest(
    string Code,
    string Name,

    /// <summary>Uma das seis do SAF-T: `GR`, `GA`, `GM`, `AR`, `AA`, `AM`.</summary>
    string Category,

    /// <summary>A conta agregadora. Obrigatória excepto no 1.º grau.</summary>
    string? ParentCode);

public sealed record JournalRequest(string Code, string Name);

public sealed record PostEntryRequest(
    string JournalCode,

    /// <summary>Como se encontra o documento físico no arquivo.</summary>
    string ArchivalNumber,
    DateOnly? TransactionDate,
    int? FiscalYear,

    /// <summary>1 a 16. Acima de 12 são os períodos de fecho e regularização.</summary>
    int? Period,
    string Description,

    /// <summary>`N`, `R`, `A` ou `J`. Por omissão `N`.</summary>
    string? Type,
    IReadOnlyList<PostEntryLineRequest>? Lines);

public sealed record PostEntryLineRequest(
    string AccountCode,

    /// <summary>`Debit` ou `Credit`.</summary>
    string Side,
    decimal Amount,
    string Description,
    Guid? CostCentreId,
    string? SourceDocumentId);

public sealed record VoidEntryRequest(string Reason);

public sealed record OpenPeriodRequest(int FiscalYear, int Number);

public sealed record ClosePeriodRequest(Guid ClosedByEmployeeId);

/// <param name="Reason">
/// Obrigatório: reabrir significa que números já dados por definitivos vão
/// mudar, e quem o faz tem de dizer porquê.
/// </param>
public sealed record ReopenPeriodRequest(string Reason);

public sealed record CostCentreRequest(
    string Code,
    string Name,

    /// <summary>Opcional por desenho (D4) — o mapeamento não é 1:1.</summary>
    Guid? DepartmentId,
    Guid ResponsibleEmployeeId);

public sealed record BudgetRequest(
    Guid CostCentreId,
    int FiscalYear,
    string? Currency,

    /// <summary>Mês (1–12) para tecto.</summary>
    IReadOnlyDictionary<int, decimal>? MonthlyCeilings);

public sealed record BudgetRevisionRequest(IReadOnlyDictionary<int, decimal>? MonthlyCeilings);

public sealed record ApproveBudgetRequest(Guid ApprovedByEmployeeId);

public sealed record CostForecastRequest(
    Guid DepartmentId,
    int FiscalYear,
    int Month,
    string? Currency,
    decimal OperationalCosts,
    decimal FixedCosts,
    bool? Submit);

/// <param name="Event">
/// Um de: `SalesInvoiceIssued`, `CreditNoteIssued`, `ReceiptRegistered`,
/// `PurchaseInvoiceRegistered`, `PaymentExecuted`.
/// </param>
public sealed record PostingRuleRequest(
    string Event,
    string JournalCode,
    string Description,
    IReadOnlyList<PostingRuleLineRequest>? Lines);

/// <param name="Amount">
/// De que parcela do documento a linha se serve: `Net`, `Tax` ou `Gross`.
/// </param>
public sealed record PostingRuleLineRequest(
    string AccountCode,
    string Side,
    string Amount,
    string Description);

public sealed record CreateChartVersionRequest(
    string Jurisdiction,
    string Name,
    string Version,
    string Source,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record CreateAccountingRuleRequest(
    string Code,
    string Name,
    string SourceType,
    string Source,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<CreateAccountingRuleLineRequest>? Lines);

public sealed record CreateAccountingRuleLineRequest(
    string AccountCode,
    string Side,
    string Amount,
    string Description);
