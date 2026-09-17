<#
.SYNOPSIS
    Varre a potência de transmissão de um Rodinbell D100 e mostra em quais níveis houve leitura de
    EPCs, e quantas.

.DESCRIPTION
    Para cada potência entre -Low e -High, aplica a potência, roda inventário por -Dwell segundos e
    conta leituras e EPCs distintos. Usa a biblioteca Rodinbell.D100 pela API pública, então os EPCs
    são decodificados, não frames contados.

    A faixa aceita pelo D100 é 18 a 26 dBm: fora dela o leitor recusa o comando com status 0x48.

.PARAMETER Port
    A porta serial do leitor, por exemplo COM4. Obrigatório.

.PARAMETER Dwell
    Segundos de inventário por potência. Padrão 1.

    Com 1 segundo saem cerca de 15 leituras por nível num arranjo bom. Isso basta para responder
    "leu ou não leu", mas é pouco para contar etiquetas marginais: uma etiqueta que aparece em
    metade dos ciclos pode não aparecer em nenhuma amostra. Para isso, use 5 ou 10.

.PARAMETER Runs
    Quantas vezes repetir a varredura inteira. Padrão 1.

    Vale mais que dwell longo quando a dúvida é se um resultado é real ou ruído: um limiar de
    potência verdadeiro cai no mesmo lugar em todas as rodadas, uma etiqueta marginal muda de lugar.

.EXAMPLE
    .\Test-D100Power.ps1 -Port COM4

.EXAMPLE
    .\Test-D100Power.ps1 -Port COM6 -Dwell 5 -Runs 3
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Port,

    [ValidateRange(18, 26)]
    [int] $Low = 18,

    [ValidateRange(18, 26)]
    [int] $High = 26,

    [ValidateRange(0.2, 60)]
    [double] $Dwell = 1.0,

    [ValidateRange(1, 20)]
    [int] $Runs = 1,

    # Republica antes de rodar. Use depois de mexer no código do exemplo.
    [switch] $Rebuild
)

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'examples\D100.PowerSweep\D100.PowerSweep.csproj'
$outDir  = Join-Path $repo 'artifacts\d100-power-sweep'
$exe     = Join-Path $outDir 'D100.PowerSweep.exe'

# Publica uma vez e reutiliza o binário. Rodar direto do binário evita o custo de build a cada
# varredura, que com dwell de 1s chega a dominar o tempo total.
if (-not (Test-Path $exe) -or $Rebuild) {
    Write-Host 'Publicando D100.PowerSweep...' -ForegroundColor DarkGray
    dotnet publish $project -c Release -o $outDir --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'A publicação falhou.' }
}

if ($Low -gt $High) {
    throw "-Low ($Low) é maior que -High ($High)."
}

# Confere a porta antes de abrir, para o erro dizer o que existe em vez de vir do driver serial.
$available = [System.IO.Ports.SerialPort]::GetPortNames()
if ($available -notcontains $Port) {
    Write-Warning "A porta $Port não está na lista do sistema."
    if ($available) {
        Write-Host "Portas disponíveis: $($available -join ', ')"
    }
    else {
        Write-Host 'Nenhuma porta serial disponível. O leitor está conectado?'
    }

    throw "Porta $Port indisponível."
}

# O executável faz o parse com InvariantCulture. Este host é pt-BR, onde "$Dwell" vira "1,0" e seria
# lido como dez - então o número é formatado explicitamente em invariante.
$dwellArg = $Dwell.ToString([System.Globalization.CultureInfo]::InvariantCulture)

Write-Host ""
Write-Host "D100 em $Port  |  $Low-$High dBm  |  dwell ${dwellArg}s  |  $Runs rodada(s)" -ForegroundColor Cyan

for ($run = 1; $run -le $Runs; $run++) {
    if ($Runs -gt 1) {
        Write-Host ""
        Write-Host "--- rodada $run de $Runs ---" -ForegroundColor DarkGray
    }

    & $exe $Port $Low $High $dwellArg
    if ($LASTEXITCODE -ne 0) {
        throw "A varredura terminou com código $LASTEXITCODE."
    }
}

Write-Host ""
Write-Host "Como ler o resultado:" -ForegroundColor Cyan
Write-Host "  'leu?' é a coluna confiável com dwell curto."
Write-Host "  Contagens de EPCs distintos variando entre rodadas indicam etiqueta marginal,"
Write-Host "  não limiar de potência - um limiar real cai no mesmo nível em todas as rodadas."
Write-Host "  Zero em todas as potências, com o leitor respondendo, é geometria ou blindagem."
