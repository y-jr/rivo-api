# Verificação do módulo `payroll`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-payroll.ps1
#
# Folha e itens, ligados a `approval` pelo total bruto, com IRT/INSS
# calculados desde 2026-08-30 — `payroll` pergunta a `fiscal`, nunca calcula
# por si (`modules/fiscal.md`). **Pressupõe que `verify-fiscal` já correu**
# nesta mesma base de dados: é lá que o INSS (3%/8%) e a tabela de escalões
# de IRT são semeados, com os códigos e datas reais que o motor consome —
# sem isso, o caso 5 recusa por falta de dados fiscais em vez de calcular
# (mesma dependência de ordem que `verify-payables` → `verify-ledger`).
#
# Recibo, desde 2026-08-30: anexar um documento já carregado a um Item de
# Folha, mesmo desenho de `hr` (ADR-009) — ver os casos 6 e 15-18.
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

# Utilizador com perfil HR -- e quem tem payroll.runs.read/write (caso 2) e
# documents.write (para o recibo, casos 6 e 15-18): HR nao precisa do Admin
# para nenhum dos dois.
$hrEmail = "rh-pl-$stamp@rivo.ao"
$hrUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $hrEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json").userId
Invoke-RestMethod "$base/identity/users/$hrUserId/roles" -Method Post -Body (@{ profile = "HR" } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null
$hrToken = Get-Token $hrEmail $pass
$hrHeaders = @{ Authorization = "Bearer $hrToken" }

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

# **Estado determinista antes de comecar** -- o caso 9 verifica a recusa
# quando nao ha politica nenhuma para payroll.payroll_run.
#
# `@(...)` a forcar array: defesa documentada contra um modo de falha real
# do Invoke-RestMethod nesta suite (nota "Filtrar respostas JSON..." em
# implemented.md).
# Clear-RivoApprovalPolicies (_ambiente.ps1) repete ate confirmar por SQL: uma
# unica tentativa tolerava o K20 (known-issues.md) na propria suite, mas
# deixava a politica activa para tras -- e a submissao do caso 10 recusaria
# por ambiguidade (duas politicas igualmente especificas) se uma corrida
# anterior tivesse ficado exactamente assim.
Clear-RivoApprovalPolicies -ProcessType "payroll.payroll_run" -Headers $adminHeaders

# --- Upload de recibo, mesmo mecanismo de verify-documents.ps1 -- ver ali o
# porque da portabilidade Windows/Linux (curl.exe vs curl, NUL vs /dev/null).
$temp = [System.IO.Path]::GetTempPath()
$curl = if (Get-Command curl.exe -ErrorAction SilentlyContinue) { "curl.exe" } else { "curl" }

$tempFile = Join-Path $temp "rivo-recibo-$stamp.txt"
Set-Content -Path $tempFile -Value "Recibo de vencimento de teste - $stamp" -NoNewline -Encoding UTF8

function Invoke-Upload {
    param([string]$FilePath, [string]$Category, [string]$Token)

    return (& $curl -s -X POST "$base/documents" `
        -H "Authorization: Bearer $Token" `
        -F "file=@$FilePath" `
        -F "category=$Category" 2>$null)
}

Write-Host "`n=== Modulo payroll ===`n"

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

Test-Case "5. Acrescentar item -- INSS e IRT calculados por fiscal, liquido derivado" {
    $body = @{ employeeId = $colaborador; grossSalary = 350000 } | ConvertTo-Json
    Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders | Out-Null

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    if ($folha.items.Count -ne 1) { throw "esperado 1 item, obtido $($folha.items.Count)" }
    $item = $folha.items[0]
    if ([decimal]$item.grossSalary -ne 350000) { throw "bruto errado: $($item.grossSalary)" }

    # 350.000 x 3% = 10.500 de INSS; materia colectavel 339.500 cai no
    # escalao 300.001-500.000: IRT = 49.250 + (339.500-300.000) x 19% =
    # 56.755. Liquido = 350.000 - 56.755 - 10.500 = 282.745 (verify-fiscal.ps1
    # semeia o INSS e a tabela de IRT que tornam isto determinado).
    if ([decimal]$item.socialSecurityContribution -ne 10500) { throw "INSS errado: $($item.socialSecurityContribution)" }
    if ([decimal]$item.withholdingTax -ne 56755) { throw "IRT errado: $($item.withholdingTax)" }
    if ([decimal]$item.netSalary -ne 282745) { throw "liquido errado: $($item.netSalary)" }

    "bruto 350000, INSS 10500, IRT 56755, liquido 282745"
}

Test-Case "6. Anexar recibo antes de Aprovada e recusado com 409" {
    $r = Invoke-Upload $tempFile "recibo" $hrToken | ConvertFrom-Json
    $script:reciboDocumentId = $r.documentId
    if (-not $script:reciboDocumentId) { throw "upload sem documentId" }

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    $script:itemId = $folha.items[0].itemId

    $body = @{ documentId = $script:reciboDocumentId; category = "recibo" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items/$($script:itemId)/documents" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "folha ainda Draft -- recibo so se anexa depois de Aprovada (inferencia, ver modules/payroll.md)"
}

Test-Case "7. Salario nao positivo e recusado com 400" {
    $body = @{ employeeId = $colaborador; grossSalary = 0 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "campo mal preenchido -- 400, nao 409"
}

Test-Case "8. Sem INSS/IRT em vigor a data, o item e recusado (recusa, nao omissao)" {
    # Um periodo anterior a 2020 nao esta coberto pela vigencia que
    # verify-fiscal.ps1 semeia (2020-01-01 em diante) -- mesmo padrao de
    # `IssueSalesInvoice` perante `NoRateInForce`: inventar o valor seria
    # pior do que recusar.
    $folhaAntiga = Invoke-RestMethod "$base/payroll/runs" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ year = 2019; month = 6; openedByEmployeeId = $rh } | ConvertTo-Json)

    $body = @{ employeeId = $colaborador; grossSalary = 100000 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($folhaAntiga.runId)/items" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }

    $folha = Invoke-RestMethod "$base/payroll/runs/$($folhaAntiga.runId)" -Headers $hrHeaders
    if ($folha.items.Count -ne 0) { throw "item nasceu apesar da recusa: $($folha.items.Count)" }
    "2019/06 fora da vigencia semeada -- recusado, sem item a nascer"
}

Test-Case "9. Sem politica configurada, submeter recusa e a folha continua Draft" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/submission" -Method Post -Headers $hrHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    if ($folha.status -ne "Draft") { throw "estado '$($folha.status)' depois de uma submissao falhada" }
    "409 sem politica; folha continua Draft"
}

Test-Case "10. Com politica, submeter cria o processo em approval" {
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

Test-Case "11. Depois de submetida, acrescentar item e recusado" {
    $body = @{ employeeId = $colaborador; grossSalary = 100000 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- ja nao esta em Draft"
}

Test-Case "12. Enquanto ninguem decide, aplicar a decisao mantem PendingApproval" {
    $r = Invoke-RestMethod "$base/payroll/runs/$($script:runId)/decision" -Method Post -Headers $hrHeaders
    if ($r.status -ne "PendingApproval") { throw "estado '$($r.status)'" }
    "continua PendingApproval"
}

Test-Case "13. Decidida em approval, o efeito e aplicado em payroll" {
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

Test-Case "14. Aplicar a decisao outra vez nao falha nem duplica" {
    $r = Invoke-RestMethod "$base/payroll/runs/$($script:runId)/decision" -Method Post -Headers $hrHeaders
    if ($r.status -ne "Approved") { throw "estado '$($r.status)' na segunda chamada" }

    $aprovacoes = Invoke-Sql "select count(*) from audit.audit_event where action='payroll.run.approved' and entity_id='$($script:runId)'"
    if ($aprovacoes -ne "1") { throw "$aprovacoes registos de aprovacao na trilha, esperado 1" }
    "segunda chamada devolve Approved, e a trilha nao duplica"
}

Test-Case "15. Folha Aprovada: anexar o mesmo recibo agora e aceite" {
    $body = @{ documentId = $script:reciboDocumentId; category = "recibo" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items/$($script:itemId)/documents" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders
    if (-not $r.linkId) { throw "sem linkId na resposta" }
    "recibo anexado ao item, folha ja Aprovada"
}

Test-Case "16. Listar documentos do item mostra o recibo, com metadados de documents" {
    $docs = @(Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items/$($script:itemId)/documents" -Headers $hrHeaders)
    if ($docs.Count -ne 1) { throw "esperado 1 documento, obtido $($docs.Count)" }
    if ($docs[0].documentId -ne $script:reciboDocumentId) { throw "documentId errado" }
    if ($docs[0].category -ne "recibo") { throw "categoria errada: $($docs[0].category)" }
    if (-not $docs[0].fileName) { throw "sem fileName -- a juncao com documents falhou" }
    "1 documento, categoria 'recibo', metadados de documents presentes"
}

Test-Case "17. Anexar documento inexistente devolve 404" {
    $body = @{ documentId = [Guid]::NewGuid().ToString(); category = "recibo" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs/$($script:runId)/items/$($script:itemId)/documents" -Method Post -Body $body -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "documento inexistente -- 404, verificado pelo contrato publicado de documents"
}

Test-Case "18. Anexar recibo fica na trilha, com actor" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='payroll.item.document_attached' and entity_id='$($script:itemId)' and actor_id is not null"
    if ($n -ne "1") { throw "$n registos de anexo na trilha, esperado 1, com actor" }
    "1 registo, com actor"
}

Test-Case "19. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/payroll/runs" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "20. Abertura, submissao e aprovacao ficam na trilha, com actor" {
    $abrir = Invoke-Sql "select count(*) from audit.audit_event where action='payroll.run.opened' and entity_id='$($script:runId)' and actor_id is not null"
    if ($abrir -ne "1") { throw "abertura nao auditada com actor" }
    $submeter = Invoke-Sql "select count(*) from audit.audit_event where action='payroll.run.submitted' and entity_id='$($script:runId)' and actor_id is not null"
    if ($submeter -ne "1") { throw "submissao nao auditada com actor" }
    "abertura e submissao na trilha, ambas com actor"
}

Test-Case "21. A suite nao deixa politica de payroll activa atras de si" {
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

Test-Case "22. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runId)" -Headers $hrHeaders
    if ($folha.status -ne "Approved") { throw "estado perdido: $($folha.status)" }
    if ($folha.items.Count -ne 1) { throw "itens perdidos: $($folha.items.Count)" }
    if ([decimal]$folha.items[0].netSalary -ne 282745) { throw "liquido perdido ou alterado: $($folha.items[0].netSalary)" }
    "folha $ano/$mes, estado, item e liquido calculado intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
