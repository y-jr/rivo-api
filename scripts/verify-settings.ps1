# Verificação da camada de composição `Rivo.Settings` (Configurações &
# Administração, ADR-041).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-settings.ps1

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
$adminEmail = $dotenv["BOOTSTRAP_ADMIN_EMAIL"]
$adminPass = $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

Write-Host "`n=== Camada de composicao settings ===`n"

Test-Case "1. Vista mostra os oito Perfis de Acesso, cada um com as suas permissoes" {
    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    if ($overview.accessProfiles.Count -ne 8) { throw "esperados 8 perfis, obtidos $($overview.accessProfiles.Count)" }
    $admin = $overview.accessProfiles | Where-Object { $_.name -eq "Admin" }
    if (-not $admin) { throw "perfil Admin nao apareceu" }
    if ($admin.permissions -notcontains "identity.roles.read") { throw "Admin sem identity.roles.read" }
    "8 perfis; Admin com $($admin.permissions.Count) permissoes"
}

Test-Case "2. Perfis vem ordenados por nome" {
    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    $nomes = $overview.accessProfiles | ForEach-Object { $_.name }
    $ordenado = $nomes | Sort-Object
    if (($nomes -join ",") -ne ($ordenado -join ",")) { throw "nao vem ordenado: $($nomes -join ',')" }
    "ordem alfabetica confirmada"
}

$processType = "settings.verify_probe_$stamp"

Test-Case "3. Regra de aprovacao nova aparece agrupada pelo seu modulo" {
    $body = @{
        processType = $processType
        requiresBudgetCheck = $false
        steps = @(@{ approverPositionId = [Guid]::NewGuid().ToString() })
    } | ConvertTo-Json -Depth 5
    $script:policyId = (Invoke-RestMethod "$base/approval/policies" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).policyId
    if (-not $script:policyId) { throw "politica nao criada" }

    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    $grupo = $overview.approvalRulesByModule | Where-Object { $_.module -eq "settings" }
    if (-not $grupo) { throw "grupo 'settings' nao apareceu na vista" }
    $regra = $grupo.rules | Where-Object { $_.policyId -eq $script:policyId }
    if (-not $regra) { throw "politica nova nao apareceu no grupo" }
    if (-not $regra.isActive) { throw "politica nova devia nascer activa" }
    if ($regra.stepCount -ne 1) { throw "esperado 1 passo, obtido $($regra.stepCount)" }
    "grupo 'settings', policyId=$($script:policyId), 1 passo, activa"
}

Test-Case "4. Desactivar a politica nao a esconde da vista - mostra o estado, nao filtra" {
    # Clear-RivoApprovalPolicies (_ambiente.ps1) repete ate confirmar por SQL
    # (K20, known-issues.md) - a mesma robustez que fecha esta suite sem
    # deixar nada activo para tras.
    Clear-RivoApprovalPolicies -ProcessType $processType -Headers $adminHeaders

    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    $grupo = $overview.approvalRulesByModule | Where-Object { $_.module -eq "settings" }
    $regra = $grupo.rules | Where-Object { $_.policyId -eq $script:policyId }
    if (-not $regra) { throw "politica desactivada desapareceu da vista" }
    if ($regra.isActive) { throw "devia mostrar isActive=false apos desactivar" }
    "continua na vista, isActive=false"
}

Test-Case "5. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/settings/overview" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "6. Autenticado sem as duas permissoes -> 403" {
    $email = "settings-verify-$stamp@rivo.ao"
    $pass = "Rivo!Settings2026"
    $body = @{ email = $email; password = $pass } | ConvertTo-Json
    $userId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    Invoke-RestMethod "$base/identity/users/$userId/roles" -Method Post -Body (@{ profile = "HR" } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $token = Get-Token $email $pass
    $code = Get-StatusCode { Invoke-RestMethod "$base/settings/overview" -Headers @{ Authorization = "Bearer $token" } }
    if ($code -ne 403) { throw "esperado 403 (HR nao tem approval.policies.read), obtido $code" }
    "HTTP 403 -- HR nao tem as duas permissoes que a vista soma"
}

function New-CsvFile {
    param([string]$Content)
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("rivo-verify-settings-" + [Guid]::NewGuid().ToString("N") + ".csv")
    Set-Content -Path $path -Value $Content -NoNewline -Encoding utf8
    return $path
}

$importPass = "Rivo!Import2026"

function New-ImportPerfilHeaders {
    param([string]$Perfil, [string]$Sufixo)
    $email = "$Sufixo-$stamp@rivo.ao"
    $body = @{ email = $email; password = $importPass } | ConvertTo-Json
    $id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    Invoke-RestMethod "$base/identity/users/$id/roles" -Method Post -Body (@{ profile = $Perfil } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null
    return @{ Authorization = "Bearer " + (Get-Token $email $importPass) }
}

$hrHeaders = New-ImportPerfilHeaders "HR" "settings-import-hr"
$salesHeaders = New-ImportPerfilHeaders "Sales" "settings-import-sales"

$script:deptId = (Invoke-RestMethod "$base/hr/departments" -Method Post -Body (@{ name = "Comercial-$stamp" } | ConvertTo-Json) -ContentType "application/json" -Headers $hrHeaders).departmentId

Test-Case "7. Importar clientes via CSV -- um novo, um duplicado, um rejeitado" {
    $csv = New-CsvFile "Nome,NIF,Morada,Cidade,Pais,Email,Telefone`nCliente CSV $stamp,IC$stamp,Rua A,Luanda,AO,,`nCliente CSV $stamp,IC$stamp,Rua A,Luanda,AO,,`n,IC2$stamp,Rua A,Luanda,AO,,"
    $resposta = Invoke-RestMethod "$base/settings/import/customers" -Method Post -Form @{ file = Get-Item $csv } -Headers $salesHeaders
    Remove-Item $csv
    if ($resposta.totalRows -ne 3) { throw "esperadas 3 linhas, vieram $($resposta.totalRows)" }
    if ($resposta.imported -ne 1) { throw "esperado 1 importado, veio $($resposta.imported)" }
    if ($resposta.duplicates -ne 1) { throw "esperado 1 duplicado, veio $($resposta.duplicates)" }
    if ($resposta.rejected -ne 1) { throw "esperado 1 rejeitado (sem nome), veio $($resposta.rejected)" }
    "3 linhas: 1 importado, 1 duplicado (mesmo NIF), 1 rejeitado (sem nome)"
}

Test-Case "8. Importar colaboradores via CSV -- departamento existente e inexistente" {
    $csv = New-CsvFile "Nome,Departamento,DataAdmissao`nColaborador CSV $stamp,Comercial-$stamp,2026-01-15`nOutro CSV $stamp,Departamento-Que-Nao-Existe,2026-01-15"
    $resposta = Invoke-RestMethod "$base/settings/import/employees" -Method Post -Form @{ file = Get-Item $csv } -Headers $hrHeaders
    Remove-Item $csv
    if ($resposta.imported -ne 1) { throw "esperado 1 importado, veio $($resposta.imported)" }
    if ($resposta.rejected -ne 1) { throw "esperado 1 rejeitado (departamento inexistente), veio $($resposta.rejected)" }
    "1 importado (departamento resolvido por nome), 1 rejeitado (departamento inexistente)"
}

Test-Case "9. Importar fornecedores via CSV -- so Admin tem a permissao" {
    $csv = New-CsvFile "Nome,NIF`nFornecedor CSV $stamp,IF$stamp"
    $codigo403 = Get-StatusCode { Invoke-RestMethod "$base/settings/import/suppliers" -Method Post -Form @{ file = Get-Item $csv } -Headers $salesHeaders }
    if ($codigo403 -ne 403) { throw "Sales nao devia poder importar fornecedores, obtido $codigo403" }

    $resposta = Invoke-RestMethod "$base/settings/import/suppliers" -Method Post -Form @{ file = Get-Item $csv } -Headers $adminHeaders
    Remove-Item $csv
    if ($resposta.imported -ne 1) { throw "esperado 1 importado, veio $($resposta.imported)" }
    "Sales -> 403; Admin importa 1 fornecedor"
}

Test-Case "10. Cabecalho sem coluna obrigatoria -> 400" {
    $csv = New-CsvFile "Nome`nSo nome $stamp"
    $codigo = Get-StatusCode { Invoke-RestMethod "$base/settings/import/customers" -Method Post -Form @{ file = Get-Item $csv } -Headers $salesHeaders }
    Remove-Item $csv
    if ($codigo -ne 400) { throw "esperado 400, obtido $codigo" }
    "HTTP 400 -- falta NIF/Morada/Cidade/Pais no cabecalho"
}

Test-Case "11. Sem autenticacao -> 401 na importacao" {
    $csv = New-CsvFile "Nome,NIF`nX,Y"
    $codigo = Get-StatusCode { Invoke-RestMethod "$base/settings/import/suppliers" -Method Post -Form @{ file = Get-Item $csv } }
    Remove-Item $csv
    if ($codigo -ne 401) { throw "esperado 401, obtido $codigo" }
    "HTTP 401"
}

Test-Case "12. Vista sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $adminHeaders2 = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders2
    if ($overview.accessProfiles.Count -ne 8) { throw "perfis nao sobreviveram: $($overview.accessProfiles.Count)" }
    $grupo = $overview.approvalRulesByModule | Where-Object { $_.module -eq "settings" }
    $regra = $grupo.rules | Where-Object { $_.policyId -eq $script:policyId }
    if (-not $regra -or $regra.isActive) { throw "estado da politica nao sobreviveu ao reinicio" }
    "8 perfis, politica desactivada intacta apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
