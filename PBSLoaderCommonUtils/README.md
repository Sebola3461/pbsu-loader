# PBSLoaderCommonUtils

Base comum dos mods:

- **Atalhos:** um registro único de teclas, editável num lugar só e com detecção de conflitos.
- **Mouse:** um dono só para o cursor. Acabam os mouses presos ou soltos fora de hora.
- **Teclado:** suspende os controles de teclado do jogo enquanto um painel precisa das teclas.
- **Veículo:** metadados do veículo que você dirige: velocidade, pedais, motor, câmbio, luzes, setas, buzina, portas
  e outros.
- **Ar comprimido:** compressor movido pelo motor, reservatório, governador com purga, freio a ar e freio de molas.
  Esta é a única parte que muda o jogo sozinha. Para desligar, use `ArAtivo` nas preferências.

O [instalador do PBSLoader](../README.md) instala a PBSLoaderCommonUtils junto com o MelonLoader. Para instalar à mão, copie
`PBSLoaderCommonUtils.dll` para a pasta `Mods`, junto com os mods que a usam.

## Para quem joga

- Os atalhos de todos os mods ficam na seção `[Atalhos]` do `UserData/MelonPreferences.cfg`, em texto como
  `Ctrl+Alt+F9`, `PageDown` ou `LeftAlt`. Texto vazio deixa a ação sem atalho. Com a PBSLoaderCommonInterface, use a página **Atalhos**
  do menu: clique no atalho e aperte a combinação nova.
- Os atalhos não disparam enquanto você digita num campo de texto. Nesse tempo os controles de teclado do jogo também
  ficam suspensos, para a buzina não tocar enquanto você escreve.
- `UserData/PBSLoaderCommonUtils/teclas.txt` é regravado a cada troca de cena. Ele lista:
  - os controles de teclado do jogo, já com o que você remapeou;
  - as teclas fixas dos scripts do jogo presentes na cena;
  - os atalhos dos mods, com os conflitos.

### Ar comprimido e freio a ar

As opções ficam na categoria `[PBSLoaderCommonUtils]` ("Veículo e ar comprimido" no menu da PBSLoaderCommonInterface).

- **Compressor:** só carrega com o motor ligado, e a vazão acompanha a rotação. Com o reservatório vazio, ele enche em
  cerca de 4 minutos na lenta e 2 minutos a 1500 rpm. O governador corta em 8,5 bar, com o "tssss" da purga do
  secador, e volta a carregar abaixo de 7,0 bar.
- **Freio de serviço:** a pressão nas câmaras segue o pedal com o atraso do ar, e o freio perde força se o reservatório
  estiver baixo. Cada pisada gasta ar (uma pisada até o fundo custa cerca de 0,35 bar), e soltar o pedal faz o chiado.
- **Freio de molas:** o freio de estacionamento é de molas, que só soltam com pressão. Abaixo de cerca de 5 bar, as
  molas começam a aplicar sozinhas. Com o reservatório vazio, o ônibus não sai do lugar até o compressor encher.
- **Alarme:** com a elétrica ligada e menos de 5,5 bar, toca a campainha de pressão baixa. Ela só é ouvida dentro do
  ônibus.
- **Consumo:** portas e suspensão a ar (SuspensaoAr) gastam do mesmo reservatório, e há um vazamento pequeno.
- **Portas pneumáticas:** abaixo de 4 bar (`PressaoPortas`), as portas não abrem nem fecham, e aparece o aviso "Portas sem ar".
- **Chiado do jogo:** com a simulação ligada, o chiado aleatório que o jogo toca fica mudo (`SilenciarArFalso`).
- **Manômetro:** segurando Alt, a faixa do jogo com a velocidade e o FPS mostra também a pressão do reservatório, no
  meio, em vermelho quando o alarme toca. Assim dá para ver o ar sem a PBSLoaderCommonInterface. Para tirar, use `PressaoNaFaixa`.
- **Pressão inicial:** é a mesma toda vez que um veículo carrega (`PressaoInicial`, padrão 8,5 bar). Use 0 para
  começar com o reservatório vazio.

O freio a ar só atua em veículos com física VPP. Em veículos EVP5 a simulação roda, mas não muda o freio.

`Ctrl+Alt+Shift+F2` grava `UserData/PBSLoaderCommonUtils/sdk.txt` com tudo que o SDK está lendo do jogo (veículo, ar, mapa, linha, hora, clima).

## Versão do jogo

A PBSLoaderCommonUtils confere a versão do jogo pelo `GameAssembly.dll`. Numa versão em que os mods não foram testados,
eles ficam em **modo seguro**: continuam lendo o jogo, mas não mexem nele, e uma falha na tela avisa. Atualize o PBSLoader
quando o jogo atualizar. Quem sabe que os mods funcionam na versão nova pode ligar `PermitirVersaoNaoTestada` em
`[PBSLoaderCommonUtils]` no `MelonPreferences.cfg` (ou no menu Mods).

## Para quem faz mods

A PBSLoaderCommonUtils é o SDK dos mods: veículo, mapa, linha, passageiros, hora, clima, câmera, ganchos no jogo,
atalhos, avisos e scripts que vêm com ônibus e mapas. A referência completa está em [SDK.md](SDK.md), e os exemplos
em [`../Exemplos`](../Exemplos).

Neste repositório, referencie com
`<ProjectReference Include="..\PBSLoaderCommonUtils\PBSLoaderCommonUtils.csproj" Private="false" />`.

## Teste automático

Rode o jogo com estas opções:

```
--pbsloadercommoninterface-partida --pbsloadercommoninterface-onibus=1519 --pbsloadercommonutils-teste --pbsloadercommonutils-sair
```

O teste entra numa partida e passa por:

- cursor solto, solto + preso, preso e devolvido;
- suspensão do teclado do jogo;
- leitura do veículo e das portas;
- freio de molas e alarme com pressão baixa;
- liga a elétrica e o motor e mede o compressor;
- corte do governador com a purga;
- o freio do VPP passando pela simulação;
- avisos de ar baixo e porta aberta (o teste abre uma porta pelo próprio jogo), um aviso e uma notificação de teste,
  e a saída de cada um quando a condição acaba. Nessa etapa ele grava um print do jogo em
  `UserData/PBSLoaderCommonUtils/avisos.png`.
- as áreas do SDK: fase, rodas e eixos, controladores, mapa, sessão, hora, câmera, dados entre mods e se os ganchos
  dispararam. O `sdk.txt` completo vai junto no relatório.

Não use o teclado enquanto o teste roda. O jogo abre na frente das outras janelas, e as teclas vão para ele: acelerar,
abrir portas e outros comandos estragam as medições.

O resultado vai para `UserData/PBSLoaderCommonUtils/teste.txt`, com os metadados completos, e o jogo fecha no fim.
