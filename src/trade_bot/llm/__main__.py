"""Command-line access to the Orchestrator client.

    python -m trade_bot.llm models
    python -m trade_bot.llm ask "Summarize the risks of a covered-call strategy" --model perplexity/glm-5.3
    python -m trade_bot.llm smoke

The API key is read from PERPLEXITY_API_KEY and is never printed.
"""

from __future__ import annotations

import argparse
import sys
from collections.abc import Sequence

from trade_bot.llm.config import LLMConfig, Schema
from trade_bot.llm.errors import OrchestratorError
from trade_bot.llm.orchestrator import OrchestratorClient


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="python -m trade_bot.llm")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("models", help="List models this API key can use (GET /models)")

    ask = sub.add_parser("ask", help="Send one prompt and print the reply")
    ask.add_argument("prompt")
    ask.add_argument("--model", help="creator/model-name slug (default: TRADE_BOT_LLM_MODEL)")
    ask.add_argument("--schema", choices=["chat", "messages"])
    ask.add_argument("--system")
    ask.add_argument("--max-tokens", type=int)
    ask.add_argument("--stream", action="store_true")

    smoke = sub.add_parser("smoke", help="Minimal real request; prints response shape only")
    smoke.add_argument("--model")
    smoke.add_argument("--schema", choices=["chat", "messages", "both"], default="both")

    args = parser.parse_args(argv)
    try:
        client = OrchestratorClient(LLMConfig.from_env())
        if args.command == "models":
            for m in client.list_models():
                print(m.id, m.pricing or "")
        elif args.command == "ask":
            kwargs = dict(model=args.model, schema=args.schema, system=args.system, max_tokens=args.max_tokens)
            if args.stream:
                stream = client.stream(args.prompt, **kwargs)
                for delta in stream:
                    print(delta, end="", flush=True)
                print(f"\n[usage] {stream.usage.input_tokens} in / {stream.usage.output_tokens} out", file=sys.stderr)
            else:
                result = client.complete(args.prompt, **kwargs)
                print(result.text)
                print(
                    f"[{result.model}] {result.usage.input_tokens} in / {result.usage.output_tokens} out",
                    file=sys.stderr,
                )
        elif args.command == "smoke":
            model = client.ensure_model_available(args.model)
            print(f"GET /models: 200, {model!r} is in the catalog")
            schemas: list[Schema] = ["chat", "messages"] if args.schema == "both" else [args.schema]
            for schema in schemas:
                r = client.complete("Reply with the single word: ok", model=model, schema=schema, max_tokens=16)
                print(
                    f"{schema}: 200 model={r.model!r} text_len={len(r.text)} stop={r.stop_reason!r} "
                    f"usage(in={r.usage.input_tokens}, out={r.usage.output_tokens})"
                )
    except OrchestratorError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
