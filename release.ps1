<#
  Prepara os arquivos de um release em dist\ e mostra a tag e o comando para publicar.

    .\release.ps1 instalador             -> dist\PbsuLoader.exe,           tag instalador-v<Version do Instalador.csproj>
    .\release.ps1 pbsloadercommonutils   -> dist\PBSLoaderCommonUtils.dll + .api.json, tag pbsloadercommonutils-v<versão do MelonInfo>
    .\release.ps1 pbsloadercommoninterface -> dist\PBSLoaderCommonInterface.dll + .api.json, tag pbsloadercommoninterface-v<versão do MelonInfo>

  Cada componente tem o próprio release no GitHub; o instalador acha o mais novo pelo prefixo da tag (ver catalogo.json).
  A PBSLoaderCommonUtils e a PBSLoaderCommonInterface são compiladas contra o jogo da sua máquina (Local.props), então o release sai daqui, não de um CI.
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('instalador', 'pbsloadercommonutils', 'pbsloadercommoninterface')][string]$Componente,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot

if ($Componente -eq 'instalador') {
    $proj = Join-Path $raiz 'Instalador\Instalador.csproj'
    $versao = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $saida = Join-Path $raiz 'Instalador\bin\Release\net48\PbsuLoader.exe'
} else {
    $nome = if ($Componente -eq 'pbsloadercommonutils') { 'PBSLoaderCommonUtils' } else { 'PBSLoaderCommonInterface' }
    $proj = Join-Path $raiz "$nome\$nome.csproj"
    $fonte = Get-Content (Join-Path $raiz "$nome\$nome.cs") -Raw
    # A CommonUtils usa a constante Sdk.Versao no MelonInfo.
    if ($fonte -match 'MelonInfo\([^,]+,\s*"[^"]+",\s*"([^"]+)"') { $versao = $Matches[1] }
    elseif ((Get-Content (Join-Path $raiz "$nome\Sdk.cs") -Raw -ErrorAction SilentlyContinue) -match 'Versao\s*=\s*"([^"]+)"') { $versao = $Matches[1] }
    else { throw "Versão não encontrada no MelonInfo da $nome." }
    $saida = Join-Path $raiz "$nome\bin\Release\net6.0\$nome.dll"
}
$tag = "$Componente-v$versao"

if (git -C $raiz tag --list $tag) { throw "A tag $tag já existe. Suba a versão antes de lançar." }

& $Dotnet build $proj -c Release -p:Instalar=false --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'O build falhou.' }

$dist = Join-Path $raiz 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item $saida $dist -Force
$arquivo = Join-Path $dist (Split-Path $saida -Leaf)
$sha = (Get-FileHash $arquivo -Algorithm SHA256).Hash.ToLower()

# As bibliotecas levam junto o <nome>.api.json, de onde o site pbs-loader-docs gera a referência da API.
$anexos = "`"$arquivo`""
if ($Componente -ne 'instalador') {
    $pastas = (& $Dotnet msbuild $proj -getProperty:MLDir -getProperty:Il2CppDir) | ConvertFrom-Json
    $referencias = @("$($pastas.Properties.MLDir)\net6", $pastas.Properties.Il2CppDir, (Join-Path $raiz 'PBSLoaderCommonUtils\bin\Release\net6.0')) -join ';'
    $api = Join-Path $dist "$nome.api.json"
    & $Dotnet run --project (Join-Path $raiz 'tools\ApiExtractor') -c Release -- `
        --dll $saida --xml ([IO.Path]::ChangeExtension($saida, '.xml')) --references $referencias --version $versao --output $api --strict
    if ($LASTEXITCODE -ne 0) { throw 'A extração da API falhou.' }
    $anexos += " `"$api`""
}

Write-Host ''
Write-Host "Arquivo: $arquivo"
Write-Host "SHA-256: $sha"
Write-Host "Tag:     $tag"
Write-Host ''
Write-Host 'Para publicar (GitHub CLI):'
Write-Host "  git tag $tag; git push origin $tag"
Write-Host "  gh release create $tag $anexos --title `"$Componente $versao`" --notes `"...`""
Write-Host 'Versão de teste: acrescente --prerelease (só aparece para quem marcar "versões de teste" no instalador).'
if ($Componente -ne 'instalador') {
    Write-Host 'Depois, para o site da documentação pegar a API nova:'
    Write-Host '  gh workflow run deploy.yml -R Sebola3461/pbs-loader-docs'
}
