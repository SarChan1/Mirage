# Mirage para Windows

O instalador funciona no Windows 10/11 de 64 bits. A instalação é feita na conta atual, em `%LOCALAPPDATA%\Programs\Mirage`, com atalho no menu Iniciar e opção de atalho na área de trabalho. Para remover, use Aplicativos instalados no Windows.

O .NET e a interface já acompanham o app. Quando o Microsoft WebView2 ainda não está instalado, o instalador usa o bootstrapper oficial da Microsoft para obtê-lo; essa etapa precisa de internet.

Favoritos, preferências e sequências ficam em `%LOCALAPPDATA%\Mirage`. Atualizar ou desinstalar preserva esses dados. Para atualizar, encerre o Mirage pelo ícone próximo ao relógio.

## Localizar saves

Selecione um jogo na biblioteca e clique em **Localizar saves** no painel de detalhes. A busca consulta locais mapeados para Windows e mostra os que existem neste computador. Ela reconhece instalações Steam, Epic e pastas registradas no Windows; o botão **Escolher pasta do jogo** permite indicar outras instalações.

Os caminhos mapeados vêm do Ludusavi Manifest, distribuído sob licença MIT. O app também procura pastas locais comuns e perfis alternativos, exibidos como **Possível save**. Esses candidatos precisam ser conferidos pelo usuário. A busca não lê o conteúdo dos saves; somente uma exportação iniciada pelo usuário copia os arquivos para o destino escolhido. Nada é enviado a servidores.

## Central de saves

A Central de saves separa jogos instalados de saves órfãos que permaneceram depois da remoção de um jogo. Ela apresenta datas, quantidade de arquivos e tamanho estimado. O resultado é mantido ao trocar de aba e ao reabrir o app. Uma verificação incremental confere locais conhecidos; a busca completa permanece disponível em **Verificar agora**.

O usuário pode criar uma cópia organizada em pasta ou um ZIP no local que escolher e restaurá-la depois. Antes de reinserir arquivos, o Mirage mostra destinos, conflitos e jogos sem pasta disponível. **Mesclar com segurança** preserva saves atuais e separa conflitos para revisão. **Substituir com ponto de restauração** cria um ZIP dos arquivos atuais antes de trocá-los. Quando o jogo não está instalado ou ainda não criou uma pasta, os arquivos ficam guardados em `%LOCALAPPDATA%\Mirage\Imported Saves`, com um manifesto que permite retomar a reinserção mais tarde.

Depois de reinstalar um jogo, recomenda-se abri-lo e criar um save uma vez: alguns títulos geram uma pasta, um perfil ou identificador novo somente nessa etapa. O Mirage reconhece identificadores variáveis, compara o caminho antigo com os perfis atuais e oferece a pasta nova compatível como destino. Enquanto ela não existir, a reinserção permanece pendente em vez de recriar o perfil antigo. A Central reavalia esses pacotes ao ser aberta novamente ou pelo botão **Rever pendentes**. A exportação apenas copia arquivos existentes; os originais não são movidos, substituídos ou apagados.

Um jogo instalado pode ainda não ter saves, e versões modificadas podem usar caminhos diferentes. A busca não garante encontrar todos os jogos, contas ou formatos. Saves também podem permanecer depois de desinstalar um jogo. Parte dos padrões de localização ainda não é suportada.

As licenças dos componentes acompanham os recursos da interface. As regras de localização de saves são derivadas do Ludusavi Manifest: https://github.com/mtkennerly/ludusavi-manifest

O Mirage usa somente o modo por processo e não exige configuração de chave de API.

## Compilar a distribuição

```
pwsh -File tools/build-installer.ps1
```

O script usa um compilador Inno Setup existente, ou o pacote portátil de desenvolvimento fixado no script. Não instala o compilador no sistema. O resultado inclui o instalador, o ZIP portátil e seus checksums na pasta `dist`.
