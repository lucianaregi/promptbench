---
name: dotnet-code-review
description: Faz revisão pragmática e contextual de código .NET e ASP.NET Core, cobrindo correção, segurança, contratos, qualidade, manutenção e testes. Use quando a pessoa pedir revisão de código, revisão de PR, análise de mudanças ou avaliação de qualidade em projetos C#/.NET.
---

# Revisão de código .NET

Faça uma revisão técnica orientada a riscos e ao contexto do projeto. Encontre problemas concretos e acionáveis sem reescrever o código nem impor preferências arquiteturais.

Código simples, correto e consistente deve permanecer simples. Não sugira Clean Architecture, Repository Pattern, interfaces para toda classe, factories, novas camadas ou outras abstrações sem apontar um problema concreto que elas resolvam e demonstrar que o benefício compensa a complexidade.

## Fluxo

1. Identifique o escopo com `git status`, `git diff` e, quando necessário, `git diff --cached`. Priorize as mudanças em revisão e não atribua ao autor problemas preexistentes sem explicar a relação com o diff.
2. Leia o código alterado junto com os tipos, endpoints, serviços, persistência, configuração e testes diretamente relacionados. Siga o fluxo real dos dados até a fronteira externa ou o efeito colateral.
3. Verifique o contrato público: códigos HTTP, validação, serialização, nullable reference types, cancelamento, tratamento de erros e compatibilidade com consumidores existentes.
4. Verifique segurança e operação: segredos em arquivos ou logs, entrada não confiável, autorização, SSRF, exposição de detalhes internos, timeouts, retries, limites, descarte de recursos e configuração por ambiente.
5. Verifique comportamento .NET: async/await, propagação de `CancellationToken`, concorrência, ciclo de vida de DI, uso de `HttpClient`, streams, exceções, igualdade e cultura, persistência, atomicidade e dados ausentes.
6. Verifique qualidade no contexto existente: legibilidade, manutenção, warnings do compilador, nulabilidade, duplicação relevante, complexidade desnecessária, código morto, testabilidade e consistência com a arquitetura já adotada.
7. Verifique os testes: cenários de sucesso e falha, limites, regressões relevantes, isolamento, determinismo e se exercitam o caminho alterado.
8. Execute as validações disponíveis no menor escopo útil. Em projetos .NET, use normalmente `dotnet build` e depois `dotnet test`, respeitando instruções do repositório. Não altere código para fazer a revisão passar.
9. Informe os achados por severidade. Para cada um, indique arquivo e linha, impacto e correção objetiva. Inclua condição reproduzível quando fizer sentido para demonstrar bug ou comportamento incorreto; não a force em achados de manutenção, clareza ou testabilidade.

## Julgamento pragmático

Avalie cada ponto pelo impacto no comportamento, na segurança, na operação ou no custo real de manutenção.

- Prefira a menor correção que trate a causa.
- Respeite padrões e decisões já adotados no repositório quando continuam adequados.
- Não transforme preferências de estilo em achados.
- Reporte duplicação apenas quando ela já provoca divergência, risco de erro ou manutenção claramente repetitiva.
- Reporte complexidade quando ela dificulta entender, testar ou alterar um fluxo relevante.
- Reporte warnings, nulabilidade e código morto conforme o risco concreto, sem inflar a severidade.
- Não peça abstrações “para o futuro” sem uma necessidade observável no escopo revisado.
- Não invente achados para preencher a revisão. Se a evidência for insuficiente, registre a dúvida ou suposição em vez de afirmar um defeito.

## Severidades

- **Crítico**: vulnerabilidade explorável, perda ou corrupção de dados, indisponibilidade ampla ou quebra grave com impacto imediato.
- **Importante**: bug relevante, regressão, quebra de contrato, risco de segurança, falha operacional ou problema de manutenção com impacto concreto.
- **Sugestão**: melhoria proporcional de legibilidade, testabilidade, consistência ou manutenção, sem comportamento incorreto demonstrado.

## Formato da resposta

Comece pelos achados, do mais grave ao menos grave:

```text
[Severidade] Título curto
Arquivo: caminho/arquivo.cs:linha
Problema: o que foi observado e qual é o impacto.
Condição: entrada, estado ou sequência que reproduz o problema, quando aplicável.
Correção: menor mudança que trata a causa.
```

Omita `Condição` quando ela não for pertinente.

Depois dos achados, registre:

- perguntas ou suposições que impeçam uma conclusão;
- validações executadas e seus resultados;
- lacunas de teste ou riscos residuais;
- uma síntese curta, somente quando acrescentar contexto útil.

Se não encontrar problemas, diga explicitamente que não encontrou achados e liste as validações executadas e os riscos residuais.

## Restrições

- A skill somente revisa: não altere código, testes, configuração ou documentação.
- Não faça commit, push, amend nem operações destrutivas.
- Todo feedback destinado à pessoa deve estar em português brasileiro; preserve identificadores técnicos em inglês.
- Não exponha valores de segredos, tokens ou chaves; mencione apenas o arquivo e o tipo de exposição.
- Não trate falha de teste não relacionada como defeito da mudança sem evidência.
- Não use `--no-verify` nem ignore falhas de build ou testes.
