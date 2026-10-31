# PBSLoaderCommonInterface

Interface comum do PBSLoader, instalada sempre junto com a PBSLoaderCommonUtils. Visual de jogos de corrida: painéis
chapados, destaque em vermelho e títulos em itálico.

- Menus, telas de seleção e formulários redesenhados. O jogo continua por baixo: cada botão chama o original.
- Prévia 3D do ônibus girando nas telas de veículo e de pintura, já com a skin aplicada.
- Menu lateral durante a partida, aberto com a tecla crase (`).
- Menu **Mods**, com as opções de todos os mods instalados (veja abaixo) e a página dos scripts de ônibus e mapa.
- Sons de interface e música de menu. Dá para trocar pelos seus em `UserData/PBSLoaderCommonInterface/sons`.

## Teclas

| Tecla | Ação |
|---|---|
| Ctrl+Alt+F2 | Alterna entre a interface nova e a original |
| ` (crase) | Abre o menu lateral na partida |
| (sem padrão) | Grava a hierarquia da interface em `UserData` (depuração) |

A página **Atalhos** do menu **Mods** junta os atalhos de todos os mods. Clique num atalho e aperte a combinação nova:
Esc cancela e Backspace deixa sem atalho. Conflitos com o jogo ou com outro mod aparecem em amarelo. Cada mod também
mostra os próprios atalhos na página dele.

## Painel de condução

No canto inferior direito, um cartão pequeno mostra:

- a velocidade e a marcha;
- a barra de rotação, que fica âmbar e depois vermelha perto do corte;
- as luzes do painel: setas no ritmo do pisca, farol (verde no baixo, azul no alto), freio de mão, porta aberta,
  parada solicitada (pisca ao acender) e a pressão do ar.

Para desligar, use `PainelNaTela`, ou defina uma tecla para "Mostrar ou esconder o painel de condução" na página Atalhos.

## Painel da carroceria

Um chevron discreto no meio da borda direita abre o painel com os controles do ônibus. Ele mostra só o que o ônibus tem:

- **Luzes:** farol baixo e alto, pisca-alerta e as luzes do salão (1 a 3).
- **Portas:** cada porta e "todas". Sem ar, elas não se mexem.
- **Suspensão:** ajoelhar e elevar, com a SuspensaoAr instalada.
- **Ar-condicionado:** ligar, nível do ventilador e temperatura.
- **Retarder:** nível e modo automático.
- **Janelas e cabine, limpadores e animações do mod do ônibus.** Estes saíram do menu lateral esquerdo.

Cada botão fica aceso enquanto o que ele controla está ligado. Com o painel aberto, o painel de condução e os avisos
vão para a esquerda dele. Para abrir pelo teclado, defina uma tecla para "Painel da carroceria" na página Atalhos.

## Avisos do veículo

Durante a viagem, os avisos e falhas do veículo aparecem no canto inferior direito. Eles vêm do ar comprimido e das
portas (PBSLoaderCommonUtils) e de qualquer mod que use `PBSLoaderCommonUtils.Avisos`. Cada aviso é uma linha pequena e translúcida, e a
mais nova entra por baixo.

- **Cores:** vermelho é falha, âmbar é aviso e cinza é informação.
- **Esmaecimento:** depois de alguns segundos, o que continua valendo esmaece. As falhas esmaecem menos.
- **Na tela:** nada pisca nem recebe clique, e cabem até 6 linhas.

Para desligar, use `AvisosNaTela`. Para subir a lista (se cobrir o painel do jogo), use `AvisosMargemInferior`.

## Scripts de ônibus e mapa

Um ônibus ou mapa pode trazer código próprio (veja os scripts de conteúdo no [SDK](../PBSLoaderCommonUtils/SDK.md)). Na
primeira vez que um aparece, a interface pergunta se ele pode rodar, com o nome, o autor e o SHA-256 do arquivo:
**Permitir**, **Agora não** ou **Nunca**. A página **Scripts de ônibus e mapa** do menu **Mods** lista os que apareceram
na sessão, com o estado de cada um ("Rodando", "Bloqueado", "Desligado por erro" e o motivo). O botão de cada um permite
ou bloqueia na hora.

## Opções

As opções ficam na categoria `[PBSLoaderCommonInterface]` do `MelonPreferences.cfg`. A opção `Previa3D` liga e desliga a prévia 3D.

O cache das texturas reduzidas da prévia fica em `UserData/PBSLoaderCommonInterface/cache3d` e pode ser apagado a qualquer hora.
Os modelos criptografados (`.c1`) de ônibus de terceiros são abertos pelo próprio jogo, em memória, e nunca são
gravados em disco.

## Para outros mods

A PBSLoaderCommonInterface é base do PBSLoader: declare a dependência e chame direto.

```csharp
[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils", "PBSLoaderCommonInterface")]
```

### Menu Mods: `PainelMods`

Todo mod com `MelonPreferences` aparece no menu **Mods** automaticamente:
- `bool` vira chave;
- número vira campo com − e +;
- texto vira campo de texto;
- `KeyCode` vira "aperte uma tecla";
- `enum` vira seletor.

`PainelMods` acrescenta o que as preferências não dizem sozinhas:
- `Acao(categoria, rotulo, acao)` adiciona um botão;
- `Faixa(categoria, entrada, min, max, passo)` transforma o número em slider;
- `Ocultar(categoria, entrada)` esconde uma entrada;
- `Nome(categoria, titulo, icone)` define o título e o ícone do mod no menu.

`categoria` é o Identifier da categoria de preferências do mod. Chame no `OnLateInitializeMelon`, quando todas as
categorias já existem. Qualquer `UiIntegracao.cs` do repositório de mods serve de modelo.

### Painéis por cima do jogo: `Paineis`

Um painel é uma janela pequena que fica por cima da direção, sem pausar o jogo. O mod descreve o conteúdo, e a interface
desenha no visual dela, atualiza os valores a cada quadro e solta o cursor enquanto o painel está aberto:

```csharp
var p = Paineis.Criar("MeuMod", "Freios", "Freios", "Ctrl+Alt+F5")   // categoria, id, título, atalho (opcional)
    .Secao("Leituras")
    .Valor("Pressão", () => $"{Ar.Pressao:0.0} bar", "reservatório")
    .Barra("Freio", () => Veiculo.Pedais.Freio)
    .Texto(() => "Texto que muda a cada quadro")
    .Secao("Ajustes")
    .Chave("Freio motor", () => _freioMotor, v => _freioMotor = v)
    .Faixa("Força", () => _forca, v => _forca = v, 0f, 2f, 0.1f)
    .Preferencia(MinhaEntrada, 0f, 10f)                              // uma MelonPreferences_Entry, com o controle do tipo dela
    .Botao("Zerar contadores", Zerar, "Zerar");
p.Abrir();   // ou Fechar(), Alternar(); Aberto diz o estado
```

- Cada painel aparece também como botão na página do mod no menu **Mods**.
- Com atalho, ele vai para a página **Atalhos** e o jogador pode trocar a tecla.
- Os painéis abertos ficam em cascata no canto superior esquerdo, abaixo do botão do menu lateral.
- Uma exceção num item mostra "—" e vai para o log; o painel continua.

### Valores na tela: `Hud`

Cartões pequenos numa coluna no canto superior direito, só durante a direção, sem receber clique:

```csharp
var w = Hud.Adicionar("MeuMod", "oleo", "Óleo", () => $"{_temp:0} °C", ordem: 0,
                      cor: () => _temp > 110 ? Color.red : (Color?)null);
w.Visivel = false;   // esconde sem remover; w.Remover() tira de vez
```

O jogador desliga todos os cartões com `WidgetsNaTela`.

## Desenvolvimento

Opções de linha de comando do jogo, para testes automáticos:

| Opção | O que faz |
|---|---|
| `--pbsloadercommoninterface-partida` | Entra direto numa partida |
| `--pbsloadercommoninterface-onibus=TEXTO` | Escolhe o ônibus para a partida |
| `--pbsloadercommoninterface-mapa=TEXTO` | Escolhe o mapa para a partida |
| `--pbsloadercommoninterface-tour-telas` | Percorre as telas e mede o tempo de cada troca no `Latest.log` (`[Desempenho]`, `[Troca]`) |
| `--pbsloadercommoninterface-tour-cinema` | Tour de câmera pelo ônibus |
| `--pbsloadercommoninterface-tour-abertura` | Tour da tela de abertura |
| `--pbsloadercommoninterface-tour-menu` | Tour do menu |
| `--pbsloadercommoninterface-abrir-carroceria` | Abre o painel da carroceria na partida (para prints de teste) |
| `--pbsloadercommoninterface-teste-sdk` | Testa painéis, cartões de HUD, a pergunta de permissão e a página de scripts; grava `teste_sdk.txt` e prints `sdk_*.png` em `UserData/PBSLoaderCommonInterface` |
| `--pbsloadercommoninterface-sair` | Fecha o jogo no fim do teste acima |

Ao editar [Theme.cs](Theme.cs), cuidado: a tabela `Ico` tem glifos da área de uso privado (Segoe Fluent Icons), que
alguns editores apagam ao salvar. Confira se eles continuam lá antes de commitar.
