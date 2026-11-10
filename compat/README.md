# vks-compat-lab

Matriz de compatibilidade **Vaultwarden × vaultwarden-kubernetes-secrets (VKS)**.

Responde, para cada release nova do Vaultwarden: *“a imagem do VKS que está em produção
decifra e sincroniza os itens criados por esta versão?”*

## Por que existe

O que quebra o VKS não é o servidor, é o **cliente que escreveu o item**. A partir do web
vault **2026.10.x** (Vaultwarden **1.37.5**) cada item ganha uma chave própria em
`ciphers.key`, e o conteúdo passa a ser cifrado com ela. Um teste que cria o item por API
com a nossa própria crypto não pega isso — por isso o harness usa a UI do web vault
embutido em cada imagem.

## O que cada execução faz

1. Lista as tags do Vaultwarden no Docker Hub e filtra as ainda não testadas (`state.json`).
2. Para cada versão nova, em paralelo (`--jobs`):
   - sobe `vaultwarden/server:<versão>` em container descartável (TLS self-signed, sqlite em
     diretório temporário, porta livre);
   - cria conta + organização + item com custom fields **pela UI** num Chromium headless
     (CDP, sem Playwright);
   - lê o sqlite e verifica se o cipher ganhou `key`;
   - roda a imagem do VKS contra a instância (`SYNC__DRYRUN=true`) e conta
     `DecryptString failed`, itens decifrados e se o nome do Secret saiu legível;
   - grava o veredito, apaga container e diretório.
3. Regenera `~/.television/artifacts/vks-compat/report.html`.

Critério de sucesso: `pad_errors == 0`, ≥1 Secret detectado e nome decifrado.

## Uso

```bash
# só ver se há versão nova (barato, ~1s)
python3 run_matrix.py --check

# testar até 3 releases novas, 3 em paralelo
KUBE_CONTEXT=admin@ton-cluster python3 run_matrix.py --jobs 3 --max-new 3

# versões específicas
python3 run_matrix.py --versions 1.37.5 1.37.6 --recheck --jobs 2
```

Variáveis:

| Variável | Default | Uso |
|---|---|---|
| `VKS_IMAGE` | tag pr-28 fixada no fleet-manifests | imagem sob teste |
| `KUBE_CONTEXT` | vazio | reescreve o `current-context` do kubeconfig copiado, para o dry-run (`admin@ton-cluster` na máquina local; vazio no CI, onde o kind já é o contexto) |
| `BROWSER_BIN` | helium → chrome → chromium | binário do browser headless |
| `CONTAINER_RUNTIME` | podman se existir, senão docker | runtime dos containers |
| `COMPAT_JOBS` | 1 | paralelismo padrão |

Custo medido: ~2m20 para 2 versões em paralelo (a maior parte é o seed via UI).
Sem release nova, a execução termina em segundos.

## Agendamento

CI (recomendado): `.github/workflows/compat.yml` — segunda 06:00 UTC, `workflow_dispatch`
para rodar versões específicas, kind cluster para o dry-run, estado commitado de volta e
job falhando quando alguma versão quebra o VKS.

Local (systemd user timer), se preferir sem CI:

```ini
# ~/.config/systemd/user/vks-compat.service
[Service]
Type=oneshot
Environment=KUBE_CONTEXT=admin@ton-cluster
ExecStart=/usr/bin/python3 %h/Projetos/vks-compat-lab/run_matrix.py --jobs 2 --max-new 3
```

```ini
# ~/.config/systemd/user/vks-compat.timer
[Timer]
OnCalendar=Mon 06:00
Persistent=true
[Install]
WantedBy=timers.target
```

## Armadilhas já pagas

- **`runs-on` tem que ser `ubuntu-24.04`.** Em 26.04 o `playwright install` morre com
  “Playwright does not support chromium on ubuntu26.04-x64”. Este harness não usa
  Playwright: instala o Chrome (`google-chrome-stable` .deb) e fala CDP direto.
- **O web vault recusa signup em HTTP** (“Insecure URL not allowed”): sempre TLS, mesmo
  self-signed. `Security.setIgnoreCertificateErrors` no CDP resolve o aviso.
- **Navegue por `127.0.0.1`, não `localhost`** — `localhost` cai em IPv6 e dá
  `ERR_CONNECTION_RESET`.
- **O runner .NET não confia no cert do lab**: `SSL_CERT_FILE=/cert.pem` com o cert montado.
- **Sem cluster o sync não roda** (o VKS inicializa o client K8s antes de buscar itens):
  no CI use kind; localmente aponte para um cluster real.
- **A UI muda entre versões**: 1.37.5 usa “Adicionar → Credencial” com modal de campo;
  1.37.2–1.37.4 usam “Criar → Credencial” com o mesmo modal. O script tenta os dois.
  Se a UI mudar de novo, o erro é explícito — e isso já é sinal de que o cliente mudou.
