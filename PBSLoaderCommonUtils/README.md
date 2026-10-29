# PBSLoaderCommonUtils

Base comum dos mods:

- **Atalhos:** um registro único de teclas, editável num lugar só e com detecção de conflitos.
- **Mouse:** um dono só para o cursor. Acabam os mouses presos ou soltos fora de hora.
- **Teclado:** suspende os controles de teclado do jogo enquanto um painel precisa das teclas.
- **Veículo:** metadados do veículo que você dirige: velocidade, pedais, motor, câmbio, luzes, setas, buzina, portas
  e outros.
- **Ar comprimido:** compressor movido pelo motor, reservatório, governador com purga, freio a ar e freio de molas.
  Esta é a única parte que muda o jogo sozinha. Para desligar, use `ArAtivo` nas preferências.

O [instalador do PBSU Loader](../README.md) instala a PBSLoaderCommonUtils junto com o MelonLoader. Para instalar à mão, copie
`PBSLoaderCommonUtils.dll` para a pasta `Mods`, junto com os mods que a usam.

## Para quem joga

- Os atalhos de todos os mods ficam na seção `[Atalhos]` do `UserData/MelonPreferences.cfg`, em texto como
  `Ctrl+Alt+F9`, `PageDown` ou `LeftAlt`. Texto vazio deixa a ação sem atalho. Com a UiModerna, use a página **Atalhos**
  do menu: clique no atalho e aperte a combinação nova.
- Os atalhos não disparam enquanto você digita num campo de texto. Nesse tempo os controles de teclado do jogo também
  ficam suspensos, para a buzina não tocar enquanto você escreve.
- `UserData/PBSLoaderCommonUtils/teclas.txt` é regravado a cada troca de cena. Ele lista:
  - os controles de teclado do jogo, já com o que você remapeou;
  - as teclas fixas dos scripts do jogo presentes na cena;
  - os atalhos dos mods, com os conflitos.

### Ar comprimido e freio a ar

As opções ficam na categoria `[PBSLoaderCommonUtils]` ("Veículo e ar comprimido" no menu da UiModerna).

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
  meio, em vermelho quando o alarme toca. Assim dá para ver o ar sem a UiModerna. Para tirar, use `PressaoNaFaixa`.
- **Pressão inicial:** é a mesma toda vez que um veículo carrega (`PressaoInicial`, padrão 8,5 bar). Use 0 para
  começar com o reservatório vazio.

O freio a ar só atua em veículos com física VPP. Em veículos EVP5 a simulação roda, mas não muda o freio.

`Ctrl+Alt+Shift+F2` grava `UserData/PBSLoaderCommonUtils/veiculo.txt` com todos os dados do veículo e do ar.

## Para quem faz mods

Referencie `PBSLoaderCommonUtils.dll`. Neste repositório, use
`<ProjectReference Include="..\PBSLoaderCommonUtils\PBSLoaderCommonUtils.csproj" Private="false" />`.

### Atalhos

```csharp
using PBSLoaderCommonUtils;

// Em OnInitializeMelon. "MeuMod" é o Identifier da categoria de preferências do mod: a UiModerna mostra os
// atalhos na página do mod e na página Atalhos.
var painel = Atalhos.Registrar("MeuMod", "Painel", "Painel do meu mod", "Ctrl+Alt+F5", Painel.Alternar,
    "Abre e fecha o painel");

// Atalho de segurar: leia .Segurando quando precisar; não tem ação ao apertar.
var turbo = Atalhos.Registrar("MeuMod", "Turbo", "Turbo", "LeftShift", deSegurar: true);
if (turbo.Segurando) { /* ... */ }

// Atalho que só vale com o painel do mod aberto (veja Teclado abaixo): conflitos com os controles do jogo
// não contam, porque eles ficam suspensos.
var proximo = Atalhos.Registrar("MeuMod", "Proximo", "Próximo item", "Keypad3", soComPainel: true);
if (Painel.Aberto && proximo.Apertou) { /* ... */ }

// Texto para mostrar ao jogador: "Ctrl + Alt + F5".
string t = painel.Combinacao.Legivel();
```

Regras:

- **Modificadores exatos:** `F9` não dispara com Ctrl+F9, e `Ctrl+Alt+F9` não dispara com Shift junto. Atalhos de
  segurar toleram modificadores a mais.
- **Escolha teclas que o jogo não usa.** O jogo usa quase todas as letras, os números, as setas e boa parte do teclado
  numérico. Alguns scripts dele leem teclas sem olhar Ctrl e Alt. As F-keys F2, F3 e F9 a F12 estão livres. F5 a F8 são
  lidas por um script de portas (`PortasBusNOVO`) quando ele está na cena. Confira `teclas.txt`.
- **Conflitos:** `atalho.Conflitos` lista as colisões com outros mods e com o jogo. A UiModerna mostra essa lista em
  amarelo.

### Mouse

Não mexa em `Cursor.lockState` nem em `Cursor.visible`. A cada frame, diga do que precisa:

```csharp
Mouse.Soltar("MeuMod", Painel.Aberto);   // painel aberto: cursor livre e visível
Mouse.Prender("MeuMod", camerasGirando); // câmera pelo mouse: cursor preso e escondido
```

- **Solto ganha de preso:** um painel aberto por cima da câmera livre deixa o cursor livre. `Mouse.Solto` diz se alguém
  está pedindo cursor livre; use isso para parar de girar a câmera.
- **Quem para de pedir sai sozinho** no frame seguinte. Um painel fechado ou um mod desligado nunca deixa o cursor preso.
- **Quando ninguém pede, o cursor volta ao jogo:** solto, e o jogo o esconde quando o mouse para. Ninguém guarda e
  restaura estado. Antes era isso que travava o mouse: dois painéis abertos e fechados fora de ordem devolviam um
  estado velho.
- **O jogo fica fora do caminho:** enquanto algum mod controla o cursor, o esconde-mostra automático do jogo
  (`MepMobileControls.MostraEscondeMouse`) não roda.

### Teclado

```csharp
Teclado.SuspenderJogo("MeuMod", Painel.Aberto); // a cada frame
```

Enquanto alguém pede, a PBSLoaderCommonUtils tira o teclado do jogador do Rewired. Então as teclas do painel não acionam seta,
porta e outros comandos do jogo. As teclas que scripts do jogo leem direto do `Input` continuam valendo: por exemplo, o
teclado numérico gira a câmera. Essas aparecem como conflito.

### Veículo

`PBSLoaderCommonUtils.Veiculo` é atualizado a cada frame. Todas as propriedades são só leitura. Sem veículo (nos menus),
`Veiculo.Existe` é false e o resto fica zerado.

| Grupo | Propriedades |
|---|---|
| Identidade | `Existe`, `Nome`, `Tipo` (Onibus, Articulado, Biarticulado, Trem), `Fisica` (Vpp, Evp5, Trem), `Geracao` (muda a cada veículo carregado), `Raiz`, `Massa`, `Caixa`, `Controles` (o `MEPControlesBus`), `Vpp`, `TodosVpp` |
| Movimento | `VelocidadeKmh`, `VelocidadeSinalKmh` (negativa de ré), `Parado`, `OdometroSessaoKm`, `OdometroTotalKm` |
| Motor e câmbio | `EletricaLigada`, `MotorLigado`, `Rpm`, `RpmLenta`, `RpmMax`, `CargaMotor`, `TorqueMotorNm`, `Marcha` (-1 ré, 0 neutro), `MarchaTexto`, `TrocandoMarcha`, `CambioManual`, `CambioAutomatizado`, `RetarderNivel`, `RetarderNiveis` |
| Pedais | `Acelerador`, `Freio`, `Embreagem`, `FreioDeMaoPedido` (0 a 1), `Volante` (-1 a 1), `FreioDeMao` |
| Ajudas | `PilotoAutomatico`, `TemLimitador`, `LimitadorAtivo`, `LimiteKmh`, `AbsAtuando`, `TracaoAtuando` |
| Luzes e sinais | `Farol` (Desligado, Baixo, Alto), `Seta` (Nenhuma, Esquerda, Direita, Alerta), `LuzDeRe`, `LuzDeFreio`, `LuzesInternas` (bits), `Buzinando`, `Limpadores` (0, 1, 2), `ParadaSolicitada` |
| Portas | `PortasDireitas`, `PortasEsquerdas`, `TemPortaEsquerda`, `AlgumaPortaAberta`, `Portas` (lista com `Lado`, `Numero`, `Aberta`, `Entrada`, `Saida`, `TemRampa`) |
| Conforto | `TemArCondicionado`, `ArCondicionadoLigado`, `ArCondicionadoVentilador`, `TemperaturaInterna`, `TemperaturaAlvo`, `JanelaMotoristaAberta`, `TemCobrador`, `PassageirosSessao` |
| Suspensão a ar | `TemSuspensaoAr`, `NivelSuspensao`, `AlturaSuspensao`, informados pelo mod de suspensão com `Veiculo.InformarSuspensao(nivel, altura)` |
| Câmera | `CameraDentro`, `Dentro(pontoNoMundo)` |

Detalhes:

- **Pedais e volante:** são o pedido do motorista, lido do barramento de entrada do VPP antes do câmbio, do piloto
  automático e do freio a ar.
- **Freio parado:** com o ônibus parado e sem acelerar, o próprio jogo segura o freio, e `Freio` vale 1.
- **Embreagem:** vale 0 com o pé fora do pedal.
- **Veículos EVP5:** os valores vêm dos campos do jogo, e não há leitura da embreagem.
- **Buzina:** vem do pedido do jogo (`MEPControlesBus.TocaBuzina`), e não do som.

### Ar

`PBSLoaderCommonUtils.Ar` tem estas leituras:

| Propriedade | O que é |
|---|---|
| `Simulando` | se a simulação está ligada e há veículo |
| `Pressao` | reservatório, em bar |
| `CompressorCarregando` | se o compressor está mandando ar |
| `VazaoCompressor` | litros livres por minuto |
| `PressaoCamaras` | pressão nas câmaras do freio de serviço |
| `FreioEfetivo` | freio que chega às rodas, de 0 a 1 |
| `PressaoMolas` | pressão nas câmaras de mola |
| `FreioMolas` | quanto as molas estão aplicadas, de 0 a 1 |
| `Alarme` | pressão baixa com a elétrica ligada |
| `Escape` | litros por segundo saindo para a atmosfera |

O evento `Ar.Evento` recebe `"purga"`, `"molas"` ou `"alarme"`.

Para um mod que usa ar:

```csharp
Ar.Gastar("meumod", litrosLivres);    // litros de ar livre a 1 bar; 100 L livres = 1 bar num reservatório de 100 L
float f = Ar.Disponivel(4.5f, 8f);   // 0 abaixo de 4,5 bar, 1 a partir de 8 bar (sempre 1 sem a simulação)
```

### Comandos

`PBSLoaderCommonUtils.Comandos` aciona a carroceria pelos mesmos métodos que os botões do jogo usam. O jogo cuida do som, das
luzes e da animação do botão do painel. Cada comando alterna o estado, e o estado se lê em `Veiculo`.

| Grupo | Comandos |
|---|---|
| Luzes e sinais | `FarolBaixo()`, `FarolAlto()`, `PiscaAlerta()`, `SetaEsquerda()`, `SetaDireita()`, `FreioDeMao()`, `LuzInterna(n)` (consulte `TemLuzInterna(n)` e `LuzInternaAcesa(n)`) |
| Portas | `Porta(lado, numero)`, `Porta(porta)`, `TodasAsPortas()`. Passam pela trava de ar |
| Ar-condicionado | `ArCondicionado()`, `ArCondicionadoVentilador()`, `ArCondicionadoTemperatura(passos)` |
| Retarder | `RetarderMais()`, `RetarderMenos()`, `RetarderAuto()` (o estado está em `RetarderAutomatico`) |
| Suspensão a ar | `Ajoelhar()`, `Elevar()`, que usam os atalhos da SuspensaoAr (`TemComandoSuspensao`) |

Os objetos `Porta` de `Veiculo.Portas` continuam os mesmos enquanto o veículo existir, então dá para guardar a
referência e ler `Aberta` depois.

Qualquer atalho registrado pode ser disparado por código com `Atalho.Executar()`.

### Avisos

`PBSLoaderCommonUtils.Avisos` junta avisos e falhas do veículo vindos de qualquer mod. Quem mostra na tela é a UiModerna, no
canto inferior direito. Trocar de veículo limpa todos.

```csharp
// Condição: fica ativa enquanto valer. Chame quando o estado mudar (ou a cada frame); com false ela sai.
Avisos.Definir("MeuMod", "oleo", pressaoBaixa, Gravidade.Falha, "Pressão do óleo baixa", "0,8 bar");

// Notificação: some sozinha depois de alguns segundos (padrão 5).
Avisos.Notificar("MeuMod", "salvo", Gravidade.Info, "Ajustes salvos", "Retrovisores do 1519");
```

- **Gravidades:** `Info` (cinza), `Aviso` (âmbar) e `Falha` (vermelho). Se uma condição piora de gravidade, ela
  volta a ser destacada.
- **Leitura:** `Avisos.Ativos` lista os avisos ativos, do mais antigo ao mais novo, cada um com `Origem`, `Id`,
  `Gravidade`, `Titulo`, `Detalhe`, `Desde`, `Condicao` e `Versao`.
- **Mudanças:** o evento `Avisos.Mudou` dispara quando um aviso entra, sai ou muda.
- **Limpeza:** `Avisos.Limpar("MeuMod")` tira todos os avisos de um mod.

O próprio PBSLoaderCommonUtils já informa estes:

| Aviso | Quando |
|---|---|
| Pressão do ar baixa / Reservatório de ar vazio (falha) | elétrica ligada e pressão abaixo do alarme. O detalhe diz a pressão, se o freio está fraco ou se as molas aplicaram, e se é preciso ligar o motor |
| Porta aberta (informação) | porta aberta com o ônibus parado. O detalhe diz qual porta |
| Porta aberta em movimento (falha) | porta aberta acima de 5 km/h |
| Portas sem ar (aviso, some em 4 s) | tentou abrir ou fechar uma porta abaixo da pressão mínima |

## Teste automático

Rode o jogo com estas opções:

```
--uimoderna-partida --uimoderna-onibus=1519 --pbsloadercommonutils-teste --pbsloadercommonutils-sair
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

Não use o teclado enquanto o teste roda. O jogo abre na frente das outras janelas, e as teclas vão para ele: acelerar,
abrir portas e outros comandos estragam as medições.

O resultado vai para `UserData/PBSLoaderCommonUtils/teste.txt`, com os metadados completos, e o jogo fecha no fim.
