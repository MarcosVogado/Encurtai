# Encurtaí ✂️ — Encurtador de URL

Projeto pequeno e descartável para **aprender DevOps / CI-CD na prática** com um
pipeline de verdade no GitHub Actions — e que rende uma boa peça de portfólio.

O app é simples de propósito: **a estrela é a esteira, não o encurtador.**

**Stack:** .NET 9 · ASP.NET Core Minimal API · Blazor WebAssembly · xUnit · GitHub Actions
// teste ruleset
---

## O que ele faz

- `POST /encurtar` → recebe `{ "url": "https://..." }` e devolve um código curto + o link.
- `GET /{codigo}` → redireciona para a URL original.
- Front Blazor com um campo, um botão e o resultado.

Armazenamento é **em memória** (some quando a API reinicia). Isso é de propósito:
mantém o CI trivial agora e deixa um gancho para você evoluir depois (ver roadmap).

---

## Estrutura

```
encurtai/
├─ .github/workflows/ci.yml     # o pipeline
├─ Encurtai.sln
├─ src/
│  ├─ Encurtai.Api/             # back: endpoints + regra de negócio
│  │  └─ Services/              # lógica testável (validação, geração, colisão)
│  └─ Encurtai.Web/             # front Blazor WASM
└─ tests/
   └─ Encurtai.Tests/           # xUnit (inclui o teste quebrado para demo)
```

A regra de negócio (`UrlShortenerService`) não conhece HTTP nem banco — depende só
de duas interfaces (`ICodeGenerator`, `IUrlStore`). É isso que a torna 100% testável
sem subir servidor, e é o que permite **forçar uma colisão** nos testes.

---

## Rodando localmente

Pré-requisito: **.NET SDK 9**.

```bash
# 1) build + testes (é o mesmo que o CI faz)
dotnet test

# 2) subir a API (fica em http://localhost:5080)
dotnet run --project src/Encurtai.Api

# 3) em outro terminal, subir o front
dotnet run --project src/Encurtai.Web
```

Abra o endereço que o Blazor imprimir, cole um link e clique em **Encurtar**.

---

## O pipeline (CI)

Arquivo: `.github/workflows/ci.yml`. A cada push na `main` e a cada Pull Request ele:

1. **Checkout** do código
2. **Instala o .NET SDK** (versão fixada → build reprodutível)
3. **Restore → Build (Release) → Test**

Se qualquer teste falhar, o job fica vermelho e o PR mostra o ✗. Esse é o objetivo:
**pegar o erro antes do merge.**

Você não baixa nada manualmente: o runner `ubuntu-latest` já vem pronto e a action
`setup-dotnet` cuida do SDK.

---

## 🎬 Roteiro de demonstração (para a apresentação)

Isto é o que impressiona mais do que o app em si:

1. Suba o repositório no GitHub e confirme o CI **verde** na aba *Actions*.
2. Abra `tests/Encurtai.Tests/UrlShortenerServiceTests.cs` e **descomente** o teste
   `Demo_TesteQuebrado_ParaVerOCiFalhar`.
3. Crie uma branch, commite e **abra um Pull Request**.
4. Mostre o GitHub Actions rodando e **barrando o merge** com o ✗ vermelho.
5. Comente o teste de novo, dê push → o CI volta a **verde** → merge liberado.

Bônus: ative *branch protection* na `main` (Settings → Branches) exigindo o check do
CI. Aí o "vermelho bloqueia o merge" deixa de ser visual e passa a ser regra.

---

## Próximos passos (roadmap de aprendizado)

Adicione **uma camada de cada vez** — cada uma é um novo conceito:

1. **Cache de NuGet** no CI (`setup-dotnet` tem `cache: true`) para acelerar.
2. **Matriz de versões** (`strategy.matrix`) para testar em mais de um SDK.
3. **Trocar o store em memória por banco** (EF Core + MySQL) e rodar as **migrations
   dentro do CI** usando um *service container* de MySQL. → é exatamente o que você
   vai encontrar no projeto real.
4. **CD para um staging descartável** no merge da `main`.
5. **Gate de aprovação manual** antes de "produção" (GitHub Environments).

Os itens 3–5 são a ponte direta para levar CI/CD ao seu projeto real com segurança.

---

## Notas

- Se em algum momento o build reclamar da workload `wasm-tools` (só acontece em
  *publish* com AOT, não em build normal), rode: `dotnet workload install wasm-tools`.
- Se o `dotnet restore` reclamar de versão de algum pacote de teste, é só ajustar os
  números no `Encurtai.Tests.csproj` para os que seu SDK oferecer.

---

## Banco de dados (MongoDB)

O armazenamento é MongoDB. A connection string **nunca** fica no código —
vem de configuração, e a origem muda por ambiente:

### Seu dev (Atlas)
Guarde a string do Atlas em user-secrets (fora do repo):

    dotnet user-secrets init --project src/Encurtai.Api
    dotnet user-secrets set "ConnectionStrings:Mongo" "SUA_STRING_DO_ATLAS" --project src/Encurtai.Api

### Parceiro / offline (Docker local)
Sem conta nenhuma, só subir um Mongo local:

    docker run -d --name mongo-encurtai -p 27017:27017 mongo:7

O default do appsettings.json já aponta pra localhost:27017 — funciona direto.

### CI
O pipeline sobe um Mongo efêmero (service container) e injeta a string por
variável de ambiente. Nada a configurar.
