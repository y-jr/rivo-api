# Verificação de Procurement — Fornecedor e Requisição Interna.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-procurement.ps1
#
# Duas coisas que os testes de domínio não conseguem provar, e que são a razão
# de esta suite existir:
#
#   - **O agregado sobrevive à base de dados.** A requisição tem linhas, e o
#     mapeamento de uma colecção com campo de apoio é exactamente onde o EF Core
#     falha em silêncio: grava e relê sem as linhas, sem erro nenhum.
#   - **A fronteira com `approval` fecha o círculo.** O domínio testa que
#     `MarkApproved` recusa fora de pendente; só aqui se vê a requisição
#     submetida, decidida do outro lado, e o efeito aplicado deste.
#
# Monta o cenário pelas rotas reais de `hr` e `approval`. Sem atalho por SQL —
# excepto para desactivar políticas, que não tem rota.
#
# Re-executável: cada corrida cria os seus colaboradores, cargo, departamento,
# fornecedores e requisições, e desactiva no fim a política que criou.

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
$curto = "$stamp".Substring("$stamp".Length - 6)

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

function New-PerfilHeaders {
    param([string]$Perfil, [string]$Sufixo)
    $email = "$Sufixo@rivo.ao"
    $body = @{ email = $email; password = $script:pass } | ConvertTo-Json
    $id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    $body = @{ profile = $Perfil } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$id/roles" -Method Post -Body $body -ContentType "application/json" -Headers $script:adminHeaders | Out-Null
    return @{ Authorization = "Bearer " + (Get-Token $email $script:pass) }
}

# `Manager` requisita; `Finance` vê fornecedores para registar a factura de
# compra; `Sales` não tem nada em `procurement` e serve para o 403.
$managerHeaders = New-PerfilHeaders "Manager" "requisitante-pr-$stamp"
$financeHeaders = New-PerfilHeaders "Finance" "tesouraria-pr-$stamp"
$salesHeaders = New-PerfilHeaders "Sales" "vendas-pr-$stamp"

# `AssetManager` recebe mercadoria e nao encomenda — a outra metade da
# segregacao que da valor ao 3-way match.
$assetHeaders = New-PerfilHeaders "AssetManager" "armazem-pr-$stamp"

# IBAN de Angola calculado pela própria norma ISO 13616: `AO` vale 1024, os
# quatro primeiros caracteres passam para o fim, e o resultado dá resto 1
# módulo 97. O segundo é o mesmo com o último dígito trocado — o erro de quem
# copia à mão, que é o caso que o mod-97 existe para apanhar.
$ibanBom = "AO71000600000109131234151"
$ibanMau = "AO71000600000109131234152"

# --- Cenário, montado pelas rotas reais.
$departamento = (Invoke-RestMethod "$base/hr/departments" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ name = "Contabilidade PR $curto" } | ConvertTo-Json)).departmentId

$requisitante = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Requisitante PR $curto"; departmentId = $departamento } | ConvertTo-Json)).employeeId

$aprovador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Aprovador PR $curto" } | ConvertTo-Json)).employeeId

# Quem recebe a mercadoria, e nao e quem a pede: sem duas pessoas, a
# segregacao do 3-way match nao se pode verificar.
$recebedor = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Fiel de Armazem PR $curto" } | ConvertTo-Json)).employeeId

# Cargo sem autoridade de aprovação: o que a confere passaria ele próprio por
# governança (BR-20), e não é isso que se verifica aqui.
$cargo = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ name = "Director de Compras $curto"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ positionId = $cargo } | ConvertTo-Json) | Out-Null

# **Estado determinista antes de começar.** O caso 15 verifica que submeter sem
# política recusa, e isso só é verificável se não houver nenhuma. Uma corrida
# anterior desta suite deixa a sua desactivada, mas uma interrompida a meio não
# — e sem isto o caso passava ou falhava conforme o que ficou para trás.
#
# **Pela rota, e já não por SQL** (2026-08-27). Enquanto não havia
# `POST /approval/policies/{id}/deactivation`, a suite era obrigada a escrever
# na base de dados por baixo da aplicação — e uma suite que se limpa por um
# caminho que a aplicação não tem verifica menos do que parece.
#
# Clear-RivoApprovalPolicies (_ambiente.ps1, 2026-08-29) repete ate confirmar
# por SQL: uma unica tentativa tolerava o K20 (known-issues.md) sem rebentar a
# suite, mas deixava a politica activa para tras -- e uma corrida futura, ou
# outra suite que use o mesmo tipo de processo, colidia com ela por
# ambiguidade (visto em `verify-approval.ps1`, primeira corrida em CI).
Clear-RivoApprovalPolicies -ProcessType "procurement.purchase_requisition" -Headers $adminHeaders

Write-Host "`n=== Procurement: Fornecedor e Requisicao Interna ===`n"

# --- Fornecedor

Test-Case "1. Quem paga nao qualifica o fornecedor (BR-3 um passo antes)" {
    # Quem fixa o IBAN decide para onde o dinheiro sai. Se fosse a mesma pessoa
    # que executa o pagamento, a segregacao de BR-3 ficava vazia — bastava
    # apontar o fornecedor a uma conta propria e mandar pagar.
    $fVe = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='procurement.suppliers.read'"
    $fEscreve = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='procurement.suppliers.write'"
    if ($fVe -ne "1") { throw "Finance nao ve fornecedores, e precisa deles para a factura de compra" }
    if ($fEscreve -ne "0") { throw "Finance qualifica fornecedores e executa pagamentos - a segregacao caiu" }

    # E quem requisita tambem nao: quem pede a compra nao escolhe a conta.
    $mEscreve = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='procurement.suppliers.write'"
    $mRequisita = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='procurement.requisitions.write'"
    if ($mEscreve -ne "0") { throw "Manager qualifica fornecedores" }
    if ($mRequisita -ne "1") { throw "Manager nao requisita" }

    "Finance ve e nao qualifica; Manager requisita e nao qualifica"
}

Test-Case "2. Qualificar fornecedor, com o IBAN normalizado na gravacao" {
    $body = @{
        name  = "Angoferragens $curto"; taxId = "5417 $curto"
        iban  = "AO71 0006 0000 0109 1312 3415 1"
        email = "geral-$curto@angoferragens.ao"
    } | ConvertTo-Json

    $f = Invoke-RestMethod "$base/procurement/suppliers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:fornecedorId = $f.supplierId

    # O NIF e o IBAN chegam agrupados, como vem da factura e do extracto. Sem
    # normalizar, procurar por qualquer um deles nao encontraria nada.
    $nif = Invoke-Sql "select tax_id from procurement.supplier where id='$($script:fornecedorId)'"
    $iban = Invoke-Sql "select iban from procurement.supplier where id='$($script:fornecedorId)'"
    if ($nif -ne "5417$curto") { throw "NIF gravado '$nif', esperado 5417$curto" }
    if ($iban -ne $ibanBom) { throw "IBAN gravado '$iban', esperado $ibanBom" }

    "NIF e IBAN sem espacos; $ibanBom"
}

Test-Case "3. NIF repetido devolve o identificador do que ja existe" {
    $body = @{ name = "Outro nome qualquer"; taxId = "5417$curto" } | ConvertTo-Json
    try {
        Invoke-RestMethod "$base/procurement/suppliers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
        throw "o duplicado passou"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw "esperado 409, obtido $([int]$_.Exception.Response.StatusCode)" }

        # O identificador vem no corpo de proposito: quem tentou registar quase
        # de certeza quer trabalhar com o fornecedor que ja existe, e sem ele
        # teria de o procurar as cegas.
        $corpo = $_.ErrorDetails.Message | ConvertFrom-Json
        if ($corpo.supplierId -ne $script:fornecedorId) { throw "409 sem apontar ao existente" }
    }

    $ux = Invoke-Sql "select count(*) from sys.indexes where object_id=object_id('procurement.supplier') and is_unique=1 and name like '%tax_id%'"
    if ($ux -ne "1") { throw "indice unico do NIF em falta na base de dados" }

    "409 com o supplierId existente, e indice unico como segunda linha"
}

Test-Case "4. IBAN com um digito trocado e recusado, e nada e gravado" {
    $body = @{ name = "Fornecedor IBAN mau $curto"; taxId = "5999$curto"; iban = $ibanMau } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/suppliers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }

    # **A recusa tem de ser total.** Guardar o fornecedor sem IBAN deixaria um
    # registo a meio, e o proximo a olhar acharia que faltava preencher em vez
    # de saber que o valor estava errado.
    $existe = Invoke-Sql "select count(*) from procurement.supplier where tax_id='5999$curto'"
    if ($existe -ne "0") { throw "fornecedor gravado apesar do IBAN recusado" }

    "400 pelo mod-97 da ISO 13616; o fornecedor nao chegou a existir"
}

Test-Case "5. IBAN de outro pais e aceite - a norma nao e de Angola" {
    # O comprimento por pais nao e verificado de proposito: o registo nacional
    # muda quando um pais entra ou altera o esquema, e nao esta em fonte
    # primaria aqui. O do Reino Unido tem 22 caracteres contra os 25 de ca.
    $body = @{ name = "Fornecedor estrangeiro $curto"; taxId = "5888$curto"; iban = "GB82WEST12345698765432" } | ConvertTo-Json
    $f = Invoke-RestMethod "$base/procurement/suppliers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:estrangeiroId = $f.supplierId

    $iban = Invoke-Sql "select iban from procurement.supplier where id='$($script:estrangeiroId)'"
    if ($iban -ne "GB82WEST12345698765432") { throw "IBAN estrangeiro perdido: '$iban'" }

    "GB82WEST12345698765432 aceite, com 22 caracteres"
}

Test-Case "6. Actualizar sem tocar no IBAN nao o apaga" {
    # Um corpo parcial que omite o campo nao pode significar "tira-lhe a conta".
    # Se significasse, cada correccao de nome apagaria o IBAN em silencio, e so
    # se descobriria no dia em que houvesse um pagamento para fazer.
    $body = @{ name = "Angoferragens SU $curto" } | ConvertTo-Json
    Invoke-RestMethod "$base/procurement/suppliers/$($script:fornecedorId)/details" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $iban = Invoke-Sql "select iban from procurement.supplier where id='$($script:fornecedorId)'"
    if ($iban -ne $ibanBom) { throw "IBAN perdido numa alteracao de nome: '$iban'" }

    $nome = Invoke-Sql "select name from procurement.supplier where id='$($script:fornecedorId)'"
    if ($nome -ne "Angoferragens SU $curto") { throw "nome nao mudou: '$nome'" }

    "nome alterado, IBAN intacto"
}

Test-Case "7. IBAN errado numa alteracao nao substitui o que la esta" {
    $body = @{ iban = $ibanMau } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/suppliers/$($script:fornecedorId)/details" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }

    # Trocar um IBAN bom por nenhum, por causa de uma tentativa falhada, seria
    # pior do que a tentativa.
    $iban = Invoke-Sql "select iban from procurement.supplier where id='$($script:fornecedorId)'"
    if ($iban -ne $ibanBom) { throw "o IBAN anterior perdeu-se: '$iban'" }

    "400, e o IBAN anterior continua la"
}

Test-Case "8. Desactivar tira da listagem e nao elimina (BR-14)" {
    $body = @{ active = $false } | ConvertTo-Json
    Invoke-RestMethod "$base/procurement/suppliers/$($script:estrangeiroId)/status" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $activos = Invoke-RestMethod "$base/procurement/suppliers" -Headers $adminHeaders
    if ($activos.supplierId -contains $script:estrangeiroId) { throw "desactivado ainda aparece na listagem por omissao" }

    $todos = Invoke-RestMethod "$base/procurement/suppliers?includeInactive=true" -Headers $adminHeaders
    if ($todos.supplierId -notcontains $script:estrangeiroId) { throw "desactivado desapareceu de includeInactive" }

    # Um fornecedor referenciado por facturas e pagamentos e parte desses
    # registos. Elimina-lo deixaria historico sem contraparte.
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/suppliers/$($script:estrangeiroId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }

    $existe = Invoke-Sql "select count(*) from procurement.supplier where id='$($script:estrangeiroId)'"
    if ($existe -ne "1") { throw "fornecedor desapareceu da base de dados" }

    "fora da listagem, DELETE recusado ($code), linha intacta"
}

Test-Case "9. O IBAN entra na trilha, antes e depois" {
    # Alterar o IBAN e o passo silencioso de uma fraude de pagamento. Sem o
    # valor anterior na trilha, uma alteracao maliciosa nao se distingue de uma
    # correccao legitima.
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.supplier.registered' and entity_id='$($script:fornecedorId)'"
    if ($n -ne "1") { throw "registo do fornecedor nao esta na trilha" }

    $comIban = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.supplier.registered' and entity_id='$($script:fornecedorId)' and new_value like '%$ibanBom%'"
    if ($comIban -ne "1") { throw "a trilha do registo nao guarda o IBAN" }

    $comAnterior = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.supplier.updated' and entity_id='$($script:fornecedorId)' and previous_value like '%$ibanBom%'"
    if ($comAnterior -lt "1") { throw "a alteracao nao guarda o IBAN anterior" }

    $semActor = Invoke-Sql "select count(*) from audit.audit_event where entity_type='procurement.supplier' and entity_id='$($script:fornecedorId)' and actor_id is null"
    if ($semActor -ne "0") { throw "ha registos sem actor" }

    "registo e alteracao na trilha, com o IBAN de antes e de depois"
}

# --- Requisição Interna

Test-Case "10. Requisitante inexistente e recusado" {
    # O colaborador e lido pelo contrato de `hr` (ADR-010). Sem esta
    # verificacao, uma requisicao nasceria com um identificador que nao e de
    # ninguem, e so `approval` o descobriria ao tentar verificar BR-2.
    $body = @{
        requestedByEmployeeId = [guid]::NewGuid().ToString()
        justification         = "Requisitante que nao existe."
        lines                 = @(@{ description = "Portatil"; quantity = 1; estimatedUnitPrice = 100 })
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "404 - o requisitante e lido do contrato de hr, nao aceite por palavra"
}

Test-Case "11. Abrir requisicao: rascunho, e o total e a soma das linhas" {
    $body = @{
        requestedByEmployeeId = $requisitante
        justification         = "Substituir os dois portateis avariados da contabilidade."
        lines                 = @(
            @{ description = "Portatil 14 pol, 16 GB"; quantity = 2; estimatedUnitPrice = 850000 },
            @{ description = "Rato sem fios"; quantity = 2; estimatedUnitPrice = 12500 }
        )
    } | ConvertTo-Json -Depth 5

    $r = Invoke-RestMethod "$base/procurement/requisitions" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:requisicaoId = $r.requisitionId

    if ($r.estado -ne "Draft") { throw "estado '$($r.estado)', esperado Draft" }
    if ([decimal]$r.estimatedTotal -ne 1725000) { throw "total $($r.estimatedTotal), esperado 1725000" }

    # Nasce em rascunho e nao submetida: submeter e acto separado, e e o que
    # congela o que se pede.
    $processo = Invoke-Sql "select count(*) from procurement.purchase_requisition where id='$($script:requisicaoId)' and approval_request_id is not null"
    if ($processo -ne "0") { throw "um rascunho ja tem processo de aprovacao" }

    "2 x 850000 + 2 x 12500 = 1725000, em Draft"
}

Test-Case "12. As linhas sobrevivem a base de dados" {
    # **E o caso que justifica esta suite.** A coleccao de linhas e mapeada por
    # campo de apoio, e um mapeamento errado grava e rele sem elas — sem erro
    # nenhum, e sem que teste de dominio algum o veja.
    $r = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)" -Headers $managerHeaders
    if ($r.lines.Count -ne 2) { throw "relidas $($r.lines.Count) linhas, esperadas 2" }

    $portatil = $r.lines | Where-Object { $_.description -like "Portatil*" }
    if (-not $portatil) { throw "a linha do portatil desapareceu" }
    if ([decimal]$portatil.estimatedTotal -ne 1700000) { throw "total da linha $($portatil.estimatedTotal)" }

    $naBase = Invoke-Sql "select count(*) from procurement.requisition_line where requisition_id='$($script:requisicaoId)'"
    if ($naBase -ne "2") { throw "$naBase linhas na base de dados, esperadas 2" }

    "2 linhas relidas da base, com o total por linha certo"
}

Test-Case "13. Sem departamento indicado, herda o do requisitante" {
    # E o departamento que escolhe a politica de aprovacao aplicavel. Deixa-lo
    # nulo quando o requisitante tem um faria a requisicao cair na politica
    # generica em vez da do seu departamento.
    $dep = Invoke-Sql "select cast(department_id as varchar(36)) from procurement.purchase_requisition where id='$($script:requisicaoId)'"
    if ($dep -ne $departamento) { throw "departamento '$dep', esperado $departamento" }
    "herdado de hr, e nao inventado"
}

Test-Case "14. Linha sem quantidade positiva e recusada" {
    $body = @{
        requestedByEmployeeId = $requisitante
        justification         = "Quantidade invalida."
        lines                 = @(@{ description = "Portatil"; quantity = 0; estimatedUnitPrice = 100 })
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "400 - pedir zero de alguma coisa nao e um pedido"
}

Test-Case "15. Sem politica configurada, submeter recusa e diz porque" {
    # 409 e nao 500: a capacidade existe e funcionou. E configuracao em falta, e
    # quem le o erro tem de saber que o corrige em `approval` e nao no pedido.
    try {
        Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/submission" -Method Post -Headers $managerHeaders | Out-Null
        throw "submeteu sem politica configurada"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw "esperado 409, obtido $([int]$_.Exception.Response.StatusCode)" }
        $corpo = $_.ErrorDetails.Message | ConvertFrom-Json
        if ($corpo.erro -notmatch "procurement.purchase_requisition") { throw "o erro nao nomeia o tipo de processo: $($corpo.erro)" }
    }

    # E a requisicao fica em rascunho: falhar a submissao nao pode fazer perder
    # o que ja foi escrito.
    $estado = Invoke-Sql "select status from procurement.purchase_requisition where id='$($script:requisicaoId)'"
    if ($estado -ne "Draft") { throw "estado '$estado' depois de uma submissao falhada" }

    "409 a nomear o tipo de processo, e a requisicao continua em Draft"
}

Test-Case "16. Com politica, submeter cria o processo em approval" {
    $politica = Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ processType = "procurement.purchase_requisition"; steps = @(@{ approverPositionId = $cargo }) } | ConvertTo-Json -Depth 5)
    $script:politicaId = $politica.policyId

    $r = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/submission" -Method Post -Headers $managerHeaders
    $script:processoId = $r.approvalRequestId

    if ($r.estado -ne "PendingApproval") { throw "estado '$($r.estado)'" }
    if (-not $script:processoId) { throw "submetida sem processo de aprovacao" }

    $processo = Invoke-RestMethod "$base/approval/requests/$($script:processoId)" -Headers $adminHeaders
    if ($processo.processType -ne "procurement.purchase_requisition") { throw "tipo de processo errado: $($processo.processType)" }
    if ($processo.sourceModule -ne "procurement") { throw "modulo de origem errado: $($processo.sourceModule)" }

    # A referencia e a requisicao, e nao o requisitante: e o registo que o
    # processo decide, e e por ela que `procurement` o reencontra.
    if ($processo.sourceReference -ne $script:requisicaoId) { throw "referencia de origem errada: $($processo.sourceReference)" }
    if ($processo.pendingApprovers -notcontains $aprovador) { throw "o aprovador nao ficou atribuido ao passo" }

    "202, processo $($script:processoId) com a requisicao por referencia"
}

Test-Case "17. O valor estimado vai com a submissao, e escolhe a alcada" {
    # E estimativa, e a palavra importa: e o que o requisitante acha que custa,
    # antes de haver cotacao e antes de haver factura. Sem valor, a requisicao
    # nao cairia em faixa de alcada nenhuma e todas seriam iguais.
    $valor = Invoke-Sql "select amount from approval.request where id='$($script:processoId)'"
    if ([decimal]$valor -ne 1725000) { throw "valor no processo $valor, esperado 1725000" }

    $moeda = Invoke-Sql "select currency from approval.request where id='$($script:processoId)'"
    if ($moeda -ne "AOA") { throw "moeda '$moeda'" }

    "1725000 AOA no processo, vindos das linhas"
}

Test-Case "18. A requisicao nao guarda o estado da decisao" {
    # Anti-padrao do prototipo, e o mesmo que `finance` evita nos pedidos de
    # pagamento: uma copia do estado fica obsoleta em silencio, e passam a
    # existir duas versoes da verdade sobre a mesma decisao.
    $colunas = Invoke-Sql @"
select count(*) from sys.columns
where object_id = object_id('procurement.purchase_requisition')
  and name in ('approval_status','decision','approved_by','decided_by')
"@
    if ($colunas -ne "0") { throw "a requisicao copia o estado da decisao" }

    $ponteiro = Invoke-Sql "select count(*) from sys.columns where object_id=object_id('procurement.purchase_requisition') and name='approval_request_id'"
    if ($ponteiro -ne "1") { throw "sem ponteiro para o processo" }

    "so um ponteiro para approval, sem copia do estado"
}

Test-Case "19. Depois de submetida, submeter outra vez recusa" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/submission" -Method Post -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 - o que se pede ja foi congelado do lado de approval (BR-6)"
}

Test-Case "20. Enquanto ninguem decide, aplicar a decisao devolve 202" {
    # 202 e nao 200: nao ha decisao para aplicar, e quem chamou tem de voltar.
    try {
        $r = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/approval-outcome" -Method Post -Headers $managerHeaders
        if ($r.estado -ne "PendingApproval") { throw "estado '$($r.estado)'" }
    }
    catch { throw "aplicar a decisao pendente falhou: $($_.Exception.Message)" }

    "202 e continua PendingApproval"
}

Test-Case "21. Decidida em approval, o efeito e aplicado em procurement" {
    # **`approval` nunca empurra.** `modules/approval.md` proibe expressamente
    # que o motor altere dados de negocio do modulo de origem — o efeito parte
    # daqui, e e por isso que existe uma rota para o pedir.
    $body = @{ decidedByEmployeeId = $aprovador; action = "Approved"; notes = "Substituicao justificada." } | ConvertTo-Json
    Invoke-RestMethod "$base/approval/requests/$($script:processoId)/decisions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    # Decidido do outro lado, e a requisicao ainda nao sabe.
    $antes = Invoke-Sql "select status from procurement.purchase_requisition where id='$($script:requisicaoId)'"
    if ($antes -ne "PendingApproval") { throw "approval alterou a requisicao sozinho: '$antes'" }

    $r = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/approval-outcome" -Method Post -Headers $managerHeaders
    if ($r.estado -ne "Approved") { throw "estado '$($r.estado)'" }

    "aprovada em approval, e so aplicada quando procurement pergunta"
}

Test-Case "22. Aplicar a decisao outra vez nao falha nem duplica" {
    # Idempotente por construcao: quem chama pode chamar outra vez, e um worker
    # de reconciliacao vai chamar.
    $r = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/approval-outcome" -Method Post -Headers $managerHeaders
    if ($r.estado -ne "Approved") { throw "estado '$($r.estado)' na segunda chamada" }

    $aprovacoes = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.requisition.approved' and entity_id='$($script:requisicaoId)'"
    if ($aprovacoes -ne "1") { throw "$aprovacoes registos de aprovacao na trilha, esperado 1" }

    "segunda chamada devolve Approved, e a trilha nao duplica"
}

Test-Case "23. Uma requisicao aprovada ja nao se cancela" {
    # Ha decisao registada, e desfaze-la aqui apagaria a decisao de outra
    # pessoa. O que se cancela nesse ponto e a Ordem de Compra.
    $body = @{ reason = "Mudei de ideias." } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $estado = Invoke-Sql "select status from procurement.purchase_requisition where id='$($script:requisicaoId)'"
    if ($estado -ne "Approved") { throw "estado alterado apesar do 409: '$estado'" }

    "409, e continua Approved"
}

Test-Case "24. Um rascunho cancela-se, com razao, e nao se elimina" {
    $body = @{
        requestedByEmployeeId = $requisitante
        justification         = "Pedido que vai ser cancelado."
        lines                 = @(@{ description = "Cadeira"; quantity = 1; estimatedUnitPrice = 45000 })
    } | ConvertTo-Json -Depth 5

    $r = Invoke-RestMethod "$base/procurement/requisitions" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:canceladaId = $r.requisitionId

    $body = @{ reason = "Resolvido de outra forma." } | ConvertTo-Json
    Invoke-RestMethod "$base/procurement/requisitions/$($script:canceladaId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $estado = Invoke-Sql "select status from procurement.purchase_requisition where id='$($script:canceladaId)'"
    if ($estado -ne "Cancelled") { throw "estado '$estado'" }

    $razao = Invoke-Sql "select closing_reason from procurement.purchase_requisition where id='$($script:canceladaId)'"
    if ($razao -ne "Resolvido de outra forma.") { throw "razao perdida: '$razao'" }

    # BR-14: a linha fica, e as linhas do pedido com ela.
    $linhas = Invoke-Sql "select count(*) from procurement.requisition_line where requisition_id='$($script:canceladaId)'"
    if ($linhas -ne "1") { throw "as linhas desapareceram com o cancelamento" }

    "Cancelled com razao, e a linha continua la (BR-14)"
}

Test-Case "25. Cancelar sem razao e recusado" {
    # Sem razao, quem abriu a requisicao nao sabe se foi engano, se foi decisao,
    # nem o que corrigir para voltar a pedir.
    $body = @{
        requestedByEmployeeId = $requisitante
        justification         = "Outro pedido."
        lines                 = @(@{ description = "Secretaria"; quantity = 1; estimatedUnitPrice = 90000 })
    } | ConvertTo-Json -Depth 5
    $r = Invoke-RestMethod "$base/procurement/requisitions" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders

    $body = @{ reason = "" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions/$($r.requisitionId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 - um cancelamento sem razao nao explica nada a ninguem"
}

Test-Case "26. Sem chave estrangeira entre schemas para hr nem para approval" {
    # O requisitante e o processo sao identificadores de outro contexto. Uma FK
    # entre schemas amarraria o ciclo de vida dos dois lados (ADR-010) — e
    # impediria `procurement` de existir sem eles.
    $fks = Invoke-Sql @"
select count(*) from sys.foreign_keys fk
join sys.tables t on t.object_id = fk.parent_object_id
join sys.schemas s on s.schema_id = t.schema_id
join sys.tables rt on rt.object_id = fk.referenced_object_id
join sys.schemas rs on rs.schema_id = rt.schema_id
where s.name = 'procurement' and rs.name <> 'procurement'
"@
    if ($fks -ne "0") { throw "$fks chaves estrangeiras a sair do schema procurement" }
    "0 FK a sair do schema - a ligacao e por identificador"
}

Test-Case "27. Autorizacao: 401 sem token, 403 no perfil errado" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/suppliers" }
    if ($code -ne 401) { throw "sem token esperado 401, obtido $code" }

    # `Sales` nao tem nada em `procurement`.
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions" -Headers $salesHeaders }
    if ($code -ne 403) { throw "Sales a ler requisicoes: $code" }

    # `Finance` ve fornecedores e nao os qualifica — a segregacao do caso 1,
    # agora imposta pela rota e nao so pelo catalogo.
    $ve = Get-StatusCode { Invoke-RestMethod "$base/procurement/suppliers" -Headers $financeHeaders }
    if ($ve -ne 200) { throw "Finance nao consegue ver fornecedores: $ve" }

    $body = @{ name = "Fornecedor da tesouraria"; taxId = "5777$curto" } | ConvertTo-Json
    $escreve = Get-StatusCode { Invoke-RestMethod "$base/procurement/suppliers" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($escreve -ne 403) { throw "Finance qualificou um fornecedor: $escreve" }

    "401 sem token; Sales 403; Finance ve e nao qualifica"
}

Test-Case "28. Abrir e submeter ficam na trilha, com actor" {
    $abertura = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.requisition.opened' and entity_id='$($script:requisicaoId)'"
    if ($abertura -ne "1") { throw "abertura nao esta na trilha" }

    $submissao = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.requisition.submitted' and entity_id='$($script:requisicaoId)'"
    if ($submissao -ne "1") { throw "submissao nao esta na trilha" }

    $comProcesso = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.requisition.submitted' and entity_id='$($script:requisicaoId)' and new_value like '%$($script:processoId)%'"
    if ($comProcesso -ne "1") { throw "a submissao nao guarda o processo de aprovacao" }

    $semActor = Invoke-Sql "select count(*) from audit.audit_event where entity_type='procurement.purchase_requisition' and entity_id='$($script:requisicaoId)' and actor_id is null"
    if ($semActor -ne "0") { throw "ha registos sem actor" }

    "abrir, submeter e aprovar na trilha, todos com actor"
}

# --- Ordem de Compra

Test-Case "29. De uma requisicao nao aprovada nao nasce ordem" {
    # **A regra que este elo existe para impor.** Encomendar sem decisao
    # registada e exactamente o que a governanca existe para impedir, e nao ha
    # ordem avulsa — o fluxo leve de despesa eventual e lacuna assumida.
    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Portatil"; quantity = 1; unitPrice = 100000 })
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod "$base/procurement/requisitions/$($script:canceladaId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null
        throw "emitiu ordem de uma requisicao cancelada"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw "esperado 409, obtido $([int]$_.Exception.Response.StatusCode)" }
        $corpo = $_.ErrorDetails.Message | ConvertFrom-Json
        if ($corpo.erro -notmatch "Cancelled") { throw "o erro nao diz em que estado esta: $($corpo.erro)" }
    }

    $ordens = Invoke-Sql "select count(*) from procurement.purchase_order where requisition_id='$($script:canceladaId)'"
    if ($ordens -ne "0") { throw "ficou uma ordem gravada apesar do 409" }

    "409 a nomear o estado, e nenhuma ordem gravada"
}

Test-Case "30. Fornecedor desactivado nao recebe encomendas" {
    # Se ainda se lhe pudesse encomendar, a desactivacao era um rotulo.
    $body = @{
        supplierId = $script:estrangeiroId
        lines      = @(@{ description = "Portatil"; quantity = 1; unitPrice = 100000 })
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 - desactivar um fornecedor tem de significar alguma coisa"
}

Test-Case "31. Emitir ordem de requisicao aprovada, ao preco acordado" {
    # **O preco nao e o estimado.** A requisicao diz o que se quer e por quanto
    # se estima; a ordem diz o que se encomenda e por quanto se acordou. Entre
    # as duas houve cotacao — copiar o estimado faria dela campo decorativo.
    $body = @{
        supplierId = $script:fornecedorId
        expectedOn = "2026-09-30"
        lines      = @(
            @{ description = "Portatil 14 pol, 16 GB"; quantity = 2; unitPrice = 500000 }
        )
    } | ConvertTo-Json -Depth 5

    $o = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:ordemA = $o.purchaseOrderId

    if ($o.estado -ne "Issued") { throw "estado '$($o.estado)'" }
    if ([decimal]$o.total -ne 1000000) { throw "total $($o.total), esperado 1000000" }

    # O estimado por unidade era 850000; o acordado e 500000. A ordem guarda o
    # acordado, e a requisicao continua a guardar o estimado.
    $preco = Invoke-Sql "select unit_price from procurement.purchase_order_line where purchase_order_id='$($script:ordemA)'"
    if ([decimal]$preco -ne 500000) { throw "preco gravado $preco, esperado o acordado 500000" }

    $estimado = Invoke-Sql "select top 1 estimated_unit_price from procurement.requisition_line where requisition_id='$($script:requisicaoId)' and description like 'Portatil%'"
    if ([decimal]$estimado -ne 850000) { throw "a requisicao perdeu o estimado: $estimado" }

    "1000000 ao preco acordado; a requisicao mantem o estimado de 850000"
}

Test-Case "32. As linhas da ordem sobrevivem a base de dados" {
    # Mesma razao do caso 12, e a mesma classe de defeito: uma coleccao mapeada
    # por campo de apoio grava e rele sem as linhas, em silencio.
    $o = Invoke-RestMethod "$base/procurement/orders/$($script:ordemA)" -Headers $managerHeaders
    if ($o.lines.Count -ne 1) { throw "relidas $($o.lines.Count) linhas, esperada 1" }
    if ([decimal]$o.lines[0].lineTotal -ne 1000000) { throw "total da linha $($o.lines[0].lineTotal)" }

    # A moeda vem da requisicao e nao de quem encomenda: foi nela que o valor
    # aprovado foi expresso.
    if ($o.currency -ne "AOA") { throw "moeda '$($o.currency)'" }

    # O nome do fornecedor e lido a cada leitura, e nao copiado para a ordem —
    # uma copia ficaria obsoleta em silencio (BR-18).
    $nome = Invoke-Sql "select name from procurement.supplier where id='$($script:fornecedorId)'"
    if ($o.supplierName -ne $nome) { throw "nome '$($o.supplierName)', na base '$nome'" }

    $colunas = Invoke-Sql "select count(*) from sys.columns where object_id=object_id('procurement.purchase_order') and name in ('supplier_name','supplier_tax_id')"
    if ($colunas -ne "0") { throw "a ordem copia o nome do fornecedor" }

    "1 linha relida, moeda herdada, e o nome do fornecedor lido e nao copiado"
}

Test-Case "33. Encomendar acima do aprovado e recusado" {
    # **A invariante sobre o conjunto.** Ja ha 1000000 encomendados contra uma
    # requisicao aprovada por 1725000: restam 725000. Uma ordem de 800000
    # passaria sozinha, e junta com a primeira encomendava acima da alcada sem
    # que nada tivesse sido violado.
    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Portateis a mais"; quantity = 1; unitPrice = 800000 })
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null
        throw "encomendou acima do aprovado"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw "esperado 409, obtido $([int]$_.Exception.Response.StatusCode)" }

        # A mensagem tem de dar as tres parcelas: sem elas, quem a le nao sabe
        # se pede menos, se cancela uma ordem, ou se abre requisicao nova.
        $corpo = $_.ErrorDetails.Message | ConvertFrom-Json
        foreach ($n in @("1725000", "1000000", "725000", "800000")) {
            if ($corpo.erro -notmatch $n) { throw "a mensagem nao diz $n : $($corpo.erro)" }
        }
    }

    "409 - a alcada nao se contorna encomendando as fatias"
}

Test-Case "34. O que resta cabe ao centimo" {
    # O limite e inclusivo: 725000 exactos entram. Se nao entrassem, o ultimo
    # bocado de cada requisicao ficava por encomendar sem razao nenhuma.
    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Ratos e cabos"; quantity = 1; unitPrice = 725000 })
    } | ConvertTo-Json -Depth 5

    $o = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:ordemB = $o.purchaseOrderId
    if ([decimal]$o.total -ne 725000) { throw "total $($o.total)" }

    # E agora nao resta nada: mais um kwanza e recusado.
    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Mais um"; quantity = 1; unitPrice = 1 })
    } | ConvertTo-Json -Depth 5
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "com a alcada esgotada, esperado 409, obtido $code" }

    "725000 exactos entram; o kwanza seguinte nao"
}

Test-Case "35. Cancelar uma ordem devolve a alcada" {
    # Uma ordem cancelada deixou de ser compromisso, e continuar a contar
    # contra o aprovado prenderia a requisicao a uma encomenda que ja nao
    # existe.
    $body = @{ reason = "Fornecedor sem stock." } | ConvertTo-Json
    Invoke-RestMethod "$base/procurement/orders/$($script:ordemA)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $estado = Invoke-Sql "select status from procurement.purchase_order where id='$($script:ordemA)'"
    if ($estado -ne "Cancelled") { throw "estado '$estado'" }

    # Libertados 1000000. Uma ordem de 900000 passa agora, e nao passava antes.
    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Substituicao noutro fornecedor"; quantity = 1; unitPrice = 900000 })
    } | ConvertTo-Json -Depth 5
    $o = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    if ([decimal]$o.total -ne 900000) { throw "total $($o.total)" }

    "cancelada nao conta; os 1000000 voltaram a estar disponiveis"
}

Test-Case "36. Uma ordem cancelada nao se altera nem se elimina (BR-14)" {
    $body = @{ reason = "Outra vez." } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders/$($script:ordemA)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "cancelar duas vezes: esperado 409, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders/$($script:ordemA)" -Method Delete -Headers $managerHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }

    # A ordem existiu e saiu para alguem: as linhas ficam.
    $linhas = Invoke-Sql "select count(*) from procurement.purchase_order_line where purchase_order_id='$($script:ordemA)'"
    if ($linhas -ne "1") { throw "as linhas desapareceram com o cancelamento" }

    "409 no segundo cancelamento, DELETE recusado, linhas intactas"
}

Test-Case "37. Cancelar sem razao e recusado" {
    $body = @{ reason = "" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 - alguem do outro lado vai perguntar porque"
}

Test-Case "38. A ordem esta amarrada a requisicao e ao fornecedor" {
    # Aqui ha FK, ao contrario da ligacao a `hr` e a `approval`: as tres tabelas
    # vivem no mesmo schema e no mesmo modulo. **Sem cascata** — apagar uma
    # requisicao levaria atras encomendas que sairam para fornecedores.
    $fks = Invoke-Sql @"
select count(*) from sys.foreign_keys fk
join sys.tables t on t.object_id = fk.parent_object_id
join sys.tables rt on rt.object_id = fk.referenced_object_id
where t.name = 'purchase_order' and rt.name in ('purchase_requisition','supplier')
  and fk.delete_referential_action = 0
"@
    if ($fks -ne "2") { throw "$fks FK sem cascata, esperadas 2" }

    $foraDoSchema = Invoke-Sql @"
select count(*) from sys.foreign_keys fk
join sys.tables t on t.object_id = fk.parent_object_id
join sys.schemas s on s.schema_id = t.schema_id
join sys.tables rt on rt.object_id = fk.referenced_object_id
join sys.schemas rs on rs.schema_id = rt.schema_id
where s.name = 'procurement' and rs.name <> 'procurement'
"@
    if ($foraDoSchema -ne "0") { throw "$foraDoSchema FK a sair do schema procurement" }

    "2 FK dentro do schema, sem cascata; 0 a sair dele"
}

Test-Case "39. Quem paga ve as ordens e nao as emite" {
    # `Finance` precisa da ordem para casar a factura do fornecedor. Emiti-la e
    # a outra ponta do mesmo processo.
    $ve = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders" -Headers $financeHeaders }
    if ($ve -ne 200) { throw "Finance nao consegue ver ordens: $ve" }

    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Ordem da tesouraria"; quantity = 1; unitPrice = 1000 })
    } | ConvertTo-Json -Depth 5
    $emite = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($emite -ne 403) { throw "Finance emitiu uma ordem: $emite" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders" -Headers $salesHeaders }
    if ($code -ne 403) { throw "Sales a ler ordens: $code" }

    "Finance ve e nao emite; Sales nem ve"
}

Test-Case "40. Emitir e cancelar ficam na trilha, com o total" {
    $emissao = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.order.issued' and entity_id='$($script:ordemA)'"
    if ($emissao -ne "1") { throw "emissao nao esta na trilha" }

    $comTotal = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.order.issued' and entity_id='$($script:ordemA)' and new_value like '%1000000%'"
    if ($comTotal -ne "1") { throw "a trilha nao guarda o total encomendado" }

    $cancelamento = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.order.cancelled' and entity_id='$($script:ordemA)'"
    if ($cancelamento -ne "1") { throw "cancelamento nao esta na trilha" }

    $semActor = Invoke-Sql "select count(*) from audit.audit_event where entity_type='procurement.purchase_order' and entity_id='$($script:ordemA)' and actor_id is null"
    if ($semActor -ne "0") { throw "ha registos sem actor" }

    "emissao com total e cancelamento na trilha, ambos com actor"
}

# --- Recepção de Mercadoria

Test-Case "41. Quem encomenda nao recebe (segregacao do 3-way match)" {
    # **E a segregacao que da valor ao match.** Se quem encomenda fosse quem
    # regista a chegada, uma entrega a menos podia ser dada como completa sem
    # que mais ninguem visse — e o terceiro lado, a factura, ficava a ser
    # comparado com dois numeros escritos pela mesma pessoa.
    $mEncomenda = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='procurement.orders.write'"
    $mRecebe = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='procurement.receipts.write'"
    if ($mEncomenda -ne "1") { throw "Manager nao encomenda" }
    if ($mRecebe -ne "0") { throw "Manager encomenda e recebe - a segregacao caiu" }

    $aRecebe = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='AssetManager' and c.claim_value='procurement.receipts.write'"
    $aEncomenda = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='AssetManager' and c.claim_value='procurement.orders.write'"
    if ($aRecebe -ne "1") { throw "AssetManager nao recebe" }
    if ($aEncomenda -ne "0") { throw "AssetManager encomenda" }

    "Manager encomenda sem receber; AssetManager recebe sem encomendar"
}

Test-Case "42. Emitir uma ordem nova para receber contra ela" {
    # A `ordemB` de 725000 ficou por receber, e a alcada tem 100000 livres
    # depois do cancelamento e da ordem de 900000. Esta suite recebe contra a
    # `ordemB`, que tem uma linha so.
    $o = Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)" -Headers $managerHeaders
    if ($o.status -ne "Issued") { throw "a ordem de referencia nao esta em vigor: $($o.status)" }
    if ($o.lines.Count -ne 1) { throw "esperada 1 linha, obtidas $($o.lines.Count)" }

    $script:linhaB = $o.lines[0].lineId
    if ([decimal]$o.lines[0].quantity -ne 1) { throw "quantidade encomendada $($o.lines[0].quantity)" }

    # Antes de haver recepcoes, o recebido e zero e a ordem nao esta completa.
    if ([decimal]$o.lines[0].quantityReceived -ne 0) { throw "recebido $($o.lines[0].quantityReceived) antes de haver recepcoes" }
    if ($o.fullyReceived -ne $false) { throw "ordem dada como recebida sem recepcao nenhuma" }

    "ordem em vigor, 1 encomendada, 0 recebidas"
}

Test-Case "43. Nao se recebe uma linha de outra ordem" {
    # Deixa-lo passar poria a recepcao a satisfazer uma encomenda diferente da
    # que se pretende, e o match comparava coisas que nao se correspondem.
    $body = @{
        receivedByEmployeeId = $recebedor
        lines                = @(@{ purchaseOrderLineId = [guid]::NewGuid().ToString(); quantityReceived = 1 })
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 - a contagem e sempre de uma linha desta ordem"
}

Test-Case "44. Receber acima do encomendado e recusado" {
    # **Recusa, e nao tolerancia.** Aceitar em silencio faria a empresa dever
    # mais do que encomendou, e o 3-way match deixava de ter contra que
    # comparar. Um limiar de excesso aceitavel e decisao de negocio sem fonte.
    $body = @{
        receivedByEmployeeId = $recebedor
        lines                = @(@{ purchaseOrderLineId = $script:linhaB; quantityReceived = 2 })
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders | Out-Null
        throw "recebeu o dobro do encomendado"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw "esperado 409, obtido $([int]$_.Exception.Response.StatusCode)" }
        $corpo = $_.ErrorDetails.Message | ConvertFrom-Json
        if ($corpo.erro -notmatch "Ratos e cabos") { throw "a mensagem nao nomeia a linha: $($corpo.erro)" }
    }

    $recepcoes = Invoke-Sql "select count(*) from procurement.goods_receipt where purchase_order_id='$($script:ordemB)'"
    if ($recepcoes -ne "0") { throw "ficou uma recepcao gravada apesar do 409" }

    "409 a nomear a linha, e nada gravado"
}

Test-Case "45. Registar a recepcao, com guia e com quem recebeu" {
    $body = @{
        receivedByEmployeeId = $recebedor
        deliveryNote         = "GR $curto"
        lines                = @(@{ purchaseOrderLineId = $script:linhaB; quantityReceived = 1 })
    } | ConvertTo-Json -Depth 5

    $g = Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders
    $script:recepcaoId = $g.goodsReceiptId
    if ($g.estado -ne "Registered") { throw "estado '$($g.estado)'" }

    $lida = Invoke-RestMethod "$base/procurement/receipts/$($script:recepcaoId)" -Headers $assetHeaders
    if ($lida.lines.Count -ne 1) { throw "relidas $($lida.lines.Count) linhas, esperada 1" }
    if ($lida.deliveryNote -ne "GR $curto") { throw "guia perdida: '$($lida.deliveryNote)'" }
    if ($lida.receivedByEmployeeId -ne $recebedor) { throw "quem recebeu perdeu-se" }
    if ($lida.lines[0].purchaseOrderLineId -ne $script:linhaB) { throw "a ligacao a linha da ordem perdeu-se" }

    "recepcao com guia GR $curto, ligada a linha da ordem"
}

Test-Case "46. A ordem passa a mostrar o que chegou" {
    # E o segundo lado do 3-way match a aparecer na ordem: encomendado contra
    # recebido, linha a linha.
    $o = Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)" -Headers $managerHeaders
    if ([decimal]$o.lines[0].quantityReceived -ne 1) { throw "recebido $($o.lines[0].quantityReceived), esperado 1" }
    if ($o.fullyReceived -ne $true) { throw "a ordem devia estar completa" }

    "1 de 1 recebida; a ordem esta completa"
}

Test-Case "47. Com a ordem completa, mais um e recusado" {
    $body = @{
        receivedByEmployeeId = $recebedor
        lines                = @(@{ purchaseOrderLineId = $script:linhaB; quantityReceived = 1 })
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 - o acumulado conta, nao so a contagem desta vez"
}

Test-Case "48. Uma ordem com mercadoria recebida nao se cancela" {
    # O material esta ca. Cancelar a encomenda nao o faz desaparecer, e
    # deixaria a empresa com material recebido contra uma ordem que diz nao
    # existir — e o match sem o lado do meio.
    $body = @{ reason = "Desisti." } | ConvertTo-Json
    try {
        Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null
        throw "cancelou uma ordem com mercadoria recebida"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw "esperado 409, obtido $([int]$_.Exception.Response.StatusCode)" }
        $corpo = $_.ErrorDetails.Message | ConvertFrom-Json
        if ($corpo.erro -notmatch "Anule primeiro") { throw "o erro nao diz o que fazer: $($corpo.erro)" }
    }

    $estado = Invoke-Sql "select status from procurement.purchase_order where id='$($script:ordemB)'"
    if ($estado -ne "Issued") { throw "a ordem mudou de estado apesar do 409: '$estado'" }

    "409 a dizer o que fazer, e a ordem continua em vigor"
}

Test-Case "49. Anular a recepcao devolve a quantidade por receber" {
    # Anular e corrigir um engano de registo — a guia lancada na ordem errada,
    # a contagem mal feita. **Nao e devolver mercadoria ao fornecedor**, que e
    # outro facto e nao existe.
    $body = @{ reason = "Contagem errada: chegou a caixa vazia." } | ConvertTo-Json
    Invoke-RestMethod "$base/procurement/receipts/$($script:recepcaoId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders | Out-Null

    $estado = Invoke-Sql "select status from procurement.goods_receipt where id='$($script:recepcaoId)'"
    if ($estado -ne "Cancelled") { throw "estado '$estado'" }

    # BR-14: as linhas ficam. O erro foi cometido, e o registo de o ter sido e
    # a parte que interessa a quem audita.
    $linhas = Invoke-Sql "select count(*) from procurement.goods_receipt_line where goods_receipt_id='$($script:recepcaoId)'"
    if ($linhas -ne "1") { throw "as linhas desapareceram com a anulacao" }

    $o = Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)" -Headers $managerHeaders
    if ([decimal]$o.lines[0].quantityReceived -ne 0) { throw "anulada e ainda conta: $($o.lines[0].quantityReceived)" }
    if ($o.fullyReceived -ne $false) { throw "a ordem continua dada como completa" }

    "anulada nao conta; a linha volta a 1 por receber, e o registo fica"
}

Test-Case "50. Recepcoes parciais somam, e a ordem so fecha no fim" {
    # Uma entrega parcial e o caso normal, e distingue-se de duas contagens da
    # mesma coisa: sao recepcoes diferentes, com guias diferentes.
    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Cadeiras"; quantity = 10; unitPrice = 9000 })
    } | ConvertTo-Json -Depth 5
    $o = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:ordemC = $o.purchaseOrderId
    $linhaC = (Invoke-RestMethod "$base/procurement/orders/$($script:ordemC)" -Headers $managerHeaders).lines[0].lineId

    foreach ($quantidade in @(4, 6)) {
        $body = @{
            receivedByEmployeeId = $recebedor
            deliveryNote         = "GR $curto-$quantidade"
            lines                = @(@{ purchaseOrderLineId = $linhaC; quantityReceived = $quantidade })
        } | ConvertTo-Json -Depth 5
        Invoke-RestMethod "$base/procurement/orders/$($script:ordemC)/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders | Out-Null

        $parcial = Invoke-RestMethod "$base/procurement/orders/$($script:ordemC)" -Headers $managerHeaders
        if ($quantidade -eq 4 -and $parcial.fullyReceived -ne $false) { throw "4 de 10 e a ordem ja diz completa" }
    }

    $final = Invoke-RestMethod "$base/procurement/orders/$($script:ordemC)" -Headers $managerHeaders
    if ([decimal]$final.lines[0].quantityReceived -ne 10) { throw "acumulado $($final.lines[0].quantityReceived), esperado 10" }
    if ($final.fullyReceived -ne $true) { throw "10 de 10 e a ordem nao fecha" }

    $recepcoes = Invoke-Sql "select count(*) from procurement.goods_receipt where purchase_order_id='$($script:ordemC)' and status='Registered'"
    if ($recepcoes -ne "2") { throw "$recepcoes recepcoes, esperadas 2" }

    "4 + 6 = 10 em duas guias; a ordem so fecha na segunda"
}

Test-Case "51. Anular sem razao e recusado" {
    $body = @{ reason = "" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/receipts/$($script:recepcaoId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/receipts/$($script:recepcaoId)" -Method Delete -Headers $assetHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }

    "409 sem razao, e DELETE recusado (BR-14)"
}

Test-Case "52. Autorizacao das recepcoes, nas duas direccoes" {
    $body = @{
        receivedByEmployeeId = $recebedor
        lines                = @(@{ purchaseOrderLineId = $script:linhaB; quantityReceived = 1 })
    } | ConvertTo-Json -Depth 5

    # Manager encomenda e nao recebe.
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/orders/$($script:ordemB)/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 403) { throw "Manager registou uma recepcao: $code" }

    # AssetManager recebe e nao encomenda.
    $body = @{
        supplierId = $script:fornecedorId
        lines      = @(@{ description = "Ordem do armazem"; quantity = 1; unitPrice = 1000 })
    } | ConvertTo-Json -Depth 5
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)/orders" -Method Post -Body $body -ContentType "application/json" -Headers $assetHeaders }
    if ($code -ne 403) { throw "AssetManager emitiu uma ordem: $code" }

    # Finance ve as recepcoes: e o lado do meio do 3-way match.
    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/receipts" -Headers $financeHeaders }
    if ($code -ne 200) { throw "Finance nao ve recepcoes: $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/procurement/receipts" -Headers $salesHeaders }
    if ($code -ne 403) { throw "Sales a ler recepcoes: $code" }

    "Manager nao recebe; AssetManager nao encomenda; Finance ve; Sales nao"
}

Test-Case "53. Recepcoes ficam na trilha, com a ordem e quem recebeu" {
    $registo = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.receipt.registered' and entity_id='$($script:recepcaoId)'"
    if ($registo -ne "1") { throw "registo nao esta na trilha" }

    $comOrdem = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.receipt.registered' and entity_id='$($script:recepcaoId)' and new_value like '%$($script:ordemB)%'"
    if ($comOrdem -ne "1") { throw "a trilha nao guarda a ordem" }

    $anulacao = Invoke-Sql "select count(*) from audit.audit_event where action='procurement.receipt.cancelled' and entity_id='$($script:recepcaoId)'"
    if ($anulacao -ne "1") { throw "anulacao nao esta na trilha" }

    $semActor = Invoke-Sql "select count(*) from audit.audit_event where entity_type='procurement.goods_receipt' and entity_id='$($script:recepcaoId)' and actor_id is null"
    if ($semActor -ne "0") { throw "ha registos sem actor" }

    "registo com a ordem e anulacao na trilha, ambos com actor"
}

# ---- 3-way match: a factura de compra e de finance, mas liga-se pela ordem (2026-08-28) ----

Test-Case "54. Registar a factura contra a ordem, e o match mostra os tres numeros" {
    # ordemC (caso 50): 10 Cadeiras a 9000, recebidas por inteiro -- 90000 dos
    # dois lados de procurement. A factura fecha o terceiro.
    $body = @{
        supplierInvoiceNumber = "FT ORD $curto"; supplierId = $script:fornecedorId; purchaseOrderId = $script:ordemC
        supplierName = "Angoferragens $curto"; supplierTaxId = "5417$curto"
        netTotal = 90000; taxTotal = 0; dueOn = "2026-12-31"
    } | ConvertTo-Json
    # Manager regista a factura -- ForPayables: quem compra regista e pede
    # que se pague. Finance so le o resultado: "quem desfaz nao faz" tambem
    # quer dizer que nao emite (AccessProfiles.cs).
    $f = Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:facturaOrdemC = $f.purchaseInvoiceId

    $match = Invoke-RestMethod "$base/finance/purchase-invoices/$($script:facturaOrdemC)/match" -Headers $financeHeaders
    if ($match.purchaseOrderId -ne $script:ordemC) { throw "match sem a ordem: $($match.purchaseOrderId)" }
    if ([decimal]$match.orderedTotal -ne 90000) { throw "encomendado $($match.orderedTotal), esperado 90000" }
    if ([decimal]$match.receivedTotal -ne 90000) { throw "recebido $($match.receivedTotal), esperado 90000" }
    if ([decimal]$match.invoicedNetTotal -ne 90000) { throw "facturado $($match.invoicedNetTotal), esperado 90000" }
    "encomendado, recebido e facturado batem em 90000"
}

Test-Case "55. Ligar a factura a uma ordem de outro fornecedor e recusado" {
    $body = @{
        supplierInvoiceNumber = "FT ERR $curto"; supplierId = $script:estrangeiroId; purchaseOrderId = $script:ordemC
        supplierName = "Fornecedor errado"; supplierTaxId = "0000000000"
        netTotal = 1000; taxTotal = 0; dueOn = "2026-12-31"
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "a ordem e do fornecedor original, nao do indicado"
}

Test-Case "56. Facturar diferente do recebido nao bloqueia, so fica visivel no match" {
    # Mesma ordem, outra factura, valor propositadamente diferente do
    # recebido -- e o caso que o 3-way match existe para apanhar, e a decisao
    # tomada foi so mostrar os numeros, nunca recusar (informacao, nao regra).
    $body = @{
        supplierInvoiceNumber = "FT DIV $curto"; supplierId = $script:fornecedorId; purchaseOrderId = $script:ordemC
        supplierName = "Angoferragens $curto"; supplierTaxId = "5417$curto"
        netTotal = 95000; taxTotal = 0; dueOn = "2026-12-31"
    } | ConvertTo-Json
    $f = Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders

    $match = Invoke-RestMethod "$base/finance/purchase-invoices/$($f.purchaseInvoiceId)/match" -Headers $financeHeaders
    if ([decimal]$match.receivedTotal -ne 90000) { throw "recebido $($match.receivedTotal), esperado 90000" }
    if ([decimal]$match.invoicedNetTotal -ne 95000) { throw "facturado $($match.invoicedNetTotal), esperado 95000" }
    "95000 facturados contra 90000 recebidos -- registado na mesma, a divergencia fica so visivel"
}

Test-Case "57. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $r = Invoke-RestMethod "$base/procurement/requisitions/$($script:requisicaoId)" -Headers $managerHeaders
    if ($r.status -ne "Approved") { throw "estado perdido: $($r.status)" }
    if ($r.lines.Count -ne 2) { throw "linhas perdidas: $($r.lines.Count)" }
    if ([decimal]$r.estimatedTotal -ne 1725000) { throw "total perdido: $($r.estimatedTotal)" }

    $f = Invoke-RestMethod "$base/procurement/suppliers/$($script:fornecedorId)" -Headers $adminHeaders
    if ($f.iban -ne $ibanBom) { throw "IBAN perdido: $($f.iban)" }

    $o = Invoke-RestMethod "$base/procurement/orders/$($script:ordemA)" -Headers $managerHeaders
    if ($o.status -ne "Cancelled") { throw "estado da ordem perdido: $($o.status)" }
    if ($o.lines.Count -ne 1) { throw "linhas da ordem perdidas: $($o.lines.Count)" }

    "requisicao, ordem, linhas de ambas e IBAN intactos apos restart"
}

Test-Case "58. A suite nao deixa politica de procurement activa atras de si" {
    # **Independencia entre suites.** Sem isto, cada corrida deixaria mais uma
    # politica generica, e o caso 15 — que verifica a recusa quando nao ha
    # nenhuma — passaria a falhar a partir da segunda vez.
    #
    # **Pela rota, e ja nao por SQL** (2026-08-27). A limpeza passou a exercitar
    # `POST /approval/policies/{id}/deactivation` — deixou de ser so arrumacao e
    # passou a verificar tambem o endpoint que a torna possivel.
    #
    # `@(...)` a forcar array: e defesa documentada contra um modo de falha
    # real do PowerShell nesta suite (nota "Filtrar respostas JSON..." em
    # implemented.md). Mantido por seguranca -- nao resolveu, por si so, o 404
    # intermitente registado em K20 (known-issues.md), que continua aberto.
    @(Invoke-RestMethod "$base/approval/policies" -Headers $adminHeaders) |
    Where-Object { $_.processType -eq "procurement.purchase_requisition" -and $_.isActive } |
    ForEach-Object {
        Invoke-RestMethod "$base/approval/policies/$($_.policyId)/deactivation" `
            -Method Post -Headers $adminHeaders | Out-Null
    }

    # Confirmado na base de dados, e nao so pela resposta da rota: o 204 diz que
    # a aplicacao aceitou, e a coluna diz que gravou.
    $activas = Invoke-Sql "select count(*) from approval.policy where process_type = 'procurement.purchase_requisition' and is_active = 1"
    if ($activas -ne "0") { throw "$activas politicas de procurement continuam activas" }

    # Repetivel: desactivar uma politica ja desactivada devolve 204 na mesma.
    $repetido = Get-StatusCode { Invoke-RestMethod "$base/approval/policies/$($script:politicaId)/deactivation" -Method Post -Headers $adminHeaders }
    if ($repetido -ne 200) { throw "segunda desactivacao devia ser aceite, obtido $repetido" }

    $inexistente = Get-StatusCode { Invoke-RestMethod "$base/approval/policies/$([guid]::NewGuid())/deactivation" -Method Post -Headers $adminHeaders }
    if ($inexistente -ne 404) { throw "politica inexistente devia dar 404, obtido $inexistente" }

    "a politica desta corrida sai; a suite volta a poder correr do zero"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
