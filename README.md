# PromptBench

PromptBench é uma API em desenvolvimento para trabalhar com conjuntos versionados de casos de avaliação de prompts.

Atualmente, a aplicação descobre, valida e expõe Evaluation Sets armazenados em JSON. Ela ainda não executa prompts nem se integra a modelos de linguagem.

## Stack

- C# e .NET 10
- ASP.NET Core Minimal APIs
- OpenAPI com Swagger UI
- xUnit

## Requisitos

- .NET SDK 10

## Como executar

```bash
dotnet restore
dotnet build
dotnet run --project src/PromptBench.Api
```

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

## Estrutura

```text
PromptBench.slnx
src/
  PromptBench.Api/
    Evals/
tests/
  PromptBench.Tests/
prompts/
evals/
```

## Roadmap

Execução e avaliação de prompts e comparação de resultados são ideias para etapas futuras.
