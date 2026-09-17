---
name: readme
description: Cria ou atualiza o README.md em português brasileiro com base no estado real do repositório. Use quando a pessoa pedir para criar, corrigir, revisar ou sincronizar a documentação principal do projeto.
---

# Manter o README

Crie ou atualize o `README.md` para refletir somente funcionalidades, configuração e formas de uso comprovadas no repositório.

Preserve conteúdo existente que continue correto e útil. Faça mudanças pontuais: não reescreva o arquivo inteiro quando bastar corrigir, remover ou acrescentar trechos específicos.

## Fluxo

1. Examine o estado do repositório e o README existente. Use `git status`, `git diff` e buscas direcionadas para distinguir o código atual de alterações ainda não concluídas.
2. Identifique a tecnologia e os comandos reais nos arquivos de projeto, solução, configuração, scripts e automações existentes.
3. Confirme funcionalidades e contratos diretamente no código. Para APIs, verifique rotas, métodos HTTP, modelos de request/response, códigos de status, persistência e configuração relacionada.
4. Consulte testes e exemplos executáveis para confirmar comportamento, casos de erro e formato de uso. Não trate nomes de testes ou comentários isolados como implementação suficiente quando o fluxo real disser o contrário.
5. Compare as evidências com o README. Preserve o que estiver correto; atualize somente conteúdo desatualizado, incorreto, ausente ou repetido.
6. Quando útil, execute validações proporcionais, como comandos documentados, `dotnet build` e `dotnet test`. Não altere código ou configuração para fazer a documentação parecer válida.
7. Revise o diff final do `README.md` e confirme que exemplos, caminhos, endpoints e nomes técnicos correspondem ao repositório.

## Conteúdo esperado

Inclua apenas se houver suporte real no projeto:

- uma explicação direta do propósito e das funcionalidades atuais;
- requisitos e configuração necessária;
- instruções para executar a aplicação;
- endpoints ou fluxos principais de uso;
- exemplos curtos que esclareçam entradas e resultados;
- como executar os testes.

Código, comandos, caminhos, nomes de tipos, propriedades, campos e outros identificadores técnicos permanecem em inglês. Todo texto destinado a pessoas deve estar em português brasileiro.

Adapte a estrutura ao projeto. Não crie seções vazias e não repita a mesma informação em locais diferentes.

## Critérios de escrita

- Use linguagem objetiva, natural e técnica, sem tom de marketing.
- Evite introduções genéricas, slogans, excesso de adjetivos e frases artificiais.
- Use títulos e listas somente quando facilitarem a consulta.
- Prefira exemplos pequenos e válidos a descrições extensas.
- Não documente funcionalidades planejadas, presumidas ou desejadas.
- Não invente roadmap, arquitetura, convenções ou decisões que o código não demonstre.
- Antes de remover uma informação, confirme no repositório que ela deixou de ser válida.
- Se uma informação relevante não puder ser confirmada, não a apresente como fato; registre a limitação no resumo da tarefa.

## Segurança

Nunca inclua API keys, tokens, credenciais, segredos ou valores reais de configuração sensível.

Use placeholders inequívocos, como `SUA_CHAVE`, e confirme que exemplos não copiam valores de arquivos locais, variáveis de ambiente, User Secrets, logs ou artefatos gerados.

## Restrições

- Altere somente o `README.md`, salvo solicitação explícita em contrário.
- Não modifique código, testes, configuração, scripts ou outros documentos.
- Não faça commit, push, amend nem operações destrutivas.
- Não invente informações para preencher seções.
- Ao finalizar, informe resumidamente o que foi atualizado e quais validações foram executadas.
