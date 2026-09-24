# PromptBench

PromptBench é uma API em desenvolvimento para trabalhar com conjuntos versionados de casos de avaliação de prompts.

Atualmente, a aplicação descobre e valida Evaluation Sets e prompts versionados armazenados em JSON, executa avaliações por meio do OpenRouter, persiste os resultados localmente e compara execuções históricas para detectar regressões.

## Stack

- C# e .NET 10
- ASP.NET Core Minimal APIs
- OpenAPI com Swagger UI
- OpenRouter
- xUnit

## Requisitos

- .NET SDK 10
- Uma API key do OpenRouter para executar evals

## Como executar

```bash
dotnet restore
dotnet build
dotnet run --project src/PromptBench.Api
```

Configure a chave durante o desenvolvimento em `src/PromptBench.Api/appsettings.Local.json`. O arquivo é opcional, não é versionado e pode ser criado a partir de `src/PromptBench.Api/appsettings.Local.example.json`:

```json
{
  "OpenRouter": {
    "ApiKey": "COLOQUE_A_CHAVE_AQUI"
  }
}
```

User Secrets e a variável de ambiente `OpenRouter__ApiKey` continuam disponíveis e têm precedência sobre o arquivo local:

```bash
dotnet user-secrets set "OpenRouter:ApiKey" "SUA_CHAVE" --project src/PromptBench.Api
```

Nunca armazene uma chave real nos arquivos versionados do projeto.

Com o perfil HTTP de desenvolvimento, o Swagger fica disponível em `http://localhost:5146/swagger`.

Para executar os testes:

```bash
dotnet test
```

## Evaluation Sets

Um Evaluation Set reúne casos de entrada e o resultado esperado para avaliações futuras. Os arquivos ficam em `evals/`, são versionados com o projeto e usam este formato:

```json
{
  "name": "resumo-basico",
  "description": "Casos básicos de avaliação para resumo de textos.",
  "cases": [
    {
      "id": "artigo-curto",
      "input": "A biblioteca do bairro ampliou o horário de funcionamento e agora também abre aos domingos.",
      "expected": "A biblioteca passou a abrir aos domingos."
    }
  ]
}
```

`name` e `description` são obrigatórios. `cases` deve conter pelo menos um item; cada caso precisa de `id`, `input` e `expected`, e seus IDs devem ser únicos dentro do conjunto.

O repositório inclui o exemplo funcional `evals/summarization-basic.json`.

## Prompts versionados

Os prompts ficam em `prompts/` e são identificados explicitamente por `name` e `version`. Cada arquivo usa este formato:

```json
{
  "name": "summarization",
  "version": "v1",
  "template": "Resuma o texto a seguir de forma objetiva:\n\n{{input}}"
}
```

`name`, `version` e `template` são obrigatórios, a combinação de nome e versão deve ser única e o template precisa conter `{{input}}`. Durante a execução, essa variável é substituída pelo input de cada caso. O exemplo disponível no repositório é `prompts/summarization-v1.json`.

## Avaliação com LLM-as-a-judge

Um Evaluation Set pode configurar o evaluator `llm_judge` para que um segundo modelo avalie cada output produzido. O modelo judge é sempre informado explicitamente; o PromptBench não escolhe modelos automaticamente.

```json
{
  "name": "resumo-com-judge",
  "description": "Avaliação de resumos com apoio de um judge.",
  "evaluation": {
    "type": "llm_judge",
    "judgeModel": "provider/model:free",
    "criteria": "O resumo deve preservar as informações principais do texto original sem acrescentar informações inexistentes."
  },
  "cases": [
    {
      "id": "biblioteca-aos-domingos",
      "input": "A biblioteca do bairro ampliou o horário de funcionamento e agora também abre aos domingos.",
      "expected": "A biblioteca passou a abrir aos domingos."
    }
  ]
}
```

Para cada caso, a geração do output e o julgamento são chamadas separadas ao OpenRouter. O prompt do judge recebe um bloco JSON delimitado com o input original, a resposta de referência, o output produzido e o critério. A resposta é solicitada com JSON Schema estrito e validada antes de ser usada.

Resultado resumido do evaluator:

```json
{
  "type": "llm_judge",
  "status": "completed",
  "passed": true,
  "reason": "A resposta mantém a informação principal do texto.",
  "requestedModel": "provider/model:free",
  "usedModel": "provider/model:free",
  "durationMs": 420,
  "usage": {
    "promptTokens": 80,
    "completionTokens": 18,
    "totalTokens": 98
  },
  "error": null
}
```

Uma reprovação possui `status` igual a `completed` e `passed` igual a `false`. Se o judge retornar conteúdo inválido, exceder o tempo limite ou falhar no OpenRouter, o resultado terá `status` igual a `failed`, `passed` igual a `null` e detalhes técnicos seguros em `error`. Assim, uma falha do judge não é convertida em reprovação e não elimina o output produzido. Não há retry automático.

O `llm_judge` funciona tanto nas execuções individuais quanto nas comparações. O PromptBench não implementa votação, pesos nem ranking automático dos modelos.

## Endpoints

- `GET /health` — confirma que a API está funcionando.
- `GET /evals` — lista nome, descrição e quantidade de casos dos evals válidos.
- `GET /evals/{name}` — retorna o eval completo; responde 404 quando ele não existe e 422 quando o arquivo é inválido.
- `GET /prompts` — lista os prompts versionados disponíveis.
- `POST /evals/{name}/runs` — executa sequencialmente todos os casos do eval no modelo OpenRouter informado.
- `POST /evals/{name}/comparisons` — coloca lado a lado as execuções do mesmo eval em pelo menos dois modelos distintos.
- `GET /runs` — lista resumos das execuções persistidas, da mais recente para a mais antiga.
- `GET /runs/{id}` — retorna uma execução ou comparação persistida; responde 404 quando o ID não existe.
- `GET /runs/{baselineId}/compare/{candidateId}` — compara duas execuções individuais persistidas do mesmo Evaluation Set.
- `GET /runs/{baselineId}/compare/{candidateId}/gate` — verifica se o candidate introduziu regressões.
- `GET /runs/{baselineId}/compare/{candidateId}/report` — gera e salva o relatório Markdown da comparação.

### Executar um Evaluation Set

O modelo, o nome do prompt e sua versão são obrigatórios e não possuem valores padrão:

```http
POST /evals/summarization-basic/runs
Content-Type: application/json

{
  "model": "provider/model",
  "promptName": "summarization",
  "promptVersion": "v1"
}
```

Resposta resumida:

```json
{
  "evaluation": "summarization-basic",
  "requestedModel": "provider/model",
  "promptName": "summarization",
  "promptVersion": "v1",
  "startedAt": "2026-09-16T12:00:00Z",
  "durationMs": 1234,
  "results": [
    {
      "caseId": "biblioteca-aos-domingos",
      "input": "A biblioteca do bairro ampliou o horário de funcionamento e agora também abre aos domingos.",
      "expected": "A biblioteca passou a abrir aos domingos.",
      "output": "A biblioteca agora funciona também aos domingos.",
      "usedModel": "provider/model",
      "durationMs": 600,
      "usage": {
        "promptTokens": 20,
        "completionTokens": 10,
        "totalTokens": 30
      }
    }
  ]
}
```

O campo `usedModel` registra o modelo efetivamente informado pelo OpenRouter quando disponível. Os dados de `usage` são opcionais e não são calculados localmente.

A disponibilidade, os identificadores e os limites dos modelos são definidos pelo OpenRouter. Consulte o [catálogo atual de modelos](https://openrouter.ai/models) antes de executar um eval.

### Smoke test com OpenRouter

Depois de inserir a chave no arquivo local, inicie a API:

```bash
dotnet run --project src/PromptBench.Api
```

Em outro terminal, execute o Evaluation Set existente com um modelo gratuito informado explicitamente:

```bash
curl -X POST http://localhost:5146/evals/summarization-basic/runs \
  -H "Content-Type: application/json" \
  -d '{"model":"qwen/qwen3-4b:free","promptName":"summarization","promptVersion":"v1"}'
```

A disponibilidade de modelos gratuitos pode mudar; confirme o identificador no catálogo do OpenRouter antes do teste.

## Comparar modelos

A comparação executa o mesmo Evaluation Set sequencialmente em cada modelo, preservando outputs, duração, tokens e o modelo efetivamente utilizado quando informado pelo OpenRouter.

```http
POST /evals/summarization-basic/comparisons
Content-Type: application/json

{
  "models": [
    "provider/modelo-a:free",
    "provider/modelo-b:free"
  ],
  "promptName": "summarization",
  "promptVersion": "v1"
}
```

A requisição exige pelo menos dois modelos distintos. Identificadores duplicados, inclusive com diferenças apenas entre maiúsculas e minúsculas, são rejeitados com HTTP 400.

Resposta resumida:

```json
{
  "evaluation": "summarization-basic",
  "startedAt": "2026-09-16T15:00:00Z",
  "durationMs": 2500,
  "status": "partial",
  "promptName": "summarization",
  "promptVersion": "v1",
  "runs": [
    {
      "requestedModel": "provider/modelo-a:free",
      "actualModel": "provider/modelo-a:free",
      "status": "completed",
      "durationMs": 1100,
      "promptTokens": 44,
      "completionTokens": 27,
      "totalTokens": 71,
      "results": [
        {
          "caseId": "biblioteca-aos-domingos",
          "expected": "A biblioteca passou a abrir aos domingos.",
          "output": "A biblioteca agora também abre aos domingos.",
          "durationMs": 500
        }
      ],
      "error": null
    },
    {
      "requestedModel": "provider/modelo-b:free",
      "actualModel": null,
      "status": "failed",
      "durationMs": 200,
      "promptTokens": null,
      "completionTokens": null,
      "totalTokens": null,
      "results": [],
      "error": {
        "type": "rate_limit",
        "message": "O limite de requisições do OpenRouter foi atingido."
      }
    }
  ]
}
```

Uma falha interrompe somente o run do modelo afetado; os modelos seguintes continuam sem retry automático. O status da comparação é `completed`, `partial` ou `failed`. Os totais de tokens ficam nulos quando os dados necessários não estão completos.

Modelos gratuitos podem ter disponibilidade e rate limits diferentes. O PromptBench apenas apresenta resultados observáveis lado a lado: ele ainda não atribui score, ranking, vencedor nem decide qual resposta é melhor.

## Persistência local dos resultados

Ao concluir com sucesso um run individual ou uma comparação, o PromptBench gera um `Guid`, inclui esse valor no campo `id` da resposta e salva o mesmo resultado completo como JSON. O campo `type` distingue `evaluation_run` de `comparison`.

Os arquivos são gravados assincronamente em `runs/<id>.json`, dentro do diretório base da aplicação. O caminho é resolvido com `AppContext.BaseDirectory`, portanto não depende do current working directory. Resultados de casos, evaluators, LLM-as-a-judge, modelos, métricas, status, falhas e a identidade `promptName`/`promptVersion` são preservados no arquivo. Runs anteriores à introdução desses campos continuam legíveis e podem apresentá-los como `null`.

Para listar resumos sem carregar outputs e resultados detalhados:

```http
GET /runs
```

Cada resumo contém ID, tipo, Evaluation Set, data/hora, modelos, status e `passRate` quando existem julgamentos concluídos. Arquivos JSON inválidos são ignorados sem impedir o retorno das execuções válidas. Um diretório vazio retorna HTTP 200 com `[]`.

Para consultar um resultado persistido:

```http
GET /runs/17fcf7ea-a4a4-49ca-8c46-e02b0325d394
```

O endpoint devolve o mesmo JSON produzido originalmente. Um ID inexistente retorna HTTP 404. Arquivos inválidos ou falhas de leitura retornam um erro controlado, sem expor caminhos internos ou stack traces.

Não há filtros, paginação nem limpeza automática dos arquivos nesta versão.

## Comparar execuções persistidas

Duas execuções individuais do mesmo Evaluation Set podem ser comparadas usando a primeira como referência:

```http
GET /runs/11111111-1111-1111-1111-111111111111/compare/22222222-2222-2222-2222-222222222222
```

Os casos são associados por `caseId`. O resultado usa `unchanged_pass` quando ambos passaram, `unchanged_fail` quando ambos falharam, `regression` quando apenas o baseline passou e `improvement` quando apenas o candidate passou. Casos exclusivos do candidate são marcados como `added`; casos exclusivos do baseline, como `removed`. Eles não são contados falsamente como regressão ou melhoria.

O resumo informa as quantidades de cada classificação. As métricas apresentam valores de baseline, candidate e a diferença `candidate - baseline` para taxa de aprovação, duração e total de tokens, quando disponíveis. Esses valores não produzem ranking nem escolhem um modelo vencedor.

As duas execuções precisam ser do tipo `evaluation_run`, pertencer ao mesmo Evaluation Set e possuir um resultado de avaliação concluído para cada caso. O resultado expõe `baselinePromptName`, `baselinePromptVersion`, `candidatePromptName` e `candidatePromptVersion`; em runs antigos, esses campos podem ser `null`. Versões diferentes podem ser comparadas e essa diferença não é classificada como regressão. Comparações multi-modelo persistidas ou runs sem `passed` possuem dados insuficientes para esta operação.

## Gate de regressão

O gate permite que automações verifiquem regressões sem interpretar o resultado completo da comparação:

```http
GET /runs/11111111-1111-1111-1111-111111111111/compare/22222222-2222-2222-2222-222222222222/gate
```

Sem regressões, o endpoint retorna HTTP 200:

```json
{
  "passed": true,
  "regressions": 0
}
```

Quando existem regressões, retorna HTTP 409 Conflict e informa os casos afetados:

```json
{
  "passed": false,
  "regressions": 2,
  "regressionCaseIds": ["biblioteca-aos-domingos", "previsao-do-tempo"]
}
```

Somente casos já classificados como `regression` pela comparação histórica fazem o gate falhar. Melhorias, casos sem alteração e casos adicionados ou removidos não provocam falha. `passRate`, duração e tokens não participam da decisão.

### Executar o gate pela linha de comando

O mesmo gate pode ser executado sem iniciar a API HTTP:

```bash
dotnet run --project src/PromptBench.Cli -- regression-gate \
  --baseline 11111111-1111-1111-1111-111111111111 \
  --candidate 22222222-2222-2222-2222-222222222222
```

Também é possível informar diretamente dois arquivos no formato persistido pelo PromptBench:

```bash
dotnet run --project src/PromptBench.Cli -- regression-gate \
  --baseline-file ./runs/baseline.json \
  --candidate-file ./runs/candidate.json
```

Use um dos pares de opções por execução; não misture IDs e arquivos.

O CLI procura os arquivos em `runs` no diretório base da aplicação, como a API. Em uma comparação válida, o `stdout` contém somente JSON:

```json
{
  "passed": true,
  "baselineId": "11111111-1111-1111-1111-111111111111",
  "candidateId": "22222222-2222-2222-2222-222222222222",
  "regressions": 0,
  "regressionCaseIds": []
}
```

O processo termina com código `0` quando aprovado, `1` quando existem regressões e `2` para erros de uso, leitura ou comparação. Regressões também produzem o JSON completo antes do exit code `1`; diagnósticos do exit code `2` são escritos em pt-BR somente no `stderr`.

## Relatório Markdown da comparação

O relatório factual da comparação pode ser gerado com:

```http
GET /runs/11111111-1111-1111-1111-111111111111/compare/22222222-2222-2222-2222-222222222222/report
```

A resposta usa `text/markdown` e inclui identificação e data das execuções, modelos, prompts e versões, resumo, métricas, tabela de casos e uma seção de regressões. Quando o mesmo prompt usa versões diferentes, essa informação é apresentada de forma factual. Duração e tokens são apresentados sem inferir que valores menores representam maior qualidade. Casos adicionados e removidos permanecem identificados separadamente.

O mesmo conteúdo é salvo em `reports/<baseline-id>_vs_<candidate-id>.md`, dentro do diretório base da aplicação. O caminho não depende do current working directory. Se o arquivo já existir, a API retorna HTTP 409 e não o sobrescreve.

## GitHub Actions

Os workflows são manuais e não executam modelos nem acessam o OpenRouter:

- `Publicar run persistido` (`.github/workflows/publish-run.yml`) valida um arquivo informado por `runFile` e o publica como artifact `promptbench-run-<runId>`;
- `Regression gate` (`.github/workflows/regression.yml`) recebe os IDs dos workflow runs e dos runs de baseline e candidate, baixa os dois artifacts e executa o gate pelo `PromptBench.Cli`.

O workflow de regressão preserva o exit code do CLI: `0` aprova o job, `1` sinaliza regressões e `2` indica erro de execução ou configuração.

## Estrutura

```text
PromptBench.slnx
src/
  PromptBench.Api/
    Evals/
    OpenRouter/
    Prompts/
    Runs/
  PromptBench.Cli/
tests/
  PromptBench.Tests/
prompts/
evals/
docs/
.github/workflows/
```

## Limites atuais

O projeto não implementa banco de dados, frontend, ranking, escolha de vencedor, votação entre judges, pesos ou cálculo de custo.
