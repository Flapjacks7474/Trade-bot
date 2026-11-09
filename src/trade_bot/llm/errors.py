"""Errors raised by the Orchestrator client, independent of the SDK used underneath."""

from __future__ import annotations


class OrchestratorError(Exception):
    """Base class for Perplexity Orchestrator API failures."""

    def __init__(self, message: str, *, status_code: int | None = None) -> None:
        super().__init__(message)
        self.status_code = status_code


class MissingAPIKeyError(OrchestratorError):
    """PERPLEXITY_API_KEY is not set."""


class OrchestratorAuthError(OrchestratorError):
    """401: the API key is missing, invalid, or revoked."""


class ModelNotAvailableError(OrchestratorError):
    """400 for a model slug not in the key's catalog, or 402 for a tier-excluded model."""


class OrchestratorRateLimitError(OrchestratorError):
    """429: rate limited or the model is temporarily overloaded.

    ``retry_after`` is the server's ``Retry-After`` value in seconds, when provided.
    The SDKs already retried (honoring ``Retry-After``) before this was raised.
    """

    def __init__(self, message: str, *, status_code: int | None = 429, retry_after: float | None = None) -> None:
        super().__init__(message, status_code=status_code)
        self.retry_after = retry_after


class IncompleteStreamError(OrchestratorError):
    """A stream ended without its terminal event, so the output may be truncated."""
