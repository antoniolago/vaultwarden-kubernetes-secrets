#!/usr/bin/env python3
"""
Register a user in vaultwarden (1.35+) via the /identity/accounts/register endpoint.

Requires generating encryption keys (RSA 2048 + AES-256) and encrypting them
using the Bitwarden protocol.
"""
import hashlib
import hmac as hmac_lib
import base64
import json
import os
import sys
import urllib.request

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import padding, serialization, hashes
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.backends import default_backend


def pbkdf2_sha256(password, salt, iterations, dklen=32):
    return hashlib.pbkdf2_hmac('sha256', password, salt, iterations, dklen)


def hkdf_expand(prk, info, length):
    """HKDF-Expand: HMAC-SHA256(prk, info || 0x01) for length <= 32."""
    h = hmac_lib.new(prk, info + b'\x01', hashlib.sha256)
    return h.digest()[:length]


def aes_cbc_encrypt(plaintext, key, iv):
    """AES-256-CBC encrypt with PKCS7 padding."""
    padder = padding.PKCS7(128).padder()
    padded_data = padder.update(plaintext) + padder.finalize()
    cipher = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend())
    encryptor = cipher.encryptor()
    return encryptor.update(padded_data) + encryptor.finalize()


def encrypt_bitwarden(plaintext, enc_key, mac_key):
    """
    Encrypt data using Bitwarden format: base64(iv).base64(ciphertext).base64(mac)
    """
    iv = os.urandom(16)
    ciphertext = aes_cbc_encrypt(plaintext, enc_key, iv)
    mac = hmac_lib.new(mac_key, iv + ciphertext, hashlib.sha256).digest()
    parts = [
        base64.b64encode(iv).decode(),
        base64.b64encode(ciphertext).decode(),
        base64.b64encode(mac).decode(),
    ]
    return ".".join(parts)


def make_mac_key(enc_key):
    """Derive MAC key from a 32-byte encryption key."""
    return hkdf_expand(enc_key, b"mac", 32)


def register(args):
    email = args["email"]
    password = args["password"]
    server = args["server"].rstrip("/")
    iterations = args.get("iterations", 600000)

    print(f"Registering {email} on {server} (KDF: PBKDF2, {iterations} iterations)")

    # Step 1: Derive master key from password + email
    master_key = pbkdf2_sha256(
        password.encode(),
        email.lower().encode(),
        iterations
    )

    # Step 2: Compute master password hash (HKDF-Expand)
    master_password_hash = base64.b64encode(
        hkdf_expand(master_key, b"auth", 32)
    ).decode()
    print(f"  masterPasswordHash: {master_password_hash[:20]}...")

    # Step 3: Generate 64-byte user symmetric key
    user_key = os.urandom(64)
    enc_key_user = user_key[:32]
    mac_key_user = user_key[32:64]

    # Step 4: Encrypt user key with master key
    mac_key_master = make_mac_key(master_key)
    encrypted_user_key = encrypt_bitwarden(user_key, master_key, mac_key_master)
    print(f"  encryptedUserKey: {encrypted_user_key[:30]}...")

    # Step 5: Generate RSA 2048 key pair
    private_key_obj = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
        backend=default_backend()
    )
    public_key_obj = private_key_obj.public_key()

    # Step 6: Serialize keys to DER format
    private_key_der = private_key_obj.private_bytes(
        encoding=serialization.Encoding.DER,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption()
    )
    public_key_der = public_key_obj.public_bytes(
        encoding=serialization.Encoding.DER,
        format=serialization.PublicFormat.SubjectPublicKeyInfo
    )

    # Step 7: Encrypt private key with user key
    encrypted_private_key = encrypt_bitwarden(
        private_key_der, enc_key_user, mac_key_user
    )

    # Public key is base64 encoded DER
    public_key_b64 = base64.b64encode(public_key_der).decode()

    # Step 8: Build registration payload
    payload = {
        "email": email,
        "masterPasswordHash": master_password_hash,
        "masterPasswordHint": "",
        "name": "E2E Test User",
        "kdf": 0,  # PBKDF2
        "kdfIterations": iterations,
        "key": encrypted_user_key,
        "keys": {
            "encryptedPrivateKey": encrypted_private_key,
            "publicKey": public_key_b64,
        },
    }

    # Step 9: Send registration request
    url = f"{server}/identity/accounts/register"
    data = json.dumps(payload).encode()

    req = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST"
    )

    try:
        with urllib.request.urlopen(req) as resp:
            result = json.loads(resp.read().decode())
            print(f"  ✓ Registration successful: {result}")
            return 0
    except urllib.error.HTTPError as e:
        print(f"  ✗ HTTP {e.code}: {e.read().decode()[:500]}", file=sys.stderr)
        return 1
    except Exception as e:
        print(f"  ✗ Error: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Register user in vaultwarden")
    parser.add_argument("--server", default="http://localhost:8080")
    parser.add_argument("--email", default="e2e-test@vaultwarden.local")
    parser.add_argument("--password", default="test-e2e-password")
    parser.add_argument("--iterations", type=int, default=600000)
    args = parser.parse_args()

    sys.exit(register(vars(args)))
