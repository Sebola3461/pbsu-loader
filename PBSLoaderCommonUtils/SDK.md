# SDK de mods do Proton Bus Simulator Urbano

A PBSLoaderCommonUtils é o SDK dos mods: ela lê o jogo (veículo, mapa, linha, passageiros, hora, clima, câmera) e
expõe esses dados em classes estáticas simples, com nomes em português. Também oferece pontos de entrada no jogo
(ganchos), atalhos de teclado, avisos na tela e a simulação de ar comprimido.

Um mod comum não precisa tocar em nenhuma classe `Il2Cpp*` do jogo nem escrever patch Harmony.

Versão da API: `Sdk.Versao` (hoje 2.0.0). Mudança no primeiro número quebra mods. Testado no jogo versão 307 com
MelonLoader 0.7.3.

## Sumário

- [Começando](#começando)
- [Regras gerais](#regras-gerais)
- [Sdk: fase do jogo e eventos](#sdk-fase-do-jogo-e-eventos)
- [Veículo](#veículo)
- [Comandos](#comandos)
- [Mapa](#mapa)
- [Linha, letreiro e passageiros](#linha-letreiro-e-passageiros)
- [Mundo: hora, clima e tráfego](#mundo-hora-clima-e-tráfego)
- [Partida: sessão, câmera e pastas](#partida-sessão-câmera-e-pastas)
- [Mídia: texturas, sons e modelos](#mídia-texturas-sons-e-modelos)
- [Ganchos](#ganchos)
- [Dados entre mods](#dados-entre-mods)
- [Salvos: dados entre sessões](#salvos-dados-entre-sessões)
- [Ar comprimido](#ar-comprimido)
- [Atalhos, mouse e teclado](#atalhos-mouse-e-teclado)
- [Avisos](#avisos)
- [Interface: menu, painéis e HUD](#interface-menu-painéis-e-hud)
- [Avançado: componentes do jogo](#avançado-componentes-do-jogo)
- [Scripts de conteúdo (ônibus e mapas)](#scripts-de-conteúdo-ônibus-e-mapas)
- [Diagnóstico](#diagnóstico)

## Começando

Referencie `PBSLoaderCommonUtils.dll` (está na pasta `Mods` do jogo depois que o PBSLoader a instala) com
`Private=false`, e declare a dependência:

```csharp
using MelonLoader;
using PBSLoaderCommonUtils;
using PBSLoaderCommonUtils.Veiculos;

[assembly: MelonInfo(typeof(MeuMod.MeuModMod), "MeuMod", "1.0.0", "você")]
[assembly: MelonGame(null, "Proton Bus Simulator")]
[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils")]

namespace MeuMod
{
    public class MeuModMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            if (!Sdk.Requer("2.0", "MeuMod")) return; // SDK velho demais: avisa o jogador e o mod fica parado
            Sdk.VeiculoTrocou += () => LoggerInstance.Msg(Veiculo.Existe ? "entrou no " + Veiculo.Nome : "sem ônibus");
        }

        public override void OnUpdate()
        {
            if (Veiculo.Existe && Veiculo.VelocidadeKmh > 80f) { /* ... */ }
        }
    }
}
```

O exemplo completo está em [`Exemplos/ModExemplo`](../Exemplos/ModExemplo/ModExemplo.cs): um painel com velocidade,
hora, clima, mapa, ponto atual, próximo ponto e passageiros, e os eventos no log.

Cada área é um namespace:

| Namespace | O que tem |
|---|---|
| `PBSLoaderCommonUtils` | `Sdk`, `Ganchos`, `Dados`, `Salvos`, `Avancado` |
| `PBSLoaderCommonUtils.Veiculos` | `Veiculo`, `Comandos`, `Conducao`, `Porta`, `Roda`, `Espelho` |
| `PBSLoaderCommonUtils.Mapas` | `Mapa`, `Parada` |
| `PBSLoaderCommonUtils.Linhas` | `Linha`, `Letreiro`, `Passageiros`, `Passageiro` |
| `PBSLoaderCommonUtils.Mundo` | `Tempo`, `Clima`, `Trafego` |
| `PBSLoaderCommonUtils.Midia` | `Carregar` (texturas, sons, modelos), `Sons`, `Som3D` |
| `PBSLoaderCommonUtils.Partida` | `Sessao`, `Vista`, `Caminhos`, `Opcoes` |
| `PBSLoaderCommonUtils.Simulacao` | `Ar` |
| `PBSLoaderCommonUtils.Entrada` | `Atalhos`, `Combinacao`, `Mouse`, `Teclado`, `TeclasDoJogo`, `ControlesDoJogo` |
| `PBSLoaderCommonUtils.Interface` | `Avisos` |
| `PBSLoaderCommonUtils.Conteudo` | `ScriptDeOnibus`, `ScriptDeMapa`, `Pacote`, `Pacotes`, `Confirmacao` |

## Regras gerais

- **Tudo na thread principal.** Leia e chame o SDK só do `OnUpdate`, `OnFixedUpdate`, `OnLateUpdate`, `OnGUI` ou dos
  eventos dele. Threads próprias não podem tocar em nada da Unity nem do jogo: o coletor de lixo do IL2CPP derruba o
  jogo.
- **Nada lança exceção.** Fora de uma partida, as propriedades voltam o valor padrão (0, false, "" ou null). Confira
  `Veiculo.Existe`, `Mapa.Existe` ou `Sdk.Fase` antes de confiar nos números.
- **Os dados já estão atualizados no seu `OnUpdate`.** A PBSLoaderCommonUtils roda com prioridade -1000, antes dos
  outros mods.
- **Unidade no nome:** `VelocidadeKmh`, `AlturaM`, `TorqueNm`, `DuracaoDoDiaMin`. Pressões estão em bar.
- **Eventos protegidos:** uma exceção no seu inscrito vai para o log (no máximo a cada 10 s) e não impede os outros
  inscritos. `Sdk.Seguro(acao, "onde")` faz o mesmo para o seu código.
- **Geração:** `Veiculo.Geracao` e `Mapa.Geracao` mudam a cada veículo e mapa carregados. Guarde o número junto com
  o estado que você monta para um veículo e reconstrua quando ele mudar.

## Sdk: fase do jogo e eventos

| Membro | O que é |
|---|---|
| `Sdk.Fase` | `Menu`, `Selecao` (telas de escolha de ônibus, pintura, mapa e linha), `Carregando`, `Dirigindo` |
| `Sdk.Cena` | nome da cena ativa da Unity |
| `Sdk.Versao`, `Sdk.Requer(versao, mod)` | versão da API e checagem |
| `Sdk.VersaoDoJogo`, `Sdk.JogoTestado`, `Sdk.ModoSeguro` | versão do jogo pelo SHA-256 do `GameAssembly.dll` e o modo seguro (veja abaixo) |
| `FaseMudou(Fase)` | a fase mudou |
| `CenaCarregou(string)` | uma cena terminou de carregar, incluindo os tiles dos mapas |
| `VeiculoTrocou` | o veículo do jogador apareceu ou sumiu (confira `Veiculo.Existe`) |
| `MapaCarregou` | um mapa novo começou |
| `Quadro`, `PassoFisico`, `QuadroTardio` | todo Update, FixedUpdate e LateUpdate, depois dos dados do SDK |

### Versão do jogo e modo seguro

No começo, o SDK confere o SHA-256 do `GameAssembly.dll` (o mesmo critério do instalador). Numa versão em que ele não
foi testado, entra em **modo seguro**: os ganchos não disparam, `Conducao` e o freio a ar não escrevem no jogo, e
`Avancado` devolve null, porque campos e métodos podem ter mudado de lugar. As fachadas de leitura continuam funcionando,
e o jogador vê uma falha na tela explicando.

- **Liberar:** o jogador liga `PermitirVersaoNaoTestada` nas preferências da PBSLoaderCommonUtils, por conta e risco dele.
- **Versões novas:** quem testou acrescenta `nome=sha256` em `UserData/PBSLoaderCommonUtils/versoes_testadas.txt` (ou espera a
  próxima versão do SDK).
- **Seu mod:** se ele mexe no jogo por conta própria (Harmony, campos do jogo), confira `Sdk.ModoSeguro` e fique parado.

## Veículo

`Veiculo` é o veículo que o jogador dirige. Tudo é só leitura e vale para o quadro atual.

| Grupo | Propriedades |
|---|---|
| `Veiculo` | `Existe`, `Nome`, `Tipo` (Onibus, Articulado, Biarticulado, Trem), `Fisica` (Vpp, Evp5, Trem), `Geracao`, `Raiz`, `Massa`, `Caixa`, `VelocidadeKmh`, `VelocidadeSinalKmh` (negativa de ré), `Parado`, `OdometroSessaoKm`, `OdometroTotalKm`, `EletricaLigada`, `CameraDentro`, `Dentro(pontoNoMundo)`, `Resumo()` |
| `Veiculo.Motor` | `Ligado`, `Rpm`, `RpmLenta`, `RpmMax`, `Carga` (0 a 1), `TorqueNm` |
| `Veiculo.Cambio` | `Marcha` (-1 ré, 0 neutro), `MarchaTexto`, `Trocando`, `Manual`, `Automatizado`, `RetarderNivel`, `RetarderNiveis` |
| `Veiculo.Pedais` | `Acelerador`, `Freio`, `Embreagem`, `FreioDeMao` (alavanca, 0 a 1), `FreioDeMaoPuxado`, `Direcao` (-1 a 1) |
| `Veiculo.Ajudas` | `PilotoAutomatico`, `TemLimitador`, `LimitadorAtivo`, `LimiteKmh`, `AbsAtuando`, `TracaoAtuando` |
| `Veiculo.Luzes` | `Farol` (Desligado, Baixo, Alto), `Seta` (Nenhuma, Esquerda, Direita, Alerta), `PiscaAceso`, `Re`, `Freio`, `Internas` (bit 0 = luz 1) |
| `Veiculo.Portas` | `Direitas`, `Esquerdas`, `TemEsquerda`, `AlgumaAberta`, `Todas` (lista de `Porta`: `Lado`, `Numero`, `Aberta`, `Entrada`, `Saida`, `TemRampa`) |
| `Veiculo.Cabine` | `Buzinando`, `Limpadores` (0, 1, 2), `ParadaSolicitada`, `TemArCondicionado`, `ArCondicionadoLigado`, `ArCondicionadoVentilador`, `TemperaturaInterna`, `TemperaturaAlvo`, `JanelaMotoristaAberta`, `TemCobrador`, `PassageirosSessao`, `PassageirosABordo` |
| `Veiculo.Rodas` | `Todas` (lista de `Roda`: `Lado`, `Eixo` (0 = dianteiro), `PosicaoLocal`, `Esterca`, `NoChao`, `AnguloGraus`), `Eixos` |
| `Veiculo.Espelhos` | `Esquerdo`, `Direito`, `Meio`, `Todos` (`Espelho`: `Posicao`, `Camera`, `Material`, `Objeto`). Lidos do jogo a cada acesso |
| `Veiculo.Suspensao` | `TemAr`, `Nivel`, `AlturaM`. Um mod de suspensão informa com `Veiculo.Suspensao.Informar(nivel, alturaM)` |
| `Veiculo.Pasta` | pasta do mod do ônibus (a do `.ini`, ou a de `[config] baseDir`); "" nos ônibus do jogo |
| `Veiculo.Objetos` | `Achar(nome)`, `Todos(trecho)`, `Renderizadores(trecho)`, `Materiais(nome, trecho: false)`, `Mostrar(trecho, visivel)`, `TrocarTextura(material, textura, propriedade, trecho: false)`, `NomeDoMaterial(m)` |

`Veiculo.Objetos` busca no modelo do ônibus do jogador pelo nome, incluindo objetos desligados e sem diferenciar
maiúsculas. Os mods de ônibus usam nomes como convenção (`_skin001_`, `_wipers1_`, `_r1y5_`). `TrocarTextura` mexe só nas
instâncias de material deste ônibus, porque o tráfego pode usar o mesmo modelo. Por padrão o nome do material é exato:
mods repetem nomes como `matvazio...` em centenas de materiais.

Detalhes:

- **Pedais:** são o pedido do motorista, lido do barramento de entrada do VPP antes do câmbio, do piloto automático e
  do freio a ar. Com o ônibus parado e sem acelerar, o próprio jogo segura o freio, e `Pedais.Freio` vale 1. A
  embreagem vale 0 com o pé fora do pedal.
- **Veículos EVP5:** os valores vêm dos campos do jogo, e não há leitura da embreagem nem das rodas.
- **Objetos estáveis:** `Porta` e `Roda` continuam os mesmos objetos enquanto o veículo existir, então dá para guardar a
  referência e ler o estado depois. As listas são reescaneadas a cada 2 s, porque alguns mods de ônibus montam o
  veículo aos poucos.
- **Buzina:** vem do pedido do jogo (`MEPControlesBus.TocaBuzina`), não do som.

## Comandos

`Comandos` aciona a carroceria pelos mesmos métodos que os botões do jogo usam. O jogo cuida do som, das luzes e da
animação do botão. Cada comando alterna o estado, e o estado se lê em `Veiculo`.

| Grupo | Comandos |
|---|---|
| Luzes e sinais | `FarolBaixo()`, `FarolAlto()`, `PiscaAlerta()`, `SetaEsquerda()`, `SetaDireita()`, `FreioDeMao()`, `LuzInterna(n)` (consulte `TemLuzInterna(n)` e `LuzInternaAcesa(n)`) |
| Portas | `Porta(lado, numero)`, `Porta(porta)`, `TodasAsPortas()`. Passam por `Ganchos.PortaPedida` e pela trava de ar |
| Ar-condicionado | `ArCondicionado()`, `ArCondicionadoVentilador()`, `ArCondicionadoTemperatura(passos)` |
| Retarder | `RetarderMais()`, `RetarderMenos()`, `RetarderAuto()` (estado em `RetarderAutomatico`) |
| Suspensão a ar | `Ajoelhar()`, `Elevar()`, pelos atalhos da SuspensaoAr (`TemComandoSuspensao`) |
| Elétrica e motor | `Eletrica(ligada)`, `Motor(ligado)`: só agem se o estado for outro. A partida precisa da elétrica |
| Sinais | `ParadaSolicitada(acesa)` |
| Limpadores e janelas | `Limpadores()`, `LimpadoresRapidos()`, `JanelaMotorista()`, `JanelasDianteiras()`, `JanelasTraseiras()` (as janelas só existem nos ônibus que têm essas animações) |
| Ajudas | `PilotoAutomatico()`, `Limitador()` |
| Câmbio automático | `Marcha(-1 / 0 / 1)` põe em R, N ou D. Retorna false no câmbio manual (use `Conducao.Marcha`) |

### Condução

`Conducao` deixa um mod dirigir: piloto automático, ajuda de direção, testes. O pedido vale enquanto for repetido a
cada quadro; parou de pedir, o controle volta ao jogador no quadro seguinte.

```csharp
Conducao.Acelerador("MeuMod", 0.4f);   // 0..1
Conducao.Freio("MeuMod", 0f);          // 0..1, passa pelo freio a ar
Conducao.Direcao("MeuMod", -0.2f);     // -1 esquerda .. 1 direita
Conducao.Embreagem("MeuMod", 1f);      // 0..1, no sentido do VPP
Conducao.FreioDeMao("MeuMod", 0f);
Conducao.Marcha("MeuMod", 2);          // câmbio manual: -1 ré, 0 neutro, 1.. à frente
Conducao.Buzina("MeuMod");             // buzina tocando enquanto pedir
```

Os valores são escritos no barramento de entrada do VPP logo antes de cada passo de física, então vencem o que o
jogador está fazendo, e `Veiculo.Pedais` mostra o pedido do mod. Com dois mods pedindo o mesmo pedal, vale o maior
(freio e freio de mão) ou o último pedido (o resto). Depois de soltar a marcha, o jogo fica nela, como se o jogador
tivesse engatado. `Conducao.Controlando` e `Conducao.Donos` dizem quem está dirigindo. Só funciona em veículos VPP.

## Mapa

`Mapa.Existe` fica true enquanto o jogador está dirigindo.

| Membro | O que é |
|---|---|
| `Nome`, `Origem` (`Jogo` ou `Mod`), `Pasta` (do mod de mapa), `Versao`, `PontoDePartida`, `Geracao` | identidade |
| `CarregandoTiles` | o jogo está carregando tiles agora |
| `Paradas` | pontos de ônibus dos tiles carregados (reescaneados a cada 3 s) |
| `MaisPerto(pontoNoMundo, maxM)` | ponto carregado mais perto |
| `Disponiveis()` | pastas de mods de mapa instaladas |

Cada `Parada` tem `Nome`, `Destino`, `Lado` (lado da rua; o ônibus abre as portas desse lado), `Inicial`, `Final`,
`Posicao`, `Esperando` (pessoas para entrar), `Descendo` (passageiros que vão descer aqui) e `TemOnibus`.

Mapas grandes carregam em tiles conforme o ônibus anda. Um ponto de um tile descarregado sai da lista.

## Linha, letreiro e passageiros

| Membro | O que é |
|---|---|
| `Linha.Nome` | a linha escolhida. No jogo, escolher a linha é escolher o ponto de partida do mapa, então é esse nome |
| `Linha.Disponiveis` | linhas que o mapa oferece |
| `Linha.ParadaAtual`, `Linha.NoPonto` | o ponto em que o ônibus está agora (o jogo detecta pelo gatilho do ponto) |
| `Linha.ProximaParada` | o próximo ponto carregado à frente (até 2 km, num cone de ±45°). É uma estimativa: o jogo não guarda o itinerário em ordem |
| `Letreiro.Indice`, `Letreiro.Ligado` | número do letreiro escolhido (-1 sem escolha) e se está aceso |
| `Letreiro.Definir(numero)` | escolhe o letreiro pelo teclado de itinerário do jogo; false se o ônibus não tem esse teclado |
| `Passageiros.PodeEmbarcar`, `Parada.PodemEntrar` | as travas do jogo: o ônibus aceita passageiros, e o ponto liberou a entrada |
| `Passageiros.ABordo`, `EmbarcaramSessao`, `DesceramSessao`, `SinalSolicitado`, `Dentro` | contagens e a lista dos que estão dentro |
| `Passageiros.Embarcou(Passageiro)`, `Passageiros.Desceu(Passageiro)` | eventos, com até meio segundo de atraso |

Um `Passageiro` tem `Origem` (o ponto em que entrou), `Sentado`, `VaiDescer` e `PassouNaCatraca`. O jogo não tem
tarifa nem dinheiro.

## Mundo: hora, clima e tráfego

| Membro | O que é |
|---|---|
| `Tempo.Existe`, `Hora`, `Minuto`, `Segundo`, `Dia`, `HoraDecimal`, `Noite`, `DuracaoDoDiaMin`, `Parado`, `Texto` ("13:05") | relógio do jogo (EnviroSky) |
| `Tempo.HoraPassou(int)` | a hora cheia mudou |
| `Clima.Chovendo`, `Clima.Chuva` (Nenhuma, Leve, Media, Pesada), `Clima.Molhado` (0 a 1), `Clima.Nome` | chuva e preset do céu |
| `Clima.Mudou` | começou ou parou de chover, ou o preset mudou |
| `Trafego.Carros`, `Pedestres`, `Onibus`, `Trens`, `MaxCarros`, `MaxPedestres`, `MaxOnibus`, `ColisoesAtivas` | contadores do tráfego de IA (os do jogo) |
| `Trafego.Veiculos`, `PedestresAtivos`, `Semaforos`, `MaisPertoAFrente(maxM)` | os objetos do trânsito, com posição (veja abaixo) |

`Clima.Molhado` sobe e desce aos poucos: use-o para pista molhada, e `Chovendo` para o que depende da chuva caindo.
Hora e clima são lidos 4 vezes por segundo.

### Mudar hora e clima

```csharp
Tempo.Definir(18, 30);              // mantém o dia
Tempo.Pausar(true);                 // relógio parado; Pausar(false) volta a andar
Tempo.DefinirDuracaoDoDia(24f);     // minutos reais por dia do jogo
Clima.Definir("Cloudy 2");          // um de Clima.Presets ("Clear Sky", "Cloudy 1".."4", "Foggy 1", "Light Rain", "Heavy Rain", "Storm")
Clima.Chover(Chuva.Pesada);         // Leve, Media, Pesada ou Nenhuma
```

A chuva do jogo tem duas partes: o clima do céu (nuvens, partículas, mundo molhado) e uma chave do próprio jogo (gotas no
para-brisa, rua molhada). `Chover` mexe nas duas, e `Clima.Chovendo` vale quando qualquer uma está chovendo. As trocas de
clima têm transição de alguns segundos.

### Veículos, pedestres e semáforos

```csharp
foreach (var v in Trafego.Veiculos)          // carros e ônibus da IA: Tipo, Posicao, Frente, VelocidadeKmh, DistanciaM
    if (v.DistanciaM < 30f) { /* ... */ }
var frente = Trafego.MaisPertoAFrente(80f);   // o mais perto à frente do ônibus (cone de ±30°)
int pedestres = Trafego.PedestresAtivos.Count;

foreach (var s in Trafego.Semaforos)
{
    var cor = s.CorPara(Veiculo.Raiz.position + Veiculo.Raiz.forward * 20f);  // foco aceso mais perto do ponto
    foreach (var luz in s.LuzesAcesas) { /* luz.Posicao, luz.Cor */ }
}
```

- **Custo:** as listas são reescaneadas 1 vez por segundo, e só enquanto algum mod as usa.
- **Semáforos:** um semáforo comanda o cruzamento inteiro, com vários focos. Por isso a cor é por foco (`LuzesAcesas`),
  não do semáforo todo. A cor sai do nome do objeto (os mapas usam `_red_`, `_green_`, `_yellow_`, ou os nomes em português)
  ou da cor da luz e do material. `Fase` e `Fases` dão o passo do ciclo.

## Partida: sessão, câmera e pastas

| Membro | O que é |
|---|---|
| `Sessao.Onibus`, `OrigemDoOnibus` (Jogo, Mod), `PastaDaPintura`, `ArquivoDaPintura`, `Mapa`, `PontoDePartida` | o que o jogador escolheu |
| `Sessao.Pausado` | o menu de pausa está aberto |
| `Vista.Modo` (Motorista, Salao, Externa, Livre, Painel, Editor), `Vista.Interna`, `Vista.Camera` | câmera do jogo |
| `Vista.IrParaMotorista()` | volta para a câmera do motorista |
| `Vista.Assumir(dono, precisa)` | seu mod posiciona a câmera neste quadro (veja abaixo) |
| `Vista.Mudou(ModoDaVista)` | o modo da câmera mudou |
| `Caminhos.Mods`, `ModsDeOnibus`, `ModsDeMapas`, `ModsDeLetreiros`, `ModsDeCargas`, `Dados`, `DadosDosVeiculos`, `Capturas` | pastas do jogo |

### Opções do jogo

`Opcoes` lê e grava as opções do menu de opções do próprio jogo (o `settings.txt` da pasta de dados, pelo `MepPrefs`
do jogo). Gravar escreve o arquivo na hora.

```csharp
int carros = Opcoes.CarrosNoTransito.Valor;
Opcoes.CarrosNoTransito.Valor = 40;            // também muda o trânsito da viagem em andamento
Opcoes.Sombras.Valor = false;                  // vale ao recomeçar a viagem (Opcoes.Sombras.Vale)
float s = Opcoes.Ler("sensibilidadeSetas", 1f); // qualquer chave do settings.txt, por nome
```

- **Com nome:** `Opcoes.Todas` lista as principais, em grupos (Trânsito, Gráficos, Espelhos, Viagem, Controles, Som), cada uma
  com `Chave`, `Nome`, `Grupo`, `Vale` e `Min`/`Max` quando fazem sentido.
- **Quando vale:** quantidade de carros, pedestres e ônibus e o volume geral mudam na hora (`QuandoVale.NaHora`). As outras
  valem ao recomeçar a viagem, como no menu do jogo.
- **Disponível:** `Opcoes.Disponivel` fica true quando o jogo abre o arquivo, logo no começo do menu. Antes disso, ler
  devolve o padrão e gravar não faz nada.
- **Tipos:** `Ler<T>`/`Gravar<T>` aceitam `int`, `float`, `bool` e `string`.

Um mod que move a câmera por conta própria (andar a pé, câmera de cinema) chama `Vista.Assumir("MeuMod", ativo)` a
cada quadro. Enquanto alguém pedir, o `MouseOrbitCSHARP` do jogo não roda, e o pedido vence no quadro seguinte ao
último. Não faça patch próprio no `MouseOrbitCSHARP.Update`.

## Mídia: texturas, sons e modelos

`Midia.Carregar` usa os mesmos carregadores que o jogo usa nos mods de ônibus e mapa, então os formatos e a cache de
texturas (e o CarregamentoRapido) valem igual:

```csharp
Texture2D t = Carregar.Textura(caminho);   // PNG ou JPG
AudioClip s = Carregar.Som(caminho);       // os formatos de som dos mods (WAV, OGG)
GameObject m = Carregar.Modelo(caminho);   // .3ds ou .c1 (o .c1 é aberto pelo próprio jogo), com materiais e texturas da pasta
```

O que é carregado não some quando o jogo troca de ônibus (ele limpa os recursos sem uso nessa hora); destrua você mesmo
o que não precisar mais. Erros voltam null e uma linha no log.

`Midia.Sons.Tocar(clip, pai, posicaoLocal, volume, loop)` toca um som 3D num objeto novo, filho de `pai` (uma peça do
ônibus, um ponto do mapa). Sem `loop`, ele se destrói ao terminar. O `Som3D` devolvido tem `Volume`, `Pitch`,
`AbafarDentro` (mais baixo com a câmera dentro do ônibus), `Tocando` e `Parar()`. `Sons.TocarArquivo(caminho, pai)`
carrega e toca de uma vez.

## Ganchos

`Ganchos` são pontos do jogo onde seu mod entra sem escrever patch Harmony. Cada um é um patch só, na
PBSLoaderCommonUtils, e só dispara para o veículo do jogador (o tráfego usa os mesmos componentes). A ordem é a da
inscrição, e a PBSLoaderCommonUtils vem antes de todos.

| Gancho | Quando dispara |
|---|---|
| `DepoisDaFisica(VPVehicleController)` | depois de cada passo do VPP (`DoUpdateBlocks`). É o lugar de mexer em motor, câmbio e pneus. Articulados disparam uma vez por controlador |
| `AntesDosFreios(Brakes)` | antes de o VPP aplicar os freios. `brakeInput` e `handbrakeInput` já trazem o freio a ar |
| `DepoisDaRodaVisual(VPWheelCollider)` | depois de o VPP posicionar a roda desenhada |
| `DepoisDoSomDoMotor(MepSomMotor)` | depois de o jogo atualizar uma camada do som do motor (pitch e volume do `asOn`) |
| `PortaPedida(PedidoDePorta)` | alguém pediu para abrir ou fechar uma porta. `Lado`, `Numero`, `Porta`, `Abrindo`; `Cancelar(mod)` impede o comando e o som |

```csharp
Ganchos.PortaPedida += p =>
{
    if (p.Abrindo && Veiculo.VelocidadeKmh > 3f) p.Cancelar("MeuMod");
};
```

Os argumentos são componentes do jogo, e os campos deles podem mudar entre versões do jogo. Ganchos de física rodam
dezenas de vezes por segundo: não aloque memória neles.

## Dados entre mods

Um mod publica um valor para os outros lerem, sem um depender da DLL do outro:

```csharp
Dados.Publicar("MeuMod", "temperaturaOleo", 92.5f);           // vale até o veículo trocar
Dados.Publicar("MeuMod", "perfil", "economico", porVeiculo: false); // vale até o jogo fechar
float t = Dados.Ler("MeuMod", "temperaturaOleo", 0f);         // padrão se não existir ou for de outro tipo
bool tem = Dados.Tem("MeuMod", "temperaturaOleo");
```

`Veiculo.Suspensao` é feito assim: o SuspensaoAr publica, e a PBSLoaderCommonInterface lê.

## Salvos: dados entre sessões

`Dados` vive só na memória. Para guardar algo de uma sessão para outra, use uma gaveta de `Salvos`:

```csharp
var g = Salvos.DoOnibus("MeuMod");          // uma por ônibus; também Salvos.DoMapa("MeuMod") e Salvos.Global("MeuMod")
int voltas = g.Ler("voltas", 0);            // padrão se não existir ou não for do tipo pedido
g.Gravar("voltas", voltas + 1);
g.Gravar("ajuste", new MeuAjuste { Altura = 0.12f, Nome = "padrão" });   // classes simples, listas e arrays também
```

- **Arquivo:** um JSON por mod e por lugar, em `UserData/PBSLoaderCommonUtils/Salvos/global`, `onibus/<ônibus>` ou
  `mapa/<mapa>`. Dá para abrir e editar à mão.
- **Quando grava:** `Gravar` só marca a gaveta. O arquivo é escrito 1,5 s depois da última mudança, na troca de cena e ao
  fechar o jogo. `Salvos.GravarAgora()` força.
- **Troca de ônibus ou mapa:** pegue a gaveta de novo em `Sdk.VeiculoTrocou` ou `Sdk.MapaCarregou`. Sem ônibus ou mapa, a
  gaveta funciona, mas não grava (`Persistente` false).
- **Scripts de conteúdo:** `Pacote.Salvos` já vem separado pelo pacote e pelo ônibus ou mapa dele.

## Ar comprimido

`Simulacao.Ar` simula compressor, reservatório, freio de serviço pneumático e freio de molas no veículo do jogador.

| Propriedade | O que é |
|---|---|
| `Simulando` | a simulação está ligada e há veículo |
| `Pressao` | reservatório, em bar |
| `CompressorCarregando`, `VazaoCompressor` | se o compressor está mandando ar, e quantos litros livres por minuto |
| `PressaoCamaras`, `FreioEfetivo` | câmaras do freio de serviço e o freio que chega às rodas (0 a 1) |
| `PressaoMolas`, `FreioMolas` | câmaras de mola e quanto as molas estão aplicadas (0 a 1) |
| `Alarme`, `Escape` | pressão baixa com a elétrica ligada; litros por segundo saindo para a atmosfera |

O evento `Ar.Evento` recebe `"purga"`, `"molas"` ou `"alarme"`. Para um mod que gasta ar:

```csharp
Ar.Gastar("meumod", litrosLivres);   // litros de ar livre a 1 bar; 100 L livres = 1 bar num reservatório de 100 L
float f = Ar.Disponivel(4.5f, 8f);  // 0 abaixo de 4,5 bar, 1 a partir de 8 bar (sempre 1 sem a simulação)
```

## Atalhos, mouse e teclado

```csharp
using PBSLoaderCommonUtils.Entrada;

// Em OnInitializeMelon. "MeuMod" é o Identifier da categoria de preferências do mod: a PBSLoaderCommonInterface mostra os
// atalhos na página do mod e na página Atalhos, e o jogador pode trocar a tecla.
var painel = Atalhos.Registrar("MeuMod", "Painel", "Painel do meu mod", "Ctrl+Alt+F5", Painel.Alternar, "Abre e fecha o painel");
var turbo = Atalhos.Registrar("MeuMod", "Turbo", "Turbo", "LeftShift", deSegurar: true);   // leia turbo.Segurando
var proximo = Atalhos.Registrar("MeuMod", "Proximo", "Próximo item", "Keypad3", soComPainel: true);
string texto = painel.Combinacao.Legivel(); // "Ctrl + Alt + F5"
```

- **Modificadores exatos:** `F9` não dispara com Ctrl+F9. Atalhos de segurar toleram modificadores a mais.
- **Escolha teclas que o jogo não usa.** O jogo usa quase todas as letras, os números, as setas e boa parte do teclado
  numérico, e alguns scripts dele ignoram Ctrl e Alt. F2, F3 e F9 a F12, Home, Insert, Delete, PgUp e PgDn estão livres.
- **Conflitos:** `atalho.Conflitos` lista as colisões com outros mods e com o jogo (`TeclasDoJogo.Conflitos`).

Não mexa em `Cursor.lockState` nem em `Cursor.visible`. A cada quadro, diga do que precisa:

```csharp
Mouse.Soltar("MeuMod", Painel.Aberto);           // cursor livre e visível
Mouse.Prender("MeuMod", cameraGirando);          // cursor preso e escondido
Teclado.SuspenderJogo("MeuMod", Painel.Aberto);  // as teclas não acionam comandos do jogo
```

Solto ganha de preso. Quem para de pedir sai sozinho no quadro seguinte. Quando ninguém pede, o cursor volta ao jogo.

### Controles do jogo

`ControlesDoJogo` diz o que o jogador está fazendo nos controles do jogo (Rewired), já com o mapeamento dele, em qualquer
dispositivo: "apertou a seta" vale no teclado, no volante e no controle.

```csharp
if (ControlesDoJogo.Apertou("Horn")) { /* ... */ }        // neste quadro
bool segurando = ControlesDoJogo.Botao("Left Blinker");    // agora
float acel = ControlesDoJogo.Eixo("Joystick Accelerate");  // -1..1
ControlesDoJogo.AcaoApertada += a => Log(a.Nome);           // qualquer botão (só confere com alguém inscrito)
var lista = ControlesDoJogo.Acoes;                          // Id, Nome, Descricao, Tipo (Botao/Eixo)
var conectados = ControlesDoJogo.Dispositivos;              // "Keyboard (Keyboard)", "Logitech G25 (Joystick)"...
```

As ações do jogo (versão 307) são:
- **Direção:** Joystick Steering, Joystick Accelerate, Joystick Brake, Joystick Clutch, Joystick Combined Pedals, Keyboard
  Steering, Keyboard Pedals, Keyboard Clutch, Keyboard/Joystick Drift Handbrake.
- **Câmbio:** Keyboard Gear 1..6, Joystick Gear 1..8, Neutral/Reverse Gear, Sequential Gear Up/Down.
- **Ônibus:** Horn, Left/Right Blinker, Turn off Blinkers, Emergency Lights, Low/High Beam, Internal Light 1..3, Handbrake,
  Turn on Electric, Turn on/off Engine, Air Conditioning, Windshield Wipers, Door 1..6, All Doors, Switch Doors Side,
  Retarder Up/Down, Retarder on Brakes, Cruise Control, Speed Limiter, Radio.
- **Jogo:** Escape Pause, Screenshot, Time Freeze, Time and Weather, Tools Window, Controls Settings, Switch/Command Cameras,
  Camera Look Left/Right, Free Camera, Reset Camera, Switch Controls, Custom Display Up/Down.

Só leitura: para mandar nos pedais use `Conducao`. Com `Teclado.SuspenderJogo` ativo, o teclado não conta.

## Avisos

`Interface.Avisos` junta avisos e falhas de qualquer mod. Quem mostra na tela é a PBSLoaderCommonInterface, no canto inferior
direito. Trocar de veículo limpa todos.

```csharp
// Condição: fica ativa enquanto valer. Chame quando o estado mudar (ou a cada quadro); com false ela sai.
Avisos.Definir("MeuMod", "oleo", pressaoBaixa, Gravidade.Falha, "Pressão do óleo baixa", "0,8 bar");
// Notificação: some sozinha depois de alguns segundos (padrão 5).
Avisos.Notificar("MeuMod", "salvo", Gravidade.Info, "Ajustes salvos");
```

Gravidades: `Info`, `Aviso` e `Falha`. `Avisos.Ativos` lista os ativos, `Avisos.Mudou` avisa mudanças e
`Avisos.Limpar("MeuMod")` tira todos os de um mod.

## Interface: menu, painéis e HUD

A parte visual fica na outra base do PBSLoader, a **PBSLoaderCommonInterface** (sempre instalada; declare
`[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils", "PBSLoaderCommonInterface")]`):

- `PainelMods`: botões, sliders, nome e ícone do mod no menu **Mods** (as preferências já aparecem sozinhas).
- `Paineis.Criar(...)`: um painel por cima do jogo, descrito com `.Valor`, `.Barra`, `.Texto`, `.Chave`, `.Faixa`,
  `.Botao` e `.Preferencia`, desenhado no visual da interface.
- `Hud.Adicionar(...)`: cartões de valor na tela durante a direção.

Os detalhes estão no [README da PBSLoaderCommonInterface](../PBSLoaderCommonInterface/README.md#para-outros-mods).

## Avançado: componentes do jogo

Quando o SDK ainda não expõe algo, `Avancado` dá os componentes por trás das fachadas: `Controles`
(`MEPControlesBus`), `Vpp` (controlador com o motor), `VppDirigido` (com as rodas que esterçam), `TodosVpp`, e
`DoJogador(vpp)` / `DoJogador(roda)`. `Porta.Jogo`, `Roda.Jogo`, `Parada.Jogo`, `Passageiro.Jogo` e `Letreiro.Jogo`
fazem o mesmo para cada objeto.

Os campos desses componentes são os do jogo e podem mudar a cada versão dele; por isso tudo aqui é null no modo seguro. Se você precisa de algo, peça no
repositório do PBSLoader para entrar no SDK.

## Scripts de conteúdo (ônibus e mapas)

Um ônibus ou um mapa pode trazer código próprio: um letreiro com lógica, um painel específico, portas especiais,
eventos do mapa. O script é uma DLL compilada contra este SDK, que vai dentro da pasta do mod. Ele só roda enquanto
aquele ônibus é o veículo do jogador, ou enquanto aquele mapa está carregado.

### Onde fica

```
<pasta de mods de ônibus>\MeuOnibus\scripts\pbsu.json
<pasta de mods de ônibus>\MeuOnibus\scripts\MeuOnibus.Scripts.dll

<pasta do mod de mapa>\scripts\pbsu.json
<pasta do mod de mapa>\scripts\MeuMapa.Scripts.dll
```

A pasta do ônibus é a do `.ini` dele (`[config] baseDir`, ou o nome do `.ini`). Mapas do jogo e ônibus que vêm com o
jogo não têm scripts.

```json
{
  "id": "autor.meuonibus",
  "nome": "Meu ônibus",
  "autor": "você",
  "versao": "1.0.0",
  "sdkMinimo": "2.0",
  "dll": "MeuOnibus.Scripts.dll"
}
```

### O script

```csharp
using PBSLoaderCommonUtils.Conteudo;
using PBSLoaderCommonUtils.Veiculos;

public class MeuPainel : ScriptDeOnibus
{
    public override void Ativar() => Pacote.Log.Msg("ativo em " + Veiculo.Nome);
    public override void Quadro() { /* todo quadro, enquanto este ônibus é o do jogador */ }
    public override void Desativar() { /* saia dos eventos do SDK em que entrou */ }
}
```

Cada classe concreta que herda de `ScriptDeOnibus` (ou `ScriptDeMapa`, conforme a pasta) e tem construtor sem
parâmetros vira uma instância. Métodos: `Ativar`, `Desativar`, `Quadro`, `PassoFisico`, `QuadroTardio` e, nos de mapa,
`TileCarregou(cena)`. O script pode usar todo o SDK.

`Pacote` dá ao script:

| Membro | O que é |
|---|---|
| `Id`, `Nome`, `Autor`, `Versao`, `Origem`, `Pasta`, `Sha256`, `Estado` | o pacote |
| `Caminho(rel)`, `LerTexto(rel)`, `LerBytes(rel)`, `Existe(rel)` | arquivos do pacote (não saem da pasta dele) |
| `Textura(rel)`, `Som(rel)`, `Modelo(rel)` | o mesmo que `Midia.Carregar`, para arquivos do pacote |
| `Config` | preferências próprias, em `UserData/PBSLoaderCommonUtils/Conteudo/<id>.cfg` |
| `Harmony` | Harmony próprio. Tudo que ele aplicar é desfeito quando o pacote desativa |
| `Log` | log com o nome do pacote |
| `Atalho(id, nome, padrao, acao)` | atalho que só age com o pacote ativo |
| `Salvos` | gaveta de dados guardados entre sessões (ver [Salvos](#salvos-dados-entre-sessões)) |

### Ciclo de vida

- **Entrada:** quando o ônibus vira o veículo do jogador (ou o mapa carrega), a PBSLoaderCommonUtils lê o
  `pbsu.json`, confere `sdkMinimo` e a permissão do jogador, carrega a DLL (uma vez por sessão para cada hash) e chama
  `Ativar`.
- **Saída:** quando o ônibus ou mapa sai de jogo, ela chama `Desativar`, desfaz os patches do `Pacote.Harmony` e limpa
  os avisos do pacote. A DLL continua carregada até o jogo fechar.
- **Volta:** ao voltar para o mesmo ônibus ou mapa, as mesmas instâncias são ativadas de novo, sem perguntar.
- **Falhas:** uma exceção em `Ativar`, ou cinco seguidas no mesmo método, desligam o pacote até o jogo fechar. O
  jogador vê uma falha na tela com o motivo.

### Permissão do jogador

Um script de conteúdo é código que roda com as mesmas permissões dos mods. Na primeira vez que um pacote aparece, o
jogador vê o nome, o autor, a origem e o SHA-256 da DLL, e escolhe:

- **Permitir:** fica gravado.
- **Agora não:** vale só para esta sessão.
- **Nunca:** fica gravado.

As decisões ficam em `%LOCALAPPDATA%\PBSLoaderCommonUtils\confianca.json`, por hash. Uma DLL atualizada tem outro hash e
pergunta de novo. Hashes listados em `%LOCALAPPDATA%\PBSLoaderCommonUtils\confiaveis.txt` (um por linha) já vêm
permitidos; esse arquivo é para o PBSLoader gravar a partir do catálogo. Os dois ficam fora da pasta do jogo de
propósito: um ônibus ou mapa extraído nela não consegue trazer a própria permissão. O arquivo antigo
(`UserData/PBSLoaderCommonUtils/confianca.json`) não é lido: quem atualizar responde de novo uma vez por pacote.

A PBSLoaderCommonInterface desenha a pergunta e tem a página **Scripts de ônibus e mapa** no menu **Mods**. Por código:
`Pacotes.Todos` lista os pacotes vistos na sessão (com `Estado`, `Detalhe` e `QuantosScripts`), `Pacotes.Permissao(p)`
diz a decisão gravada, e `Pacotes.Permitir(p)` e `Pacotes.Revogar(p)` mudam a decisão na hora. Sem a interface, a
própria PBSLoaderCommonUtils mostra uma janela simples (`Confirmacao`).

### Testar

Os exemplos estão em [`Exemplos/ScriptDeOnibusExemplo`](../Exemplos/ScriptDeOnibusExemplo/PortaTravada.cs) (trava as
portas acima de 3 km/h e avisa se o ônibus anda com porta aberta) e
[`Exemplos/ScriptDeMapaExemplo`](../Exemplos/ScriptDeMapaExemplo/AnuncioDeParada.cs) (anuncia cada ponto). Para
compilar e copiar direto para a pasta de um ônibus:

```
dotnet build Exemplos/ScriptDeOnibusExemplo -p:PastaDeTeste="C:\...\BusMods\MeuOnibus"
```

## Diagnóstico

`Ctrl+Alt+Shift+F2` grava `UserData/PBSLoaderCommonUtils/sdk.txt` com tudo que o SDK está lendo: partida, mapa, linha,
mundo, veículo, scripts de conteúdo, avisos e mouse. Compare com o que você vê no jogo.
