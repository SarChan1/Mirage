# Arquitetura

O Mirage separa o controlador das interfaces. `MirageController` mantém catálogo, preferências e sessões; `WebWindow` hospeda a interface local React no WebView2 e encaminha apenas comandos validados.

## Segundo plano

Ao esconder ou minimizar a janela, a página é suspensa imediatamente. Após 15 segundos, o WebView2 e o catálogo completo são descartados. Permanecem apenas o controlador, a bandeja e os dados necessários para uma sessão ativa. Sem sessão, o temporizador é interrompido. Durante uma sessão escondida, a verificação ocorre a cada cinco segundos; visível, a interface é atualizada a cada segundo.

Ao restaurar, o catálogo local e a interface são recriados. Um snapshot pequeno em memória preserva a navegação. Nenhum estado visual é gravado em Documents.

O carregamento inicial inclui apenas biblioteca, sequência e navegação. Central de saves, localizador, configurações e rotinas de diagnóstico são divididos em módulos carregados sob demanda. Recursos com nome versionado usam cache imutável; a página principal permanece revalidável. Em português, a interface não mantém um observador de tradução ativo porque o texto-fonte já está no idioma correto.

## Dados e rede

- Preferências: `%LOCALAPPDATA%\Mirage\preferences.json`, com escrita atômica e confirmação antes de substituir arquivo ilegível.
- Catálogo: cópia embutida e atualização periódica pelo endpoint público do Discord.
- Imagens: CDNs do Discord e da Steam, bloqueando outras origens.
- Saves: busca local limitada por regras derivadas do catálogo MIT do Ludusavi, cache persistente, validação incremental, histórico limitado, separação de instalações e saves órfãos, exportação explícita e restauração com análise de conflitos, migração de identificadores de perfil, fila persistente de reinserções e rollback.
- Reconhecimento: processo auxiliar local com nome compatível com o catálogo.

Não há servidor local, telemetria, conta, anúncios ou chaves de API.

## Segurança da ponte

A página aceita somente a origem interna `https://mirage.local`. Navegação externa, novas janelas, permissões do navegador e objetos nativos são bloqueados. Identificadores e parâmetros são validados no C#; a interface não pode iniciar executáveis arbitrários.
