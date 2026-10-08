# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e versionamento conforme [SemVer](https://semver.org/lang/pt-BR/).

A versão declarada aqui é a mesma de `Directory.Build.props`, a mesma injetada
na imagem Docker e a mesma devolvida por `GET /health`. Uma única fonte — é o
que permite confirmar com um `curl` qual versão está no ar depois de um rollback.

---

## [1.1.0] — 2026-10-08

Entrega 2: containerização, CD, gestão de segredos, versionamento e rollback.

### Adicionado

- **Imagens Docker** (`Dockerfile.api`, `Dockerfile.web`): multi-stage com
  camada de restore separada para aproveitamento de cache, usuário sem
  privilégio, `HEALTHCHECK` e rótulos OCI com a versão.
- **Orquestração local** (`docker-compose.yml`): aplicação, MongoDB com volume
  nomeado e Redis, encadeados por `depends_on` com `condition: service_healthy`.
- **Cache de leitura opcional** (`CachedUrlStore`): decorator de `IUrlStore`
  sobre Redis, ativado só quando existe `ConnectionStrings__Redis`. Tolerante a
  falha — Redis fora do ar degrada para o Mongo em vez de derrubar a API.
- **Endpoints de saúde**: `GET /health` (liveness, devolve a versão) e
  `GET /health/ready` (readiness, faz ping no Mongo com teto de 3s).
- **`Encurtai:BaseUrl`**: endereço público configurável para a URL curta.
- **Pipeline de CD** (`.github/workflows/cd.yml`): publica as duas imagens no
  GHCR, implanta em cluster kind com smoke test ponta a ponta, reverte sozinho
  quando o rollout não converge, e tem gate de aprovação manual para produção.
- **Manifestos Kubernetes** (`k8s/`): probes, limites de recurso,
  `securityContext` sem privilégio e `RollingUpdate` com `maxUnavailable: 0`.
- **Varredura de segredos** (gitleaks) e **verificação de formatação**
  (`dotnet format`) no CI.
- **Reprodutibilidade**: `global.json`, `Directory.Build.props`,
  `packages.lock.json` e `.gitattributes`.
- **Workflow de evidência de rollback** (`.github/workflows/rollback-demo.yml`):
  provoca uma falha de readiness de propósito, prova que o pod quebrado nunca
  entra nos endpoints do Service, e reverte — tudo numa execução, no mesmo
  cluster. Existe separado do CD porque o CD cria um cluster novo a cada
  execução, onde um Deployment de uma só revisão não tem para onde voltar.
- Documentação: `docs/segredos.md`, `docs/rollback.md`,
  `docs/evidencias-entrega-2.md` (com as evidências de execução preenchidas).

### Modificado

- **Testes separados por categoria no CI**: o trait `Category=Integration` já
  existia no código mas nunca era usado como filtro. Agora os unitários rodam
  sem banco e só os de integração sobem o `mongo:7`.
- **CORS deixou de ser aberto em qualquer ambiente**: passou a allowlist por
  configuração, com o modo permissivo restrito a `Development`. Com o nginx
  encaminhando `/api/`, o tráfego de produção é same-origin.
- **CI endurecido**: `permissions` explícito por job, `concurrency` com
  cancelamento, cache de NuGet, cobertura de código e publicação de artefatos.
- `.gitignore`: `.env` sozinho não cobria `.env.local` nem `.env.production`.

### Corrigido

- **URL curta apontava para o host interno do container.** A resposta de
  `POST /encurtar` era montada com `Request.Scheme`/`Request.Host`, que atrás de
  nginx ou ingress devolve o endereço interno — o encurtador geraria links
  quebrados exatamente no ambiente containerizado. Agora há
  `Encurtai:BaseUrl`, com `UseForwardedHeaders` como rede de segurança, e
  cobertura em `ShortUrlBuilderTests`.
- **URL da API cravada no frontend.** `Encurtai.Web` fixava
  `http://localhost:5080`. Sendo Blazor WebAssembly, o código roda no navegador
  e variável de ambiente não o alcança. Agora o endereço vem de
  `wwwroot/appsettings.json`, lido em tempo de execução.
- **Erro de configuração era silencioso.** O default `mongodb://localhost:27017`
  em `appsettings.json` neutralizava a validação de `Program.cs`: dentro de um
  container, `localhost` é o próprio container, então a imagem subia "saudável"
  e só quebrava na primeira escrita. O default saiu; a falha agora é no boot.
- **Banco fora do ar devolvia 500 com stack trace.** Passou a devolver `503`
  com corpo JSON enxuto. `500` diz "a aplicação tem defeito"; `503` diz "a
  dependência está fora", que é o sinal correto para cliente, proxy e probe.
- **Readiness demorava 30s para falhar.** O timeout padrão de seleção de
  servidor do driver Mongo prendia a requisição. Reduzido para 5s configuráveis,
  com teto de 3s dentro do próprio endpoint.
- Resíduo de teste `// teste ruleset 3` removido do `README.md`, e as três
  afirmações defasadas sobre armazenamento em memória foram corrigidas.

---

## [1.0.0] — 2026-10-01

Entrega 1: aplicação e integração contínua.

### Adicionado

- Encurtador de URLs em .NET 9: `Encurtai.Api` (Minimal API),
  `Encurtai.Web` (Blazor WebAssembly) e `Encurtai.Tests` (xUnit).
- `POST /encurtar` com validação restrita a `http`/`https` e tratamento de
  colisão de código; `GET /{codigo}` com redirecionamento.
- Persistência em MongoDB (`MongoUrlStore`), com o código curto como `_id` —
  unicidade sem índice adicional.
- Pipeline de CI no GitHub Actions com service container `mongo:7`.
- Testes unitários e de integração.

---

<!--
Ao publicar uma versão, crie a tag anotada — é ela que dispara o CD e nomeia
as imagens no GHCR:

  git tag -a v1.1.0 -m "Entrega 2: containerizacao, CD, segredos e rollback"
  git push origin v1.1.0

Para desfazer uma tag ainda não divulgada:

  git tag -d v1.1.0 && git push origin :refs/tags/v1.1.0
-->

[1.1.0]: https://github.com/MarcosVogado/Encurtai/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/MarcosVogado/Encurtai/releases/tag/v1.0.0
