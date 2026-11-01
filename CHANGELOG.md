# Changelog

Cada componente tem versão e release próprios no GitHub, com a tag `<componente>-v<versão>`.

## Instalador 0.2.1 (2026-10-31)

- Uma instância só: abrir de novo (inclusive pelo link do site) encaminha o pedido para a janela que já está aberta, em vez de abrir outra brigando por consultas do GitHub e pelos arquivos do jogo.
- O link `pbsloader://mod/<id>` não atrapalha mais a detecção do jogo: a URL não é mais tratada como pasta do jogo.

## Instalador 0.2.0 (2026-10-31)

- Lê também o catálogo da comunidade (`pbs-loader-catalogo`, publicado no GitHub Pages). Entram todos os tipos: plugin como `.dll` pelo GitHub; ônibus, mapa e outros como `.zip` — release do GitHub ou origem externa por https. `PBSU_CATALOGO_COMUNIDADE` troca a fonte para testes.
- Um mod da comunidade nunca é obrigatório nem vem marcado, não pode usar o id nem o nome de arquivo de outro mod, e sai da lista se faltar alguma dependência.
- Antes de instalar pela primeira vez um mod da comunidade de autor não confiável, a confirmação avisa que ninguém revisou o mod.
- Mod da comunidade de release do GitHub sem SHA-256 não é baixado. Origem externa não publica SHA-256: o hash calculado na primeira instalação passa a ser a referência, e o instalador avisa se o arquivo mudar fora dele.
- Registra o protocolo `pbsloader://` (HKCU, sem administração) a cada abertura: o link `pbsloader://mod/<id>` do site abre o instalador com o mod — e as dependências — marcado.
- Sem rede para o catálogo base, não tenta baixar o da comunidade.

## Instalador 0.1.0 (2026-10-30)

Primeira versão de teste.

- Instala o MelonLoader 0.7.3 a partir do release oficial e confere o SHA-256 do zip.
- Instala e atualiza a PBSLoaderCommonUtils, a PBSLoaderCommonInterface e os mods do catálogo a partir dos releases no GitHub. O SHA-256 de cada arquivo é conferido com o que o GitHub publica.
- Desinstala só o que ele mesmo instalou. A lista fica em `UserData/pbsu-loader/instalado.json`, e um arquivo trocado à mão aparece como *Diferente do release*.
- A opção *Incluir versões de teste* mostra os pré-lançamentos dos mods.
- Roda sem janela com `PbsuLoader.exe --silencioso ["pasta do jogo"] [--beta] [--dev]`, com log em `UserData/pbsu-loader/instalador.log`.
- O modo desenvolvedor usa os builds dos seus repositórios no lugar dos releases.
- Grava os hashes dos scripts de ônibus e mapa revisados pela comunidade (`scriptsConfiaveis` do catálogo) em `%LOCALAPPDATA%\PBSLoaderCommonUtils\confiaveis.txt`, fora da pasta do jogo.
- Os caminhos lidos do `instalado.json` e os nomes de arquivo do catálogo precisam ficar dentro da pasta do jogo; o que sair dela é ignorado.

O `PbsuLoader.exe` não tem assinatura digital, então o SmartScreen pode avisar na primeira vez. Ainda não foram testados numa instalação real a autoatualização do instalador e a detecção do jogo pela Steam.

## PBSLoaderCommonUtils 2.0.0 (2026-10-30)

Primeira versão publicada. Testada no jogo versão 307 com o MelonLoader 0.7.3.

- SDK dos mods, com uma área por namespace: `Veiculos`, `Mapas`, `Linhas`, `Mundo`, `Partida`, `Midia`, `Simulacao`, `Entrada`, `Interface` e `Conteudo`.
- Leitura do veículo do jogador (motor, câmbio, pedais, luzes, portas, rodas, espelhos e suspensão), do mapa e das paradas, da linha e dos passageiros, da hora, do clima e do tráfego, com os semáforos.
- `Comandos` aciona a carroceria pelos mesmos métodos dos botões do jogo, e `Conducao` deixa um mod dirigir.
- Ganchos no jogo sem patch Harmony próprio: `DepoisDaFisica`, `AntesDosFreios`, `DepoisDaRodaVisual`, `DepoisDoSomDoMotor` e `PortaPedida`.
- `Dados` troca valores entre mods na memória, e `Salvos` guarda dados entre sessões, por mod, ônibus ou mapa.
- `Opcoes` lê e grava as opções do jogo, e `ControlesDoJogo` lê os controles com o mapeamento do jogador.
- Scripts de conteúdo: um ônibus ou mapa pode trazer uma DLL própria, que só roda com a permissão do jogador, gravada por SHA-256.
- Numa versão do jogo que não foi testada, o SDK entra em modo seguro: continua lendo o jogo, mas não escreve nele.
- Simulação de ar comprimido com compressor, governador e purga, freio a ar e freio de molas. Abaixo de 4 bar as portas não abrem.
- Atalhos de teclado com detecção de conflito, um dono só para o cursor e suspensão do teclado do jogo enquanto um painel precisa das teclas.
- O release traz o `PBSLoaderCommonUtils.api.json`, de onde sai a referência da API no site da documentação.

Esta biblioteca se chamava ProtonComum nas versões internas. Se ainda houver um `ProtonComum.dll` na pasta `Mods`, apague: com os dois, os patches rodam em dobro.

## PBSLoaderCommonInterface 0.4.0 (2026-10-30)

Primeira versão publicada.

- Menus, telas de seleção e formulários redesenhados. Cada botão continua chamando o original do jogo, e Ctrl+Alt+F2 volta para a interface original.
- Prévia 3D do ônibus nas telas de veículo e de pintura, já com a pintura aplicada.
- Menu lateral na partida, aberto com a tecla crase.
- Menu **Mods** com as opções de todos os mods, a página **Atalhos**, onde dá para trocar as teclas, e a página **Scripts de ônibus e mapa**.
- Painel de condução no canto inferior direito: velocidade, marcha, rotação e as luzes do painel.
- Painel da carroceria na borda direita, só com o que o ônibus tem: luzes, portas, suspensão, ar-condicionado, retarder, janelas e limpadores.
- Avisos e falhas do veículo na tela.
- Para outros mods: `PainelMods`, `Paineis` e `Hud`.
- O release traz o `PBSLoaderCommonInterface.api.json` para a referência da API.
