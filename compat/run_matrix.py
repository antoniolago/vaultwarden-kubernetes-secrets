#!/usr/bin/env python3
"""
VKS × Vaultwarden compatibility matrix runner.

For every new Vaultwarden release (Docker Hub), this script:
  1. starts a throwaway Vaultwarden instance (TLS self-signed, sqlite on tmpfs);
  2. drives the *bundled web vault* with a headless Chromium to create user + org +
     item with custom fields (this is the part that defines the cipher key format,
     so it must be the real client, not our own crypto);
  3. inspects ciphers.key in the instance sqlite;
  4. runs the VKS image against it (SYNC__DRYRUN) and counts decryption failures;
  5. records the verdict in state.json and regenerates report.html.

Only untested versions are exercised, so a weekly run costs ~90s per new release.

Usage:
  run_matrix.py                  # test releases not in state yet (max --max-new)
  run_matrix.py --versions 1.37.5 1.37.6
  run_matrix.py --check          # only report pending versions (no test)
Env:
  VKS_IMAGE      image under test (default: the tag pinned in fleet-manifests)
  VKS_WORKDIR    scratch dir (default: <repo>/work)
"""

import argparse
import base64
import fcntl
import hashlib
import json
import os
import random
import re
import shutil
import socket
import subprocess
import sys
import time
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent
WORK = Path(os.environ.get("VKS_WORKDIR", ROOT / "work"))
STATE = ROOT / "state.json"
REPORT = Path.home() / ".television/artifacts/vks-compat/report.html"
BROWSER_BIN = os.environ.get("BROWSER_BIN") or shutil.which("helium") or shutil.which("google-chrome") or shutil.which("chromium")
CONTAINER_RUNTIME = os.environ.get("CONTAINER_RUNTIME") or ("podman" if shutil.which("podman") else "docker")
KUBE_CONTEXT = os.environ.get("KUBE_CONTEXT", "")
KUBECTL = shutil.which("kubectl") or "kubectl"
DEFAULT_VKS_IMAGE = "ghcr.io/antoniolago/vaultwarden-kubernetes-secrets:pr-28-bd7b7ac6"
MASTER_PW = "CompatLab2026!Key#42"
LAB_EMAIL_DOMAIN = "lag0.local"
LAB_LOCALE = "pt-BR"  # pinned so the web vault UI labels are stable
TARGET_NAMESPACE_PREFIX = "compat-ns"  # per-version namespace: keeps parallel runs isolated


# ----------------------------------------------------------------------------- helpers

def sh(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


def dockerhub_versions(limit=40):
    url = ("https://hub.docker.com/v2/repositories/vaultwarden/server/tags"
           f"?page_size={limit}&ordering=last_updated")
    with urllib.request.urlopen(url, timeout=30) as r:
        data = json.load(r)
    out = []
    for t in data.get("results", []):
        name = t["name"]
        if not re.fullmatch(r"\d+\.\d+\.\d+", name):
            continue
        out.append(name)
    return out


def semver_key(v):
    return tuple(int(x) for x in v.split("."))


def load_state():
    if STATE.exists():
        return json.loads(STATE.read_text())
    return {"tested": {}}


def save_state(state):
    STATE.write_text(json.dumps(state, indent=2, sort_keys=True) + "\n")


def ensure_cert():
    """Create the lab TLS pair once, even when several workers start at the same time."""
    cert, key = WORK / "cert.pem", WORK / "key.pem"
    if cert.exists() and key.exists():
        return cert, key

    WORK.mkdir(parents=True, exist_ok=True)
    with open(WORK / "cert.lock", "w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)  # only one worker generates the pair
        if cert.exists() and key.exists():
            return cert, key
        tmp_cert, tmp_key = WORK / "cert.pem.tmp", WORK / "key.pem.tmp"
        sh(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-keyout", str(tmp_key),
            "-out", str(tmp_cert), "-days", "30", "-nodes", "-subj", "/CN=127.0.0.1",
            "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1"])
        # Atomic swap: a worker never mounts a half-written file.
        os.replace(tmp_cert, cert)
        os.replace(tmp_key, key)
    return cert, key


# ----------------------------------------------------------------------------- CDP

class Tab:
    def __init__(self, ws_url):
        import websocket  # websocket-client
        self.ws = websocket.create_connection(ws_url, timeout=60, max_size=200 * 1024 * 1024)
        self.id = 0
        self.send("Page.enable")
        self.send("Runtime.enable")

    def send(self, method, **params):
        self.id += 1
        self.ws.send(json.dumps({"id": self.id, "method": method, "params": params}))
        while True:
            msg = json.loads(self.ws.recv())
            if msg.get("id") == self.id:
                if "error" in msg:
                    raise RuntimeError(f"{method}: {msg['error']}")
                return msg.get("result", {})

    def js(self, expr):
        r = self.send("Runtime.evaluate", expression=expr, returnByValue=True, userGesture=True)
        if r.get("exceptionDetails"):
            raise RuntimeError(str(r["exceptionDetails"])[:400])
        return r.get("result", {}).get("value")

    def goto(self, url, wait=5):
        self.send("Page.navigate", url=url)
        t0 = time.time()
        while time.time() - t0 < 40:
            try:
                if self.js("document.readyState") == "complete":
                    break
            except Exception:
                pass
            time.sleep(0.3)
        time.sleep(wait)

    def close(self):
        try:
            self.ws.close()
        except Exception:
            pass


class Browser:
    """Private headless Chromium (helium) on its own debug port + profile."""

    def __init__(self):
        self.port = free_port()
        self.profile = WORK / f"chrome-{self.port}"
        self.proc = subprocess.Popen(
            [BROWSER_BIN, "--headless=new", f"--remote-debugging-port={self.port}",
             "--remote-allow-origins=*", "--ignore-certificate-errors", "--no-proxy-server",
             f"--user-data-dir={self.profile}", "--no-first-run", "--no-default-browser-check",
             "--disable-gpu", "--disable-dev-shm-usage", "about:blank"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(60):
            try:
                urllib.request.urlopen(f"http://127.0.0.1:{self.port}/json/version", timeout=1)
                break
            except Exception:
                time.sleep(0.3)

    def tab(self):
        tabs = json.loads(urllib.request.urlopen(f"http://127.0.0.1:{self.port}/json", timeout=10).read())
        page = [t for t in tabs if t["type"] == "page"][0]
        t = Tab(page["webSocketDebuggerUrl"])
        t.send("Security.setIgnoreCertificateErrors", ignore=True)
        # The web vault follows the browser language; pin it so the PT-BR labels below always match.
        try:
            t.send("Emulation.setLocaleOverride", locale=LAB_LOCALE)
            t.send("Network.enable")
            t.send("Network.setExtraHTTPHeaders", headers={"Accept-Language": f"{LAB_LOCALE},pt;q=0.9"})
        except Exception:
            pass
        t.send("Page.addScriptToEvaluateOnNewDocument",
               source="try { localStorage.setItem('locale', '%s'); } catch (e) {}" % LAB_LOCALE)
        return t

    def close(self):
        self.proc.terminate()
        try:
            self.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.proc.kill()
        shutil.rmtree(self.profile, ignore_errors=True)


def click_text(t, text, selector="button,a", timeout=15):
    """Click the last visible element whose trimmed text matches exactly."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        r = t.js("""(() => {const bs=[...document.querySelectorAll(%s)]
            .filter(e=>e.innerText.trim()===%s && e.offsetWidth && !e.disabled);
          if(!bs.length) return 'none'; bs[bs.length-1].click(); return 'ok';})()"""
                 % (json.dumps(selector), json.dumps(text)))
        if r == "ok":
            return True
        time.sleep(0.5)
    return False


def fill(t, sel, value):
    return t.js("""(() => {const el=document.querySelector(%s); if(!el) return 'missing';
      const proto=el.tagName==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;
      const s=Object.getOwnPropertyDescriptor(proto,'value').set; s.call(el,%s);
      el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true}));
      el.dispatchEvent(new Event('blur',{bubbles:true})); return el.value;})()"""
               % (json.dumps(sel), json.dumps(value)))


def fill_last_empty_field(t, value):
    return t.js("""(() => {const els=[...document.querySelectorAll('input')]
        .filter(i=>i.offsetWidth && i.id.startsWith('bit-form-field') && i.value==='');
      const el=els[els.length-1]; if(!el) return 'missing';
      const s=Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set; s.call(el,%s);
      el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true}));
      el.dispatchEvent(new Event('blur',{bubbles:true})); return el.value;})()""" % json.dumps(value))


def dismiss_overlays(t, texts=("Adicionar mais tarde", "Pular", "Ir para o aplicativo web", "Close")):
    for text in texts:
        for _ in range(2):
            if click_text(t, text, "button,a", timeout=2):
                time.sleep(1.5)


# ----------------------------------------------------------------------------- web vault flow

def seed_via_webvault(base, email, org, item, fields):
    """Create user+org+item using the image's own web vault. Returns item name."""
    b = Browser()
    try:
        t = b.tab()
        t.goto(f"{base}/#/signup", wait=4)
        m = re.search(r"\b(20\d\d\.\d+\.\d+)\b", t.js("document.body.innerText") or "")
        webvault = m.group(1) if m else None

        if not t.js("!!document.querySelector('#register-start_form_input_email')"):
            click_text(t, "Criar conta", "a,button")
            time.sleep(2)
        if not t.js("!!document.querySelector('#register-start_form_input_email')"):
            raise RuntimeError("signup form not found (web vault layout changed?)")

        fill(t, "#register-start_form_input_email", email)
        fill(t, "#register-start_form_input_name", "Compat Lab")
        click_text(t, "Continuar")
        for _ in range(10):
            if "finish-signup" in (t.js("location.href") or ""):
                break
            time.sleep(1)
        if "finish-signup" not in (t.js("location.href") or ""):
            # fallback: pressing Enter in the e-mail field usually submits the form
            t.js("""(() => {const el=document.querySelector('#register-start_form_input_email');
                if(!el) return 0;
                for (const type of ['keydown','keypress','keyup'])
                  el.dispatchEvent(new KeyboardEvent(type,{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true}));
                return 1;})()""")
            time.sleep(3)

        if "finish-signup" not in (t.js("location.href") or ""):
            raise RuntimeError(f"unexpected signup step: {t.js('location.href')}")
        fill(t, "#input-password-form_new-password", MASTER_PW)
        fill(t, "#input-password-form_new-password-confirm", MASTER_PW)
        t.js("""(() => {const c=document.querySelector('#input-password-form_check-for-breaches');
          if(c&&c.checked) c.click(); return 1;})()""")
        time.sleep(1)
        click_text(t, "Criar conta")
        time.sleep(6)

        dismiss_overlays(t)

        # organization
        t.goto(f"{base}/#/vault", wait=4)
        dismiss_overlays(t)
        if not click_text(t, "Nova organização", "a,button", timeout=6):
            # sidebar may be collapsed: go straight to the route
            t.goto(f"{base}/#/create-organization", wait=4)
        time.sleep(4)
        if not t.js("!!document.querySelector('#bit-form-field-5')"):
            raise RuntimeError("organization form did not open: " + (t.js("location.href") or ""))
        print("   org form:", t.js("JSON.stringify([...document.querySelectorAll('input')]"
                                   ".filter(i=>i.offsetWidth).map(i=>({id:i.id,type:i.type,val:i.value})))"), flush=True)
        t.js("""(() => {
          const set=(el,v)=>{const s=Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set;
            s.call(el,v); el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true}));};
          const byId=(id)=>document.querySelector('#'+id);
          if(byId('bit-form-field-5')) set(byId('bit-form-field-5'),%s);
          else {const txt=[...document.querySelectorAll('input')].filter(i=>i.offsetWidth&&i.type==='text'); if(txt[0]) set(txt[0],%s);}
          if(byId('bit-form-field-6')) set(byId('bit-form-field-6'),%s);
          else {const mail=[...document.querySelectorAll('input')].filter(i=>i.offsetWidth&&i.type==='email'); if(mail[0]) set(mail[0],%s);}
          return 1;})()"""
              % (json.dumps(org), json.dumps(org), json.dumps(email), json.dumps(email)))
        time.sleep(1)

        for _ in range(30):
            disabled = t.js("""(() => {const b=[...document.querySelectorAll('button')]
                .find(e=>e.innerText.trim()==='Enviar' && e.offsetWidth); return b ? b.disabled : null;})()""")
            if disabled is False:
                break
            time.sleep(0.5)
        click_text(t, "Enviar")

        deadline = time.time() + 40
        while time.time() < deadline and "organizations/" not in (t.js("location.href") or ""):
            time.sleep(0.5)
        if "organizations/" not in (t.js("location.href") or ""):
            raise RuntimeError("organization not created: " + (t.js("location.href") or "")
                               + " | " + (t.js("document.body.innerText") or "")[:300].replace("\n", " "))

        # item: new UI uses "Adicionar" (Add), older uses "Criar" (Create)
        if not click_text(t, "Adicionar", "button") and not click_text(t, "Criar", "button,a"):
            raise RuntimeError("add-item button not found")
        time.sleep(2)
        if not click_text(t, "Credencial", "button,[role=menuitem]"):
            raise RuntimeError("'Credencial' menu entry not found")
        time.sleep(4)
        if not t.js("!!document.querySelector('#bit-form-field-7')"):
            raise RuntimeError("item form not opened")

        fill(t, "#bit-form-field-7", item)
        for label, value in fields:
            if not click_text(t, "Adicionar campo", "button,a"):
                raise RuntimeError("'Adicionar campo' not found")
            time.sleep(1.5)
            fill(t, "#fieldLabel", label)
            time.sleep(1)
            click_text(t, "Adicionar")
            time.sleep(1.5)
            if fill_last_empty_field(t, value) == "missing":
                raise RuntimeError(f"field value input not found for {label}")
            time.sleep(1)
        if not click_text(t, "Salvar"):
            raise RuntimeError("save button not found")
        time.sleep(6)

        if "itemId=" not in (t.js("location.href") or ""):
            raise RuntimeError(f"item not saved: {t.js('location.href')}")
        return {"item": item, "webvault": webvault}
    finally:
        b.close()


# ----------------------------------------------------------------------------- VW instance

class Instance:
    def __init__(self, version):
        self.version = version
        self.port = free_port()
        self.base = f"https://127.0.0.1:{self.port}"
        self.name = f"vwcompat-{version.replace('.', '')}"
        self.data = WORK / f"data-{self.port}"
        self.email = f"compat-{version.replace('.', '-')}@{LAB_EMAIL_DOMAIN}"
        self.org = f"CompatOrg {version}"
        self.item = f"compat-item-{version}"
        if not re.fullmatch(r"\d+\.\d+\.\d+", version):
            raise ValueError(f"unexpected version format: {version!r}")
        self.namespace = f"{TARGET_NAMESPACE_PREFIX}-{version.replace('.', '-')}"
        self.namespace_created = False
        self.kubeconfig = None
        self.fields = [("namespaces", self.namespace), ("secret-name", self.item), ("token", "abc123")]

    def start(self):
        cert, key = ensure_cert()
        self.data.mkdir(parents=True, exist_ok=True)
        sh([CONTAINER_RUNTIME, "rm", "-f", self.name])  # leftover from an interrupted run
        r = sh([CONTAINER_RUNTIME, "run", "-d", "--name", self.name,
                "-p", f"{self.port}:443",
                "-v", f"{self.data}:/data",
                "-v", f"{cert}:/data/cert.pem:ro", "-v", f"{key}:/data/key.pem:ro",
                "-e", 'ROCKET_TLS={certs="/data/cert.pem",key="/data/key.pem"}',
                "-e", "ROCKET_PORT=443",
                "-e", "SIGNUPS_ALLOWED=true",
                "-e", "LOG_LEVEL=warn",
                "-e", f"DOMAIN={self.base}",
                "-e", f"ADMIN_TOKEN=compat-{uuid.uuid4().hex}",
                f"docker.io/vaultwarden/server:{self.version}"])
        if r.returncode != 0:
            raise RuntimeError(f"podman run failed: {r.stderr.strip()[:300]}")
        for _ in range(90):
            try:
                urllib.request.urlopen(self.base + "/api/config", timeout=2,
                                       context=_noverify())
                return
            except Exception:
                time.sleep(1)
        raise RuntimeError("Vaultwarden did not answer /api/config in 90s")

    def stop(self):
        sh([CONTAINER_RUNTIME, "rm", "-f", self.name])
        shutil.rmtree(self.data, ignore_errors=True)

    def read_db(self):
        db = WORK / f"db-{self.port}.sqlite3"
        sh([CONTAINER_RUNTIME, "cp", f"{self.name}:/data/db.sqlite3", str(db)])
        sh([CONTAINER_RUNTIME, "cp", f"{self.name}:/data/db.sqlite3-wal", str(db) + "-wal"])
        return db

    def api_key(self):
        mk = hashlib.pbkdf2_hmac("sha256", MASTER_PW.encode(), self.email.lower().encode(), 600000, 32)
        mph = base64.b64encode(hashlib.pbkdf2_hmac("sha256", mk, MASTER_PW.encode(), 1, 32)).decode()
        tok = _post(f"{self.base}/identity/connect/token", dict(
            grant_type="password", username=self.email, password=mph, scope="api offline_access",
            client_id="web", deviceType="10", deviceIdentifier=str(uuid.uuid4()), deviceName="compat"))
        at = tok["access_token"]
        ak = _post(f"{self.base}/api/accounts/api-key", {"masterPasswordHash": mph},
                   {"Authorization": f"Bearer {at}"}, form=False)["apiKey"]
        prof = json.loads(_get(f"{self.base}/api/accounts/profile",
                               {"Authorization": f"Bearer {at}"}))
        return prof["id"], ak, prof["organizations"][0]["id"]


def _noverify():
    import ssl
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    return ctx


def _req(url, data=None, headers=None, form=True):
    body = None
    if data is not None:
        body = urllib.parse.urlencode(data).encode() if form else json.dumps(data).encode()
    r = urllib.request.Request(url, data=body, method="POST" if body else "GET")
    r.add_header("Bitwarden-Client-Version", "2024.12.0")
    r.add_header("User-Agent", "Bitwarden_Desktop/2026.7.0")
    if body:
        r.add_header("Content-Type",
                     "application/x-www-form-urlencoded" if form else "application/json")
    for k, v in (headers or {}).items():
        r.add_header(k, v)
    with urllib.request.urlopen(r, timeout=30, context=_noverify()) as x:
        return x.read().decode()


def _post(url, data, headers=None, form=True):
    return json.loads(_req(url, data, headers, form))


def _get(url, headers=None):
    return _req(url, None, headers)


# ----------------------------------------------------------------------------- VKS run

def run_vks(inst, vks_image):
    uid, secret, org_id = inst.api_key()
    cert, _ = ensure_cert()

    # One kubeconfig per worker: parallel runs must not truncate each other's file.
    kubeconfig = WORK / f"kubeconfig-{inst.port}.yaml"
    src = Path(os.environ.get("KUBECONFIG") or (Path.home() / ".kube" / "config"))
    text = src.read_text()
    if KUBE_CONTEXT:
        text = re.sub(r"(?m)^current-context:.*$", f"current-context: {KUBE_CONTEXT}", text, count=1)
    kubeconfig.write_text(text)

    inst.kubeconfig = kubeconfig
    kubectl = [KUBECTL, "--kubeconfig", str(kubeconfig)]
    if KUBE_CONTEXT:
        kubectl += ["--context", KUBE_CONTEXT]
    # The item targets inst.namespace; without it the VKS skips the secret (no reconciliation).
    create_ns = subprocess.run([*kubectl, "create", "ns", inst.namespace], capture_output=True)
    inst.namespace_created = create_ns.returncode == 0
    for _ in range(20):
        phase = subprocess.run([*kubectl, "get", "ns", inst.namespace, "-o",
                                "jsonpath={.status.phase}"], capture_output=True, text=True)
        if phase.stdout.strip() == "Active":
            break
        time.sleep(1)

    tag = f"vkscompat-{inst.version.replace('.', '')}"
    log = WORK / f"log-{inst.port}.txt"
    cmd = [CONTAINER_RUNTIME, "run", "--rm", "--name", tag, "--network=host",
           "-e", f"BW_CLIENTID=user.{uid}", "-e", f"BW_CLIENTSECRET={secret}",
           "-e", f"VAULTWARDEN__MASTERPASSWORD={MASTER_PW}",
           "-e", f"VAULTWARDEN__SERVERURL={inst.base}",
           "-e", f"VAULTWARDEN__ORGANIZATIONID={org_id}",
           "-e", f"VAULTWARDEN__ORGANIZATIONNAME={inst.org}",
           "-e", f"VAULTWARDEN__EMAIL={inst.email}",
           "-e", "KUBERNETES__INCLUSTER=false", "-e", f"KUBERNETES__DEFAULTNAMESPACE={inst.namespace}",
           "-e", "SYNC__DRYRUN=true", "-e", "SYNC__DELETEORPHANS=false",
           "-e", "SYNC__CONTINUOUSSYNC=false", "-e", "SYNC__SYNCINTERVALSECONDS=3600",
           "-e", "LOGGING__LOGLEVEL__DEFAULT=Information", "-e", "DATABASE_PATH=/tmp/sync.db",
           "-e", "SSL_CERT_FILE=/cert.pem",
           "-v", f"{cert}:/cert.pem:ro", "-v", f"{kubeconfig}:/kube/config:ro",
           "-e", "KUBECONFIG=/kube/config", vks_image]

    timed_out = False
    returncode = 0
    with open(log, "w") as f:
        try:
            proc = subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT, timeout=180)
            returncode = proc.returncode
        except subprocess.TimeoutExpired:
            timed_out = True
            returncode = -1
            f.write("\n[TIMEOUT]\n")

    if returncode == 125:
        # The container runtime refused to start the image: a launch problem, not a VKS verdict.
        first_error = next((ln for ln in log.read_text(errors="ignore").splitlines()
                            if "rror" in ln), "")[:200]
        raise RuntimeError(f"container runtime could not start the VKS image (exit 125): {first_error}")

    text = log.read_text(errors="ignore")
    # Item detection alone proves nothing: the secret is only reconciled after the namespace
    # check. Require the dry-run reconciliation line for this item.
    synced = bool(re.search(rf"\[DRY RUN\] Would (sync item {re.escape(inst.item)}|apply)", text))
    already_ok = bool(re.search(r"Up-to-date: [1-9]", text)) and f"Namespace '{inst.namespace}' does not exist" not in text
    reconciled = (synced or already_ok) and f"Namespace '{inst.namespace}' does not exist" not in text
    return {
        "pad_errors": text.count("DecryptString failed"),
        "secrets_detected": len(re.findall(r"Secret '[^']*' .*detected", text)),
        "item_name_decoded": bool(re.search(rf"Secret '{re.escape(inst.item)}'", text)),
        "reconciled": reconciled,
        "process_failed": timed_out or returncode != 0,
        "returncode": returncode,
        "timed_out": timed_out,
        "vks_log": str(log),
    }


# ----------------------------------------------------------------------------- per version

def webvault_version(inst):
    try:
        html = _get(inst.base + "/")
        m = re.findall(r"\b(20\d\d\.\d+\.\d+)\b", html)
        return m[0] if m else None
    except Exception:
        return None


def cleanup_namespace(inst):
    """Delete the lab namespace, but only when this run created it."""
    if not getattr(inst, "namespace_created", False) or inst.kubeconfig is None:
        return
    kubectl = [KUBECTL, "--kubeconfig", str(inst.kubeconfig)]
    if KUBE_CONTEXT:
        kubectl += ["--context", KUBE_CONTEXT]
    subprocess.run([*kubectl, "delete", "ns", inst.namespace, "--wait=false"], capture_output=True)


def test_version(version, vks_image):
    inst = Instance(version)
    started = time.time()
    try:
        inst.start()
        seeded = seed_via_webvault(inst.base, inst.email, inst.org, inst.item, inst.fields)
        wv = seeded.get("webvault")

        db = inst.read_db()
        conn = __import__("sqlite3").connect(db)
        has_key = False
        try:
            for (k,) in conn.execute("select key from ciphers"):
                if k:
                    has_key = True
                    break
        finally:
            conn.close()
        db.unlink(missing_ok=True)
        Path(str(db) + "-wal").unlink(missing_ok=True)

        vks = run_vks(inst, vks_image)
        ok = (vks["pad_errors"] == 0 and vks["item_name_decoded"] and vks["reconciled"]
              and not vks["process_failed"])
        return {
            "version": version,
            "webvault": wv,
            "has_item_key": has_key,
            "vks_image": vks_image,
            "ok": ok,
            "pad_errors": vks["pad_errors"],
            "secrets_detected": vks["secrets_detected"],
            "item_name_decoded": vks["item_name_decoded"],
            "reconciled": vks["reconciled"],
            "vks_returncode": vks["returncode"],
            "duration_s": round(time.time() - started, 1),
            "tested_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        }
    finally:
        inst.stop()
        cleanup_namespace(inst)


# ----------------------------------------------------------------------------- report

def write_report(state, pending):
    rows = sorted(state["tested"].values(), key=lambda r: semver_key(r["version"]))
    fails = [r for r in rows if not r["ok"]]
    html = ["""<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>VKS × Vaultwarden — matriz de compatibilidade</title>
<style>
:root{color-scheme:light dark}
body{font:14px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif;margin:0;padding:22px 26px;color:var(--foreground,#1a1a1a);background:transparent}
h1{font-size:18px;margin:0 0 4px}h2{font-size:14px;margin:22px 0 8px}
.sub{color:var(--muted-foreground,#666);margin-bottom:16px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{border:1px solid var(--border,#ddd);padding:6px 9px;text-align:left}
th{background:var(--card,#f6f6f6)}
code{font-family:ui-monospace,Menlo,monospace;font-size:12px}
.bad{color:#b42318;font-weight:600}.good{color:#067647;font-weight:600}
.note{border-left:3px solid var(--accent,#4a7dff);background:var(--card,#f7f8fa);padding:8px 12px;margin:12px 0}
</style></head><body>""",
            "<h1>VKS × Vaultwarden — matriz de compatibilidade</h1>",
            f'<div class="sub">last run: {time.strftime("%Y-%m-%d %H:%M")} · '
            f'<code>{len(rows)}</code> versions tested · VKS image <code>{rows[-1]["vks_image"] if rows else "-"}</code></div>']

    if fails:
        html.append(f'<div class="note"><b>{len(fails)} version(s) break the VKS.</b> '
                    'Items created by the web vault of these versions carry <code>ciphers.key</code> (per-item key).</div>')
    html.append("<h2>Results</h2><table><thead><tr><th>Vaultwarden</th><th>web vault</th>"
                "<th>ciphers.key</th><th>VKS</th><th>pad errors</th><th>secrets</th><th>tested at</th></tr></thead><tbody>")
    for r in rows:
        cls = "good" if r["ok"] else "bad"
        html.append(
            f'<tr><td><b>{r["version"]}</b></td><td>{r["webvault"] or "-"}</td>'
            f'<td>{"yes" if r["has_item_key"] else "no"}</td>'
            f'<td class="{cls}">{"OK" if r["ok"] else "FALHA"}</td>'
            f'<td>{r["pad_errors"]}</td><td>{r["secrets_detected"]}</td>'
            f'<td>{r["tested_at"][:16]}</td></tr>')
    html.append("</tbody></table>")

    if pending:
        html.append("<h2>Pending</h2><ul>" + "".join(f"<li>{v}</li>" for v in pending) + "</ul>")
    html.append('<h2>How it works</h2><p class="sub">Each version runs in a throwaway container with a self-signed TLS cert; '
                'a user+org+item is created <b>through the bundled web vault UI</b> (the client defines the key format) '
                'the sqlite is inspected (<code>ciphers.key</code>) and the VKS image runs in '
                '<code>SYNC__DRYRUN</code> against it. Failure = padding error or item name could not be decoded.</p>')
    html.append("</body></html>")

    rendered = "\n".join(html)
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(rendered)
    # Copy inside the workspace too so CI can upload it as an artifact.
    (WORK / "report.html").write_text(rendered)
    return rows, fails


# ----------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--versions", nargs="*")
    ap.add_argument("--max-new", type=int, default=3)
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--min-version", default="1.37.0")
    ap.add_argument("--recheck", action="store_true", help="re-test versions already in state")
    ap.add_argument("--jobs", type=int, default=int(os.environ.get("COMPAT_JOBS", "1")),
                    help="versions tested in parallel (each one gets its own port/container/profile)")
    args = ap.parse_args()

    WORK.mkdir(parents=True, exist_ok=True)
    state = load_state()
    vks_image = os.environ.get("VKS_IMAGE", DEFAULT_VKS_IMAGE)

    if args.versions:
        candidates = args.versions
    else:
        try:
            candidates = [v for v in dockerhub_versions() if semver_key(v) >= semver_key(args.min_version)]
        except Exception as e:
            print(f"warning: docker hub check failed ({e}); using state only", file=sys.stderr)
            candidates = []

    state.setdefault("errors", {})

    def is_pending(v):
        if args.recheck or v in state["errors"]:
            return True
        record = state["tested"].get(v)
        if record is None:
            return True
        # A different image invalidates previous results: the question being asked changed.
        return record.get("vks_image") != vks_image

    pending = [v for v in candidates if is_pending(v)]
    if args.check:
        print(json.dumps({"pending": pending, "tested": len(state["tested"])}))
        return

    if not pending:
        rows, fails = write_report(state, [])
        print(f"nothing new (min {args.min_version}); {len(rows)} versions in history; report: {REPORT}")
        return

    # Explicit versions are a direct request: test all of them, ignore the incremental limit.
    batch = pending if args.versions else pending[:args.max_new]
    ensure_cert()  # before the pool, so parallel workers never race on the TLS pair
    if args.jobs > 1 and len(batch) > 1:
        from concurrent.futures import ThreadPoolExecutor

        def _run(v):
            try:
                return v, test_version(v, vks_image), None
            except Exception as e:  # harness failure, not a product verdict
                return v, None, str(e)[:300]

        print(f"== testing {len(batch)} versions with {min(args.jobs, len(batch))} jobs", flush=True)
        with ThreadPoolExecutor(max_workers=min(args.jobs, len(batch))) as pool:
            for v, res, err in pool.map(_run, batch):
                if err:
                    # Harness failure, not a product verdict: keep it pending for the next run.
                    print(f"   {v}: harness error: {err}", flush=True)
                    state["errors"][v] = {
                        "version": v, "error": err,
                        "vks_image": vks_image,
                        "at": time.strftime("%Y-%m-%dT%H:%M:%S%z")}
                else:
                    state["tested"][v] = res
                    state["errors"].pop(v, None)
                    print(f"   {v}: {'OK' if res['ok'] else 'FALHA'} webvault={res['webvault']} "
                          f"item_key={res['has_item_key']} pad={res['pad_errors']} "
                          f"secrets={res['secrets_detected']} ({res['duration_s']}s)", flush=True)
                save_state(state)
    else:
        for v in batch:
            print(f"== {v}: starting lab...", flush=True)
            try:
                res = test_version(v, vks_image)
                state["tested"][v] = res
                state["errors"].pop(v, None)
                save_state(state)
                print(f"   {'OK' if res['ok'] else 'FALHA'}: webvault={res['webvault']} "
                      f"item_key={res['has_item_key']} pad={res['pad_errors']} "
                      f"secrets={res['secrets_detected']} ({res['duration_s']}s)", flush=True)
            except Exception as e:
                # Harness failure, not a product verdict: keep it pending for the next run.
                print(f"   {v}: harness error: {e}", flush=True)
                state["errors"][v] = {
                    "version": v, "error": str(e)[:300],
                    "vks_image": vks_image,
                    "at": time.strftime("%Y-%m-%dT%H:%M:%S%z")}
                save_state(state)

    batch_fails = [v for v in batch if v in state["tested"] and not state["tested"][v].get("ok")]
    batch_errors = [v for v in batch if v in state["errors"]]
    if batch_errors:
        print(f"harness errors (kept pending): {', '.join(batch_errors)}")
    rows, fails = write_report(state, [v for v in pending[args.max_new:]])
    print(f"\n{len(rows)} versions in history · {len(fails)} failing "
          f"(in this run: {len(batch_fails)}/{len(batch)}) · report: {REPORT}")
    # exit code reflects this run, not known historical failures
    sys.exit(1 if batch_fails else 0)


if __name__ == "__main__":
    main()
