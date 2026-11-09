"""Offline tests for the Orchestrator client using a mocked HTTP transport."""

from __future__ import annotations

import json
from collections.abc import Callable
from typing import Any

import anthropic
import httpx2
import openai
import pytest

from trade_bot.llm import (
    IncompleteStreamError,
    LLMConfig,
    MissingAPIKeyError,
    ModelNotAvailableError,
    OrchestratorAuthError,
    OrchestratorClient,
    OrchestratorRateLimitError,
)

Handler = Callable[[httpx2.Request], httpx2.Response]
FAKE_KEY = "test-key-not-real"


def make_client(handler: Handler, **config: Any) -> tuple[OrchestratorClient, list[httpx2.Request]]:
    seen: list[httpx2.Request] = []

    def record(request: httpx2.Request) -> httpx2.Response:
        seen.append(request)
        return handler(request)

    http = httpx2.Client(transport=httpx2.MockTransport(record))
    cfg = LLMConfig(api_key=FAKE_KEY, max_retries=0, **config)
    client = OrchestratorClient(
        cfg,
        openai_client=openai.OpenAI(
            api_key=FAKE_KEY, base_url="https://api.perplexity.ai/orchestrator/v1", http_client=http, max_retries=0
        ),
        anthropic_client=anthropic.Anthropic(
            api_key=FAKE_KEY, base_url="https://api.perplexity.ai/orchestrator", http_client=http, max_retries=0
        ),
    )
    return client, seen


def sse(events: list[tuple[str | None, Any]]) -> bytes:
    out = []
    for name, data in events:
        prefix = f"event: {name}\n" if name else ""
        payload = data if isinstance(data, str) else json.dumps(data)
        out.append(f"{prefix}data: {payload}\n\n")
    return "".join(out).encode()


CHAT_RESPONSE = {
    "id": "chatcmpl-1",
    "object": "chat.completion",
    "created": 1753747200,
    "model": "perplexity/kimi-k3",
    "choices": [
        {"index": 0, "message": {"role": "assistant", "content": "ok"}, "logprobs": None, "finish_reason": "stop"}
    ],
    "usage": {
        "prompt_tokens": 18,
        "completion_tokens": 2,
        "total_tokens": 20,
        "prompt_tokens_details": {"cached_tokens": 5},
    },
}

MESSAGES_RESPONSE = {
    "id": "msg_1",
    "type": "message",
    "role": "assistant",
    "model": "perplexity/kimi-k3",
    "content": [{"type": "text", "text": "ok"}],
    "stop_reason": "end_turn",
    "stop_sequence": None,
    "usage": {"input_tokens": 10, "output_tokens": 2, "cache_creation_input_tokens": 3, "cache_read_input_tokens": 4},
}


def test_chat_completion_uses_orchestrator_chat_endpoint() -> None:
    client, seen = make_client(lambda r: httpx2.Response(200, json=CHAT_RESPONSE))
    result = client.complete("hi", system="be brief")

    req = seen[0]
    assert req.method == "POST"
    assert str(req.url) == "https://api.perplexity.ai/orchestrator/v1/chat/completions"
    assert req.headers["authorization"] == f"Bearer {FAKE_KEY}"
    body = json.loads(req.content)
    assert body["model"] == "perplexity/kimi-k3"
    assert body["messages"][0] == {"role": "system", "content": "be brief"}
    assert result.text == "ok"
    assert result.stop_reason == "stop"
    assert (result.usage.input_tokens, result.usage.output_tokens, result.usage.cache_read_tokens) == (18, 2, 5)


def test_messages_completion_uses_orchestrator_messages_endpoint() -> None:
    client, seen = make_client(lambda r: httpx2.Response(200, json=MESSAGES_RESPONSE), schema="messages")
    result = client.complete("hi", system="be brief", model="perplexity/glm-5.3")

    req = seen[0]
    assert str(req.url) == "https://api.perplexity.ai/orchestrator/v1/messages"
    body = json.loads(req.content)
    assert body["model"] == "perplexity/glm-5.3"
    assert body["system"] == "be brief"
    assert all(m["role"] != "system" for m in body["messages"])
    assert result.text == "ok"
    assert result.usage.input_tokens == 10 + 3 + 4
    assert result.usage.cache_read_tokens == 4
    assert result.usage.cache_write_tokens == 3


def test_list_models_reads_catalog_with_pricing() -> None:
    catalog = {
        "object": "list",
        "data": [
            {
                "id": "perplexity/kimi-k3",
                "object": "model",
                "created": 0,
                "owned_by": "perplexity",
                "pricing": {"input": 3, "output": 15, "cache_read": 0.3, "unit": "usd_per_1m_tokens"},
            }
        ],
    }
    client, seen = make_client(lambda r: httpx2.Response(200, json=catalog))
    models = client.list_models()
    assert str(seen[0].url) == "https://api.perplexity.ai/orchestrator/v1/models"
    assert models[0].id == "perplexity/kimi-k3"
    assert models[0].pricing == catalog["data"][0]["pricing"]  # type: ignore[index]
    assert client.ensure_model_available() == "perplexity/kimi-k3"
    with pytest.raises(ModelNotAvailableError):
        client.ensure_model_available("someone/not-listed")


@pytest.mark.parametrize(
    ("status", "headers", "body", "expected"),
    [
        (401, {}, {"error": {"message": "invalid key"}}, OrchestratorAuthError),
        (402, {}, {"error": {"message": "tier"}}, ModelNotAvailableError),
        (400, {}, {"error": {"message": "invalid model: perplexity/kimi-k3"}}, ModelNotAvailableError),
        (429, {"retry-after": "7"}, {"error": {"message": "overloaded"}}, OrchestratorRateLimitError),
    ],
)
def test_http_errors_are_translated(status: int, headers: dict[str, str], body: Any, expected: type) -> None:
    for schema in ("chat", "messages"):
        client, _ = make_client(lambda r: httpx2.Response(status, headers=headers, json=body), schema=schema)
        with pytest.raises(expected) as info:
            client.complete("hi")
        assert FAKE_KEY not in str(info.value)
        if status == 429:
            assert isinstance(info.value, OrchestratorRateLimitError)
            assert info.value.retry_after == 7.0


def test_chat_stream_collects_text_and_final_usage() -> None:
    chunk = {"id": "c", "object": "chat.completion.chunk", "created": 0, "model": "perplexity/kimi-k3"}
    body = sse(
        [
            (
                None,
                {
                    **chunk,
                    "choices": [{"index": 0, "delta": {"role": "assistant", "content": "o"}, "finish_reason": None}],
                },
            ),
            (None, {**chunk, "choices": [{"index": 0, "delta": {"content": "k"}, "finish_reason": "stop"}]}),
            (None, {**chunk, "choices": [], "usage": {"prompt_tokens": 9, "completion_tokens": 2, "total_tokens": 11}}),
            (None, "[DONE]"),
        ]
    )
    client, seen = make_client(
        lambda r: httpx2.Response(200, headers={"content-type": "text/event-stream"}, content=body)
    )
    stream = client.stream("hi")
    assert stream.text() == "ok"
    sent = json.loads(seen[0].content)
    assert sent["stream"] is True and sent["stream_options"] == {"include_usage": True}
    assert (stream.usage.input_tokens, stream.usage.output_tokens) == (9, 2)
    assert stream.stop_reason == "stop"


def messages_events(include_stop: bool) -> bytes:
    events: list[tuple[str | None, Any]] = [
        (
            "message_start",
            {
                "type": "message_start",
                "message": {
                    **MESSAGES_RESPONSE,
                    "content": [],
                    "stop_reason": None,
                    "usage": {"input_tokens": 10, "output_tokens": 1},
                },
            },
        ),
        (
            "content_block_start",
            {"type": "content_block_start", "index": 0, "content_block": {"type": "text", "text": ""}},
        ),
        (
            "content_block_delta",
            {"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": "ok"}},
        ),
        ("content_block_stop", {"type": "content_block_stop", "index": 0}),
        (
            "message_delta",
            {
                "type": "message_delta",
                "delta": {"stop_reason": "end_turn", "stop_sequence": None},
                "usage": {"output_tokens": 2},
            },
        ),
    ]
    if include_stop:
        events.append(("message_stop", {"type": "message_stop"}))
    return sse(events)


def test_messages_stream_collects_text_and_usage() -> None:
    client, seen = make_client(
        lambda r: httpx2.Response(200, headers={"content-type": "text/event-stream"}, content=messages_events(True)),
        schema="messages",
    )
    stream = client.stream("hi")
    assert stream.text() == "ok"
    assert json.loads(seen[0].content)["stream"] is True
    assert (stream.usage.input_tokens, stream.usage.output_tokens) == (10, 2)
    assert stream.stop_reason == "end_turn"


def test_unterminated_stream_raises() -> None:
    client, _ = make_client(
        lambda r: httpx2.Response(200, headers={"content-type": "text/event-stream"}, content=messages_events(False)),
        schema="messages",
    )
    with pytest.raises(IncompleteStreamError):
        client.stream("hi").text()


def test_config_requires_api_key_and_hides_it() -> None:
    with pytest.raises(MissingAPIKeyError, match="console.perplexity.ai"):
        LLMConfig.from_env({})
    cfg = LLMConfig.from_env({"PERPLEXITY_API_KEY": FAKE_KEY, "TRADE_BOT_LLM_MODEL": "perplexity/glm-5.3"})
    assert cfg.model == "perplexity/glm-5.3"
    assert FAKE_KEY not in repr(cfg)
    with pytest.raises(ValueError):
        LLMConfig.from_env({"PERPLEXITY_API_KEY": FAKE_KEY, "TRADE_BOT_LLM_SCHEMA": "responses"})
