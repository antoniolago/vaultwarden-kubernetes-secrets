# Vaultwarden × VKS compatibility harness

Answers one question for every new Vaultwarden release: *"does the VKS image we run in
production still decrypt and sync the items this version creates?"*

## Why it exists

What breaks the VKS is not the server, it is **the client that wrote the item**. Starting with
web vault **2026.10.x** (Vaultwarden **1.37.5**) every item carries its own key in
`ciphers.key`, and the item payload is encrypted with that key. A test that creates items
through the API with our own crypto cannot see that, so the harness drives the **web vault UI
bundled with each image**.

## What a run does

1. Lists Vaultwarden tags on Docker Hub and keeps the ones not tested yet (`state.json`).
2. For each new version, in parallel (`--jobs`):
   - starts `vaultwarden/server:<version>` in a throwaway container (self-signed TLS, sqlite in
     a temp dir, free port);
   - creates user + organization + item with custom fields **through the UI** in a headless
     Chromium (CDP, no Playwright);
   - reads the sqlite and checks whether the cipher got a `key`;
   - runs the VKS image against the instance (`SYNC__DRYRUN=true`) and counts
     `DecryptString failed`, decoded items and whether the secret name came out readable;
   - records the verdict, removes the container and the temp dir.
3. Regenerates `~/.television/artifacts/vks-compat/report.html`.

Pass criteria: `pad_errors == 0`, at least one secret detected and the name decoded.

## Usage

```bash
# only check whether a new release is pending (cheap, ~1s)
python3 run_matrix.py --check

# test up to 3 new releases, 3 in parallel
KUBE_CONTEXT=admin@ton-cluster python3 run_matrix.py --jobs 3 --max-new 3

# specific versions
python3 run_matrix.py --versions 1.37.5 1.37.6 --recheck --jobs 2
```

| Variable | Default | Purpose |
|---|---|---|
| `VKS_IMAGE` | pr-28 tag pinned in fleet-manifests | image under test |
| `KUBE_CONTEXT` | empty | rewrites the copied kubeconfig `current-context` for the dry-run (`admin@ton-cluster` locally; empty in CI where kind is the context) |
| `BROWSER_BIN` | helium → chrome → chromium | headless browser binary |
| `CONTAINER_RUNTIME` | podman if present, else docker | container runtime |
| `COMPAT_JOBS` | 1 | default parallelism |

Measured cost: ~2m20 for two versions in parallel (most of it is the UI seeding). With no new
release the run finishes in seconds.

## Scheduling

CI (recommended): `.github/workflows/vks-compat.yml` — Monday 06:00 UTC, `workflow_dispatch`
for specific versions, kind cluster for the dry-run, state committed back, and the job fails
when a version tested in that run breaks the VKS.

Local alternative (systemd user timer). Replace `<checkout>` with the path where this
repository lives on your machine (`%h` expands to your home directory):

```ini
# ~/.config/systemd/user/vks-compat.service
[Service]
Type=oneshot
Environment=KUBE_CONTEXT=admin@ton-cluster
ExecStart=/usr/bin/python3 %h/<checkout>/compat/run_matrix.py --jobs 2 --max-new 3
```

```ini
# ~/.config/systemd/user/vks-compat.timer
[Timer]
OnCalendar=Mon 06:00
Persistent=true
[Install]
WantedBy=timers.target
```

## End-to-end scenario

`scenario_missing_namespace.py` recreates the "new secret only appears after a pod restart"
bug: it starts the VKS in continuous mode, inserts an item for a namespace that does not exist
yet, creates the namespace afterwards and asserts the Secret shows up within seconds, without
restarting anything. Run it with the same env vars as the matrix, plus `VKS_IMAGE` pointing at
the image you want to verify.

## Pitfalls already paid for

- **`runs-on` must be `ubuntu-24.04`.** On 26.04 `playwright install` dies with
  "Playwright does not support chromium on ubuntu26.04-x64". This harness does not use
  Playwright: it installs Chrome (`google-chrome-stable` .deb) and speaks CDP.
- **The web vault refuses to sign up over HTTP** ("Insecure URL not allowed"): always TLS, even
  self-signed. `Security.setIgnoreCertificateErrors` through CDP handles the warning.
- **Browse `127.0.0.1`, never `localhost`** — `localhost` may resolve to IPv6 and fails with
  `ERR_CONNECTION_RESET`.
- **The .NET runtime does not trust the lab cert**: mount it and set `SSL_CERT_FILE=/cert.pem`.
- **Without a cluster the sync does not run** (the VKS initialises the K8s client before
  fetching items): use kind in CI, a real cluster locally.
- **The UI changes between versions**: 1.37.5 uses "Adicionar → Credencial" with a field modal,
  1.37.2–1.37.4 use "Criar → Credencial" with the same modal. The script tries both and fails
  loudly, which is itself a signal that the client changed.
- **You cannot create an organization item through the API with a user token**: both
  `POST /api/ciphers` and `POST /api/ciphers/create` answer 422 on 1.37.5, so the scenario
  inserts the cipher straight into the sqlite with the org key.
