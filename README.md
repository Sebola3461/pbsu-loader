# PBSU Loader

Modloader para o Proton Bus Simulator Urbano clássico. O repositório tem três coisas:

- o instalador (`PbsuLoader.exe`), que instala o [MelonLoader](https://github.com/LavaGang/MelonLoader) na versão
  testada, instala e atualiza os mods do catálogo e se atualiza sozinho;
- a PBSLoaderCommonUtils, base que todos os mods do catálogo usam: atalhos sem conflito com o jogo, um dono só para o cursor,
  metadados do veículo e simulação de ar comprimido (veja [PBSLoaderCommonUtils/README.md](PBSLoaderCommonUtils/README.md));
- o [catalogo.json](catalogo.json), a lista de mods que o instalador oferece. Cada mod fica no repositório do próprio
  autor e entra no catálogo por pull request.

## Para quem joga

1. Baixe o `PbsuLoader.exe` do [último release](https://github.com/Sebola3461/pbsu-loader/releases) e abra.
2. Ele procura o jogo sozinho. Se não achar, clique em **Escolher...** e aponte o `Proton Bus Simulator.exe`.
3. Marque os mods que você quer e clique em **Aplicar**. O instalador põe o MelonLoader, a PBSLoaderCommonUtils e os mods
   marcados. Se você desmarcar um mod e aplicar, ele é removido.
4. Clique em **Jogar**. Na primeira abertura, o MelonLoader leva alguns minutos preparando os arquivos.

Para atualizar, abra o instalador de novo. Ele mostra o que tem versão nova, e **Aplicar** atualiza. Feche o jogo antes:
com ele aberto, as DLLs estão em uso e não dá para trocá-las.

Quem quiser os pré-lançamentos dos mods marca *Incluir versões de teste*.

Também dá para rodar sem janela: `PbsuLoader.exe --silencioso ["pasta do jogo"] [--beta]` deixa o MelonLoader na versão
certa, instala a PBSLoaderCommonUtils e atualiza o que já está instalado. O log fica em `UserData/pbsu-loader/instalador.log`.

**Desinstalar tudo...** remove os mods do catálogo e o MelonLoader. As configurações (`UserData`) e os mods que você pôs
à mão continuam lá. O instalador só apaga o que ele mesmo instalou: a lista, com o SHA-256 de cada arquivo, está em
`UserData/pbsu-loader/instalado.json`. Um arquivo que você trocou à mão aparece como *Diferente do release*.

### Avisos do Windows

O `PbsuLoader.exe` não tem assinatura digital, então o SmartScreen pode mostrar "O Windows protegeu o computador". Para
abrir mesmo assim, clique em **Mais informações** e depois em **Executar assim mesmo**. Alguns antivírus também acusam o
`version.dll` do MelonLoader, que é por onde ele entra no jogo. É um falso positivo conhecido do MelonLoader.

## Para quem faz mods: entrar no catálogo

1. Publique a DLL do mod como asset de um Release no GitHub. A tag é um prefixo seguido da versão, como `v1.2.0` ou
   `MeuMod-v1.2.0`; com prefixos, vários mods podem dividir um repositório. O asset pode ser uma `.dll`, que vai para a
   pasta `Mods`, ou um `.zip`, que é extraído na pasta do jogo. Use o zip para mods com `UserLibs`, `UserData` ou mais
   de uma DLL. Nenhum caminho dentro do zip pode sair da pasta do jogo.
2. Abra um pull request acrescentando o mod em `mods` no [catalogo.json](catalogo.json):

```json
{
  "id": "MeuMod",
  "nome": "Meu Mod",
  "autor": "seu nome",
  "descricao": "Uma frase sobre o que o mod faz.",
  "estado": "experimental",
  "repo": "usuario/repositorio",
  "tag": "MeuMod-v",
  "arquivo": "MeuMod.dll",
  "dependencias": ["PBSLoaderCommonUtils"],
  "versoesDoJogo": ["307"]
}
```

| Campo | |
|---|---|
| `id` | Nome único, igual ao do `MelonInfo` do mod. |
| `estado` | `estável`, `testado` ou `experimental`, com o mesmo sentido do repositório de mods. |
| `tag` | Prefixo da tag. O resto da tag é a versão. Padrão: `v`. |
| `dependencias` | `id`s de outros mods do catálogo. Quem marca o mod no instalador leva as dependências junto. |
| `versoesDoJogo` | Versões do jogo em que o mod foi testado (nomes de `jogo.versoes`). Em outra versão, o instalador avisa. |
| `padrao` | `true` deixa o mod marcado na primeira instalação. |
| `obrigatorio` | Reservado à PBSLoaderCommonUtils. |
| `pagina` | Link mostrado no instalador. Padrão: o repositório. |

O instalador baixa sempre o release mais novo cuja tag comece pelo prefixo, e confere o SHA-256 que o GitHub publica
para cada asset. Pré-lançamentos só aparecem para quem marcou *versões de teste*.

Para testar a sua entrada antes do pull request, aponte o instalador para uma cópia local:
`set PBSU_CATALOGO=C:\caminho\catalogo.json` e abra o `PbsuLoader.exe` no mesmo prompt.

### Usar a PBSLoaderCommonUtils no seu mod

Referencie a `PBSLoaderCommonUtils.dll` com `Private=false`, seja a da pasta `Mods` do jogo (já instalada pelo instalador), seja
o projeto deste repositório. A API está em [PBSLoaderCommonUtils/README.md](PBSLoaderCommonUtils/README.md).

## Versão nova do jogo

Quando o jogo atualiza, o hash do `GameAssembly.dll` muda e o instalador avisa que não conhece a versão. Depois de
testar os mods na versão nova, acrescente-a em `jogo.versoes` (o hash aparece no log do `--silencioso`) e nos
`versoesDoJogo` dos mods que funcionaram.

Para trocar a versão do MelonLoader, mude `melonloader` no catálogo: `versao`, a `url` do `MelonLoader.x64.zip` e o
`sha256` dele. Faça isso só depois de testar todos os mods do catálogo na versão nova.

## Compilar

Você precisa do [.NET SDK 8](https://dotnet.microsoft.com/download).

O instalador compila sem o jogo: `dotnet build Instalador -c Release`.

A PBSLoaderCommonUtils precisa do jogo com o MelonLoader instalado e aberto uma vez, porque as referências vêm de
`MelonLoader/Il2CppAssemblies`. Diga onde o jogo está com um `Local.props` (copie o `Local.props.exemplo`), com a
variável `PBS_DIR` ou com `-p:GameDir="..."`. O build copia a DLL para `Mods`; para só compilar, use `-p:Instalar=false`.

### Publicar um release

```powershell
.\release.ps1 instalador             # dist\PbsuLoader.exe, tag instalador-v<Version do Instalador.csproj>
.\release.ps1 pbsloadercommonutils   # dist\PBSLoaderCommonUtils.dll, tag pbsloadercommonutils-v<versão do MelonInfo>
```

O script compila, calcula o SHA-256 e mostra os comandos `git tag` e `gh release create`. Cada componente tem o próprio
release. Suba a versão antes de rodar: `Version` no `Instalador.csproj` ou o `MelonInfo` da PBSLoaderCommonUtils.

## Licença

Código sob [GPL-3.0](LICENSE). Sons sob CC BY 4.0 (veja [LICENSE-ASSETS.md](LICENSE-ASSETS.md)). O MelonLoader é da
LavaGang, sob Apache-2.0, e o instalador o baixa do release oficial; ele não é distribuído aqui.

Este projeto não é afiliado à MEP / Proton Bus Simulator e não contém arquivos do jogo.
