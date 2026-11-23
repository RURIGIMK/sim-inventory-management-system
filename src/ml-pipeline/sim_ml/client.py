"""Thin client for the API's integration endpoints."""
from __future__ import annotations

import base64
import hashlib
import time
from typing import Any

import requests

TOKEN_HEADER = "X-Integration-Token"


class ApiClient:
    def __init__(self, base_url: str, token: str, timeout: float = 120.0):
        if not base_url.startswith(("https://", "http://localhost", "http://127.0.0.1")):
            raise ValueError("The API base URL must use HTTPS (plain HTTP is allowed only for localhost).")
        if not token or len(token) < 32:
            raise ValueError("The integration token is missing or shorter than 32 characters.")
        self.base = base_url.rstrip("/")
        self.session = requests.Session()
        self.session.headers[TOKEN_HEADER] = token
        self.timeout = timeout

    def wait_until_healthy(self, attempts: int = 60, delay: float = 10.0) -> None:
        """Render's free tier sleeps when idle; the first request can take a minute or more."""
        for attempt in range(1, attempts + 1):
            try:
                response = requests.get(f"{self.base}/api/health", timeout=30)
                if response.status_code == 200:
                    return
            except requests.RequestException:
                pass
            print(f"API not ready (attempt {attempt}/{attempts}); waiting {delay:.0f}s")
            time.sleep(delay)
        raise RuntimeError("The API did not become healthy.")

    def _request(self, method: str, path: str, **kwargs: Any) -> Any:
        response = self.session.request(method, f"{self.base}{path}", timeout=self.timeout, **kwargs)
        if response.status_code >= 400:
            raise RuntimeError(f"{method} {path} failed with HTTP {response.status_code}: {response.text[:500]}")
        return response.json() if response.content else None

    def dataset(self) -> dict:
        return self._request("GET", "/api/integrations/ml/dataset")

    def models(self) -> list[dict]:
        return self._request("GET", "/api/integrations/ml/models")

    def artifact(self, model_id: int) -> tuple[dict, bytes]:
        body = self._request("GET", f"/api/integrations/ml/models/{model_id}/artifact")
        artifact = base64.b64decode(body["artifactBase64"])
        digest = hashlib.sha256(artifact).hexdigest()
        if digest != body["artifactSha256"].lower():
            raise RuntimeError(f"Artifact checksum mismatch for model {body['version']}.")
        return body, artifact

    def register(self, payload: dict) -> dict:
        return self._request("POST", "/api/integrations/ml/models", json=payload)

    def publish_scores(self, model_version_id: int, scores: list[dict], batch_size: int = 5000) -> int:
        published = 0
        for start in range(0, len(scores), batch_size):
            body = self._request("POST", "/api/integrations/ml/scores",
                                 json={"modelVersionId": model_version_id, "scores": scores[start:start + batch_size]})
            published += body["published"]
        return published
