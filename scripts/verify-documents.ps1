# Verificação do módulo `documents` e da ligação a `hr` (ADR-009).
#
#   docker compose up -d --build
#   pwsh -File scripts/verify-documents.ps1

$ErrorActionPreference = "Stop"
$base = "http://localhost:5080"
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

function Invoke-Sql { param([string]$q) return (docker exec rivo-postgres psql -U rivo -d rivo -t -A -c $q).Trim() }

$dotenv = @{}
Get-Content ".env" | Where-Object { $_ -match "=" -and $_ -notmatch "^\s*#" } | ForEach-Object {
    $p = $_ -split "=", 2; $dotenv[$p[0].Trim()] = $p[1].Trim()
}

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$adminToken = Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]
$adminHeaders = @{ Authorization = "Bearer $adminToken" }

# Ficheiro de teste, com conteudo conhecido para verificar o hash.
$tempFile = Join-Path $env:TEMP "rivo-doc-$stamp.txt"
$content = "Contrato de trabalho de teste - $stamp"
Set-Content -Path $tempFile -Value $content -NoNewline -Encoding UTF8
$expectedHash = (Get-FileHash $tempFile -Algorithm SHA256).Hash.ToLower()

Write-Host "`n=== Modulo documents ===`n"

Test-Case "1. Schema documents com migration propria" {
    $m = Invoke-Sql "select count(*) from documents.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='documents'"
    "$m migration(s), $t tabelas"
}

Test-Case "2. documents nao referencia registos de negocio (ADR-009)" {
    # Nenhuma FK a sair de documents para outro schema: a ligacao vive no
    # contexto de origem, nao aqui.
    $out = Invoke-Sql @"
select count(*) from information_schema.table_constraints tc
join information_schema.constraint_column_usage ccu on ccu.constraint_name = tc.constraint_name
where tc.constraint_type='FOREIGN KEY' and tc.table_schema='documents' and ccu.table_schema<>'documents'
"@
    if ($out -ne "0") { throw "documents tem $out FK para fora" }
    "sem chaves estrangeiras para fora"
}

Test-Case "3. FK entre schemas de hr para documents existe" {
    $fk = Invoke-Sql @"
select count(*) from information_schema.table_constraints tc
join information_schema.constraint_column_usage ccu on ccu.constraint_name = tc.constraint_name
where tc.constraint_type='FOREIGN KEY' and tc.table_schema='hr' and ccu.table_schema='documents'
"@
    if ($fk -ne "1") { throw "esperada 1 FK, obtidas $fk" }
    "hr.employee_document -> documents.document"
}

# Upload multipart via curl.exe: o Windows PowerShell 5.1 nao tem -Form, que
# so existe no PowerShell 7.
function Invoke-Upload {
    param([string]$FilePath, [string]$Category, [string]$Token)

    $body = curl.exe -s -X POST "$base/documents" `
        -H "Authorization: Bearer $Token" `
        -F "file=@$FilePath" `
        -F "category=$Category" 2>$null
    return $body
}

function Get-CurlStatus {
    param([string]$Url, [string]$Token, [string]$FilePath, [string]$Category)

    if ($FilePath) {
        return (curl.exe -s -o NUL -w "%{http_code}" -X POST $Url -H "Authorization: Bearer $Token" -F "file=@$FilePath" -F "category=$Category" 2>$null)
    }
    if ($Token) {
        return (curl.exe -s -o NUL -w "%{http_code}" $Url -H "Authorization: Bearer $Token" 2>$null)
    }
    return (curl.exe -s -o NUL -w "%{http_code}" $Url 2>$null)
}

$script:documentId = $null
Test-Case "4. Upload com permissao devolve metadados e hash correcto" {
    $r = Invoke-Upload $tempFile "contrato" $adminToken | ConvertFrom-Json
    $script:documentId = $r.documentId
    if (-not $script:documentId) { throw "sem id na resposta" }
    if ($r.contentHash -ne $expectedHash) { throw "hash '$($r.contentHash)' difere do esperado '$expectedHash'" }
    # Comparar com o tamanho do ficheiro, nao com o da string: o
    # Set-Content -Encoding UTF8 do PowerShell 5.1 escreve BOM.
    $expectedSize = (Get-Item $tempFile).Length
    if ($r.sizeInBytes -ne $expectedSize) { throw "tamanho $($r.sizeInBytes), esperado $expectedSize" }
    "hash SHA-256 confere, $expectedSize bytes"
}

Test-Case "5. Download devolve o conteudo original" {
    $out = Join-Path $env:TEMP "rivo-down-$stamp.txt"
    curl.exe -s -o $out "$base/documents/$($script:documentId)" -H "Authorization: Bearer $adminToken" 2>$null
    $downloaded = Get-Content $out -Raw -Encoding UTF8
    if ($downloaded -ne $content) { throw "conteudo difere: '$downloaded'" }
    Remove-Item $out -Force
    "conteudo identico ao carregado"
}

Test-Case "6. Sem autenticacao -> 401; sem permissao -> 403" {
    $code = Get-CurlStatus "$base/documents/$($script:documentId)"
    if ($code -ne "401") { throw "esperado 401, obtido $code" }

    $e = "semdoc-$stamp@rivo.ao"
    $b = @{ email = $e; password = $pass } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json" | Out-Null
    $t = Get-Token $e $pass
    $code = Get-CurlStatus "$base/documents/$($script:documentId)" $t
    if ($code -ne "403") { throw "esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "7. Ficheiro vazio e recusado" {
    $empty = Join-Path $env:TEMP "rivo-empty-$stamp.txt"
    New-Item -ItemType File -Path $empty -Force | Out-Null
    $code = Get-CurlStatus "$base/documents" $adminToken $empty "contrato"
    Remove-Item $empty -Force
    if ($code -ne "400") { throw "esperado 400, obtido $code" }
    "HTTP 400"
}

# --- Ligacao a hr, que e o ponto do ADR-009 ---

$script:employeeId = $null
Test-Case "8. Anexar documento a colaborador (ADR-009)" {
    $b = @{ name = "Dept-$stamp" } | ConvertTo-Json
    $deptId = (Invoke-RestMethod "$base/hr/departments" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).departmentId
    $b = @{ fullName = "Colaborador Doc"; departmentId = $deptId } | ConvertTo-Json
    $script:employeeId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).employeeId

    $b = @{ documentId = $script:documentId; category = "contrato" } | ConvertTo-Json
    Invoke-RestMethod "$base/hr/employees/$($script:employeeId)/documents" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $count = Invoke-Sql "select count(*) from hr.employee_document where employee_id='$($script:employeeId)'"
    if ($count -ne "1") { throw "ligacao nao gravada" }
    "ligacao em hr.employee_document"
}

Test-Case "9. Listagem junta metadados de documents" {
    $docs = Invoke-RestMethod "$base/hr/employees/$($script:employeeId)/documents" -Headers $adminHeaders
    if ($docs.Count -ne 1) { throw "esperado 1 documento, obtidos $($docs.Count)" }
    # Os metadados vem de `documents`, pelo contrato — nao por JOIN entre schemas.
    if (-not $docs[0].fileName) { throw "metadados de documents em falta" }
    if ($docs[0].category -ne "contrato") { throw "categoria de hr em falta" }
    "categoria de hr + fileName de documents"
}

Test-Case "10. Documento inexistente e recusado ao anexar" {
    $b = @{ documentId = [Guid]::NewGuid().ToString(); category = "contrato" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/employees/$($script:employeeId)/documents" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "HTTP 404"
}

Test-Case "11. FK impede eliminar documento ligado" {
    # A restricao tem de existir na base de dados, e nao so na aplicacao:
    # e isso que a chave polimorfica nao dava.
    #
    # O psql escreve o erro para stderr, e com ErrorActionPreference=Stop isso
    # tornar-se-ia excepcao terminante antes da verificacao. Baixa-se o nivel
    # so para esta chamada.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $err = (docker exec rivo-postgres psql -U rivo -d rivo -c "delete from documents.document where id='$($script:documentId)';" 2>&1) | Out-String
    $ErrorActionPreference = $previous

    if ($err -notmatch "violates foreign key") { throw "eliminacao nao foi bloqueada: $err" }

    # Confirma que o documento continua la.
    $still = Invoke-Sql "select count(*) from documents.document where id='$($script:documentId)'"
    if ($still -ne "1") { throw "documento foi eliminado" }
    "eliminacao bloqueada pela FK entre schemas"
}

Test-Case "12. Upload auditado" {
    $c = Invoke-Sql "select count(*) from audit.audit_event where action='documents.document.uploaded' and entity_id='$($script:documentId)'"
    if ($c -ne "1") { throw "upload nao auditado" }
    $a = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.document_attached' and entity_id='$($script:employeeId)'"
    if ($a -ne "1") { throw "anexacao nao auditada" }
    "upload e anexacao na trilha"
}

Test-Case "13. Ficheiro sobrevive ao reinicio da stack" {
    docker compose restart | Out-Null
    $deadline = (Get-Date).AddSeconds(180)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $t = Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]
    $out = Join-Path $env:TEMP "rivo-after-$stamp.txt"
    curl.exe -s -o $out "$base/documents/$($script:documentId)" -H "Authorization: Bearer $t" 2>$null
    $after = Get-Content $out -Raw -Encoding UTF8
    Remove-Item $out -Force
    if ($after -ne $content) { throw "conteudo perdido ou alterado" }
    "conteudo intacto apos restart"
}

Remove-Item $tempFile -Force -ErrorAction SilentlyContinue

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
