# Clone the template
git clone https://github.com/ai-trader-prod/final-deployment.git
cd final-deployment

# Create your new repo
gh repo create ai-trading-bot-prod --private --push --source .

## LLM access (Perplexity Orchestrator API)

All LLM calls go through `trade_bot.llm.OrchestratorClient`, which sends them to the
[Perplexity Orchestrator API](https://docs.perplexity.ai/docs/orchestrator/quickstart).
You can switch frontier models by changing a `creator/model-name` slug. You don't
change providers or SDKs.

| Schema | Endpoint | SDK / base URL |
| --- | --- | --- |
| `chat` (default) | `POST /orchestrator/v1/chat/completions` | `openai`, `https://api.perplexity.ai/orchestrator/v1` |
| `messages` | `POST /orchestrator/v1/messages` | `anthropic`, `https://api.perplexity.ai/orchestrator` |

### Setup

```bash
uv sync
export PERPLEXITY_API_KEY=...            # create one at https://console.perplexity.ai
uv run python -m trade_bot.llm models    # the models this key can use (its allowlist)
uv run python -m trade_bot.llm smoke     # minimal real request on both schemas
```

Optional environment variables:

- `TRADE_BOT_LLM_MODEL`: a slug from `models`. Defaults to `perplexity/kimi-k3`.
- `TRADE_BOT_LLM_SCHEMA`: `chat` or `messages`.
- `TRADE_BOT_LLM_MAX_TOKENS`, `TRADE_BOT_LLM_MAX_RETRIES`, `TRADE_BOT_LLM_TIMEOUT`.

### Usage

```python
from trade_bot.llm import OrchestratorClient

llm = OrchestratorClient.from_env()
reply = llm.complete("Summarize the risks of a covered-call strategy.", system="Be concise.")
print(reply.text, reply.usage.input_tokens, reply.usage.output_tokens)

for delta in llm.stream("Explain RSI divergence.", schema="messages", model="perplexity/glm-5.3"):
    print(delta, end="")
```

Errors are raised as `trade_bot.llm` exceptions:

| Status | Exception |
| --- | --- |
| 401 | `OrchestratorAuthError` |
| 400 (unlisted model) or 402 (model excluded for your tier) | `ModelNotAvailableError` |
| 429 | `OrchestratorRateLimitError`, with `retry_after` |

On a 429 the SDKs retry first and honor `Retry-After`.

### Development

```bash
uv run ruff check . && uv run ruff format --check . && uv run mypy && uv run pytest
```
