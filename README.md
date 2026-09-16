# PromptBench

PromptBench é uma API em desenvolvimento para trabalhar com conjuntos versionados de casos de avaliação de prompts.

Atualmente, a aplicação descobre e valida Evaluation Sets armazenados em JSON e pode executá-los sequencialmente em um modelo informado explicitamente por meio do OpenRouter.

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

## Endpoints

- `GET /health` — confirma que a API está funcionando.
- `GET /evals` — lista nome, descrição e quantidade de casos dos evals válidos.
- `GET /evals/{name}` — retorna o eval completo; responde 404 quando ele não existe e 422 quando o arquivo é inválido.
- `POST /evals/{name}/runs` — executa sequencialmente todos os casos do eval no modelo OpenRouter informado.

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

Comparação entre modelos, avaliação automática e scoring são ideias para etapas futuras.