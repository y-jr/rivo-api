# Corre todas as suites de verificação, por ordem.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml down -v
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-all.ps1
#
# Existe porque várias suites reiniciam containers para verificar persistência.
# Corridas em cadeia sem pausa, essas reinicializações acumulam-se e a suite
# seguinte começa contra uma API ainda a subir — falhas que não são defeitos da
# aplicação. Este ficheiro espera que a stack assente entre suites.

$ErrorActionPreference = "Continue"
$base = "http://localhost:5080"

# A ordem segue as dependências: `verify-finance` monta os seus pré-requisitos
# pelas rotas de `fiscal` e `commercial`, e corre depois delas para que uma
# falha apareça na suite do módulo que a causou, e não na de quem o consome.
# `verify-procurement` fecha, pela mesma razão: consome `hr` e `approval`.
$suites = @(
    "verify-bootstrap",
    "verify-authorization",
    "verify-audit",
    "verify-hr",

    # Camada de composicao (ADR-041/ADR-042), nao modulo -- so precisa de
    # `identity` e `hr` de pe. Cria o seu proprio colaborador isolado
    # ($stamp), por isso nao depende de estado deixado por `verify-hr`.
    "verify-employee-portal",

    "verify-documents",
    "verify-notifications",
    "verify-fiscal",
    "verify-commercial",
    "verify-finance",
    "verify-payables",
    "verify-ledger",

    # Os quatro esqueletos (project-state.md) nao dependem uns dos outros nem
    # de `hr`/`approval` -- excepto `payroll`, colocado a seguir, junto de
    # `procurement`, pela mesma razao.
    "verify-projects",
    "verify-inventory",
    "verify-fleet",

    # `payroll` e `procurement` fecham: ambos montam o seu cenario por `hr` e
    # por `approval`, e ambos terminam a desactivar pela rota a politica que
    # criaram -- o caso conhecido de K20 (known-issues.md). `verify-approval`
    # corre logo a seguir a `verify-payroll`, mesmo veiculo (folha submetida)
    # para exercitar o cancelamento de `approval` que nenhuma suite cobria
    # (K18, "Proximos passos" #7 em project-state.md).
    #
    # `verify-payroll` tambem depende de `verify-fiscal` (2026-08-30): e la
    # que o INSS e a tabela de escaloes de IRT sao semeados com os codigos e
    # datas reais que o motor de calculo consome -- mesma dependencia de
    # `verify-payables` -> `verify-ledger`, so que `fiscal` ja corre bem
    # antes de `payroll` nesta lista, por isso nao precisou de se mover.
    "verify-payroll",
    "verify-approval",

    # Camada de composicao (ADR-041), nao modulo -- so precisa de `identity`
    # e `approval` de pe, o que e sempre verdade a esta altura. Cria a sua
    # propria politica isolada (processType unico por corrida), por isso
    # nao depende de estado deixado por nenhuma suite anterior.
    "verify-settings",

    # Terceira camada de composicao -- factura e regista despesa numa moeda
    # propria (ZZZ) que nenhuma outra suite usa, por isso os totais saem
    # exactos em vez de "pelo menos" mesmo correndo depois de
    # verify-finance/verify-payables.
    "verify-dashboard",

    "verify-procurement"
)

function Wait-ForApi {
    # 420 s e nao 180: **a paciencia deste script nao pode ser menor que a da
    # aplicacao.** A API espera ate `Database__StartupTimeoutSeconds` pela base
    # de dados, e em desenvolvimento isso sao 420 s — um `docker compose
    # restart` nao respeita `depends_on`, por isso o SQL Server reinicia com ela
    # e a recuperacao da base leva o tempo que leva.
    #
    # Com 180 s aqui, uma suite que reinicia a stack no ultimo caso devolvia o
    # controlo antes de a API voltar, e a suite **seguinte** falhava inteira com
    # "An error occurred while sending the request" — dezenas de falhas que nao
    # sao defeitos da aplicacao e apontam para o modulo errado. Observado a
    # 2026-08-27, com `verify-procurement` a falhar 40 casos depois de
    # `verify-ledger` reiniciar a stack.
    #
    # O mesmo numero ja estava em `Wait-RivoApi` (_ambiente.ps1); ficou por
    # alinhar aqui, e a divergencia entre os dois e que criou o defeito.
    param([int]$TimeoutSeconds = 420)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)

    return $up
}

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# Interpretador para as suites individuais.
#
# `pwsh` (PowerShell 7+) e o preferido e o unico que existe em Linux, onde
# este ficheiro corre no CI. Em Windows sem pwsh instalado, recorre-se ao
# `powershell` 5.1 que vem com o sistema.
#
# A preferencia nao e estetica: `verify-authorization` usa `Join-String`, que
# so existe a partir do PowerShell 6.2. Sob o 5.1 essa linha falha — mas so no
# caminho de erro, por isso o problema so aparece quando uma verificacao ja
# esta a falhar, que e o pior momento possivel para descobrir.
$shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { "pwsh" }
         elseif (Get-Command powershell -ErrorAction SilentlyContinue) { "powershell" }
         else { $null }

if (-not $shell) {
    Write-Host "Nao foi encontrado nem 'pwsh' nem 'powershell'." -ForegroundColor Red
    exit 1
}

if ($shell -eq "powershell") {
    Write-Host "Aviso: a correr sob Windows PowerShell 5.1. Recomenda-se instalar o PowerShell 7+ ('winget install Microsoft.PowerShell')." -ForegroundColor Yellow
}

if (-not (Wait-ForApi)) {
    Write-Host "API não responde. Arranque a stack antes de correr as suites." -ForegroundColor Red
    exit 1
}

# K20 (known-issues.md): 404 intermitente a desactivar uma politica de
# approval no fim de tres suites, investigado quatro vezes sem causa de
# codigo encontrada. Deliberadamente nao escondido nos casos abaixo -- ficam
# a falhar, para que continuem a assinalar o problema a quem correr a suite
# a mao. Aqui, cada texto (nao o numero do caso, que ja mudou varias vezes
# no historico do K20) e a fonte de verdade sobre o que e tolerado: uma
# suite so passa o gate se toda FALHA nela bater com o texto listado.
$k20 = @{
    "verify-ledger"      = "A suite nao deixa politica de BR-8 activa atras de si"
    "verify-payroll"     = "A suite nao deixa politica de payroll activa atras de si"
    "verify-procurement" = "A suite nao deixa politica de procurement activa atras de si"
}

$failed = @()
$knownFailures = @()
$total = 0
$passed = 0

foreach ($suite in $suites) {
    Write-Host ("`n" + ("=" * 60)) -ForegroundColor Cyan
    Write-Host $suite -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan

    # Join-Path e nao "scripts\...": a barra invertida literal nao e separador
    # de caminho fora do Windows, e este runner tambem corre em Linux no CI.
    $suitePath = Join-Path "scripts" "$suite.ps1"
    $output = & $shell -NoProfile -File $suitePath 2>&1
    $exit = $LASTEXITCODE

    # Reconhece uma linha de caso pelo formato que `Test-Case` emite: dois
    # espacos, PASSA ou FALHA em maiusculas, espaco.
    #
    # `-CaseSensitive` e o ancoramento nao sao zelo excessivo — sao a correccao
    # de um defeito. `Select-String` e case-insensitive por omissao, e cinco das
    # seis suites terminam com "Todos os testes passaram.": a palavra "passaram"
    # casava com o padrao "PASSA" e era contada como um caso. O total reportado
    # vinha inflacionado em 5 (dizia 71 onde ha 66).
    $casos = $output | Select-String -CaseSensitive -Pattern '^\s{2}(PASSA|FALHA)\s'

    # Write-Output e nao Write-Host: o Write-Host escreve directamente na
    # consola e nao passa pelo pipeline, o que torna o resultado invisivel a
    # quem redirecciona ou filtra a saida deste script.
    $casos | ForEach-Object { Write-Output $_.Line }

    $total += $casos.Count
    $passed += @($casos | Where-Object { $_.Line -cmatch '^\s{2}PASSA\s' }).Count

    # Uma suite que rebenta antes do primeiro Test-Case nao produz linhas de
    # caso, e o filtro acima engolia-lhe a saida inteira: via-se que falhou,
    # nunca porque. Aqui a saida crua e a unica pista que existe.
    #
    # Nota: os literais de texto deste ficheiro sao ASCII de proposito. Sem BOM,
    # o Windows PowerShell 5.1 le-o como ANSI, e um travessao UTF-8 decodifica
    # com o byte 0x94, que em cp1252 e a aspa curva de fecho - que o PowerShell
    # aceita como delimitador e termina a string a meio.
    if ($casos.Count -eq 0) {
        Write-Output "  (a suite nao produziu nenhum caso - saida crua abaixo)"
        $output | ForEach-Object { Write-Output ("  | " + $_) }
    }

    if ($exit -ne 0) {
        $falhas = @($casos | Where-Object { $_.Line -cmatch '^\s{2}FALHA\s' })
        $toleradoTexto = $k20[$suite]

        # Sem falhas reconhecidas (a suite rebentou antes do primeiro
        # Test-Case) nunca e tolerado -- essa e a saida crua acima, nao um
        # 404 conhecido do K20.
        $todasConhecidas = [bool]$toleradoTexto -and $falhas.Count -gt 0

        foreach ($falha in $falhas) {
            $titulo = $null

            if ($falha.Line -cmatch '^\s{2}FALHA\s+\d+\.\s(.+?)\s{2}--') {
                $titulo = $Matches[1]
            }

            if ($titulo -cne $toleradoTexto) {
                $todasConhecidas = $false
            }
        }

        if ($todasConhecidas) {
            $knownFailures += $suite
            Write-Output "  ($($falhas.Count) falha(s) conhecida(s) -- K20, ver known-issues.md; nao bloqueia o gate)"
        } else {
            $failed += $suite
        }
    }

    # Deixa a stack assentar: a suite anterior pode ter reiniciado containers.
    if (-not (Wait-ForApi)) {
        Write-Host "API não recuperou depois de $suite." -ForegroundColor Red
        $failed += "$suite (recuperação)"
        break
    }
}

Write-Output ("`n" + ("=" * 60))
Write-Output "$passed de $total casos passaram."

if ($knownFailures.Count -gt 0) {
    Write-Output ("Falhas conhecidas, toleradas (K20): " + ($knownFailures -join ", "))
}

if ($failed.Count -gt 0) {
    Write-Output ("Suites com falhas: " + ($failed -join ", "))
    exit 1
}

Write-Output "Todas as suites passaram (fora das falhas conhecidas acima, se houver)."
exit 0
