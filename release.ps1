<#
  Prepara os arquivos de um release em dist\ e mostra a tag e o comando para publicar.

    .\release.ps1 instalador             -> dist\PbsuLoader.exe,           tag instalador-v<Version do Instalador.csproj>
    .\release.ps1 pbsloadercommonutils   -> dist\PBSLoaderCommonUtils.dll, tag pbsloadercommonutils-v<versão do MelonInfo>
    .\release.ps1 pbsloadercommoninterface -> dist\PBSLoaderCommonInterface.dll, tag pbsloadercommoninterface-v<versão do MelonInfo>

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

Write-Host ''
Write-Host "Arquivo: $arquivo"
Write-Host "SHA-256: $sha"
Write-Host "Tag:     $tag"
Write-Host ''
Write-Host 'Para publicar (GitHub CLI):'
Write-Host "  git tag $tag; git push origin $tag"
Write-Host "  gh release create $tag `"$arquivo`" --title `"$Componente $versao`" --notes `"...`""
Write-Host 'Versão de teste: acrescente --prerelease (só aparece para quem marcar "versões de teste" no instalador).'
