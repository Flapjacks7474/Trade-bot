"""LLM access for trade-bot, routed through the Perplexity Orchestrator API.

Every LLM call in the project should go through :class:`OrchestratorClient` so the
underlying frontier model can be swapped by changing a ``creator/model-name`` slug
(see ``GET /orchestrator/v1/models``) instead of changing providers or SDKs.
"""

from trade_bot.llm.config import LLMConfig, Schema
from trade_bot.llm.errors import (
    IncompleteStreamError,
    MissingAPIKeyError,
    ModelNotAvailableError,
    OrchestratorAuthError,
    OrchestratorError,
    OrchestratorRateLimitError,
)
from trade_bot.llm.orchestrator import (
    Completion,
    CompletionStream,
    Message,
    ModelInfo,
    OrchestratorClient,
    Usage,
)

__all__ = [
    "Completion",
    "CompletionStream",
    "IncompleteStreamError",
    "LLMConfig",
    "Message",
    "MissingAPIKeyError",
    "ModelInfo",
    "ModelNotAvailableError",
    "OrchestratorAuthError",
    "OrchestratorClient",
    "OrchestratorError",
    "OrchestratorRateLimitError",
    "Schema",
    "Usage",
]
