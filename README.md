# Mirage

Mirage é um aplicativo local para Windows que inicia processos reconhecíveis como jogos pelo Discord. Pesquise no catálogo, escolha um jogo ou monte uma sequência com duração definida para cada item.

## Instalação

Baixe `Mirage-Setup-1.0.0-win-x64.exe` na página de Releases e execute o instalador. O Mirage é instalado somente para o usuário atual em `%LOCALAPPDATA%\Programs\Mirage`.

O aplicativo é compatível com Windows 10 e Windows 11 de 64 bits. O .NET acompanha o instalador. O Windows 11 normalmente já inclui o Microsoft WebView2; se ele estiver ausente, o instalador obtém o componente oficial da Microsoft pela internet.

O Windows pode exibir um aviso porque os binários ainda não possuem assinatura digital própria.

### Versão portátil

Extraia todo o conteúdo de `Mirage-Portable-1.0.0-win-x64.zip` para uma pasta e execute `Mirage.exe`. Não execute o programa diretamente de dentro do ZIP.

## Como usar

1. Abra o Discord para Windows e mantenha-o em execução.
2. Abra o Mirage, pesquise um jogo e selecione o resultado desejado.
3. Para uma sessão individual, defina a duração e escolha **Iniciar só este jogo**.
4. Para uma sequência, defina a duração de cada jogo, use **Adicionar à lista** e organize os itens em **Minha lista**.
5. Inicie a lista. O Mirage encerra cada processo ao fim do tempo e começa o próximo automaticamente.
6. A janela pode ser fechada durante a execução: o Mirage permanece na bandeja, próximo ao relógio. Use o ícone da bandeja para reabrir ou encerrar o aplicativo.

O idioma pode ser alterado no canto superior da janela. Português do Brasil, inglês e espanhol estão incluídos, e a escolha fica salva neste computador.

O reconhecimento é feito pelo aplicativo do Discord para Windows e pode levar alguns segundos. O Mirage inicia apenas um processo auxiliar local; nenhum jogo é aberto.

## Recursos

- Catálogo pesquisável de jogos reconhecidos pelo Discord.
- Processo individual ou sequência automática com tempo por jogo.
- Favoritos, recentes e sequências salvas localmente.
- Busca local e limitada por possíveis pastas de saves.
- Central de saves com jogos instalados e saves órfãos deixados por jogos removidos.
- Datas, quantidade de arquivos e tamanho estimado de cada conjunto de saves.
- Backup de todos os saves encontrados para uma pasta organizada ou arquivo ZIP escolhido pelo usuário.
- Restauração assistida de backups, com análise de destinos, migração entre identificadores de perfil, preservação de conflitos e ponto de restauração antes de substituir arquivos.
- Resultado da Central salvo localmente, verificação incremental e histórico de saves alterados, movidos ou removidos.
- Funcionamento na bandeja com liberação da interface após 15 segundos em segundo plano.
- Preferências armazenadas em `%LOCALAPPDATA%\Mirage`.

O Mirage não exige conta, servidor próprio ou chave de API.

## Central de saves e backups

A **Central de saves** verifica instalações Steam, Epic e programas registrados no Windows. Os resultados são separados entre jogos instalados e saves órfãos — arquivos que continuam no computador mesmo quando a instalação do jogo não é mais detectada.

O Mirage mostra os locais encontrados, a alteração mais recente, a quantidade de arquivos e o tamanho estimado. **Exportar pasta** cria uma cópia organizada por jogo; **Criar ZIP** produz um arquivo único apropriado para armazenamento local ou envio posterior a um serviço de nuvem.

O resultado fica salvo em `%LOCALAPPDATA%\Mirage` e reaparece ao trocar de aba ou reabrir o programa. A conferência rápida revisita somente locais já conhecidos; **Verificar agora** executa uma busca completa. A Central registra alterações, mudanças de caminho e locais removidos em um histórico limitado.

O destino do backup sempre é escolhido pelo usuário. A busca e o backup não excluem, movem, substituem nem modificam os saves originais. Na restauração, o Mirage analisa o conteúdo antes de copiar. O modo seguro mantém os saves atuais e separa conflitos; o modo de substituição cria antes um ZIP com os arquivos que serão trocados. Jogos ainda não instalados ou sem uma pasta reconhecível ficam em **Imported Saves**, aguardando revisão.

Alguns jogos criam uma pasta, perfil ou identificador novo somente depois da primeira execução ou do primeiro save. Após formatar o computador ou reinstalar um jogo, abra-o e salve uma vez antes de restaurar. O Mirage compara a estrutura dos caminhos, identifica a pasta de perfil atual e pode encaixar nela o conteúdo do perfil antigo. Se o destino novo ainda não existir, o pacote fica pendente e é reavaliado ao retornar à Central; o app não recria silenciosamente o identificador obsoleto. Como jogos modificados podem usar caminhos próprios, confira os resultados antes de depender de um backup.

## Requisitos para usar

- Windows 10 ou Windows 11 de 64 bits.
- Discord para Windows aberto, quando o reconhecimento for necessário.
- Microsoft WebView2 Runtime. Ele normalmente já acompanha o Windows 11; o instalador do Mirage obtém o componente oficial se estiver ausente.
- Internet apenas para instalar um WebView2 ausente, atualizar o catálogo ou carregar imagens. O catálogo embutido continua disponível offline.

O .NET necessário acompanha o Mirage. Quem instala ou usa a versão portátil não precisa instalar Node.js, Visual Studio ou o SDK do .NET.

## Privacidade e serviços externos

O aplicativo atualiza o catálogo básico pelo endpoint público de jogos detectáveis do Discord e carrega capas pelos CDNs do Discord e da Steam. A busca e o backup de saves ocorrem no computador; o Mirage não envia esses arquivos. Consulte [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) para as fontes e licenças.

## Dependências de desenvolvimento

Requisitos: Windows x64, .NET SDK 10, Node.js 24 e PowerShell 7.

```powershell
npm --prefix source/Frontend ci
npm --prefix source/Frontend test
dotnet run --project source/Tests/Mirage.Tests.csproj -c Release -- --workspace
dotnet build source/Mirage.csproj -c Release
```

Para gerar o executável, o instalador, o pacote portátil e os checksums:

```powershell
dotnet publish source/Mirage.csproj -c Release -r win-x64 --self-contained true -o work/release
./tools/build-installer.ps1 -PublishDir work/release -OutputDir dist
```

O workflow em `.github/workflows/release.yml` executa os testes e anexa os pacotes a uma Release ao receber uma tag que corresponda à versão do projeto, como `v1.0.0`.

## Licença

O código autoral do Mirage é distribuído sob a licença [MIT](LICENSE). Nomes, marcas, capas e dados de terceiros pertencem aos respectivos titulares.
