#Requires -Version 5.1
<#
.SYNOPSIS
    在本地跑一遍 CI 的闸门。镜像 .github/workflows/ci.yml 与 docs.yml。

.DESCRIPTION
    在推之前就把 CI 会报的问题问出来——同样的失败,推前发现只值一次编辑,推后要搭上一次提交、
    一次推送和一趟 run。所以这个脚本的目标不是"跑点测试",而是**和 CI 判定一致**:
    同样的 -warnaserror、同样的 SqlServer 子集过滤、同样的连接串与端口。
    判定不一致的本地脚本比没有更糟,它会给你一个假的绿灯。

    与 CI 的三处**刻意**不同,每处都有理由:
      * 并行度:CI 给 --max-threads 8(2 核 runner 上这批测试在等数据库往返,不吃 CPU);
        本机核多,按 CLAUDE.md 记的,保持 xUnit 默认的核数。
      * npm ci:CI 每次全新安装;本地只在 node_modules 缺失时装,否则用现成的(-Clean 强制重装)。
      * codeql / dependency-review:要 GitHub 自己的分析与公告库,本地没有等价物,这里也不假装有。

.PARAMETER Stage
    要跑的闸门。默认跑不需要 Docker 的那些。
    backend / web / web-e2e / docs / template / audit / docker-smoke,或 all。

.PARAMETER Dialect
    backend 闸门要跑的方言。sqlite 不需要 Docker;其余三个会起容器,用完删掉。
    (不叫 -Db:CmdletBinding 给 -Debug 自动加了 db 别名,会撞。)

.PARAMETER FullSqlServer
    跑 SqlServer 全量套件。默认只跑方言敏感子集,与 CI 的 push/PR 一致。
    全量在 CI 上要 40-60 分钟,本机通常快些,但仍然很久。

.PARAMETER Clean
    web 闸门强制 npm ci(而不是复用现有 node_modules)。

.EXAMPLE
    powershell -File scripts\ci-local.ps1
    推之前的日常检查:后端(sqlite)+ 前端 + 文档站 + 模板冒烟 + 依赖公告,不需要 Docker。

.EXAMPLE
    powershell -File scripts\ci-local.ps1 -Stage backend -Dialect mysql,postgres,sqlserver
    动了数据层之后补方言腿(需要 Docker Desktop 在跑)。

.EXAMPLE
    powershell -File scripts\ci-local.ps1 -Stage all -Dialect sqlite,mysql,postgres,sqlserver
    合 main 之前的全量。
#>
[CmdletBinding()]
param(
    [string[]]$Stage = @('backend', 'web', 'docs', 'template', 'audit'),
    [string[]]$Dialect = @('sqlite'),
    [switch]$FullSqlServer,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$allStages = @('backend', 'web', 'web-e2e', 'docs', 'template', 'audit', 'docker-smoke')
$allDialects = @('sqlite', 'mysql', 'postgres', 'sqlserver')

# 这里不用 ValidateSet,是因为 `powershell -File` 把每个参数都当字面字符串传:
# `-Stage docs,audit` 到手是一个 "docs,audit",ValidateSet 当场就拒了。自己拆逗号,
# 两种调用方式(-File 与点调用)才都能用。
function Expand-List {
    param([string[]]$Value, [string[]]$Allowed, [string]$Label)
    $out = @($Value | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $bad = @($out | Where-Object { $Allowed -notcontains $_ -and $_ -ne 'all' })
    if ($bad.Count -gt 0) {
        throw ("$Label 不认识: " + ($bad -join ', ') + "。可选: " + ($Allowed -join ', '))
    }
    return $out
}

$Stage = Expand-List $Stage ($allStages + 'all') '-Stage'
$Dialect = Expand-List $Dialect $allDialects '-Dialect'

if ($Stage -contains 'all') { $Stage = $allStages }

$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# 连接串除端口外与 ci.yml 逐字一致。ConnectionIdleTimeout / Connection Idle Lifetime 不是装饰:
# 每个测试一个库、一个连接池,空闲连接不尽快归还就会把服务端连接数撑爆。
#
# 端口必须动态挑,不能照抄 CI 的默认值。CI 的 runner 是干净的,开发机不是——本机常年
# 装着 SQL Server / MySQL,或者自己的开发容器就占着 3306。抢不到端口时连上去的是**那一个**,
# 而测试会在里面建删两百个数据库。这次就撞上了:本机 MSSQLSERVER 服务占着 1433,
# 整条腿 57 个用例报「用户 'sa' 登录失败」——那还算走运,是假红;
# 要是口令恰好对得上(root/root、postgres/postgres 都是默认值),就是在别人的开发库上跑套件。
$dbSpec = @{
    mysql     = @{ Port = 3306; Env = 'SMART_TEST_MYSQL'; Conn = 'Server=127.0.0.1;Port={0};User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;ConnectionIdleTimeout=15;' }
    sqlserver = @{ Port = 1433; Env = 'SMART_TEST_SQLSERVER'; Conn = 'Server=127.0.0.1,{0};User ID=sa;Password=Smart_Test_Pa55w0rd!;TrustServerCertificate=True;Encrypt=False;' }
    postgres  = @{ Port = 5432; Env = 'SMART_TEST_POSTGRESQL'; Conn = 'Server=127.0.0.1;Port={0};User ID=postgres;Password=postgres;Connection Idle Lifetime=15;' }
}
$dbType = @{ mysql = 'MySql'; sqlserver = 'SqlServer'; postgres = 'PostgreSQL'; sqlite = '' }
# 环境变量名与 ci.yml 一致——postgres 那条是 SMART_TEST_POSTGRESQL,不是容器名的大写
# (环境变量名与连接串模板都在上面的 $dbSpec 里;postgres 那条是 SMART_TEST_POSTGRESQL,不是容器名的大写)

# 方言敏感子集,与 ci.yml 的 filter 逐字一致。与库无关的逻辑由其余三条腿全量覆盖。
# xunit v3 起走 Microsoft.Testing.Platform,过滤是测试程序自己的参数,多个 --filter-class 之间是「或」。
$sqlServerSubset = @(
    '--filter-class', '*CodeFirstNullableUpgradeTests*', '--filter-class', '*ProductionBootstrapTests*',
    '--filter-class', '*SeedUpgradeTests*', '--filter-class', '*SeedIdRangeTests*',
    '--filter-class', '*DataScopeTests*', '--filter-class', '*SoftDeleteAuditTests*',
    '--filter-class', '*DictCrudTests*', '--filter-class', '*UserCrudTests*',
    '--filter-class', '*JobClaimTests*', '--filter-class', '*MultiConfigIdTests*')

$results = New-Object System.Collections.ArrayList
$startedContainers = New-Object System.Collections.ArrayList
# 不判红、但绝不能在长日志里划过去的事。跟着汇总表一起打。
$warnings = New-Object System.Collections.ArrayList

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host ('== ' + $text) -ForegroundColor Cyan
}

# 原生命令的退出码检查。PowerShell 不会因为 exe 返回非零就停,不显式判就是个永远绿的检查。
function Invoke-Native {
    param([string]$File, [string[]]$Arguments, [string]$Cwd)
    $prev = Get-Location
    if ($Cwd) { Set-Location $Cwd }
    try {
        Write-Host ("   > $File " + ($Arguments -join ' ')) -ForegroundColor DarkGray
        & $File @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$File 退出码 $LASTEXITCODE" }
    }
    finally { Set-Location $prev }
}

function Invoke-Gate {
    param([string]$Name, [scriptblock]$Body)
    Write-Head $Name
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $status = 'PASS'
    $note = ''
    try { & $Body }
    catch {
        $status = 'FAIL'
        $note = $_.Exception.Message
        Write-Host ('   ' + $note) -ForegroundColor Red
    }
    $sw.Stop()
    [void]$results.Add([pscustomobject]@{
            Gate     = $Name
            Status   = $status
            Duration = ('{0:mm\:ss}' -f [TimeSpan]::FromSeconds($sw.Elapsed.TotalSeconds))
            Note     = $note
        })
}

# Docker Desktop 启动到一半时 `docker info` 会挂在命名管道上不返回,本地 CI 就此卡死——
# 卡死比判红更糟,它不给你任何信号。所以起独立进程 + 硬超时 + Kill。
# 不用 Start-Job:作业里的 docker.exe 一样会阻塞,而 Stop-Job 收不动一个卡在管道读上的子进程。
# 也不能只看管道存不存在:Desktop 的 Windows 侧服务先起来,那些管道早就在了,Linux 引擎还没就绪。
function Test-DockerUp {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = 'docker'
    $psi.Arguments = 'info'
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    try { $proc = [Diagnostics.Process]::Start($psi) } catch { return $false }
    if (-not $proc.WaitForExit(20000)) {
        try { $proc.Kill(); $proc.WaitForExit() } catch { }
        return $false
    }
    return ($proc.ExitCode -eq 0)
}

function Test-PortOpen([int]$Port) {
    $c = New-Object Net.Sockets.TcpClient
    try {
        if (-not $c.ConnectAsync('127.0.0.1', $Port).Wait(500)) { return $false }
        return $c.Connected
    }
    catch { return $false }
    finally { $c.Dispose() }
}

# 默认端口被占就换一个,并且说出来。悄悄复用一个已经开着的端口是最坏的选择:
# 那台服务多半是开发机自己的,而这套测试会在里面建删两百个数据库。
function Get-FreePort([int]$Preferred, [string]$Label) {
    if (-not (Test-PortOpen $Preferred)) { return $Preferred }
    # 每一项都要自己的括号:PowerShell 里逗号的优先级高于 +,
    # 写成 @($Preferred + 20000, $Preferred + 21000) 会被解析成「int 加数组」,当场抛 op_Addition。
    foreach ($p in @(($Preferred + 20000), ($Preferred + 21000), ($Preferred + 22000))) {
        if ($p -lt 65536 -and -not (Test-PortOpen $p)) {
            Write-Host ("   $Label 的默认端口 $Preferred 已被本机其它服务占用,容器改用 $p") -ForegroundColor Yellow
            [void]$warnings.Add("$Label 没走默认端口 $Preferred(被占),用的是 $p。本机那个服务没被碰。")
            return $p
        }
    }
    throw "${Label}: 默认端口 $Preferred 及备用端口都被占用"
}

function Start-Container {
    param([string]$Name, [string[]]$RunArgs)
    docker rm -f $Name *> $null
    # docker run 把容器 ID 打到 stdout。不接住的话它会顺着函数返回值冒到调用方,
    # 而 Start-DbContainer 是要返回端口号的——那样 $port 会变成一个数组。
    $out = & docker @RunArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | Write-Host
        throw "起 $Name 容器失败"
    }
    [void]$startedContainers.Add($Name)
}

function Wait-Healthy {
    param([string]$Name, [int]$TimeoutSeconds = 300)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $s = docker inspect -f '{{.State.Health.Status}}' $Name 2>$null
        if ($s -eq 'healthy') { Write-Host "   $Name 就绪" -ForegroundColor DarkGray; return }
        Start-Sleep -Seconds 5
    }
    docker logs $Name 2>&1 | Select-Object -Last 40
    throw "$Name 容器没能变healthy"
}

# 起容器,返回它实际映射到的宿主端口(默认端口被占时会换一个)。
function Start-DbContainer([string]$which) {
    $port = Get-FreePort $dbSpec[$which].Port $which
    switch ($which) {
        'mysql' {
            # 默认 max_connections=151 不够几百个测试库的连接池
            Start-Container 'db-mysql' @('run', '-d', '--name', 'db-mysql', '-p', "${port}:3306",
                '-e', 'MYSQL_ROOT_PASSWORD=root',
                '--health-cmd', 'mysqladmin ping -h 127.0.0.1 -proot',
                '--health-interval', '5s', '--health-timeout', '5s', '--health-retries', '30',
                'mysql:8.0', '--max-connections=2000')
            Wait-Healthy 'db-mysql'
        }
        'postgres' {
            Start-Container 'db-postgres' @('run', '-d', '--name', 'db-postgres', '-p', "${port}:5432",
                '-e', 'POSTGRES_PASSWORD=postgres',
                '--health-cmd', 'pg_isready -U postgres',
                '--health-interval', '5s', '--health-timeout', '5s', '--health-retries', '30',
                'postgres:16')
            Wait-Healthy 'db-postgres'
        }
        'sqlserver' {
            Start-Container 'db-sqlserver' @('run', '-d', '--name', 'db-sqlserver', '-p', "${port}:1433",
                '-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_PID=Developer', '-e', 'MSSQL_SA_PASSWORD=Smart_Test_Pa55w0rd!',
                '--health-cmd', "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Smart_Test_Pa55w0rd!' -Q 'SELECT 1' -b",
                '--health-interval', '10s', '--health-timeout', '10s', '--health-retries', '30', '--health-start-period', '30s',
                'mcr.microsoft.com/mssql/server:2022-latest')
            Wait-Healthy 'db-sqlserver' 420
            # 测试库都是从 model 复制出来的,把它调成"用完即弃"该有的样子(与 ci.yml 同)
            docker exec db-sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Smart_Test_Pa55w0rd!' -b `
                -Q "ALTER DATABASE [model] SET RECOVERY SIMPLE; ALTER DATABASE [model] SET DELAYED_DURABILITY = FORCED;" *> $null
            if ($LASTEXITCODE -ne 0) { throw 'model 库调优失败' }
        }
    }
    return $port
}

# ── 闸门 ────────────────────────────────────────────────────────────────────────

function Gate-Backend {
    $needDocker = @($Dialect | Where-Object { $_ -ne 'sqlite' })
    $dockerUp = $false
    if ($needDocker.Count -gt 0) {
        $dockerUp = Test-DockerUp
        if (-not $dockerUp) { throw ("要跑 " + ($needDocker -join ',') + " 得先起 Docker Desktop") }
    }

    # Redis 契约测试:缺连接串会静默跳过,而 CLAUDE.md 明确记着「静默跳过 = 假绿」。
    # 顺序是:已有的连接串 > 本机 6379 上已经在跑的(你自己的开发容器就归这一类,顺带避开端口冲突) > 自己起一个。
    if (-not $env:SMART_TEST_REDIS -and (Test-PortOpen 6379)) {
        $env:SMART_TEST_REDIS = 'localhost:6379'
        Write-Host '   6379 上已经有 Redis,直接用' -ForegroundColor DarkGray
    }
    if (-not $dockerUp) { $dockerUp = Test-DockerUp }
    if ($dockerUp -and -not $env:SMART_TEST_REDIS) {
        Start-Container 'ci-redis' @('run', '-d', '--name', 'ci-redis', '-p', '6379:6379',
            '--health-cmd', 'redis-cli ping', '--health-interval', '3s', '--health-retries', '20',
            'redis:7-alpine')
        Wait-Healthy 'ci-redis' 60
        $env:SMART_TEST_REDIS = 'localhost:6379'
    }
    if ($env:SMART_TEST_REDIS) {
        Write-Host ('   Redis 已接入 (' + $env:SMART_TEST_REDIS + '),契约测试是真跑的') -ForegroundColor DarkGray
    }
    else {
        # RedisCacheTests.SkipWithoutRedis() 在本地是静默 return,只有 GITHUB_ACTIONS 下才判红。
        # 所以"跳过"和"跑过"的用例计数一模一样,日志里看不出来——必须由这里说出来。
        Write-Host '   ! Redis 没接入' -ForegroundColor Yellow
        [void]$warnings.Add('Redis 契约测试被静默跳过(用例计数看不出差别)。起 Docker 后重跑,或自己设 SMART_TEST_REDIS。')
    }

    # 零警告基线:可空引用、XML 注释断链等编译警告直接判红
    Invoke-Native 'dotnet' @('build', 'backend/SmartAdmin.slnx', '-c', 'Release', '-warnaserror')

    foreach ($d in $Dialect) {
        Write-Host ''
        Write-Host ("-- backend ($d)") -ForegroundColor Yellow
        if ($d -ne 'sqlite') {
            # 连接串必须用容器实际映射到的端口,不能用默认值——否则可能连到本机自己那台服务上
            $port = Start-DbContainer $d
            Set-Item -Path ('env:' + $dbSpec[$d].Env) -Value ($dbSpec[$d].Conn -f $port)
        }
        $env:SMART_TEST_DBTYPE = $dbType[$d]

        $testArgs = @('test', 'backend/SmartAdmin.slnx', '-c', 'Release', '--no-build')
        if ($d -eq 'sqlserver' -and -not $FullSqlServer) {
            # `--` 之后的都原样交给测试程序(MTP),不再是 dotnet test 自己的选项
            $testArgs += '--'
            $testArgs += $sqlServerSubset
            Write-Host '   (只跑方言敏感子集,要全量加 -FullSqlServer)' -ForegroundColor DarkGray
        }
        try { Invoke-Native 'dotnet' $testArgs }
        finally {
            # 跑完就收:CI 上每条腿是独立 job,本地是顺序跑同一台机器。
            # 三个库全堆着要好几 GB(光 SQL Server 就 2GB+),会把后面几条腿自己拖慢。
            if ($d -ne 'sqlite') { docker rm -f ('db-' + $d) *> $null }
        }
    }
    $env:SMART_TEST_DBTYPE = ''
}

function Gate-Web {
    $web = Join-Path $repo 'web'
    if ($Clean -or -not (Test-Path (Join-Path $web 'node_modules'))) {
        Invoke-Native 'npm' @('ci') $web
    }
    Invoke-Native 'npm' @('run', 'lint') $web
    Invoke-Native 'npm' @('run', 'format:check') $web
    Invoke-Native 'npm' @('test') $web
    Invoke-Native 'npm' @('run', 'build') $web        # 含 vue-tsc --noEmit
}

function Gate-WebE2E {
    $web = Join-Path $repo 'web'
    if (-not (Test-Path (Join-Path $web 'node_modules'))) { Invoke-Native 'npm' @('ci') $web }
    # 与 CI 同:显式注入端口。让配置自己找端口时,抢端口的症状会伪装成「后端起不来」。
    $env:SMART_E2E_API_PORT = '21100'
    $env:SMART_E2E_WEB_PORT = '32100'
    # 后端 webServer 的就绪窗口是 120 秒,而它跑 dotnet run(自带 restore + build)。
    # 先把还原做掉,免得网络时间挤进那扇窗口。
    Invoke-Native 'dotnet' @('restore', 'backend/samples/MinimalHost')
    Invoke-Native 'npx' @('playwright', 'install', 'chromium') $web
    Invoke-Native 'npm' @('run', 'test:e2e') $web
}

function Gate-Docs {
    # agent skill 的三份包装(.claude / .agents / .codex)必须一致:不一致的后果是同一个 skill 在 Codex 里根本看不见
    Invoke-Native 'node' @('scripts/gen-skill-wrappers.mjs', '--check') $repo
    $site = Join-Path $repo 'site'
    if (-not (Test-Path (Join-Path $site 'node_modules'))) { Invoke-Native 'npm' @('ci') $site }
    Invoke-Native 'npm' @('run', 'lint:prose:selftest') $site   # 先自检,免得规则本身坏了还报绿
    Invoke-Native 'npm' @('run', 'lint:prose') $site
    Invoke-Native 'npx' @('vitepress', 'build') $site
}

function Gate-Template {
    Invoke-Native 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $repo 'templates\smoke-test.ps1'))
}

function Gate-Audit {
    Invoke-Native 'dotnet' @('restore', 'backend/SmartAdmin.slnx')
    # `dotnet list package --vulnerable` 找到漏洞照样退出 0,只在正文里写一行。
    # 不判这行就是个永远绿的检查——比没有更糟,它让人以为查过了。
    $out = & dotnet list backend/SmartAdmin.slnx package --vulnerable --include-transitive 2>&1
    $out | Write-Host
    if ($out -match 'has the following vulnerable packages') { throw 'NuGet 依赖存在已知漏洞,见上方清单' }
    # --omit=dev:只审会发出去的那部分。构建工具链的公告拦下来的是发版,不是用户面临的风险。
    Invoke-Native 'npm' @('audit', '--omit=dev', '--audit-level=high') (Join-Path $repo 'web')
    Invoke-Native 'npm' @('audit', '--omit=dev', '--audit-level=high') (Join-Path $repo 'site')
}

function Gate-DockerSmoke {
    if (-not (Test-DockerUp)) { throw 'docker-smoke 要先起 Docker Desktop' }
    # compose 里这三个是强制变量(${VAR:?}),不给就拒绝启动
    $env:SMART_JWT_SECRET = 'ci-local-signing-key-please-keep-32plus-bytes'
    $env:SMART_DB_PASSWORD = 'ci-local-db'
    $env:SMART_ADMIN_PASSWORD = 'Smart@123456'
    try {
        Invoke-Native 'docker' @('compose', 'up', '-d', '--build')
        $ok = $false
        foreach ($i in 1..90) {
            try {
                Invoke-WebRequest -Uri 'http://localhost:8080/health' -UseBasicParsing -TimeoutSec 3 | Out-Null
                $ok = $true; break
            }
            catch { Start-Sleep -Seconds 2 }
        }
        if (-not $ok) { throw '应用一直没变健康' }
        $body = Invoke-WebRequest -Uri 'http://localhost:8080/api/v1/auth/login' -Method Post -UseBasicParsing `
            -ContentType 'application/json' -Body '{"account":"superAdmin","password":"Smart@123456"}'
        if ($body.Content -notmatch 'accessToken') { throw '登录没有返回 accessToken' }
        Write-Host '   首启建表 + 种子 + 签发令牌 通过' -ForegroundColor DarkGray
    }
    finally { docker compose down -v *> $null }
}

# ── 主流程 ──────────────────────────────────────────────────────────────────────

$total = [Diagnostics.Stopwatch]::StartNew()
Write-Host ('SmartAdmin 本地 CI  |  闸门: ' + ($Stage -join ', ') + '  |  方言: ' + ($Dialect -join ', ')) -ForegroundColor Green

try {
    if ($Stage -contains 'backend') { Invoke-Gate 'backend' { Gate-Backend } }
    if ($Stage -contains 'web') { Invoke-Gate 'web' { Gate-Web } }
    if ($Stage -contains 'web-e2e') { Invoke-Gate 'web-e2e' { Gate-WebE2E } }
    if ($Stage -contains 'docs') { Invoke-Gate 'docs' { Gate-Docs } }
    if ($Stage -contains 'template') { Invoke-Gate 'template-smoke' { Gate-Template } }
    if ($Stage -contains 'audit') { Invoke-Gate 'deps-audit' { Gate-Audit } }
    if ($Stage -contains 'docker-smoke') { Invoke-Gate 'docker-smoke' { Gate-DockerSmoke } }
}
finally {
    foreach ($c in $startedContainers) { docker rm -f $c *> $null }
}

$total.Stop()
Write-Host ''
Write-Host ('== 汇总  (' + ('{0:mm\:ss}' -f [TimeSpan]::FromSeconds($total.Elapsed.TotalSeconds)) + ')') -ForegroundColor Cyan
$results | Format-Table Gate, Status, Duration -AutoSize

foreach ($w in $warnings) { Write-Host ('! ' + $w) -ForegroundColor Yellow }
if ($warnings.Count -gt 0) { Write-Host '' }

$failed = @($results | Where-Object { $_.Status -eq 'FAIL' })
if ($failed.Count -gt 0) {
    Write-Host ('红了 ' + $failed.Count + ' 个闸门: ' + (($failed | ForEach-Object { $_.Gate }) -join ', ')) -ForegroundColor Red
    exit 1
}
Write-Host '全绿。' -ForegroundColor Green
exit 0
