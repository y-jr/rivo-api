# Ambiente contra o qual as suites de verificacao correm.
#
# As suites nasceram a falar directamente com o Docker local: `docker exec` no
# container da base de dados para consultar, `docker compose restart` para
# testar persistencia, e `.env` para credenciais. Isso amarrava-as a uma stack
# local e tornava impossivel correr as mesmas verificacoes contra um ambiente
# ja publicado.
#
# Este ficheiro e a camada que falta. As suites passam a pedir "corre este SQL"
# e "reinicia a aplicacao" sem saberem onde estao.
#
# Configurado por variaveis de ambiente, para nao mexer na assinatura de seis
# ficheiros:
#
#   RIVO_BASE_URL        URL da API.       Omissao: http://localhost:5080
#   RIVO_DB_CONNECTION   Ligacao ADO.NET.  Omissao: docker exec no container local
#   RIVO_ADMIN_EMAIL     Credenciais.      Omissao: lidas do .env
#   RIVO_ADMIN_PASSWORD
#   RIVO_RESTART_COMMAND Comando que reinicia a aplicacao remota.
#
# Sem nenhuma delas, o comportamento e o da stack local em Docker.

$script:BaseUrl = if ($env:RIVO_BASE_URL) { $env:RIVO_BASE_URL } else { "http://localhost:5080" }
$script:DbConnection = $env:RIVO_DB_CONNECTION
$script:RestartCommand = $env:RIVO_RESTART_COMMAND

# Ficheiros do compose da stack local. E a sobreposicao de desenvolvimento que
# acrescenta o SQL Server em container e publica o porto 5080 (ADR-029).
$script:ComposeFiles = @("-f", "docker-compose.yml", "-f", "docker-compose.dev.yml")

# Credenciais do SQL Server local. Sao as do `docker-compose.dev.yml` e nao
# saem de desenvolvimento.
$script:LocalContainer = "rivo-sqlserver"
$script:LocalSaPassword = "Rivo_Dev_2026!"
$script:LocalDatabase = "rivo"

# Caminho do sqlcmd dentro da imagem do SQL Server 2022 e da imagem de
# ferramentas. A versao 18 recusa certificados auto-assinados sem -C, que e
# exactamente o caso de um container.
$script:SqlCmd = "/opt/mssql-tools18/bin/sqlcmd"

# Imagem usada para falar com uma base de dados remota, onde nao ha container
# local em que fazer `docker exec`.
$script:ToolsImage = "mcr.microsoft.com/mssql/server:2022-latest"

<#
.SYNOPSIS
    URL base da API a verificar.
#>
function Get-RivoBaseUrl { return $script:BaseUrl }

<#
.SYNOPSIS
    Verdadeiro quando se verifica a stack local em Docker.
.DESCRIPTION
    Distingue os dois modos num sitio so. Sem ligacao remota configurada,
    assume-se local - que e o caso corrente e o que mantem o comportamento
    historico.
#>
function Test-RivoLocal { return [string]::IsNullOrWhiteSpace($script:DbConnection) }

<#
.SYNOPSIS
    Reparte uma connection string ADO.NET em pares chave/valor.
#>
function Get-RivoConnectionParts {
    $partes = @{}

    $script:DbConnection -split ';' |
    Where-Object { $_ -match '=' } |
    ForEach-Object {
        $par = $_ -split '=', 2
        $partes[$par[0].Trim().ToLowerInvariant()] = $par[1].Trim()
    }

    return $partes
}

<#
.SYNOPSIS
    Corre SQL contra a base de dados do ambiente.
.DESCRIPTION
    Local: docker exec no container, que ja traz o sqlcmd.

    Remoto: sqlcmd dentro de um container descartavel. Evita exigir as
    ferramentas do SQL Server instaladas na maquina - o Docker ja e requisito
    para as suites.

    SET NOCOUNT ON e obrigatorio: sem ele o sqlcmd intercala linhas
    "(N rows affected)" com os resultados, e quem chama compara valores exactos.
#>
function Invoke-RivoSql {
    param(
        [Parameter(Mandatory)][string]$Query,

        # Devolve a saida crua, com o erro do sqlcmd e sem Trim.
        #
        # Necessario para os casos que verificam que uma operacao *falha* - a
        # FK entre schemas a impedir eliminar um documento ligado, por exemplo.
        # Sem isto, o sqlcmd nao devolve nada, o Trim rebenta sobre nulo, e o
        # teste falha por uma razao que nao tem nada a ver com o que verifica.
        [switch]$Raw
    )

    # -h -1  sem cabecalhos de coluna
    # -W     corta o enchimento com que o sqlcmd alinha as colunas, e que
    #        faria falhar comparacoes exactas
    # -C     confia no certificado auto-assinado do servidor
    # -b     codigo de saida diferente de zero quando o SQL falha
    # -I    QUOTED_IDENTIFIER ON, que o sqlcmd deixa desligado por omissao.
    #       Sem isto, qualquer INSERT ou UPDATE numa tabela com indice
    #       filtrado - notifications.notification tem um - falha com o erro
    #       1934, que nao diz nada sobre a causa real.
    $comuns = @("-h", "-1", "-W", "-C", "-b", "-I")
    $sql = "SET NOCOUNT ON; $Query"

    $saida = if (Test-RivoLocal) {
        docker exec $script:LocalContainer $script:SqlCmd -S localhost -U sa -P $script:LocalSaPassword -d $script:LocalDatabase @comuns -Q $sql 2>&1
    }
    else {
        $ligacao = Get-RivoConnectionParts

        $servidor = if ($ligacao.ContainsKey("server")) { $ligacao["server"] } else { $ligacao["data source"] }
        $base = if ($ligacao.ContainsKey("database")) { $ligacao["database"] } else { $ligacao["initial catalog"] }
        $utilizador = if ($ligacao.ContainsKey("user id")) { $ligacao["user id"] } else { $ligacao["uid"] }
        $password = if ($ligacao.ContainsKey("password")) { $ligacao["password"] } else { $ligacao["pwd"] }

        # A imagem do servidor, e nao uma de ferramentas: `mssql-tools18` nao
        # existe no MCR, e esta ja traz o sqlcmd 18 no mesmo caminho. E a mesma
        # do `docker-compose.dev.yml`, portanto cliente e servidor nao divergem
        # de versao (ADR-021).
        #
        # `--entrypoint` e obrigatorio: sem ele a imagem arranca um SQL Server
        # e trata os argumentos como sendo para ele.
        docker run --rm --entrypoint $script:SqlCmd $script:ToolsImage -S $servidor -U $utilizador -P $password -d $base @comuns -Q $sql 2>&1
    }

    if ($Raw) { return $saida }

    # Normaliza para \n antes de devolver. Quem chama reparte por linhas, e o
    # \r\n que o Out-String introduz em Windows deixaria um \r colado a cada
    # valor - o que faz falhar comparacoes exactas por uma razao invisivel a
    # leitura.
    return ($saida | Out-String).Replace("`r`n", "`n").Trim()
}

<#
.SYNOPSIS
    Reinicia a aplicacao e espera que volte a responder.
.PARAMETER ApiOnly
    Reinicia so a API, deixando a base de dados de pe. Em ambientes remotos e
    sempre o caso: a base de dados e externa e nao se reinicia para testar
    persistencia da aplicacao (ADR-029).
.DESCRIPTION
    E o que torna verificavel a persistencia - que os dados sobrevivem ao
    processo.
#>
function Restart-RivoStack {
    # `-IncludeDatabase` reinicia tambem o SQL Server. **Nao e o comportamento
    # por omissao, e a razao e de desenho, nao de conveniencia.**
    #
    # Em producao a base de dados e externa e nunca reinicia com a aplicacao
    # (ADR-029) — reinicia-la aqui verifica uma situacao que la nao acontece. E
    # traz um problema real: `docker compose restart` nao respeita `depends_on`,
    # por isso a API sobe enquanto o SQL Server ainda recupera a base, e o
    # arranque fica refem do tempo de recuperacao. A 2026-08-25 isso passou dos
    # tres minutos e tornou as suites intermitentes.
    #
    # O que interessa verificar e que **a aplicacao nao guarda estado em
    # memoria** — e isso prova-se reiniciando so a aplicacao, que e exactamente
    # o que acontece num deployment.
    param([switch]$IncludeDatabase)

    if (Test-RivoLocal) {
        if ($IncludeDatabase) { docker compose @script:ComposeFiles restart | Out-Null }
        else { docker compose @script:ComposeFiles restart api | Out-Null }
    }
    else {
        if (-not $script:RestartCommand) {
            throw "RIVO_RESTART_COMMAND e necessario para reiniciar um ambiente remoto."
        }

        # Invoke-Expression porque o comando vem de quem corre a suite e pode
        # ser qualquer coisa - um ssh, um script, outro cliente. Nao ha aqui
        # entrada de terceiros: e configuracao do operador.
        Invoke-Expression $script:RestartCommand | Out-Null
    }

    if (-not (Wait-RivoApi)) {
        throw "A API nao voltou a responder depois do reinicio."
    }
}

<#
.SYNOPSIS
    Espera que a API responda em /health.
#>
function Wait-RivoApi {
    # 420 s e nao 180: **a paciencia do script nao pode ser menor que a da
    # aplicacao.** A API espera ate `Database__StartupTimeoutSeconds` pela base
    # de dados, e em desenvolvimento isso sao 420 s porque um
    # `docker compose restart` nao respeita `depends_on` e o SQL Server reinicia
    # com ela.
    #
    # Com 180 s aqui, o script desistia enquanto a aplicacao ainda estava
    # legitimamente a arrancar, e reportava "a API nao voltou" quando a API
    # estava a fazer exactamente o que devia. Observado a 2026-08-25.
    param([int]$TimeoutSeconds = 420)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$($script:BaseUrl)/health" -TimeoutSec 8 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)

    return $up
}

<#
.SYNOPSIS
    Credenciais de arranque do ambiente.
.DESCRIPTION
    Local: do .env, que nunca e versionado.

    Remoto: de variaveis de ambiente, que quem chama preenche a partir do seu
    gestor de segredos. Nunca ficam em ficheiro.
#>
function Get-RivoCredentials {
    if ($env:RIVO_ADMIN_EMAIL -and $env:RIVO_ADMIN_PASSWORD) {
        return @{
            BOOTSTRAP_ADMIN_EMAIL      = $env:RIVO_ADMIN_EMAIL
            BOOTSTRAP_ADMIN_PASSWORD   = $env:RIVO_ADMIN_PASSWORD
            BOOTSTRAP_DECIDER_EMAIL    = $env:RIVO_DECIDER_EMAIL
            BOOTSTRAP_DECIDER_PASSWORD = $env:RIVO_DECIDER_PASSWORD
        }
    }

    if (-not (Test-Path ".env")) {
        throw "Sem .env e sem RIVO_ADMIN_EMAIL/RIVO_ADMIN_PASSWORD - nao ha credenciais para autenticar."
    }

    $valores = @{}
    Get-Content ".env" | Where-Object { $_ -match "=" -and $_ -notmatch "^\s*#" } | ForEach-Object {
        $partes = $_ -split "=", 2
        # O .env escrito em Windows pode trazer BOM na primeira chave.
        $valores[$partes[0].Trim().TrimStart([char]0xFEFF)] = $partes[1].Trim()
    }

    return $valores
}

<#
.SYNOPSIS
    Descreve o ambiente, para o cabecalho das suites.
#>
function Get-RivoDescricao {
    if (Test-RivoLocal) { return "local (Docker) em $($script:BaseUrl)" }
    return "remoto em $($script:BaseUrl)"
}

<#
.SYNOPSIS
    Garante zero politicas activas de um tipo de processo, tolerando o K20.
.DESCRIPTION
    K20 (known-issues.md): `POST /approval/policies/{id}/deactivation` falha
    por vezes com 404 contra uma politica que a listagem, momentos antes,
    mostrava activa - sem causa de codigo confirmada em quatro investigacoes.
    Uma tentativa isolada que engole o erro (como havia antes em cada suite)
    tolera a falha da propria suite, mas deixa a politica activa para tras -
    e isso e o que fez `verify-approval.ps1` rebentar sem produzir nenhum
    caso: a submissao seguinte encontrou duas politicas igualmente
    especificas e recusou por ambiguidade (ADR-034 a funcionar
    correctamente perante duplicacao real).

    Esta funcao repete a desactivacao ate a base confirmar zero activas -
    a confirmacao e por SQL de proposito, e nao pela API: se a API negar o
    que a base ja tem, e o proprio K20 a impedir tambem a confirmacao, nao
    so a limpeza.
.PARAMETER ProcessType
    O `processType` da politica a limpar (ex.: "payroll.payroll_run").
.PARAMETER Headers
    Cabecalhos de autorizacao com `approval.policies.write`.
#>
function Clear-RivoApprovalPolicies {
    param(
        [Parameter(Mandatory)][string]$ProcessType,
        [Parameter(Mandatory)][hashtable]$Headers,
        [int]$MaxAttempts = 5
    )

    for ($tentativa = 1; $tentativa -le $MaxAttempts; $tentativa++) {
        $activas = @(Invoke-RestMethod "$($script:BaseUrl)/approval/policies" -Headers $Headers) |
            Where-Object { $_.processType -eq $ProcessType -and $_.isActive }

        if ($activas.Count -eq 0) { return }

        foreach ($politica in $activas) {
            try {
                Invoke-RestMethod "$($script:BaseUrl)/approval/policies/$($politica.policyId)/deactivation" `
                    -Method Post -Headers $Headers | Out-Null
            }
            catch {
                # K20: tentativa seguinte trata.
            }
        }

        Start-Sleep -Milliseconds 500
    }

    $restantes = Invoke-RivoSql "select count(*) from approval.policy where process_type = '$ProcessType' and is_active = 1"
    if ($restantes -ne "0") {
        throw "Nao foi possivel garantir zero politicas activas de '$ProcessType' apos $MaxAttempts tentativas (K20) - $restantes continuam activas."
    }
}

<#
.SYNOPSIS
Cria um Colaborador **ligado a uma conta**, e devolve os cabeçalhos dessa conta.

.DESCRIPTION
Existe desde 2026-09-04, com o ADR-050.

Até essa data, uma suite que precisasse de aprovar alguma coisa autenticava-se
como `Admin` e indicava o identificador do aprovador no corpo do pedido. O
servidor aceitava — e era essa a falha que o ADR-050 fechou: quem decide passou
a ser resolvido a partir da conta autenticada, e o corpo já não tem por onde o
declarar.

Consequência para as suites: **quem decide tem de ser uma conta ligada a um
Colaborador.**

A ligação faz-se em três passos desde o ADR-054: registar a conta, admitir o
colaborador, e ligar os dois com `POST /hr/employees/{id}/account`. Eram dois
até 2026-09-05, porque a admissão aceitava `userId` — e era essa aceitação que
deixava o perfil HR criar vínculos sem ter `hr.employees.link_account`.

⚠ **A ligação exige `AdminHeaders`, e não `HeadersDeAdmissao`.** A permissão
está fora do perfil HR de propósito: uma suite que se autentique como RH admite
o colaborador mas não o liga. É por isso que os dois conjuntos de cabeçalhos são
parâmetros separados.

.OUTPUTS
Hashtable com `EmployeeId`, `UserId`, `Email` e `Headers`.
#>
function New-RivoColaboradorComConta {
    param(
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)][string]$Nome,
        [Parameter(Mandatory)][hashtable]$AdminHeaders,
        [hashtable]$HeadersDeAdmissao,
        [string]$Perfil = "Admin",
        [string]$Password = "Rivo!Aprovador2026",
        [string]$DepartmentId
    )

    $base = $script:BaseUrl
    if (-not $HeadersDeAdmissao) { $HeadersDeAdmissao = $AdminHeaders }

    $corpo = @{ email = $Email; password = $Password } | ConvertTo-Json
    $userId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $corpo `
        -ContentType "application/json").userId

    Invoke-RestMethod "$base/identity/users/$userId/roles" -Method Post `
        -Body (@{ profile = $Perfil } | ConvertTo-Json) -ContentType "application/json" `
        -Headers $AdminHeaders | Out-Null

    $admissao = @{ fullName = $Nome }
    if ($DepartmentId) { $admissao.departmentId = $DepartmentId }

    $employeeId = (Invoke-RestMethod "$base/hr/employees" -Method Post `
        -Body ($admissao | ConvertTo-Json) -ContentType "application/json" `
        -Headers $HeadersDeAdmissao).employeeId

    # Passo proprio desde o ADR-054, e sempre com os cabecalhos de Admin: a
    # permissao de ligar fica fora do perfil HR, e quem admite pode nao poder
    # ligar.
    Invoke-RestMethod "$base/hr/employees/$employeeId/account" -Method Post `
        -Body (@{ userId = $userId } | ConvertTo-Json) -ContentType "application/json" `
        -Headers $AdminHeaders | Out-Null

    $token = (Invoke-RestMethod "$base/identity/login" -Method Post `
        -Body $corpo -ContentType "application/json").accessToken

    return @{
        EmployeeId = $employeeId
        UserId     = $userId
        Email      = $Email
        Headers    = @{ Authorization = "Bearer $token" }
    }
}
