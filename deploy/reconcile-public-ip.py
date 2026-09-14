#!/usr/bin/env python3
"""Reconcile only War3's IP routes/certificate through the local Caddy admin API.

Runs as the deployment user. Private key/config snapshots never leave the server.
An ETag protects unrelated concurrent edits; unchanged configurations are not reloaded.
"""
import copy
import fcntl
import json
import ipaddress
import os
from pathlib import Path
import re
import subprocess
import tempfile
import urllib.error
import urllib.request

def public_ip():
    value = os.environ.get("WAR3CONNECT_PUBLIC_IP", "")
    try:
        address = ipaddress.IPv4Address(value)
    except ipaddress.AddressValueError:
        raise SystemExit("Set WAR3CONNECT_PUBLIC_IP to the deployment IPv4 address") from None
    if not address.is_global:
        raise SystemExit("WAR3CONNECT_PUBLIC_IP must be a public IPv4 address")
    return str(address)

BASE = Path.home() / "apps/war3connect"
TLS = BASE / "tls"
ADMIN = "http://127.0.0.1:2019/config/"
os.umask(0o077)


def main():
    IP = public_ip()
    with (TLS / "reconcile.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        with tempfile.TemporaryDirectory(dir=TLS) as scratch:
            headers, body = Path(scratch) / "headers", Path(scratch) / "body"
            subprocess.run(["curl", "--fail", "--silent", "--show-error", "--max-time", "10",
                            "-D", str(headers), "-o", str(body), ADMIN], check=True)
            etags = re.findall(r"^etag:\s*(.+)$", headers.read_text(), re.I | re.M)
            if not etags:
                raise RuntimeError("Caddy did not provide an ETag; refusing unguarded update")
            current = json.loads(body.read_text())
            baseline = TLS / "caddy-before-war3-public-ip.json"
            if not baseline.exists():
                with baseline.open("x") as backup:
                    json.dump(current, backup)
            config = copy.deepcopy(current)
            apps = config.setdefault("apps", {})
            servers = apps.setdefault("http", {}).setdefault("servers", {})
            if "srv0" not in servers or ":443" not in servers["srv0"].get("listen", []):
                raise RuntimeError("Expected existing HTTPS server not found")
            challenge = {
                "@id": "war3connect-acme-server",
                "listen": [":80"],
                "routes": [
                    {"match": [{"host": [IP], "path": ["/.well-known/acme-challenge/*"]}],
                     "handle": [{"handler": "reverse_proxy", "upstreams": [{"dial": "127.0.0.1:5081"}]}], "terminal": True},
                    {"match": [{"host": [IP]}], "handle": [{"handler": "static_response", "status_code": 308,
                     "headers": {"Location": ["https://" + IP + "{http.request.uri}"]}}], "terminal": True}
                ]
            }
            existing = servers.get("war3connect_acme")
            if existing and existing.get("@id") != "war3connect-acme-server":
                raise RuntimeError("Refusing to overwrite an unrelated HTTP server")
            servers["war3connect_acme"] = challenge
            live = TLS / "letsencrypt/live/war3connect-ip"
            if (live / "fullchain.pem").exists() and (live / "privkey.pem").exists():
                certificate = {"certificate": (live / "fullchain.pem").read_text(),
                               "key": (live / "privkey.pem").read_text(), "tags": ["war3connect-ip"]}
                certificates = apps.setdefault("tls", {}).setdefault("certificates", {})
                certificates["load_pem"] = [c for c in certificates.get("load_pem", [])
                                            if "war3connect-ip" not in c.get("tags", [])] + [certificate]
                server = servers["srv0"]
                routes = server.setdefault("routes", [])
                server["routes"] = [r for r in routes if r.get("@id") != "war3connect-ip-route"] + [{
                    "@id": "war3connect-ip-route", "match": [{"host": [IP]}],
                    "handle": [{"handler": "reverse_proxy", "upstreams": [{"dial": "127.0.0.1:5080"}]}],
                    "terminal": True}]
                # IP clients often omit SNI. Named hosts still select their own certificates.
                policies = server.setdefault("tls_connection_policies", [])
                if not policies:
                    policies.append({"@id": "war3connect-default-sni", "default_sni": IP})
                elif not any(p.get("default_sni") == IP for p in policies):
                    raise RuntimeError("Existing TLS policies require manual review")
                skip = server.setdefault("automatic_https", {}).setdefault("skip_certificates", [])
                if IP not in skip:
                    skip.append(IP)
            if config == current:
                return
            request = urllib.request.Request(ADMIN, json.dumps(config).encode(), method="POST",
                                             headers={"Content-Type": "application/json", "If-Match": etags[-1].strip()})
            try:
                with urllib.request.urlopen(request, timeout=20) as response:
                    if response.status != 200:
                        raise RuntimeError(f"Caddy rejected update: HTTP {response.status}")
            except urllib.error.HTTPError as error:
                # Do not print bodies that might contain private configuration.
                raise RuntimeError(f"Caddy rejected update: HTTP {error.code}") from None
            print("War3 IP routes/certificate synchronized; existing routes preserved.")


if __name__ == "__main__":
    main()
