# Versionamento semântico e rollback

> Entregável: *Evidência de Versionamento e Rollback — versionamento semântico
> nas imagens/releases e simulação documentada de um fluxo de rollback em caso
> de falha.*

---

## 1. Como a versão atravessa a esteira

Uma única fonte, propagada por todo o caminho:

```
git tag v1.1.0
   └─> CD lê GITHUB_REF_NAME, remove o "v"        -> 1.1.0
         ├─> docker build --build-arg APP_VERSION=1.1.0
         │      └─> dotnet publish -p:Version=1.1.0
         │            └─> AssemblyInformationalVersion
         │                  └─> GET /health  ->  {"versao":"1.1.0"}
         └─> tags da imagem no GHCR: 1.1.0, 1.1, 1, latest, sha-<curto>
```

A última linha é o que faz diferença na prática: **a versão em execução é
verificável com um `curl`**, não com uma captura de tela de pipeline verde.

```bash
curl -s http://localhost:8080/api/health
# {"status":"healthy","versao":"1.1.0"}
```

### Tags geradas e para que servem

| Tag | Para quem |
|---|---|
| `1.1.0` | Fixa exata — é a que o deploy registra |
| `1.1` | Quem aceita correções de patch automaticamente |
| `1` | Quem aceita qualquer versão compatível |
| `latest` | Conveniência; **nunca** usada em deploy |
| `sha-a1b2c3d` | Rastreabilidade até o commit |
| `main` | Último build da branch principal |

### Deploy por digest, não por tag

Os manifestos recebem `ghcr.io/.../encurtai-api@sha256:...`, não
`:1.1.0`. Tag é ponteiro — alguém pode movê-la. Digest é imutável.

Sem isso, "rollback para v1.0.0" não garante o mesmo binário de antes, e o
rollback deixa de ser uma garantia para virar uma expectativa.

---

## 2. Os três caminhos de rollback

### a) Pelo pipeline — reimplanta uma versão já publicada, sem recompilar

`Actions` → `CD` → `Run workflow`:

| Campo | Valor |
|---|---|
| `reimplantar_tag` | `v1.0.0` |
| `ambiente` | `homologacao` ou `producao` |

O job `imagens` entra em **modo rollback**: não compila nada, apenas descobre o
digest daquela tag com `docker buildx imagetools inspect` e implanta exatamente
aquele binário. Rápido, porque não há build, e fiel, porque é o mesmo artefato.

### b) No cluster — reverte a revisão anterior

```bash
NS=encurtai-homologacao

kubectl rollout history deployment/encurtai-api -n $NS
kubectl rollout undo    deployment/encurtai-api -n $NS
kubectl rollout status  deployment/encurtai-api -n $NS --timeout=120s

# Confirmação que importa:
kubectl port-forward -n $NS service/encurtai-web 18080:8080 &
curl -s http://localhost:18080/api/health
```

Para voltar a uma revisão específica:

```bash
kubectl rollout undo deployment/encurtai-api -n $NS --to-revision=3
```

`revisionHistoryLimit: 10` em `k8s/api.yaml` é o que mantém esse histórico.

### c) No Docker Compose — fixa a tag anterior

```bash
IMAGE_TAG=v1.0.0 docker compose pull
IMAGE_TAG=v1.0.0 docker compose up -d --no-build
```

`--no-build` é essencial: sem ele o Compose recompila do código local e a tag é
ignorada.

---

## 3. Por que o rollback automático funciona

Dois ajustes em `k8s/api.yaml`, que juntos transformam a reversão de encenação
em mecanismo:

```yaml
strategy:
  rollingUpdate:
    maxUnavailable: 0     # pod antigo só sai depois que o novo está ready
    maxSurge: 1
readinessProbe:
  httpGet:
    path: /health/ready   # só fica ready quem fala com o Mongo
```

A sequência de uma versão quebrada:

1. O pod novo sobe e falha na `readinessProbe`.
2. Com `maxUnavailable: 0`, o Deployment **não derruba** nenhum pod antigo.
3. O tráfego continua inteiro na versão anterior — **nenhuma requisição de
   usuário chega à versão quebrada**.
4. `kubectl rollout status` estoura o timeout e o step falha.
5. O step `Rollback automatico` dispara `kubectl rollout undo`.
6. As evidências são coletadas e publicadas como artefato.

O ponto decisivo é o 3: o rollback não é o que *salva* o usuário — quem salva é
a readiness. O rollback apenas devolve o Deployment a um estado consistente.

---

## 4. Simulação documentada da falha

É o mesmo espírito do teste quebrado comentado que o projeto já usa para
demonstrar CI vermelho (`README.md`), agora aplicado ao deploy.

A API tem a chave `Encurtai:SimularFalhaDeReadiness`. Com `true`,
`/health/ready` passa a responder `503` — exatamente o que uma versão
incompatível com o banco faria.

### Caminho A — sem recompilar nada (recomendado para a apresentação)

```bash
NS=encurtai-homologacao

# 1. Estado saudável
kubectl get pods -n $NS
kubectl port-forward -n $NS service/encurtai-web 18080:8080 &
curl -s http://localhost:18080/api/health          # versao: 1.1.0

# 2. Provoca a falha de readiness
kubectl create configmap encurtai-config -n $NS \
  --from-literal=public-base-url="http://encurtai.homologacao.local" \
  --from-literal=simular-falha-readiness="true" \
  --dry-run=client -o yaml | kubectl apply -f -

kubectl rollout restart deployment/encurtai-api -n $NS

# 3. O rollout NÃO converge — e é isso que se quer provar
kubectl rollout status deployment/encurtai-api -n $NS --timeout=90s
#   error: timed out waiting for the condition

kubectl get pods -n $NS          # novos pods: Running mas 0/1 READY
kubectl describe pod -n $NS -l app.kubernetes.io/component=api | grep -A3 Readiness
#   Readiness probe failed: HTTP probe failed with statuscode: 503

# 4. Durante tudo isso a aplicação continua respondendo
curl -s http://localhost:18080/api/health          # ainda no ar

# 5. Reverte
kubectl create configmap encurtai-config -n $NS \
  --from-literal=public-base-url="http://encurtai.homologacao.local" \
  --from-literal=simular-falha-readiness="false" \
  --dry-run=client -o yaml | kubectl apply -f -

kubectl rollout undo deployment/encurtai-api -n $NS
kubectl rollout status deployment/encurtai-api -n $NS --timeout=120s
curl -s http://localhost:18080/api/health          # versao: 1.1.0 de volta
```

Localmente, com Compose, o equivalente é uma linha:

```bash
SIMULAR_FALHA=true docker compose up -d
docker compose ps        # api fica "unhealthy"
```

### Caminho B — rollback de versão de verdade, ponta a ponta

Prova a troca de artefato, não só de configuração:

1. Com `v1.0.0` e `v1.1.0` publicadas, implante `v1.1.0` e confirme em
   `/health`.
2. `Actions` → `CD` → `Run workflow` → `reimplantar_tag: v1.0.0`.
3. Confirme que `/health` volta a reportar `1.0.0` e que o digest no pod é o
   da imagem antiga:

```bash
kubectl get pod -n $NS -l app.kubernetes.io/component=api \
  -o jsonpath='{.items[0].status.containerStatuses[0].imageID}'
```

---

## 5. O que capturar como evidência

O pipeline já publica o artefato `evidencias-homologacao-<n>` com
`recursos.txt`, `pods.txt`, `historico-api.txt`, `logs-api.txt`, `eventos.txt`
e `secret.txt` (apenas os nomes das chaves).

Para o relatório, vale somar:

| Evidência | Comando / local |
|---|---|
| Versão antes da falha | `curl /api/health` |
| Rollout não convergindo | log do step `Aguardar rollout da aplicacao` |
| Probe reprovando | `kubectl describe pod` |
| Aplicação no ar durante a falha | `curl /api/health` repetido |
| Reversão executada | log do step `Rollback automatico` |
| Histórico de revisões | `kubectl rollout history` |
| Versão após a reversão | `curl /api/health` |
| Gate de produção aguardando | tela do run com "Review deployments" |
| Imagens no registry | aba *Packages* do repositório |

Consolide em `docs/evidencias-entrega-2.md`.

---

## 6. Comandos de tag (executar manualmente)

```bash
# Consolidar a entrega 1 como marco inicial
git tag -a v1.0.0 <sha-do-commit-da-entrega-1> -m "Entrega 1: aplicacao e CI"
git push origin v1.0.0

# Entrega 2
git tag -a v1.1.0 -m "Entrega 2: containerizacao, CD, segredos e rollback"
git push origin v1.1.0
```

O push da tag é o que dispara o CD com versionamento semântico. Sem tag, o CD
ainda roda no merge em `main`, mas publica como `0.0.0-main.<run>` — rastreável,
porém não é release.
