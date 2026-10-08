# Gestão de segredos e configuração

> Entregável: *Gestão Segura de Configurações e Segredos — uso de Secrets
> Management da ferramenta de CI/CD, sem senhas salvas no código-fonte.*

## Ponto de partida: o repositório já está limpo

Antes de construir qualquer coisa, o histórico foi varrido — todos os commits de
todas as refs, não só a árvore atual — procurando `mongodb+srv`, `password`,
`secret`, `api_key`, `token=` e variantes.

**Nenhuma credencial foi encontrada.** As únicas ocorrências são inócuas:

| Local | Conteúdo | Por que não é segredo |
|---|---|---|
| `.github/workflows/ci.yml` | `mongodb://localhost:27017` | Endereço do service container efêmero do runner, sem usuário nem senha |
| `README.md` | `"SUA_STRING_DO_ATLAS"` | Placeholder literal |

Nenhum arquivo `.env`, `.pem` ou `secrets.json` existe hoje nem jamais existiu
no histórico. O item desta entrega, portanto, é **demonstrar o mecanismo** —
não remediar vazamento.

---

## Onde cada segredo vive

| Segredo | Onde é guardado | Quem lê | Como chega na aplicação |
|---|---|---|---|
| Credenciais do GHCR | *Não existe.* `GITHUB_TOKEN` é gerado pelo run e expira com ele | Job `imagens` do CD | `docker/login-action`, com `packages: write` concedido só nesse job |
| `MONGO_USER` | Secret do **Environment** (`homologacao`, `producao`) | Jobs de deploy | `Secret` do Kubernetes → `secretKeyRef` |
| `MONGO_PASSWORD` | Secret do **Environment** | Jobs de deploy | idem |
| `MONGO_CONNECTION_STRING` | Secret do **Environment** | Jobs de deploy | idem, lido pela API como `ConnectionStrings__Mongo` |
| Senha do Mongo local | `.env` (não versionado) | `docker compose` | Variável de ambiente do container |
| Connection string de desenvolvimento | `dotnet user-secrets` | Máquina do desenvolvedor | `IConfiguration` |

### Por que Environment e não secret de repositório

Secret de repositório é visível a todo workflow do repositório. Secret de
**Environment** só é entregue a um job que declara `environment: <nome>` — e,
quando o Environment tem *required reviewers*, só depois da aprovação.

Resultado prático: **homologação e produção não compartilham credencial**, e a
credencial de produção não é alcançável por um job que não passou pelo gate.
Isso é mais forte do que jogar tudo em escopo de repositório.

---

## O que cadastrar, passo a passo

`Settings` → `Environments` → `New environment`

### 1. Environment `homologacao`

Sem protection rules (o deploy precisa ser automático). Secrets:

| Nome | Exemplo de valor |
|---|---|
| `MONGO_USER` | `encurtai` |
| `MONGO_PASSWORD` | senha forte, **sem** `@ : / ? # &` (ou com percent-encoding) |
| `MONGO_CONNECTION_STRING` | `mongodb://encurtai:SENHA@encurtai-mongo:27017/?authSource=admin` |

O host `encurtai-mongo` é o `Service` do Kubernetes definido em
`k8s/mongo.yaml` — a API fala com o banco pela rede interna do cluster.

### 2. Environment `producao`

Mesmos três secrets, **com valores diferentes**, e em `Protection rules`:

- marque **Required reviewers** e adicione a si mesmo — é o que materializa o
  gate de aprovação manual entre homologação e produção;
- opcionalmente, **Deployment branches** restrito a tags.

> A senha vai dentro de uma connection string. Caractere especial não escapado
> quebra o parsing do driver — este é o erro mais comum deste passo.

---

## As sete práticas aplicadas

1. **Escopo por ambiente** — secrets em Environments, não no repositório.
2. **Registry sem segredo manual** — `GITHUB_TOKEN` efêmero no GHCR.
3. **Privilégio mínimo** — `permissions` declarado por job; `packages: write`
   existe somente no job que publica. Antes não havia bloco `permissions`
   algum, e os workflows herdavam o token amplo do repositório.
4. **Nada no log** — secrets só em `env:` de step. O GitHub já mascara valores
   conhecidos; o risco real é interpolar segredo dentro de um `run:`, o que não
   é feito em nenhum lugar.
5. **Nada em disco** — o Secret do cluster é criado por
   `kubectl create secret ... --dry-run=client -o yaml | kubectl apply -f -`,
   que é idempotente e **não materializa arquivo** no runner.
6. **Manifesto sem valor** — os YAML de `k8s/` carregam apenas *nomes* de chave.
   O contrato está em `docs/secret-contrato.example.yaml`, que é documentação e
   fica de propósito fora de `k8s/` para não ser aplicado por engano.
7. **Prova contínua** — job `segredos` (gitleaks) no CI, com
   `fetch-depth: 0`, porque segredo apagado num commit posterior continua
   recuperável no histórico.

---

## O interruptor do deploy

Os jobs de deploy do CD são guardados por uma **variável de repositório**:

```yaml
if: vars.CD_DEPLOY_HABILITADO == 'true'
```

Sem ela, os jobs ficam **pulados (cinza)**, não reprovados. O motivo: eles
dependem do Environment e dos três segredos, que só existem onde alguém com
permissão de administrador os cadastrou. Num repositório sem eles, o job falharia
na primeira verificação e deixaria uma falha vermelha que não diz nada sobre o
código — ruído que faz as pessoas pararem de olhar o CI.

O job `imagens` **não** é guardado: publicar no GHCR funciona em qualquer
repositório, porque usa o `GITHUB_TOKEN` do próprio run.

Para habilitar: `Settings → Secrets and variables → Actions → Variables` →
`CD_DEPLOY_HABILITADO = true`. É variável, não segredo — o valor não é sigiloso,
e tratá-lo como segredo só tornaria o deploy mais opaco.

---

## Configuração que *não* é segredo

Separar as duas coisas importa: tratar configuração como segredo torna o
deploy opaco, e tratar segredo como configuração vaza credencial.

| Chave | Onde vive | Por quê |
|---|---|---|
| `Encurtai:BaseUrl` | `ConfigMap encurtai-config` | Endereço público; muda por ambiente, não é sigiloso |
| `Mongo:Database` | Manifesto | Nome do banco |
| `ConnectionStrings:Redis` | Manifesto | Cache sem autenticação, interno ao cluster |
| `ApiBaseUrl` (frontend) | `wwwroot/appsettings.json` | Baixado pelo navegador — **é público por natureza** |
| `Encurtai:SimularFalhaDeReadiness` | `ConfigMap` | Chave de demonstração de rollback |

Vale notar o caso do frontend: `Encurtai.Web` é Blazor **WebAssembly**, então
qualquer valor que ele leia chega ao navegador do usuário. **Nunca** colocar
segredo ali — e é por isso que o único valor configurável é o endereço da API.

---

## Rotação

1. Gere a nova credencial no banco, mantendo a antiga ativa.
2. Atualize o secret do Environment.
3. Rode o CD (`Actions` → `CD` → `Run workflow`).
4. Confirme pelo smoke test e só então revogue a credencial antiga.

Com `GITHUB_TOKEN` para o registry, não há o que rotacionar: ele nasce e morre
com cada execução.

---

## Se um segredo vazar

1. **Revogue primeiro.** Apagar o commit não invalida a credencial — e ela já
   pode ter sido coletada.
2. Troque o valor no Environment e reimplante.
3. Só então limpe o histórico (`git filter-repo`), avisando quem tem clone.
4. Confirme com o job `segredos` verde.
