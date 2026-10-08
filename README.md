# Encurtaí ✂️ — Encurtador de URL

Projeto pequeno e descartável para **aprender DevOps / CI-CD na prática** com um
pipeline de verdade no GitHub Actions — e que rende uma boa peça de portfólio.

O app é simples de propósito: **a estrela é a esteira, não o encurtador.**

**Stack:** .NET 9 · ASP.NET Core Minimal API · Blazor WebAssembly · MongoDB ·
Redis · xUnit · Docker · Kubernetes · GitHub Actions

---

## O que ele faz

- `POST /encurtar` → recebe `{ "url": "https://..." }` e devolve um código curto + o link.
- `GET /{codigo}` → redireciona para a URL original.
- `GET /health` → liveness; devolve a versão em execução.
- `GET /health/ready` → readiness; só responde `200` se o MongoDB responder.
- Front Blazor com um campo, um botão e o resultado.

Armazenamento é **MongoDB**, com cache de leitura opcional em **Redis** (ativado
apenas quando `ConnectionStrings__Redis` está definida; sem ele, a API fala
direto com o banco).

---

## Estrutura

```
encurtai/
├─ .github/workflows/
│  ├─ ci.yml                    # formato, testes, segredos, build das imagens
│  └─ cd.yml                    # publica no GHCR, implanta, reverte
├─ Dockerfile.api               # imagem da API (processo ASP.NET)
├─ Dockerfile.web               # imagem do front (estático + nginx)
├─ docker-compose.yml           # orquestração local completa
├─ nginx/default.conf.template  # SPA + proxy de /api/ + links curtos
├─ k8s/                         # manifestos do ambiente de homologação
├─ docs/                        # segredos, rollback, evidências
├─ global.json                  # versão do SDK fixada
├─ Directory.Build.props        # versão semântica compartilhada
├─ Encurtai.sln
├─ src/
│  ├─ Encurtai.Api/             # back: endpoints + regra de negócio
│  │  └─ Services/              # lógica testável (validação, geração, colisão, cache)
│  └─ Encurtai.Web/             # front Blazor WASM
└─ tests/
   └─ Encurtai.Tests/           # xUnit (inclui o teste quebrado para demo)
```

A regra de negócio (`UrlShortenerService`) não conhece HTTP nem banco — depende só
de duas interfaces (`ICodeGenerator`, `IUrlStore`). É isso que a torna 100% testável
sem subir servidor, é o que permite **forçar uma colisão** nos testes, e é o que
permitiu encaixar o cache como decorator sem tocar na regra.

---

## Rodando com Docker (recomendado)

Pré-requisito: **Docker** com Compose.

```bash
cp .env.example .env     # preencha MONGO_ROOT_PASSWORD
docker compose up -d --build
docker compose ps        # os quatro serviços devem ficar "healthy"
```

- Aplicação: <http://localhost:8080> — UI, `/api/` e os links curtos, tudo no
  mesmo endereço (o nginx encaminha).
- API direta: <http://localhost:5080> — útil para inspecionar `/health`.

```bash
# Encurtar
curl -s -X POST localhost:8080/api/encurtar \
  -H 'Content-Type: application/json' \
  -d '{"url":"https://ceub.br"}'

# Seguir o link curto
curl -i localhost:8080/<codigo>
```

Para derrubar mantendo os dados: `docker compose down`
Para apagar tudo, inclusive o volume: `docker compose down -v`

---

## Rodando sem Docker

Pré-requisitos: **.NET SDK 9** e um MongoDB acessível.

```bash
# 1) build + testes unitários (sem precisar de banco)
dotnet test --filter "Category!=Integration"

# 2) subir a API (http://localhost:5080)
dotnet run --project src/Encurtai.Api

# 3) em outro terminal, subir o front
dotnet run --project src/Encurtai.Web
```

Em `Development`, `appsettings.Development.json` já aponta para
`mongodb://localhost:27017`. Em qualquer outro ambiente a connection string é
**obrigatória** — ver a seção de banco abaixo.

---

## Os pipelines

### CI — `.github/workflows/ci.yml`

A cada push na `main` e a cada Pull Request, cinco jobs em paralelo:

| Job | O que faz |
|---|---|
| `formato` | `dotnet format --verify-no-changes` contra o `.editorconfig` |
| `unitarios` | testes **sem** banco (`Category!=Integration`), com cobertura |
| `integracao` | testes **com** `mongo:7` efêmero (`Category=Integration`) |
| `segredos` | gitleaks sobre o histórico completo |
| `imagens` | compila as duas imagens Docker, sem publicar |

A separação entre unitários e integração usa o trait `Category=Integration` que
já existia no código e nunca era aplicado como filtro — antes, os unitários
dependiam de um MongoDB no ar sem precisar.

### CD — `.github/workflows/cd.yml`

| Gatilho | Efeito |
|---|---|
| push em `main` | publica `0.0.0-main.<run>` no GHCR e implanta em homologação |
| tag `v*.*.*` | publica release versionada e habilita o caminho de produção |
| manual | reimplanta uma versão **já publicada** (rollback), sem recompilar |

O deploy vai para um cluster **kind** criado e destruído no próprio run, por
**digest** de imagem (não por tag), e passa por um smoke test que encurta um
link de verdade e segue o redirecionamento. Produção fica atrás de aprovação
manual via GitHub Environments.

Detalhes: [`docs/segredos.md`](docs/segredos.md) e
[`docs/rollback.md`](docs/rollback.md).

---

## 🎬 Roteiro de demonstração (para a apresentação)

### CI barrando um merge

1. Confirme o CI **verde** na aba *Actions*.
2. Abra `tests/Encurtai.Tests/UrlShortenerServiceTests.cs` e **descomente** o teste
   `Demo_TesteQuebrado_ParaVerOCiFalhar`.
3. Crie uma branch, commite e **abra um Pull Request**.
4. Mostre o GitHub Actions **barrando o merge** com o ✗ vermelho.
5. Comente o teste de novo, dê push → o CI volta a **verde** → merge liberado.

Bônus: ative *branch protection* na `main` (Settings → Branches) exigindo o check do
CI. Aí o "vermelho bloqueia o merge" deixa de ser visual e passa a ser regra.

### CD e rollback

1. `git push origin v1.1.0` → acompanhe o CD publicando as imagens no GHCR.
2. Mostre o smoke test conferindo a versão e seguindo o redirecionamento.
3. Mostre o job de produção **parado em "Review deployments"** — o gate.
4. Provoque a falha de readiness e mostre o rollout **não convergindo** enquanto
   a aplicação continua respondendo. Passo a passo na seção 4 de
   [`docs/rollback.md`](docs/rollback.md).
5. Reverta e confirme com `curl /api/health` que a versão anterior voltou.

O passo 4 é o que vale: com `maxUnavailable: 0` e `readinessProbe`, uma versão
quebrada **não recebe uma única requisição** de usuário.

---

## Próximos passos (roadmap de aprendizado)

Já entregues: cache de NuGet no CI, banco de verdade (MongoDB), CD para staging
descartável e gate de aprovação manual.

Restam:

1. **Matriz de versões** (`strategy.matrix`) para testar em mais de um SDK.
2. **Restore em modo travado** — os `packages.lock.json` já são gerados; falta
   exigir o lockfile no CI e no Dockerfile.
3. **Ingress com TLS** em vez de `port-forward`, e um banco gerenciado.
4. **Observabilidade**: métricas, tracing e alerta.
5. **Rate limiting** em `POST /encurtar`, que hoje é escrita anônima e irrestrita.

---

## Notas

- Se o *publish* do Blazor reclamar da workload `wasm-tools` (acontece com AOT ou
  relink nativo, não em build normal), rode `dotnet workload install wasm-tools`.
  No `Dockerfile.web` há uma linha comentada pronta para isso.
- O `global.json` fixa a faixa do SDK. Se o `dotnet restore` reclamar de versão
  de pacote, ajuste os números nos `.csproj`.
- A pasta `dashboard/` que pode aparecer nesta árvore é de **outro projeto**
  (resultados eleitorais) e não faz parte do Encurtaí. Está excluída do contexto
  de build pelo `.dockerignore`.

---

## Banco de dados (MongoDB)

A connection string **nunca** fica no código — vem de configuração, e a origem
muda por ambiente:

### Seu dev (Atlas)

Guarde a string do Atlas em user-secrets (fora do repo):

    dotnet user-secrets init --project src/Encurtai.Api
    dotnet user-secrets set "ConnectionStrings:Mongo" "SUA_STRING_DO_ATLAS" --project src/Encurtai.Api

### Parceiro / offline (Docker local)

Sem conta nenhuma, só subir um Mongo local:

    docker run -d --name mongo-encurtai -p 27017:27017 mongo:7

Em `Development`, `appsettings.Development.json` já aponta para
`localhost:27017` — funciona direto.

> **Fora de `Development` não há default.** O `appsettings.json` tinha
> `mongodb://localhost:27017` como valor padrão, o que neutralizava a validação
> de configuração: dentro de um container, `localhost` é o próprio container, e
> a imagem subia "saudável" para só quebrar na primeira escrita. O default foi
> removido — agora a aplicação falha no boot, com mensagem clara.

### Compose

`ConnectionStrings__Mongo` é montada a partir do `.env`. Ver `.env.example`.

### CI

O pipeline sobe um Mongo efêmero (service container) e injeta a string por
variável de ambiente. Nada a configurar.

### Homologação e produção

Secret do GitHub Environment → `Secret` do Kubernetes → `secretKeyRef`.
Ver [`docs/segredos.md`](docs/segredos.md).
