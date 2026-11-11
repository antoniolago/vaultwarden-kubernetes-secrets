#!/usr/bin/env python3
"""Repro: a new Vaultwarden item is not picked up between continuous VKS syncs.

  1. sobe VW 1.37.5 (lab do compat) e cria conta+org+item "item-a" (namespaces=vks-repro) pela UI;
  2. starts the VKS in continuous mode (15s), namespace vks-repro;
  3. confirma o Secret de item-a;
  4. inserts "item-b" straight into the sqlite (encrypted with the org key) and copies the DB back;
  5. watches for up to 120s to see whether item-b shows up without a VKS restart;
  6. if it does not, restarts only the VKS container and confirms.
"""
import base64
import hashlib
import hmac
import json
import os
import sqlite3
import shutil
import subprocess
import sys
import time
import uuid
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding as ap
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives.padding import PKCS7
from cryptography.hazmat.primitives.serialization import load_der_private_key

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_matrix as lab

VKS_IMAGE = os.environ.get("VKS_IMAGE", "vks-main:test")
NS = "compat-new-secret"
NS2 = "compat-new-namespace"
TAG = "vks-compat-scenario"
SCRATCH = Path.home() / ".hermes/cache/scratch/repro-new-item"
SCRATCH.mkdir(parents=True, exist_ok=True)
KC = ["--context", lab.KUBE_CONTEXT] if lab.KUBE_CONTEXT else []


def b64d(s):
    return base64.b64decode(s + "=" * (-len(s) % 4))


def aes_dec(enc, ek, mk=None):
    parts = enc.split(".", 1)[1].split("|")
    iv, ct = b64d(parts[0]), b64d(parts[1])
    if len(parts) > 2 and mk:
        assert hmac.compare_digest(hmac.new(mk, iv + ct, hashlib.sha256).digest(), b64d(parts[2]))
    d = Cipher(algorithms.AES(ek), modes.CBC(iv)).decryptor()
    raw = d.update(ct) + d.finalize()
    u = PKCS7(128).unpadder()
    return u.update(raw) + u.finalize()


def aes_enc(plain, sym):
    ek, mk = sym[:32], sym[32:64]
    iv = os.urandom(16)
    e = Cipher(algorithms.AES(ek), modes.CBC(iv)).encryptor()
    p = PKCS7(128).padder()
    ct = e.update(p.update(plain) + p.finalize()) + e.finalize()
    return (f"2.{base64.b64encode(iv).decode()}|{base64.b64encode(ct).decode()}"
            f"|{base64.b64encode(hmac.new(mk, iv + ct, hashlib.sha256).digest()).decode()}")


def org_key_from_db(db, email, password):
    c = sqlite3.connect(db)
    akey, priv = c.execute("select akey, private_key from users where email=?", (email,)).fetchone()
    org_akey = c.execute("select akey from users_organizations").fetchone()[0]
    c.close()
    mk = hashlib.pbkdf2_hmac("sha256", password.encode(), email.lower().encode(), 600000, 32)

    def hkdf(k, info, n=32):
        out = p = b""
        for i in range(1, (n + 31) // 32 + 1):
            p = hmac.new(k, p + info + bytes([i]), hashlib.sha256).digest()
            out += p
        return out[:n]

    sym = aes_dec(akey, hkdf(mk, b"enc"), hkdf(mk, b"mac"))
    rsa = load_der_private_key(aes_dec(priv, sym[:32], sym[32:64]), password=None)
    return rsa.decrypt(b64d(org_akey[2:]),
                       ap.OAEP(mgf=ap.MGF1(hashes.SHA1()), algorithm=hashes.SHA1(), label=None))


def insert_item(db, org_sym, name, values):
    c = sqlite3.connect(db, timeout=20)
    org_uuid = c.execute("select org_uuid from users_organizations").fetchone()[0]
    coll = c.execute("select uuid from collections where org_uuid=?", (org_uuid,)).fetchone()[0]
    uid, now = str(uuid.uuid4()), time.strftime("%Y-%m-%d %H:%M:%S")
    fields = [{"name": aes_enc(k.encode(), org_sym), "value": aes_enc(v.encode(), org_sym), "type": 0}
              for k, v in values.items()]
    data = json.dumps({"username": aes_enc(b"svc-b", org_sym),
                       "password": aes_enc(b"SecretB123", org_sym), "uris": []})
    c.execute("""insert into ciphers (uuid, created_at, updated_at, user_uuid, organization_uuid, atype,
                 name, notes, fields, data, password_history, deleted_at, reprompt, key)
                 values (?,?,?,NULL,?,1,?,NULL,?,?,NULL,NULL,0,NULL)""",
              (uid, now, now, org_uuid, aes_enc(name.encode(), org_sym), json.dumps(fields), data))
    c.execute("insert into ciphers_collections (cipher_uuid, collection_uuid) values (?,?)", (uid, coll))
    c.commit()
    c.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    c.close()



def add_item_via_ui(inst, item, values):
    """Logs into the web vault and creates a second item in the org (the real user path)."""
    b = lab.Browser()
    try:
        t = b.tab()
        t.goto(f"{inst.base}/#/login", wait=4)
        lab.fill(t, "#email", inst.email)
        if not lab.click_text(t, "Continuar"):
            raise RuntimeError("login: Continuar button not found")
        time.sleep(3)
        if not t.js("!!document.querySelector('#masterPassword')"):
            raise RuntimeError("login: password field did not appear")
        lab.fill(t, "#masterPassword", lab.MASTER_PW)
        if not lab.click_text(t, "Conectar-se"):
            raise RuntimeError("login: Conectar-se button not found")
        time.sleep(6)
        lab.dismiss_overlays(t)
        t.goto(f"{inst.base}/#/vault", wait=4)
        lab.dismiss_overlays(t)
        if not lab.click_text(t, inst.org, "a,button,span,div"):
            raise RuntimeError(f"org '{inst.org}' not found in the sidebar")
        time.sleep(4)
        if not (lab.click_text(t, "Adicionar", "button") or lab.click_text(t, "Criar", "button,a")):
            raise RuntimeError("add-item button not found")
        time.sleep(2)
        if not lab.click_text(t, "Credencial", "button,[role=menuitem]"):
            raise RuntimeError("entrada Credencial não encontrada")
        time.sleep(4)
        if not t.js("!!document.querySelector('#bit-form-field-7')"):
            raise RuntimeError("item form did not open")
        lab.fill(t, "#bit-form-field-7", item)
        for label, value in values:
            if not lab.click_text(t, "Adicionar campo", "button,a"):
                raise RuntimeError("Adicionar campo não encontrado")
            time.sleep(1.5)
            lab.fill(t, "#fieldLabel", label)
            time.sleep(1)
            lab.click_text(t, "Adicionar")
            time.sleep(1.5)
            if lab.fill_last_empty_field(t, value) == "missing":
                raise RuntimeError(f"value input not found for {label}")
            time.sleep(1)
        if not lab.click_text(t, "Salvar"):
            raise RuntimeError("Salvar não encontrado")
        time.sleep(5)
        return item
    finally:
        b.close()


def api_session(inst):
    mk = hashlib.pbkdf2_hmac("sha256", lab.MASTER_PW.encode(), inst.email.lower().encode(), 600000, 32)
    mph = base64.b64encode(hashlib.pbkdf2_hmac("sha256", mk, lab.MASTER_PW.encode(), 1, 32)).decode()
    body = urllib.parse.urlencode(dict(grant_type="password", username=inst.email, password=mph,
                                       scope="api offline_access", client_id="web", deviceType="10",
                                       deviceIdentifier=str(uuid.uuid4()), deviceName="repro")).encode()
    r = urllib.request.Request(inst.base + "/identity/connect/token", data=body, method="POST")
    r.add_header("Content-Type", "application/x-www-form-urlencoded")
    r.add_header("Bitwarden-Client-Version", "2024.12.0")
    r.add_header("User-Agent", "Bitwarden_Desktop/2026.7.0")
    with urllib.request.urlopen(r, timeout=30, context=lab._noverify()) as x:
        return json.loads(x.read().decode())["access_token"]


def api_get(inst, token, path):
    r = urllib.request.Request(inst.base + path)
    r.add_header("Authorization", "Bearer " + token)
    r.add_header("Bitwarden-Client-Version", "2024.12.0")
    r.add_header("User-Agent", "Bitwarden_Desktop/2026.7.0")
    with urllib.request.urlopen(r, timeout=30, context=lab._noverify()) as x:
        return json.loads(x.read().decode())


def api_post(inst, token, path, body):
    r = urllib.request.Request(inst.base + path, data=json.dumps(body).encode(), method="POST")
    r.add_header("Authorization", "Bearer " + token)
    r.add_header("Content-Type", "application/json")
    r.add_header("Bitwarden-Client-Version", "2024.12.0")
    r.add_header("User-Agent", "Bitwarden_Desktop/2026.7.0")
    try:
        with urllib.request.urlopen(r, timeout=30, context=lab._noverify()) as x:
            return x.status, x.read().decode()[:200]
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:300]


def create_org_item_via_api(inst, token, org_sym, name, values):
    prof = api_get(inst, token, "/api/accounts/profile")
    org = prof["organizations"][0]["id"]
    sync = api_get(inst, token, "/api/sync")
    coll = next(c["id"] for c in sync["collections"] if c["organizationId"] == org)
    body = {"cipher": {
        "type": 1,
        "name": aes_enc(name.encode(), org_sym),
        "notes": None,
        "login": {"username": aes_enc(b"svc-b", org_sym),
                  "password": aes_enc(b"SecretB123", org_sym), "uris": []},
        "fields": [{"name": aes_enc(k.encode(), org_sym), "value": aes_enc(v.encode(), org_sym), "type": 0}
                   for k, v in values.items()],
        "organizationId": org},
        "collectionIds": [coll]}
    status, resp = api_post(inst, token, "/api/ciphers", body)
    if status >= 300:
        status2, resp2 = api_post(inst, token, "/api/ciphers/create", body)
        return status2, f"/ciphers={status}:{resp[:120]} /create={status2}:{resp2[:120]}"
    return status, resp


def count_org_items(inst, token):
    data = api_get(inst, token, "/api/ciphers")
    items = data.get("data", data)
    return [(i.get("organizationId") or "personal")[:8] for i in items]



TEMPLATE = lab.WORK / "template-vw"


def prepare_instance(version="1.37.5"):
    """Ready instance (user+org+item-a). Reuses a template when present: ~10s instead of ~3min."""
    inst = lab.Instance(version)
    if TEMPLATE.exists() and any(TEMPLATE.iterdir()):
        shutil.rmtree(inst.data, ignore_errors=True)
        inst.data.mkdir(parents=True, exist_ok=True)
        subprocess.run(["cp", "-a", f"{TEMPLATE}/.", str(inst.data)], check=True)
        inst.start()
        return inst, False

    inst.start()
    lab.seed_via_webvault(inst.base, inst.email, inst.org, "item-a",
                          [("namespaces", NS), ("secret-name", "item-a"), ("token", "abc123")])
    subprocess.run(["podman", "stop", inst.name], capture_output=True, check=True)
    TEMPLATE.mkdir(parents=True, exist_ok=True)
    shutil.rmtree(TEMPLATE, ignore_errors=True)
    TEMPLATE.mkdir(parents=True, exist_ok=True)
    subprocess.run(["cp", "-a", f"{inst.data}/.", str(TEMPLATE)], check=True)
    subprocess.run(["podman", "rm", "-f", inst.name], capture_output=True)
    shutil.rmtree(inst.data, ignore_errors=True)
    inst.data.mkdir(parents=True, exist_ok=True)
    subprocess.run(["cp", "-a", f"{TEMPLATE}/.", str(inst.data)], check=True)
    inst.start()
    return inst, True

def exists(name, ns=None):
    return subprocess.run(["kubectl", *KC, "-n", ns or NS, "get", "secret", name],
                          capture_output=True).returncode == 0


def vks_start(inst, uid, secret, org_id, tag):
    cert, _ = lab.ensure_cert()
    kc = SCRATCH / "kubeconfig.yaml"
    text = (Path.home() / ".kube/config").read_text()
    if lab.KUBE_CONTEXT:
        text = lab.re.sub(r"(?m)^current-context:.*$", f"current-context: {lab.KUBE_CONTEXT}", text, count=1)
    kc.write_text(text)
    r = subprocess.run(["podman", "run", "-d", "--name", tag, "--network=host",
                        "-e", f"BW_CLIENTID=user.{uid}", "-e", f"BW_CLIENTSECRET={secret}",
                        "-e", f"VAULTWARDEN__MASTERPASSWORD={lab.MASTER_PW}",
                        "-e", f"VAULTWARDEN__SERVERURL={inst.base}",
                        "-e", f"VAULTWARDEN__ORGANIZATIONID={org_id}",
                        "-e", f"VAULTWARDEN__ORGANIZATIONNAME={inst.org}",
                        "-e", f"VAULTWARDEN__EMAIL={inst.email}",
                        "-e", "KUBERNETES__INCLUSTER=false", "-e", f"KUBERNETES__DEFAULTNAMESPACE={NS}",
                        "-e", "SYNC__DRYRUN=false", "-e", "SYNC__DELETEORPHANS=false",
                        "-e", "SYNC__CONTINUOUSSYNC=true", "-e", "SYNC__SYNCINTERVALSECONDS=15",
                        "-e", "LOGGING__LOGLEVEL__DEFAULT=Debug",
                        "-e", "DATABASE_PATH=/tmp/sync.db", "-e", "SSL_CERT_FILE=/cert.pem",
                        "-v", f"{cert}:/cert.pem:ro", "-v", f"{kc}:/kube/config:ro",
                        "-e", "KUBECONFIG=/kube/config", VKS_IMAGE], capture_output=True, text=True)
    if r.returncode != 0:
        raise RuntimeError(r.stderr[:400])




def wait_config(inst, tries=40):
    for _ in range(tries):
        try:
            lab.urllib.request.urlopen(inst.base + "/api/config", timeout=2, context=lab._noverify())
            return True
        except Exception:
            time.sleep(1)
    return False


def main():
    for _ in range(30):
        out = subprocess.run(["kubectl", *KC, "get", "ns", NS], capture_output=True, text=True)
        if "NotFound" in out.stderr:
            break
        time.sleep(2)
    subprocess.run(["kubectl", *KC, "create", "ns", NS], capture_output=True)

    inst, seeded = prepare_instance("1.37.5")
    print(f"VW {inst.base} ({'seed' if seeded else 'template'})", flush=True)
    try:
        uid, secret, org_id = inst.api_key()
        subprocess.run(["podman", "rm", "-f", TAG], capture_output=True)
        vks_start(inst, uid, secret, org_id, TAG)

        for _ in range(20):
            if exists("item-a"):
                break
            time.sleep(3)
        print(f"item-a no cluster: {exists('item-a')}", flush=True)

        # --- DB surgery: stop VW, pull the DB out, insert item-b, copy it back, start VW ---
        subprocess.run(["podman", "stop", inst.name], capture_output=True, check=True)
        db = Path.home() / ".hermes/cache/scratch/repro-new-item" / "live.db"
        db.parent.mkdir(parents=True, exist_ok=True)
        for suffix, name in (("", "db.sqlite3"), ("-wal", "db.sqlite3-wal"), ("-shm", "db.sqlite3-shm")):
            subprocess.run(["podman", "cp", f"{inst.name}:/data/{name}", str(db) + suffix],
                           capture_output=True)

        org_sym = org_key_from_db(db, inst.email, lab.MASTER_PW)
        insert_item(db, org_sym, "item-c", {"namespaces": NS2, "secret-name": "item-c", "token": "abc123"})

        subprocess.run(["podman", "cp", str(db), f"{inst.name}:/data/db.sqlite3"], check=True, capture_output=True)
        subprocess.run(["podman", "exec", inst.name, "rm", "-f", "/data/db.sqlite3-wal", "/data/db.sqlite3-shm"],
                       capture_output=True)
        subprocess.run(["podman", "start", inst.name], capture_output=True, check=True)
        wait_config(inst)

        tok = api_session(inst)
        print("API after insert:", count_org_items(inst, tok), flush=True)

        # create the namespace AFTERWARDS, like a new service rollout would
        time.sleep(15)
        subprocess.run(["kubectl", *KC, "create", "ns", NS2], capture_output=True)
        print(f"namespace {NS2} criado; observando...", flush=True)

        appeared = False
        for i in range(24):
            time.sleep(5)
            if exists("item-c", NS2):
                appeared = True
                print(f"OK: item-c appeared ~{(i + 1) * 5}s after the namespace was created", flush=True)
                break
        if not appeared:
            print("FAIL: item-c did not appear within 120s even with the namespace in place", flush=True)
            print("restarting the VKS container...", flush=True)
            subprocess.run(["podman", "restart", TAG], capture_output=True)
            for _ in range(24):
                if exists("item-c", NS2):
                    print("OK: item-c appeared after the VKS restart", flush=True)
                    break
                time.sleep(5)
            logs = subprocess.run(["podman", "logs", TAG], capture_output=True, text=True).stdout
            for line in logs.splitlines():
                if any(k in line for k in ("No changes", "Items from Vaultwarden", "Created", "detected")):
                    print("   log:", line[:150], flush=True)
    finally:
        subprocess.run(["podman", "rm", "-f", TAG], capture_output=True)
        inst.stop()
        subprocess.run(["kubectl", *KC, "delete", "ns", NS, NS2, "--wait=false"], capture_output=True)


if __name__ == "__main__":
    main()
