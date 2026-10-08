# Entrega 2 — Evidências

Cada entregável exigido, ligado ao artefato que o implementa e ao resultado de
execução que o comprova. Todas as execuções citadas são públicas e verificáveis
nos endereços indicados.

**Repositórios:**

- `MarcosVogado/Encurtai` — repositório principal, onde está o Pull Request #8.
- `GUILHERME-LA/Encurtai` — fork usado para executar o CD completo, porque criar
  GitHub Environment e cadastrar segredo exige permissão de administrador.

---

## Quadro geral

| Entregável | Artefato | Execução que comprova | Resultado |
|---|---|---|---|
| Dockerfile otimizado | `Dockerfile.api`, `Dockerfile.web`, `.dockerignore`, `nginx/default.conf.template` | CI, job `imagens` | ✅ |
| Orquestração com dependências | `k8s/*.yaml`, `docker-compose.yml`, `.env.example` | CD, deploy em cluster kind | ✅ |
| CD: registry + deploy automático | `.github/workflows/cd.yml` | CD em `main` e na tag `v1.1.0` | ✅ |
| Gestão segura de segredos | `docs/segredos.md`, GitHub Environments, job `segredos` | CI + CD | ✅ |
| Versionamento semântico | `Directory.Build.props`, `global.json`, `CHANGELOG.md` | CD na tag `v1.1.0` | ✅ |
| Rollback simulado e documentado | `.github/workflows/rollback-demo.yml`, `docs/rollback.md` | Workflow `Evidencia de rollback` | ✅ |

---

## 1. Dockerfile otimizado

**Execução:** CI do PR #8, run `37829185017`.

```
Build das imagens (api, Dockerfile.api)    pass   23s
Build das imagens (web, Dockerfile.web)    pass   30s
```

As duas imagens são construídas em **todo Pull Request**, antes de qualquer
publicação — descobrir que o Dockerfile quebrou só no merge é tarde.

### Decisões e o que cada uma evita

| Decisão | O que evita |
|---|---|
| Estágio de restore separado, só com os `.csproj` | Mudar uma linha de C# invalidaria a camada de download dos pacotes; cada build baixaria tudo de novo |
| Runtime `aspnet:9.0-alpine` + `InvariantGlobalization=true` | ~110 MB em vez de ~220 MB; a ICU não vem na imagem alpine |
| `USER $APP_UID` | Processo como root dentro do container |
| `ASPNETCORE_HTTP_PORTS=8080` explícito | A porta 5080 existe só no `launchSettings.json`, que o `publish` não copia — dentro do container ele é ignorado |
| `HEALTHCHECK` com o `wget` do busybox | Container `running` mas quebrado, sem ninguém notar |
| Testes **fora** do Dockerfile | Três testes exigem MongoDB (categoria `Integration`); o build do container não tem banco |
| `nginxinc/nginx-unprivileged` | A imagem `nginx` padrão sobe como root na porta 80 e não inicia sob `runAsNonRoot: true` |
| Upstream do nginx por `${API_UPSTREAM}` | A **mesma imagem** serve ao Compose (`api:8080`) e ao Kubernetes (`encurtai-api:8080`), sem rebuild |

**Por que duas imagens e não uma:** a API é um processo ASP.NET Core; o
`Encurtai.Web` é Blazor **WebAssembly**, ou seja, arquivos estáticos que rodam no
navegador. Processo precisa de runtime .NET; arquivo estático precisa de servidor
web. Forçar os dois na mesma imagem só aumentaria o tamanho sem ganho algum.

---

## 2. Orquestração com as dependências integradas

O enunciado aceita Docker Compose **ou** manifestos Kubernetes. Ambos foram
entregues; os **manifestos K8s são os que têm evidência de execução**, por terem
sido aplicados em cluster kind em quatro execuções distintas de pipeline.

### Serviços

| Serviço | Papel | Ponto de atenção |
|---|---|---|
| `encurtai-web` | nginx servindo o WASM e encaminhando `/api/` e os links curtos | Porta de entrada única — deixa o tráfego *same-origin* e dispensa CORS em produção |
| `encurtai-api` | API .NET | 2 réplicas, `livenessProbe`, `readinessProbe`, `startupProbe`, limites de recurso, `runAsNonRoot` |
| `encurtai-mongo` | Banco (fonte da verdade) | PVC + estratégia `Recreate`: volume `ReadWriteOnce` não pode ser montado por dois pods ao mesmo tempo |
| `encurtai-redis` | Cache de leitura | Sem persistência, `allkeys-lru`, 128 MB |

### Sobre o cache

O enunciado cita "cache" como exemplo de dependência. O `CachedUrlStore` é um
decorator de `IUrlStore` que põe o Redis na frente do Mongo **na leitura**, que é
a operação quente de um encurtador — poucos links concentram os cliques. Cinco
testes cobrem o comportamento, incluindo dois casos que seriam fáceis de errar:

- **colisão de código não entra no cache** — cachear antes da confirmação do
  store transformaria um código recusado em link fantasma;
- **ausência não é cacheada** — um código consultado antes de ser criado viraria
  404 persistente pelo tempo do TTL.

O cache é opt-in: sem `ConnectionStrings__Redis` a aplicação roda direto no
Mongo. E é tolerante a falha — Redis fora do ar cai para o banco em vez de
derrubar a API. Cache indisponível custa latência, não disponibilidade.

### Status do Docker Compose

O `docker-compose.yml` está completo e com a sintaxe validada, porém **nunca foi
executado**: não havia Docker na máquina de desenvolvimento. É o único artefato
desta entrega sem evidência de execução. O critério fica satisfeito pelos
manifestos Kubernetes, que rodaram.

---

## 3. Pipeline de CD

### 3.1 Publicação no Container Registry

**Execução:** CD na tag `v1.1.0`, run `37827570701`, job `Imagens`.

Tags geradas pelo `docker/metadata-action`:

```
ghcr.io/guilherme-la/encurtai-api:1.1.0
ghcr.io/guilherme-la/encurtai-api:1.1
ghcr.io/guilherme-la/encurtai-api:1
ghcr.io/guilherme-la/encurtai-api:latest
ghcr.io/guilherme-la/encurtai-api:sha-35424fd
```

Digest da imagem da API no deploy em `main`:
`sha256:58462b8887285dfa89290b3aa4b10c949ba8b3904cb3e1c2dc88e801c3a5134c`

**O deploy referencia o digest, não a tag.** Tag é um ponteiro que alguém pode
mover; digest é imutável. Sem isso, "rollback para v1.0.0" não garantiria o mesmo
binário de antes — a reversão deixaria de ser garantia e passaria a ser
expectativa.

### 3.2 Deploy automático em homologação

**Execução:** CD em `main`, run `37826513351`.

O job cria um cluster kind, aplica os manifestos com o digest fixado, espera o
rollout e roda um smoke test ponta a ponta:

```
--- versao em execucao ---
{"status":"healthy","versao":"0.0.0-main.1"}
--- encurtar ---
{"codigo":"56AJyR","urlCurta":"http://encurtai.homologacao.local/56AJyR"}
URL curta usa o BaseUrl configurado: http://encurtai.homologacao.local/56AJyR
--- resolver o redirecionamento ---
Redirect correto: 56AJyR -> https://example.com/devops-entrega-2
```

O smoke test não se contenta com "o pod subiu". Ele confere quatro coisas, e
qualquer uma falhando dispara o rollback:

1. a versão no ar é a que foi implantada;
2. um link é encurtado de verdade;
3. a URL curta usa o endereço público configurado — **não** o hostname interno
   do pod;
4. o redirecionamento chega ao destino original.

A terceira verificação é a que prova, dentro de um container, a correção do
defeito nº 1 listado na seção 7.

### 3.3 Gate de aprovação manual para produção

**Execução:** CD na tag `v1.1.0`, run `37827570701`.

Enquanto aguardava, a API do GitHub reportava:

```
ambiente:            producao
reviewers exigidos:  GUILHERME-LA
estado:              aguardando aprovacao
```

Após a aprovação, a cadeia completa:

```
success  Imagens
success  Deploy em homologacao
success  Deploy em producao (aprovacao manual)
```

Verificação de versão em produção: `no ar: 1.1.0 | esperada: 1.1.0`

O gate vem de um GitHub Environment com *required reviewers* — não de um passo
no script, que qualquer um poderia remover sem deixar rastro.

---

## 4. Gestão segura de configurações e segredos

### 4.1 O repositório já estava limpo

Varredura em **todos os commits de todas as refs** procurando `mongodb+srv`,
`password`, `secret`, `api_key`, `token=` e variantes. Nada foi encontrado além
de duas ocorrências inócuas:

| Local | Conteúdo | Por que não é segredo |
|---|---|---|
| `ci.yml` | `mongodb://localhost:27017` | Endereço do service container efêmero do runner, sem usuário nem senha |
| `README.md` | `"SUA_STRING_DO_ATLAS"` | Placeholder literal |

Nenhum arquivo `.env`, `.pem` ou `secrets.json` existe hoje nem jamais existiu no
histórico. **Este item é demonstração de mecanismo, não remediação de
vazamento.**

### 4.2 Onde cada segredo vive

| Segredo | Onde é guardado | Como chega na aplicação |
|---|---|---|
| Credencial do GHCR | *Não existe.* `GITHUB_TOKEN` nasce e expira com o run | `docker/login-action`, com `packages: write` só no job que publica |
| `MONGO_USER` | Secret do **Environment** | `Secret` do Kubernetes → `secretKeyRef` |
| `MONGO_PASSWORD` | Secret do **Environment** | idem |
| `MONGO_CONNECTION_STRING` | Secret do **Environment** | idem, lido como `ConnectionStrings__Mongo` |
| Senha do Mongo local | `.env`, não versionado | Variável de ambiente do container |
| Connection string de dev | `dotnet user-secrets` | `IConfiguration` |

**Environment e não secret de repositório:** secret de repositório é visível a
todo workflow. Secret de Environment só é entregue a um job que declara
`environment: <nome>` e, com reviewers, só após a aprovação. Resultado prático:
homologação e produção **não compartilham credencial**, e a de produção não é
alcançável por um job que não passou pelo gate.

### 4.3 Evidências

- Job `segredos` (gitleaks) **verde** no CI, com `fetch-depth: 0` — segredo
  apagado num commit posterior continua recuperável e precisa ser detectado.
- `evidencias/secret.txt` dos artefatos de CD lista **apenas os nomes das
  chaves** do Secret, nunca valores.
- O Secret do cluster é criado por
  `kubectl create secret … --dry-run=client -o yaml | kubectl apply -f -`:
  idempotente e **sem materializar arquivo** com a credencial no runner.
- Os YAML de `k8s/` carregam apenas nomes de chave. O contrato de exemplo mora em
  `docs/secret-contrato.example.yaml`, **de propósito fora de `k8s/`** — lá
  dentro ele seria capturado por um `kubectl apply -f k8s/` e aplicaria valores
  de placeholder no cluster.
- `permissions` declarado job por job. Antes desta entrega não havia bloco algum,
  e os workflows herdavam o token amplo do repositório.

> **Sinal colateral de que o mascaramento funciona — e o que ele custou.** Nas
> primeiras execuções o nome do projeto aparecia como `***` nos logs, porque o
> valor de `MONGO_USER` era `encurtai` e o GitHub censura todo segredo conhecido
> na saída. A proteção estava certa; o nome escolhido estava errado. O usuário
> foi rotacionado para `svc_shortener` (com as três chaves regeradas nos dois
> ambientes) e os logs voltaram a ser legíveis:
>
> ```
> {"codigo":"Gcdwkz","urlCurta":"http://encurtai.homologacao.local/Gcdwkz"}
> URL curta usa o BaseUrl configurado: http://encurtai.homologacao.local/Gcdwkz
> Redirect correto: Gcdwkz -> https://example.com/devops-entrega-2
> ```
>
> Execução `37834060336`, que também serviu para confirmar que a rotação das
> credenciais não quebrou o deploy.

### 4.4 Configuração que *não* é segredo

Separar as duas coisas importa: tratar configuração como segredo torna o deploy
opaco; tratar segredo como configuração vaza credencial.

| Chave | Onde vive | Por quê |
|---|---|---|
| `Encurtai:BaseUrl` | `ConfigMap` | Endereço público; muda por ambiente, não é sigiloso |
| `Mongo:Database` | Manifesto | Nome do banco |
| `ConnectionStrings:Redis` | Manifesto | Cache sem autenticação, interno ao cluster |
| `ApiBaseUrl` (frontend) | `wwwroot/appsettings.json` | Baixado pelo navegador — **público por natureza** |

O caso do frontend merece atenção: `Encurtai.Web` é Blazor **WebAssembly**, então
qualquer valor que ele leia chega ao navegador do usuário. Nunca colocar segredo
ali — é por isso que o único valor configurável é o endereço da API.

---

## 5. Versionamento semântico

Uma única fonte, propagada por toda a esteira:

```
git tag v1.1.0
  └─ CD remove o "v"                          -> 1.1.0
      ├─ docker build --build-arg APP_VERSION=1.1.0
      │    └─ dotnet publish -p:Version=1.1.0
      │         └─ AssemblyInformationalVersion
      │              └─ GET /health -> {"versao":"1.1.0"}
      └─ tags no GHCR: 1.1.0, 1.1, 1, latest, sha-35424fd
```

A última linha é o que muda na prática: **a versão em execução é verificável com
um `curl`**, não com uma captura de tela de pipeline verde. Foi exatamente assim
que a reversão da seção 6 foi comprovada.

| Tag | Para quem |
|---|---|
| `1.1.0` | Fixa exata — é a que o deploy registra |
| `1.1` | Quem aceita correções de patch automaticamente |
| `1` | Quem aceita qualquer versão compatível |
| `latest` | Conveniência; **nunca** usada em deploy |
| `sha-35424fd` | Rastreabilidade até o commit |

---

## 6. Rollback — simulação executada

**Execução:** workflow `Evidencia de rollback`, run `37831306544`, **success**.

### Por que isto exigiu um workflow separado do CD

O CD cria um cluster kind do zero a cada execução, então o Deployment nasce com
**uma única revisão** — e `kubectl rollout undo` não tem para onde voltar. O
passo de rollback do CD existe e está correto para um cluster que persiste entre
deploys, mas num cluster efêmero por execução ele não teria como demonstrar
nada. Uma demonstração honesta precisa de **dois deploys sequenciais no mesmo
cluster, dentro da mesma execução** — que é o que este workflow faz.

### Como a falha é provocada

Alterando `Encurtai__SimularFalhaDeReadiness=true` **no pod template**, via
`kubectl set env`. A distinção importa: alterar apenas o `ConfigMap` não criaria
revisão nova, e sem revisão nova o `rollout undo` não teria alvo. Além disso, o
`undo` restaura o template — mas não restauraria um `ConfigMap`, então os pods
voltariam com a falha ainda ativa.

### Ato 1 — versão boa no ar

```
versao reportada por /health: 1.1.0
endpoints prontos do Service encurtai-api:
  10.244.0.5
  10.244.0.6
```

### Ato 2 — a versão quebrada não converge

```
deployment.apps/encurtai-api env updated
aguardando o rollout (deve estourar o timeout)...
rollout NAO convergiu em 90s, como esperado.
```

### Ato 3 — prova de que o tráfego nunca chegou na versão quebrada

```
pods (os novos ficam Running mas 0/1 READY):
NOME                          IP            FASE      PRONTO
encurtai-api-56b87f67f5-2vrcr 10.244.0.12   Running   false
encurtai-api-68576db845-mqdb5 10.244.0.5    Running   true
encurtai-api-68576db845-mw29j 10.244.0.6    Running   true

ReplicaSets (o novo tem 0 prontos):
NOME                    DESEJADO   PRONTOS
encurtai-api-56b87f67f5 1          <none>
encurtai-api-68576db845 2          2

IPs dos pods NAO prontos (a versao quebrada):
  10.244.0.12

endpoints prontos do Service durante a falha:
  10.244.0.5
  10.244.0.6

PROVADO: nenhum pod nao-pronto entrou no balanceamento.
versao atendendo DURANTE a falha: 1.1.0

readinessProbe reprovando:
  Warning  Unhealthy  0s (x11 over 84s)  kubelet  spec.containers{api}:
    Readiness probe failed: HTTP probe failed with statuscode: 503
```

O IP `10.244.0.12` — o pod da versão quebrada — **não aparece** entre os
endpoints do Service. Essa é a prova central, e foi escolhida de propósito em vez
de um contador de disponibilidade: `kubectl port-forward` fixa um pod, então um
contador zerado não demonstraria que o balanceamento excluiu o pod ruim.

### Ato 4 — reversão

```
historico antes da reversao:
REVISION  CHANGE-CAUSE
1         <none>
2         <none>

deployment.apps/encurtai-api rolled back
deployment "encurtai-api" successfully rolled out

historico depois da reversao:
REVISION  CHANGE-CAUSE
2         <none>
3         <none>

NOME                          FASE        PRONTO
encurtai-api-56b87f67f5-2vrcr Succeeded   false
encurtai-api-68576db845-mqdb5 Running     true
encurtai-api-68576db845-mw29j Running     true

versao no ar depois da reversao: 1.1.0
encurtou apos a reversao: {"codigo":"KugWtz","urlCurta":"http://encurtai.rollback.local/KugWtz"}
redirect resolveu para:   https://ceub.br/evidencia-rollback
```

### Resumo

| Momento | Versão atendendo | Observação |
|---|---|---|
| Antes | `1.1.0` | duas réplicas prontas |
| Durante a falha | `1.1.0` | pod novo `Running` mas `0/1 READY`, fora dos endpoints |
| Depois da reversão | `1.1.0` | `rollout undo` concluído; encurtador voltou a funcionar |

### O que faz a reversão funcionar

Dois ajustes em `k8s/api.yaml`:

```yaml
strategy:
  rollingUpdate:
    maxUnavailable: 0     # pod antigo só sai depois que o novo está ready
    maxSurge: 1
readinessProbe:
  httpGet:
    path: /health/ready   # só fica ready quem consegue falar com o Mongo
```

Vale separar os papéis: **quem protege o usuário é a readiness**, não o rollback.
A probe impede que a versão quebrada receba uma única requisição; o rollback
apenas devolve o Deployment a um estado consistente depois. Confundir os dois
leva a confiar em rollback sem probe — que não protege ninguém.

### O workflow reprova falso positivo

O passo falha se o rollout **convergir** (a falha não foi provocada), se nenhum
pod ficar não-pronto, se algum IP não-pronto aparecer nos endpoints, se a versão
no ar divergir em qualquer um dos três momentos, ou se o redirecionamento não
voltar a funcionar. Uma demonstração que sempre passa não demonstra nada.

### Outros caminhos de reversão

Documentados em `docs/rollback.md`:

- **Pelo pipeline, sem recompilar:** `Actions → CD → Run workflow` com
  `reimplantar_tag: v1.0.0`. O job entra em modo rollback, descobre o digest
  daquela tag e implanta aquele binário.
- **No Compose:** `IMAGE_TAG=v1.0.0 docker compose pull && docker compose up -d --no-build`.
  O `--no-build` é essencial — sem ele o Compose recompila do código local e a
  tag é silenciosamente ignorada.

---

## 7. Defeitos encontrados e corrigidos

Nenhum estava no escopo pedido. Todos apareceram ao containerizar, e cada um
quebraria a entrega em execução.

| # | Defeito | Consequência se não tratado | Cobertura |
|---|---|---|---|
| 1 | URL curta montada com `Request.Host` | Atrás de nginx ou ingress, o encurtador devolveria links apontando para o hostname interno do pod. A função principal do produto quebrada **só** no ambiente containerizado | 9 testes + verificação no smoke test |
| 2 | URL da API cravada no Blazor WebAssembly | Em WASM o código é compilado para o navegador; variável de ambiente de container nunca o alcança. Funcionava na máquina do desenvolvedor e em nenhum outro lugar | Configuração em tempo de execução |
| 3 | Default de connection string no `appsettings.json` | Em container `localhost` é o próprio container: a imagem subia "saudável", passava no healthcheck e quebrava na primeira escrita | Falha no boot, verificada |
| 4 | Ausência de endpoint de saúde | Sem `HEALTHCHECK` nem probes. E **sem `readinessProbe` o rollback automático não existe** — justamente o item 5 | Seções 3.2 e 6 |
| 5 | Readiness levava 30s para falhar | Probe prendia thread e deixava o timeout dela decidir no lugar da aplicação | Reduzido a 3s, medido |
| 6 | Banco fora do ar devolvia `500` com stack trace | `500` diz "a aplicação tem defeito"; `503` diz "a dependência está fora". Em `Development` a stack trace inteira ia no corpo | Handler global |
| 7 | `add_header` aninhado no nginx descartava os herdados | O `index.html` — único documento HTML da aplicação — saía sem nenhum cabeçalho de segurança | Cabeçalhos repetidos em cada `location` |
| 8 | Ruleset exigia o check `build-and-test`, renomeado na reescrita do CI | O PR ficaria `BLOCKED` para sempre, esperando um check que nunca mais apareceria — sem relação com revisão humana | Job agregador com o nome exigido |

Os defeitos 1, 3, 5 e 8 têm verificação automática. O nº 8 só apareceu porque o
PR foi aberto de verdade: um ruleset server-side não é visível no código.

---

## 8. Verificado localmente

Com o SDK .NET 9:

- Build `Release`: **0 erros, 0 avisos**
- **27 testes unitários** passando (eram 14 antes desta entrega)
- `dotnet format --verify-no-changes`: conforme o `.editorconfig`
- API executada: `/health` devolvendo `200` com a versão; `/health/ready` em
  **503 em 3,1s** sem banco; `503` JSON enxuto nos endpoints com Mongo fora;
  `400` em URL inválida; falha imediata no boot sem connection string
- Precedência de rota do `/health` sobre o catch-all `GET /{codigo}`
  **confirmada no log de execução**, não presumida

---

## 9. Limitações declaradas

1. **"Produção" é simulada.** Clusters kind efêmeros, criados e destruídos no
   run. Sem domínio, TLS, ingress ou banco gerenciado — o que o próprio
   enunciado pede ("ambiente de homologação/produção simulado").
2. **O `docker-compose.yml` nunca foi executado.** Único artefato sem evidência
   de execução; não havia Docker na máquina de desenvolvimento. O critério de
   orquestração fica satisfeito pelos manifestos Kubernetes.
3. **MongoDB de homologação é instância única**, `Deployment` + PVC, não replica
   set. Suficiente para homologação, insuficiente para produção real.
4. **O CD rodou no fork**, não em `MarcosVogado/Encurtai`, porque criar
   Environment e cadastrar segredo exige permissão de administrador.
5. **Lockfiles em modo permissivo.** Os `packages.lock.json` são gerados, mas o
   restore ainda não roda em modo travado — ligar depois de confirmar que o
   lockfile gerado no Windows serve ao build Linux.
6. **Sem observabilidade e sem rate limiting.** O diagnóstico depende de
   `kubectl logs` e dos artefatos do pipeline; `POST /encurtar` é escrita anônima
   e irrestrita. Lacunas conhecidas, registradas no roadmap do README.

---

## 10. Índice de execuções

| Run | Workflow | Onde | Resultado |
|---|---|---|---|
| `37829185017` | CI (PR #8) | `MarcosVogado/Encurtai` | success — 7 checks |
| `37826513351` | CD em `main` | fork | success — GHCR + kind + smoke test |
| `37827570701` | CD na tag `v1.1.0` | fork | success — cadeia completa com gate |
| `37831306544` | Evidencia de rollback | fork | success — falha provocada e revertida |

Artefatos de evidência ficam anexados a cada run, retidos por 30 dias (90 para a
demonstração de rollback).
