"""Client for the Perplexity Orchestrator API.

The Orchestrator API exposes frontier models from many creators behind OpenAI- and
Anthropic-compatible schemas. This module wraps both official SDKs behind one small
interface so callers pick a model by its ``creator/model-name`` slug and a schema
(``"chat"`` -> ``POST /orchestrator/v1/chat/completions`` via the OpenAI SDK,
``"messages"`` -> ``POST /orchestrator/v1/messages`` via the Anthropic SDK).

Docs: https://docs.perplexity.ai/docs/orchestrator/quickstart
"""

from __future__ import annotations

import logging
from collections.abc import Callable, Iterable, Iterator, Sequence
from dataclasses import dataclass, field
from typing import Any, Literal, TypedDict

import anthropic
import openai

from trade_bot.llm.config import ANTHROPIC_BASE_URL, OPENAI_BASE_URL, LLMConfig, Schema
from trade_bot.llm.errors import (
    IncompleteStreamError,
    ModelNotAvailableError,
    OrchestratorAuthError,
    OrchestratorError,
    OrchestratorRateLimitError,
)

logger = logging.getLogger(__name__)


class Message(TypedDict):
    role: Literal["system", "user", "assistant"]
    content: str


@dataclass(frozen=True)
class Usage:
    """Token usage, normalized across schemas.

    * ``input_tokens``: all prompt tokens, including cache reads and writes. For Chat
      Completions this is ``prompt_tokens``; for Messages it is
      ``input_tokens + cache_creation_input_tokens + cache_read_input_tokens``.
    * ``output_tokens``: ``completion_tokens`` (includes reasoning tokens) or
      ``output_tokens``.
    * ``cache_read_tokens``: ``prompt_tokens_details.cached_tokens`` or
      ``cache_read_input_tokens``.
    * ``cache_write_tokens``: ``cache_creation_input_tokens`` (Messages only).
    * ``raw``: the schema-native usage object as a dict.
    """

    input_tokens: int = 0
    output_tokens: int = 0
    cache_read_tokens: int = 0
    cache_write_tokens: int | None = None
    raw: dict[str, Any] = field(default_factory=dict)

    @property
    def total_tokens(self) -> int:
        return self.input_tokens + self.output_tokens

    @classmethod
    def from_chat(cls, usage: Any) -> Usage:
        if usage is None:
            return cls()
        details = getattr(usage, "prompt_tokens_details", None)
        return cls(
            input_tokens=usage.prompt_tokens or 0,
            output_tokens=usage.completion_tokens or 0,
            cache_read_tokens=(getattr(details, "cached_tokens", None) or 0) if details else 0,
            raw=usage.model_dump(),
        )

    @classmethod
    def from_messages(cls, usage: Any) -> Usage:
        if usage is None:
            return cls()
        cache_write = getattr(usage, "cache_creation_input_tokens", None) or 0
        cache_read = getattr(usage, "cache_read_input_tokens", None) or 0
        return cls(
            input_tokens=(usage.input_tokens or 0) + cache_write + cache_read,
            output_tokens=usage.output_tokens or 0,
            cache_read_tokens=cache_read,
            cache_write_tokens=cache_write,
            raw=usage.model_dump(),
        )


@dataclass(frozen=True)
class Completion:
    text: str
    model: str
    schema: Schema
    usage: Usage
    stop_reason: str | None = None


@dataclass(frozen=True)
class ModelInfo:
    id: str
    owned_by: str | None = None
    pricing: dict[str, Any] | None = None


class CompletionStream:
    """Iterates text deltas; ``usage`` and ``stop_reason`` are set once exhausted.

    Raises :class:`IncompleteStreamError` if the stream ends without its terminal
    event (a ``finish_reason`` for Chat Completions, ``message_stop`` for Messages),
    which the Orchestrator docs say means the output is incomplete.
    """

    def __init__(self, events: Iterator[str], state: _StreamState) -> None:
        self._events = events
        self._state = state

    def __iter__(self) -> Iterator[str]:
        yield from _translate_iter(self._events)
        if not self._state.completed:
            raise IncompleteStreamError("Stream ended before its terminal event; output may be truncated.")

    @property
    def usage(self) -> Usage:
        return self._state.usage

    @property
    def stop_reason(self) -> str | None:
        return self._state.stop_reason

    def text(self) -> str:
        """Consume the whole stream and return the concatenated text."""
        return "".join(self)


@dataclass
class _StreamState:
    usage: Usage = field(default_factory=Usage)
    stop_reason: str | None = None
    completed: bool = False


class OrchestratorClient:
    """One entry point for every LLM call in trade-bot.

    >>> client = OrchestratorClient.from_env()          # doctest: +SKIP
    >>> client.complete("Summarize today's BTC move.")  # doctest: +SKIP
    """

    def __init__(
        self,
        config: LLMConfig,
        *,
        openai_client: openai.OpenAI | None = None,
        anthropic_client: anthropic.Anthropic | None = None,
    ) -> None:
        self.config = config
        self._openai = openai_client
        self._anthropic = anthropic_client

    @classmethod
    def from_env(cls) -> OrchestratorClient:
        return cls(LLMConfig.from_env())

    # -- SDK clients (created lazily so only the schema in use is configured) --

    @property
    def openai(self) -> openai.OpenAI:
        if self._openai is None:
            self._openai = openai.OpenAI(
                api_key=self.config.api_key,
                base_url=OPENAI_BASE_URL,
                max_retries=self.config.max_retries,
                timeout=self.config.timeout,
            )
        return self._openai

    @property
    def anthropic(self) -> anthropic.Anthropic:
        if self._anthropic is None:
            self._anthropic = anthropic.Anthropic(
                api_key=self.config.api_key,
                base_url=ANTHROPIC_BASE_URL,
                max_retries=self.config.max_retries,
                timeout=self.config.timeout,
            )
        return self._anthropic

    # -- Public API --

    def list_models(self) -> list[ModelInfo]:
        """``GET /orchestrator/v1/models``: this key's tier-aware catalog and allowlist."""
        page = _translate(lambda: self.openai.models.list(), model=None)
        return [
            ModelInfo(
                id=m.id,
                owned_by=getattr(m, "owned_by", None),
                pricing=getattr(m, "pricing", None),
            )
            for m in page
        ]

    def ensure_model_available(self, model: str | None = None) -> str:
        """Raise :class:`ModelNotAvailableError` unless ``model`` is in this key's catalog."""
        model = model or self.config.model
        ids = {m.id for m in self.list_models()}
        if model not in ids:
            raise ModelNotAvailableError(
                f"Model {model!r} is not in this API key's Orchestrator catalog. "
                f"Available: {', '.join(sorted(ids)) or '(none)'}"
            )
        return model

    def complete(
        self,
        prompt: str | Sequence[Message],
        *,
        model: str | None = None,
        schema: Schema | None = None,
        system: str | None = None,
        max_tokens: int | None = None,
        temperature: float | None = None,
    ) -> Completion:
        model = model or self.config.model
        schema = schema or self.config.schema
        max_tokens = max_tokens or self.config.max_tokens
        messages = _normalize(prompt, system)

        if schema == "chat":
            chat_kwargs: dict[str, Any] = {"model": model, "max_tokens": max_tokens, "messages": messages}
            if temperature is not None:
                chat_kwargs["temperature"] = temperature
            resp = _translate(lambda: self.openai.chat.completions.create(**chat_kwargs), model=model)
            choice = resp.choices[0]
            return Completion(
                text=choice.message.content or "",
                model=resp.model,
                schema=schema,
                usage=Usage.from_chat(resp.usage),
                stop_reason=choice.finish_reason,
            )

        msg_kwargs = self._messages_kwargs(model, messages, max_tokens, temperature)
        msg = _translate(lambda: self.anthropic.messages.create(**msg_kwargs), model=model)
        return Completion(
            text="".join(b.text for b in msg.content if b.type == "text"),
            model=msg.model,
            schema=schema,
            usage=Usage.from_messages(msg.usage),
            stop_reason=msg.stop_reason,
        )

    def stream(
        self,
        prompt: str | Sequence[Message],
        *,
        model: str | None = None,
        schema: Schema | None = None,
        system: str | None = None,
        max_tokens: int | None = None,
        temperature: float | None = None,
    ) -> CompletionStream:
        model = model or self.config.model
        schema = schema or self.config.schema
        max_tokens = max_tokens or self.config.max_tokens
        messages = _normalize(prompt, system)
        state = _StreamState()

        if schema == "chat":
            chat_kwargs: dict[str, Any] = {
                "model": model,
                "max_tokens": max_tokens,
                "messages": messages,
                "stream": True,
                "stream_options": {"include_usage": True},
            }
            if temperature is not None:
                chat_kwargs["temperature"] = temperature
            chunks = _translate(lambda: self.openai.chat.completions.create(**chat_kwargs), model=model)
            return CompletionStream(_chat_deltas(chunks, state), state)

        msg_kwargs = self._messages_kwargs(model, messages, max_tokens, temperature)
        msg_kwargs["stream"] = True
        events = _translate(lambda: self.anthropic.messages.create(**msg_kwargs), model=model)
        return CompletionStream(_messages_deltas(events, state), state)

    # -- Helpers --

    @staticmethod
    def _messages_kwargs(
        model: str, messages: list[Message], max_tokens: int, temperature: float | None
    ) -> dict[str, Any]:
        # The Messages schema takes system prompts as a top-level `system` field.
        system = "\n\n".join(m["content"] for m in messages if m["role"] == "system")
        kwargs: dict[str, Any] = {
            "model": model,
            "max_tokens": max_tokens,
            "messages": [m for m in messages if m["role"] != "system"],
        }
        if system:
            kwargs["system"] = system
        if temperature is not None:
            kwargs["temperature"] = temperature
        return kwargs


def _normalize(prompt: str | Sequence[Message], system: str | None) -> list[Message]:
    messages: list[Message] = [{"role": "user", "content": prompt}] if isinstance(prompt, str) else list(prompt)
    if system:
        messages.insert(0, {"role": "system", "content": system})
    return messages


def _chat_deltas(chunks: Iterable[Any], state: _StreamState) -> Iterator[str]:
    for chunk in chunks:
        if chunk.usage:  # final chunk from stream_options.include_usage
            state.usage = Usage.from_chat(chunk.usage)
        if not chunk.choices:
            continue
        choice = chunk.choices[0]
        if choice.delta and choice.delta.content:
            yield choice.delta.content
        if choice.finish_reason:
            state.stop_reason = choice.finish_reason
            state.completed = True


def _messages_deltas(events: Iterable[Any], state: _StreamState) -> Iterator[str]:
    start_usage: Any = None
    for event in events:
        if event.type == "message_start":
            start_usage = event.message.usage
            state.usage = Usage.from_messages(start_usage)
        elif event.type == "content_block_delta" and event.delta.type == "text_delta":
            yield event.delta.text
        elif event.type == "message_delta":
            state.stop_reason = event.delta.stop_reason
            # message_delta carries cumulative output usage; input/cache come from message_start.
            base = state.usage
            state.usage = Usage(
                input_tokens=base.input_tokens,
                output_tokens=event.usage.output_tokens or 0,
                cache_read_tokens=base.cache_read_tokens,
                cache_write_tokens=base.cache_write_tokens,
                raw={**base.raw, **event.usage.model_dump(exclude_none=True)},
            )
        elif event.type == "message_stop":
            state.completed = True


def _translate_iter(events: Iterator[str]) -> Iterator[str]:
    """Re-raise SDK errors that surface mid-stream as :class:`OrchestratorError`."""
    try:
        yield from events
    except (openai.APIError, anthropic.APIError) as exc:
        raise _convert(exc, model=None) from exc


def _translate[T](call: Callable[[], T], *, model: str | None) -> T:
    try:
        return call()
    except (openai.APIError, anthropic.APIError) as exc:
        raise _convert(exc, model=model) from exc


def _convert(exc: Exception, *, model: str | None) -> OrchestratorError:
    status: int | None = getattr(exc, "status_code", None)
    headers = getattr(getattr(exc, "response", None), "headers", None) or {}
    detail = str(exc)
    if status == 401:
        return OrchestratorAuthError(
            "Perplexity rejected the API key (401). Check PERPLEXITY_API_KEY; if it may "
            "have been exposed, rotate it at https://console.perplexity.ai.",
            status_code=status,
        )
    if status == 402:
        return ModelNotAvailableError(
            f"Model {model!r} is not available for this organization's usage tier (402).",
            status_code=status,
        )
    if status == 400 and model and model in detail:
        return ModelNotAvailableError(
            f"Model {model!r} is not in this key's Orchestrator catalog (400). "
            "List valid slugs with `python -m trade_bot.llm models`.",
            status_code=status,
        )
    if status == 429:
        retry_after = _parse_retry_after(headers.get("retry-after"))
        logger.warning("Orchestrator API returned 429 after SDK retries (retry_after=%s)", retry_after)
        return OrchestratorRateLimitError(
            "Rate limited or model temporarily overloaded (429); retry after "
            f"{retry_after if retry_after is not None else 'a short delay'} seconds.",
            retry_after=retry_after,
        )
    if status is None:
        return OrchestratorError(f"Could not reach the Orchestrator API: {detail}")
    return OrchestratorError(f"Orchestrator API error ({status}): {detail}", status_code=status)


def _parse_retry_after(value: str | None) -> float | None:
    if not value:
        return None
    try:
        return float(value)
    except ValueError:
        return None
