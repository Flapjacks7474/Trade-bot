"""Configuration for the Perplexity Orchestrator API, resolved from the environment."""

from __future__ import annotations

import os
from collections.abc import Mapping
from dataclasses import dataclass, field
from typing import Literal

from trade_bot.llm.errors import MissingAPIKeyError

Schema = Literal["chat", "messages"]

# The OpenAI SDK appends `/chat/completions` (and `/models`) to this base URL.
OPENAI_BASE_URL = "https://api.perplexity.ai/orchestrator/v1"
# The Anthropic SDK appends `/v1/messages`, so it is configured without `/v1`.
ANTHROPIC_BASE_URL = "https://api.perplexity.ai/orchestrator"

# A model id documented in the Orchestrator quickstart and catalog. Override it with
# TRADE_BOT_LLM_MODEL using a slug returned by `GET /orchestrator/v1/models` for your key.
DEFAULT_MODEL = "perplexity/kimi-k3"

API_KEY_ENV = "PERPLEXITY_API_KEY"
MODEL_ENV = "TRADE_BOT_LLM_MODEL"
SCHEMA_ENV = "TRADE_BOT_LLM_SCHEMA"
MAX_TOKENS_ENV = "TRADE_BOT_LLM_MAX_TOKENS"
MAX_RETRIES_ENV = "TRADE_BOT_LLM_MAX_RETRIES"
TIMEOUT_ENV = "TRADE_BOT_LLM_TIMEOUT"

_SCHEMAS: tuple[Schema, ...] = ("chat", "messages")


@dataclass(frozen=True)
class LLMConfig:
    """Settings for :class:`~trade_bot.llm.OrchestratorClient`.

    ``api_key`` is excluded from ``repr`` so it never ends up in logs or tracebacks.
    """

    api_key: str = field(repr=False)
    model: str = DEFAULT_MODEL
    schema: Schema = "chat"
    max_tokens: int = 1024
    # Both SDKs retry 429/5xx responses and honor the `Retry-After` header.
    max_retries: int = 2
    timeout: float = 60.0

    @classmethod
    def from_env(cls, env: Mapping[str, str] | None = None) -> LLMConfig:
        env = os.environ if env is None else env
        api_key = env.get(API_KEY_ENV, "").strip()
        if not api_key:
            raise MissingAPIKeyError(
                f"{API_KEY_ENV} is not set. Create an API key in the Perplexity API "
                f"Console (https://console.perplexity.ai) and export it in your shell: "
                f"export {API_KEY_ENV}=..."
            )
        schema = env.get(SCHEMA_ENV, "chat").strip().lower()
        if schema not in _SCHEMAS:
            raise ValueError(f"{SCHEMA_ENV} must be one of {_SCHEMAS}, got {schema!r}")
        return cls(
            api_key=api_key,
            model=env.get(MODEL_ENV, "").strip() or DEFAULT_MODEL,
            schema=schema,  # validated above
            max_tokens=int(env.get(MAX_TOKENS_ENV, "1024")),
            max_retries=int(env.get(MAX_RETRIES_ENV, "2")),
            timeout=float(env.get(TIMEOUT_ENV, "60")),
        )
