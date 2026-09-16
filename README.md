# PromptBench

PromptBench é uma API em desenvolvimento para trabalhar com conjuntos versionados de casos de avaliação de prompts.

Atualmente, a aplicação descobre e valida Evaluation Sets armazenados em JSON, executa cada conjunto em um modelo e compara execuções sequenciais entre vários modelos informados explicitamente por meio do OpenRouter.

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

Configure a chave durante o desenvolvimento com User Secrets:

```bash
dotnet user-secrets set "OpenRouter:ApiKey" "SUA_CHAVE" --project src/PromptBench.Api
```

Também é possível usar a variável de ambiente `OpenRouter__ApiKey`. Nunca armazene uma chave real nos arquivos versionados do projeto.

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
- `POST /evals/{name}/runs` — executa sequencialmente todos os casos do eval no modelo OpenRouter informado.
- POST /evals/{name}/comparisons — coloca lado a lado as execuções do mesmo eval em pelo menos dois modelos distintos.

### Executar um Evaluation Set

O modelo é obrigatório e não possui valor padrão:

```http
POST /evals/summarization-basic/runs
Content-Type: application/json

{
  "model": "provider/model"
}
```

Resposta resumida:

```json
{
  "evaluation": "summarization-basic",
  "requestedModel": "provider/model",
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

## Comparar modelos

A comparação executa o mesmo Evaluation Set sequencialmente em cada modelo, preservando outputs, duração, tokens e o modelo efetivamente utilizado quando informado pelo OpenRouter.

```http
POST /evals/summarization-basic/comparisons
Content-Type: application/json

{
  "models": [
    "provider/modelo-a:free",
    "provider/modelo-b:free"
  ]
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

Os arquivos são gravados assincronamente em `runs/<id>.json`, dentro do diretório base da aplicação. O caminho é resolvido com `AppContext.BaseDirectory`, portanto não depende do current working directory. Resultados de casos, evaluators, LLM-as-a-judge, modelos, métricas, status e falhas presentes na resposta são preservados no arquivo.

Para consultar um resultado persistido:

```http
GET /runs/17fcf7ea-a4a4-49ca-8c46-e02b0325d394
```

O endpoint devolve o mesmo JSON produzido originalmente. Um ID inexistente retorna HTTP 404. Arquivos inválidos ou falhas de leitura retornam um erro controlado, sem expor caminhos internos ou stack traces.

Não há listagem, filtros, paginação nem limpeza automática dos arquivos nesta versão.

## Estrutura

```text
PromptBench.slnx
src/
  PromptBench.Api/
    Evals/
    OpenRouter/
    Runs/
tests/
  PromptBench.Tests/
prompts/
evals/
```

## Roadmap

Novos evaluators, scoring agregado, ranking e escolha de vencedor não fazem parte da implementação atual.
