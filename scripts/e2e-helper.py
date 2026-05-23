#!/usr/bin/env python3
"""
E2E test helper for vaultwarden operations.
Handles registration, login, API key retrieval, and encrypted item creation.
"""
import hashlib, hmac, base64, json, logging, os, sys, urllib.request, urllib.error

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import padding
from cryptography.hazmat.backends import default_backend


# ── Crypto helpers ─────────────────────────────────────────────────────────

def pbkdf2_sha256(password, salt, iterations, dklen=32):
    return hashlib.pbkdf2_hmac('sha256', password, salt, iterations, dklen)

def hkdf_expand(prk, info, length):
    h = hmac.new(prk, info + b'\x01', hashlib.sha256)
    return h.digest()[:length]

def aes_cbc_encrypt(plaintext, key, iv):
    padder = padding.PKCS7(128).padder()
    padded = padder.update(plaintext) + padder.finalize()
    cipher = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend())
    enc = cipher.encryptor()
    return enc.update(padded) + enc.finalize()

def encrypt_bitwarden_v1(plaintext, enc_key, mac_key):
    iv = os.urandom(16)
    ct = aes_cbc_encrypt(plaintext, enc_key, iv)
    mac = hmac.new(mac_key, iv + ct, hashlib.sha256).digest()
    return ".".join([base64.b64encode(iv).decode(),
                     base64.b64encode(ct).decode(),
                     base64.b64encode(mac).decode()])

def encrypt_bitwarden_v2(plaintext, enc_key, mac_key):
    iv = os.urandom(16)
    ct = aes_cbc_encrypt(plaintext, enc_key, iv)
    mac = hmac.new(mac_key, iv + ct, hashlib.sha256).digest()
    parts = "|".join([
        base64.b64encode(iv).decode(),
        base64.b64encode(ct).decode(),
        base64.b64encode(mac).decode(),
    ])
    return "2." + parts

# Default to V2 format (used by sync service)
encrypt_bitwarden = encrypt_bitwarden_v2

def make_mac_key(enc_key):
    return hkdf_expand(enc_key, b"mac", 32)

def derive_master_key(password, email, iterations=600000):
    return pbkdf2_sha256(password.encode(), email.lower().encode(), iterations)

def compute_auth_hash(password, email, iterations=600000):
    mk = derive_master_key(password, email, iterations)
    return base64.b64encode(hkdf_expand(mk, b"auth", 32)).decode()


# ── API Helpers ────────────────────────────────────────────────────────────

def api_post(url, data=None, token=None, headers_extra=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    if headers_extra:
        headers.update(headers_extra)
    body = json.dumps(data).encode() if data else b'{}'
    req = urllib.request.Request(url, data=body, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code}: {e.read().decode()[:300]}") from e

def api_get(url, token=None):
    headers = {}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code}: {e.read().decode()[:300]}") from e


# ── High-level operations ─────────────────────────────────────────────────

def register_user(server, email, password, iterations=600000):
    mk = derive_master_key(password, email, iterations)
    auth_hash = compute_auth_hash(password, email, iterations)
    user_key = os.urandom(64)
    mac_master = make_mac_key(mk)
    enc_user_key = encrypt_bitwarden(user_key, mk, mac_master)

    # RSA key pair
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.hazmat.primitives import serialization
    priv = rsa.generate_private_key(65537, 2048, backend=default_backend())
    pub = priv.public_key()
    priv_der = priv.private_bytes(serialization.Encoding.DER,
                                   serialization.PrivateFormat.PKCS8,
                                   serialization.NoEncryption())
    pub_der = pub.public_bytes(serialization.Encoding.DER,
                                serialization.PublicFormat.SubjectPublicKeyInfo)

    # Encrypt private key with user key
    enc_priv = encrypt_bitwarden(priv_der, user_key[:32], user_key[32:64])
    pub_b64 = base64.b64encode(pub_der).decode()

    payload = {
        "email": email,
        "masterPasswordHash": auth_hash,
        "masterPasswordHint": "",
        "name": "E2E User",
        "kdf": 0,
        "kdfIterations": iterations,
        "key": enc_user_key,
        "keys": {
            "encryptedPrivateKey": enc_priv,
            "publicKey": pub_b64,
        },
    }
    url = f"{server.rstrip('/')}/identity/accounts/register"
    return api_post(url, payload)

def login_get_session(server, email, password_hash, device_id="e2e-device"):
    url = f"{server.rstrip('/')}/identity/connect/token"
    body = urllib.parse.urlencode({
        "grant_type": "password",
        "username": email,
        "password": password_hash,
        "scope": "api offline_access",
        "client_id": "web",
        "deviceType": "3",
        "deviceIdentifier": device_id,
        "deviceName": "e2e-test",
        "devicePushToken": "",
    }).encode()
    req = urllib.request.Request(url, data=body,
        headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode())["access_token"]
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"Login HTTP {e.code}: {e.read().decode()[:300]}") from e

def get_api_key(server, session, password_hash):
    url = f"{server.rstrip('/')}/api/accounts/api-key"
    resp = api_post(url, {"masterPasswordHash": password_hash}, token=session)
    api_key = resp.get("apiKey", "")
    # Get userId from profile
    profile = api_get(f"{server.rstrip('/')}/api/accounts/profile", token=session)
    user_id = profile.get("id", "")
    return api_key, user_id

def encrypt_cipher_field(value, enc_key, mac_key):
    return encrypt_bitwarden(value.encode(), enc_key, mac_key)

def create_cipher(server, session, enc_key, mac_key, name, login_username="",
                  login_password="", fields=None, notes=""):
    enc_name = encrypt_cipher_field(name, enc_key, mac_key)
    enc_notes = encrypt_cipher_field(notes, enc_key, mac_key) if notes else None
    enc_user = encrypt_cipher_field(login_username, enc_key, mac_key) if login_username else None
    enc_pass = encrypt_cipher_field(login_password, enc_key, mac_key) if login_password else None

    payload = {
        "type": 1,
        "name": enc_name,
        "login": {},
        "fields": [],
    }
    if enc_notes:
        payload["notes"] = enc_notes
    if login_username or login_password:
        payload["login"] = {
            "username": enc_user or "",
            "password": enc_pass or "",
        }
    for f in (fields or []):
        payload["fields"].append({
            "name": encrypt_cipher_field(f["name"], enc_key, mac_key),
            "value": encrypt_cipher_field(f["value"], enc_key, mac_key),
            "type": f.get("type", 0),
        })
    url = f"{server.rstrip('/')}/api/ciphers"
    return api_post(url, payload, token=session)

def parse_v2_encrypted(akey):
    if akey.startswith("2."):
        inner = akey[2:]
        parts = inner.split("|")
        if len(parts) >= 2:
            return base64.b64decode(parts[0]), base64.b64decode(parts[1]), \
                   base64.b64decode(parts[2]) if len(parts) > 2 else None
    raise RuntimeError(f"Cannot parse akey format: {akey[:40]}...")

def get_user_key(server, session, master_key):
    profile = api_get(f"{server.rstrip('/')}/api/accounts/profile", token=session)
    akey = profile.get("key", "")
    if not akey:
        raise RuntimeError("No encrypted user key (akey) in profile")
    iv, ct, mac_val = parse_v2_encrypted(akey)

    mac_key = make_mac_key(master_key)
    if mac_val:
        expected_mac = hmac.new(mac_key, iv + ct, hashlib.sha256).digest()
        if not hmac.compare_digest(expected_mac, mac_val):
            raise RuntimeError("MAC verification failed for akey")

    cipher = Cipher(algorithms.AES(master_key), modes.CBC(iv), backend=default_backend())
    dec = cipher.decryptor()
    plaintext = dec.update(ct) + dec.finalize()

    unpadder = padding.PKCS7(128).unpadder()
    user_key = unpadder.update(plaintext) + unpadder.finalize()

    if len(user_key) != 64:
        raise RuntimeError(f"Unexpected user key length: {len(user_key)}")
    return user_key


# ── Main entry point ──────────────────────────────────────────────────────

def main():
    import urllib.parse
    server = os.environ.get("VW_SERVER", "http://localhost:8080")
    email = os.environ.get("VW_EMAIL", "e2e-test@vaultwarden.local")
    password = os.environ.get("VW_PASSWORD", "test-e2e-password")

    action = sys.argv[1] if len(sys.argv) > 1 else "help"

    if action == "register":
        register_user(server, email, password)
        print(json.dumps({"status": "registered", "email": email}))

    elif action == "login":
        pwd_hash = compute_auth_hash(password, email)
        session = login_get_session(server, email, pwd_hash)
        print(json.dumps({"session": session[:20] + "...", "status": "ok"}))

    elif action == "api-key":
        pwd_hash = compute_auth_hash(password, email)
        session = login_get_session(server, email, pwd_hash)
        api_key, user_id = get_api_key(server, session, pwd_hash)
        client_id = f"user.{user_id}"
        print(json.dumps({"clientId": client_id, "clientSecret": api_key,
                          "userId": user_id, "apiKey": api_key}))

    elif action == "create-items":
        pwd_hash = compute_auth_hash(password, email)
        session = login_get_session(server, email, pwd_hash)
        mk = derive_master_key(password, email)
        user_key = get_user_key(server, session, mk)
        enc_key = user_key[:32]
        mac_key = user_key[32:64]
        namespace = os.environ.get("VW_NAMESPACE", "default")
        items_to_create = int(sys.argv[2]) if len(sys.argv) > 2 else 3
        results = []
        for i in range(1, items_to_create + 1):
            item_name = f"e2e-item-{chr(96 + i)}"
            cipher = create_cipher(
                server, session, enc_key, mac_key,
                name=item_name,
                login_username=f"user-{item_name}",
                login_password=f"pass-{item_name}",
                fields=[
                    {"name": "namespaces", "value": namespace},
                    {"name": "data-key", "value": f"value-{item_name}"},
                ],
                notes=f"E2E test item {i}",
            )
            results.append({"name": item_name, "id": cipher.get("id", "")})
        print(json.dumps({"items": results}))

    elif action == "modify-item":
        item_id = sys.argv[2] if len(sys.argv) > 2 else ""
        if not item_id:
            print(json.dumps({"error": "item_id required"}), file=sys.stderr)
            sys.exit(1)
        pwd_hash = compute_auth_hash(password, email)
        session = login_get_session(server, email, pwd_hash)
        mk = derive_master_key(password, email)
        user_key = get_user_key(server, session, mk)
        enc_key = user_key[:32]
        mac_key = user_key[32:64]

        # Fetch existing cipher
        url = f"{server.rstrip('/')}/api/ciphers/{item_id}"
        existing = api_get(url, token=session)

        modified = False
        for f in existing.get("fields", []):
            if f.get("name") and f["name"].startswith("2."):
                try:
                    f_iv, f_ct, f_mac = parse_v2_encrypted(f["name"])
                    if f_mac:
                        f_mac_check = hmac.new(mac_key, f_iv + f_ct, hashlib.sha256).digest()
                        if not hmac.compare_digest(f_mac_check, f_mac):
                            continue
                    cipher2 = Cipher(algorithms.AES(enc_key), modes.CBC(f_iv), backend=default_backend())
                    dec = cipher2.decryptor()
                    name_plain = dec.update(f_ct) + dec.finalize()
                    unpadder = padding.PKCS7(128).unpadder()
                    plain_name = (unpadder.update(name_plain) + unpadder.finalize()).decode()
                    if plain_name == "data-key":
                        f["value"] = encrypt_cipher_field("modified-value", enc_key, mac_key)
                        modified = True
                        break
                except Exception as e:
                    logging.warning("Failed to decrypt field '%s': %s",
                                    f.get("name", "unknown"), e)

        # Send update
        url = f"{server.rstrip('/')}/api/ciphers/{item_id}"
        result = api_post(url, existing, token=session)
        print(json.dumps({"modified": item_id, "status": "ok", "result": result.get("id", "")}))

    else:
        print(f"Usage: {sys.argv[0]} <register|login|api-key|create-items|modify-item>")
        sys.exit(1)


if __name__ == "__main__":
    main()
