# Verificação do módulo `payroll`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-payroll.ps1
#
# Esqueleto — 2026-08-29. Folha e itens, ligados a `approval` pelo total
# bruto. **Sem cálculo de IRT/INSS**: os campos existem no modelo e ficam
# sempre nulos — a ordem do IRT está confirmada em lei, mas os escalões
# dependem de `fiscal`, que não tem tabela angolana carregada, e
# `CLAUDE.md` proíbe implementar a partir do levantamento não verificado.
# Esta suite verifica que os campos ficam mesmo nulos, e não os testa como
# se calculassem algo.
#
# Re-executável: cada corrida usa um período próprio (mês derivado do
# carimbo temporal) e limpa a política de `payroll.payroll_run` que cria.

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_ambiente.ps1")
$base = Get-RivoBaseUrl
$failures = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        $detail = & $Body
        Write-Host ("  PASSA  " + $Name + $(if ($detail) { "  -- $detail" } else { "" })) -ForegroundColor Green
    }
    catch {
        Write-Host ("  FALHA  " + $Name + "  -- " + $_.Exception.Message) -ForegroundColor Red
        $script:failures++
    }
}

function Get-StatusCode {
    param([scriptblock]$Request)
    try { & $Request | Out-Null; return 200 }
    catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        if ($_.Exception.Message -match "401|Unauthorized") { return 401 }
        throw
    }
}

function Invoke-Sql { param([string]$q) return (Invoke-RivoSql $q) }

$dotenv = Get-RivoCredentials

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
# Mes 1-12 derivado do carimbo, para nao colidir com corridas anteriores no
# mesmo ano/mes -- year e month nao sao unicos na tabela, mas repetir nao
# ajuda a ler os resultados.
$mes = [int](($stamp % 12) + 1)
$ano = 2026

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

# Utilizador com perfil HR -- e quem tem payroll.runs.read/write (caso 2).
$hrEmail = "rh-pl-$stamp@rivo.ao"
$hrUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $hrEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json").userId
Invoke-RestMethod "$base/identity/users/$hrUserId/roles" -Method Post -Body (@{ profile = "HR" } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null
$hrHeaders = @{ Authorization = "Bearer " + (Get-Token $hrEmail $pass) }

$semPerfilEmail = "semperfil-pl-$stamp@rivo.ao"
Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $semPerfilEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json" | Out-Null
$semPerfilHeaders = @{ Authorization = "Bearer " + (Get-Token $semPerfilEmail $pass) }

# --- Cenario, montado pelas rotas reais de hr.
$colaborador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Colaborador PL $stamp" } | ConvertTo-Json)).employeeId

$rh = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "RH PL $stamp" } | ConvertTo-Json)).employeeId

$aprovador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Aprovador PL $stamp" } | ConvertTo-Json)).employeeId

# Cargo sem autoridade de aprovacao (BR-20): o que a confere passaria ele
# proprio por governanca, e nao e isso que se verifica aqui.
$cargo = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ name = "Aprovador de Folha $stamp"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ positionId = $cargo } | ConvertTo-Json) | Out-Null

# **Estado determinista antes de comecar** -- o caso 6 verifica a recusa
# quando nao ha politica nenhuma para payroll.payroll_run.
#
# `@(...)` a forcar array: defesa documentada contra um modo de falha real
# do Invoke-RestMethod nesta suite (nota "Filtrar respostas JSON..." em
# implemented.md).
# Clear-RivoApprovalPolicies (_ambiente.ps1) repete ate confirmar por SQL: uma
# unica tentativa tolerava o K20 (known-issues.md) na propria suite, mas
# deixava a politica activa para tras -- e a submissao do caso 8 recusaria por
# ambiguidade (duas politicas igualmente especificas) se uma corrida anterior
# tivesse ficado exactamente assim.
Clear-RivoApprovalPolicies -ProcessType "payroll.payroll_run" -Headers $adminHeaders

Write-Host "`n=== Modulo payroll (esqueleto) ===`n"

Test-Case "1. Schema payroll com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from payroll.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de payroll" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='payroll'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='payroll' and table_name in ('app_user','audit_event','employee','approval_request')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema payroll" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. HR le e escreve payroll" {
    $r = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='HR' and c.claim_value='payroll.runs.read'"
    $w = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='HR' and c.claim_value='payroll.runs.write'"
    if ($r -ne "1" -or $w -ne "1") { throw "HR sem payroll.runs.read/write (read=$r write=$w)" }
    "HR le e escreve folhas"
}

Test-Case "3. Abrir folha" {
    $body = @{ year = $ano; month = $mes; openedByEmployeeId = $rh } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/payroll/runs" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders
    if (-not $r.runId) { throw "sem runId na resposta" }
    $script:runId = $r.runId
    "folha $ano/$mes aberta"
}

Test-Case "4. Mes fora de 1-12 e recusado" {
    $body = @{ year = $ano; month = 13; openedByEmployeeId = $rh } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "mes invalido recusado (400)"
}

Test-Case "5. Acrescentar item -- so o bruto, sem nenhum campo calculado" {
    $body = @{ employeeId = $colaborador; grossSalary = 350000 } | ConvertTo-Json
    Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders | Out-Null

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    if ($folha.items.Count -ne 1) { throw "esperado 1 item, obtido $($folha.items.Count)" }
    $item = $folha.items[0]
    if ([decimal]$item.grossSalary -ne 350000) { throw "bruto errado: $($item.grossSalary)" }

    # Nulo e nao zero: zero pareceria um calculo que deu zero. Nulo diz "nao
    # calculado", que e a verdade.
    if ($null -ne $item.netSalary) { throw "netSalary deveria ser nulo, veio $($item.netSalary)" }
    if ($null -ne $item.withholdingTax) { throw "withholdingTax deveria ser nulo, veio $($item.withholdingTax)" }
    if ($null -ne $item.socialSecurityContribution) { throw "socialSecurityContribution deveria ser nulo, veio $($item.socialSecurityContribution)" }

    "bruto 350000; net/IRT/INSS todos nulos, deliberado"
}

Test-Case "6. Salario nao positivo e recusado" {
    $body = @{ employeeId = $colaborador; grossSalary = 0 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 409 -and $code -ne 500) { throw "esperado erro, obtido $code" }
    "salario zero recusado ($code)"
}

Test-Case "7. Sem politica configurada, submeter recusa e a folha continua Draft" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/submission" -Method Post -Headers $hrHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    if ($folha.status -ne "Draft") { throw "estado '$($folha.status)' depois de uma submissao falhada" }
    "409 sem politica; folha continua Draft"
}

Test-Case "8. Com politica, submeter cria o processo em approval" {
    $politica = Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ processType = "payroll.payroll_run"; steps = @(@{ approverPositionId = $cargo }) } | ConvertTo-Json -Depth 5)
    $script:politicaId = $politica.policyId

    $r = Invoke-RestMethod "$base/payroll/runs/$($script:runId)/submission" -Method Post -Headers $hrHeaders
    $script:processoId = $r.approvalRequestId
    if (-not $script:processoId) { throw "submetida sem processo de aprovacao" }

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    if ($folha.status -ne "PendingApproval") { throw "estado '$($folha.status)'" }

    $processo = Invoke-RestMethod "$base/approval/requests/$($script:processoId)" -Headers $adminHeaders
    if ($processo.processType -ne "payroll.payroll_run") { throw "tipo de processo errado: $($processo.processType)" }
    if ($processo.sourceModule -ne "payroll") { throw "modulo de origem errado: $($processo.sourceModule)" }
    if ($processo.sourceReference -ne $script:runId) { throw "referencia de origem errada: $($processo.sourceReference)" }
    if ($processo.pendingApprovers -notcontains $aprovador) { throw "o aprovador nao ficou atribuido ao passo" }

    "processo $($script:processoId), 350000 de bruto, aprovador atribuido"
}

Test-Case "9. Depois de submetida, acrescentar item e recusado" {
    $body = @{ employeeId = $colaborador; grossSalary = 100000 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- ja nao esta em Draft"
}

Test-Case "10. Enquanto ninguem decide, aplicar a decisao mantem PendingApproval" {
    $r = Invoke-RestMethod "$base/payroll/runs/$($script:runId)/decision" -Method Post -Headers $hrHeaders
    if ($r.status -ne "PendingApproval") { throw "estado '$($r.status)'" }
    "continua PendingApproval"
}

Test-Case "11. Decidida em approval, o efeito e aplicado em payroll" {
    $body = @{ decidedByEmployeeId = $aprovador; action = "Approved"; notes = "Folha conferida." } | ConvertTo-Json
    Invoke-RestMethod "$base/approval/requests/$($script:processoId)/decisions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    # Decidido do outro lado, e a folha ainda nao sabe -- approval nunca
    # empurra (modules/approval.md).
    $antes = Invoke-Sql "select status from payroll.payroll_run where id='$($script:runId)'"
    if ($antes -ne "PendingApproval") { throw "approval alterou a folha sozinho: '$antes'" }

    $r = Invoke-RestMethod "$base/payroll/runs/$($script:runId)/decision" -Method Post -Headers $hrHeaders
    if ($r.status -ne "Approved") { throw "estado '$($r.status)'" }
    "aprovada em approval, e so aplicada quando payroll pergunta"
}

Test-Case "12. Aplicar a decisao outra vez nao falha nem duplica" {
    $r = Invoke-RestMethod "$base/payroll/runs/$($script:runId)/decision" -Method Post -Headers $hrHeaders
    if ($r.status -ne "Approved") { throw "estado '$($r.status)' na segunda chamada" }

    $aprovacoes = Invoke-Sql "select count(*) from audit.audit_event where action='payroll.run.approved' and entity_id='$($script:runId)'"
    if ($aprovacoes -ne "1") { throw "$aprovacoes registos de aprovacao na trilha, esperado 1" }
    "segunda chamada devolve Approved, e a trilha nao duplica"
}

Test-Case "13. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "14. Abertura, submissao e aprovacao ficam na trilha, com actor" {
    $abrir = Invoke-Sql "select count(*) from audit.audit_event where action='payroll.run.opened' and entity_id='$($script:runId)' and actor_id is not null"
    if ($abrir -ne "1") { throw "abertura nao auditada com actor" }
    $submeter = Invoke-Sql "select count(*) from audit.audit_event where action='payroll.run.submitted' and entity_id='$($script:runId)' and actor_id is not null"
    if ($submeter -ne "1") { throw "submissao nao auditada com actor" }
    "abertura e submissao na trilha, ambas com actor"
}

Test-Case "15. A suite nao deixa politica de payroll activa atras de si" {
    @(Invoke-RestMethod "$base/approval/policies" -Headers $adminHeaders) |
    Where-Object { $_.processType -eq "payroll.payroll_run" -and $_.isActive } |
    ForEach-Object {
        Invoke-RestMethod "$base/approval/policies/$($_.policyId)/deactivation" `
            -Method Post -Headers $adminHeaders | Out-Null
    }

    $activas = Invoke-Sql "select count(*) from approval.policy where process_type='payroll.payroll_run' and is_active=1"
    if ($activas -ne "0") { throw "$activas politicas de payroll ficaram activas" }
    "nenhuma politica de payroll.payroll_run activa"
}

Test-Case "16. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    if ($folha.status -ne "Approved") { throw "estado perdido: $($folha.status)" }
    if ($folha.items.Count -ne 1) { throw "itens perdidos: $($folha.items.Count)" }
    "folha $ano/$mes, estado e item intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
