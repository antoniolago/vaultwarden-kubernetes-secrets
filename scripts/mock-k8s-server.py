#!/usr/bin/env python3
"""
Mock Kubernetes API server for e2e testing.
Stores secrets in memory and responds to the minimum set of K8s API
endpoints needed by the vaultwarden-kubernetes-secrets sync service.
"""
import json
import uuid
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse


class MockK8sHandler(BaseHTTPRequestHandler):
    # In-memory storage: {namespace: {secret_name: secret_data_dict}}
    secrets_store: dict[str, dict[str, dict]] = {}
    # Track created secret names per namespace for SecretExists checks
    secret_names: dict[str, set[str]] = {}

    def _get_body(self) -> dict:
        content_length = int(self.headers.get("Content-Length", "0"))
        if content_length == 0:
            return {}
        raw = self.rfile.read(content_length)
        if raw:
            try:
                return json.loads(raw)
            except json.JSONDecodeError:
                self._send_json({
                    "kind": "Status",
                    "apiVersion": "v1",
                    "metadata": {},
                    "status": "Failure",
                    "message": "Invalid JSON in request body",
                    "reason": "BadRequest",
                    "code": 400,
                }, 400)
                self._parse_error = True
                return {}
        return {}

    def _send_json(self, data: dict, code: int = 200):
        body = json.dumps(data).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _extract_ns_and_secret(self, path: str):
        """Parse /api/v1/namespaces/{ns}/secrets/{name}"""
        parts = [p for p in path.rstrip("/").split("/") if p]
        ns = None
        secret_name = None
        try:
            # Look for .../namespaces/{ns}/secrets/{name}
            for i, p in enumerate(parts):
                if p == "namespaces" and i + 1 < len(parts):
                    ns = parts[i + 1]
                if p == "secrets" and i + 1 < len(parts):
                    secret_name = parts[i + 1]
        except (IndexError, ValueError):
            pass
        return ns, secret_name

    # ---- HTTP Methods ----

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        # /version - K8s client init
        if path == "/version":
            self._send_json({
                "major": "1",
                "minor": "28",
                "gitVersion": "v1.28.0",
                "platform": "linux/amd64"
            })
            return

        # /api, /api/v1 - API discovery
        if path in ("/api", "/api/v1"):
            self._send_json({
                "kind": "APIResourceList",
                "groupVersion": "v1",
                "resources": [
                    {"name": "namespaces", "namespaced": False, "kind": "Namespace",
                     "verbs": ["get", "list", "create"]},
                    {"name": "secrets", "namespaced": True, "kind": "Secret",
                     "verbs": ["get", "list", "create", "update", "delete"]},
                ]
            })
            return

        # /api/v1/namespaces/{ns} - namespace read
        if "/namespaces/" in path and "/secrets" not in path:
            ns, _ = self._extract_ns_and_secret(path)
            if ns:
                self._send_json({
                    "kind": "Namespace",
                    "apiVersion": "v1",
                    "metadata": {"name": ns, "uid": str(uuid.uuid4())},
                    "status": {"phase": "Active"}
                })
                return

        # /api/v1/namespaces/{ns}/secrets - list secrets
        if "/secrets" in path and "/secrets/" not in path:
            ns, _ = self._extract_ns_and_secret(path)
            ns = ns or "default"
            items = []
            if ns in self.secrets_store:
                for sname, sdata in self.secrets_store[ns].items():
                    items.append({
                        "kind": "Secret",
                        "apiVersion": "v1",
                        "metadata": {
                            "name": sname,
                            "namespace": ns,
                            "uid": str(uuid.uuid4()),
                            "annotations": sdata.get("metadata", {}).get("annotations", {}),
                            "labels": sdata.get("metadata", {}).get("labels", {}),
                        },
                        "data": sdata.get("data", {}),
                        "type": sdata.get("type", "Opaque"),
                    })
            self._send_json({
                "kind": "SecretList",
                "apiVersion": "v1",
                "metadata": {},
                "items": items
            })
            return

        # /api/v1/namespaces/{ns}/secrets/{name} - get specific secret
        if "/secrets/" in path:
            ns, secret_name = self._extract_ns_and_secret(path)
            if ns and secret_name:
                if ns in self.secrets_store and secret_name in self.secrets_store[ns]:
                    sdata = self.secrets_store[ns][secret_name]
                    self._send_json({
                        "kind": "Secret",
                        "apiVersion": "v1",
                        "metadata": {
                            "name": secret_name,
                            "namespace": ns,
                            "uid": str(uuid.uuid4()),
                            "annotations": sdata.get("metadata", {}).get("annotations", {}),
                            "labels": sdata.get("metadata", {}).get("labels", {}),
                        },
                        "data": sdata.get("data", {}),
                        "type": sdata.get("type", "Opaque"),
                    })
                    return
                # Secret not found
                self._send_json({
                    "kind": "Status",
                    "apiVersion": "v1",
                    "metadata": {},
                    "status": "Failure",
                    "message": f"secrets \"{secret_name}\" not found",
                    "reason": "NotFound",
                    "code": 404
                }, 404)
                return

        # Fallback
        self._send_json({})

    def do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path
        body = self._get_body()
        if getattr(self, '_parse_error', False):
            return

        # /api/v1/namespaces/{ns}/secrets - create secret
        if "/secrets" in path:
            ns, _ = self._extract_ns_and_secret(path)
            ns = ns or "default"
            name = body.get("metadata", {}).get("name", f"secret-{uuid.uuid4().hex[:8]}")

            if ns not in self.secrets_store:
                self.secrets_store[ns] = {}
                self.secret_names[ns] = set()
            elif name in self.secrets_store[ns]:
                self._send_json({
                    "kind": "Status",
                    "apiVersion": "v1",
                    "metadata": {},
                    "status": "Failure",
                    "message": f"secret \"{name}\" already exists",
                    "reason": "AlreadyExists",
                    "code": 409,
                    "details": {"name": name, "kind": "secrets"},
                }, 409)
                return

            self.secrets_store[ns][name] = {
                "data": body.get("data", {}),
                "type": body.get("type", "Opaque"),
                "metadata": {
                    "annotations": body.get("metadata", {}).get("annotations", {}),
                    "labels": body.get("metadata", {}).get("labels", {}),
                },
            }
            self.secret_names[ns].add(name)

            self._send_json({
                "kind": "Secret",
                "apiVersion": "v1",
                "metadata": {
                    "name": name,
                    "namespace": ns,
                    "uid": str(uuid.uuid4()),
                },
            }, 201)
            return

        self._send_json({}, 201)

    def do_PUT(self):
        parsed = urlparse(self.path)
        path = parsed.path
        body = self._get_body()
        if getattr(self, '_parse_error', False):
            return

        # /api/v1/namespaces/{ns}/secrets/{name} - update secret
        if "/secrets/" in path:
            ns, secret_name = self._extract_ns_and_secret(path)
            if ns and secret_name:
                if ns not in self.secrets_store or secret_name not in self.secrets_store.get(ns, {}):
                    self._send_json({
                        "kind": "Status",
                        "apiVersion": "v1",
                        "metadata": {},
                        "status": "Failure",
                        "message": f"secrets \"{secret_name}\" not found",
                        "reason": "NotFound",
                        "code": 404,
                        "details": {"name": secret_name, "kind": "secrets"},
                    }, 404)
                    return
                if ns not in self.secrets_store:
                    self.secrets_store[ns] = {}
                    self.secret_names[ns] = set()
                self.secrets_store[ns][secret_name] = {
                    "data": body.get("data", {}),
                    "type": body.get("type", "Opaque"),
                    "metadata": {
                        "annotations": body.get("metadata", {}).get("annotations", {}),
                        "labels": body.get("metadata", {}).get("labels", {}),
                    },
                }
                self.secret_names[ns].add(secret_name)
                self._send_json({
                    "kind": "Secret",
                    "apiVersion": "v1",
                    "metadata": {"name": secret_name, "namespace": ns},
                })
                return

        self._send_json({})

    def do_DELETE(self):
        parsed = urlparse(self.path)
        path = parsed.path

        # /api/v1/namespaces/{ns}/secrets/{name} - delete secret
        if "/secrets/" in path:
            ns, secret_name = self._extract_ns_and_secret(path)
            if ns and secret_name and ns in self.secrets_store:
                self.secrets_store[ns].pop(secret_name, None)
                self.secret_names.get(ns, set()).discard(secret_name)
            self._send_json({
                "kind": "Status",
                "apiVersion": "v1",
                "metadata": {},
                "status": "Success",
            })
            return

        self._send_json({"kind": "Status", "status": "Success"})

    def log_message(self, fmt: str, *args):
        pass  # Suppress HTTP log output


if __name__ == "__main__":
    import os
    port = int(os.environ.get("MOCK_K8S_PORT", "16443"))
    server = HTTPServer(("0.0.0.0", port), MockK8sHandler)
    server.serve_forever()
