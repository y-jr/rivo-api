# Verificação da camada de composição `Rivo.EmployeePortal` (Portal do
# Colaborador, ADR-042).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-employee-portal.ps1

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

$dotenv = Get-RivoCredentials
$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

# Utilizador com perfil HR, para admitir o colaborador desta suite.
#
# **Colaborador com conta, e nao so uma conta**: desde os casos dos recibos, esta
# e a conta que abre a folha salarial -- e `payroll` recusa quem nao esteja ligado
# a um colaborador, porque quem abre a folha e o requerente do processo de
# aprovacao (ADR-050). Com uma conta sem vinculo, o POST /payroll/runs devolvia
# 400: "so um colaborador abre folhas".
$hrEmail = "portal-rh-$stamp@rivo.ao"
$hrConta = New-RivoColaboradorComConta -Email $hrEmail -Nome "RH Portal $stamp" `
    -AdminHeaders $adminHeaders -Perfil "HR" -Password $pass
$hrHeaders = $hrConta.Headers

# Quem decide, para os dois processos que esta suite atravessa: o pedido de
# ferias (caso 13) e a folha salarial (caso 16). Desde o ADR-050 quem decide
# resolve-se do token, e por isso tem de ser uma conta ligada a um colaborador.
$aprovadorConta = New-RivoColaboradorComConta -Email "portal-apr-$stamp@rivo.ao" `
    -Nome "Aprovador Portal $stamp" -AdminHeaders $adminHeaders -Perfil "Admin"

# Cargo sem autoridade de aprovacao (BR-20): o que a confere passaria ele proprio
# por governanca, e nao e isso que se verifica aqui.
$cargoAprovador = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" `
        -Headers $adminHeaders `
        -Body (@{ name = "Aprovador Portal $stamp"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

Invoke-RestMethod "$base/hr/employees/$($aprovadorConta.EmployeeId)/positions" -Method Post `
    -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ positionId = $cargoAprovador } | ConvertTo-Json) | Out-Null

Write-Host "`n=== Camada de composicao employee-portal ===`n"

Test-Case "1. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "2. Admin autenticado sem colaborador ligado -> 403 -- o portal nao contorna a falta de vinculo" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me" -Headers $adminHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- Admin nao tem hr.Employee ligado, e o portal recusa em vez de adivinhar"
}

$script:deptId = $null
Test-Case "3. Abrir departamento para o colaborador da suite" {
    $b = @{ name = "PortalDept-$stamp" } | ConvertTo-Json
    $script:deptId = (Invoke-RestMethod "$base/hr/departments" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).departmentId
    if (-not $script:deptId) { throw "sem id" }
    "departamento criado"
}

$script:ownEmail = "colaborador-$stamp@rivo.ao"
$script:ownUserId = $null
$script:ownEmployeeId = $null
Test-Case "4. Colaborador com conta ligada ve o seu proprio perfil" {
    # `Colaborador` desde o ADR-061: e o perfil de quem trabalha ca e nao
    # administra nada, e entra sem uma unica permissao. Era `Cliente` enquanto
    # nao existia -- que e a audiencia externa, e trazia `documents.write` a
    # quem so queria ver o seu. O portal nao pede permissao nenhuma: pede o
    # vinculo.
    $script:ownUserId = New-RivoConta -Email $script:ownEmail -Password $pass `
        -AdminHeaders $adminHeaders -Perfil "Colaborador"

    $b = @{ fullName = "Colaborador Portal $stamp"; departmentId = $script:deptId } | ConvertTo-Json
    $script:ownEmployeeId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId
    if (-not $script:ownEmployeeId) { throw "colaborador nao foi criado" }

    # Ligacao em passo proprio desde o ADR-054, e com cabecalhos de Admin: RH
    # admite mas nao liga -- a permissao esta fora do perfil de proposito.
    Invoke-RestMethod "$base/hr/employees/$($script:ownEmployeeId)/account" -Method Post `
        -Body (@{ userId = $script:ownUserId } | ConvertTo-Json) -ContentType "application/json" `
        -Headers $adminHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($perfil.employeeId -ne $script:ownEmployeeId) { throw "employeeId '$($perfil.employeeId)' nao e o proprio '$($script:ownEmployeeId)'" }
    if ($perfil.displayName -ne "Colaborador Portal $stamp") { throw "nome errado: '$($perfil.displayName)'" }
    if ($perfil.departmentId -ne $script:deptId) { throw "departamento errado" }
    if ($perfil.status -ne "Active") { throw "estado esperado Active, obtido '$($perfil.status)'" }
    "employeeId=$($script:ownEmployeeId), nome e departamento correctos"
}

Test-Case "5. Sem cargo atribuido, currentPosition vem nulo" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($null -ne $perfil.currentPosition) { throw "currentPosition devia ser nulo, veio $($perfil.currentPosition | ConvertTo-Json -Compress)" }
    "currentPosition nulo"
}

$script:positionId = $null
Test-Case "6. Com cargo atribuido, currentPosition aparece no proprio perfil" {
    $b = @{ name = "Analista-$stamp"; hierarchyLevel = 5; grantsApprovalAuthority = $false } | ConvertTo-Json
    $script:positionId = (Invoke-RestMethod "$base/hr/positions" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).positionId
    $b = @{ positionId = $script:positionId } | ConvertTo-Json
    Invoke-RestMethod "$base/hr/employees/$($script:ownEmployeeId)/positions" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($perfil.currentPosition.name -ne "Analista-$stamp") { throw "cargo actual nao apareceu" }
    if ($perfil.currentPosition.grantsApprovalAuthority) { throw "este cargo nao confere autoridade" }
    "currentPosition.name='Analista-$stamp'"
}

Test-Case "7. Outro utilizador sem colaborador ligado -> 403, nunca ve o colaborador de outro" {
    # O 403 aqui e por falta de vinculo, e nao por falta de permissao: mesmo com
    # perfil -- e desde o ADR-059 o convite obriga a um --, quem nao esta ligado
    # a um colaborador nao tem "o proprio" para ver.
    $e2 = "semvinculo-$stamp@rivo.ao"
    New-RivoConta -Email $e2 -Password $pass -AdminHeaders $adminHeaders -Perfil "Cliente" | Out-Null
    $h2 = @{ Authorization = "Bearer " + (Get-Token $e2 $pass) }

    $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me" -Headers $h2 }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- so ve o proprio, e o proprio nao existe para esta conta"
}

# --- As quatro leituras do proprio (ADR-062). Montadas com as rotas reais de
# `hr` e `payroll`, porque e a juncao entre os tres que se quer provar: o
# portal nao tem dados seus.
$script:rotas = @("attendance", "leave", "documents", "payslips")

Test-Case "8. As quatro leituras sem autenticacao -> 401" {
    foreach ($r in $script:rotas) {
        $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me/$r" }
        if ($code -ne 401) { throw "/portal/me/$r devolveu $code, esperado 401" }
    }
    "401 nas quatro"
}

Test-Case "9. Conta sem colaborador ligado -> 403 nas quatro, nunca 404" {
    # 403 e nao 404 de proposito: a conta existe e esta autenticada, so nao tem
    # "o proprio" que o portal existe para mostrar (ADR-042).
    foreach ($r in $script:rotas) {
        $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me/$r" -Headers $adminHeaders }
        if ($code -ne 403) { throw "/portal/me/$r devolveu $code, esperado 403" }
    }
    "403 nas quatro -- por falta de vinculo, e nao de permissao"
}

$script:hoje = [DateTime]::UtcNow.Date
$script:hojeIso = $script:hoje.ToString("yyyy-MM-dd")

Test-Case "10. Assiduidade marcada por RH aparece na leitura do proprio" {
    Invoke-RestMethod "$base/hr/attendance/clock" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ employeeId = $script:ownEmployeeId; day = $script:hojeIso } | ConvertTo-Json) | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $registos = Get-RivoLista "$base/portal/me/attendance" -Headers $ownHeaders

    if ($registos.Count -lt 1) { throw "nenhum registo, esperado o de hoje" }
    $doDia = $registos | Where-Object { $_.day -eq $script:hojeIso }
    if (-not $doDia) { throw "o dia de hoje nao aparece: $($registos.day -join ', ')" }
    if (-not $doDia.checkedInAt) { throw "sem hora de entrada" }
    "$($registos.Count) registo(s), com o dia de hoje e hora de entrada"
}

Test-Case "11. Sem from/to, a janela por omissao e o mes corrente" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $registos = Get-RivoLista "$base/portal/me/attendance" -Headers $ownHeaders

    $primeiroDoMes = (Get-Date -Year $script:hoje.Year -Month $script:hoje.Month -Day 1).ToString("yyyy-MM-dd")
    foreach ($r in $registos) {
        if ($r.day -lt $primeiroDoMes) { throw "o dia $($r.day) e anterior ao mes corrente" }
        if ($r.day -gt $script:hojeIso) { throw "o dia $($r.day) e futuro" }
    }
    "todos os $($registos.Count) registo(s) dentro de [$primeiroDoMes, $($script:hojeIso)]"
}

Test-Case "12. A janela pedida e respeitada -- um mes sem registos vem vazio" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $registos = Get-RivoLista "$base/portal/me/attendance?from=2020-01-01&to=2020-01-31" -Headers $ownHeaders
    # A mensagem nomeia os dias: "obtido 1" sozinho nao distingue um registo
    # verdadeiro de uma lista vazia mal contada, e foi exactamente essa a duvida
    # que custou uma corrida de CI a 2026-09-16.
    if ($registos.Count -ne 0) {
        throw "esperado vazio em Janeiro de 2020, obtido $($registos.Count): $($registos.day -join ', ')"
    }

    # Janela invertida e 400, e nao uma lista vazia: sem esta recusa, o ecra
    # leria a resposta como "nao tem marcacoes".
    $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me/attendance?from=2026-03-31&to=2026-03-01" -Headers $ownHeaders }
    if ($code -ne 400) { throw "janela invertida devolveu $code, esperado 400" }

    "Janeiro de 2020 vazio, janela invertida com 400 -- a janela nao e ignorada"
}

Test-Case "13. Pedido de ferias criado por RH aparece na leitura do proprio" {
    # **Ninguem pede ferias sem politica de aprovacao.** `POST /hr/leave` cria o
    # pedido e submete-o no mesmo acto; sem politica para `hr.leave_request`, a
    # submissao recusa com 409 -- "e configuracao em falta, nao um problema do
    # pedido", como o proprio servidor diz. Esta suite foi a primeira a exercitar
    # a rota, e foi assim que se descobriu que nenhuma outra a tocava.
    Clear-RivoApprovalPolicies -ProcessType "hr.leave_request" -Headers $adminHeaders
    Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" `
        -Headers $adminHeaders `
        -Body (@{ processType = "hr.leave_request"; steps = @(@{ approverPositionId = $cargoAprovador }) } | ConvertTo-Json -Depth 5) | Out-Null

    $inicio = $script:hoje.AddDays(30)
    $fim = $inicio.AddDays(4)
    Invoke-RestMethod "$base/hr/leave" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{
            employeeId = $script:ownEmployeeId
            type       = "Annual"
            startsOn   = $inicio.ToString("yyyy-MM-dd")
            endsOn     = $fim.ToString("yyyy-MM-dd")
            reason     = "Ferias do portal $stamp"
        } | ConvertTo-Json) | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $pedidos = Get-RivoLista "$base/portal/me/leave" -Headers $ownHeaders

    $meu = $pedidos | Where-Object { $_.startsOn -eq $inicio.ToString("yyyy-MM-dd") }
    if (-not $meu) { throw "o pedido nao aparece: $($pedidos.Count) pedido(s)" }
    if ($meu.calendarDays -ne 5) { throw "dias de calendario esperados 5, obtido $($meu.calendarDays)" }

    # O processo em `approval` chega ao portal: e o que permite ao ecra dizer
    # "pendente de decisao" em vez de inventar um estado seu.
    if (-not $meu.approvalRequestId) { throw "o pedido chegou sem approvalRequestId" }

    "1 pedido, 5 dias de calendario, estado '$($meu.status)', com processo de aprovacao"
}

Test-Case "14. Documento anexado ao colaborador aparece, com metadados e sem conteudo" {
    $ficheiro = Join-Path ([System.IO.Path]::GetTempPath()) "rivo-portal-$stamp.txt"
    Set-Content -Path $ficheiro -Value "Contrato de teste do portal - $stamp" -NoNewline -Encoding UTF8
    $curl = if (Get-Command curl.exe -ErrorAction SilentlyContinue) { "curl.exe" } else { "curl" }
    $hrToken = $hrHeaders.Authorization -replace "^Bearer "

    $documentId = (& $curl -s -X POST "$base/documents" -H "Authorization: Bearer $hrToken" `
        -F "file=@$ficheiro" -F "category=contrato" 2>$null | ConvertFrom-Json).documentId
    if (-not $documentId) { throw "o upload nao devolveu documentId" }

    Invoke-RestMethod "$base/hr/employees/$($script:ownEmployeeId)/documents" -Method Post -ContentType "application/json" `
        -Headers $hrHeaders -Body (@{ documentId = $documentId; category = "contrato" } | ConvertTo-Json) | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $docs = Get-RivoLista "$base/portal/me/documents" -Headers $ownHeaders

    $meu = $docs | Where-Object { $_.documentId -eq $documentId }
    if (-not $meu) { throw "o documento nao aparece na leitura do proprio" }
    if (-not $meu.fileName) { throw "sem fileName -- a juncao com documents falhou" }
    if ($meu.sizeInBytes -le 0) { throw "tamanho invalido: $($meu.sizeInBytes)" }

    # Metadados e nao conteudo: descarregar continua a ser de `documents`, com a
    # sua propria permissao.
    if ($meu.PSObject.Properties.Name -contains "content") { throw "a resposta traz conteudo do ficheiro" }
    "documentId=$documentId, com fileName e tamanho, sem conteudo"
}

# --- Recibos. A propriedade que interessa e o filtro por estado: uma folha em
# rascunho e um numero por confirmar, e nao se mostra ao proprio.
$script:runPortal = $null
$script:itemPortal = $null

Test-Case "15. Folha em rascunho com item do proprio -- os recibos vem vazios" {
    $r = Invoke-RestMethod "$base/payroll/runs" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ year = $script:hoje.Year; month = $script:hoje.Month } | ConvertTo-Json)
    $script:runPortal = $r.runId

    $script:itemPortal = (Invoke-RestMethod "$base/payroll/runs/$($script:runPortal)/items" -Method Post `
            -ContentType "application/json" -Headers $hrHeaders `
            -Body (@{ employeeId = $script:ownEmployeeId; grossSalary = 450000; foodAllowance = 30000 } | ConvertTo-Json)).itemId
    if (-not $script:itemPortal) { throw "o item nao foi criado" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $recibos = Get-RivoLista "$base/portal/me/payslips" -Headers $ownHeaders
    if ($recibos.Count -ne 0) { throw "uma folha em rascunho apareceu ao proprio: $($recibos.Count) recibo(s)" }
    "folha Draft com item de 450000 -- e nada no portal"
}

Test-Case "16. Depois de aprovada, o recibo aparece com ano, mes e valores" {
    # Fluxo real de governanca, com o aprovador e o cargo do preambulo: politica,
    # submissao, decisao em `approval`, e `payroll` a aplicar quando pergunta
    # (ADR-050 -- quem decide resolve-se do token, nunca do corpo do pedido).
    Clear-RivoApprovalPolicies -ProcessType "payroll.payroll_run" -Headers $adminHeaders
    Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ processType = "payroll.payroll_run"; steps = @(@{ approverPositionId = $cargoAprovador }) } | ConvertTo-Json -Depth 5) | Out-Null

    $processo = (Invoke-RestMethod "$base/payroll/runs/$($script:runPortal)/submission" -Method Post -Headers $hrHeaders).approvalRequestId
    Invoke-RestMethod "$base/approval/requests/$processo/decisions" -Method Post -ContentType "application/json" `
        -Headers $aprovadorConta.Headers -Body (@{ action = "Approved"; notes = "Folha conferida." } | ConvertTo-Json) | Out-Null
    $folha = Invoke-RestMethod "$base/payroll/runs/$($script:runPortal)/decision" -Method Post -Headers $hrHeaders
    if ($folha.status -ne "Approved") { throw "a folha ficou em '$($folha.status)'" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $recibos = Get-RivoLista "$base/portal/me/payslips" -Headers $ownHeaders
    $meu = $recibos | Where-Object { $_.itemId -eq $script:itemPortal }
    if (-not $meu) { throw "o recibo aprovado nao aparece: $($recibos.Count) recibo(s)" }
    if ($meu.year -ne $script:hoje.Year -or $meu.month -ne $script:hoje.Month) { throw "periodo errado: $($meu.year)-$($meu.month)" }
    if ($meu.grossSalary -ne 450000) { throw "bruto errado: $($meu.grossSalary)" }
    if ($null -ne $meu.documentId) { throw "documentId devia ser nulo antes de anexar o recibo" }
    "recibo de $($meu.year)-$($meu.month), bruto 450000, liquido '$($meu.netSalary)', sem documento"
}

Test-Case "17. Anexado o recibo, o documentId aparece no do proprio" {
    $ficheiro = Join-Path ([System.IO.Path]::GetTempPath()) "rivo-recibo-portal-$stamp.txt"
    Set-Content -Path $ficheiro -Value "Recibo de vencimento do portal - $stamp" -NoNewline -Encoding UTF8
    $curl = if (Get-Command curl.exe -ErrorAction SilentlyContinue) { "curl.exe" } else { "curl" }
    $hrToken = $hrHeaders.Authorization -replace "^Bearer "

    $documentId = (& $curl -s -X POST "$base/documents" -H "Authorization: Bearer $hrToken" `
        -F "file=@$ficheiro" -F "category=recibo" 2>$null | ConvertFrom-Json).documentId
    if (-not $documentId) { throw "o upload nao devolveu documentId" }

    Invoke-RestMethod "$base/payroll/runs/$($script:runPortal)/items/$($script:itemPortal)/documents" -Method Post `
        -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ documentId = $documentId; category = "recibo" } | ConvertTo-Json) | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $recibos = Get-RivoLista "$base/portal/me/payslips" -Headers $ownHeaders
    $meu = $recibos | Where-Object { $_.itemId -eq $script:itemPortal }
    if ($meu.documentId -ne $documentId) { throw "documentId '$($meu.documentId)' nao e o anexado '$documentId'" }
    "documentId=$documentId no recibo do proprio"
}

Test-Case "18. Cada colaborador ve so o seu -- o do colega nao aparece em nenhuma das quatro" {
    # O caso central do ADR-062. Segunda conta ligada a segundo colaborador, com
    # assiduidade propria: nenhuma das quatro leituras cruza os dois.
    $colega = New-RivoColaboradorComConta -Email "colega-$stamp@rivo.ao" -Nome "Colega Portal $stamp" `
        -AdminHeaders $adminHeaders -Perfil "Colaborador" -Password $pass

    Invoke-RestMethod "$base/hr/attendance/clock" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ employeeId = $colega.EmployeeId; day = $script:hojeIso } | ConvertTo-Json) | Out-Null

    $registosColega = Get-RivoLista "$base/portal/me/attendance" -Headers $colega.Headers
    if ($registosColega.Count -ne 1) { throw "o colega devia ver 1 registo seu, ve $($registosColega.Count)" }

    # As ferias, os documentos e os recibos do primeiro nao lhe chegam.
    if ((Get-RivoLista "$base/portal/me/leave" -Headers $colega.Headers).Count -ne 0) { throw "o colega ve ferias que nao sao dele" }
    if ((Get-RivoLista "$base/portal/me/documents" -Headers $colega.Headers).Count -ne 0) { throw "o colega ve documentos que nao sao dele" }
    if ((Get-RivoLista "$base/portal/me/payslips" -Headers $colega.Headers).Count -ne 0) { throw "o colega ve recibos que nao sao dele" }

    # E o perfil Colaborador, sem permissao nenhuma, chega para o portal todo --
    # e a prova de que autoriza por vinculo (ADR-061).
    $perfilColega = Invoke-RestMethod "$base/portal/me" -Headers $colega.Headers
    if ($perfilColega.employeeId -ne $colega.EmployeeId) { throw "o perfil do colega nao e o dele" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    if ((Get-RivoLista "$base/portal/me/payslips" -Headers $ownHeaders).Count -lt 1) { throw "o primeiro deixou de ver o seu recibo" }

    "perfil Colaborador sem permissoes le as quatro, e so as suas"
}

Test-Case "19. Vista sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($perfil.employeeId -ne $script:ownEmployeeId) { throw "vinculo nao sobreviveu ao reinicio" }
    "employeeId=$($script:ownEmployeeId) intacto apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
