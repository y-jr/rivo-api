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

# Havia aqui uma conta de perfil RH, sem colaborador associado, que abria a
# folha declarando `openedByEmployeeId`. O ADR-057 tirou-lhe o campo: quem abre
# uma folha e a conta autenticada, que tem de estar ligada a um colaborador.
# A conta desapareceu porque nao havia nada que so ela pudesse fazer --
# `$requisitanteConta`, ja ligada, abre a folha e submete-a.

# Era uma conta sem perfil nenhum ate o ADR-059 tirar o registo publico: nao ha
# forma de criar uma agora, e o perfil e obrigatorio no convite. `Cliente` e o
# mais estreito que existe -- tem `documents.write` e mais nada --, por isso
# continua a provar o mesmo 403 por falta de permissao de approval.
$semPermissaoEmail = "sempermissao-ap-$stamp@rivo.ao"
New-RivoConta -Email $semPermissaoEmail -Password $pass -AdminHeaders $adminHeaders -Perfil "Cliente" | Out-Null
$semPermissaoHeaders = @{ Authorization = "Bearer " + (Get-Token $semPermissaoEmail $pass) }

# --- Cenario: uma folha submetida, so para ter um pedido PendingApproval em
# approval. O que se verifica daqui para a frente e o pedido, nao a folha.
#
# **Desde o ADR-050, decidir e cancelar exigem conta ligada a Colaborador.** O
# identificador deixou de vir no corpo do pedido: quem decide e quem cancela
# resolvem-se da conta autenticada. Por isso o requisitante e o aprovador
# nascem com conta propria, e cada um age com os seus cabecalhos.
$requisitanteConta = New-RivoColaboradorComConta `
    -Email "req-ap-$stamp@rivo.ao" -Nome "RH Aprovacao $stamp" `
    -AdminHeaders $adminHeaders -Perfil "Admin"
$rh = $requisitanteConta.EmployeeId

$aprovadorConta = New-RivoColaboradorComConta `
    -Email "apr-ap-$stamp@rivo.ao" -Nome "Aprovador AP $stamp" `
    -AdminHeaders $adminHeaders -Perfil "Admin"
$aprovador = $aprovadorConta.EmployeeId

$outroColaborador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Outro Colaborador AP $stamp" } | ConvertTo-Json)).employeeId

$cargo = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ name = "Aprovador AP $stamp"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ positionId = $cargo } | ConvertTo-Json) | Out-Null

# Clear-RivoApprovalPolicies (_ambiente.ps1) repete ate confirmar por SQL: uma
# unica tentativa tolerava o K20 (known-issues.md) na propria suite, mas
# deixava a politica activa para tras -- e foi exactamente isso que fez esta
# suite rebentar sem produzir nenhum caso, na primeira corrida em CI a seguir
# a verify-payroll: a submissao (mais abaixo) encontrou duas politicas
# igualmente especificas e recusou por ambiguidade.
Clear-RivoApprovalPolicies -ProcessType "payroll.payroll_run" -Headers $adminHeaders

$politica = Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ processType = "payroll.payroll_run"; steps = @(@{ approverPositionId = $cargo }) } | ConvertTo-Json -Depth 5)
$politicaId = $politica.policyId

$folha = Invoke-RestMethod "$base/payroll/runs" -Method Post -ContentType "application/json" -Headers $requisitanteConta.Headers `
    -Body (@{ year = $ano; month = $mes } | ConvertTo-Json)
$runId = $folha.runId

Invoke-RestMethod "$base/payroll/runs/$runId/items" -Method Post -ContentType "application/json" -Headers $requisitanteConta.Headers `
    -Body (@{ employeeId = $outroColaborador; grossSalary = 250000 } | ConvertTo-Json) | Out-Null

$submissao = Invoke-RestMethod "$base/payroll/runs/$runId/submission" -Method Post -Headers $requisitanteConta.Headers
$requestId = $submissao.approvalRequestId

Write-Host "`n=== Cancelamento de pedidos de aprovacao (K18) ===`n"

Test-Case "1. Pedido pendente foi criado, com o requisitante certo" {
    if (-not $requestId) { throw "submissao da folha nao devolveu approvalRequestId" }
    $processo = Invoke-RestMethod "$base/approval/requests/$requestId" -Headers $adminHeaders
    if ($processo.status -ne "InProgress") { throw "estado inicial '$($processo.status)', esperado InProgress" }
    "pedido $requestId, PendingApproval do lado de payroll"
}

Test-Case "1a. Requisitante atribuido ao proprio passo nao aparece na sua caixa (BR-2)" {
    # $rh submeteu o pedido. Atribui-lo tambem ao cargo aprovador simula o
    # caso real: uma pessoa que ocupa o cargo aprovador e tambem submete o
    # pedido. BR-2 recusa-lhe sempre a decisao (ApprovalRequest.Decide) --
    # aparecer em "pendingFor=$rh" seria a caixa de entrada prometer uma
    # decisao que o servidor nunca vai deixar registar.
    Invoke-RestMethod "$base/hr/employees/$rh/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ positionId = $cargo } | ConvertTo-Json) | Out-Null

    $caixaDoRequisitante = Invoke-RestMethod "$base/approval/requests?pendingFor=$rh" -Headers $adminHeaders
    if ($caixaDoRequisitante | Where-Object { $_.requestId -eq $requestId }) {
        throw "o pedido apareceu na caixa de quem o submeteu"
    }

    # Controlo: o aprovador de facto continua a ve-lo -- o filtro nao ficou
    # vazio para todos, so exclui o requisitante.
    $caixaDoAprovador = Invoke-RestMethod "$base/approval/requests?pendingFor=$aprovador" -Headers $adminHeaders
    if (-not ($caixaDoAprovador | Where-Object { $_.requestId -eq $requestId })) {
        throw "o aprovador de facto deixou de ver o pedido"
    }
    "ausente da caixa do requisitante, presente na do aprovador de facto"
}

Test-Case "2. Quem nao submeteu nao cancela (K18)" {
    # $rh submeteu a folha (abriu-a com a sua conta) -- e quem approval regista como
    # RequestedByEmployeeId. $aprovador esta atribuido ao passo, mas nao e o
    # requisitante: tentar cancelar e a mesma familia de regra que BR-2/BR-4.
    # Age com os cabecalhos do aprovador: e a conta dele que o servidor
    # resolve, e nao um identificador que ele declare (ADR-050).
    $code = try {
        Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Headers $aprovadorConta.Headers
        200
    }
    catch { if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { throw } }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }

    $processo = Invoke-RestMethod "$base/approval/requests/$requestId" -Headers $adminHeaders
    if ($processo.status -ne "InProgress") { throw "a tentativa recusada alterou o estado: '$($processo.status)'" }
    "403 -- so quem submeteu cancela; o pedido continua InProgress"
}

Test-Case "3. Pedido inexistente devolve 404" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$([guid]::NewGuid())/cancellation" -Method Post -Headers $requisitanteConta.Headers }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "404 num pedido que nao existe"
}

Test-Case "4. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Headers $semPermissaoHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "5. Quem submeteu cancela" {
    Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Headers $requisitanteConta.Headers | Out-Null

    $processo = Invoke-RestMethod "$base/approval/requests/$requestId" -Headers $adminHeaders
    if ($processo.status -ne "Cancelled") { throw "estado '$($processo.status)', esperado Cancelled" }
    "204, e o pedido passa a Cancelled"
}

Test-Case "6. Cancelar outra vez e recusado -- ja esta fechado" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$requestId/cancellation" -Method Post -Headers $requisitanteConta.Headers }
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

    $r = Invoke-RestMethod "$base/payroll/runs/$runId/decision" -Method Post -Headers $requisitanteConta.Headers
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
