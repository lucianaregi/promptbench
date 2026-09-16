---
name: commit
description: Prepara e cria commits focados para o PromptBench usando Conventional Commits. Use quando a pessoa pedir para criar um commit do trabalho atual; não use para operações de push ou alterações de código não relacionadas.
---

# Criar commit

Crie um commit somente para uma unidade de trabalho coerente.

## Fluxo

1. Execute `git status` e revise os arquivos rastreados, não rastreados e no staging.
2. Revise os diffs relevantes do working tree e do staging. Identifique arquivos gerados, configurações locais, secrets, credenciais, chaves de API, tokens e alterações não relacionadas.
3. Confirme que os arquivos pretendidos formam uma única unidade coerente. Não inclua silenciosamente alterações não relacionadas; pergunte à pessoa quando houver ambiguidade relevante sobre autoria ou escopo.
4. Execute `dotnet build` e depois `dotnet test`. Se algum deles falhar, pare antes do staging ou do commit e informe a falha. Não altere o código da aplicação apenas para viabilizar o commit.
5. Adicione ao staging somente os caminhos revisados que pertencem à unidade de trabalho. Nunca use `git add .` automaticamente.
6. Revise `git status` e `git diff --cached`. Pare se o conteúdo no staging incluir secrets, credenciais, arquivos locais, artefatos gerados ou alterações não relacionadas.
7. Crie o commit com uma mensagem Conventional Commits curta, clara e descritiva, escrita em português brasileiro. Escolha um tipo adequado, como `feat`, `fix`, `test`, `docs`, `refactor`, `chore`, `build` ou `ci`, com base na alteração real.
8. Execute `git status` novamente e informe o resultado do commit e quaisquer alterações restantes.

Bons exemplos incluem `feat: adiciona carregamento de conjuntos de avaliação`, `test: adiciona teste de integração do endpoint de health`, `docs: documenta formato dos conjuntos de avaliação` e `chore: inicializa estrutura do projeto`. Nunca use mensagens vagas como `chore: atualiza arquivos` ou `fix: corrige coisas`.

## Restrições

- Nunca faça push.
- Nunca use `--no-verify` ou `--force`.
- Nunca faça amend de um commit existente, salvo quando a pessoa solicitar explicitamente.
- Não ignore falhas de build ou de testes.
- Não adicione `Co-authored-by` nem outras assinaturas do agente.
- Não mencione IA, Codex ou geração automática na mensagem do commit.
- Não divida desnecessariamente uma alteração coerente nem combine alterações claramente independentes.
