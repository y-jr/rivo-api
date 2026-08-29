# Verificação do cancelamento de pedidos de aprovação, `POST
# /approval/requests/{id}/cancellation`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-approval.ps1
#
# Não é a suite geral de `approval` -- políticas, decisões e BR-2/4/6/17 já
# são exercitadas indirectamente por `verify-procurement.ps1`,
# `verify-ledger.ps1` e `verify-payroll.ps1`, cada uma a partir do módulo que
# submete. O cancelamento (K18, known-issues.md e "Próximos passos" em
# project-state.md) é o único caminho HTTP que nenhuma suite exercitava: a
# regra tinha teste de domínio, e nada mais.
#
# `approval` não tem rota para criar um pedido directamente -- só um módulo de
# negócio submete. Esta suite usa `payroll` como veículo, por ser o mais
# simples a montar; o que se verifica é o endpoint de `approval`, não
# `payroll`.
#
# Re-executável: cada corrida usa um período de folha próprio e limpa a
# política de `payroll.payroll_run` que cria.

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
$mes = [int](($stamp % 12) + 1)
$ano = 2026

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

$hrEmail = "rh-ap-$stamp@rivo.ao"
$hrUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $hrEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json").userId
Invoke-RestMethod "$base/identity/users/$hrUserId/roles" -Method Post -Body (@{ profile = "HR" } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null
$hrHeaders = @{ Authorization = "Bearer " + (Get-Token $hrEmail $pass) }

$semPerfilEmail = "semperfil-ap-$stamp@rivo.ao"
Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $semPerfilEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json" | Out-Null
$semPerfilHeaders = @{ Authorization = "Bearer " + (Get-Token $semPerfilEmail $pass) }

# --- Cenario: uma folha submetida, so para ter um pedido PendingApproval em
# approval. O que se verifica daqui para a frente e o pedido, nao a folha.
$rh = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "RH Aprovacao $stamp" } | ConvertTo-Json)).employeeId

$outroColaborador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Outro Colaborador AP $stamp" } | ConvertTo-Json)).employeeId

$aprovador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Aprovador AP $stamp" } | ConvertTo-Json)).employeeId

$cargo = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ name = "Aprovador AP $stamp"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ positionId = $cargo } | ConvertTo-Json) | Out-Null

# Envolvido em try/catch: um 404 aqui e o K20 (known-issues.md), ja
# documentado em verify-ledger, verify-procurement e verify-payroll -- nao se
# deixa a limpeza inicial abortar a suite inteira.
try {
    @(Invoke-RestMethod "$base/approval/policies" -Headers $adminHeaders) |
    Where-Object { $_.processType -eq "payroll.payroll_run" -and $_.isActive } |
    ForEach-Object {
        Invoke-RestMethod "$base/approval/policies/$($_.policyId)/deactivation" `
            -Method Post -Headers $adminHeaders | Out-Null
    }
}
catch {
    Write-Host "  AVISO  limpeza inicial de politica falhou (K20): $($_.Exception.Message)" -ForegroundColor Yellow
}

$politica = Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ processType = "payroll.payroll_run"; steps = @(@{ approverPositionId = $cargo }) } | ConvertTo-Json -Depth 5)
$politicaId = $politica.policyId

$folha = Invoke-RestMethod "$base/payroll/runs" -Method Post -ContentType "application/json" -Headers $hrHeaders `
    -Body (@{ year = $ano; month = $mes; openedByEmployeeId = $rh } | ConvertTo-Json)
$runId = $folha.runId

Invoke-RestMethod "$base/payroll/runs/$runId/items" -Method Post -ContentType "application/json" -Headers $hrHeaders `
    -Body (@{ employeeId = $outroColaborador; grossSalary = 250000 } | ConvertTo-Json) | Out-Null

$submissao = Invoke-RestMethod "$base/payroll/runs/$runId/submission" -Method Post -Headers $hrHeaders
$requestId = $submissao.approvalRequestId

Write-Host "`n=== Cancelamento de pedidos de aprovacao (K18) ===`n"

Test-Case "1. Pedido pendente foi criado, com o requisitante certo" {
    if (-not $requestId) { throw "submissao da folha nao devolveu approvalRequestId" }
    $processo = Invoke-RestMethod "$base/approval/requests/$requestId" -Headers $adminHeaders
    if ($processo.status -ne "InProgress") { throw "estado inicial '$($processo.status)', esperado InProgress" }
    "pedido $requestId, PendingApproval do lado de payroll"
}

Test-Case "2. Quem nao submeteu nao cancela (K18)" {
    # $rh submeteu a folha (openedByEmployeeId) -- e quem approval regista como
    # RequestedByEmployeeId. $aprovador esta atribuido ao passo, mas nao e o
    # requisitante: tentar cancelar e a mesma familia de regra que BR-2/BR-4.
    $body = @{ cancelledByEmployeeId = $aprovador } | ConvertTo-Json
    $resp = $null
    $code = try {
        $resp = Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
        200
    }
    catch { if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { throw } }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }

    $processo = Invoke-RestMethod "$base/approval/requests/$requestId" -Headers $adminHeaders
    if ($processo.status -ne "InProgress") { throw "a tentativa recusada alterou o estado: '$($processo.status)'" }
    "403 -- so quem submeteu cancela; o pedido continua InProgress"
}

Test-Case "3. Pedido inexistente devolve 404" {
    $body = @{ cancelledByEmployeeId = $rh } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$([guid]::NewGuid())/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "404 num pedido que nao existe"
}

Test-Case "4. Autorizacao: sem token 401, sem perfil 403" {
    $body = @{ cancelledByEmployeeId = $rh } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Body $body -ContentType "application/json" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "5. Quem submeteu cancela" {
    $body = @{ cancelledByEmployeeId = $rh } | ConvertTo-Json
    Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $processo = Invoke-RestMethod "$base/approval/requests/$requestId" -Headers $adminHeaders
    if ($processo.status -ne "Cancelled") { throw "estado '$($processo.status)', esperado Cancelled" }
    "204, e o pedido passa a Cancelled"
}

Test-Case "6. Cancelar outra vez e recusado -- ja esta fechado" {
    $body = @{ cancelledByEmployeeId = $rh } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no segundo cancelamento"
}

Test-Case "7. Cancelamento e a tentativa recusada ficam na trilha, com actor" {
    $cancelado = Invoke-Sql "select count(*) from audit.audit_event where action='approval.request.cancelled' and entity_id='$requestId' and actor_id is not null"
    if ($cancelado -ne "1") { throw "cancelamento nao esta na trilha com actor" }

    $violacao = Invoke-Sql "select count(*) from audit.audit_event where action='approval.request.segregation_violation' and entity_id='$requestId'"
    if ([int]$violacao -lt 1) { throw "a tentativa recusada (caso 2) nao ficou na trilha" }
    "cancelamento com actor, e a tentativa de quem nao submeteu tambem na trilha"
}

Test-Case "8. payroll trata Cancelled como recusa -- approval nunca empurra" {
    # A folha nao sabe ainda: so muda quando payroll pergunta (mesmo desenho
    # verificado em verify-payroll.ps1 caso 11).
    $antes = Invoke-Sql "select status from payroll.payroll_run where id='$runId'"
    if ($antes -ne "PendingApproval") { throw "payroll mudou sozinho: '$antes'" }

    $r = Invoke-RestMethod "$base/payroll/runs/$runId/decision" -Method Post -Headers $hrHeaders
    if ($r.status -ne "Refused") { throw "estado '$($r.status)', esperado Refused" }
    "Cancelled em approval torna-se Refused em payroll, so quando perguntado"
}

Test-Case "9. A suite nao deixa politica de payroll activa atras de si" {
    Invoke-RestMethod "$base/approval/policies/$politicaId/deactivation" -Method Post -Headers $adminHeaders | Out-Null
    $activas = Invoke-Sql "select count(*) from approval.policy where process_type='payroll.payroll_run' and is_active=1"
    if ($activas -ne "0") { throw "$activas politicas de payroll ficaram activas" }
    "nenhuma politica de payroll.payroll_run activa"
}

Test-Case "10. Estado sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $processo = Invoke-RestMethod "$base/approval/requests/$requestId" -Headers $adminHeaders
    if ($processo.status -ne "Cancelled") { throw "estado perdido: '$($processo.status)'" }
    "pedido $requestId continua Cancelled apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
