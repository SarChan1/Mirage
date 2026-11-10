# Mirage interface

React + TypeScript + Tailwind CSS + Motion. Executada dentro do WebView2 no app de Windows.

`npm run build` verifica TypeScript e gera os arquivos em `dist`. `npm test` verifica os formatos de tempo e a validação dos minutos. O projeto .NET também executa a compilação da interface quando os arquivos mudam.

O comando `npm run dev` serve os arquivos para desenvolvimento visual. As ações nativas exigem o host WebView2 e não são substituídas por dados simulados no navegador comum.

Os testes integrados são ativados somente pelo host em modo `--web-smoke`. Esse modo bloqueia a gravação de preferências e não inicia sessões reais.
